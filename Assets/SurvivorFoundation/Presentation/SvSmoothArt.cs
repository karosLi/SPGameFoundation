using System;
using SPF.Presentation.Sprites;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace SurvivorFoundation.Presentation
{
    public enum SvArtStyle : byte { Pixel, SmoothOutline }

    /// <summary>Original smooth outlined cutout art. Supersampling supplies real alpha coverage;
    /// the renderer uses sorted translucent actors, not an opaque cutout with a bilinear filter.</summary>
    static class SvSmoothArt
    {
        const int Super = 3;
        static readonly Color32 Ink = new Color32(40, 42, 45, 255);
        static readonly Color32 Paper = new Color32(250, 244, 217, 255);
        static Color32 C(byte r, byte g, byte b, byte a = 255) => new Color32(r, g, b, a);

        sealed class Pen
        {
            public readonly PixelCanvas Canvas;
            public Pen(int w, int h) { Canvas = new PixelCanvas(w * Super, h * Super); }
            public void Oval(float x, float y, float rx, float ry, Color32 color, bool rim = true)
            {
                if (rim)
                {
                    Canvas.Ellipse(x * Super, y * Super, (rx + 1.8f) * Super, (ry + 1.8f) * Super, Paper);
                    Canvas.Ellipse(x * Super, y * Super, (rx + 1f) * Super, (ry + 1f) * Super, Ink);
                }
                Canvas.Ellipse(x * Super, y * Super, rx * Super, ry * Super, color);
            }
            public void Line(float ax, float ay, float bx, float by, float width, Color32 color, bool rim = true)
            {
                if (rim)
                {
                    Canvas.Line(new float2(ax, ay) * Super, new float2(bx, by) * Super, (width + 3.6f) * Super, Paper);
                    Canvas.Line(new float2(ax, ay) * Super, new float2(bx, by) * Super, (width + 2f) * Super, Ink);
                }
                Canvas.Line(new float2(ax, ay) * Super, new float2(bx, by) * Super, width * Super, color);
            }
        }

        static int Add(SpriteAtlasBuilder atlas, int w, int h, Action<Pen> draw)
        {
            var pen = new Pen(w, h); draw(pen);
            return atlas.Add(SmoothSpriteArt.Downsample(pen.Canvas, Super));
        }

        public static SvArt Build(int enemyKinds, Func<int, Color> enemyColor)
        {
            var art = new SvArt(); var atlas = new SpriteAtlasBuilder();
            art.Font = new SpriteFont(atlas, 2);
            int firstHero = -1;
            for (int f = 0; f < 2; f++)
            {
                int bob = f;
                int frame = Add(atlas, 64, 64, p =>
                {
                    p.Line(24, 10, 25, 21 + bob, 7, C(43, 68, 82));
                    p.Line(39, 10, 36, 22 + bob, 7, C(43, 68, 82));
                    p.Oval(31, 27 + bob, 12, 14, C(77, 156, 178));
                    p.Oval(31, 45 + bob, 14, 13, C(97, 204, 221));
                    p.Oval(31, 43 + bob, 9, 8, C(249, 225, 182));
                    p.Oval(27, 45 + bob, 1.5f, 2.2f, Ink, false);
                    p.Oval(35, 45 + bob, 1.5f, 2.2f, Ink, false);
                    p.Line(18, 27 + bob, 11, 21 + bob, 5, C(245, 211, 163));
                    p.Line(43, 31 + bob, 49, 26 + bob, 5, C(245, 211, 163));
                    p.Line(49, 12, 51, 43, 3.5f, C(170, 123, 81));
                    p.Oval(51, 47, 5, 6, C(148, 246, 246));
                    p.Line(24, 31 + bob, 39, 31 + bob, 3, C(242, 198, 91), false);
                });
                if (f == 0) firstHero = frame;
            }
            art.Hero = new SpriteClip(firstHero, 2, 6f, true);
            art.Enemies = new SpriteClip[enemyKinds];
            for (int k = 0; k < enemyKinds; k++)
            {
                int first = -1, shape = k % 4;
                Color32 tint = (Color32)Color.Lerp(enemyColor(k), new Color(0.63f, 0.68f, 0.36f), 0.6f);
                for (int f = 0; f < 2; f++)
                {
                    int bob = f;
                    int frame = Add(atlas, 64, 64, p =>
                    {
                        var dark = PixelCanvas.Shade(tint, 0.65f);
                        p.Line(22, 11, 24, 23, 6, dark); p.Line(40, 10, 36, 23, 6, dark);
                        p.Oval(31, 27 + bob, shape == 2 ? 18 : 11, 14, dark);
                        p.Line(21, 30 + bob, 12, 36 - bob * 3, 5, tint);
                        p.Line(42, 30 + bob, 52, 34 + bob * 3, 5, tint);
                        p.Oval(31, 45 + bob, shape == 2 ? 17 : 12, 12, tint);
                        p.Oval(26, 46 + bob, 3.5f, 4, Paper);
                        p.Oval(37, 46 + bob, 3.5f, 4, Paper);
                        p.Oval(27, 45 + bob, 1.7f, 2.3f, Ink, false);
                        p.Oval(36, 45 + bob, 1.7f, 2.3f, Ink, false);
                        p.Line(27, 37 + bob, 35, 37 + bob, 2.5f, Ink, false);
                        if (shape == 0) { p.Oval(20, 54 + bob, 4, 5, tint); p.Oval(39, 55 + bob, 4, 4, tint); }
                        if (shape == 2) { p.Line(18, 52, 15, 59, 3, Paper); p.Line(43, 52, 46, 59, 3, Paper); }
                        if (shape == 3) p.Oval(53, 38 + bob, 4, 5, C(211, 129, 231));
                    });
                    if (f == 0) first = frame;
                }
                art.Enemies[k] = new SpriteClip(first, 2, 5f, true);
            }
            art.Beacon = Add(atlas, 80, 96, p =>
            {
                p.Line(16, 12, 64, 12, 14, C(117, 126, 118));
                p.Line(21, 24, 59, 24, 14, C(161, 170, 150));
                p.Line(20, 27, 19, 69, 12, C(132, 145, 134));
                p.Line(60, 27, 61, 69, 12, C(132, 145, 134));
                p.Line(14, 72, 26, 72, 9, C(184, 194, 168));
                p.Line(54, 72, 66, 72, 9, C(184, 194, 168));
                p.Oval(40, 53, 12, 27, C(135, 213, 131));
                p.Oval(40, 55, 6, 18, C(219, 254, 154));
                p.Line(35, 35, 45, 71, 3, C(245, 255, 207), false);
                p.Line(13, 14, 30, 14, 2, C(206, 214, 188), false);
            });
            art.Ground = Add(atlas, 64, 64, p =>
            {
                p.Canvas.Rect(0, 0, 64 * Super, 64 * Super, C(182, 177, 151));
                p.Line(4, 12, 23, 19, 0.6f, C(167, 164, 142), false);
                p.Line(23, 19, 30, 31, 0.5f, C(167, 164, 142), false);
                p.Line(48, 48, 61, 52, 0.6f, C(194, 188, 161), false);
                p.Oval(14, 47, 1, 0.5f, C(158, 157, 136), false);
            });
            art.Bullets = new int[4];
            var colors = new[] { C(120, 236, 255), C(255, 222, 119), C(143, 248, 191), C(237, 110, 188) };
            for (int b = 0; b < 4; b++)
            {
                var color = colors[b];
                art.Bullets[b] = Add(atlas, 24, 24, p => { p.Oval(12, 12, 9, 9, color, false); p.Oval(12, 12, 4, 4, Paper, false); });
            }
            art.Gem = Add(atlas, 24, 32, p => { p.Line(12, 5, 7, 16, 6, C(135, 227, 233)); p.Line(7, 16, 12, 27, 6, C(184, 247, 240)); });
            art.Blade = Add(atlas, 32, 32, p => { p.Line(4, 4, 28, 28, 4, Paper); p.Line(9, 9, 18, 3, 3, C(199, 152, 69)); });
            art.White = atlas.Add(new[] { new Color32(255, 255, 255, 255) }, 1, 1, "solid");
            art.Shadow = atlas.Add(BlobShadow.CreateCanvas());
            art.Glow = Add(atlas, 32, 32, p =>
            {
                for (int r = 15; r > 0; r--) p.Oval(16, 16, r, r, C(255, 255, 255, (byte)(220 * (1f - r / 16f) * (1f - r / 16f))), false);
            });
            art.Ring = Add(atlas, 128, 128, p => p.Canvas.Ring(64 * Super, 64 * Super, 59 * Super, 62 * Super, Paper));
            int puff = -1;
            for (int f = 0; f < 4; f++)
            {
                int step = f;
                int frame = Add(atlas, 32, 32, p => p.Canvas.Ring(16 * Super, 16 * Super, (5 + step * 2) * Super, (7 + step * 2) * Super, C(244, 247, 233, (byte)(200 - step * 45))));
                if (f == 0) puff = frame;
            }
            art.Puff = new SpriteClip(puff, 4, 14, false);
            art.Sheet = atlas.Build(filterMode: FilterMode.Bilinear, padding: 2, extrudeEdges: true);
            art.EnemyUv = new NativeArray<float4>(math.max(enemyKinds, 1) * 2, Allocator.Persistent);
            for (int k = 0; k < enemyKinds; k++) for (int f = 0; f < 2; f++) art.EnemyUv[k * 2 + f] = art.Sheet[art.Enemies[k].First + f].Uv;
            art.BulletUv = new NativeArray<float4>(4, Allocator.Persistent);
            for (int b = 0; b < 4; b++) art.BulletUv[b] = art.Sheet[art.Bullets[b]].Uv;
            return art;
        }
    }
}
