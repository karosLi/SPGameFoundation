using System;
using SPF.Presentation.Sprites;
using SPF.Presentation.Combat;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace SurvivorFoundation.Presentation
{
    /// <summary>Procedural pixel art for the survivor game, packed into one atlas.</summary>
    public sealed class SvArt : IDisposable
    {
        public const float PixelsPerUnit = 16f;

        public SpriteSheet Sheet { get; internal set; }
        public SpriteFont Font { get; internal set; }
        public SpriteClip Hero;
        public SpriteClip[] Enemies;              // index = kind - 1, 2-frame bob
        public int[] Bullets;                     // index = BulletVisual
        public int Gem, Blade, Ground, Glow, Ring, White, Shadow, Beacon;
        public SpriteClip Puff;
        public CombatVfxArt CombatFx;
        public WeaponProjectileArt WeaponProjectiles;

        /// <summary>Atlas rects of every enemy frame (kind - 1) * 2 + frame, for Burst draw jobs.</summary>
        public NativeArray<float4> EnemyUv;
        public NativeArray<float4> BulletUv;

        static Color32 C(byte r, byte g, byte b, byte a = 255) => new Color32(r, g, b, a);
        static readonly Color32 Outline = C(20, 16, 28);

        public static SvArt Build(int enemyKinds, Func<int, Color> enemyColor, SvArtStyle style = SvArtStyle.Pixel, bool includeWeaponProjectiles = false)
            => Build(enemyKinds, enemyColor, style, includeWeaponProjectiles, false);

        public static SvArt Build(int enemyKinds, Func<int, Color> enemyColor, SvArtStyle style, bool includeWeaponProjectiles, bool includeDamageNumbers)
        {
            if (style == SvArtStyle.SmoothOutline) return SvSmoothArt.Build(enemyKinds, enemyColor, includeWeaponProjectiles, includeDamageNumbers);
            var art = new SvArt();
            var atlas = new SpriteAtlasBuilder();
            art.Font = includeDamageNumbers ? SpriteFont.CreateSmooth(atlas) : new SpriteFont(atlas, 2);
            art.Hero = new SpriteClip(atlas.AddStrip(2, 14, 16, (c, f) =>
            {
                int bob = f;
                c.Ellipse(7, 4 + bob, 4, 4, C(70, 90, 200));          // cloak
                c.Ellipse(7, 10 + bob, 3.2f, 3.2f, C(240, 205, 170));  // head
                c.Rect(3, 12 + bob, 8, 2, C(200, 60, 60));             // hat brim
                c.Rect(5, 13 + bob, 4, 3, C(200, 60, 60));
                c.Set(6, 10 + bob, C(20, 20, 40)); c.Set(8, 10 + bob, C(20, 20, 40));
                c.Outline(Outline);
            }), 2, 6f, true);

            art.Enemies = new SpriteClip[enemyKinds];
            for (int k = 0; k < enemyKinds; k++)
            {
                var tint = (Color32)enemyColor(k);
                int shape = k % 4;
                art.Enemies[k] = new SpriteClip(atlas.AddStrip(2, 16, 16, (c, f) => DrawEnemy(c, shape, tint, f)), 2, 5f, true);
            }

            art.Bullets = new int[4];
            art.Bullets[(int)BulletVisual.Bolt] = Single(atlas, 8, 8, c => { c.Ellipse(4, 4, 3.5f, 3.5f, C(150, 220, 255)); c.Ellipse(4, 4, 1.8f, 1.8f, C(255, 255, 255)); });
            art.Bullets[(int)BulletVisual.Nova] = Single(atlas, 8, 8, c => { c.Ring(4, 4, 1.8f, 3.6f, C(255, 230, 120)); });
            art.Bullets[(int)BulletVisual.Spiral] = Single(atlas, 7, 7, c => { c.Line(new float2(3.5f, 0.5f), new float2(3.5f, 6.5f), 2.2f, C(140, 255, 170)); c.Line(new float2(0.5f, 3.5f), new float2(6.5f, 3.5f), 2.2f, C(140, 255, 170)); });
            art.Bullets[(int)BulletVisual.EnemyOrb] = Single(atlas, 10, 10, c => { c.Ellipse(5, 5, 4.6f, 4.6f, C(255, 70, 200)); c.Ellipse(5, 5, 2.4f, 2.4f, C(255, 220, 250)); });
            art.Gem = Single(atlas, 7, 9, c =>
            {
                c.Line(new float2(3.5f, 0.5f), new float2(0.5f, 4.5f), 1.2f, C(255, 255, 255));
                c.Line(new float2(0.5f, 4.5f), new float2(3.5f, 8.5f), 1.2f, C(255, 255, 255));
                c.Line(new float2(3.5f, 8.5f), new float2(6.5f, 4.5f), 1.2f, C(255, 255, 255));
                c.Line(new float2(6.5f, 4.5f), new float2(3.5f, 0.5f), 1.2f, C(255, 255, 255));
                c.Ellipse(3.5f, 4.5f, 2f, 3f, C(220, 230, 255));
                c.Outline(Outline);
            });
            art.Blade = Single(atlas, 14, 14, c =>
            {
                for (int a = 0; a < 3; a++)
                {
                    float ang = a * 2.094f;
                    c.Line(new float2(7f, 7f), new float2(7f + math.cos(ang) * 6.5f, 7f + math.sin(ang) * 6.5f), 2f, C(220, 225, 240));
                }
                c.Ellipse(7, 7, 2f, 2f, C(120, 130, 160));
                c.Outline(Outline);
            });
            var random = new Unity.Mathematics.Random(5);
            art.Ground = Single(atlas, 32, 32, c =>
            {
                c.Rect(0, 0, 32, 32, C(34, 46, 38));
                for (int i = 0; i < 40; i++)
                {
                    int x = random.NextInt(32), y = random.NextInt(32);
                    c.Set(x, y, random.NextBool() ? C(42, 58, 46) : C(28, 38, 32));
                }
            });
            art.Glow = Single(atlas, 16, 16, c =>
            {
                for (int r = 8; r > 0; r--) c.Ellipse(8, 8, r, r, C(255, 255, 255, (byte)(255 * (1f - r / 8.5f) * (1f - r / 8.5f))));
            });
            art.Ring = Single(atlas, 32, 32, c => c.Ring(16, 16, 13, 15.5f, C(255, 255, 255)));
            art.Puff = new SpriteClip(atlas.AddStrip(4, 16, 16, (c, f) =>
            {
                float r = 3f + f * 1.8f;
                byte a = (byte)(220 - f * 50);
                c.Ring(8, 8, r - 1.5f, r, C(230, 230, 240, a));
            }), 4, 14f, false);

            art.White = Single(atlas, 1, 1, c => c.Set(0, 0, C(255, 255, 255)));
            art.Shadow = atlas.Add(BlobShadow.CreateCanvas(32, 16));
            art.Beacon = Single(atlas, 24, 32, c =>
            {
                c.Rect(2, 2, 20, 5, C(140, 148, 129)); c.Rect(3, 7, 4, 18, C(158, 168, 143));
                c.Rect(17, 7, 4, 18, C(158, 168, 143)); c.Ellipse(12, 18, 4, 10, C(169, 238, 118)); c.Outline(Outline);
            });
            art.CombatFx = CombatVfxArt.AddTo(atlas);
            if (includeWeaponProjectiles) art.WeaponProjectiles = WeaponProjectileArt.AddTo(atlas);
            art.Sheet = atlas.Build();
            art.EnemyUv = new NativeArray<float4>(math.max(enemyKinds, 1) * 2, Allocator.Persistent);
            for (int k = 0; k < enemyKinds; k++)
                for (int f = 0; f < 2; f++) art.EnemyUv[k * 2 + f] = art.Sheet[art.Enemies[k].First + f].Uv;
            art.BulletUv = new NativeArray<float4>(4, Allocator.Persistent);
            for (int b = 0; b < 4; b++) art.BulletUv[b] = art.Sheet[art.Bullets[b]].Uv;
            return art;
        }

        static void DrawEnemy(PixelCanvas c, int shape, Color32 tint, int f)
        {
            Color32 dark = PixelCanvas.Shade(tint, 0.6f), eye = C(255, 240, 90);
            switch (shape)
            {
                case 0:   // bat: body and flapping wings
                    c.Ellipse(8, 8, 3f, 3f, tint);
                    if (f == 0) { c.Line(new float2(5, 9), new float2(0.5f, 13), 2f, dark); c.Line(new float2(11, 9), new float2(15.5f, 13), 2f, dark); }
                    else { c.Line(new float2(5, 8), new float2(0.5f, 4), 2f, dark); c.Line(new float2(11, 8), new float2(15.5f, 4), 2f, dark); }
                    c.Set(7, 9, eye); c.Set(9, 9, eye);
                    break;
                case 1:   // zombie: hunched body, arms forward
                    c.Rect(5, 1, 6, 9 + f, dark);
                    c.Ellipse(8, 11 + f, 3f, 3f, tint);
                    c.Rect(10, 6 + f, 5, 2, tint);
                    c.Set(9, 12 + f, eye);
                    break;
                case 2:   // brute: wide horned block
                    c.Rect(2, 1, 12, 10, dark);
                    c.Ellipse(8, 11 - f * 0.5f, 5f, 4f, tint);
                    c.Line(new float2(4, 13), new float2(2, 15.5f), 1.5f, C(230, 220, 200));
                    c.Line(new float2(12, 13), new float2(14, 15.5f), 1.5f, C(230, 220, 200));
                    c.Set(6, 11, eye); c.Set(10, 11, eye);
                    break;
                default:  // mage: robe and glowing orb
                    c.Line(new float2(8, 1), new float2(8, 11), 6f - f, dark);
                    c.Ellipse(8, 12, 2.6f, 2.6f, tint);
                    c.Rect(6, 13, 4, 3, dark);
                    c.Ellipse(13, 9 + f, 1.8f, 1.8f, C(255, 120, 230));
                    c.Set(8, 12, eye);
                    break;
            }
            c.Outline(Outline);
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
            if (EnemyUv.IsCreated) EnemyUv.Dispose();
            if (BulletUv.IsCreated) BulletUv.Dispose();
        }
    }
}
