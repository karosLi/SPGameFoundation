using System;
using SPF.Presentation.Sprites;
using Unity.Mathematics;
using UnityEngine;

namespace SlingFoundation.Presentation
{
    /// <summary>Procedural pixel art for the slingshot game: materials, targets, the bird, the sling and effects.</summary>
    public sealed class SlArt : IDisposable
    {
        public SpriteSheet Sheet { get; private set; }
        public int Wood, Stone, Glass, Ground, Grass, Target, TargetHurt, Bird, BirdBlink, Sling, Dot, Band, Chip;
        public SpriteClip Puff;

        static Color32 C(byte r, byte g, byte b, byte a = 255) => new Color32(r, g, b, a);
        static readonly Color32 Outline = C(30, 22, 30);

        public static SlArt Build()
        {
            var art = new SlArt();
            var atlas = new SpriteAtlasBuilder();
            var random = new Unity.Mathematics.Random(11);
            art.Wood = Single(atlas, 16, 16, c =>
            {
                c.Rect(0, 0, 16, 16, C(186, 132, 74));
                for (int y = 2; y < 16; y += 4) c.Rect(0, y, 16, 1, C(150, 102, 54));
                for (int i = 0; i < 6; i++) c.Rect(random.NextInt(14), random.NextInt(16), 2, 1, C(205, 155, 95));
                Border(c, 16, 16, Outline);
            });
            art.Stone = Single(atlas, 16, 16, c =>
            {
                c.Rect(0, 0, 16, 16, C(140, 140, 150));
                c.Rect(0, 7, 16, 1, C(100, 100, 112)); c.Rect(7, 0, 1, 7, C(100, 100, 112)); c.Rect(3, 8, 1, 8, C(100, 100, 112)); c.Rect(11, 8, 1, 8, C(100, 100, 112));
                for (int i = 0; i < 10; i++) c.Set(random.NextInt(16), random.NextInt(16), C(165, 165, 175));
                Border(c, 16, 16, Outline);
            });
            art.Glass = Single(atlas, 16, 16, c =>
            {
                c.Rect(0, 0, 16, 16, C(170, 220, 240));
                c.Line(new float2(3, 12), new float2(7, 4), 1.2f, C(235, 250, 255));
                c.Line(new float2(9, 13), new float2(12, 7), 1f, C(235, 250, 255));
                Border(c, 16, 16, C(80, 130, 160));
            });
            art.Ground = Single(atlas, 16, 16, c =>
            {
                c.Rect(0, 0, 16, 16, C(120, 86, 56));
                for (int i = 0; i < 16; i++) c.Set(random.NextInt(16), random.NextInt(16), C(98, 70, 44));
            });
            art.Grass = Single(atlas, 16, 4, c => { c.Rect(0, 0, 16, 4, C(90, 180, 80)); for (int x = 0; x < 16; x += 3) c.Set(x, 3, C(120, 210, 100)); });
            void Face(PixelCanvas c, bool hurt)
            {
                c.Ellipse(8, 8, 7f, 7f, C(120, 200, 90));
                c.Ellipse(8, 6, 3f, 2f, C(90, 160, 70));
                if (hurt) { c.Line(new float2(4, 11), new float2(6, 9), 1f, C(20, 20, 20)); c.Line(new float2(10, 9), new float2(12, 11), 1f, C(20, 20, 20)); }
                else { c.Rect(5, 9, 2, 2, C(255, 255, 255)); c.Rect(10, 9, 2, 2, C(255, 255, 255)); c.Set(6, 9, C(0, 0, 0)); c.Set(10, 9, C(0, 0, 0)); }
                c.Outline(Outline);
            }
            art.Target = Single(atlas, 16, 16, c => Face(c, false));
            art.TargetHurt = Single(atlas, 16, 16, c => Face(c, true));
            void BirdFace(PixelCanvas c, bool blink)
            {
                c.Ellipse(8, 8, 7f, 7f, C(220, 60, 50));
                c.Ellipse(9, 6, 4f, 3f, C(245, 220, 190));
                c.Rect(11, 7, 4, 2, C(250, 190, 40));                         // beak
                if (blink) c.Rect(8, 10, 4, 1, C(30, 20, 20));
                else { c.Rect(8, 9, 3, 3, C(255, 255, 255)); c.Set(10, 10, C(0, 0, 0)); }
                c.Rect(6, 12, 5, 1, C(60, 20, 20));                           // brow
                c.Outline(Outline);
            }
            art.Bird = Single(atlas, 16, 16, c => BirdFace(c, false));
            art.BirdBlink = Single(atlas, 16, 16, c => BirdFace(c, true));
            art.Sling = Single(atlas, 12, 28, c =>
            {
                c.Rect(5, 0, 3, 16, C(120, 80, 40));
                c.Line(new float2(6, 15), new float2(1, 27), 2.5f, C(120, 80, 40));
                c.Line(new float2(6, 15), new float2(11, 27), 2.5f, C(120, 80, 40));
                c.Outline(Outline);
            });
            art.Dot = Single(atlas, 4, 4, c => c.Ellipse(2, 2, 1.8f, 1.8f, C(255, 255, 255)));
            art.Band = Single(atlas, 4, 4, c => c.Rect(0, 0, 4, 4, C(70, 40, 30)));
            art.Chip = Single(atlas, 4, 4, c => c.Rect(0, 0, 4, 4, C(255, 255, 255)));
            art.Puff = new SpriteClip(atlas.AddStrip(4, 12, 12, (c, f) =>
            {
                float r = 2.5f + f * 1.2f;
                c.Ellipse(6, 6, r, r, C(255, 255, 255, (byte)(230 - f * 50)));
            }), 4, 14f, false);
            art.Sheet = atlas.Build();
            return art;
        }

        static void Border(PixelCanvas c, int w, int h, Color32 color)
        {
            c.Rect(0, 0, w, 1, color); c.Rect(0, h - 1, w, 1, color);
            c.Rect(0, 0, 1, h, color); c.Rect(w - 1, 0, 1, h, color);
        }

        static int Single(SpriteAtlasBuilder atlas, int w, int h, Action<PixelCanvas> draw)
        {
            var c = new PixelCanvas(w, h);
            draw(c);
            return atlas.Add(c);
        }

        public void Dispose()
        {
            Sheet?.Dispose();
            Sheet = null;
        }
    }
}
