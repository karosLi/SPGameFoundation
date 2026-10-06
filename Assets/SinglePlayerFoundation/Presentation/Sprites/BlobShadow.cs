using System;
using Unity.Mathematics;
using UnityEngine;

namespace SPF.Presentation.Sprites
{
    /// <summary>Reusable contact-shadow footprint in world units, independent of animation and gameplay.</summary>
    public struct BlobShadowProfile
    {
        public float2 Size;
        public float2 Offset;
        public float4 Color;
        /// <summary>Alpha loss per world-unit height; at 1 / HeightFade the shadow disappears.</summary>
        public float HeightFade;
        /// <summary>Fractional footprint growth per world-unit height.</summary>
        public float HeightSpread;

        public static BlobShadowProfile Default => new BlobShadowProfile
        {
            Size = new float2(0.8f, 0.3f), Offset = new float2(0f, -0.02f),
            Color = new float4(0f, 0f, 0f, 0.3f), HeightFade = 0.25f, HeightSpread = 0.15f,
        };
    }

    /// <summary>
    /// A soft footprint drawn through the normal sprite batch on either render tier. This is an artistic
    /// contact cue, not a shadow map, occlusion query or lighting/physics authority. Use a translucent batch
    /// and explicitly choose scene depth; it does not cast shadows onto walls or other characters.
    /// </summary>
    public static class BlobShadow
    {
        /// <summary>Loading-time white elliptical mask. Tint/opacity come from the profile at draw time.</summary>
        public static PixelCanvas CreateCanvas(int width = 64, int height = 32)
        {
            if (width < 2 || height < 2) throw new ArgumentOutOfRangeException(nameof(width), "Shadow dimensions must be at least two pixels.");
            var canvas = new PixelCanvas(width, height);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float2 p = new float2((x + 0.5f) / width * 2f - 1f, (y + 0.5f) / height * 2f - 1f);
                float a = math.saturate(1f - math.lengthsq(p));
                a = a * a * (3f - 2f * a);
                canvas.Pixels[y * width + x] = new Color32(255, 255, 255, (byte)(a * 255f + 0.5f));
            }
            return canvas;
        }

        /// <summary>Allocation-free append. Returns false when invisible, zero-sized or the batch is full.</summary>
        public static bool Add(SpriteBatch batch, float4 uv, float2 groundPosition, float depth, in BlobShadowProfile profile, float height = 0f, float scale = 1f)
        {
            height = math.max(0f, height);
            scale = math.max(0f, scale);
            float4 color = profile.Color;
            color.w *= math.saturate(1f - height * math.max(0f, profile.HeightFade));
            float2 size = math.max(profile.Size, 0f) * scale * (1f + height * math.max(0f, profile.HeightSpread));
            if (color.w <= 0f || math.any(size <= 0f)) return false;
            return batch.Add(groundPosition + profile.Offset * scale, size, uv, depth, color);
        }
    }
}
