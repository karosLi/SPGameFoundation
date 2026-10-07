using System;
using NUnit.Framework;
using SPF.Contracts;
using SPF.L2.Combat;
using SPF.Testing;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SPF.Tests.EditMode
{
    public class GroundProjectileSweepTests
    {
        struct Scenario
        {
            public float2 From, To, TargetFrom, TargetTo;
            public float HeightFrom, HeightTo, Radius, TargetHeightFrom, TargetHeightTo;
            public float TargetRadius, Bottom, Top;
            public bool Expected;
            public float Fraction;
        }

        static bool Sweep(in Scenario s, out float fraction) => GroundCombatQueries.SweepProjectile(
            s.From, s.To, s.HeightFrom, s.HeightTo, s.Radius, s.TargetFrom, s.TargetTo,
            s.TargetHeightFrom, s.TargetHeightTo, s.TargetRadius, s.Bottom, s.Top, out fraction);

        static Scenario Baseline() => new Scenario
        {
            From = new float2(-2f, 0f), To = new float2(2f, 0f),
            HeightFrom = -2f, HeightTo = 2f, TargetRadius = 1f, Top = 1f,
            Expected = true, Fraction = .5f,
        };

        [Test]
        public void FirstGroundContactHeightGateMissesLaterJointContact()
        {
            var s = Baseline();
            // Actual belt host stores one fixed projectile height and both target heights.
            s.HeightFrom = s.HeightTo = 1f;
            s.TargetHeightFrom = 2f;
            s.TargetHeightTo = 0f;
            s.Radius = .25f;
            s.TargetRadius = .75f;
            Assert.IsTrue(CombatSweep.Circles(s.From, s.To, s.Radius, s.TargetFrom,
                s.TargetTo, s.TargetRadius, out float firstGround));
            Assert.AreEqual(.25f, firstGround);
            float oldBottom = math.lerp(s.TargetHeightFrom, s.TargetHeightTo, firstGround) + s.Bottom;
            Assert.Less(s.HeightFrom + s.Radius, oldBottom,
                "Old first-ground-contact-only gate rejects this projectile.");
            Assert.IsTrue(Sweep(s, out float joint));
            Assert.AreEqual(.375f, joint, "Height enters while ground overlap is still active.");
        }

        [TestCase(-2f, 2f, .5f)]
        [TestCase(3f, -1f, .5f)]
        [TestCase(-3f, 1f, .75f)]
        [TestCase(4f, 0f, .75f)]
        public void ProjectileHeightCanEnterFromEitherSideIncludingGroundExit(float from, float to, float expected)
        {
            var s = Baseline(); s.HeightFrom = from; s.HeightTo = to;
            Assert.IsTrue(Sweep(s, out float fraction));
            Assert.AreEqual(expected, fraction);
        }

        [TestCase(-4f, 1f)]
        [TestCase(1f, -4f)]
        public void SeparateGroundAndHeightHitsWithDisjointTimesAreNotAHit(float from, float to)
        {
            var s = Baseline(); s.HeightFrom = from; s.HeightTo = to;
            Assert.IsTrue(CombatSweep.CircleContactInterval(s.From, s.To, s.Radius,
                s.TargetFrom, s.TargetTo, s.TargetRadius, out _, out _));
            Assert.IsFalse(Sweep(s, out _));
        }

        [Test]
        public void DoubleIntervalIntersectionDoesNotRoundARealGapIntoAHit()
        {
            var s = Baseline(); s.From = float2.zero; s.To = new float2(2f, 0f);
            s.HeightFrom = -1f; s.HeightTo = math.asfloat(math.asuint(1f) - 1u);
            double heightEntry = 1d / (1d + s.HeightTo);
            Assert.Greater(heightEntry, .5d);
            Assert.AreEqual(.5f, (float)heightEntry, "Float rounding would hide the gap.");
            Assert.IsTrue(CombatSweep.CircleContactInterval(s.From, s.To, 0f,
                float2.zero, float2.zero, 1f, out _, out double groundExit));
            Assert.AreEqual(.5d, groundExit);
            Assert.IsFalse(Sweep(s, out _));
        }

        [Test]
        public void BothBodiesMoveOnGroundAndInHeightAtNegativeCoordinates()
        {
            var s = Baseline();
            s.From = new float2(-3f, -4f); s.To = new float2(1f, -4f);
            s.TargetFrom = new float2(1f, -4f); s.TargetTo = new float2(-3f, -4f);
            s.HeightFrom = 0f; s.HeightTo = 2f;
            s.TargetHeightFrom = 1f; s.TargetHeightTo = 0f;
            Assert.IsTrue(Sweep(s, out float fraction));
            Assert.AreEqual(.375f, fraction);
        }

        [Test]
        public void EqualRelativeMotionPreservesStaticGroundAndVerticalContacts()
        {
            var s = Baseline(); s.From = new float2(-3f, -4f); s.To = new float2(7f, 6f);
            s.TargetFrom = s.From; s.TargetTo = s.To;
            s.HeightFrom = s.TargetHeightFrom = -2f;
            s.HeightTo = s.TargetHeightTo = 5f;
            Assert.IsTrue(Sweep(s, out float fraction)); Assert.AreEqual(0f, fraction);
            s.HeightFrom -= 1f; s.HeightTo -= 1f;
            Assert.IsFalse(Sweep(s, out _));
            s.HeightFrom += 1f; s.HeightTo += 1f;
            s.TargetFrom.x += 2f; s.TargetTo.x += 2f;
            Assert.IsFalse(Sweep(s, out _));
        }

        [Test]
        public void GroundTangencyAndItsAdjacentOutsideFloatHaveDifferentResults()
        {
            var s = Baseline(); s.From.y = s.To.y = 1f;
            Assert.IsTrue(Sweep(s, out float fraction)); Assert.AreEqual(.5f, fraction);
            s.From.y = s.To.y = math.asfloat(math.asuint(1f) + 1u);
            Assert.IsFalse(Sweep(s, out _));
            s.From.y = s.To.y = math.asfloat(math.asuint(1f) - 1u);
            Assert.IsTrue(Sweep(s, out fraction)); Assert.AreEqual(.5f, fraction);
        }

        [Test]
        public void VerticalTangencyAndItsAdjacentOutsideFloatHaveDifferentResults()
        {
            var s = Baseline(); s.From = s.To = float2.zero;
            s.Radius = .25f; s.HeightFrom = s.HeightTo = 1.25f;
            Assert.IsTrue(Sweep(s, out float fraction)); Assert.AreEqual(0f, fraction);
            s.HeightFrom = s.HeightTo = math.asfloat(math.asuint(1.25f) + 1u);
            Assert.IsFalse(Sweep(s, out _));
            s.HeightFrom = s.HeightTo = -.25f;
            Assert.IsTrue(Sweep(s, out fraction)); Assert.AreEqual(0f, fraction);
            s.HeightFrom = s.HeightTo = -math.asfloat(math.asuint(.25f) + 1u);
            Assert.IsFalse(Sweep(s, out _));
        }

        [Test]
        public void ZeroLengthGroundSweepStillFindsLaterVerticalContact()
        {
            var s = Baseline(); s.From = s.To = float2.zero;
            Assert.IsTrue(Sweep(s, out float fraction)); Assert.AreEqual(.5f, fraction);
            s.TargetFrom = s.TargetTo = new float2(2f, 0f);
            Assert.IsFalse(Sweep(s, out _));
        }

        [Test]
        public void ZeroSizeBodiesAndZeroWidthHurtIntervalHaveClosedInstantContact()
        {
            var s = Baseline(); s.TargetRadius = s.Top = 0f;
            Assert.IsTrue(Sweep(s, out float fraction)); Assert.AreEqual(.5f, fraction);
            s.HeightFrom = s.HeightTo = math.asfloat(1u);
            Assert.IsFalse(Sweep(s, out _));
        }

        [Test]
        public void InvalidGroundOrHurtGeometryIsRejected()
        {
            var s = Baseline(); s.Radius = -1f; Assert.IsFalse(Sweep(s, out _));
            s.Radius = 0f; s.TargetRadius = -1f; Assert.IsFalse(Sweep(s, out _));
            s.TargetRadius = 1f; s.Bottom = 2f; Assert.IsFalse(Sweep(s, out _));
        }

        [TestCase(1e-30f)]
        [TestCase(1e-15f)]
        [TestCase(1f)]
        [TestCase(1e15f)]
        [TestCase(1e30f)]
        public void CircleIntervalsAndJointTimesAreScaleIndependent(float scale)
        {
            float2 from = new float2(-4f * scale, 0f), to = new float2(4f * scale, 0f);
            Assert.IsTrue(CombatSweep.CircleContactInterval(from, to, 0f,
                float2.zero, float2.zero, scale, out double entry, out double exit));
            Assert.AreEqual(.375d, entry, 1e-14d); Assert.AreEqual(.625d, exit, 1e-14d);
            Assert.IsTrue(GroundCombatQueries.SweepProjectile(from, to, -2f * scale, 2f * scale,
                0f, float2.zero, float2.zero, 0f, 0f, scale, 0f, scale, out float joint));
            Assert.AreEqual(.5f, joint);
        }

        [Test]
        public void RelativeSubnormalMotionHasNoWorldUnitCutoff()
        {
            float tiny = math.asfloat(1u);
            AssertInterval(new float2(-4f * tiny, 0f), new float2(4f * tiny, 0f),
                0f, float2.zero, float2.zero, tiny, .375d, .625d);
        }

        [Test]
        public void HighSpeedOpposingMotionReturnsEntryAndExit()
        {
            AssertInterval(new float2(-1000000f, -32f), new float2(1000000f, -32f), 1f,
                new float2(1000000f, -32f), new float2(-1000000f, -32f), 1f,
                .4999995d, .5000005d);
        }

        [Test]
        public void CircleIntervalClipsInitialOverlapAndBothClosedEndpoints()
        {
            AssertInterval(float2.zero, new float2(4f, 0f), 0f, float2.zero, float2.zero, 1f, 0d, .25d);
            AssertInterval(new float2(-4f, 0f), float2.zero, 0f, float2.zero, float2.zero, 1f, .75d, 1d);
            AssertInterval(new float2(1f, 0f), new float2(2f, 0f), 0f, float2.zero, float2.zero, 1f, 0d, 0d);
            AssertInterval(new float2(-2f, 0f), new float2(-1f, 0f), 0f, float2.zero, float2.zero, 1f, 1d, 1d);
            AssertInterval(new float2(-2f, 1f), new float2(2f, 1f), 0f, float2.zero, float2.zero, 1f, .5d, .5d);
            AssertInterval(new float2(1f, 0f), new float2(1f, 1f), 0f, float2.zero, float2.zero, 1f, 0d, 0d);
        }

        [Test]
        public void StationaryCircleOverlapSpansTickAndOutsideMisses()
        {
            AssertInterval(float2.zero, float2.zero, 0f, float2.zero, float2.zero, 0f, 0d, 1d);
            AssertInterval(new float2(1f, 0f), new float2(1f, 0f), 0f,
                float2.zero, float2.zero, 1f, 0d, 1d);
            Assert.IsFalse(CombatSweep.CircleContactInterval(new float2(2f, 0f), new float2(2f, 0f),
                0f, float2.zero, float2.zero, 1f, out _, out _));
        }

        [Test]
        public void ZeroRadiusAndNegativeRadiusClampMatchCircleContract()
        {
            AssertInterval(new float2(-2f, 0f), new float2(2f, 0f), -1f,
                float2.zero, float2.zero, -1f, .5d, .5d);
            AssertInterval(float2.zero, new float2(1f, 0f), 0f,
                float2.zero, float2.zero, 0f, 0d, 0d);
        }

        [Test]
        public void CircleIntervalMissesContactsOutsideTickAndMovingAway()
        {
            Assert.IsFalse(CombatSweep.CircleContactInterval(new float2(2f, 0f), new float2(3f, 0f),
                0f, float2.zero, float2.zero, 1f, out _, out _));
            Assert.IsFalse(CombatSweep.CircleContactInterval(new float2(-3f, 0f), new float2(-2f, 0f),
                0f, float2.zero, float2.zero, 1f, out _, out _));
        }

        static void AssertInterval(float2 from, float2 to, float radius, float2 targetFrom,
            float2 targetTo, float targetRadius, double expectedEntry, double expectedExit)
        {
            Assert.IsTrue(CombatSweep.CircleContactInterval(from, to, radius, targetFrom,
                targetTo, targetRadius, out double entry, out double exit));
            Assert.AreEqual(expectedEntry, entry, 1e-14d);
            Assert.AreEqual(expectedExit, exit, 1e-14d);
        }

        static Scenario[] SentinelCases()
        {
            var cases = new Scenario[12];
            for (int i = 0; i < cases.Length; i++) cases[i] = Baseline();
            cases[1].HeightFrom = cases[1].HeightTo = 1f;
            cases[1].TargetHeightFrom = 2f; cases[1].TargetHeightTo = 0f;
            cases[2].HeightFrom = 3f; cases[2].HeightTo = -1f;
            cases[3].HeightFrom = -4f; cases[3].HeightTo = 1f; cases[3].Expected = false;
            cases[4].HeightFrom = 1f; cases[4].HeightTo = -4f; cases[4].Expected = false;
            cases[5].HeightFrom = -3f; cases[5].HeightTo = 1f; cases[5].Fraction = .75f;
            cases[6].From.y = cases[6].To.y = 1f;
            cases[7].From.y = cases[7].To.y = math.asfloat(math.asuint(1f) + 1u); cases[7].Expected = false;
            cases[8].From = cases[8].To = float2.zero;
            cases[9].TargetRadius = cases[9].Top = 0f;
            cases[10].From = float2.zero; cases[10].To = new float2(2f, 0f);
            cases[10].HeightFrom = -1f; cases[10].HeightTo = math.asfloat(math.asuint(1f) - 1u);
            cases[10].Expected = false;
            cases[11].From = new float2(-3f, -4f); cases[11].To = new float2(1f, -4f);
            cases[11].TargetFrom = new float2(1f, -4f); cases[11].TargetTo = new float2(-3f, -4f);
            cases[11].HeightFrom = 0f; cases[11].HeightTo = 2f; cases[11].TargetHeightFrom = 1f;
            cases[11].Fraction = .375f;
            return cases;
        }

        struct Probe
        {
            [ReadOnly] public NativeArray<Scenario> Cases;
            public NativeArray<int> Hits, Sentinel;
            public NativeArray<float> Fractions;
            public void Execute()
            {
                int burst = 1; MarkManaged(ref burst); Sentinel[0] = burst;
                for (int i = 0; i < Cases.Length; i++)
                {
                    Hits[i] = Sweep(Cases[i], out float fraction) ? 1 : 0;
                    Fractions[i] = fraction;
                }
            }
        }

        [BurstCompile(FloatMode = FloatMode.Strict, CompileSynchronously = true)]
        struct StrictProbeJob : IJob
        {
            public Probe Data;
            public void Execute() => Data.Execute();
        }

        [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
        struct FastProbeJob : IJob
        {
            public Probe Data;
            public void Execute() => Data.Execute();
        }

#if !SPF_DOTNET_HARNESS
        [BurstDiscard]
#endif
        static void MarkManaged(ref int burst) { burst = 0; }

        [TestCase(false)]
        [TestCase(true)]
        public void ScheduledJointSweepsUseStrictOrFastBurstWithIndependentExpectedResults(bool fast)
        {
            Scenario[] expected = SentinelCases();
            using var cases = new NativeArray<Scenario>(expected.Length, Allocator.TempJob);
            using var hits = new NativeArray<int>(expected.Length, Allocator.TempJob);
            using var fractions = new NativeArray<float>(expected.Length, Allocator.TempJob);
            using var sentinel = new NativeArray<int>(1, Allocator.TempJob);
            for (int i = 0; i < expected.Length; i++) cases.Set(i, expected[i]);
            var probe = new Probe { Cases = cases, Hits = hits, Fractions = fractions, Sentinel = sentinel };
            if (fast) new FastProbeJob { Data = probe }.Schedule().Complete();
            else new StrictProbeJob { Data = probe }.Schedule().Complete();
#if !SPF_DOTNET_HARNESS
            if (BurstCompiler.IsEnabled) Assert.AreEqual(1, sentinel[0], "Enabled Burst did not execute the joint-sweep job.");
#endif
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.AreEqual(expected[i].Expected ? 1 : 0, hits[i], "case=" + i + "; fast=" + fast);
                if (expected[i].Expected) Assert.AreEqual(expected[i].Fraction, fractions[i], "case=" + i);
            }
        }

        [Test]
        public void WarmedJointSweepsAndCircleIntervalsAllocateNoManagedMemory()
        {
            Scenario[] cases = SentinelCases(); int hits = 0;
            Action measured = () =>
            {
                for (int repeat = 0; repeat < 256; repeat++)
                for (int i = 0; i < cases.Length; i++)
                {
                    if (Sweep(cases[i], out _)) hits++;
                    Scenario s = cases[i];
                    if (CombatSweep.CircleContactInterval(s.From, s.To, s.Radius,
                        s.TargetFrom, s.TargetTo, s.TargetRadius, out _, out _)) hits++;
                }
            };
            measured(); measured();
            using var probe = new ManagedAllocationProbe();
            var before = probe.Calibrate(); var sample = probe.Measure(measured); var after = probe.Calibrate();
            TestContext.WriteLine($"Joint sweeps + intervals, 3072 each after 6144 warm-up calls each: {sample.Value} current-thread {sample.Metric}; independent process-wide gen0 collections={sample.Collections}; retained-array/empty controls before={before.RetainedArrays.Value}/{before.Empty.Value}, after={after.RetainedArrays.Value}/{after.Empty.Value}.");
            Assert.AreEqual(0, sample.Value); Assert.Greater(hits, 0);
        }
    }
}
