using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace SPF.Presentation.Sprites
{
    /// <summary>A sprite frame inside an atlas: UV rect and its size in pixels.</summary>
    public struct SpriteFrame
    {
        public float4 Uv;       // min xy, size zw
        public int2 Pixels;

        /// <summary>World size at <paramref name="pixelsPerUnit"/>.</summary>
        public float2 Size(float pixelsPerUnit) => (float2)Pixels / pixelsPerUnit;
    }

    /// <summary>
    /// Packs runtime-drawn frames (<see cref="PixelCanvas"/>) into one point-filtered atlas texture with a
    /// shelf packer (frames sorted by height), 1 px padding against bleeding. Frames are addressed by the
    /// index returned from <see cref="Add"/>; names are optional.
    /// </summary>
    public sealed class SpriteAtlasBuilder
    {
        readonly List<PixelCanvas> m_Frames = new List<PixelCanvas>();
        readonly Dictionary<string, int> m_Names = new Dictionary<string, int>();

        public int Count => m_Frames.Count;

        public int Add(PixelCanvas frame, string name = null)
        {
            m_Frames.Add(frame);
            if (name != null) m_Names[name] = m_Frames.Count - 1;
            return m_Frames.Count - 1;
        }

        /// <summary>Adds frames produced by <paramref name="draw"/>(canvas, frameIndex); returns the first index.</summary>
        public int AddStrip(int count, int width, int height, Action<PixelCanvas, int> draw, string name = null)
        {
            int first = m_Frames.Count;
            for (int i = 0; i < count; i++)
            {
                var canvas = new PixelCanvas(width, height);
                draw(canvas, i);
                Add(canvas, i == 0 ? name : null);
            }
            return first;
        }

        public SpriteSheet Build(int maxWidth = 1024)
        {
            const int Pad = 1;
            var order = new int[m_Frames.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (a, b) => m_Frames[b].Height != m_Frames[a].Height ? m_Frames[b].Height.CompareTo(m_Frames[a].Height) : a.CompareTo(b));

            var origins = new int2[m_Frames.Count];
            int x = Pad, y = Pad, shelf = 0, width = 0;
            foreach (int i in order)
            {
                var f = m_Frames[i];
                if (x + f.Width + Pad > maxWidth) { x = Pad; y += shelf + Pad; shelf = 0; }
                origins[i] = new int2(x, y);
                x += f.Width + Pad;
                width = math.max(width, x);
                shelf = math.max(shelf, f.Height);
            }
            int texW = math.ceilpow2(math.max(width, 4)), texH = math.ceilpow2(math.max(y + shelf + Pad, 4));
            var pixels = new Color32[texW * texH];
            var frames = new SpriteFrame[m_Frames.Count];
            for (int i = 0; i < m_Frames.Count; i++)
            {
                var f = m_Frames[i];
                int2 o = origins[i];
                for (int row = 0; row < f.Height; row++)
                    Array.Copy(f.Pixels, row * f.Width, pixels, (o.y + row) * texW + o.x, f.Width);
                frames[i] = new SpriteFrame
                {
                    Uv = new float4((float)o.x / texW, (float)o.y / texH, (float)f.Width / texW, (float)f.Height / texH),
                    Pixels = new int2(f.Width, f.Height),
                };
            }
            var texture = new Texture2D(texW, texH, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
                name = "SPF Sprite Atlas",
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return new SpriteSheet(texture, frames, new Dictionary<string, int>(m_Names), origins, new int2(texW, texH));
        }
    }

    /// <summary>A built atlas: texture + frames. Owns the texture.</summary>
    public sealed class SpriteSheet : IDisposable
    {
        readonly SpriteFrame[] m_Frames;
        readonly Dictionary<string, int> m_Names;

        internal SpriteSheet(Texture2D texture, SpriteFrame[] frames, Dictionary<string, int> names, int2[] origins, int2 size)
        {
            Texture = texture;
            m_Frames = frames;
            m_Names = names;
            Origins = origins;
            Size = size;
        }

        public Texture2D Texture { get; }
        public int Count => m_Frames.Length;
        public int2 Size { get; }
        /// <summary>Pixel origin of each frame in the atlas (tests, tooling).</summary>
        public int2[] Origins { get; }
        public SpriteFrame this[int index] => m_Frames[index];
        public int Find(string name) => m_Names.TryGetValue(name, out int i) ? i : -1;

        public void Dispose()
        {
            if (Texture != null) UnityEngine.Object.Destroy(Texture);
        }
    }
}
