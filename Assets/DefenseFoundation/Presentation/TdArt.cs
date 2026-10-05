using System;
using SPF.Presentation.Sprites;
using Unity.Mathematics;
using UnityEngine;

namespace DefenseFoundation.Presentation
{
    public sealed class TdArt : IDisposable
    {
        public SpriteSheet Sheet { get; private set; }
        public int Grass, Grass2, Rock, Portal, Castle, Base, Dot, Frame, Ring;
        public int[] Turrets;
        public SpriteClip[] Enemies;
        public SpriteClip Puff;

        static Color32 C(byte r, byte g, byte b, byte a = 255) => new Color32(r, g, b, a);
        static readonly Color32 Outline = C(22, 20, 28);

        public static TdArt Build()
        {
            var art = new TdArt();
            var atlas = new SpriteAtlasBuilder();
            var random = new Unity.Mathematics.Random(7);
            int GrassTile(Color32 a, Color32 b) => Single(atlas, 16, 16, c => { c.Rect(0, 0, 16, 16, a); for (int i = 0; i < 20; i++) c.Set(random.NextInt(16), random.NextInt(16), b); });
            art.Grass = GrassTile(C(78, 138, 70), C(90, 155, 80));
            art.Grass2 = GrassTile(C(74, 132, 66), C(86, 150, 76));
            art.Rock = Single(atlas, 16, 16, c => { c.Ellipse(8, 7, 6.5f, 5.5f, C(120, 118, 125)); c.Ellipse(6, 9, 3f, 2f, C(150, 148, 155)); c.Outline(Outline); });
            art.Portal = Single(atlas, 16, 16, c => { c.Ring(8, 8, 4f, 7f, C(170, 80, 220)); c.Ellipse(8, 8, 3.5f, 3.5f, C(60, 20, 80)); });
            art.Castle = Single(atlas, 16, 16, c =>
            {
                c.Rect(2, 0, 12, 10, C(180, 175, 165));
                for (int x = 2; x < 14; x += 3) c.Rect(x, 10, 2, 3, C(180, 175, 165));
                c.Rect(6, 0, 4, 5, C(90, 60, 40));
                c.Rect(7, 13, 1, 3, C(120, 90, 60)); c.Rect(8, 14, 3, 2, C(220, 60, 60));
                c.Outline(Outline);
            });
            art.Base = Single(atlas, 16, 16, c => { c.Rect(1, 1, 14, 14, C(110, 100, 90)); c.Rect(2, 2, 12, 12, C(140, 130, 115)); c.Outline(Outline); });
            art.Turrets = new int[3];
            art.Turrets[(int)TowerKind.Arrow] = Single(atlas, 16, 16, c => { c.Ellipse(7, 8, 4.5f, 4.5f, C(150, 110, 70)); c.Rect(9, 7, 7, 2, C(90, 60, 30)); c.Outline(Outline); });
            art.Turrets[(int)TowerKind.Cannon] = Single(atlas, 16, 16, c => { c.Ellipse(7, 8, 5f, 5f, C(70, 70, 80)); c.Rect(9, 6, 7, 4, C(40, 40, 48)); c.Outline(Outline); });
            art.Turrets[(int)TowerKind.Frost] = Single(atlas, 16, 16, c => { c.Ellipse(8, 8, 5f, 5f, C(140, 200, 240)); c.Line(new float2(8, 3), new float2(8, 13), 1.5f, C(240, 250, 255)); c.Line(new float2(3, 8), new float2(13, 8), 1.5f, C(240, 250, 255)); c.Outline(Outline); });
            art.Enemies = new SpriteClip[3];
            var colors = new[] { C(220, 80, 70), C(120, 70, 50), C(240, 200, 60) };
            for (int k = 0; k < 3; k++)
            {
                var col = colors[k];
                art.Enemies[k] = new SpriteClip(atlas.AddStrip(2, 12, 12, (c, f) =>
                {
                    c.Ellipse(6, 6 + f * 0.5f, 5f - f * 0.3f, 4.5f + f * 0.3f, col);
                    c.Set(4, 7, C(255, 255, 255)); c.Set(8, 7, C(255, 255, 255));
                    c.Outline(Outline);
                }), 2, 6f, true);
            }
            art.Dot = Single(atlas, 4, 4, c => c.Rect(0, 0, 4, 4, C(255, 255, 255)));
            art.Frame = Single(atlas, 16, 16, c => { c.Rect(0, 0, 16, 1, C(255, 255, 255)); c.Rect(0, 15, 16, 1, C(255, 255, 255)); c.Rect(0, 0, 1, 16, C(255, 255, 255)); c.Rect(15, 0, 1, 16, C(255, 255, 255)); });
            art.Ring = Single(atlas, 64, 64, c => c.Ring(32, 32, 30, 31.5f, C(255, 255, 255)));
            art.Puff = new SpriteClip(atlas.AddStrip(4, 16, 16, (c, f) => c.Ring(8, 8, 2f + f * 1.5f, 3.5f + f * 1.5f, C(240, 240, 240, (byte)(220 - f * 50)))), 4, 14f, false);
            art.Sheet = atlas.Build();
            return art;
        }

        static int Single(SpriteAtlasBuilder atlas, int w, int h, Action<PixelCanvas> draw)
        {
            var c = new PixelCanvas(w, h);
            draw(c);
            return atlas.Add(c);
        }

        public void Dispose() { Sheet?.Dispose(); Sheet = null; }
    }
}
