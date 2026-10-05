using Unity.Mathematics;
using UnityEngine;

namespace SPF.Presentation.Sprites
{
    /// <summary>
    /// Bakes a normal atlas for lit sprites from the colour atlas, so procedural or imported pixel art gets relief
    /// without hand-painted normal maps: each frame becomes a height field (a bevel from its silhouette and its own
    /// frame border, plus luminance detail) and the normals are its slopes. Runs once at load; the result shares
    /// the colour atlas layout, so instances need no extra data.
    /// </summary>
    public static class NormalMapBaker
    {
        public struct Settings
        {
            /// <summary>Width of the edge bevel in pixels.</summary>
            public float Bevel;
            /// <summary>Slope scale (bigger = deeper relief).</summary>
            public float Strength;
            /// <summary>How much brightness adds bumps inside a frame (0 = flat interior).</summary>
            public float Detail;

            public static Settings Default => new Settings { Bevel = 3f, Strength = 1.6f, Detail = 0.6f };
        }

        public static Texture2D Bake(SpriteSheet sheet) => Bake(sheet, Settings.Default);

        public static Texture2D Bake(SpriteSheet sheet, Settings settings)
        {
            var source = sheet.Texture;
            var size = new int2(source.width, source.height);
            var sizes = new int2[sheet.Count];
            for (int i = 0; i < sheet.Count; i++) sizes[i] = sheet[i].Pixels;
            var normals = Bake(source.GetPixels32(), size, sheet.Origins, sizes, settings);
            var texture = new Texture2D(size.x, size.y, TextureFormat.RGBA32, false, true)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
                name = "SPF Normal Atlas",
            };
            texture.SetPixels32(normals);
            texture.Apply(false, true);
            return texture;
        }

        /// <summary>Pure bake on pixel arrays (row-major, bottom row first), for tests and tools.</summary>
        public static Color32[] Bake(Color32[] pixels, int2 size, int2[] origins, int2[] frameSizes, Settings settings)
        {
            var result = new Color32[pixels.Length];
            var flat = new Color32(128, 128, 255, 255);
            for (int i = 0; i < result.Length; i++) result[i] = flat;
            int largest = 0;
            for (int f = 0; f < frameSizes.Length; f++) largest = math.max(largest, frameSizes[f].x * frameSizes[f].y);
            var dist = new float[largest];
            var height = new float[largest];
            for (int f = 0; f < origins.Length; f++)
            {
                int2 o = origins[f], s = frameSizes[f];
                // Chamfer distance to the nearest transparent pixel or frame edge (two passes).
                for (int y = 0; y < s.y; y++)
                    for (int x = 0; x < s.x; x++)
                    {
                        bool solid = pixels[(o.y + y) * size.x + o.x + x].a >= 128;
                        dist[y * s.x + x] = solid ? 1e6f : 0f;
                    }
                for (int y = 0; y < s.y; y++)
                    for (int x = 0; x < s.x; x++)
                    {
                        int i = y * s.x + x;
                        float d = dist[i];
                        if (d == 0f) continue;
                        d = math.min(d, x > 0 ? dist[i - 1] + 1f : 1f);
                        d = math.min(d, y > 0 ? dist[i - s.x] + 1f : 1f);
                        d = math.min(d, x > 0 && y > 0 ? dist[i - s.x - 1] + 1.414f : 1f);
                        d = math.min(d, x < s.x - 1 && y > 0 ? dist[i - s.x + 1] + 1.414f : 1f);
                        dist[i] = d;
                    }
                for (int y = s.y - 1; y >= 0; y--)
                    for (int x = s.x - 1; x >= 0; x--)
                    {
                        int i = y * s.x + x;
                        float d = dist[i];
                        if (d == 0f) continue;
                        d = math.min(d, x < s.x - 1 ? dist[i + 1] + 1f : 1f);
                        d = math.min(d, y < s.y - 1 ? dist[i + s.x] + 1f : 1f);
                        d = math.min(d, x < s.x - 1 && y < s.y - 1 ? dist[i + s.x + 1] + 1.414f : 1f);
                        d = math.min(d, x > 0 && y < s.y - 1 ? dist[i + s.x - 1] + 1.414f : 1f);
                        dist[i] = d;
                    }
                for (int y = 0; y < s.y; y++)
                    for (int x = 0; x < s.x; x++)
                    {
                        int i = y * s.x + x;
                        var p = pixels[(o.y + y) * size.x + o.x + x];
                        float lum = (0.299f * p.r + 0.587f * p.g + 0.114f * p.b) / 255f;
                        float bevel = math.smoothstep(0f, settings.Bevel, dist[i] - 0.5f);
                        height[i] = dist[i] == 0f ? 0f : bevel + settings.Detail * (lum - 0.5f) * 0.5f;
                    }
                for (int y = 0; y < s.y; y++)
                    for (int x = 0; x < s.x; x++)
                    {
                        int i = y * s.x + x;
                        if (dist[i] == 0f) continue;
                        float hl = x > 0 ? height[i - 1] : 0f, hr = x < s.x - 1 ? height[i + 1] : 0f;
                        float hd = y > 0 ? height[i - s.x] : 0f, hu = y < s.y - 1 ? height[i + s.x] : 0f;
                        float3 n = math.normalize(new float3((hl - hr) * settings.Strength, (hd - hu) * settings.Strength, 1f));
                        result[(o.y + y) * size.x + o.x + x] = new Color32((byte)math.round((n.x * 0.5f + 0.5f) * 255f),
                            (byte)math.round((n.y * 0.5f + 0.5f) * 255f), (byte)math.round((n.z * 0.5f + 0.5f) * 255f), 255);
                    }
            }
            return result;
        }
    }
}
