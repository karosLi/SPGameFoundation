using System;
using SPF.Presentation.Sprites;
using Unity.Mathematics;
using UnityEngine;

namespace RpgFoundation.Presentation
{
    /// <summary>Animation clips of one character.</summary>
    public enum CharacterClip { Idle = 0, Walk = 1, Attack = 2, Cast = 3, Hit = 4, Death = 5, Run = 6, AreaCast = 7, Channel = 8, SlamCast = 9 }

    public sealed class CharacterArt
    {
        public SpriteClip[] Clips = new SpriteClip[10];
        public float2 Size;          // world size of a frame
        public float2 Center;        // sprite centre relative to the actor position (feet below)
        public float2 Hand;          // weapon hand relative to the actor position, facing right
        public SpriteClip Clip(CharacterClip c) => Clips[(int)c];
        public float PoseScale;      // humanoid authored pixels -> world; zero for non-humanoids
        public float2 HandAt(CharacterClip clip, int frame) => PoseScale > 0f
            ? RpgArt.HandAt(clip, frame - Clip(clip).First) * PoseScale : Hand;
    }

    public sealed class WeaponArt
    {
        public int Frame;
        public float2 Size;
        public float Grip;           // distance from the sprite's left end to the grip (world)
    }

    /// <summary>
    /// Procedural pixel art for the dungeon (the repository ships no art): characters with idle / walk / run /
    /// attack / skill-specific cast / hit / death clips, weapon overlays, projectiles, items, tiles, effects and a pixel
    /// font, all packed into one atlas. 24 pixels per world unit (one tile = 24 px).
    /// </summary>
    public sealed class RpgArt : IDisposable
    {
        public const float PixelsPerUnit = 24f;

        public SpriteSheet Sheet { get; private set; }
        public SpriteFont Font { get; private set; }
        public CharacterArt Hero { get; private set; }
        public CharacterArt[] Monsters { get; private set; }     // index = monster kind - 1
        public WeaponArt[] Weapons { get; private set; }         // index = WeaponKind
        public SpriteClip Spark, Slash, Explosion, Puff, Fireball, Bolt, Coin, Stairs, Pillar, Sparkle, Dust;
        public int Arrow, Spit, Potion, ArmourIcon, Ring, Disc, Shadow;
        public int ChestClosed, ChestOpen, Barrel, SpikesDown, SpikesUp;
        public int[] Floor, Wall;

        static readonly Color32 Outline = new Color32(24, 16, 22, 255);

        public static RpgArt Build(int monsterKinds, Func<int, Color> monsterColor)
        {
            var art = new RpgArt();
            var atlas = new SpriteAtlasBuilder();
            art.Font = new SpriteFont(atlas, 2);

            art.Hero = Humanoid(atlas, new Style
            {
                Skin = C(240, 200, 160), Hair = C(110, 70, 40), Shirt = C(60, 110, 210), Pants = C(70, 60, 90), Boots = C(80, 50, 30),
                Eye = C(30, 30, 50), Size = 24, Cast = C(255, 150, 60), HeroSkills = true,
            });
            art.Monsters = new CharacterArt[monsterKinds];
            for (int k = 0; k < monsterKinds; k++)
            {
                var tint = (Color32)monsterColor(k);
                art.Monsters[k] = k switch
                {
                    0 => Slime(atlas, tint),
                    1 => Humanoid(atlas, new Style { Skin = C(235, 230, 210), Shirt = C(200, 195, 175), Pants = C(170, 165, 150), Boots = C(140, 135, 120), Eye = C(255, 60, 40), Size = 24, Skeleton = true, Cast = C(200, 80, 255) }),
                    2 => Humanoid(atlas, new Style { Skin = Shade(tint, 1.1f), Hair = C(60, 40, 30), Shirt = C(90, 70, 50), Pants = C(70, 55, 40), Boots = C(50, 35, 25), Eye = C(255, 230, 120), Size = 32, Horns = true, Bulky = true, Cast = C(255, 90, 60) }),
                    4 => Slime(atlas, tint),
                    5 => Humanoid(atlas, new Style { Skin = tint, Shirt = Shade(tint, 0.6f), Pants = C(90, 30, 20), Boots = C(50, 20, 15), Eye = C(255, 240, 80), Size = 20, Horns = true, Cast = C(255, 120, 30) }),
                    _ => Humanoid(atlas, new Style { Skin = C(110, 100, 130), Shirt = Shade(tint, 0.8f), Pants = C(60, 55, 80), Boots = C(40, 36, 55), Eye = C(255, 90, 255), Size = 48, Helmet = true, Bulky = true, Cape = Shade(tint, 0.55f), Cast = C(220, 100, 255) }),
                };
            }

            art.Weapons = new WeaponArt[Enum.GetValues(typeof(WeaponKind)).Length];
            art.Weapons[(int)WeaponKind.Sword] = Weapon(atlas, 16, 7, 3, DrawSword);
            art.Weapons[(int)WeaponKind.Axe] = Weapon(atlas, 15, 11, 3, DrawAxe);
            art.Weapons[(int)WeaponKind.Spear] = Weapon(atlas, 26, 7, 8, DrawSpear);
            art.Weapons[(int)WeaponKind.Bow] = Weapon(atlas, 8, 18, 3, DrawBow);
            art.Weapons[(int)WeaponKind.Staff] = Weapon(atlas, 18, 8, 5, DrawStaff);
            art.Weapons[(int)WeaponKind.Hammer] = Weapon(atlas, 16, 12, 3, DrawHammer);
            art.Weapons[(int)WeaponKind.Claw] = null;

            art.Spark = Strip(atlas, 4, 16, 16, 18f, false, (c, f) =>
            {
                float r = 2f + f * 2.2f;
                for (int k = 0; k < 8; k++)
                {
                    float a = k * math.PI / 4f + f * 0.2f;
                    float len = k % 2 == 0 ? r + 2f : r;
                    c.Line(new float2(8, 8) + Dir(a) * (r * 0.4f), new float2(8, 8) + Dir(a) * len, f < 2 ? 2f : 1f, k % 2 == 0 ? C(255, 255, 220) : C(255, 200, 80));
                }
                if (f < 2) c.Ellipse(8, 8, 2.5f - f, 2.5f - f, C(255, 255, 255));
            });
            art.Slash = Strip(atlas, 3, 32, 32, 20f, false, (c, f) =>
            {
                // Crescent opening to the right (rotated to the aim at runtime).
                float thick = 5f - f * 1.5f;
                for (int k = -14; k <= 14; k++)
                {
                    float a = k * 0.085f;
                    float2 p = new float2(10, 16) + Dir(a) * 13f;
                    c.Ellipse(p.x, p.y, thick * (1f - math.abs(k) / 16f), thick * (1f - math.abs(k) / 16f), f == 0 ? C(255, 255, 255) : C(220, 235, 255, (byte)(230 - f * 60)));
                }
            });
            art.Explosion = Strip(atlas, 6, 32, 32, 16f, false, (c, f) =>
            {
                float t = f / 5f;
                c.Ellipse(16, 16, 5 + t * 11, 5 + t * 11, C(90, 70, 70, (byte)(200 - t * 180)));
                c.Ellipse(16, 16, 4 + t * 9, 4 + t * 9, C(255, 120, 30, (byte)(255 - t * 200)));
                c.Ellipse(16, 16, 3 + t * 6, 3 + t * 6, C(255, 220, 90, (byte)(255 - t * 230)));
                if (f < 3) c.Ellipse(16, 16, 3 - f, 3 - f, C(255, 255, 255));
            });
            art.Puff = Strip(atlas, 5, 24, 24, 12f, false, (c, f) =>
            {
                float t = f / 4f;
                for (int k = 0; k < 5; k++)
                {
                    float2 p = new float2(12, 12) + Dir(k * 1.3f) * (2f + t * 7f);
                    c.Ellipse(p.x, p.y, 3.5f - t * 1.5f, 3.5f - t * 1.5f, C(170, 160, 170, (byte)(220 - t * 200)));
                }
            });
            art.Dust = Strip(atlas, 3, 12, 8, 14f, false, (c, f) =>
            {
                c.Ellipse(6, 3, 4 - f, 2.5f - f * 0.6f, C(180, 165, 140, (byte)(200 - f * 60)));
            });
            art.Fireball = Strip(atlas, 3, 14, 14, 14f, true, (c, f) =>
            {
                c.Ellipse(7, 7, 6, 6, C(255, 90, 20, 200));
                c.Ellipse(7 + (f == 1 ? 1 : 0), 7, 4.5f, 4.5f, C(255, 170, 40));
                c.Ellipse(8, 7 + (f == 2 ? 1 : 0), 2.5f, 2.5f, C(255, 255, 200));
            });
            art.Bolt = Strip(atlas, 2, 10, 10, 10f, true, (c, f) =>
            {
                c.Ellipse(5, 5, 4.5f, 4.5f, C(160, 80, 255, 180));
                c.Ellipse(5, 5, 3f - f * 0.5f, 3f - f * 0.5f, C(230, 200, 255));
            });
            art.Arrow = Single(atlas, 12, 5, c =>
            {
                c.Line(new float2(1, 2.5f), new float2(9, 2.5f), 1f, C(150, 110, 60));
                c.Set(10, 2, C(220, 220, 230)); c.Set(11, 2, C(220, 220, 230)); c.Set(10, 3, C(200, 200, 210)); c.Set(10, 1, C(200, 200, 210));
                c.Set(1, 1, C(230, 230, 230)); c.Set(1, 3, C(230, 230, 230)); c.Set(2, 1, C(230, 230, 230)); c.Set(2, 3, C(230, 230, 230));
            });
            art.Spit = Single(atlas, 8, 8, c => { c.Ellipse(4, 4, 3, 3, C(140, 230, 90)); c.Set(3, 5, C(230, 255, 200)); });
            art.Coin = Strip(atlas, 4, 9, 9, 10f, true, (c, f) =>
            {
                float w = f switch { 0 => 3.5f, 1 => 2.5f, 2 => 1f, _ => 2.5f };
                c.Ellipse(4.5f, 4.5f, w, 3.5f, C(255, 200, 40));
                if (w > 1.5f) c.Ellipse(4.5f, 4.5f, w * 0.5f, 2f, C(255, 235, 120));
                c.Outline(Outline);
            });
            art.Potion = Single(atlas, 10, 12, c =>
            {
                c.Rect(4, 8, 2, 3, C(200, 190, 170));
                c.Ellipse(5, 5, 4, 4, C(220, 40, 70));
                c.Ellipse(4, 6, 1.2f, 1.2f, C(255, 170, 190));
                c.Outline(Outline);
            });
            art.ArmourIcon = Single(atlas, 12, 12, c =>
            {
                c.Rect(2, 2, 8, 8, C(150, 160, 180));
                c.Rect(0, 7, 3, 3, C(130, 140, 160)); c.Rect(9, 7, 3, 3, C(130, 140, 160));
                c.Rect(5, 3, 2, 6, C(200, 210, 230));
                c.Outline(Outline);
            });
            art.Ring = Single(atlas, 64, 64, c => c.Ring(32, 32, 27, 31, C(255, 255, 255)));
            art.Disc = Single(atlas, 32, 32, c =>
            {
                for (int r = 15; r > 0; r--) c.Ellipse(16, 16, r, r, C(255, 255, 255, (byte)(255 * (1f - r / 16f) * (1f - r / 16f) + 40)));
            });
            art.Shadow = Single(atlas, 16, 8, c => c.Ellipse(8, 4, 7, 3, C(0, 0, 0, 110)));
            art.ChestClosed = Single(atlas, 18, 15, c => DrawChest(c, false));
            art.ChestOpen = Single(atlas, 18, 15, c => DrawChest(c, true));
            art.Barrel = Single(atlas, 13, 15, c =>
            {
                c.Ellipse(6.5f, 7.5f, 5.5f, 7f, C(150, 95, 50));
                c.Rect(2, 3, 9, 1, C(90, 90, 100)); c.Rect(1, 10, 11, 1, C(90, 90, 100));
                c.Rect(4, 1, 1, 13, C(120, 75, 40)); c.Rect(8, 1, 1, 13, C(120, 75, 40));
                c.Ellipse(6.5f, 13f, 4f, 1.2f, C(175, 120, 70));
                c.Outline(Outline);
            });
            art.SpikesDown = Single(atlas, 20, 20, c => DrawSpikes(c, false));
            art.SpikesUp = Single(atlas, 20, 20, c => DrawSpikes(c, true));
            art.Pillar = Strip(atlas, 4, 16, 40, 10f, true, (c, f) =>
            {
                for (int k = 0; k < 4; k++)
                {
                    int x = 2 + ((k * 5 + f * 3) % 12);
                    c.Rect(x, 0, 2, 40 - k * 4, C(255, 230, 120, (byte)(140 + k * 25)));
                }
            });
            art.Sparkle = Strip(atlas, 4, 9, 9, 12f, false, (c, f) =>
            {
                int r = 4 - f;
                c.Rect(4, 4 - r, 1, r * 2 + 1, C(220, 255, 220));
                c.Rect(4 - r, 4, r * 2 + 1, 1, C(220, 255, 220));
            });

            var random = new Unity.Mathematics.Random(91);
            art.Floor = new int[3];
            for (int v = 0; v < 3; v++) art.Floor[v] = Single(atlas, 24, 24, c => DrawFloor(c, ref random));
            art.Wall = new int[2];
            for (int v = 0; v < 2; v++) art.Wall[v] = Single(atlas, 24, 24, c => DrawWall(c, ref random, v));
            art.Stairs = Strip(atlas, 4, 24, 24, 6f, true, (c, f) =>
            {
                for (int s = 0; s < 5; s++)
                {
                    byte g = (byte)(70 + s * 18);
                    c.Rect(2 + s * 2, 2 + s * 4, 20 - s * 4, 4, C(g, g, (byte)(g + 15)));
                    c.Rect(2 + s * 2, 2 + s * 4, 20 - s * 4, 1, C(30, 28, 40));
                }
                c.Ring(12, 12, 9 + f * 0.6f, 10.5f + f * 0.6f, C(90, 230, 255, (byte)(200 - f * 40)));
            });

            art.Sheet = atlas.Build(1024);
            return art;
        }

        public void Dispose() => Sheet?.Dispose();

        // ---- Characters ----

        struct Style
        {
            public Color32 Skin, Hair, Shirt, Pants, Boots, Eye, Cast, Cape;
            public int Size;
            public bool Skeleton, Horns, Helmet, Bulky, HeroSkills;
        }

        static CharacterArt Humanoid(SpriteAtlasBuilder atlas, Style s)
        {
            int size = s.Size;
            float k = size / 24f;
            var art = new CharacterArt
            {
                Size = new float2(size, size) / PixelsPerUnit,
                // Feet at the actor position (pixel y = 2), sprite centre above it.
                Center = new float2(0f, (size * 0.5f - 2f * k) / PixelsPerUnit),
                Hand = new float2(4.5f * k, (10f - 2f) * k) / PixelsPerUnit,
                PoseScale = k / PixelsPerUnit,
            };
            SpriteClip PoseStrip(CharacterClip clip, int count, float fps, bool loop, bool glow = false)
            {
                return new SpriteClip(atlas.AddStrip(count, size, size, (c, f) =>
                {
                    var pose = Pose(clip, f);
                    DrawHumanoid(c, s, pose.Bob, pose.Leg, pose.Lean, pose.Arms, pose.Swing);
                    if (!glow) return;
                    float2 hand = HandAt(clip, f) + new float2(12f, 2f);
                    float radius = (1f + f * 0.35f) * k;
                    c.Ellipse(hand.x * k, hand.y * k, radius, radius, s.Cast);
                    c.Ellipse(hand.x * k, hand.y * k, radius * 0.5f, radius * 0.5f, C(255, 255, 230));
                }), count, fps, loop);
            }
            art.Clips[(int)CharacterClip.Idle] = PoseStrip(CharacterClip.Idle, 4, 5f, true);
            art.Clips[(int)CharacterClip.Walk] = PoseStrip(CharacterClip.Walk, 4, 6f, true);
            art.Clips[(int)CharacterClip.Run] = PoseStrip(CharacterClip.Run, 4, 12f, true);
            art.Clips[(int)CharacterClip.Attack] = PoseStrip(CharacterClip.Attack, 4, 12f, false);
            // Only the hero owns projectile/nova/whirlwind. The Warden's existing three cast frames
            // become its overhead slam; unused variants alias the existing strip, avoiding atlas bloat.
            var casting = s.Helmet ? CharacterClip.SlamCast : CharacterClip.Cast;
            art.Clips[(int)CharacterClip.Cast] = PoseStrip(casting, 3, 10f, false, true);
            art.Clips[(int)CharacterClip.SlamCast] = art.Clip(CharacterClip.Cast);
            art.Clips[(int)CharacterClip.AreaCast] = s.HeroSkills
                ? PoseStrip(CharacterClip.AreaCast, 3, 10f, false, true) : art.Clip(CharacterClip.Cast);
            art.Clips[(int)CharacterClip.Channel] = s.HeroSkills
                ? PoseStrip(CharacterClip.Channel, 4, 12f, true) : art.Clip(CharacterClip.Cast);
            art.Clips[(int)CharacterClip.Hit] = PoseStrip(CharacterClip.Hit, 2, 10f, false);
            art.Clips[(int)CharacterClip.Death] = new SpriteClip(atlas.AddStrip(5, size, size, (c, f) =>
            {
                var upright = new PixelCanvas(size, size);
                DrawHumanoid(upright, s, bob: -0.3f * f, leg: 0f, lean: -1f, arms: 0.5f);
                Rotate(upright, c, new float2(12 * k, 2 * k), -f * 0.39f);   // falls backwards to ~90 degrees
                if (f == 4) c.Fade(0.85f);
            }), 5, 10f, false);
            return art;
        }

        struct CharacterPose
        {
            public float Bob, Leg, Lean, Arms, Swing;
        }

        // Sprite pixels and weapon grips share the exact authored frame pose. This is a pixel-art
        // attachment anchor, not a skeletal IK solver.
        static CharacterPose Pose(CharacterClip clip, int f)
        {
            float stride = f == 0 ? -1f : f == 2 ? 1f : 0f;
            switch (clip)
            {
                case CharacterClip.Walk:
                    return new CharacterPose { Bob = f == 1 ? 0.8f : f == 3 ? 0.3f : 0f, Leg = stride, Lean = 0.3f, Swing = stride * 0.5f };
                case CharacterClip.Run:
                    return new CharacterPose { Bob = f == 1 ? 1.5f : f == 3 ? 0.7f : -0.3f, Leg = stride * 2.4f, Lean = 1.6f, Swing = stride * 1.8f };
                case CharacterClip.Attack:
                    return new CharacterPose { Bob = f < 2 ? 0.4f : -0.4f, Leg = f < 2 ? -0.6f : 0.8f, Lean = f < 2 ? -1f : 1.6f };
                case CharacterClip.Cast: // projectile: gather close to the chest, then extend forwards
                    return new CharacterPose { Bob = 0.2f, Lean = -0.8f + f * 0.7f, Arms = 0.3f, Swing = -1.2f + f * 1.4f };
                case CharacterClip.AreaCast: // nova: crouch, then raise both hands to release
                    return new CharacterPose { Bob = -1f + f * 0.7f, Leg = -0.8f, Arms = 0.35f + f * 0.3f };
                case CharacterClip.SlamCast: // Warden: overhead windup, braced stance
                    return new CharacterPose { Bob = f * 0.3f, Leg = -1f, Lean = -1f, Arms = 0.65f + f * 0.15f };
                case CharacterClip.Channel: // whirlwind: alternate planted contacts and torso/arm sweeps
                    return new CharacterPose { Bob = f % 2 == 0 ? -0.5f : 0.3f, Leg = stride * 1.8f, Lean = stride * 1.5f, Arms = 0.25f, Swing = stride * 2.2f };
                case CharacterClip.Hit:
                    return new CharacterPose { Bob = -0.5f, Lean = -1.8f + f * 0.6f, Arms = 0.4f };
                default:
                    return new CharacterPose { Bob = f == 2 ? 0.6f : f == 0 ? 0f : 0.3f };
            }
        }

        internal static float2 HandAt(CharacterClip clip, int frame)
        {
            var pose = Pose(clip, frame);
            return new float2(4.5f + pose.Lean * 0.5f + pose.Swing, 8f + pose.Bob + pose.Arms * 8f);
        }

        static void DrawHumanoid(PixelCanvas c, Style s, float bob, float leg, float lean, float arms, float swing = 0f)
        {
            float k = s.Size / 24f;
            float bulk = s.Bulky ? 1.25f : 1f;
            if (s.Cape.a > 0) c.Ellipse((10 - lean * 0.3f) * k, (10 + bob) * k, 4.5f * k * bulk, 6f * k, s.Cape);
            // Legs.
            c.Rect((int)((9.5f + leg) * k), (int)(2 * k), (int)(2.5f * k), (int)(7 * k), s.Pants);
            c.Rect((int)((12.5f - leg) * k), (int)(2 * k), (int)(2.5f * k), (int)(7 * k), Shade(s.Pants, 0.8f));
            c.Rect((int)((9f + leg) * k), (int)(1.5f * k), (int)(3.5f * k), (int)(1.8f * k), s.Boots);
            c.Rect((int)((12f - leg) * k), (int)(1.5f * k), (int)(3.5f * k), (int)(1.8f * k), Shade(s.Boots, 0.8f));
            // Back arm, torso, front arm (raised when casting).
            float tx = (12 + lean * 0.5f) * k, ty = (11.5f + bob) * k;
            c.Line(new float2(tx - 2.5f * k, ty + 1.5f * k), new float2(tx - (3.5f + swing) * k, ty - 2.5f * k + arms * 7f * k), 2f * k, Shade(s.Skin, 0.8f));
            c.Ellipse(tx, ty, 4f * k * bulk, 4.6f * k, s.Shirt);
            c.Ellipse(tx - 1.2f * k, ty + 1f * k, 2f * k, 2.5f * k, Shade(s.Shirt, 1.18f));
            if (s.Skeleton)
                for (int r = 0; r < 3; r++) c.Rect((int)(tx - 2.5f * k), (int)(ty - 1.5f * k + r * 1.6f * k), (int)(5 * k), 1, C(90, 85, 80));
            c.Line(new float2(tx + 2.5f * k, ty + 1.5f * k), new float2(tx + (4.5f + swing) * k, ty - 1.5f * k + arms * 8f * k), 2f * k, s.Skin);
            // Head.
            float hx = (12.5f + lean) * k, hy = (17.8f + bob) * k;
            c.Ellipse(hx, hy, 3.8f * k, 3.8f * k, s.Skin);
            if (s.Hair.a > 0 && !s.Helmet)
            {
                c.Ellipse(hx - 0.8f * k, hy + 1.8f * k, 3.9f * k, 2.2f * k, s.Hair);
                c.Rect((int)(hx - 4f * k), (int)(hy - 1f * k), (int)(2f * k), (int)(3f * k), s.Hair);
            }
            if (s.Helmet)
            {
                c.Ellipse(hx, hy + 0.5f * k, 4.3f * k, 4.3f * k, Shade(s.Shirt, 0.9f));
                c.Rect((int)(hx - 0.5f * k), (int)(hy - 0.3f * k), (int)(4f * k), (int)(1.2f * k), s.Eye);
            }
            else if (s.Skeleton)
            {
                c.Ellipse(hx + 1.6f * k, hy + 0.3f * k, 1.2f * k, 1.4f * k, C(40, 30, 35));
                c.Set((int)(hx + 1.6f * k), (int)(hy + 0.3f * k), s.Eye);
                c.Rect((int)(hx), (int)(hy - 2.6f * k), (int)(3f * k), 1, C(60, 55, 50));
            }
            else
                c.Rect((int)(hx + 1.5f * k), (int)(hy + 0.3f * k), math.max(1, (int)(1 * k)), math.max(1, (int)(1.4f * k)), s.Eye);
            if (s.Horns)
            {
                c.Line(new float2(hx - 2f * k, hy + 3f * k), new float2(hx - 3.5f * k, hy + 6f * k), 1.6f * k, C(235, 225, 200));
                c.Line(new float2(hx + 2f * k, hy + 3f * k), new float2(hx + 3.5f * k, hy + 6f * k), 1.6f * k, C(235, 225, 200));
            }
            c.Outline(Outline);
        }

        static CharacterArt Slime(SpriteAtlasBuilder atlas, Color32 tint)
        {
            const int W = 24, H = 20;
            var art = new CharacterArt { Size = new float2(W, H) / PixelsPerUnit, Center = new float2(0f, (H * 0.5f - 2f) / PixelsPerUnit), Hand = new float2(0.2f, 0.15f) };
            void Blob(PixelCanvas c, float rx, float ry, float y, float x, float eyes)
            {
                c.Ellipse(12 + x, 2 + ry + y, rx, ry, tint);
                c.Ellipse(12 + x - rx * 0.3f, 2 + ry * 1.4f + y, rx * 0.35f, ry * 0.3f, Shade(tint, 1.35f));
                if (eyes > 0f)
                {
                    c.Ellipse(14 + x + rx * 0.2f, 2 + ry * 1.1f + y, 1.6f, 1.8f * eyes, C(255, 255, 255));
                    c.Set((int)(14.5f + x + rx * 0.2f), (int)(2 + ry * 1.1f + y), C(20, 20, 30));
                }
                c.Outline(Outline);
            }
            art.Clips[(int)CharacterClip.Idle] = new SpriteClip(atlas.AddStrip(4, W, H, (c, f) => Blob(c, 8 + (f == 2 ? 0.6f : 0f), 5.5f - (f == 2 ? 0.5f : 0f), 0, 0, 1)), 4, 5f, true);
            art.Clips[(int)CharacterClip.Walk] = new SpriteClip(atlas.AddStrip(4, W, H, (c, f) =>
            {
                float hop = f switch { 0 => 0f, 1 => 2.5f, 2 => 3.5f, _ => 1f };
                Blob(c, f == 0 ? 9f : 7f, f == 0 ? 4.5f : 6f, hop, 0, 1);
            }), 4, 8f, true);
            art.Clips[(int)CharacterClip.Run] = new SpriteClip(atlas.AddStrip(4, W, H, (c, f) =>
            {
                float hop = f == 0 ? 0f : f == 1 ? 4f : f == 2 ? 2.5f : 1f;
                Blob(c, f == 0 ? 10f : f == 1 ? 6f : 8f, f == 0 ? 3.8f : 6.5f, hop, f == 1 ? 1f : 0f, 1);
            }), 4, 12f, true);
            art.Clips[(int)CharacterClip.Attack] = new SpriteClip(atlas.AddStrip(4, W, H, (c, f) =>
                Blob(c, f < 2 ? 7f : 10f, f < 2 ? 6.5f : 4f, f < 2 ? 0.5f : 0f, f < 2 ? -1.5f : 2.5f, 1)), 4, 12f, false);
            art.Clips[(int)CharacterClip.Cast] = art.Clips[(int)CharacterClip.Idle];
            art.Clips[(int)CharacterClip.AreaCast] = art.Clip(CharacterClip.Cast);
            art.Clips[(int)CharacterClip.Channel] = art.Clip(CharacterClip.Cast);
            art.Clips[(int)CharacterClip.SlamCast] = art.Clip(CharacterClip.Cast);
            art.Clips[(int)CharacterClip.Hit] = new SpriteClip(atlas.AddStrip(2, W, H, (c, f) => Blob(c, 10f - f, 3.8f + f * 0.6f, 0, -1, 0.4f)), 2, 10f, false);
            art.Clips[(int)CharacterClip.Death] = new SpriteClip(atlas.AddStrip(5, W, H, (c, f) =>
            {
                Blob(c, 8 + f * 1.4f, math.max(5.5f - f * 1.2f, 1f), 0, 0, f < 2 ? 0.5f : 0f);
                if (f == 4) c.Fade(0.7f);
            }), 5, 10f, false);
            return art;
        }

        // ---- Weapons (drawn pointing right, grip at the left) ----

        static WeaponArt Weapon(SpriteAtlasBuilder atlas, int w, int h, int grip, Action<PixelCanvas> draw)
        {
            var canvas = new PixelCanvas(w, h);
            draw(canvas);
            canvas.Outline(Outline);
            return new WeaponArt { Frame = atlas.Add(canvas), Size = new float2(w, h) / PixelsPerUnit, Grip = grip / PixelsPerUnit };
        }

        static void DrawSword(PixelCanvas c)
        {
            c.Rect(1, 3, 4, 2, C(110, 70, 40));
            c.Rect(5, 1, 2, 6, C(230, 190, 70));
            c.Rect(7, 3, 8, 2, C(215, 220, 235));
            c.Rect(7, 4, 8, 1, C(250, 250, 255));
            c.Set(15, 4, C(215, 220, 235));
        }

        static void DrawAxe(PixelCanvas c)
        {
            c.Rect(1, 4, 11, 2, C(120, 80, 45));
            c.Ellipse(11, 5, 3, 4.5f, C(190, 195, 205));
            c.Rect(8, 1, 3, 9, C(160, 165, 175));
        }

        static void DrawSpear(PixelCanvas c)
        {
            c.Rect(1, 3, 19, 1, C(130, 90, 50));
            c.Rect(1, 2, 19, 1, C(110, 75, 40));
            c.Line(new float2(19, 1.5f), new float2(24.5f, 3f), 2f, C(220, 225, 235));
            c.Line(new float2(19, 4.5f), new float2(24.5f, 3f), 2f, C(200, 205, 215));
        }

        static void DrawBow(PixelCanvas c)
        {
            for (int y = 1; y < 17; y++)
            {
                float t = (y - 9f) / 8f;
                int x = (int)math.round(5.5f - 3.5f * t * t);
                c.Set(x, y, C(140, 95, 50));
                c.Set(x - 1, y, C(110, 70, 35));
            }
            c.Rect(1, 1, 1, 16, C(230, 230, 220));
        }

        static void DrawStaff(PixelCanvas c)
        {
            c.Rect(1, 3, 13, 2, C(90, 60, 40));
            c.Ellipse(14.5f, 4, 3, 3, C(150, 70, 240));
            c.Ellipse(14, 4.5f, 1.3f, 1.3f, C(240, 210, 255));
        }

        static void DrawHammer(PixelCanvas c)
        {
            c.Rect(1, 5, 10, 2, C(100, 70, 45));
            c.Rect(10, 1, 6, 10, C(120, 120, 135));
            c.Rect(10, 1, 6, 2, C(170, 170, 185));
        }

        // ---- Tiles ----

        static void DrawFloor(PixelCanvas c, ref Unity.Mathematics.Random random)
        {
            c.Rect(0, 0, 24, 24, C(42, 39, 50));
            // Cobblestones.
            for (int s = 0; s < 6; s++)
            {
                float x = random.NextFloat(2, 22), y = random.NextFloat(2, 22);
                byte g = (byte)random.NextInt(46, 60);
                c.Ellipse(x, y, random.NextFloat(3, 5), random.NextFloat(2.5f, 4), new Color32(g, (byte)(g - 3), (byte)(g + 8), 255));
            }
            for (int d = 0; d < 6; d++) c.Set(random.NextInt(0, 24), random.NextInt(0, 24), C(30, 28, 36));
        }

        static void DrawWall(PixelCanvas c, ref Unity.Mathematics.Random random, int variant)
        {
            c.Rect(0, 0, 24, 24, C(78, 72, 88));
            for (int row = 0; row < 4; row++)
            {
                int y = row * 6;
                c.Rect(0, y, 24, 1, C(46, 42, 54));
                int offset = (row + variant) % 2 == 0 ? 0 : 6;
                for (int x = offset; x < 24; x += 12) c.Rect(x, y, 1, 6, C(46, 42, 54));
                c.Rect(0, y + 5, 24, 1, C(100, 94, 110));
            }
            for (int d = 0; d < 8; d++) c.Set(random.NextInt(0, 24), random.NextInt(0, 24), C(60, 56, 70));
        }

        // ---- Helpers ----

        static void DrawChest(PixelCanvas c, bool open)
        {
            c.Rect(1, 0, 16, 8, C(130, 80, 40));
            c.Rect(1, 3, 16, 1, C(90, 55, 25));
            c.Rect(0, 0, 1, 8, C(210, 170, 60)); c.Rect(17, 0, 1, 8, C(210, 170, 60));
            if (open)
            {
                c.Rect(2, 8, 14, 2, C(40, 25, 15));
                c.Rect(3, 8, 12, 1, C(255, 215, 80));
                c.Rect(1, 10, 16, 4, C(110, 65, 30));
                c.Rect(1, 13, 16, 1, C(210, 170, 60));
            }
            else
            {
                c.Rect(1, 8, 16, 5, C(150, 95, 50));
                c.Rect(1, 12, 16, 1, C(210, 170, 60));
                c.Rect(8, 6, 2, 4, C(240, 210, 90));
            }
            c.Outline(Outline);
        }

        static void DrawSpikes(PixelCanvas c, bool up)
        {
            c.Rect(1, 1, 18, 18, C(55, 52, 62));
            for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++)
            {
                int cx = 4 + x * 6, cy = 4 + y * 6;
                if (up)
                {
                    c.Line(new float2(cx - 1.5f, cy - 1), new float2(cx, cy + 3), 1f, C(210, 215, 225));
                    c.Line(new float2(cx + 1.5f, cy - 1), new float2(cx, cy + 3), 1f, C(170, 175, 190));
                }
                else c.Rect(cx - 1, cy - 1, 2, 2, C(25, 24, 30));
            }
        }

        static SpriteClip Strip(SpriteAtlasBuilder atlas, int count, int w, int h, float fps, bool loop, Action<PixelCanvas, int> draw) =>
            new SpriteClip(atlas.AddStrip(count, w, h, draw), count, fps, loop);

        static int Single(SpriteAtlasBuilder atlas, int w, int h, Action<PixelCanvas> draw)
        {
            var c = new PixelCanvas(w, h);
            draw(c);
            return atlas.Add(c);
        }

        /// <summary>Nearest-neighbour rotation of <paramref name="src"/> around <paramref name="pivot"/> into <paramref name="dst"/>.</summary>
        static void Rotate(PixelCanvas src, PixelCanvas dst, float2 pivot, float angle)
        {
            float cs = math.cos(-angle), sn = math.sin(-angle);
            for (int y = 0; y < dst.Height; y++)
            for (int x = 0; x < dst.Width; x++)
            {
                float2 d = new float2(x + 0.5f, y + 0.5f) - pivot;
                float2 s = pivot + new float2(d.x * cs - d.y * sn, d.x * sn + d.y * cs);
                dst.Set(x, y, src.Get((int)math.floor(s.x), (int)math.floor(s.y)));
            }
        }

        static float2 Dir(float a) => new float2(math.cos(a), math.sin(a));
        static Color32 C(int r, int g, int b, int a = 255) => new Color32((byte)r, (byte)g, (byte)b, (byte)a);
        static Color32 Shade(Color32 c, float f) => PixelCanvas.Shade(c, f);
    }
}
