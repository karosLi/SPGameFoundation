using NUnit.Framework;
using SPF.Contracts;
using SPF.L2.AI;
using Unity.Mathematics;

namespace BrawlerFoundation.Tests
{
    public class BwBeltAiTests
    {
        // Frozen policy from 1c9731a; no shared predicates with the evaluated tree.
        internal static BwBeltIntent Legacy(bool eligible, float2 delta, float reach, float cooldown) =>
            !eligible ? BwBeltIntent.Hold : math.abs(delta.x) > reach || math.abs(delta.y) > .38f ? BwBeltIntent.Approach :
            cooldown <= 0 ? BwBeltIntent.Attack : BwBeltIntent.Hold;
        [Test] public void AllFactsAndExactLaneReachCooldownBoundariesMatchLegacy()
        {
            using var nodes = BwBeltDecisions.CreateProgram();
            var seen = new bool[nodes.Length];
            for (uint facts = 0; facts < 8; facts++)
            {
                var selected = BwBeltDecisions.Select(nodes, facts, out var trace);
                Assert.AreEqual((facts & 1) == 0 ? BwBeltIntent.Hold : (facts & 2) != 0 ? BwBeltIntent.Approach : (facts & 4) != 0 ? BwBeltIntent.Attack : BwBeltIntent.Hold, selected);
                Assert.IsTrue(trace.Succeeded); seen[trace.Leaf] = true;
            }
            for (int i = 0; i < nodes.Length; i++) if (nodes[i].Kind == DecisionNodeKind.Leaf) Assert.IsTrue(seen[i]);
            foreach (float reach in new[] { .92f, 1.2f })
            foreach (float x in new[] { -reach-.00001f, -reach, 0, reach, reach+.00001f })
            foreach (float y in new[] { -.38001f, -.38f, 0, .38f, .38001f })
            foreach (float cooldown in new[] { -.00001f, 0, .00001f })
            foreach (bool eligible in new[] { false, true })
            {
                var delta = new float2(x, y);
                Assert.AreEqual(Legacy(eligible, delta, reach, cooldown), BwBeltDecisions.Select(nodes, BwBeltDecisions.Facts(eligible, delta, reach, cooldown), out _));
            }
        }
        [Test] public void InvalidProgramFailsToHold()
        {
            Assert.AreEqual(BwBeltIntent.Hold, BwBeltDecisions.Select(default, 7, out var trace));
            Assert.AreEqual(DecisionStatus.InvalidProgram, trace.Status);
        }
        [TestCase(FighterState.Hit)] [TestCase(FighterState.KO)] [TestCase(FighterState.Attack)]
        public void ActualFighterSystemDoesNotRestartAnAttackWhileLocked(FighterState state)
        {
            using var t = new BwTestWorld(belt: BwBeltConfig.Default);
            t.World.ClearLevel(); t.Game.Flow = BwFlow.Fighting;
            BwSpawner.Spawn(t.World, 0, float2.zero, 1, 0);
            BwSpawner.Spawn(t.World, 1, new float2(.8f, 0), -1, 2);
            var f = t.Info(1); f.State = state; f.StateTime = 0; f.Cooldown = 0; f.Attack = state == FighterState.Attack ? AttackKind.Kick : AttackKind.None;
            t.World.Column(BwKeys.Info).Set(1, f); t.Step();
            var after = t.Info(1); Assert.AreEqual(state, after.State); Assert.Greater(after.StateTime, 0); Assert.Less(after.Cooldown, 0);
        }
        [Test] public void ActualFullTicksMatchFrozenLegacyAndRestoreExactly()
        {
            using var a = new BwTestWorld(belt: BwBeltConfig.Default);
            using var b = new BwTestWorld(belt: BwBeltConfig.Default, legacyAi: true);
            for (int tick = 0; tick < 240; tick++)
            {
                var input = new InputFrame { Held = 1, Move = new float2(tick % 100 < 50 ? .45f : -.45f, tick % 80 < 40 ? .2f : -.2f) };
                a.Game.Input = b.Game.Input = input; a.Step(); b.Step();
                if (tick % 30 == 0) CollectionAssert.AreEqual(a.Session.CaptureSnapshot(), b.Session.CaptureSnapshot(), "tick=" + tick);
                if (tick == 119) b.Session.RestoreSnapshot(a.Session.CaptureSnapshot());
            }
            CollectionAssert.AreEqual(a.Session.CaptureSnapshot(), b.Session.CaptureSnapshot());
        }
    }
}
