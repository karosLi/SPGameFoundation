using NUnit.Framework;
using SPF.Contracts;
using SPF.L2.AI;
using SPF.Testing;
using Unity.Mathematics;

namespace RpgFoundation.Tests
{
    public class RpgAiDecisionTests
    {
        // Frozen branch oracle from 1c9731a ActorSystems.cs; deliberately independent of the tree.
        internal static RpgCombatIntent Legacy(bool skill, bool ranged, bool sees, bool inRange, bool retreat)
        {
            if (skill) return RpgCombatIntent.Skill;
            var action = RpgCombatIntent.Hold;
            if (ranged)
            {
                if (retreat) action |= RpgCombatIntent.Retreat;
                else if (!inRange) action |= RpgCombatIntent.Approach;
                if (inRange && sees) action |= RpgCombatIntent.Attack;
            }
            else if (!inRange) action |= RpgCombatIntent.Approach;
            else action |= RpgCombatIntent.Attack;
            return action;
        }
        [Test] public void AllTacticalFactsPreserveSkillPriorityAndConcurrentRetreatAttack()
        {
            using var program = RpgDecisions.CreateProgram();
            ReactiveDecisionConformance.Verify(program, 5,
                facts => (int)Legacy((facts & 1) != 0, (facts & 2) != 0, (facts & 16) != 0, (facts & 8) != 0, (facts & 4) != 0),
                (uint facts, out DecisionResult trace) => (int)RpgDecisions.Select(program, facts, out trace));
        }
        [Test] public void PerceptionBoundaryFactsMatchTheOriginalStrictComparisons()
        {
            using var program = RpgDecisions.CreateProgram();
            foreach (float reach in new[] { .75f, 2f, 8f })
            foreach (float distance in new[] { 0f, reach * .45f - .00001f, reach * .45f, reach * .45f + .00001f, reach, reach + .00001f })
            for (int flags = 0; flags < 8; flags++)
            {
                bool skill = (flags & 1) != 0, ranged = (flags & 2) != 0, sees = (flags & 4) != 0;
                bool inRange = distance <= reach && (sees || distance < 1.5f);
                uint facts = RpgDecisions.Facts(skill, ranged, sees, inRange, distance, reach);
                Assert.AreEqual(Legacy(skill, ranged, sees, inRange, sees && distance < reach * .45f), RpgDecisions.Select(program, facts, out _));
            }
        }
        [Test] public void DefaultConfigurationKeepsDirectPolicyAndOptInIsBaked()
        {
            using var direct = new RpgTestWorld(start: false);
            using var tree = new RpgTestWorld(start: false, tweak: c => c.UseDecisionTree = true);
            Assert.IsFalse(direct.Config.UseDecisionTree); Assert.IsFalse(direct.Runtime.UseDecisionTree);
            Assert.IsTrue(tree.Runtime.UseDecisionTree);
        }
        [Test] public void InvalidProgramFailsToHoldWithoutAnAttack()
        {
            Assert.AreEqual(RpgCombatIntent.Hold, RpgDecisions.Select(default, 31, out var trace));
            Assert.AreEqual(DecisionStatus.InvalidProgram, trace.Status);
        }
        [Test] public void FullTicksMatchFrozenLegacyAndMidChaseRestoreRemainsByteExact()
        {
            using var a = new RpgTestWorld(seed: 23, runSeed: 61, tweak: c => { c.Hero.Health = 100000; c.UseDecisionTree = true; });
            using var b = new RpgTestWorld(seed: 23, runSeed: 61, tweak: c => { c.Hero.Health = 100000; c.UseDecisionTree = true; }, legacyAi: true);
            for (int tick = 0; tick < 180; tick++)
            {
                var input = new InputFrame { Move = new float2(tick % 40 < 20 ? .4f : -.4f, .15f), Held = 1u << RpgButton.Attack };
                a.Input(input); b.Input(input); a.Step(); b.Step();
                if (tick % 30 == 0) CollectionAssert.AreEqual(a.Session.CaptureSnapshot(), b.Session.CaptureSnapshot(), "tick=" + tick);
                if (tick == 89) b.Session.RestoreSnapshot(a.Session.CaptureSnapshot());
            }
            CollectionAssert.AreEqual(a.Session.CaptureSnapshot(), b.Session.CaptureSnapshot());
        }
    }
}
