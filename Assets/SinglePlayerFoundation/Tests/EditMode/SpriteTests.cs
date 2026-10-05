using NUnit.Framework;
using SPF.Presentation;
using SPF.Presentation.Sprites;
using Unity.Mathematics;
using UnityEngine;

namespace SPF.Tests.EditMode
{
    public class SpriteTests
    {
        [Test]
        public void AtlasPacksFramesWithoutOverlap()
        {
            var builder = new SpriteAtlasBuilder();
            var random = new Unity.Mathematics.Random(3);
            for (int i = 0; i < 60; i++)
            {
                var canvas = new PixelCanvas(random.NextInt(4, 40), random.NextInt(4, 40));
                canvas.Rect(0, 0, 2, 2, new Color32(255, 0, 0, 255));
                builder.Add(canvas, i == 7 ? "seven" : null);
            }
            using var sheet = builder.Build(256);
            Assert.AreEqual(60, sheet.Count);
            Assert.AreEqual(7, sheet.Find("seven"));
            Assert.AreEqual(-1, sheet.Find("missing"));
            for (int a = 0; a < sheet.Count; a++)
            {
                var fa = sheet[a];
                int2 oa = sheet.Origins[a];
                Assert.IsTrue(math.all(oa >= 1) && math.all(oa + fa.Pixels <= sheet.Size), $"frame {a} inside the atlas");
                Assert.AreEqual((float)oa.x / sheet.Size.x, fa.Uv.x, 1e-6f);
                for (int b = a + 1; b < sheet.Count; b++)
                {
                    int2 ob = sheet.Origins[b];
                    var fb = sheet[b];
                    bool overlap = oa.x < ob.x + fb.Pixels.x + 1 && ob.x < oa.x + fa.Pixels.x + 1 && oa.y < ob.y + fb.Pixels.y + 1 && ob.y < oa.y + fa.Pixels.y + 1;
                    Assert.IsFalse(overlap, $"frames {a} and {b} overlap (or touch without padding)");
                }
            }
        }

        [Test]
        public void ClipsLoopClampAndFollowProgress()
        {
            var loop = new SpriteClip(10, 4, 8f, true);
            Assert.AreEqual(10, loop.FrameAt(0f));
            Assert.AreEqual(11, loop.FrameAt(0.13f));
            Assert.AreEqual(10, loop.FrameAt(0.5f), "wraps after 4 frames at 8 fps");
            var once = new SpriteClip(20, 3, 10f, false);
            Assert.AreEqual(22, once.FrameAt(5f), "clamps on the last frame");
            Assert.AreEqual(20, once.FrameAtProgress(0f));
            Assert.AreEqual(21, once.FrameAtProgress(0.5f));
            Assert.AreEqual(22, once.FrameAtProgress(1f));

            var animator = new SpriteAnimator();
            animator.Play(1);
            animator.Advance(0.2f);
            animator.Play(1);
            Assert.AreEqual(0.2f, animator.Time, 1e-6f, "same clip keeps running");
            animator.Play(2);
            Assert.AreEqual(0f, animator.Time, "new clip restarts");
            animator.Advance(0.31f);
            Assert.IsTrue(animator.Finished(once));
            Assert.IsFalse(animator.Finished(loop), "looping clips never finish");
        }

        [Test]
        public void FontDrawsNumbersAndEffectsExpire()
        {
            var builder = new SpriteAtlasBuilder();
            var font = new SpriteFont(builder, 2);
            using var sheet = builder.Build();
            Assert.AreEqual(SpriteFont.Glyphs.Length, sheet.Count);
            Assert.GreaterOrEqual(font.Frame('7'), 0);
            Assert.AreEqual(-1, font.Frame('?'));
            using var batch = new SpriteBatch(RenderTier.DataTexture, sheet.Texture, BlendKind.Translucent, 64);
            font.DrawNumber(batch, sheet, 1234, '-', '!', float2.zero, 0.5f, 0f, new float4(1));
            Assert.AreEqual(6, batch.Count, "'-', four digits, '!'");
            Assert.Less(math.cmax(math.abs(sheet[font.Frame('-')].Uv - batch.Instances[0].Uv)), 1e-4f);
            Assert.Less(math.cmax(math.abs(sheet[font.Frame('4')].Uv - batch.Instances[4].Uv)), 1e-4f, "digits in reading order");

            var effects = new SpriteEffects(4);
            var clip = new SpriteClip(0, 2, 10f, false);
            for (int i = 0; i < 6; i++)
                effects.Spawn(new SpriteEffects.Effect { Clip = clip, Size = new float2(1), Color = new float4(1) });
            batch.Clear();
            effects.UpdateAndDraw(0.05f, batch, sheet, font);
            Assert.AreEqual(4, effects.Active, "capacity bounds the pool (oldest replaced)");
            Assert.AreEqual(4, batch.Count);
            batch.Clear();
            effects.UpdateAndDraw(0.3f, batch, sheet, font);
            Assert.AreEqual(0, effects.Active, "one-shot effects expire after their clip");
        }

        [Test]
        public void CanvasOutlinesShapes()
        {
            var c = new PixelCanvas(8, 8);
            c.Rect(3, 3, 2, 2, new Color32(255, 255, 255, 255));
            c.Outline(new Color32(0, 0, 0, 255));
            Assert.AreEqual(255, c.Get(2, 3).a, "left of the shape is outlined");
            Assert.AreEqual(0, c.Get(2, 3).r);
            Assert.AreEqual(0, c.Get(2, 2).a, "diagonals stay empty (4-neighbour outline)");
            Assert.AreEqual(255, c.Get(3, 3).r, "shape itself untouched");
        }
    
        [Test]
        public void PackedSpritesRoundTripWithinPrecision()
        {
            var p = PackedSprite.Pack(new float2(1234.5f, -77.25f), new float2(-1.3f, 0.75f), new float4(0.125f, 0.5f, 0.0078125f, 0.015625f),
                -12.3456f, new float4(1.25f, 0.5f, 0.1f, 0.8f), 7f, 0.4f);
            Assert.AreEqual(new float2(1234.5f, -77.25f), p.Center, "positions are exact");
            Assert.AreEqual(-12.3456f, p.Depth, "depth is exact (sort order)");
            Assert.Less(math.cmax(math.abs(p.Size - new float2(-1.3f, 0.75f))), 1e-3f, "half sizes, mirror sign kept");
            Assert.Less(math.cmax(math.abs(p.Uv - new float4(0.125f, 0.5f, 0.0078125f, 0.015625f))), 1f / 65535f);
            Assert.Less(math.cmax(math.abs(p.Color - new float4(1.25f, 0.5f, 0.1f, 0.8f))), 1f / 127f, "tints up to 2x");
            Assert.AreEqual(7f - 2f * math.PI, p.Rotation, 2e-3f, "rotation wrapped to ±π");
            Assert.AreEqual(0.4f, p.Flash, 1f / 255f);
            Assert.AreEqual(32, System.Runtime.InteropServices.Marshal.SizeOf<PackedSprite>());
        }

        [Test]
        public void ReservedSlotsAreFilledByJobsAndTheFloatFallbackDecodes()
        {
            SpriteBatch.PackedTexturesOverride = false;
            try
            {
                using var batch = new SpriteBatch(RenderTier.DataTexture, null, BlendKind.Opaque, 8);
                Assert.IsFalse(batch.PackedTextures);
                Assert.AreEqual(64, batch.BytesPerInstance);
                batch.Add(float2.zero, new float2(1f), new float4(0f, 0f, 1f, 1f), 0f, new float4(1f));
                var slots = batch.Reserve(10);
                Assert.AreEqual(7, slots.Length, "clamped to capacity");
                Assert.AreEqual(8, batch.Count);
                for (int i = 0; i < slots.Length; i++) slots[i] = PackedSprite.Pack(new float2(i, 0f), new float2(1f), new float4(0f, 0f, 1f, 1f), 0f, new float4(1f));
                batch.Trim(5);
                Assert.AreEqual(5, batch.Count);
                Assert.AreEqual(new float2(3f, 0f), batch.Instances[4].Center);
            }
            finally { SpriteBatch.PackedTexturesOverride = null; }
            using var packed = new SpriteBatch(RenderTier.GpuDriven, null, BlendKind.Opaque, 8);
            Assert.AreEqual(32, packed.BytesPerInstance);
        }
    }
}
