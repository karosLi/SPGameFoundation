using System;
using SPF.Presentation.Sprites;
using SPF.Presentation.Combat;
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
            public Color32 Rim = C(115, 138, 129);
            public Pen(int w, int h) { Canvas = new PixelCanvas(w * Super, h * Super); }
            public void Oval(float x, float y, float rx, float ry, Color32 color, bool rim = true)
            {
                if (rim)
                {
                    Canvas.Ellipse(x * Super, y * Super, (rx + 1.25f) * Super, (ry + 1.25f) * Super, Rim);
                    Canvas.Ellipse(x * Super, y * Super, (rx + 1f) * Super, (ry + 1f) * Super, Ink);
                }
                Canvas.Ellipse(x * Super, y * Super, rx * Super, ry * Super, color);
            }
            public void Line(float ax, float ay, float bx, float by, float width, Color32 color, bool rim = true)
            {
                if (rim)
                {
                    Canvas.Line(new float2(ax, ay) * Super, new float2(bx, by) * Super, (width + 2.5f) * Super, Rim);
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
                    p.Rim = Paper;
                    p.Line(24, 10, 25, 21 + bob, 7, C(43, 68, 82));
                    p.Line(39, 10, 36, 22 + bob, 7, C(43, 68, 82));
                    p.Oval(31, 27 + bob, 12, 14, C(224, 217, 190));
                    p.Oval(31, 45 + bob, 14, 13, C(239, 230, 201));
                    p.Oval(31, 43 + bob, 9, 8, C(249, 225, 182));
                    p.Oval(27, 45 + bob, 1.5f, 2.2f, Ink, false);
                    p.Oval(35, 45 + bob, 1.5f, 2.2f, Ink, false);
                    p.Line(18, 27 + bob, 11, 21 + bob, 5, C(245, 211, 163));
                    p.Line(43, 31 + bob, 49, 26 + bob, 5, C(245, 211, 163));
                    p.Line(49, 12, 51, 43, 3.5f, C(170, 123, 81));
                    p.Oval(51, 47, 5, 6, C(148, 246, 246));
                    p.Line(24, 31 + bob, 39, 31 + bob, 3, C(242, 198, 91), false);
                    p.Line(26, 17+bob, 24, 27+bob, 2, C(43,103,131), false);
                    p.Line(35, 17+bob, 38, 27+bob, 2, C(128,215,219), false);
                    p.Line(23, 51+bob, 28, 55+bob, 2, C(187,246,230), false);
                    p.Line(39, 48+bob, 40, 40+bob, 2, C(50,128,155), false);
                    p.Oval(31,31+bob,2.4f,2.5f,C(253,230,155),false);
                    p.Line(49, 35, 53, 37, 2.5f, C(242,198,91), false);
                    p.Oval(50,49,1.7f,2.5f,Paper,false);
                });
                if (f == 0) firstHero = frame;
            }
            art.Hero = new SpriteClip(firstHero, 2, 6f, true);
            art.Enemies = new SpriteClip[enemyKinds];
            for (int k = 0; k < enemyKinds; k++)
            {
                int first = -1, shape = k % 4;
                Color32 family = shape == 0 ? C(199,109,94) : shape == 1 ? C(169,87,103) : shape == 2 ? C(192,139,87) : C(137,87,123);
                Color32 tint = (Color32)Color.Lerp(enemyColor(k), (Color)family, 0.78f);
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
                        p.Line(22,50+bob,26,54+bob,2,PixelCanvas.Shade(tint,1.2f),false);
                        p.Line(38,38+bob,40,43+bob,2,dark,false);
                        p.Oval(28,27+bob,5,7,PixelCanvas.Shade(tint,0.86f),false);
                        p.Line(22,23+bob,39,23+bob,2.3f,C(77,71,62),false);
                        p.Oval(32,23+bob,2,2,C(218,179,103),false);
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
                p.Line(16, 12, 64, 12, 14, C(117, 115, 94));
                p.Line(21, 24, 59, 24, 14, C(196, 191, 164));
                p.Line(20, 27, 19, 69, 12, C(185, 179, 150));
                p.Line(60, 27, 61, 69, 12, C(185, 179, 150));
                p.Line(14, 72, 26, 72, 9, C(220, 207, 166));
                p.Line(54, 72, 66, 72, 9, C(220, 207, 166));
                p.Oval(40, 53, 12, 27, C(72, 175, 160));
                p.Oval(40, 55, 6, 18, C(163, 241, 217));
                p.Line(35, 35, 45, 71, 3, C(245, 255, 207), false);
                p.Line(13, 14, 30, 14, 2, C(241, 231, 194), false);
                p.Line(18, 36, 19, 65, 2, C(232, 217, 164), false);
                p.Line(57, 30, 59, 64, 3, C(114, 119, 100), false);
                p.Line(14, 72, 26, 72, 3, C(179, 139, 71), false);
                p.Line(54, 72, 66, 72, 3, C(179, 139, 71), false);
                p.Canvas.Ring(40*Super,54*Super,21*Super,23*Super,C(191,153,83));
                p.Line(40, 83, 40, 91, 3, C(235, 214, 153), false);
            });
            art.Ground = Add(atlas, 64, 64, p =>
            {
                p.Canvas.Rect(0, 0, 64 * Super, 64 * Super, C(64, 82, 79));
                p.Line(4, 12, 23, 19, 0.6f, C(53, 71, 70), false);
                p.Line(23, 19, 30, 31, 0.5f, C(53, 71, 70), false);
                p.Line(48, 48, 61, 52, 0.6f, C(78, 97, 88), false);
                p.Oval(14, 47, 1, 0.5f, C(95, 111, 92), false);
                p.Line(0, 2, 62, 2, 0.45f, C(57, 76, 74), false);
                p.Line(2, 0, 2, 62, 0.45f, C(70, 89, 82), false);
                p.Oval(53, 19, 2.6f, 1.0f, C(74, 89, 74), false);
            });
            art.Bullets = new int[4];
            var colors = new[] { C(120, 236, 255), C(255, 222, 119), C(143, 248, 191), C(237, 110, 188) };
            for (int b = 0; b < 4; b++)
            {
                var color = colors[b];
                int shape = b;
                art.Bullets[b] = Add(atlas, 32, 32, p =>
                {
                    if (shape == 0) { p.Line(5,16,27,16,5,C(40,101,124),false); p.Line(9,16,26,16,3.2f,color,false); p.Line(16,16,26,16,1.4f,Paper,false); }
                    else { p.Oval(16,16,10,10,C(44,63,71),false); p.Oval(16,16,8,8,color,false); p.Oval(14,18,3,3,Paper,false); p.Oval(18,13,3,2,PixelCanvas.Shade(color,0.65f),false); }
                });
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
            art.CombatFx = CombatVfxArt.AddTo(atlas);
            art.Sheet = atlas.Build(filterMode: FilterMode.Bilinear, padding: 2, extrudeEdges: true);
            art.EnemyUv = new NativeArray<float4>(math.max(enemyKinds, 1) * 2, Allocator.Persistent);
            for (int k = 0; k < enemyKinds; k++) for (int f = 0; f < 2; f++) art.EnemyUv[k * 2 + f] = art.Sheet[art.Enemies[k].First + f].Uv;
            art.BulletUv = new NativeArray<float4>(4, Allocator.Persistent);
            for (int b = 0; b < 4; b++) art.BulletUv[b] = art.Sheet[art.Bullets[b]].Uv;
            return art;
        }
    }
}
