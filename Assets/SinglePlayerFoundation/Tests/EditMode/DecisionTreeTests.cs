using System;
using NUnit.Framework;
using SPF.L2.AI;
using SPF.Testing;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace SPF.Tests.EditMode
{
    public class DecisionTreeTests
    {
        static DecisionNode[] Policy() => new[] {
            DecisionNode.Branch(1, 2, 1, 2), DecisionNode.Leaf(10),
            DecisionNode.Branch(4, 0, 3, 4), DecisionNode.Leaf(20), DecisionNode.Leaf(30) };
        static int Legacy(uint facts) => (facts & 1) != 0 && (facts & 2) == 0 ? 10 : (facts & 4) != 0 ? 20 : 30;

        [Test] public void EveryFactCombinationMatchesOrderedLegacyPolicy()
        {
            using var nodes = DecisionTree.Create(Policy(), 7);
            var visited = new bool[nodes.Length];
            for (uint facts = 0; facts < 256; facts++)
            {
                var r = DecisionTree.Evaluate(nodes, facts);
                Assert.AreEqual(DecisionStatus.Success, r.Status); Assert.AreEqual(Legacy(facts), r.Action);
                Assert.LessOrEqual(r.Visits, nodes.Length); visited[r.Leaf] = true;
            }
            Assert.IsTrue(visited[1] && visited[3] && visited[4], "every leaf reached");
        }
        [Test] public void StartupRejectsInvalidProgramsBeforeNativeAllocation()
        {
            Assert.Throws<ArgumentException>(() => DecisionTree.Create(null, 7));
            Assert.Throws<ArgumentException>(() => DecisionTree.Create(new DecisionNode[0], 7));
            Assert.Throws<ArgumentException>(() => DecisionTree.Create(new DecisionNode[33], 7));
            Assert.Throws<ArgumentException>(() => DecisionTree.Create(new[] { DecisionNode.Leaf(-1) }, 7));
            Assert.Throws<ArgumentException>(() => DecisionTree.Create(new[] { DecisionNode.Leaf(1), DecisionNode.Leaf(2) }, 7));
            foreach (var node in new[] { DecisionNode.Branch(1, 0, 0, 1), DecisionNode.Branch(1, 0, 2, 1),
                DecisionNode.Branch(1, 1, 1, 1), DecisionNode.Branch(8, 0, 1, 1), DecisionNode.Branch(0, 0, 1, 1),
                new DecisionNode { Kind = (DecisionNodeKind)99 } })
                Assert.Throws<ArgumentException>(() => DecisionTree.Create(new[] { node, DecisionNode.Leaf(0) }, 7));
        }
        [Test] public void RuntimeFailuresAreExplicitAndBudgetIsBounded()
        {
            Assert.AreEqual(DecisionStatus.InvalidProgram, DecisionTree.Evaluate(default, 0).Status);
            using var nodes = DecisionTree.Create(Policy(), 7);
            foreach (int budget in new[] { -1, 0, 1 })
            {
                var r = DecisionTree.Evaluate(nodes, 1, budget);
                Assert.AreEqual(DecisionStatus.BudgetExceeded, r.Status); Assert.AreEqual(-1, r.Action);
                Assert.LessOrEqual(r.Visits, Math.Max(0, budget));
            }
            Assert.AreEqual(DecisionStatus.Success, DecisionTree.Evaluate(nodes, 1, 2).Status);
            var writable = nodes; writable[0] = DecisionNode.Branch(1, 0, 0, 2);
            var invalid = DecisionTree.Evaluate(nodes, 1); Assert.AreEqual(DecisionStatus.InvalidProgram, invalid.Status);
            Assert.AreEqual(-1, invalid.Action); Assert.AreEqual(1, invalid.Visits);
        }
        [Test] public void MaximumProgramTerminatesWithinTheDocumentedBound()
        {
            var data = new DecisionNode[32];
            for (int i = 0; i < 31; i++) data[i] = DecisionNode.Branch(1, 0, i + 1, 31);
            data[31] = DecisionNode.Leaf(123);
            using var nodes = DecisionTree.Create(data, 1);
            var r = DecisionTree.Evaluate(nodes, 1, int.MaxValue);
            Assert.AreEqual(123, r.Action); Assert.AreEqual(32, r.Visits);
            Assert.AreEqual(DecisionStatus.BudgetExceeded, DecisionTree.Evaluate(nodes, 1, 31).Status);
        }
        [BurstCompile(CompileSynchronously = true)]
        struct EvaluateJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<DecisionNode> Nodes;
            public NativeArray<DecisionResult> Results;
            public NativeArray<int> Backend;
            public void Execute(int i)
            {
                bool burst = true;
#if !SPF_DOTNET_HARNESS
                MarkManaged(ref burst);
#else
                burst = false;
#endif
                Backend[i] = burst ? 1 : 0;
                Results[i] = DecisionTree.Evaluate(Nodes, (uint)i);
            }
#if !SPF_DOTNET_HARNESS
            [BurstDiscard] static void MarkManaged(ref bool burst) => burst = false;
#endif
        }
        [TestCase(1)] [TestCase(16)] [TestCase(64)]
        public void ScheduledBurstMatchesSynchronousAndChecksActualBackend(int batch)
        {
            using var nodes = DecisionTree.Create(Policy(), 7);
            using var results = new NativeArray<DecisionResult>(256, Allocator.TempJob);
            using var backend = new NativeArray<int>(256, Allocator.TempJob);
            new EvaluateJob { Nodes = nodes, Results = results, Backend = backend }.Schedule(256, batch).Complete();
            for (int i = 0; i < results.Length; i++)
            {
                var expected = DecisionTree.Evaluate(nodes, (uint)i); var actual = results[i];
                Assert.AreEqual(expected.Action, actual.Action); Assert.AreEqual(expected.Status, actual.Status);
                Assert.AreEqual(expected.Leaf, actual.Leaf); Assert.AreEqual(expected.Visits, actual.Visits);
#if !SPF_DOTNET_HARNESS
                Assert.AreEqual(1, backend[i], "Scheduled evaluator must actually execute through native Burst; managed fallback is not native evidence.");
#endif
            }
        }
        [Test] public void WarmedEvaluationHasNoCalibratedManagedAllocations()
        {
            using var nodes = DecisionTree.Create(Policy(), 7);
            int checksum = 0;
            Action work = () => { for (uint i = 0; i < 4096; i++) checksum += DecisionTree.Evaluate(nodes, i).Action; };
            work();
            using var probe = new ManagedAllocationProbe(); var before = probe.Calibrate();
            var sample = probe.Measure(work); var after = probe.Calibrate();
            Assert.AreEqual(0, sample.Value, sample.Metric.ToString()); Assert.Greater(checksum, 0);
            TestContext.WriteLine($"Decision evaluator allocation: {sample.Value} {sample.Metric}; controls before={before.RetainedArrays.Value}/{before.Empty.Value}, after={after.RetainedArrays.Value}/{after.Empty.Value}. Current-thread evaluation only; excludes scheduler/other-thread/native allocations.");
        }
    }
}
