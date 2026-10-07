using System;
using SPF.L2.AI;
using Unity.Collections;

namespace SPF.Testing
{
    /// <summary>
    /// Cold test fixture for the existing fact-mask reactive selector, not a gameplay backend API.
    /// Delegates and managed coverage arrays are deliberately outside every measured/hot path.
    /// The caller supplies an independent direct-policy oracle and its real module selector.
    /// </summary>
    public static class ReactiveDecisionConformance
    {
        public delegate int Select(uint facts, out DecisionResult trace);

        public static void Verify(NativeArray<DecisionNode> program, int factBits,
            Func<uint, int> direct, Select reactive)
        {
            if (factBits < 0 || factBits > 8) throw new ArgumentOutOfRangeException(nameof(factBits));
            if (!program.IsCreated) throw new ArgumentException("A validated module program is required.", nameof(program));
            if (direct == null || reactive == null) throw new ArgumentNullException();
            int count = 1 << factBits;
            var original = program.ToArray();
            var leaves = new bool[program.Length];
            var first = new DecisionResult[count];
            // Ascending, descending, then ascending again detects accidental retained selection state.
            for (int pass = 0; pass < 3; pass++)
            for (int i = 0; i < count; i++)
            {
                uint facts = (uint)(pass == 1 ? count - 1 - i : i);
                int selected = reactive(facts, out var trace);
                Require(trace.Succeeded && selected == direct(facts) && trace.Action == selected,
                    "Direct/reactive action differs", facts);
                Require(trace.Leaf >= 0 && trace.Leaf < program.Length &&
                    program[trace.Leaf].Kind == DecisionNodeKind.Leaf && program[trace.Leaf].Action == selected,
                    "Selected leaf is invalid", facts);
                Require(trace.Visits > 0 && trace.Visits <= program.Length && trace.Visits <= DecisionTree.MaxNodes,
                    "Visit count is not bounded", facts);
                leaves[trace.Leaf] = true;
                if (pass == 0) first[facts] = trace;
                else Require(Same(first[facts], trace), "Selection depends on previous facts", facts);

                var exact = DecisionTree.Evaluate(program, facts, trace.Visits);
                Require(Same(trace, exact), "Exact visit budget changes the selection", facts);
                var shortBudget = DecisionTree.Evaluate(program, facts, trace.Visits - 1);
                Require(shortBudget.Status == DecisionStatus.BudgetExceeded && shortBudget.Action == -1 &&
                    shortBudget.Leaf == -1 && shortBudget.Visits == trace.Visits - 1,
                    "Budget exhaustion masquerades as an action", facts);
                // Unused input facts cannot create hidden module state or alter a valid program.
                int withUnknown = reactive(facts | ~((uint)count - 1), out var unknown);
                Require(selected == withUnknown && Same(trace, unknown), "Unused input facts change selection", facts);
            }
            for (int i = 0; i < program.Length; i++)
            {
                var actual = program[i]; var before = original[i];
                if (actual.Kind == DecisionNodeKind.Leaf && !leaves[i])
                    throw new InvalidOperationException("Uncovered reactive leaf " + i);
                if (actual.Kind != before.Kind || actual.Required != before.Required || actual.Forbidden != before.Forbidden ||
                    actual.Pass != before.Pass || actual.Fail != before.Fail || actual.Action != before.Action)
                    throw new InvalidOperationException("Selector mutated shared program node " + i);
            }
        }

        static bool Same(DecisionResult a, DecisionResult b) => a.Status == b.Status && a.Action == b.Action &&
            a.Leaf == b.Leaf && a.Visits == b.Visits;
        static void Require(bool holds, string message, uint facts)
        {
            if (!holds) throw new InvalidOperationException(message + "; facts=" + facts);
        }
    }
}
