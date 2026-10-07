using System;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Contracts.Combat;
using SPF.L2.Combat;
using SPF.L2.Weapons;
using SPF.Presentation;
using SPF.Presentation.Combat;
using SPF.Testing;
using Unity.Mathematics;

namespace SPF.Tests.EditMode
{
    public class CombatCollisionPolicyTests
    {
        [Test]
        public void FiltersAndStableGenerationTiesAreExplicit()
        {
            var owner = new EntityHandle(1, 1); var target = new EntityHandle(2, 1);
            Assert.AreEqual(CombatContactReason.Self, CombatCollisionPolicy.Filter(owner, owner, 0, 1, false, false));
            Assert.AreEqual(CombatContactReason.Friendly, CombatCollisionPolicy.Filter(owner, target, 0, 0, false, false));
            Assert.AreEqual(CombatContactReason.Dead, CombatCollisionPolicy.Filter(owner, target, 0, 1, true, false));
            Assert.AreEqual(CombatContactReason.Invulnerable, CombatCollisionPolicy.Filter(owner, target, 0, 1, false, true));
            Assert.AreEqual(CombatContactReason.Candidate, CombatCollisionPolicy.Filter(owner, target, 0, 1, false, false));
            Assert.IsTrue(CombatCollisionPolicy.Before(.5f, new EntityHandle(2, 1), .5f, new EntityHandle(2, 2), true));
            Assert.IsFalse(CombatCollisionPolicy.Before(.5f, new EntityHandle(2, 2), .5f, new EntityHandle(2, 1), true));
            Assert.IsTrue(CombatCollisionPolicy.Before(.4f, new EntityHandle(9, 2), .5f, target, true));
        }
        [Test]
        public void TraceBudgetRejectsNewestAndClearDoesNotChangeEnabledPreference()
        {
            using var trace = new CombatTraceBuffer(2);
            trace.Begin(1); trace.Record(new CombatContactTrace { Reason = CombatContactReason.Accepted, DamageOutcome = CombatDamageOutcome.Applied }); Assert.AreEqual(0, trace.Count);
            trace.Enabled = true; trace.Begin(1);
            trace.Record(new CombatContactTrace { Scope = 1, Reason = CombatContactReason.GroundMiss });
            trace.Record(new CombatContactTrace { Scope = 2, Reason = CombatContactReason.Accepted, DamageOutcome = CombatDamageOutcome.Applied });
            trace.Record(new CombatContactTrace { Scope = 3, Reason = CombatContactReason.QueueFull });
            Assert.AreEqual(2, trace.Count); Assert.AreEqual(1, trace.Dropped); Assert.AreEqual(1, trace.Entries[0].Scope); Assert.AreEqual(2, trace.LastAccepted.Scope);
            trace.Begin(2); Assert.AreEqual(0, trace.Count); Assert.IsTrue(trace.HasAccepted);
            trace.Clear(); Assert.IsTrue(trace.Enabled); Assert.IsFalse(trace.HasAccepted); Assert.AreEqual(0, trace.Count);
        }
        [Test]
        public void DerivedBeltProfileUsesJointHeightIntervalAndNoToleranceInflation()
        {
            var profile = new ProjectileCollisionProfile { Radius = .25f, TargetGroundRadius = .25f, HurtBottom = 0, HurtTop = 1 };
            Assert.AreEqual(CombatBoundaryRule.Closed, profile.Boundary);
            Assert.IsTrue(profile.SweepGroundHeight(new float2(-1, 0), new float2(1, 0), 1,
                float2.zero, float2.zero, 2, 0, out float fraction));
            Assert.AreEqual(.375f, fraction);
            Assert.IsFalse(profile.SweepGroundHeight(new float2(-1, .5001f), new float2(1, .5001f), 1,
                float2.zero, float2.zero, 2, 0, out _));
        }
        [Test]
        public void TraceRecordingAndShapeSubmissionRemainBoundedAfterWarmup()
        {
            using var trace = new CombatTraceBuffer(16); trace.Enabled = true;
            long tick = 0;
            Action work = () => { for (int i = 0; i < 120; i++) { trace.Begin(++tick); for (int j = 0; j < 20; j++) trace.Record(new CombatContactTrace { Scope = j, Reason = CombatContactReason.Candidate }); } };
            work(); using var probe = new ManagedAllocationProbe(); var before = probe.Calibrate(); var sample = probe.Measure(work); var after = probe.Calibrate();
            TestContext.WriteLine("Trace current-thread " + sample.Metric + "=" + sample.Value + "; controls=" + before.RetainedArrays.Value + "/" + before.Empty.Value + "," + after.RetainedArrays.Value + "/" + after.Empty.Value);
            Assert.AreEqual(0, sample.Value); Assert.AreEqual(16, trace.Count); Assert.AreEqual(4, trace.Dropped);
        }
        [Test]
        public void DebugShapesNeverGrowPastFixedPrimitiveOrSegmentBudgets()
        {
            using var overlay = new CombatDebugOverlay(RenderTier.DataTexture);
            for (int i = 0; i < 300; i++) overlay.Add(new CombatDebugShape { Kind = CombatShapeKind.Capsule, Role = CombatShapeRole.Attack, A = new float2(i % 10, 0), B = new float2(i % 10, 1), Radius = .25f });
            Assert.AreEqual(CombatDebugOverlay.PrimitiveCapacity, overlay.Count); Assert.LessOrEqual(overlay.Segments, CombatDebugOverlay.SegmentCapacity); Assert.Greater(overlay.Dropped, 0);
            overlay.Clear(); Assert.AreEqual(0, overlay.Count); Assert.AreEqual(0, overlay.Segments);
        }
    }
}
