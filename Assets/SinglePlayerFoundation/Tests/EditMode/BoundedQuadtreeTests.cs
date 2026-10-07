using System;
using System.Collections.Generic;
using NUnit.Framework;
using SPF.Contracts;
using SPF.L1.Spatial;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SPF.Tests.EditMode
{
    public class BoundedQuadtreeTests
    {
        struct Collect : IGridVisitor
        {
            public List<int> Hits;
            public bool Visit(in GridEntry entry) { Hits.Add(entry.Owner); return true; }
        }

        struct StopAfter : IGridVisitor
        {
            public List<int> Hits;
            public int Limit;
            public bool Visit(in GridEntry entry) { Hits.Add(entry.Owner); return Hits.Count < Limit; }
        }

        [TestCase(1, 12)]
        [TestCase(5, 12)]
        [TestCase(127, 12)]
        [TestCase(0, 12)]
        [TestCase(0, 0)]
        [TestCase(0, 1)]
        public void RandomizedQueriesMatchBruteForceAndMeasuredCounts(int nodeCapacity, int maxDepth)
        {
            using var tree = new BoundedQuadtreeReference(new float2(-64), new float2(128), 512,
                bucketSize: 8, maxDepth: maxDepth, nodeCapacity: nodeCapacity);
            var staging = tree.Staging;
            var random = new Unity.Mathematics.Random(9876);
            var source = new GridEntry[512];
            int accepted = 0;
            for (int i = 0; i < source.Length; i++)
            {
                source[i] = new GridEntry
                {
                    Position = random.NextFloat2(new float2(-70), new float2(70)),
                    Radius = i % 29 == 0 ? 90f : random.NextFloat(0f, 3f), Owner = i,
                };
                staging[i] = source[i];
                if (tree.AsReader().Covers(source[i].Position)) accepted++;
            }
            tree.Build(source.Length);
            Assert.AreEqual(accepted, tree.EntryCount);
            Assert.AreEqual(source.Length - accepted, tree.RejectedCount);
            Assert.LessOrEqual(tree.NodeCount, tree.NodeCapacity);
            Assert.LessOrEqual(tree.Depth, maxDepth);
            if (nodeCapacity == 1 || maxDepth == 0) Assert.Greater(tree.SaturatedSplits, 0);

            var reader = tree.AsReader();
            for (int query = 0; query < 128; query++)
            {
                float2 center = random.NextFloat2(new float2(-140), new float2(140));
                float radius = random.NextFloat(0f, 15f);
                AssertMatches(reader, source, center, radius, "random query " + query);
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(12)]
        public void DepthExhaustionKeepsCoincidentEntriesInStagingOrder(int maxDepth)
        {
            using var tree = new BoundedQuadtreeReference(new float2(-8), new float2(16), 96,
                bucketSize: 1, maxDepth: maxDepth, nodeCapacity: 64);
            var staging = tree.Staging;
            for (int i = 0; i < staging.Length; i++)
                staging[i] = new GridEntry { Position = float2.zero, Radius = 1, Owner = i };
            tree.Build(staging.Length);
            Assert.AreEqual(96, tree.EntryCount);
            Assert.AreEqual(0, tree.RejectedCount);
            Assert.AreEqual(maxDepth, tree.Depth);
            Assert.AreEqual(1, tree.SaturatedSplits);
            var visitor = new Collect { Hits = new List<int>() };
            Assert.IsTrue(tree.AsReader().Query(float2.zero, 1, ref visitor));
            Assert.AreEqual(96, visitor.Hits.Count);
            for (int i = 0; i < visitor.Hits.Count; i++) Assert.AreEqual(i, visitor.Hits[i]);
        }

        [Test]
        public void NodeBudgetExhaustionKeepsEveryAcceptedEntry()
        {
            using var tree = new BoundedQuadtreeReference(float2.zero, new float2(16), 64,
                bucketSize: 1, maxDepth: 12, nodeCapacity: 1);
            var staging = tree.Staging;
            for (int i = 0; i < staging.Length; i++)
                staging[i] = new GridEntry { Position = new float2(i % 8, i / 8), Radius = 1, Owner = i };
            tree.Build(staging.Length);
            Assert.AreEqual(1, tree.NodeCount);
            Assert.AreEqual(1, tree.SaturatedSplits);
            Assert.AreEqual(64, tree.EntryCount);
            Assert.AreEqual(0, tree.RejectedCount);
            var visitor = new Collect { Hits = new List<int>() };
            var stats = new GridQueryStats();
            Assert.IsTrue(tree.AsReader().QueryMeasured(new float2(8), 100, ref visitor, ref stats));
            Assert.AreEqual(64, visitor.Hits.Count);
            Assert.AreEqual(1, stats.CellsVisited);
            Assert.AreEqual(64, stats.EntriesExamined);
            Assert.AreEqual(64, stats.VisitorCalls);
        }

        [Test]
        public void FloatPrecisionLimitedSplitKeepsItsOversizedLeaf()
        {
            // Adjacent representable coordinates at this magnitude differ by two. The midpoint
            // rounds back to the lower bound, even though the constructor's window is nonempty.
            float2 origin = new float2(16777216f);
            using var tree = new BoundedQuadtreeReference(origin, new float2(2), 4,
                bucketSize: 1, maxDepth: 128, nodeCapacity: 64);
            var staging = tree.Staging;
            for (int i = 0; i < staging.Length; i++)
                staging[i] = new GridEntry { Position = origin, Radius = 1, Owner = i };
            tree.Build(4);
            Assert.AreEqual(4, tree.EntryCount);
            Assert.AreEqual(1, tree.NodeCount);
            Assert.AreEqual(0, tree.Depth);
            Assert.AreEqual(1, tree.SaturatedSplits);
            var visitor = new Collect { Hits = new List<int>() };
            tree.AsReader().Query(origin, 1, ref visitor);
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, visitor.Hits);
        }

        [Test]
        public void HalfOpenWindowSplitBoundariesAndStrictTangencyMatchTheOracle()
        {
            var source = new[]
            {
                Entry(-8, -8, 1, 0), Entry(NextDown(8), NextDown(8), 1, 1),
                Entry(8, 0, 1, 2), Entry(0, 8, 1, 3), Entry(NextDown(-8), 0, 1, 4),
                Entry(0, 0, 1, 5), Entry(0, 4, 0, 6), Entry(4, 0, 1, 7),
                Entry(-4, 0, 12, 8), Entry(float.NaN, 0, 1, 9),
            };
            using var tree = new BoundedQuadtreeReference(new float2(-8), new float2(16), source.Length,
                bucketSize: 1, nodeCapacity: 128);
            var staging = tree.Staging;
            for (int i = 0; i < source.Length; i++) staging[i] = source[i];
            tree.Build(source.Length);
            Assert.AreEqual(6, tree.EntryCount);
            Assert.AreEqual(4, tree.RejectedCount);
            var reader = tree.AsReader();
            AssertMatches(reader, source, new float2(2, 0), 1, "exact tangent excluded");
            AssertMatches(reader, source, new float2(NextDown(2), 0), 1, "just inside tangent");
            AssertMatches(reader, source, new float2(NextUp(2), 0), 1, "just outside tangent");
            AssertMatches(reader, source, new float2(-15, 0), 1, "large circle across root bounds");
            AssertMatches(reader, source, new float2(0, 4), 0, "zero-radius strict overlap");
        }

        [Test]
        public void PreBuildReaderTracksRebuildEmptyResetAndRequestedCapacityOverflow()
        {
            using var tree = new BoundedQuadtreeReference(float2.zero, new float2(8), 4, bucketSize: 1);
            var reader = tree.AsReader();
            var visitor = new Collect { Hits = new List<int>() };
            var stats = new GridQueryStats();
            Assert.IsTrue(reader.QueryMeasured(float2.zero, 100, ref visitor, ref stats));
            Assert.AreEqual(0, visitor.Hits.Count);
            Assert.AreEqual(0, stats.CellsVisited);
            var staging = tree.Staging;
            staging[0] = Entry(1, 1, 1, 0);
            staging[1] = Entry(2, 2, 2, 1);
            staging[2] = Entry(8, 4, 1, 2);
            staging[3] = Entry(3, 3, 3, 3);
            tree.AsBuilder().Build(7);
            Assert.AreEqual(3, tree.EntryCount);
            Assert.AreEqual(4, tree.RejectedCount, "three unavailable slots plus one outside center");
            Assert.AreEqual(3, reader.EntryCount);
            Assert.AreEqual(3, reader.MaxEntryRadius);
            reader.Query(float2.zero, 100, ref visitor);
            visitor.Hits.Sort();
            CollectionAssert.AreEqual(new[] { 0, 1, 3 }, visitor.Hits);

            tree.Build(0);
            AssertEmpty(tree, reader);
            tree.Build(4);
            Assert.AreEqual(3, reader.EntryCount);
            tree.Build(int.MinValue);
            AssertEmpty(tree, reader);
        }

        [Test]
        public void ZeroCapacityRejectsUnavailableSlotsWithoutCreatingNodes()
        {
            using var tree = new BoundedQuadtreeReference(float2.zero, new float2(8), 0);
            var reader = tree.AsReader();
            tree.Build(7);
            Assert.AreEqual(0, tree.EntryCount);
            Assert.AreEqual(7, tree.RejectedCount);
            Assert.AreEqual(0, tree.NodeCount);
            Assert.AreEqual(0, tree.Depth);
            Assert.Greater(tree.AllocatedBytes, 0);
            var visitor = new Collect { Hits = new List<int>() };
            Assert.IsTrue(reader.Query(float2.zero, 100, ref visitor));
            Assert.AreEqual(0, visitor.Hits.Count);
            tree.Build(0);
            AssertEmpty(tree, reader);
        }

        [TestCase(-1)]
        [TestCase(65537)]
        public void RejectsUnsupportedEntryCapacity(int capacity)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new BoundedQuadtreeReference(float2.zero, new float2(8), capacity));
        }

        [Test]
        public void RebuildOrderIsDeterministicAndEarlyExitStopsBothQueryPaths()
        {
            using var tree = new BoundedQuadtreeReference(new float2(-16), new float2(32), 96, bucketSize: 2);
            var staging = tree.Staging;
            var random = new Unity.Mathematics.Random(3456);
            for (int i = 0; i < staging.Length; i++)
                staging[i] = new GridEntry
                {
                    Position = random.NextFloat2(new float2(-15), new float2(15)), Radius = 1, Owner = i,
                };
            tree.Build(staging.Length);
            var original = new Collect { Hits = new List<int>() };
            tree.AsReader().Query(float2.zero, 100, ref original);
            Assert.AreEqual(staging.Length, original.Hits.Count);
            for (int build = 0; build < 5; build++)
            {
                tree.Build(staging.Length);
                var repeated = new Collect { Hits = new List<int>() };
                Assert.IsTrue(tree.AsReader().Query(float2.zero, 100, ref repeated));
                CollectionAssert.AreEqual(original.Hits, repeated.Hits);
                for (int i = 0; i < staging.Length; i++) Assert.AreEqual(i, staging[i].Owner, "staging stays unchanged");
            }
            var plain = new StopAfter { Hits = new List<int>(), Limit = 3 };
            var measured = new StopAfter { Hits = new List<int>(), Limit = 3 };
            var stats = new GridQueryStats { CellsVisited = 10, CellsPruned = 10, EntriesExamined = 10, VisitorCalls = 10 };
            Assert.IsFalse(tree.AsReader().Query(float2.zero, 100, ref plain));
            Assert.IsFalse(tree.AsReader().QueryMeasured(float2.zero, 100, ref measured, ref stats));
            CollectionAssert.AreEqual(original.Hits.GetRange(0, 3), plain.Hits);
            CollectionAssert.AreEqual(plain.Hits, measured.Hits);
            Assert.AreEqual(13, stats.EntriesExamined);
            Assert.AreEqual(13, stats.VisitorCalls);
            Assert.AreEqual(10, stats.CellsPruned);
            Assert.Greater(stats.CellsVisited, 10);
        }

        struct RecordHits : IGridVisitor
        {
            public NativeArray<int> Masks;
            public NativeArray<int> Order;
            public int Row;
            public int Count;
            public bool RecordOrder;
            public bool Visit(in GridEntry entry)
            {
                Masks[Row + entry.Owner] = Masks[Row + entry.Owner] + 1;
                if (RecordOrder) Order[Row + Count] = entry.Owner;
                Count++;
                return true;
            }
        }

        struct NumericProbe
        {
            public BoundedQuadtreeReference.Reader Tree;
            public GridReader Grid;
            [ReadOnly] public NativeArray<GridEntry> Source;
            [ReadOnly] public NativeArray<float2> Centers;
            public float Radius;
            public int EntryCount;
            public NativeArray<int> GridMasks;
            public NativeArray<int> PrunedMasks;
            public NativeArray<int> TreeMasks;
            public NativeArray<int> OracleMasks;
            public NativeArray<int> GridOrder;
            public NativeArray<int> PrunedOrder;

            public void Execute()
            {
                for (int q = 0; q < Centers.Length; q++)
                {
                    int row = q * EntryCount;
                    for (int i = 0; i < EntryCount; i++)
                    {
                        GridMasks[row + i] = 0;
                        PrunedMasks[row + i] = 0;
                        TreeMasks[row + i] = 0;
                        GridOrder[row + i] = -1;
                        PrunedOrder[row + i] = -1;
                        GridEntry entry = Source[i];
                        float reach = Radius + entry.Radius;
                        OracleMasks[row + i] = Tree.Covers(entry.Position)
                            && math.distancesq(Centers[q], entry.Position) < reach * reach ? 1 : 0;
                    }
                    var grid = new RecordHits { Masks = GridMasks, Order = GridOrder, Row = row, RecordOrder = true };
                    var pruned = new RecordHits { Masks = PrunedMasks, Order = PrunedOrder, Row = row, RecordOrder = true };
                    var tree = new RecordHits { Masks = TreeMasks, Order = TreeMasks, Row = row };
                    Grid.Query(Centers[q], Radius, ref grid);
                    Grid.QueryPruned(Centers[q], Radius, ref pruned);
                    Tree.Query(Centers[q], Radius, ref tree);
                }
            }
        }

        [BurstCompile(FloatMode = FloatMode.Strict, CompileSynchronously = true)]
        struct StrictNumericProbe : IJob
        {
            public NumericProbe Probe;
            public void Execute() => Probe.Execute();
        }

        [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
        struct FastNumericProbe : IJob
        {
            public NumericProbe Probe;
            public void Execute() => Probe.Execute();
        }

        [TestCase(0.0009765625f, 0f)]
        [TestCase(1f, 1000000f)]
        [TestCase(1024f, -1048576f)]
        [TestCase(1e10f, -1e12f)]
        [TestCase(1e18f, 1e19f)]
        public void SplitBoundaryNeighboursAtDifferentScalesMatchFloatBruteForceInStrictAndFastJobs(float scale, float offset)
        {
            const int Count = 64;
            float2 origin = new float2(offset) - new float2(32 * scale);
            using var tree = new BoundedQuadtreeReference(origin, new float2(64 * scale), Count,
                bucketSize: 1, maxDepth: 12, nodeCapacity: 512);
            using var grid = new SpatialGrid(new int2(8), 8 * scale, Count) { Origin = origin };
            var source = new GridEntry[Count];
            using var sourceEntries = new NativeArray<GridEntry>(Count, Allocator.TempJob);
            using var centers = new NativeArray<float2>(Count * 6 * 3, Allocator.TempJob);
            int maskSlots = centers.Length * Count;
            using var gridMasks = new NativeArray<int>(maskSlots, Allocator.TempJob);
            using var prunedMasks = new NativeArray<int>(maskSlots, Allocator.TempJob);
            using var treeMasks = new NativeArray<int>(maskSlots, Allocator.TempJob);
            using var oracleMasks = new NativeArray<int>(maskSlots, Allocator.TempJob);
            using var gridOrder = new NativeArray<int>(maskSlots, Allocator.TempJob);
            using var prunedOrder = new NativeArray<int>(maskSlots, Allocator.TempJob);
            var staging = tree.Staging;
            var gridStaging = grid.Staging;
            float radius = 0.375f * scale;
            int nextQuery = 0;
            for (int i = 0; i < Count; i++)
            {
                float2 position = origin + new float2(i % 8, i / 8) * (8 * scale);
                var entry = new GridEntry { Position = position, Radius = 1.25f * scale, Owner = i };
                source[i] = entry;
                sourceEntries.Set(i, entry);
                staging[i] = entry;
                gridStaging[i] = entry;
                for (int direction = 0; direction < 6; direction++)
                {
                    float2 axis = direction == 0 ? new float2(1, 0) : direction == 1 ? new float2(-1, 0)
                        : direction == 2 ? new float2(0, 1) : direction == 3 ? new float2(0, -1)
                        : direction == 4 ? new float2(0.6f, 0.8f) : new float2(-0.6f, -0.8f);
                    float2 tangent = position + axis * (radius + entry.Radius);
                    centers.Set(nextQuery++, new float2(NextDown(tangent.x), NextDown(tangent.y)));
                    centers.Set(nextQuery++, tangent);
                    centers.Set(nextQuery++, new float2(NextUp(tangent.x), NextUp(tangent.y)));
                }
            }
            tree.Build(Count);
            var stagingCount = grid.StagingCount;
            stagingCount[0] = Count;
            grid.ScheduleBuild(default).Complete();
            Assert.AreEqual(Count, tree.EntryCount);
            Assert.AreEqual(Count, grid.EntryCount);
            var probe = new NumericProbe
            {
                Tree = tree.AsReader(), Grid = grid.AsReader(), Source = sourceEntries, Centers = centers,
                Radius = radius, EntryCount = Count, GridMasks = gridMasks,
                PrunedMasks = prunedMasks, TreeMasks = treeMasks, OracleMasks = oracleMasks,
                GridOrder = gridOrder, PrunedOrder = prunedOrder,
            };
            // Exact per-target multiplicities detect both missing and duplicate hits; full order
            // arrays also prove the additive grid pruning path retains baseline visitor order.
            // Each job computes its brute-force masks in that same float mode: Fast can change a
            // diagonal near-tangent decision versus managed separate-multiply arithmetic. The
            // comparison must test each broadphase without imposing new cross-mode hit semantics.
            probe.Execute();
            AssertNumericProbe(probe, source, "managed", compareManagedOracle: true);
            new StrictNumericProbe { Probe = probe }.Schedule().Complete();
            AssertNumericProbe(probe, source, "strict job");
            new FastNumericProbe { Probe = probe }.Schedule().Complete();
            AssertNumericProbe(probe, source, "fast job");
        }

        static void AssertNumericProbe(NumericProbe probe, GridEntry[] source, string mode,
            bool compareManagedOracle = false)
        {
            int expectedHits = 0;
            for (int q = 0; q < probe.Centers.Length; q++)
            {
                int row = q * source.Length;
                for (int i = 0; i < source.Length; i++)
                {
                    int expected = probe.OracleMasks[row + i];
                    if (compareManagedOracle)
                    {
                        GridEntry entry = source[i];
                        float reach = probe.Radius + entry.Radius;
                        int managedExpected = probe.Tree.Covers(entry.Position)
                            && math.distancesq(probe.Centers[q], entry.Position) < reach * reach ? 1 : 0;
                        if (expected != managedExpected)
                            Assert.Fail("managed oracle mismatch at query " + q + " target " + i);
                    }
                    expectedHits += expected;
                    if (probe.GridMasks[row + i] != expected || probe.PrunedMasks[row + i] != expected
                        || probe.TreeMasks[row + i] != expected)
                        Assert.Fail(mode + " query " + q + " target " + i + " expected " + expected
                            + "; baseline/pruned/tree=" + probe.GridMasks[row + i] + "/"
                            + probe.PrunedMasks[row + i] + "/" + probe.TreeMasks[row + i]);
                    if (probe.GridOrder[row + i] != probe.PrunedOrder[row + i])
                        Assert.Fail(mode + " grid visit order differs at query " + q + " slot " + i);
                }
            }
            Assert.Greater(expectedHits, 0, "boundary corpus must contain real strict overlaps");
        }

        static GridEntry Entry(float x, float y, float radius, int owner) =>
            new GridEntry { Position = new float2(x, y), Radius = radius, Owner = owner };

        static float NextDown(float value)
        {
            if (value == 0f) return -float.Epsilon;
            uint bits = math.asuint(value);
            return math.asfloat(value > 0f ? bits - 1u : bits + 1u);
        }

        static float NextUp(float value)
        {
            if (value == 0f) return float.Epsilon;
            uint bits = math.asuint(value);
            return math.asfloat(value > 0f ? bits + 1u : bits - 1u);
        }

        static void AssertEmpty(BoundedQuadtreeReference tree, BoundedQuadtreeReference.Reader reader)
        {
            Assert.AreEqual(0, tree.EntryCount);
            Assert.AreEqual(0, tree.RejectedCount);
            Assert.AreEqual(0, tree.NodeCount);
            Assert.AreEqual(0, tree.Depth);
            Assert.AreEqual(0, tree.SaturatedSplits);
            Assert.AreEqual(0, reader.MaxEntryRadius);
            var visitor = new Collect { Hits = new List<int>() };
            Assert.IsTrue(reader.Query(float2.zero, 100, ref visitor));
            Assert.AreEqual(0, visitor.Hits.Count);
        }

        static void AssertMatches(BoundedQuadtreeReference.Reader reader, GridEntry[] source,
            float2 center, float radius, string context)
        {
            var plain = new Collect { Hits = new List<int>() };
            var measured = new Collect { Hits = new List<int>() };
            var stats = new GridQueryStats();
            Assert.IsTrue(reader.Query(center, radius, ref plain), context);
            Assert.IsTrue(reader.QueryMeasured(center, radius, ref measured, ref stats), context);
            CollectionAssert.AreEqual(plain.Hits, measured.Hits, "instrumentation preserves traversal order: " + context);
            Assert.AreEqual(plain.Hits.Count, stats.VisitorCalls, context);
            Assert.GreaterOrEqual(stats.EntriesExamined, stats.VisitorCalls, context);
            Assert.GreaterOrEqual(stats.CellsVisited, stats.CellsPruned, context);
            var expected = new List<int>();
            for (int i = 0; i < source.Length; i++)
            {
                GridEntry entry = source[i];
                float reach = radius + entry.Radius;
                if (reader.Covers(entry.Position) && math.distancesq(center, entry.Position) < reach * reach)
                    expected.Add(entry.Owner);
            }
            plain.Hits.Sort();
            expected.Sort();
            CollectionAssert.AreEqual(expected, plain.Hits, context);
        }
    }
}
