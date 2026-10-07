using System.Collections.Generic;
using NUnit.Framework;
using SPF.L1.Spatial;
using Unity.Mathematics;
using SPF.Contracts;

namespace SPF.Tests.EditMode
{
    public class L1GridTests
    {
        struct Collect : IGridVisitor
        {
            public List<int> Hits;
            public bool Visit(in GridEntry entry) { Hits.Add(entry.Owner); return true; }
        }

        /// <param name="largeRadius">0: single layer; otherwise entries above it use the coarse large layer.</param>
        [TestCase(0f)]
        [TestCase(1.5f)]
        public void QueryMatchesBruteForce(float largeRadius)
        {
            using var grid = new SpatialGrid(new int2(32, 32), 4f, 5000, largeRadius);
            grid.Origin = new float2(-64, -64);
            var staging = grid.Staging;
            var stagingCount = grid.StagingCount;
            var random = new Random(1234);
            var all = new List<GridEntry>();
            for (int i = 0; i < 3000; i++)
            {
                var e = new GridEntry
                {
                    Position = random.NextFloat2(new float2(-70), new float2(70)), // some outside the window
                    Radius = random.NextFloat(0.2f, 3f),
                    Owner = i,
                };
                all.Add(e);
                staging[i] = e;
            }
            stagingCount[0] = all.Count;
            grid.ScheduleBuild(default).Complete();
            var reader = grid.AsReader();

            int inside = 0, large = 0;
            foreach (var e in all)
                if (reader.Covers(e.Position)) { inside++; if (largeRadius > 0f && e.Radius > largeRadius) large++; }
            Assert.AreEqual(inside, grid.EntryCount);
            Assert.AreEqual(large, grid.LargeEntryCount);
            Assert.AreEqual(all.Count - inside, grid.DroppedLastBuild);

            for (int q = 0; q < 200; q++)
            {
                float2 center = random.NextFloat2(new float2(-60), new float2(60));
                float radius = random.NextFloat(0.1f, 6f);
                var visitor = new Collect { Hits = new List<int>() };
                reader.Query(center, radius, ref visitor);

                var expected = new List<int>();
                foreach (var e in all)
                {
                    float r = radius + e.Radius;
                    if (reader.Covers(e.Position) && math.distancesq(center, e.Position) < r * r)
                        expected.Add(e.Owner);
                }
                visitor.Hits.Sort();
                CollectionAssert.AreEqual(expected, visitor.Hits, $"query {q}");
            }
        }

        /// <param name="spread">Half-size of the area entries are placed in: 70 = sparse (some outside the
        /// window), 6 = dense (~2x2 cells, long multi-block chains, frequent tail-block frees).</param>
        [TestCase(70f)]
        [TestCase(6f)]
        public void CellListGridMatchesBruteForceUnderRandomEdits(float spread)
        {
            const int Keys = 600;
            using var grid = new CellListGrid(new int2(16, 16), 8f, Keys, Keys) { Origin = new float2(-64, -64) };
            grid.MarkBuilt();
            var writer = grid.AsWriter();
            var random = new Random(77);
            var model = new GridEntry?[Keys];
            for (int step = 0; step < 4000; step++)
            {
                int key = random.NextInt(Keys);
                if (random.NextFloat() < 0.3f)
                {
                    writer.Remove(key);
                    model[key] = null;
                }
                else
                {
                    var e = new GridEntry { Position = random.NextFloat2(new float2(-spread), new float2(spread)), Radius = random.NextFloat(0.2f, 2f), Owner = key };
                    bool stored = writer.Set(key, e);
                    model[key] = stored ? e : (GridEntry?)null;
                    Assert.AreEqual(math.all(e.Position >= grid.Origin) && math.all(e.Position < grid.Origin + grid.Size), stored);
                }

                if (step % 200 != 0) continue;
                var reader = grid.AsReader();
                int expectedCount = 0;
                foreach (var m in model) if (m.HasValue) expectedCount++;
                Assert.AreEqual(expectedCount, grid.Count);
                for (int q = 0; q < 20; q++)
                {
                    float2 center = random.NextFloat2(new float2(-math.min(spread, 60f)), new float2(math.min(spread, 60f)));
                    float radius = random.NextFloat(0.1f, 10f);
                    var visitor = new Collect { Hits = new List<int>() };
                    reader.Query(center, radius, ref visitor);
                    var expected = new List<int>();
                    for (int k = 0; k < Keys; k++)
                    {
                        if (!model[k].HasValue) continue;
                        var e = model[k].Value;
                        float r = radius + e.Radius;
                        if (math.distancesq(center, e.Position) < r * r) expected.Add(k);
                    }
                    visitor.Hits.Sort();
                    CollectionAssert.AreEqual(expected, visitor.Hits, $"step {step} query {q}");
                }
            }
            writer.Clear();
            Assert.AreEqual(0, grid.Count);
        }

        [Test]
        public void BuildIsStableWithinACell()
        {
            using var grid = new SpatialGrid(new int2(4, 4), 10f, 16);
            var staging = grid.Staging;
            var stagingCount = grid.StagingCount;
            for (int i = 0; i < 5; i++)
                staging[i] = new GridEntry { Position = new float2(5, 5), Radius = 1, Owner = i };
            stagingCount[0] = 5;
            grid.ScheduleBuild(default).Complete();
            var visitor = new Collect { Hits = new List<int>() };
            grid.AsReader().Query(new float2(5, 5), 1f, ref visitor);
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4 }, visitor.Hits);
        }

        [Test]
        public void FollowMovesWithHysteresisAndStaysInRegion()
        {
            using var grid = new SpatialGrid(new int2(256, 256), 4f, 16);
            float2 regionMin = new float2(-3750), regionMax = new float2(3750);
            Assert.IsTrue(grid.Follow(float2.zero, regionMin, regionMax));
            float2 origin = grid.Origin;
            Assert.IsFalse(grid.Follow(new float2(100, 100), regionMin, regionMax), "inside the central half: no move");
            Assert.AreEqual(origin, grid.Origin);
            Assert.IsTrue(grid.Follow(new float2(3700, -3700), regionMin, regionMax));
            Assert.LessOrEqual(grid.Origin.x + grid.Size.x, 3750f + 4f);
            Assert.GreaterOrEqual(grid.Origin.y, -3750f - 4f);
        }

        [Test]
        public void CoarseLayerUsesTheSameHalfOpenWindowAsFineLayer()
        {
            using var grid = new SpatialGrid(new int2(5, 7), 1f, 8, .5f, 4) { Origin = new float2(-2, -3) };
            var entries = new[]
            {
                new GridEntry { Position = new float2(-2, -3), Radius = 1, Owner = 0 },
                new GridEntry { Position = new float2(2.999f, 3.999f), Radius = 1, Owner = 1 },
                new GridEntry { Position = new float2(3, 0), Radius = 1, Owner = 2 },
                new GridEntry { Position = new float2(0, 4), Radius = 1, Owner = 3 },
                new GridEntry { Position = new float2(4, 4), Radius = 1, Owner = 4 },
                new GridEntry { Position = new float2(-2.001f, 0), Radius = .2f, Owner = 5 },
            };
            for (int i = 0; i < entries.Length; i++) grid.Staging.Set(i, entries[i]);
            grid.StagingCount.Set(0, entries.Length); grid.ScheduleBuild(default).Complete();
            Assert.AreEqual(2, grid.EntryCount); Assert.AreEqual(2, grid.LargeEntryCount); Assert.AreEqual(4, grid.DroppedLastBuild);
            var hits = new Collect { Hits = new List<int>() }; grid.AsReader().Query(float2.zero, 100, ref hits);
            CollectionAssert.AreEqual(new[] { 0, 1 }, hits.Hits);
        }

        [TestCase(.01f, .005f)]
        [TestCase(1e-10f, 1e-12f)]
        public void ReciprocalRoundedUpperEdgeCannotDisagreeBetweenCountAndScatter(float entryRadius, float largeThreshold)
        {
            using var grid = new SpatialGrid(new int2(3), .01f, 1, largeThreshold, 3);
            float justInside = math.asfloat(math.asuint(3 * .01f) - 1u);
            var position = new float2(justInside, .005f);
            grid.Staging.Set(0, new GridEntry { Position = position, Radius = entryRadius, Owner = 0 });
            grid.StagingCount.Set(0, 1); grid.ScheduleBuild(default).Complete();
            Assert.IsTrue(grid.AsReader().Covers(position)); Assert.AreEqual(1, grid.EntryCount); Assert.AreEqual(1, grid.LargeEntryCount);
            Assert.AreEqual(0, grid.DroppedLastBuild);
            var hits = new Collect { Hits = new List<int>() }; grid.AsReader().Query(position, entryRadius, ref hits);
            CollectionAssert.AreEqual(new[] { 0 }, hits.Hits);
            var pruned = new Collect { Hits = new List<int>() }; grid.AsReader().QueryPruned(position, entryRadius, ref pruned);
            CollectionAssert.AreEqual(hits.Hits, pruned.Hits);
            var cellHits = new Collect { Hits = new List<int>() }; grid.AsReader().QueryCells(position, position, ref cellHits);
            CollectionAssert.AreEqual(new[] { 0 }, cellHits.Hits, "point cell query uses the same accepted upper-edge mapping");
            var outside = new Collect { Hits = new List<int>() };
            grid.AsReader().QueryCells(new float2(.04f), new float2(.05f), ref outside);
            Assert.AreEqual(0, outside.Hits.Count, "an external rectangle must not be clamped into the last cell");
        }

        [Test]
        public void PruningAccountsForLargeOriginCancellationWhenReconstructingCells()
        {
            using var grid = new SpatialGrid(new int2(1000010, 1), .01f, 1) { Origin = new float2(-10000, 0) };
            var position = new float2(-.01f, .005f);
            grid.Staging.Set(0, new GridEntry { Position = position, Radius = .0001f, Owner = 0 });
            grid.StagingCount.Set(0, 1); grid.ScheduleBuild(default).Complete();
            var regular = new Collect { Hits = new List<int>() }; var pruned = new Collect { Hits = new List<int>() };
            grid.AsReader().Query(position, .0001f, ref regular); grid.AsReader().QueryPruned(position, .0001f, ref pruned);
            CollectionAssert.AreEqual(new[] { 0 }, regular.Hits); CollectionAssert.AreEqual(regular.Hits, pruned.Hits);
        }

        [Test]
        public void OverreportedStagingCountAccountsForCapacityAndWindowDrops()
        {
            using var grid = new SpatialGrid(new int2(2), 1, 2);
            grid.Staging.Set(0, new GridEntry { Position = new float2(.5f), Radius = .2f, Owner = 0 });
            grid.Staging.Set(1, new GridEntry { Position = new float2(3), Radius = .2f, Owner = 1 });
            grid.StagingCount.Set(0, 5); grid.ScheduleBuild(default).Complete();
            Assert.AreEqual(1, grid.EntryCount); Assert.AreEqual(4, grid.DroppedLastBuild); Assert.AreEqual(0, grid.StagingCount[0]);
            grid.ScheduleBuild(default).Complete(); Assert.AreEqual(0, grid.EntryCount); Assert.AreEqual(0, grid.DroppedLastBuild);
        }

        struct StopAfter : IGridVisitor
        {
            public List<int> Hits;
            public int Limit;
            public bool Visit(in GridEntry e) { Hits.Add(e.Owner); return Hits.Count < Limit; }
        }

        [TestCase(0f)] [TestCase(.5f)]
        public void PrunedQueriesPreserveExactVisitOrderEarlyExitAndBoundarySemantics(float largeRadius)
        {
            foreach (float cell in new[] { .03125f, 1f, 3.7f, 4096f })
            foreach (float offset in new[] { 0f, -123.25f, 1000000f })
            {
                using var grid = new SpatialGrid(new int2(17, 13), cell, 300, largeRadius * cell, 4) { Origin = new float2(offset) };
                var random = new Random(9173);
                for (int i = 0; i < 300; i++)
                {
                    float2 p = grid.Origin + random.NextFloat2(float2.zero, grid.Size);
                    if (i % 4 == 0) p = grid.Origin + new float2(i % 17, i % 13) * cell;
                    grid.Staging.Set(i, new GridEntry { Position = p, Radius = (i % 11 == 0 ? 2 : .2f) * cell, Owner = i });
                }
                grid.StagingCount.Set(0, 300); grid.ScheduleBuild(default).Complete(); var reader = grid.AsReader();
                for (int q = 0; q < 80; q++)
                {
                    float2 center = grid.Origin + random.NextFloat2(-grid.Size * .2f, grid.Size * 1.2f);
                    float radius = q % 7 == 0 ? 0 : random.NextFloat(0, 7 * cell);
                    var regular = new Collect { Hits = new List<int>() }; var pruned = new Collect { Hits = new List<int>() };
                    reader.Query(center, radius, ref regular); reader.QueryPruned(center, radius, ref pruned);
                    CollectionAssert.AreEqual(regular.Hits, pruned.Hits, "cell=" + cell + "; offset=" + offset + "; query=" + q);
                    var a = new StopAfter { Hits = new List<int>(), Limit = 8 }; var b = new StopAfter { Hits = new List<int>(), Limit = 8 };
                    reader.Query(center, radius, ref a); reader.QueryPruned(center, radius, ref b); CollectionAssert.AreEqual(a.Hits, b.Hits);
                    var counted = new Collect { Hits = new List<int>() }; var stats = new GridQueryStats();
                    reader.QueryMeasured(center, radius, ref counted, ref stats, true);
                    CollectionAssert.AreEqual(regular.Hits, counted.Hits); Assert.AreEqual(counted.Hits.Count, stats.VisitorCalls);
                    Assert.GreaterOrEqual(stats.EntriesExamined, stats.VisitorCalls);
                }
            }
        }

        [Test]
        public void StrictCircleTangencyAndInclusiveCellTraversalRemainDistinct()
        {
            using var grid = new SpatialGrid(new int2(8), 1, 3);
            grid.Staging.Set(0, new GridEntry { Position = new float2(2, 2), Radius = 1, Owner = 0 });
            grid.Staging.Set(1, new GridEntry { Position = new float2(math.asfloat(math.asuint(2f) - 1u), 2), Radius = 1, Owner = 1 });
            grid.Staging.Set(2, new GridEntry { Position = new float2(math.asfloat(math.asuint(2f) + 1u), 2), Radius = 1, Owner = 2 });
            grid.StagingCount.Set(0, 3); grid.ScheduleBuild(default).Complete();
            var reader = grid.AsReader(); var a = new Collect { Hits = new List<int>() }; var b = new Collect { Hits = new List<int>() };
            reader.Query(new float2(0, 2), 1, ref a); reader.QueryPruned(new float2(0, 2), 1, ref b);
            CollectionAssert.AreEqual(new[] { 1 }, a.Hits); CollectionAssert.AreEqual(a.Hits, b.Hits);
            var cells = new Collect { Hits = new List<int>() }; reader.QueryCells(new float2(2, 2), new float2(2, 2), ref cells);
            CollectionAssert.AreEqual(new[] { 0, 2 }, cells.Hits);
        }

        [Test]
        public void ChunkLayoutMatchesBigMap()
        {
            var layout = new ChunkLayout(new float2(-3750), new float2(7500), 125f);
            Assert.AreEqual(new int2(60, 60), layout.Dimensions);
            Assert.AreEqual(0, layout.IndexOf(new float2(-3750, -3750)));
            Assert.AreEqual(3599, layout.IndexOf(new float2(3749, 3749)));
            Assert.AreEqual(3599, layout.IndexOf(new float2(9999, 9999)), "clamped");
        }
    }
}
