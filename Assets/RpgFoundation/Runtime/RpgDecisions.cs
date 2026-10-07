using SPF.L2.AI;
using Unity.Collections;

namespace RpgFoundation
{
    [System.Flags]
    public enum RpgCombatIntent : byte { Hold = 0, Approach = 1, Retreat = 2, Attack = 4, Skill = 8 }

    /// <summary>Pure tactical selection for the existing Chase/Attack FSM states. Home/wander, random
    /// draws, sight memory, combat timing and transitions remain owned by MonsterAISystem.</summary>
    public static class RpgDecisions
    {
        public const uint SkillReady = 1, Ranged = 2, Retreat = 4, InRange = 8, Visible = 16;
        public static NativeArray<DecisionNode> CreateProgram() => DecisionTree.Create(new[] {
            DecisionNode.Branch(SkillReady, 0, 7, 1),
            DecisionNode.Branch(Ranged, 0, 2, 6),
            DecisionNode.Branch(Retreat, 0, 3, 4),
            DecisionNode.Branch(InRange | Visible, 0, 8, 9),
            DecisionNode.Branch(InRange, 0, 5, 10),
            DecisionNode.Branch(Visible, 0, 11, 12),
            DecisionNode.Branch(InRange, 0, 11, 10),
            DecisionNode.Leaf((int)RpgCombatIntent.Skill),
            DecisionNode.Leaf((int)(RpgCombatIntent.Retreat | RpgCombatIntent.Attack)),
            DecisionNode.Leaf((int)RpgCombatIntent.Retreat),
            DecisionNode.Leaf((int)RpgCombatIntent.Approach),
            DecisionNode.Leaf((int)RpgCombatIntent.Attack),
            DecisionNode.Leaf((int)RpgCombatIntent.Hold),
        }, 31);

        public static uint Facts(bool skillReady, bool ranged, bool sees, bool inRange, float distance, float reach) =>
            (skillReady ? SkillReady : 0u) | (ranged ? Ranged : 0u) | (sees && distance < reach * .45f ? Retreat : 0u) |
            (inRange ? InRange : 0u) | (sees ? Visible : 0u);

        public static RpgCombatIntent Select(NativeArray<DecisionNode> program, uint facts, out DecisionResult trace)
        {
            trace = DecisionTree.Evaluate(program, facts);
            // Invalid data must not issue attacks/movement. The caller can inspect the explicit trace.
            return trace.Succeeded ? (RpgCombatIntent)trace.Action : RpgCombatIntent.Hold;
        }
    }
}
