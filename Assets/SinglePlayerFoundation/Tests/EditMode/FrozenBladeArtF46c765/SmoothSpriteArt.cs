// Frozen test oracle from f46c7651193186126bcaa9f3f6537002293b7e2c; do not update with candidate art.
// Only namespace relocation and type-import directives differ from the original source.
using System;
using UnityEngine;

namespace SPF.Tests.EditMode.FrozenBladeArtF46c765
{
    /// <summary>Loading-time supersampling for original smooth placeholder art; never call per frame.</summary>
    public static class SmoothSpriteArt
    {
        /// <summary>
        /// Box-downsamples an integer supersampling grid, averaging premultiplied colour then returning
        /// straight-alpha RGBA. Transparent RGB cannot contaminate this averaging. Dimensions must be
        /// exact multiples of factor. A final one-pixel RGB bleed keeps zero-alpha neighbours suitable
        /// for mipless bilinear sampling without changing coverage. Filtering is in the source colour
        /// space, not a linear-light conversion.
        /// </summary>
        public static PixelCanvas Downsample(PixelCanvas source, int factor)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (factor < 1) throw new ArgumentOutOfRangeException(nameof(factor));
            if (source.Width <= 0 || source.Height <= 0 || source.Width % factor != 0 || source.Height % factor != 0)
                throw new ArgumentException("Source dimensions must be positive multiples of the factor.", nameof(source));
            var result = new PixelCanvas(source.Width / factor, source.Height / factor);
            long samples = (long)factor * factor;
            for (int y = 0; y < result.Height; y++)
            for (int x = 0; x < result.Width; x++)
            {
                long alpha = 0, red = 0, green = 0, blue = 0;
                for (int sy = 0; sy < factor; sy++)
                for (int sx = 0; sx < factor; sx++)
                {
                    var c = source.Pixels[(y * factor + sy) * source.Width + x * factor + sx];
                    alpha += c.a;
                    red += c.r * c.a;
                    green += c.g * c.a;
                    blue += c.b * c.a;
                }
                if (alpha == 0) continue;
                result.Pixels[y * result.Width + x] = new Color32(
                    (byte)((red + alpha / 2) / alpha), (byte)((green + alpha / 2) / alpha),
                    (byte)((blue + alpha / 2) / alpha), (byte)((alpha + samples / 2) / samples));
            }
            BleedTransparentRgb(result);
            return result;
        }

        /// <summary>
        /// Load-time one-pixel colour dilation for straight-alpha bilinear sprites. Only RGB in fully
        /// transparent pixels changes; alpha and every covered pixel stay exact. Does not support mipmaps
        /// or unlimited minification. Imported art may call this before Add(canvas) when its transparent
        /// border contains black RGB. Neighbours are read from a snapshot, so iteration order cannot smear.
        /// </summary>
        public static void BleedTransparentRgb(PixelCanvas canvas)
        {
            if (canvas == null) throw new ArgumentNullException(nameof(canvas));
            var source = (Color32[])canvas.Pixels.Clone();
            for (int y = 0; y < canvas.Height; y++)
            for (int x = 0; x < canvas.Width; x++)
            {
                int index = y * canvas.Width + x;
                if (source[index].a != 0) continue;
                int alpha = 0, red = 0, green = 0, blue = 0;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int px = x + dx, py = y + dy;
                    if (px < 0 || py < 0 || px >= canvas.Width || py >= canvas.Height) continue;
                    var c = source[py * canvas.Width + px];
                    alpha += c.a;
                    red += c.r * c.a;
                    green += c.g * c.a;
                    blue += c.b * c.a;
                }
                if (alpha > 0)
                    canvas.Pixels[index] = new Color32((byte)((red + alpha / 2) / alpha),
                        (byte)((green + alpha / 2) / alpha), (byte)((blue + alpha / 2) / alpha), 0);
            }
        }
    }
}
