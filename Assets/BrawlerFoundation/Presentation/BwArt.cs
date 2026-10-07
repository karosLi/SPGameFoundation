using System;
using SPF.Presentation.Animation;
using SPF.Presentation.Combat;
using SPF.Presentation.Sprites;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace BrawlerFoundation.Presentation
{
    /// <summary>
    /// Procedural cut-out parts (greyscale, tinted per skin through a palette) and the arena. The part list is the
    /// draw order: back limbs, then body and head, then front limbs.
    /// </summary>
    public sealed class BwArt : IDisposable
    {
        public SpriteSheet Sheet { get; private set; }
        public SpriteFont Font { get; private set; }
        public int Floor, Wall, Shadow, Bar, Spark, Star;
        public WeaponProjectileArt WeaponProjectiles;
        public SpriteClip Puff;
        public NativeArray<BoneAttachment> Parts;
        /// <summary>Per skin (0 = player, 1..3 enemies), per part: skin, shirt, trousers colours.</summary>
        public NativeArray<float4> Palette;
        public const int Skins = 4;

        static Color32 C(byte r, byte g, byte b, byte a = 255) => new Color32(r, g, b, a);
        static readonly Color32 Line = C(40, 40, 48);

        enum Part : byte { Skin, Shirt, Trousers, Shoe }

        static PixelCanvas SmoothStone(bool wall)
        {
            var c=new PixelCanvas(128,64);
            for(int y=0;y<64;y++)for(int x=0;x<128;x++)
            {
                int noise=((x*37+y*71)^(x*y*13))&3;
                c.Set(x,y,C((byte)(wall?52+noise:65+noise),(byte)(wall?70+noise:83+noise),(byte)(wall?85+noise:99+noise)));
            }
            c.Line(new float2(0,2),new float2(128,2),2,C(28,45,58));
            c.Line(new float2(2,0),new float2(2,64),2,C(29,45,57));
            c.Line(new float2(4,61),new float2(125,61),1,C(98,118,128));
            c.Line(new float2(127,4),new float2(127,60),1,C(81,102,113));
            c.Line(new float2(84,8),new float2(95,19),1,C(38,58,68));
            c.Line(new float2(95,19),new float2(100,31),1,C(38,58,68));
            return c;
        }

        public static BwArt Build(BwRig rig, bool smoothStage = false, bool includeWeaponProjectiles = false, bool includeDamageNumbers = false)
        {
            var art = new BwArt();
            var atlas = new SpriteAtlasBuilder();
            int Limb(int w, int h, bool end)
            {
                var c = new PixelCanvas(w, h);
                c.Rect(1, 1, w - 2, h - 2, C(235, 235, 235));
                c.Rect(1, 1, w - 2, 1, C(200, 200, 200));
                if (end) c.Ellipse(w - 3.5f, h * 0.5f, 3f, h * 0.5f, C(250, 250, 250));
                c.Outline(Line);
                return atlas.Add(c);
            }
            int arm = Limb(12, 6, false), fist = Limb(12, 6, true), thigh = Limb(16, 8, false), shin = Limb(16, 7, true);
            var torsoCanvas = new PixelCanvas(22, 16);
            torsoCanvas.Rect(1, 2, 20, 12, C(235, 235, 235));
            torsoCanvas.Rect(15, 2, 2, 12, C(205, 205, 205));
            torsoCanvas.Outline(Line);
            int torso = atlas.Add(torsoCanvas);
            var headCanvas = new PixelCanvas(13, 13);
            headCanvas.Ellipse(6.5f, 6.5f, 5.5f, 5.5f, C(240, 240, 240));
            headCanvas.Rect(2, 9, 8, 3, C(70, 60, 50));        // hair
            headCanvas.Rect(8, 6, 2, 2, C(20, 20, 20));        // eye (facing +x... drawn rotated with the bone)
            headCanvas.Outline(Line);
            int head = atlas.Add(headCanvas);
            var random = new Unity.Mathematics.Random(4);
            art.Floor = atlas.Add(smoothStage ? SmoothStone(false) : Fill(16, 16, c =>
            {
                c.Rect(0, 0, 16, 16, C(120, 100, 90));
                c.Rect(0, 15, 16, 1, C(150, 130, 115));
                for (int i = 0; i < 10; i++) c.Set(random.NextInt(16), random.NextInt(15), C(105, 88, 80));
            }));
            art.Wall = atlas.Add(smoothStage ? SmoothStone(true) : Fill(16, 16, c =>
            {
                c.Rect(0, 0, 16, 16, C(80, 72, 92));
                c.Rect(0, 7, 16, 1, C(62, 56, 72)); c.Rect(0, 15, 16, 1, C(62, 56, 72));
                c.Rect(7, 0, 1, 7, C(62, 56, 72)); c.Rect(3, 8, 1, 7, C(62, 56, 72)); c.Rect(12, 8, 1, 7, C(62, 56, 72));
            }));
            art.Shadow = atlas.Add(smoothStage ? BlobShadow.CreateCanvas() : Fill(16, 6, c => c.Ellipse(8, 3, 7.5f, 2.5f, C(0, 0, 0, 120))));
            art.Bar = atlas.Add(Fill(4, 4, c => c.Rect(0, 0, 4, 4, C(255, 255, 255))));
            art.Spark = atlas.Add(Fill(9, 9, c =>
            {
                c.Line(new float2(0, 4), new float2(8, 4), 1.5f, C(255, 255, 255));
                c.Line(new float2(4, 0), new float2(4, 8), 1.5f, C(255, 255, 255));
                c.Line(new float2(1, 1), new float2(7, 7), 1f, C(255, 240, 160));
                c.Line(new float2(1, 7), new float2(7, 1), 1f, C(255, 240, 160));
            }));
            art.Star = atlas.Add(Fill(7, 7, c => { c.Rect(3, 0, 1, 7, C(255, 230, 90)); c.Rect(0, 3, 7, 1, C(255, 230, 90)); c.Rect(2, 2, 3, 3, C(255, 240, 160)); }));
            art.Puff = new SpriteClip(atlas.AddStrip(4, 12, 12, (c, f) => c.Ellipse(6, 6, 2.5f + f * 1.2f, 2.5f + f * 1.2f, C(255, 255, 255, (byte)(220 - f * 50)))), 4, 16f, false);
            if (includeWeaponProjectiles) art.WeaponProjectiles = WeaponProjectileArt.AddTo(atlas);
            if (includeDamageNumbers) art.Font = SpriteFont.CreateSmooth(atlas);
            // Keep the opt-in catalog on bounded shelves rather than letting three small masks
            // double a mostly empty row's power-of-two width. Classic packing stays unchanged.
            art.Sheet = atlas.Build(maxWidth: includeWeaponProjectiles ? (smoothStage ? 512 : 256) : 1024,
                filterMode: smoothStage ? FilterMode.Bilinear : FilterMode.Point);

            // Parts, back to front: (bone, sprite, size along the bone, thickness, kind).
            var parts = new (int bone, int sprite, float length, float width, Part kind, float layer)[]
            {
                (rig.UpperB, arm, 0.36f, 0.16f, Part.Shirt, 0f),
                (rig.ForeB, fist, 0.36f, 0.15f, Part.Skin, 0.5f),
                (rig.ThighB, thigh, 0.5f, 0.24f, Part.Trousers, 1f),
                (rig.ShinB, shin, 0.5f, 0.21f, Part.Trousers, 1.5f),
                (rig.Torso, torso, 0.68f, 0.5f, Part.Shirt, 2f),
                (rig.Head, head, 0.36f, 0.36f, Part.Skin, 3f),
                (rig.ThighF, thigh, 0.5f, 0.24f, Part.Trousers, 4f),
                (rig.ShinF, shin, 0.5f, 0.21f, Part.Trousers, 4.5f),
                (rig.UpperF, arm, 0.36f, 0.16f, Part.Shirt, 5f),
                (rig.ForeF, fist, 0.36f, 0.15f, Part.Skin, 5.5f),
            };
            art.Parts = new NativeArray<BoneAttachment>(parts.Length, Allocator.Persistent);
            var bones = rig.Asset.Bones;
            for (int i = 0; i < parts.Length; i++)
            {
                var p = parts[i];
                float along = math.min(p.length, bones[p.bone].Length + 0.06f);
                art.Parts[i] = new BoneAttachment
                {
                    Bone = p.bone, Uv = art.Sheet[p.sprite].Uv, Offset = new float2(along * 0.5f - 0.02f, 0f),
                    Size = new float2(along + 0.06f, p.width), Layer = p.layer, Tint = new float4(1f),
                };
            }
            // Palettes: skin tone, shirt, trousers per look.
            var looks = new (float4 skin, float4 shirt, float4 trousers)[]
            {
                (new float4(0.95f, 0.78f, 0.62f, 1f), new float4(0.25f, 0.5f, 0.95f, 1f), new float4(0.2f, 0.22f, 0.35f, 1f)),
                (new float4(0.85f, 0.66f, 0.5f, 1f), new float4(0.85f, 0.25f, 0.2f, 1f), new float4(0.3f, 0.25f, 0.2f, 1f)),
                (new float4(0.7f, 0.52f, 0.4f, 1f), new float4(0.3f, 0.7f, 0.35f, 1f), new float4(0.25f, 0.25f, 0.25f, 1f)),
                (new float4(0.95f, 0.8f, 0.7f, 1f), new float4(0.6f, 0.3f, 0.75f, 1f), new float4(0.15f, 0.15f, 0.18f, 1f)),
            };
            art.Palette = new NativeArray<float4>(Skins * parts.Length, Allocator.Persistent);
            for (int s = 0; s < Skins; s++)
                for (int i = 0; i < parts.Length; i++)
                    art.Palette[s * parts.Length + i] = parts[i].kind switch
                    {
                        Part.Skin => looks[s].skin,
                        Part.Shirt => looks[s].shirt,
                        _ => looks[s].trousers,
                    };
            return art;
        }

        static PixelCanvas Fill(int w, int h, Action<PixelCanvas> draw)
        {
            var c = new PixelCanvas(w, h);
            draw(c);
            return c;
        }

        public void Dispose()
        {
            Sheet?.Dispose();
            Sheet = null;
            if (Parts.IsCreated) Parts.Dispose();
            if (Palette.IsCreated) Palette.Dispose();
        }
    }
}
