using System;
using Unity.Mathematics;
using UnityEngine;

namespace SPF.Presentation.Sprites
{
    /// <summary>Original load-time vector-stroke numerals, rasterized with analytic edge coverage.
    /// No imported font, OS font dependency, runtime text object or per-hit glyph generation.
    /// Stroke coordinates are authored here; NaN pairs separate pen strokes.</summary>
    public static class SmoothNumberGlyphs
    {
        public const int Scale = 8, Width = 26, Height = 42;
        static readonly float[][] Paths =
        {
            new float[] { 9,31, 5,30, 2,26, 2,7, 5,3, 9,2, 13,3, 16,7, 16,26, 13,30, 9,31 },
            new float[] { 4,26, 9,31, 9,2, float.NaN,float.NaN, 4,2, 14,2 },
            new float[] { 2,26, 4,30, 8,31, 12,31, 16,27, 16,23, 13,19, 3,6, 2,2, 16,2 },
            new float[] { 2,28, 6,31, 12,31, 16,27, 16,22, 13,18, 8,17, float.NaN,float.NaN, 13,18, 16,13, 16,7, 12,2, 6,2, 2,5 },
            new float[] { 13,2, 13,31, 2,12, 17,12 },
            new float[] { 16,31, 3,31, 2,18, 10,18, 14,16, 16,12, 16,7, 12,2, 6,2, 2,5 },
            new float[] { 15,29, 11,31, 6,28, 3,23, 2,15, 2,7, 6,2, 12,2, 16,6, 16,12, 12,17, 7,18, 2,14 },
            new float[] { 2,31, 16,31, 6,2 },
            new float[] { 8,17, 3,21, 2,26, 5,30, 9,31, 13,30, 16,26, 15,21, 10,17, 5,16, 2,12, 2,7, 5,3, 9,2, 13,3, 16,7, 16,12, 13,16, 8,17 },
            new float[] { 16,19, 11,16, 6,17, 2,21, 2,27, 6,31, 12,31, 16,27, 16,18, 15,10, 12,5, 7,2, 3,4 },
            new float[] { 2,16, 16,16, float.NaN,float.NaN, 9,24, 9,8 },
            new float[] { 2,16, 16,16 },
            new float[] { 9,31, 9,12, float.NaN,float.NaN, 9,3, 9,3 },
            new float[] { 3,24, 15,8, float.NaN,float.NaN, 15,24, 3,8 },
            new float[] { 16,27, 12,31, 6,31, 2,26, 2,7, 6,2, 12,2, 16,6, 16,16, 10,16 },
        };
        public static PixelCanvas Create(int glyph)
        {
            if ((uint)glyph >= (uint)Paths.Length) throw new ArgumentOutOfRangeException(nameof(glyph));
            var canvas = new PixelCanvas(Width, Height); var path = Paths[glyph];
            for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++)
            {
                var p = new float2(x + .5f - 4f, y + .5f - 5f); float distance = float.MaxValue;
                for (int i = 2; i < path.Length; i += 2)
                {
                    var a = new float2(path[i - 2], path[i - 1]); var b = new float2(path[i], path[i + 1]);
                    if (!math.all(math.isfinite(a)) || !math.all(math.isfinite(b))) continue;
                    var delta = b - a; float lengthSq = math.lengthsq(delta);
                    float t = lengthSq > 0 ? math.saturate(math.dot(p - a, delta) / lengthSq) : 0;
                    distance = math.min(distance, math.distance(p, a + delta * t));
                }
                float outer = math.saturate(3.6f + .5f - distance);
                float fill = math.saturate(2.05f + .5f - distance);
                // Straight-alpha ink/white mix; the SpriteBatch tint supplies ivory or warm yellow.
                float white = outer > 0 ? fill / outer : 0;
                byte value = (byte)math.round(math.lerp(13f, 255f, white));
                canvas.Pixels[y * Width + x] = new Color32(value, value, value, (byte)math.round(outer * 255f));
            }
            return canvas;
        }
    }
}
