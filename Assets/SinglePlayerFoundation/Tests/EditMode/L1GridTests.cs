using System.Collections.Generic;
using NUnit.Framework;
using SPF.L1.Spatial;
using Unity.Mathematics;

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
