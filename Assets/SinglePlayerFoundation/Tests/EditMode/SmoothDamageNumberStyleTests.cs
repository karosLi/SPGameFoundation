using System;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Presentation;
using SPF.Presentation.Combat;
using SPF.Presentation.Sprites;
using SPF.Testing;
using Unity.Mathematics;
using UnityEngine;

namespace SPF.Tests.EditMode
{
    public class SmoothDamageNumberStyleTests
    {
        [Test]
        public void EveryOriginalSmoothGlyphHasCoverageInkWhiteAndTransparentPadding()
        {
            var hashes = new uint[SpriteFont.Glyphs.Length];
            for (int glyph = 0; glyph < hashes.Length; glyph++)
            {
                var canvas = SmoothNumberGlyphs.Create(glyph); int partial = 0, ink = 0, white = 0; uint hash = 2166136261u;
                for (int y = 0; y < canvas.Height; y++) for (int x = 0; x < canvas.Width; x++)
                {
                    var c = canvas.Get(x, y);
                    if (x == 0 || y == 0 || x == canvas.Width - 1 || y == canvas.Height - 1) Assert.Zero(c.a, "atlas edge must remain transparent");
                    if (c.a > 0 && c.a < 255) partial++;
                    if (c.a == 255 && c.r < 30) ink++;
                    if (c.a == 255 && c.r > 240) white++;
                    unchecked { hash = (hash ^ c.a) * 16777619u; hash = (hash ^ c.r) * 16777619u; }
                }
                Assert.Greater(partial, 10); Assert.Greater(ink, 10); Assert.Greater(white, 10);
                for (int i = 0; i < glyph; i++) Assert.AreNotEqual(hashes[i], hash, "each glyph has its own authored silhouette");
                hashes[glyph] = hash;
            }
        }
        [Test]
        public void LegacyPixelConstructorDimensionsAndSmoothOptInStaySeparate()
        {
            var atlas = new SpriteAtlasBuilder(); var legacy = new SpriteFont(atlas); var smooth = SpriteFont.CreateSmooth(atlas);
            using var sheet = atlas.Build(256, FilterMode.Bilinear, 2, true);
            Assert.AreEqual(2, legacy.Scale); Assert.AreEqual(new int2(8, 12), sheet[legacy.Frame('0')].Pixels);
            Assert.AreEqual(8, smooth.Scale); Assert.AreEqual(new int2(26, 42), sheet[smooth.Frame('0')].Pixels);
            Assert.AreEqual(SpriteFont.Glyphs.Length * 2, atlas.Count);
            Assert.Throws<ArgumentOutOfRangeException>(() => SmoothNumberGlyphs.Create(-1));
        }
        [Test]
        public void CriticalPopSettlesQuicklyRisesMostlyVerticallyAndExpiresWithinPoint65Seconds()
        {
            var atlas = new SpriteAtlasBuilder(); var font = SpriteFont.CreateSmooth(atlas); using var sheet = atlas.Build(128);
            using var batch = new SpriteBatch(RenderTier.DataTexture, sheet.Texture, BlendKind.Translucent, 192);
            var pool = new DamageNumberPool(); var target = new EntityHandle(2, 1); var view = new float4(-8, -5, 8, 5);
            pool.Emit(target, float2.zero, 24422, true, 1); pool.Draw(batch, sheet, font, view);
            var first = batch.Instances[0]; float height = first.Size.y;
            pool.BeginFrame(.18f, 0); batch.Clear(); pool.Draw(batch, sheet, font, view); var settled = batch.Instances[0];
            Assert.Greater(height / settled.Size.y, 2.1f); Assert.Less(height / settled.Size.y, 2.2f);
            Assert.Greater(settled.Center.y, first.Center.y);
            Assert.AreEqual(float2.zero, pool.Read(0).Anchor, "pop/shrink never moves the accepted anchor");
            Assert.AreEqual(24422d, pool.Read(0).Amount); Assert.IsTrue(pool.Read(0).Critical);
            pool.BeginFrame(.48f, 0); Assert.Zero(pool.Active);
        }
        [TestCase(false)]
        [TestCase(true)]
        public void StackLanesRiseMonotonicallyWhileGlyphPopShrinks(bool critical)
        {
            var atlas = new SpriteAtlasBuilder(); var font = SpriteFont.CreateSmooth(atlas); using var sheet = atlas.Build(128);
            using var batch = new SpriteBatch(RenderTier.DataTexture, sheet.Texture, BlendKind.Translucent, 192);
            var pool = new DamageNumberPool(); var target = new EntityHandle(2, 1); var view = new float4(-8, -5, 8, 5);
            var previousY = new float[4]; float previousAge = 0;
            // Authoritative tick gaps prevent merge; spatially separated accepted anchors prevent
            // overlap reflow. Only the target identity is shared, exercising its actual four lanes.
            for (int lane = 0; lane < 4; lane++)
            {
                pool.Emit(target, new float2(-6 + lane * 4, 0), 1, critical, (ulong)lane + 1, lane * 8, 1d / 60);
                Assert.AreEqual(lane, pool.Read(lane).Lane);
            }
            int glyphsPerLabel = critical ? 2 : 1;
            var ages = new[] { 0f, .03f, .09f, .18f, .32f, .50f, .64f };
            for (int sample = 0; sample < ages.Length; sample++)
            {
                pool.BeginFrame(ages[sample] - previousAge, 0); batch.Clear(); pool.Draw(batch, sheet, font, view);
                Assert.AreEqual(4, pool.Stats.Visible); Assert.Zero(pool.Stats.OverlapDrops);
                Assert.AreEqual(4 * glyphsPerLabel, batch.Count);
                float layoutHeight = 10f * (critical ? .028f * 2.15f : .025f * 1.18f);
                float rise = ages[sample] * (critical ? 2.2f - .85f * ages[sample] : 1.1f);
                for (int lane = 0; lane < 4; lane++)
                {
                    float y = batch.Instances[lane * glyphsPerLabel].Center.y;
                    Assert.That(y, Is.EqualTo(.25f + layoutHeight * lane * 1.25f + rise).Within(1e-5),
                        "fixed peak-size lane placement confirms overlap reflow did not affect this sample");
                    if (sample > 0) Assert.Greater(y, previousY[lane], "shrinking glyphs must not pull an anchored stack lane downward");
                    Assert.AreEqual(new float2(-6 + lane * 4, 0), pool.Read(lane).Anchor);
                    previousY[lane] = y;
                }
                previousAge = ages[sample];
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FixedOverlapAttemptsRiseMonotonicallyWhileGlyphPopShrinks(bool critical)
        {
            var atlas = new SpriteAtlasBuilder(); var font = SpriteFont.CreateSmooth(atlas); using var sheet = atlas.Build(128);
            using var batch = new SpriteBatch(RenderTier.DataTexture, sheet.Texture, BlendKind.Translucent, 192);
            var pool = new DamageNumberPool(); var view = new float4(-8, -5, 8, 5);
            var previousY = new float[3]; float previousAge = 0;
            // Equal anchors and side drift keep the three distinct targets on attempts 0, 1, 2.
            // This is fixed-attempt placement, not a claim that intentional reflow is monotonic.
            for (int label = 0; label < 3; label++)
                pool.Emit(new EntityHandle(2 + label * 2, 1), float2.zero, 1, critical, (ulong)label + 1);
            int glyphsPerLabel = critical ? 2 : 1;
            var ages = new[] { 0f, .03f, .09f, .18f, .32f, .50f, .64f };
            for (int sample = 0; sample < ages.Length; sample++)
            {
                pool.BeginFrame(ages[sample] - previousAge, 0); batch.Clear(); pool.Draw(batch, sheet, font, view);
                Assert.AreEqual(3, pool.Stats.Visible); Assert.Zero(pool.Stats.OverlapDrops);
                Assert.AreEqual(3 * glyphsPerLabel, batch.Count);
                float layoutHeight = 10f * (critical ? .028f * 2.15f : .025f * 1.18f);
                float rise = ages[sample] * (critical ? 2.2f - .85f * ages[sample] : 1.1f);
                for (int label = 0; label < 3; label++)
                {
                    float y = batch.Instances[label * glyphsPerLabel].Center.y;
                    Assert.That(y, Is.EqualTo(.25f + layoutHeight * label * 1.15f + rise).Within(1e-5),
                        "the same bounded placement attempt stays selected throughout shrinking");
                    if (sample > 0) Assert.Greater(y, previousY[label], "shrinking glyphs must not lower a fixed fallback attempt");
                    previousY[label] = y;
                }
                previousAge = ages[sample];
            }
        }

        [Test]
        public void SmoothGlyphPackingKeepsZeroManagedAllocationAfterWarmup()
        {
            var atlas = new SpriteAtlasBuilder(); var font = SpriteFont.CreateSmooth(atlas); using var sheet = atlas.Build(128, FilterMode.Bilinear, 2, true);
            using var batch = new SpriteBatch(RenderTier.DataTexture, sheet.Texture, BlendKind.Translucent, 192); batch.Warmup(192);
            var pool = new DamageNumberPool(); ulong sequence = 0;
            Action run = () => { for (int i = 0; i < 1000; i++) { pool.BeginFrame(.03f, i % 4); pool.Emit(new EntityHandle(i % 8, 1), new float2(i % 8 - 4, 0), 12422, i % 3 == 0, ++sequence); batch.Clear(); pool.Draw(batch, sheet, font, new float4(-8, -5, 8, 5)); } };
            run(); using var probe = new ManagedAllocationProbe(); var before = probe.Calibrate(); var result = probe.Measure(run); var after = probe.Calibrate();
            Assert.Zero(before.Empty.Value); Assert.Greater(before.RetainedArrays.Value, 0);
            Assert.Zero(after.Empty.Value); Assert.Greater(after.RetainedArrays.Value, 0);
            Assert.Zero(result.Value); TestContext.WriteLine($"Smooth damage font1000 warmed pack samples: {result.Value} {result.Metric}; empty/positive={before.Empty.Value}/{before.RetainedArrays.Value}, {after.Empty.Value}/{after.RetainedArrays.Value}.");
        }
    }
}
