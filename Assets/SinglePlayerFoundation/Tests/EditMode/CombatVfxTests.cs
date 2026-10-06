using System;
using NUnit.Framework;
using SPF.Testing;
using SPF.Presentation;
using SPF.Presentation.Combat;
using SPF.Presentation.Sprites;
using Unity.Mathematics;

namespace SPF.Tests.EditMode
{
    public class CombatVfxTests
    {
        static readonly float4 View = new float4(-10f,-10f,10f,10f);
        static readonly CombatVfxUv Uv = new CombatVfxUv { Core = new float4(0,0,1,1), Glow = new float4(0,0,1,1), Ring = new float4(0,0,1,1), Streak = new float4(0,0,1,1) };
        static VfxProfile Separate { get { var p = VfxProfile.Impact; p.MergeRadius = 0; p.MergeSeconds = 0; return p; } }

        [Test]
        public void CapacityRejectsEqualPriorityAndProtectsCriticalEffects()
        {
            var pool = new CombatVfxPool(2); var low = Separate; var high = VfxProfile.HeroHurt; high.MergeSeconds = 0;
            Assert.AreEqual(VfxAdmission.Accepted,pool.Emit(low,float2.zero,1));
            Assert.AreEqual(VfxAdmission.Accepted,pool.Emit(low,new float2(1,0),2));
            Assert.AreEqual(VfxAdmission.Dropped,pool.Emit(low,new float2(2,0),3));
            Assert.AreEqual(VfxAdmission.Accepted,pool.Emit(high,float2.zero,4));
            Assert.AreEqual(2,pool.Active); Assert.AreEqual(1,pool.Stats.Evicted);
            Assert.AreEqual(VfxAdmission.Dropped,pool.Emit(low,new float2(5,0),5));
        }
        [Test]
        public void ImpactFloodReservesAdmissionForCriticalFeedback()
        {
            var pool=new CombatVfxPool();pool.BeginFrame(0,3);
            for(int i=0;i<100;i++) pool.Emit(Separate,new float2(i,0),(ulong)i+1);
            Assert.AreEqual(VfxAdmission.Accepted,pool.Emit(VfxProfile.HeroHurt,float2.zero,999));
            Assert.LessOrEqual(pool.Stats.Accepted,pool.Budget.Emissions);
        }
        [TestCase(true)]
        [TestCase(false)]
        public void DuplicateOrRejectedPriorityTwoFloodCannotSpendHeroCriticalAttemptReserve(bool duplicates)
        {
            var pool = new CombatVfxPool(); pool.BeginFrame(0f, 3);
            var medium = VfxProfile.Destruction; medium.MergeSeconds = 0f;
            Assert.AreEqual(VfxAdmission.Accepted, pool.Emit(medium, float2.zero, 1));
            for (int i = 0; i < 48; i++)
                pool.Emit(medium, new float2(i + 1, 0), duplicates ? 1ul : (ulong)i + 2);
            Assert.Greater(pool.Stats.Dropped, 0);
            if (duplicates) Assert.Greater(pool.Stats.Duplicates, 0);
            Assert.AreEqual(VfxAdmission.Accepted, pool.Emit(VfxProfile.HeroHurt, float2.zero, 1000),
                "lower-priority attempts cannot consume the highest-priority attempt lane or final emission slot");
            Assert.LessOrEqual(pool.Stats.Accepted, pool.Budget.Emissions);
            using var batch = new SpriteBatch(RenderTier.DataTexture, null, BlendKind.Translucent, 384);
            pool.Draw(batch, Uv, View);
            Assert.LessOrEqual(batch.Count, pool.Budget.Sprites);
            Assert.LessOrEqual(pool.Stats.ScreenArea, pool.Budget.ScreenArea);
            Assert.AreEqual(new float2(0,0), batch.Instances[0].Center);
        }
        [Test]
        public void DuplicateAndSpatialMergeDoNotRestartLifetimeAndClearResetsHistory()
        {
            var pool = new CombatVfxPool(); var p = VfxProfile.Impact;
            Assert.AreEqual(VfxAdmission.Accepted,pool.Emit(p,float2.zero,42));
            Assert.AreEqual(VfxAdmission.Duplicate,pool.Emit(p,float2.zero,42));
            Assert.AreEqual(VfxAdmission.Merged,pool.Emit(p,new float2(0.1f),43));
            pool.BeginFrame(p.Duration + 0.01f,0); Assert.AreEqual(0,pool.Active); Assert.AreEqual(1,pool.Stats.Expired);
            Assert.AreEqual(VfxAdmission.Duplicate,pool.Emit(p,float2.zero,43),"bounded history covers merged keys after expiry");
            pool.Clear(); Assert.AreEqual(0,pool.Stats.Accepted);
            Assert.AreEqual(VfxAdmission.Accepted,pool.Emit(p,float2.zero,43));
        }
        [Test]
        public void EveryQualityEnforcesEmissionSpriteAndConservativeFillCaps()
        {
            for (int quality=0;quality<4;quality++)
            {
                var pool = new CombatVfxPool(128); var p = Separate;
                pool.BeginFrame(0f,quality);
                for(int i=0;i<200;i++) pool.Emit(p,new float2((i%10)*0.4f-2f,i/10*0.1f-1f),(ulong)i+1);
                Assert.LessOrEqual(pool.Active,pool.Budget.Active); Assert.LessOrEqual(pool.Stats.Accepted,pool.Budget.Emissions);
                Assert.Greater(pool.Stats.Dropped,0);
                using var batch = new SpriteBatch(RenderTier.DataTexture,null,BlendKind.Translucent,384);
                pool.Draw(batch,Uv,new float4(-2,-2,2,2));
                Assert.LessOrEqual(batch.Count,pool.Budget.Sprites); Assert.LessOrEqual(pool.Stats.ScreenArea,pool.Budget.ScreenArea);
                Assert.Greater(pool.Stats.SpriteDrops,0);
            }
        }
        [Test]
        public void QualityReductionEvictsLowestPriorityAndNeverExpandsPool()
        {
            var pool = new CombatVfxPool(); var p = Separate;
            for(int i=0;i<40;i++) pool.Emit(p,new float2(i,0),(ulong)i+1);
            pool.Emit(VfxProfile.HeroHurt,new float2(0,5),100);
            pool.BeginFrame(0,3); Assert.AreEqual(24,pool.Active); Assert.Greater(pool.Stats.Evicted,0);
            using var batch=new SpriteBatch(RenderTier.DataTexture,null,BlendKind.Translucent,384);
            pool.Draw(batch,Uv,View); Assert.AreEqual(new float2(0,5),batch.Instances[0].Center,"critical readable core is submitted first");
        }
        [Test]
        public void DeterministicSeedsAndPackedStreamsIgnoreOtherPoolsAndGlobalRandom()
        {
            var a = new CombatVfxPool(); var b = new CombatVfxPool(); var p = Separate;
            using var ba=new SpriteBatch(RenderTier.DataTexture,null,BlendKind.Translucent,384);
            using var bb=new SpriteBatch(RenderTier.DataTexture,null,BlendKind.Translucent,384);
            a.Emit(p,float2.zero,0x123456789ul); b.Emit(p,float2.zero,0x123456789ul);
            a.BeginFrame(0.04f,0); b.BeginFrame(0.04f,0); a.Draw(ba,Uv,View); b.Draw(bb,Uv,View);
            Assert.AreEqual(ba.Count,bb.Count); Assert.Greater(ba.Count,4);
            for(int i=0;i<ba.Count;i++) { Assert.AreEqual(ba.Instances[i].A,bb.Instances[i].A); Assert.AreEqual(ba.Instances[i].B,bb.Instances[i].B); }
            Assert.AreNotEqual(CombatVfxPool.VisualSeed(123),CombatVfxPool.VisualSeed(124));
        }
        [Test]
        public void ExpiryPausedTimeCullingAndInvalidInputsAreSafe()
        {
            Assert.Throws<ArgumentOutOfRangeException>(()=>new CombatVfxPool(0));
            var pool=new CombatVfxPool();var p=Separate;
            Assert.AreEqual(VfxAdmission.Dropped,pool.Emit(p,new float2(float.NaN),1));
            p.Duration=float.PositiveInfinity;Assert.AreEqual(VfxAdmission.Dropped,pool.Emit(p,float2.zero,2));
            pool.Emit(Separate,new float2(100),3); pool.BeginFrame(0,0);
            using var batch=new SpriteBatch(RenderTier.DataTexture,null,BlendKind.Translucent,384);
            pool.Draw(batch,Uv,View);Assert.AreEqual(0,batch.Count);Assert.AreEqual(1,pool.Active);
            pool.BeginFrame(10f,0);Assert.AreEqual(0,pool.Active);
        }
        [Test]
        public void WarmEmitMergeExpireAndPackAllocateNoManagedMemory()
        {
            var pool=new CombatVfxPool();var p=VfxProfile.Impact;
            using var batch=new SpriteBatch(RenderTier.DataTexture,null,BlendKind.Translucent,384);
            for(int i=0;i<16;i++) { pool.BeginFrame(0.05f,0);pool.Emit(p,float2.zero,(ulong)i+1);batch.Clear();pool.Draw(batch,Uv,View); }
            pool.BeginFrame(1f,0); // Expire warmup effects before the measured, spatially distinct sequence.
            int mergedBefore=pool.Stats.Merged;
            Action measured = () =>
            {
                for(int i=0;i<1000;i++)
                {
                    pool.BeginFrame(0.02f,i%4);pool.Emit(p,new float2(i%5,0),(ulong)i+100);
                    pool.Emit(p,new float2(i%5,0),(ulong)i+100);
                    pool.Emit(p,new float2(i%5+.05f,0),(ulong)i+10000);batch.Clear();pool.Draw(batch,Uv,View);
                }
            };
            using var probe = new ManagedAllocationProbe();
            var calibrationBefore = probe.Calibrate();
            var sample = probe.Measure(measured);
            var calibrationAfter = probe.Calibrate();
            TestContext.WriteLine($"Combat VFX, 1000 iterations after 16 warm-up iterations and expiry: {sample.Value} current-thread {sample.Metric}; independent process-wide gen0 collections={sample.Collections}; retained-array/empty calibration before={calibrationBefore.RetainedArrays.Value}/{calibrationBefore.Empty.Value}, after={calibrationAfter.RetainedArrays.Value}/{calibrationAfter.Empty.Value}.");
            Assert.AreEqual(0L, sample.Value, $"Warmed VFX emit/merge/expire/pack must allocate zero {sample.Metric}.");
            Assert.AreEqual(1000,pool.Stats.Merged-mergedBefore,"every measured iteration includes a genuine spatial merge");
        }
        [Test]
        public void AnalyticMasksHaveTransparentBordersPartialCoverageAndDistinctProfiles()
        {
            for(int kind=0;kind<4;kind++)
            {
                var mask=CombatVfxArt.Mask(kind);int partial=0,visible=0;
                for(int i=0;i<mask.Pixels.Length;i++){var c=mask.Pixels[i];if(c.a>0&&c.a<255)partial++;if(c.a>0)visible++;}
                Assert.Greater(partial,20);Assert.Greater(visible,80);Assert.AreEqual(0,mask.Get(0,0).a);
            }
        }
    }
}
