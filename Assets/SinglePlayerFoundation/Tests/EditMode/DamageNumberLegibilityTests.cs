using System;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Presentation;
using SPF.Presentation.Combat;
using SPF.Presentation.Sprites;
using SPF.Testing;
using Unity.Mathematics;

namespace SPF.Tests.EditMode
{
    public class DamageNumberLegibilityTests
    {
        static readonly float4 View = new float4(-8, -5, 8, 5);
        [TestCase(RenderTier.GpuDriven, false)] [TestCase(RenderTier.DataTexture, false)]
        [TestCase(RenderTier.GpuDriven, true)] [TestCase(RenderTier.DataTexture, true)]
        public void ContactLabelsClearDenseHeadBarsAndRetainPopHeightWhileRising(RenderTier tier, bool critical)
        {
            var atlas = new SpriteAtlasBuilder(); var font = SpriteFont.CreateSmooth(atlas); using var sheet = atlas.Build(128);
            using var batch = new SpriteBatch(tier, sheet.Texture, BlendKind.Translucent, 192);
            var pool = new DamageNumberPool(); var layout = new DamageNumberLayout();
            var obstacle = new float4(-.6f, .8f, .6f, 2.36f);
            pool.Emit(new EntityHandle(2, 1), new float2(0, 1), 44, critical, 1);
            float previous = float.NegativeInfinity, birthHeight = 0;
            for (int i = 0; i < 3; i++)
            {
                if (i > 0) pool.BeginFrame(.09f, 0);
                layout.Begin(View); layout.ReserveActor(obstacle); batch.Clear(); pool.Draw(batch, sheet, font, View, layout: layout);
                Assert.Greater(batch.Count, 0); var glyph = batch.Instances[0];
                Assert.Greater(glyph.Center.y - glyph.Size.y * .5f, obstacle.w, "entire label clears the projected head/bar top");
                Assert.Greater(glyph.Center.y, previous, "pop shrink cannot pull a retained clearance downward");
                if (i == 0) birthHeight = glyph.Size.y;
                else Assert.Less(glyph.Size.y, birthHeight);
                previous = glyph.Center.y;
            }
            float originalHeight = 10 * (critical ? .028f * 2.15f : .025f * 1.18f);
            Assert.AreEqual(math.f16tof32(math.f32tof16(originalHeight)), birthHeight, "original size after the existing half-float sprite packing");
            Assert.AreEqual(new float2(0, 1), pool.Read(0).Anchor); Assert.AreEqual(44, pool.Read(0).Amount);
        }
        [Test]
        public void ClearedHeadReservationCannotPullDetachedLabelDownOrChangeItsHandle()
        {
            var atlas = new SpriteAtlasBuilder(); var font = SpriteFont.CreateSmooth(atlas); using var sheet = atlas.Build(128);
            using var batch = new SpriteBatch(RenderTier.DataTexture, sheet.Texture, BlendKind.Translucent, 192);
            var pool = new DamageNumberPool(); var layout = new DamageNumberLayout(); var target = new EntityHandle(2, 7);
            pool.Emit(target, new float2(0, 1), 44, true, 1); layout.Begin(View); layout.ReserveActor(new float4(-.6f, .8f, .6f, 2.43f));
            pool.Draw(batch, sheet, font, View, layout: layout); float first = batch.Instances[0].Center.y;
            pool.BeginFrame(.09f, 0); layout.Begin(View); batch.Clear(); pool.Draw(batch, sheet, font, View, layout: layout);
            Assert.Greater(batch.Instances[0].Center.y, first); Assert.AreEqual(target, pool.Read(0).Target);
            Assert.AreEqual(new float2(0, 1), pool.Read(0).Anchor); Assert.AreEqual(44, pool.Read(0).Amount);
        }
        [TestCase(RenderTier.GpuDriven)] [TestCase(RenderTier.DataTexture)]
        public void SafeAreaAndHudNeverReceivePartialLabelsAndOutsideHeaderRemainsUsable(RenderTier tier)
        {
            var atlas = new SpriteAtlasBuilder(); var font = SpriteFont.CreateSmooth(atlas); using var sheet = atlas.Build(128);
            using var batch = new SpriteBatch(tier, sheet.Texture, BlendKind.Translucent, 192);
            var pool = new DamageNumberPool(); var layout = new DamageNumberLayout {
                SafeViewport = new float4(.04f, .03f, .96f, .94f), HeaderViewport = new float4(.04f, .82f, .70f, .94f) };
            layout.Begin(View);
            pool.Emit(new EntityHandle(0, 1), new float2(-2, 3.3f), 44, true, 1);
            pool.Emit(new EntityHandle(1, 1), new float2(6, 3.3f), 20, false, 2);
            pool.Draw(batch, sheet, font, View, layout: layout);
            Assert.AreEqual(2, batch.Count, "obscured critical is withheld whole, ordinary label beside the header survives");
            Assert.Greater(batch.Instances[0].Center.x, 5);
            for (int i = 0; i < batch.Count; i++) { var g = batch.Instances[i]; Assert.IsTrue(layout.Allows(new float4(g.Center - g.Size * .5f, g.Center + g.Size * .5f))); }
        }
        [TestCase(RenderTier.GpuDriven)] [TestCase(RenderTier.DataTexture)]
        public void DenseNormalsCannotConsumeCriticalReservationOrCompleteGlyphBudget(RenderTier tier)
        {
            var atlas = new SpriteAtlasBuilder(); var font = SpriteFont.CreateSmooth(atlas); using var sheet = atlas.Build(128);
            using var batch = new SpriteBatch(tier, sheet.Texture, BlendKind.Translucent, 3);
            var pool = new DamageNumberPool(); var layout = new DamageNumberLayout(); layout.Begin(View);
            for (ulong i = 1; i <= 24; i++) pool.Emit(new EntityHandle((int)i, 1), new float2(0, 1), 20, false, i);
            pool.Emit(new EntityHandle(50, 1), new float2(0, 1), 44, true, 25);
            layout.ReserveActor(new float4(-.6f, .8f, .6f, 2.36f)); pool.Draw(batch, sheet, font, View, layout: layout);
            Assert.AreEqual(3, batch.Count); Assert.AreEqual(1, pool.Stats.Visible); Assert.Greater(pool.Stats.GlyphDrops, 0);
            Assert.Less(batch.Instances[0].Color.z, .4f); Assert.Greater(batch.Instances[0].Center.y, 2.36f);
        }
        [TestCase(720, 1280, 154f)] [TestCase(720, 1600, 154f)] [TestCase(720, 1600, 178f)]
        [TestCase(1280, 720, 115f)]
        public void TallSafeAreaHeaderReservationTracksActualReferencePixelOffsets(int width, int height, float bottom)
        {
            var safe = new UnityEngine.Rect(24, 30, width - 48, height - 100);
            var bounds = SPF.Shell.UI.MobileSafeArea.ChildViewport(safe, width, height,
                new float4(.015f, 1, .75f, 1), new float4(0, -bottom, 0, -20));
            float scale = width >= height ? height / 720f : width / 720f;
            Assert.That(bounds.y * height, Is.EqualTo(safe.yMax - bottom * scale).Within(.001f));
            Assert.That(bounds.w * height, Is.EqualTo(safe.yMax - 20 * scale).Within(.001f));
            Assert.That(bounds.x * width, Is.EqualTo(safe.x + safe.width * .015f).Within(.001f));
        }
        [Test]
        public void RepeatedPausedDrawsCannotLiftLabelsBeyondQuarterViewport()
        {
            var atlas = new SpriteAtlasBuilder(); var font = SpriteFont.CreateSmooth(atlas); using var sheet = atlas.Build(128);
            using var batch = new SpriteBatch(RenderTier.DataTexture, sheet.Texture, BlendKind.Translucent, 192);
            var pool = new DamageNumberPool(); var layout = new DamageNumberLayout(); layout.Begin(View);
            for (int i = 0; i < 6; i++) layout.ReserveActor(new float4(-.6f, -3.1f + i * 1.2f, .6f, -2f + i * 1.2f));
            pool.Emit(new EntityHandle(2, 1), new float2(0, -3), 44, true, 1);
            for (int i = 0; i < 10; i++)
            {
                batch.Clear(); pool.Draw(batch, sheet, font, View, layout: layout);
                Assert.LessOrEqual(pool.Read(0).PresentationLift, 10 * DamageNumberPool.MaxClearanceFraction);
                Assert.Zero(batch.Count); Assert.AreEqual(1, pool.Stats.ReservedDrops); Assert.Zero(pool.Read(0).Age); Assert.Zero(pool.Read(0).PresentationLift, "failed placement must not ratchet at frozen time");
            }
        }
        [Test]
        public void RenderedCameraTranslationAndFullWidthStatusStripAreReserved()
        {
            var go = new UnityEngine.GameObject("Damage projection test");
            try
            {
                var camera = go.AddComponent<UnityEngine.Camera>(); camera.orthographic = true; camera.orthographicSize = 5; camera.aspect = 1.6f;
                camera.transform.position = new UnityEngine.Vector3(.35f, -.22f, -50);
                var actual = DamageNumberLayout.RenderedView(camera, View);
                Assert.AreEqual(new float4(-7.65f, -5.22f, 8.35f, 4.78f), actual);
                var layout = new DamageNumberLayout { HeaderViewport = new float4(0, .82f, .75f, 1), StatusViewport = new float4(0, .992f, 1, 1) };
                layout.Begin(actual);
                Assert.IsFalse(layout.Allows(new float4(4.7f, 4.72f, 5.1f, 4.77f)), "XP strip protects the gap to the right of telemetry");
                Assert.IsTrue(layout.Allows(new float4(4.7f, 4.2f, 5.1f, 4.5f)), "free region below the XP strip remains usable");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
        [Test]
        public void ReservationOverflowIsBoundedConservativeAndHealthyBarRuleKeepsImportantCues()
        {
            var layout = new DamageNumberLayout(); layout.Begin(View);
            for (int i = 0; i < 1000; i++) layout.ReserveActor(new float4(-1, 0, 1, 1));
            Assert.AreEqual(256, layout.Actors); Assert.AreEqual(744, layout.ReservationOverflows);
            Assert.Greater(layout.ActorLift(new float4(-.2f, .5f, .2f, 1.1f)), 0);
            Assert.IsFalse(DamageNumberLayout.ShowHealthBar(100, 100)); Assert.IsTrue(DamageNumberLayout.ShowHealthBar(99, 100));
            Assert.IsTrue(DamageNumberLayout.ShowHealthBar(100, 100, true));
        }
        [Test]
        public void ReservedPackingRemainsZeroAllocationWithUnchangedCalibrationControls()
        {
            var atlas = new SpriteAtlasBuilder(); var font = SpriteFont.CreateSmooth(atlas); using var sheet = atlas.Build(128);
            using var batch = new SpriteBatch(RenderTier.DataTexture, sheet.Texture, BlendKind.Translucent, 192); batch.Warmup(192);
            var pool = new DamageNumberPool(); var layout = new DamageNumberLayout(); ulong sequence = 0;
            Action run = () => { for (int i = 0; i < 1000; i++) { pool.BeginFrame(.03f, i % 4); layout.Begin(View); layout.ReserveActor(new float4(-.5f, .8f, .5f, 2.36f)); pool.Emit(new EntityHandle(i % 8, 1), new float2(i % 8 - 4, 1), 44, i % 3 == 0, ++sequence); batch.Clear(); pool.Draw(batch, sheet, font, View, layout: layout); } };
            run(); using var probe = new ManagedAllocationProbe(); var before = probe.Calibrate(); var result = probe.Measure(run); var after = probe.Calibrate();
            Assert.Zero(before.Empty.Value); Assert.Greater(before.RetainedArrays.Value, 0); Assert.Zero(after.Empty.Value); Assert.Greater(after.RetainedArrays.Value, 0); Assert.Zero(result.Value);
            TestContext.WriteLine($"Reserved glyph packing 1000 calls: {result.Value} {result.Metric}; controls {before.Empty.Value}/{before.RetainedArrays.Value}, {after.Empty.Value}/{after.RetainedArrays.Value}");
        }
    }
}
