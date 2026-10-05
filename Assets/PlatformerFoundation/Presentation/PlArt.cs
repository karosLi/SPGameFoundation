using System;
using SPF.Presentation.Sprites;
using Unity.Mathematics;
using UnityEngine;

namespace PlatformerFoundation.Presentation
{
    /// <summary>Procedural pixel art for the platformer (16 px per tile).</summary>
    public sealed class PlArt : IDisposable
    {
        public const float PixelsPerUnit = 16f;
        public SpriteSheet Sheet { get; private set; }
        public SpriteClip HeroIdle, HeroRun, Coin, Walker, Flag, Sparkle;
        public int HeroJump, HeroFall, WalkerSquashed, Ground, GroundTop, Plank, Spikes, Platform, Sky;

        static Color32 C(byte r, byte g, byte b, byte a = 255) => new Color32(r, g, b, a);
        static readonly Color32 Outline = C(24, 20, 32);

        public static PlArt Build()
        {
            var art = new PlArt();
            var atlas = new SpriteAtlasBuilder();
            void Hero(PixelCanvas c, float legL, float legR, float bob, bool arms)
            {
                c.Line(new float2(6, 1), new float2(6 + legL, 5), 2f, C(60, 60, 90));
                c.Line(new float2(10, 1), new float2(10 + legR, 5), 2f, C(60, 60, 90));
                c.Rect(4, 5 + (int)bob, 8, 6, C(220, 70, 60));                 // body
                if (arms) { c.Rect(2, 8 + (int)bob, 2, 3, C(240, 200, 160)); c.Rect(12, 8 + (int)bob, 2, 3, C(240, 200, 160)); }
                c.Ellipse(8, 13 + bob, 3.2f, 3f, C(240, 200, 160));             // head
                c.Rect(5, 14 + (int)bob, 7, 2, C(60, 90, 200));                 // cap
                c.Set(10, 13 + (int)bob, C(20, 20, 30));
                c.Outline(Outline);
            }
            art.HeroIdle = new SpriteClip(atlas.AddStrip(2, 16, 18, (c, f) => Hero(c, 0, 0, f * 0.6f, false)), 2, 3f, true);
            art.HeroRun = new SpriteClip(atlas.AddStrip(4, 16, 18, (c, f) => Hero(c, f % 2 == 0 ? -2 : 2, f % 2 == 0 ? 2 : -2, f % 2, false)), 4, 12f, true);
            art.HeroJump = Single(atlas, 16, 18, c => Hero(c, -2, 1, 1, true));
            art.HeroFall = Single(atlas, 16, 18, c => Hero(c, 1, -1, 0, true));
            art.Coin = new SpriteClip(atlas.AddStrip(4, 10, 10, (c, f) =>
            {
                float w = f switch { 0 => 4f, 1 => 2.5f, 2 => 1f, _ => 2.5f };
                c.Ellipse(5, 5, w, 4f, C(255, 210, 60));
                if (w > 1.5f) c.Rect(5, 3, 1, 4, C(255, 240, 150));
                c.Outline(Outline);
            }), 4, 8f, true);
            art.Walker = new SpriteClip(atlas.AddStrip(2, 16, 14, (c, f) =>
            {
                c.Ellipse(8, 6, 6.5f, 5f, C(140, 80, 60));
                c.Rect(3 + f, 0, 3, 2, C(60, 40, 30)); c.Rect(10 - f, 0, 3, 2, C(60, 40, 30));
                c.Rect(5, 7, 2, 2, C(255, 255, 255)); c.Rect(9, 7, 2, 2, C(255, 255, 255));
                c.Set(6, 7, C(0, 0, 0)); c.Set(10, 7, C(0, 0, 0));
                c.Outline(Outline);
            }), 2, 6f, true);
            art.WalkerSquashed = Single(atlas, 16, 6, c => { c.Ellipse(8, 2.5f, 7f, 2f, C(140, 80, 60)); c.Outline(Outline); });
            var random = new Unity.Mathematics.Random(3);
            art.Ground = Single(atlas, 16, 16, c =>
            {
                c.Rect(0, 0, 16, 16, C(120, 82, 52));
                for (int i = 0; i < 18; i++) c.Set(random.NextInt(16), random.NextInt(16), C(100, 68, 44));
            });
            art.GroundTop = Single(atlas, 16, 16, c =>
            {
                c.Rect(0, 0, 16, 16, C(120, 82, 52));
                for (int i = 0; i < 14; i++) c.Set(random.NextInt(16), random.NextInt(12), C(100, 68, 44));
                c.Rect(0, 12, 16, 4, C(80, 170, 70));
                for (int x = 0; x < 16; x += 3) c.Set(x, 11, C(80, 170, 70));
            });
            art.Plank = Single(atlas, 16, 16, c => { c.Rect(0, 11, 16, 5, C(170, 120, 70)); c.Rect(0, 11, 16, 1, C(110, 75, 40)); c.Rect(2, 7, 2, 4, C(110, 75, 40)); c.Rect(12, 7, 2, 4, C(110, 75, 40)); });
            art.Spikes = Single(atlas, 16, 16, c =>
            {
                for (int k = 0; k < 4; k++) c.Line(new float2(2 + k * 4, 0), new float2(2 + k * 4, 9), 2.2f, C(200, 205, 220));
                c.Outline(Outline);
            });
            art.Platform = Single(atlas, 48, 8, c => { c.Rect(0, 0, 48, 8, C(150, 150, 170)); c.Rect(0, 6, 48, 2, C(200, 200, 220)); for (int x = 4; x < 48; x += 8) c.Rect(x, 2, 2, 2, C(90, 90, 110)); c.Outline(Outline); });
            art.Flag = new SpriteClip(atlas.AddStrip(2, 16, 32, (c, f) =>
            {
                c.Rect(2, 0, 2, 32, C(220, 220, 230));
                for (int y = 0; y < 10; y++) c.Rect(4, 20 + y, 10 - (f == 0 ? y % 3 : (y + 1) % 3), 1, C(80, 220, 120));
                c.Outline(Outline);
            }), 2, 4f, true);
            art.Sparkle = new SpriteClip(atlas.AddStrip(4, 9, 9, (c, f) =>
            {
                int r = 4 - f;
                c.Rect(4, 4 - r, 1, r * 2 + 1, C(255, 250, 200));
                c.Rect(4 - r, 4, r * 2 + 1, 1, C(255, 250, 200));
            }), 4, 14f, false);
            art.Sky = Single(atlas, 4, 4, c => c.Rect(0, 0, 4, 4, C(255, 255, 255)));
            art.Sheet = atlas.Build();
            return art;
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
