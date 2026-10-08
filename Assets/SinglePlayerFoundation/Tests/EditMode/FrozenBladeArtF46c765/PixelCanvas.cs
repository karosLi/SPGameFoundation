// Frozen test oracle from f46c7651193186126bcaa9f3f6537002293b7e2c; do not update with candidate art.
// Only namespace relocation and type-import directives differ from the original source.
using Unity.Mathematics;
using UnityEngine;

namespace SPF.Tests.EditMode.FrozenBladeArtF46c765
{
    /// <summary>
    /// Small CPU drawing surface for procedurally generated pixel art (characters, items, effects, fonts)
    /// when a game ships without art assets, or to tint / compose frames at load time. Origin bottom-left.
    /// </summary>
    public sealed class PixelCanvas
    {
        public readonly int Width, Height;
        public readonly Color32[] Pixels;

        public PixelCanvas(int width, int height)
        {
            Width = width;
            Height = height;
            Pixels = new Color32[width * height];
        }

        public void Clear() => System.Array.Clear(Pixels, 0, Pixels.Length);

        public Color32 Get(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height ? Pixels[y * Width + x] : default;

        public void Set(int x, int y, Color32 c)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height || c.a == 0) return;
            Pixels[y * Width + x] = c;
        }

        public void Rect(int x, int y, int w, int h, Color32 c)
        {
            for (int j = y; j < y + h; j++)
            for (int i = x; i < x + w; i++)
                Set(i, j, c);
        }

        /// <summary>Filled ellipse centred at (cx, cy) with radii (rx, ry) in pixels.</summary>
        public void Ellipse(float cx, float cy, float rx, float ry, Color32 c)
        {
            if (rx <= 0f || ry <= 0f) return;
            int x0 = (int)math.floor(cx - rx), x1 = (int)math.ceil(cx + rx);
            int y0 = (int)math.floor(cy - ry), y1 = (int)math.ceil(cy + ry);
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float dx = (x + 0.5f - cx) / rx, dy = (y + 0.5f - cy) / ry;
                if (dx * dx + dy * dy <= 1f) Set(x, y, c);
            }
        }

        /// <summary>Ring between radii (pixels).</summary>
        public void Ring(float cx, float cy, float inner, float outer, Color32 c)
        {
            int x0 = (int)math.floor(cx - outer), x1 = (int)math.ceil(cx + outer);
            int y0 = (int)math.floor(cy - outer), y1 = (int)math.ceil(cy + outer);
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float d = math.length(new float2(x + 0.5f - cx, y + 0.5f - cy));
                if (d >= inner && d <= outer) Set(x, y, c);
            }
        }

        /// <summary>Thick line (pixels) from a to b.</summary>
        public void Line(float2 a, float2 b, float thickness, Color32 c)
        {
            float length = math.length(b - a);
            int steps = math.max(1, (int)math.ceil(length * 2f));
            for (int s = 0; s <= steps; s++)
            {
                float2 p = math.lerp(a, b, s / (float)steps);
                Ellipse(p.x, p.y, thickness * 0.5f, thickness * 0.5f, c);
            }
        }

        /// <summary>Draws a 1-pixel outline of <paramref name="c"/> around every opaque pixel (classic pixel-art readability).</summary>
        public void Outline(Color32 c)
        {
            var copy = (Color32[])Pixels.Clone();
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                if (copy[y * Width + x].a != 0) continue;
                bool edge = false;
                for (int k = 0; k < 4 && !edge; k++)
                {
                    int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                    edge = nx >= 0 && ny >= 0 && nx < Width && ny < Height && copy[ny * Width + nx].a > 128;
                }
                if (edge) Pixels[y * Width + x] = c;
            }
        }

        /// <summary>Multiplies every pixel's alpha (fades, ghosts).</summary>
        public void Fade(float alpha)
        {
            for (int i = 0; i < Pixels.Length; i++) Pixels[i].a = (byte)(Pixels[i].a * math.saturate(alpha));
        }

        public static Color32 Shade(Color32 c, float factor) =>
            new Color32((byte)math.clamp(c.r * factor, 0, 255), (byte)math.clamp(c.g * factor, 0, 255), (byte)math.clamp(c.b * factor, 0, 255), c.a);
    }
}
