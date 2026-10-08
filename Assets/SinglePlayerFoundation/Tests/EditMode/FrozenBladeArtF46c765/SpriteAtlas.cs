// Frozen test oracle from f46c7651193186126bcaa9f3f6537002293b7e2c; do not update with candidate art.
// Only namespace relocation and type-import directives differ from the original source.
using SPF.Presentation;
using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace SPF.Tests.EditMode.FrozenBladeArtF46c765
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
    /// Packs runtime-drawn or imported RGBA frames into one atlas with a deterministic shelf packer.
    /// The default remains point-filtered with 1 px spacing. Smooth art can select bilinear filtering,
    /// padding and edge extrusion. Building and importing allocate and belong in loading, not gameplay.
    /// </summary>
    public sealed class SpriteAtlasBuilder
    {
        readonly List<PixelCanvas> m_Frames = new List<PixelCanvas>();
        readonly Dictionary<string, int> m_Names = new Dictionary<string, int>();

        public int Count => m_Frames.Count;

        public int Add(PixelCanvas frame, string name = null)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (frame.Width <= 0 || frame.Height <= 0) throw new ArgumentException("Frames must have positive dimensions.", nameof(frame));
            m_Frames.Add(frame);
            if (name != null) m_Names[name] = m_Frames.Count - 1;
            return m_Frames.Count - 1;
        }

        /// <summary>Copies bottom-left-origin straight-alpha RGBA pixels; later caller edits are independent.</summary>
        public int Add(Color32[] pixels, int width, int height, string name = null)
        {
            if (pixels == null) throw new ArgumentNullException(nameof(pixels));
            if (width <= 0 || height <= 0 || (long)width * height != pixels.Length)
                throw new ArgumentException("Pixel count must match positive frame dimensions.", nameof(pixels));
            var canvas = new PixelCanvas(width, height);
            Array.Copy(pixels, canvas.Pixels, pixels.Length);
            return Add(canvas, name);
        }

        /// <summary>
        /// Copies a readable imported texture at load time. Enable Read/Write in its import settings;
        /// this method does not take ownership of the source texture or change its import settings.
        /// </summary>
        public int Add(Texture2D source, string name = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            return Add(source.GetPixels32(), source.width, source.height, name);
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

        public SpriteSheet Build(int maxWidth = 1024, FilterMode filterMode = FilterMode.Point, int padding = 1, bool extrudeEdges = false)
        {
            if (maxWidth < 1) throw new ArgumentOutOfRangeException(nameof(maxWidth));
            if (padding < 0 || (long)padding * 2 >= maxWidth) throw new ArgumentOutOfRangeException(nameof(padding));
            if (extrudeEdges && padding == 0) throw new ArgumentException("Edge extrusion needs at least one padding pixel.", nameof(padding));
            int pad = padding;
            // Extrusion needs a private gutter on BOTH sides of every frame. Preserve the legacy
            // one-sided spacing exactly when extrusion is off, including default pixel-art layouts.
            int gap = extrudeEdges ? padding * 2 : padding;
            for (int i = 0; i < m_Frames.Count; i++)
                if ((long)m_Frames[i].Width + 2 * pad > maxWidth)
                    throw new ArgumentException("A frame and its padding exceed the atlas shelf width.", nameof(maxWidth));
            var order = new int[m_Frames.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (a, b) => m_Frames[b].Height != m_Frames[a].Height ? m_Frames[b].Height.CompareTo(m_Frames[a].Height) : a.CompareTo(b));

            var origins = new int2[m_Frames.Count];
            int x = pad, y = pad, shelf = 0, width = 0;
            foreach (int i in order)
            {
                var f = m_Frames[i];
                if (x + f.Width + pad > maxWidth) { x = pad; y += shelf + gap; shelf = 0; }
                origins[i] = new int2(x, y);
                width = math.max(width, x + f.Width + pad);
                x += f.Width + gap;
                shelf = math.max(shelf, f.Height);
            }
            int texW = math.ceilpow2(math.max(width, 4)), texH = math.ceilpow2(math.max(y + shelf + pad, 4));
            var pixels = new Color32[texW * texH];
            var frames = new SpriteFrame[m_Frames.Count];
            for (int i = 0; i < m_Frames.Count; i++)
            {
                var f = m_Frames[i];
                int2 o = origins[i];
                for (int row = 0; row < f.Height; row++)
                    Array.Copy(f.Pixels, row * f.Width, pixels, (o.y + row) * texW + o.x, f.Width);
                if (extrudeEdges)
                {
                    // Copy the nearest edge/corner pixel into this frame's own gutter. UVs still
                    // reference only the original content; adjacent frames never share gutter pixels.
                    for (int row = -pad; row < f.Height + pad; row++)
                    for (int col = -pad; col < f.Width + pad; col++)
                    {
                        if (row >= 0 && row < f.Height && col >= 0 && col < f.Width) continue;
                        pixels[(o.y + row) * texW + o.x + col] = f.Pixels[math.clamp(row, 0, f.Height - 1) * f.Width + math.clamp(col, 0, f.Width - 1)];
                    }
                }
                frames[i] = new SpriteFrame
                {
                    Uv = new float4((float)o.x / texW, (float)o.y / texH, (float)f.Width / texW, (float)f.Height / texH),
                    Pixels = new int2(f.Width, f.Height),
                };
            }
            var texture = new Texture2D(texW, texH, TextureFormat.RGBA32, false)
            {
                filterMode = filterMode,
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
            if (Texture != null) RenderObjects.Destroy(Texture);
        }
    }
}
