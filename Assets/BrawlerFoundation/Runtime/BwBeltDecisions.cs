using SPF.L2.AI;
using Unity.Collections;
using Unity.Mathematics;

namespace BrawlerFoundation
{
    public enum BwBeltIntent : byte { Hold, Approach, Attack }

    /// <summary>Reactive intent only. Hit/KO/attack locks, animation, cooldown advancement, facing,
    /// lane offsets and weapon timelines remain owned by BeltFighterSystem.</summary>
    public static class BwBeltDecisions
    {
        public const uint Eligible = 1, OutsideReach = 2, CooldownReady = 4;
        public static NativeArray<DecisionNode> CreateProgram() => DecisionTree.Create(new[] {
            DecisionNode.Branch(Eligible, 0, 1, 4),
            DecisionNode.Branch(OutsideReach, 0, 3, 2),
            DecisionNode.Branch(CooldownReady, 0, 5, 4),
            DecisionNode.Leaf((int)BwBeltIntent.Approach), DecisionNode.Leaf((int)BwBeltIntent.Hold),
            DecisionNode.Leaf((int)BwBeltIntent.Attack),
        }, 7);
        public static uint Facts(bool eligible, float2 delta, float reach, float cooldown) =>
            (eligible ? Eligible : 0u) | (math.abs(delta.x) > reach || math.abs(delta.y) > .38f ? OutsideReach : 0u) |
            (cooldown <= 0f ? CooldownReady : 0u);
        public static BwBeltIntent Select(NativeArray<DecisionNode> program, uint facts, out DecisionResult trace)
        {
            trace = DecisionTree.Evaluate(program, facts);
            return trace.Succeeded ? (BwBeltIntent)trace.Action : BwBeltIntent.Hold;
        }
    }
}
