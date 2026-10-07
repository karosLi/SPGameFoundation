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
    public class DamageNumberPoolTests
    {
        static EntityHandle Target(int index = 1, int generation = 1) => new EntityHandle(index, generation);
        static readonly float4 View = new float4(-8, -5, 8, 5);
        [Test]
        public void MergesActualFractionalSumOnlyForSameGenerationAndKindWithoutMovingOrExtendingLife()
        {
            var p = new DamageNumberPool();
            Assert.AreEqual(DamageNumberAdmission.Accepted, p.Emit(Target(), new float2(1, 2), .2f, false, 1));
            p.BeginFrame(.1f, 0);
            Assert.AreEqual(DamageNumberAdmission.Merged, p.Emit(Target(), new float2(8), .3f, false, 2));
            Assert.That(p.Read(0).Amount, Is.EqualTo(.5).Within(1e-6));
            Assert.AreEqual(1, DamageNumberPool.DisplayAmount(p.Read(0).Amount));
            Assert.AreEqual(new float2(1, 2), p.Read(0).Anchor); Assert.AreEqual(.1f, p.Read(0).Age);
            Assert.AreEqual(DamageNumberAdmission.Accepted, p.Emit(Target(1, 2), new float2(1), 2, false, 3));
            Assert.AreEqual(DamageNumberAdmission.Accepted, p.Emit(Target(), new float2(1), 2, true, 4));
            p.BeginFrame(.03f, 0);
            Assert.AreEqual(DamageNumberAdmission.Accepted, p.Emit(Target(), new float2(2), 2, false, 5));
            Assert.AreEqual(4, p.Active); p.BeginFrame(.6f, 0); Assert.AreEqual(3, p.Active);
            Assert.AreEqual(1, p.Stats.Merged);
        }
        [Test]
        public void BufferedHitsOutsideAuthoritativeWindowDoNotMergeWithinOneRenderFrame()
        {
            var p = new DamageNumberPool();
            p.Emit(Target(), float2.zero, 2, false, 1, 10, 1d / 30);
            Assert.AreEqual(DamageNumberAdmission.Merged, p.Emit(Target(), float2.zero, 3, false, 2, 13, 1d / 30));
            Assert.AreEqual(DamageNumberAdmission.Accepted, p.Emit(Target(), float2.zero, 4, false, 3, 14, 1d / 30));
            Assert.AreEqual(5, p.Read(0).Amount); Assert.AreEqual(4, p.Read(1).Amount);
        }
        [Test]
        public void CriticalReserveEvictsOldestNormalButNeverEqualPriorityAndDoesNotLoseGeneration()
        {
            var p = new DamageNumberPool(2);
            p.Emit(Target(1), float2.zero, 1, false, 1); p.Emit(Target(2), float2.zero, 2, false, 2);
            Assert.AreEqual(DamageNumberAdmission.Dropped, p.Emit(Target(3), float2.zero, 3, false, 3));
            Assert.AreEqual(DamageNumberAdmission.Accepted, p.Emit(Target(4), float2.zero, 4, true, 4));
            Assert.AreEqual(Target(2), p.Read(0).Target); Assert.AreEqual(Target(4), p.Read(1).Target);
            Assert.AreEqual(DamageNumberAdmission.Accepted, p.Emit(Target(5), float2.zero, 5, true, 5));
            Assert.AreEqual(DamageNumberAdmission.Dropped, p.Emit(Target(6), float2.zero, 6, true, 6));
            Assert.AreEqual(2, p.Stats.Evicted); Assert.AreEqual(2, p.Stats.Dropped); Assert.AreEqual(2, p.Stats.HighWater);
        }
        [Test]
        public void PerTargetAndGlobalAdmissionRemainBoundedAndNormalFloodLeavesCriticalSpace()
        {
            var p = new DamageNumberPool(); p.BeginFrame(0, 3);
            for (ulong i = 1; i <= 1000; i++) p.Emit(Target((int)i), float2.zero, 4, false, i);
            Assert.AreEqual(9, p.Active);
            Assert.AreEqual(DamageNumberAdmission.Accepted, p.Emit(Target(2000), float2.zero, 8, true, 1001));
            Assert.AreEqual(10, p.Active);
            var target = new DamageNumberPool(); target.BeginFrame(0, 3);
            target.Emit(Target(), float2.zero, 2, false, 1); target.BeginFrame(.13f, 3);
            target.Emit(Target(), float2.zero, 3, false, 2); target.BeginFrame(.13f, 3);
            Assert.AreEqual(DamageNumberAdmission.Dropped, target.Emit(Target(), float2.zero, 4, false, 3));
            Assert.AreEqual(DamageNumberAdmission.Accepted, target.Emit(Target(), float2.zero, 8, true, 4));
            Assert.AreEqual(2, target.Active); Assert.AreEqual(1, target.Stats.Evicted);
        }
        [Test]
        public void QualityDropReducesGlobalAndExistingTargetLimitsStably()
        {
            var p = new DamageNumberPool(); ulong sequence = 0;
            for (int round = 0; round < 4; round++)
            {
                p.BeginFrame(.13f, 0);
                for (int i = 0; i < 12; i++) p.Emit(Target(i), new float2(i, 0), 2, round == 3, ++sequence);
            }
            Assert.AreEqual(48, p.Active); p.BeginFrame(0, 3);
            Assert.LessOrEqual(p.Active, 20);
            int critical = 0;
            for (int i = 0; i < p.Active; i++)
            {
                int same = 0;
                for (int j = 0; j < p.Active; j++) if (p.Read(j).Target == p.Read(i).Target) same++;
                Assert.LessOrEqual(same, 2); if (p.Read(i).Critical) critical++;
            }
            Assert.AreEqual(12, critical); Assert.Greater(p.Stats.Evicted, 0);
        }
        [Test]
        public void SessionAndSameTickTimelineAndLevelChangesClearButPauseDoesNot()
        {
            var p = new DamageNumberPool(); var owner = new object();
            Assert.IsTrue(p.Bind(owner, 1, 1)); p.Emit(Target(), float2.zero, 3, false, 1);
            Assert.IsFalse(p.Bind(owner, 1, 1)); p.BeginFrame(0, 0); Assert.Zero(p.Read(0).Age);
            Assert.IsTrue(p.Bind(owner, 2, 1)); Assert.Zero(p.Active);
            p.Emit(Target(), float2.zero, 4, true, 1); Assert.IsTrue(p.Bind(owner, 2, 2)); Assert.Zero(p.Active);
            p.Emit(Target(), float2.zero, 4, true, 1); Assert.IsTrue(p.Bind(new object(), 2, 2)); Assert.Zero(p.Active);
        }
        [Test]
        public void InvalidAmountsAndDuplicatesCannotCreateOrAlterLabelsAndClippingIsExplicit()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new DamageNumberPool(0)); var p = new DamageNumberPool();
            Assert.AreEqual(DamageNumberAdmission.Dropped, p.Emit(default, float2.zero, 1, false, 1));
            Assert.AreEqual(DamageNumberAdmission.Dropped, p.Emit(Target(), new float2(float.NaN), 1, false, 2));
            Assert.AreEqual(DamageNumberAdmission.Dropped, p.Emit(Target(), float2.zero, float.PositiveInfinity, false, 3));
            Assert.AreEqual(DamageNumberAdmission.Dropped, p.Emit(Target(), float2.zero, 0, false, 4));
            Assert.AreEqual(DamageNumberAdmission.Accepted, p.Emit(Target(), float2.zero, 999999, false, 5));
            Assert.AreEqual(DamageNumberAdmission.Duplicate, p.Emit(Target(), float2.zero, 900, true, 5));
            Assert.AreEqual(DamageNumberAdmission.Merged, p.Emit(Target(), float2.zero, .1f, false, 6));
            Assert.AreEqual(DamageNumberPool.MaxDisplay, DamageNumberPool.DisplayAmount(p.Read(0).Amount));
            Assert.AreEqual(1, p.Stats.DisplayOverflows); Assert.AreEqual(1, p.Stats.Duplicates);
            p.BeginFrame(2, 0); Assert.AreEqual(DamageNumberAdmission.Duplicate, p.Emit(Target(), float2.zero, 1, false, 6));
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void DrawUsesCompletePrioritizedLabelsAndBoundedGlyphFillAndOverlap(int quality)
        {
            var atlas = new SpriteAtlasBuilder(); var font = new SpriteFont(atlas);
            using var sheet = atlas.Build(128);
            using var batch = new SpriteBatch(RenderTier.DataTexture, sheet.Texture, BlendKind.Translucent, 192);
            var p = new DamageNumberPool(); p.BeginFrame(0, quality);
            for (ulong i = 1; i <= 40; i++) p.Emit(Target((int)i), new float2((i % 5) * 1.5f - 3, (i / 5) * .4f - 3), 123456, false, i);
            p.Emit(Target(100), float2.zero, float.MaxValue, true, 41); p.Draw(batch, sheet, font, View);
            Assert.Greater(batch.Count, 0); Assert.AreEqual(p.Stats.Glyphs, batch.Count);
            Assert.LessOrEqual(batch.Count, p.Budget.Glyphs); Assert.LessOrEqual(p.Stats.ScreenArea, p.Budget.ScreenArea);
            Assert.GreaterOrEqual(p.Stats.Glyphs, 8, "the critical saturated complete label !999999+ is submitted first");
            Assert.Less(batch.Instances[0].Center.x, 0); Assert.Greater(p.Stats.GlyphHighWater, 0);
            batch.Clear(); p.Draw(batch, sheet, font, View); Assert.AreEqual(p.Stats.Glyphs, batch.Count);
        }
        [Test]
        public void TinyBatchNeverDrawsPartialCriticalNumber()
        {
            var atlas = new SpriteAtlasBuilder(); var font = new SpriteFont(atlas); using var sheet = atlas.Build(128);
            using var batch = new SpriteBatch(RenderTier.DataTexture, sheet.Texture, BlendKind.Translucent, 2);
            var p = new DamageNumberPool(); p.Emit(Target(), float2.zero, 123, true, 1); p.Draw(batch, sheet, font, View);
            Assert.Zero(batch.Count); Assert.AreEqual(1, p.Stats.GlyphDrops);
        }
        [Test]
        public void WarmedAdmissionMergeExpiryQualityAndPackingAllocateZeroWithControls()
        {
            var atlas = new SpriteAtlasBuilder(); var font = new SpriteFont(atlas); using var sheet = atlas.Build(128);
            using var batch = new SpriteBatch(RenderTier.DataTexture, sheet.Texture, BlendKind.Translucent, 192); batch.Warmup(192);
            var p = new DamageNumberPool(); ulong sequence = 0;
            Action body = () =>
            {
                for (int i = 0; i < 1000; i++)
                {
                    p.BeginFrame(.025f, i % 4); var target = Target(i % 7);
                    p.Emit(target, new float2(i % 7 - 3, 0), .4f, i % 3 == 0, ++sequence);
                    p.Emit(target, new float2(i % 7 - 3, 0), .3f, i % 3 == 0, ++sequence);
                    batch.Clear(); p.Draw(batch, sheet, font, View);
                }
            };
            body(); using var probe = new ManagedAllocationProbe(); var before = probe.Calibrate(); var sample = probe.Measure(body); var after = probe.Calibrate();
            TestContext.WriteLine($"Damage numbers: 1000 warmed update/merge/pack samples; {sample.Value} {sample.Metric}, gen0={sample.Collections}; empty/retained calibration before={before.Empty.Value}/{before.RetainedArrays.Value}, after={after.Empty.Value}/{after.RetainedArrays.Value}");
            Assert.AreEqual(0, sample.Value); Assert.Greater(p.Stats.Merged, 0);
        }
    }
}
