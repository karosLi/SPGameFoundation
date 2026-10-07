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
