using System;
using System.Reflection;
using NUnit.Framework;
using SPF.Testing;
using Unity.Collections;
using UnityEngine.Rendering;
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
        public void ReservedSlotsAreFilledByJobs()
        {
            using var batch = new SpriteBatch(RenderTier.DataTexture, null, BlendKind.Opaque, 8);
            Assert.AreEqual(32, batch.BytesPerInstance);
            batch.Add(float2.zero, new float2(1f), new float4(0f, 0f, 1f, 1f), 0f, new float4(1f));
            var slots = batch.Reserve(10);
            Assert.AreEqual(7, slots.Length, "clamped to capacity");
            Assert.AreEqual(8, batch.Count);
            for (int i = 0; i < slots.Length; i++) slots[i] = PackedSprite.Pack(new float2(i, 0f), new float2(1f), new float4(0f, 0f, 1f, 1f), 0f, new float4(1f));
            batch.Trim(5);
            Assert.AreEqual(5, batch.Count);
            Assert.AreEqual(new float2(3f, 0f), batch.Instances[4].Center);
        }

        [Test]
        public void DataTextureWarmupIsLazyBoundedAndIdempotent()
        {
            using var batch = new SpriteBatch(RenderTier.DataTexture, null, BlendKind.Opaque, SpriteBatch.PageSize * 2 + 13);
            batch.Add(new float2(3f, 7f), new float2(1f), new float4(0f, 0f, 1f, 1f), 0f, new float4(1f));
            var pages = PrivateArray(batch, "m_Pages");
            batch.Warmup(0);
            Assert.IsNull(pages.GetValue(0), "no pages allocated for an empty warmup");
            batch.Warmup(1);
            Assert.IsNotNull(pages.GetValue(0));
            Assert.IsNull(pages.GetValue(1), "unused pages stay lazy");
            Assert.IsNull(pages.GetValue(2));
            var textures = PrivateArray(pages.GetValue(0), "m_Textures");
            Assert.IsNotNull(textures.GetValue(0), "only the smallest texture is needed");
            Assert.IsNull(textures.GetValue(1));
            Assert.AreEqual(1, batch.Count, "warmup must not change the caller's contents");
            Assert.AreEqual(new float2(3f, 7f), batch.Instances[0].Center);
            Assert.AreEqual(0L, batch.BytesUploaded, "allocation alone is not an upload");

            batch.Warmup(int.MaxValue);
            var firstPage = pages.GetValue(0);
            var firstTexture = textures.GetValue(0);
            var lastPage = pages.GetValue(2);
            batch.Warmup(int.MaxValue);
            Assert.AreSame(firstPage, pages.GetValue(0));
            Assert.AreSame(firstTexture, textures.GetValue(0));
            Assert.AreSame(lastPage, pages.GetValue(2));
            Assert.AreEqual(1, batch.Count);
        }

        [TestCase(1)]
        [TestCase(63)]
        [TestCase(129)]
        [TestCase(256)]
        [TestCase(257)]
        [TestCase(513)]
        [TestCase(1025)]
        [TestCase(1300)]
        [TestCase(2049)]
        [TestCase(3073)]
        [TestCase(4096)]
        public void DataTextureCacheStaysBelowTwiceFullPageStorage(int capacity)
        {
            using var batch = new SpriteBatch(RenderTier.DataTexture, null, BlendKind.Opaque, capacity);
            batch.Warmup(capacity);
            var page = PrivateArray(batch, "m_Pages").GetValue(0);
            int total = 0;
            foreach (var cached in PrivateArray(page, "m_Textures"))
                if (cached != null) total += CachedTexture(cached).width * CachedTexture(cached).height * 4;
            int width = math.min(2048, math.ceilpow2(capacity * 8));
            int height = (capacity * 8 + width - 1) / width;
            Assert.Less(total, 2 * width * height * 4, "includes the awkward non-power-of-two final page");
        }

        static Array PrivateArray(object owner, string field) => (Array)PrivateField(owner, field);
        static object PrivateField(object owner, string field) =>
            owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
        static Texture2D CachedTexture(object cached) => (Texture2D)cached.GetType().GetProperty("Texture").GetValue(cached);

#if !SPF_DOTNET_HARNESS
        // The harness has no shaders and its textures do not retain pixel data. These assertions require
        // real Unity resources; do not let the no-op rendering stubs stand in for an upload/GC test.
        [Test]
        public void DataTextureUploadsSelectedPrefixesAndReusesThemAfterShrink()
        {
            using var batch = new SpriteBatch(RenderTier.DataTexture, null, BlendKind.Opaque, SpriteBatch.PageSize * 2);
            Assert.IsNotNull(batch.Material, "SpriteTex shader must be included");
            FillPackedSprites(batch);
            var bounds = new Bounds(Vector3.zero, new Vector3(100f, 100f, 100f));
            int[] counts = { 1, 256, 257, 512, 513, 1024, 1025, 2048, 2049, 4096, 4097, 8192, 7000, 513, 1, 0, 1 };
            for (int repeat = 0; repeat < 2; repeat++)
            {
                foreach (int count in counts)
                {
                    batch.Count = count;
                    batch.Draw(bounds);
                    long expected = 0;
                    for (int start = 0; start < count; start += SpriteBatch.PageSize)
                    {
                        int used = math.min(SpriteBatch.PageSize, count - start);
                        expected += math.max(DiscMesh.MinPrefix, math.ceilpow2(used)) * PackedSprite.Stride;
                        AssertTextureMatches(batch, start / SpriteBatch.PageSize, used);
                    }
                    Assert.AreEqual(expected, batch.BytesUploaded, $"full texture payload for {count} sprites");
                    batch.Draw(bounds, dirty: false);
                    Assert.AreEqual(0L, batch.BytesUploaded, "unchanged content must not upload");
                }
            }
            batch.Clear();
            batch.Draw(bounds, dirty: false);
            Assert.AreEqual(0L, batch.BytesUploaded);
            batch.Count = 1;
            batch.Draw(bounds, dirty: false);
            Assert.AreEqual(8192L, batch.BytesUploaded, "restoring a cleared batch must upload even without dirty");
        }

        [Test]
        public void DataTextureCountChangesUploadEvenWithoutDirtyAndIncludeFinalPagePadding()
        {
            using var batch = new SpriteBatch(RenderTier.DataTexture, null, BlendKind.Opaque, SpriteBatch.PageSize + 1300);
            FillPackedSprites(batch);
            var bounds = new Bounds(Vector3.zero, new Vector3(100f, 100f, 100f));
            int[] counts = { 4097, 4608, 4609, 5396, 4353, 4097 };
            long[] payloads = { 139264, 147456, 180224, 180224, 147456, 139264 };
            for (int i = 0; i < counts.Length; i++)
            {
                batch.Count = counts[i];
                batch.Draw(bounds, dirty: false);
                Assert.AreEqual(payloads[i], batch.BytesUploaded, "changed count forces upload, including padding");
                AssertTextureMatches(batch, 0, SpriteBatch.PageSize);
                AssertTextureMatches(batch, 1, counts[i] - SpriteBatch.PageSize);
            }
        }

        [Test]
        public void DataTextureWarmedCountChangesDoNotAllocate()
        {
            using var batch = new SpriteBatch(RenderTier.DataTexture, null, BlendKind.Opaque, SpriteBatch.PageSize + 1300);
            FillPackedSprites(batch);
            batch.Warmup(batch.Capacity);
            var bounds = new Bounds(Vector3.zero, new Vector3(100f, 100f, 100f));
            int[] counts = { 1, 256, 257, 513, 1025, 2049, 4096, 4097, 4609, 5396, 257, 0 };
            for (int i = 0; i < counts.Length * 2; i++)
            {
                batch.Count = counts[i % counts.Length];
                batch.Draw(bounds);
            }
            Action measured = () =>
            {
                for (int i = 0; i < counts.Length * 4; i++)
                {
                    batch.Count = counts[i % counts.Length];
                    batch.Draw(bounds);
                }
            };
            using var probe = new ManagedAllocationProbe();
            var calibrationBefore = probe.Calibrate();
            var sample = probe.Measure(measured);
            var calibrationAfter = probe.Calibrate();
            TestContext.WriteLine($"Data-texture sprite counts, 48 draws after 24 warm-up draws: {sample.Value} current-thread {sample.Metric}; independent process-wide gen0 collections={sample.Collections}; retained-array/empty calibration before={calibrationBefore.RetainedArrays.Value}/{calibrationBefore.Empty.Value}, after={calibrationAfter.RetainedArrays.Value}/{calibrationAfter.Empty.Value}.");
            Assert.AreEqual(0L, sample.Value, $"Cached textures, copies, and count changes must allocate zero {sample.Metric}.");
        }

        [Test]
        public void GpuSpriteUploadAccountingIncludesArgumentsAndResetsOnReuse()
        {
            if (RenderCapabilities.Detect() != RenderTier.GpuDriven)
                Assert.Ignore("GPU-driven rendering is not supported on this test device.");
            using var batch = new SpriteBatch(RenderTier.GpuDriven, null, BlendKind.Opaque, 16);
            FillPackedSprites(batch);
            var bounds = new Bounds(Vector3.zero, new Vector3(100f, 100f, 100f));
            batch.Count = 3;
            batch.Draw(bounds, dirty: false);
            Assert.AreEqual(3L * PackedSprite.Stride + GraphicsBuffer.IndirectDrawIndexedArgs.size, batch.BytesUploaded);
            batch.Draw(bounds, dirty: false);
            Assert.AreEqual(0L, batch.BytesUploaded);
            batch.Count = 2;
            batch.Draw(bounds, dirty: false);
            Assert.AreEqual(2L * PackedSprite.Stride + GraphicsBuffer.IndirectDrawIndexedArgs.size, batch.BytesUploaded);
            batch.Clear();
            batch.Draw(bounds);
            Assert.AreEqual(0L, batch.BytesUploaded);
        }

        static void FillPackedSprites(SpriteBatch batch)
        {
            var instances = batch.Instances;
            for (int i = 0; i < batch.Capacity; i++)
                instances[i] = PackedSprite.Pack(new float2(i + 0.25f, -i - 0.5f), new float2(-1.3f, 0.75f),
                    new float4(0.125f, 0.5f, 0.0078125f, 0.015625f), -12.3456f, new float4(1.25f, 0.5f, 0.1f, 0.8f), 7f, 0.4f);
        }

        static void AssertTextureMatches(SpriteBatch batch, int pageIndex, int used)
        {
            var page = PrivateArray(batch, "m_Pages").GetValue(pageIndex);
            var texture = CachedTexture(PrivateField(page, "m_ActiveTexture"));
            var actual = texture.GetPixelData<uint>(0);
            var expected = batch.Instances.Reinterpret<uint>(PackedSprite.Stride);
            for (int word = 0; word < used * 8; word++)
                Assert.AreEqual(expected[pageIndex * SpriteBatch.PageSize * 8 + word], actual[word], "packed bits must be exact");
            for (int slot = used; slot < actual.Length / 8; slot++)
                Assert.AreEqual(0u, actual[slot * 8 + 2], "unused slots collapse, including after switching cached sizes");
        }
#endif
    }
}
