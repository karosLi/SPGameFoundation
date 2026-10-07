using System;
using Unity.Collections;

namespace SPF.L2.AI
{
    public enum DecisionNodeKind : byte { Branch, Leaf }
    public enum DecisionStatus : byte { Success, InvalidProgram, BudgetExceeded }

    /// <summary>One immutable instruction. Action IDs and fact bits belong to the consuming module.</summary>
    public struct DecisionNode
    {
        public DecisionNodeKind Kind;
        public uint Required, Forbidden;
        public int Pass, Fail, Action;
        public static DecisionNode Branch(uint required, uint forbidden, int pass, int fail) =>
            new DecisionNode { Kind = DecisionNodeKind.Branch, Required = required, Forbidden = forbidden, Pass = pass, Fail = fail, Action = -1 };
        public static DecisionNode Leaf(int action) => new DecisionNode { Kind = DecisionNodeKind.Leaf, Action = action };
    }

    public struct DecisionResult
    {
        public DecisionStatus Status;
        public int Action, Leaf, Visits;
        public bool Succeeded => Status == DecisionStatus.Success;
    }

    /// <summary>
    /// Bounded reactive decision selection, not a resumable task executor. Native nodes are shared by
    /// all agents of a policy; the owner allocates/disposes once. Facts are captured by the module and
    /// evaluation has no world access, random draws, timers, callbacks or other side effects.
    /// </summary>
    public static class DecisionTree
    {
        public const int MaxNodes = 32;

        /// <summary>Startup-only validation. Forward edges make cycles impossible and bound path length.
        /// No unused instructions or contradictory/unknown fact guards are accepted.</summary>
        public static NativeArray<DecisionNode> Create(DecisionNode[] nodes, uint knownFacts, Allocator allocator = Allocator.Persistent)
        {
            if (nodes == null || nodes.Length < 1 || nodes.Length > MaxNodes)
                throw new ArgumentException("A decision program must contain 1..32 nodes.", nameof(nodes));
            var reached = new bool[nodes.Length]; reached[0] = true;
            for (int i = 0; i < nodes.Length; i++)
            {
                var n = nodes[i];
                if (!reached[i]) throw new ArgumentException("Unreachable decision node.", nameof(nodes));
                if (n.Kind == DecisionNodeKind.Leaf)
                {
                    if (n.Action < 0 || n.Required != 0 || n.Forbidden != 0)
                        throw new ArgumentException("Invalid decision leaf.", nameof(nodes));
                }
                else if (n.Kind == DecisionNodeKind.Branch)
                {
                    if ((n.Required & n.Forbidden) != 0 || ((n.Required | n.Forbidden) & ~knownFacts) != 0 ||
                        (n.Required | n.Forbidden) == 0 || n.Pass <= i || n.Fail <= i || n.Pass >= nodes.Length || n.Fail >= nodes.Length)
                        throw new ArgumentException("Invalid decision guard or non-forward edge.", nameof(nodes));
                    reached[n.Pass] = reached[n.Fail] = true;
                }
                else throw new ArgumentException("Unknown decision instruction.", nameof(nodes));
            }
            var program = new NativeArray<DecisionNode>(nodes.Length, allocator);
            for (int i = 0; i < nodes.Length; i++) program[i] = nodes[i];
            return program;
        }

        /// <summary>At most min(maxVisits, 32) node reads, including the selected leaf. Errors never
        /// masquerade as an action. Callers must select their own safe fallback on a failed result.</summary>
        public static DecisionResult Evaluate(NativeArray<DecisionNode> nodes, uint facts, int maxVisits = MaxNodes)
        {
            var result = new DecisionResult { Status = DecisionStatus.InvalidProgram, Action = -1, Leaf = -1 };
            if (!nodes.IsCreated || nodes.Length < 1 || nodes.Length > MaxNodes) return result;
            int budget = maxVisits < MaxNodes ? maxVisits : MaxNodes;
            int index = 0;
            while (result.Visits < budget)
            {
                if (index < 0 || index >= nodes.Length) return result;
                var n = nodes[index]; result.Visits++;
                if (n.Kind == DecisionNodeKind.Leaf)
                {
                    if (n.Action < 0 || n.Required != 0 || n.Forbidden != 0) return result;
                    result.Status = DecisionStatus.Success; result.Action = n.Action; result.Leaf = index;
                    return result;
                }
                if (n.Kind != DecisionNodeKind.Branch || (n.Required & n.Forbidden) != 0 ||
                    (n.Required | n.Forbidden) == 0 || n.Pass <= index || n.Fail <= index || n.Pass >= nodes.Length || n.Fail >= nodes.Length)
                    return result;
                index = (facts & n.Required) == n.Required && (facts & n.Forbidden) == 0 ? n.Pass : n.Fail;
            }
            result.Status = DecisionStatus.BudgetExceeded;
            return result;
        }
    }
}
