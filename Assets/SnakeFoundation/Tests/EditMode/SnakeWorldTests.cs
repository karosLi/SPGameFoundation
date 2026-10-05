using NUnit.Framework;
using SPF.Contracts;
using Unity.Mathematics;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace SnakeFoundation.Tests
{
    public class SnakeWorldTests
    {
        [Test]
        public void RegionsMatchTheDesign()
        {
            using var t = new SnakeTestWorld();
            var big = t.Runtime.Regions[0];
            var small = t.Runtime.Regions[1];
            Assert.AreEqual(new float2(7500), big.Size);
            Assert.AreEqual(big.Size.x * big.Size.y / 4f, small.Size.x * small.Size.y, "small map is a quarter of the area");
            Assert.AreEqual(3600, t.World.Resource(SnakeKeys.Populations).Food[0].Layout.ChunkCount);
            Assert.AreEqual(900, t.World.Resource(SnakeKeys.Populations).Food[1].Layout.ChunkCount);
        }

        [Test]
        public void WorldFillsWithAIAndStreamsFoodAroundTheFocus()
        {
            using var t = new SnakeTestWorld(aiPerRegion: 150, foodPerChunk: 40, propsPerChunk: 1);
            t.Step(30);
            var snakes = t.World.Table(SnakeKeys.Snake);
            Assert.AreEqual(150, snakes.Count, "AI population reaches its target (10 per tick)");

            int food = t.World.Table(SnakeKeys.Food).Count;
            var population = t.World.Resource(SnakeKeys.Populations).Food[0];
            Assert.Greater(food, 40 * 40, "chunks overlapping the 1024-unit window are instantiated");
            Assert.Less(food, 40 * 200);
            Assert.AreEqual(40L * 3600, population.Total, 40, "untouched chunks keep their stored count (minus a few eaten pellets)");

            for (int row = 0; row < snakes.Count; row++)
            {
                Assert.IsTrue(t.World.Registry.TryResolve(snakes.Handles[row], out _, out int resolved));
                Assert.AreEqual(row, resolved);
            }
        }

        struct CollectFood : SPF.L1.Spatial.IGridVisitor
        {
            public System.Collections.Generic.List<int> Rows;
            public bool Visit(in SPF.L1.Spatial.GridEntry entry)
            {
                if (entry.Data == 0) Rows.Add(entry.Owner);
                return true;
            }
        }

        /// <summary>
        /// The item grid is updated incrementally from changed rows (full rebuild only when the window
        /// moves); after a run with eating, spawning and swap-back removals its queries must still match
        /// a brute-force scan.
        /// </summary>
        [Test]
        public void ItemGridStaysExactWithIncrementalUpdates()
        {
            using var t = new SnakeTestWorld(aiPerRegion: 40, foodPerChunk: 30);
            t.StartPlayer();
            long skipped = 0;
            var random = new Unity.Mathematics.Random(5);
            for (int round = 0; round < 6; round++)
            {
                t.Step(round % 2 == 0 ? 1 : 15);
                // Two extra ticks without input: usually at least one has no item change.
                t.Step(2);
                var world = t.World;
                var reader = world.Resource(SnakeKeys.ItemGrid).AsReader();
                var positions = world.Column(SnakeKeys.FoodPosition);
                var infos = world.Column(SnakeKeys.FoodInfo);
                int foodCount = world.Table(SnakeKeys.Food).Count;
                for (int q = 0; q < 20; q++)
                {
                    float2 center = random.NextFloat2(reader.Origin + 40f, reader.Max - 40f);
                    float radius = random.NextFloat(1f, 25f);
                    var visitor = new CollectFood { Rows = new System.Collections.Generic.List<int>() };
                    reader.Query(center, radius, ref visitor);
                    var expected = new System.Collections.Generic.List<int>();
                    for (int i = 0; i < foodCount; i++)
                    {
                        float r = radius + infos[i].Radius;
                        if (reader.Covers(positions[i]) && math.distancesq(center, positions[i]) < r * r)
                            expected.Add(i);
                    }
                    visitor.Rows.Sort();
                    CollectionAssert.AreEqual(expected, visitor.Rows, $"round {round} query {q}");
                }
            }
            for (int i = 0; i < t.Session.Pipeline.SystemCount; i++)
                if (t.Session.Pipeline.GetSystem(i) is SnakeFoundation.Systems.ItemGridSystem items)
                {
                    skipped = items.IncrementalUpdates;
                    Assert.Greater(items.IncrementalUpdates, 0, "the grid was updated incrementally");
                    TestContext.WriteLine($"item grid: {items.FullRebuilds} full rebuilds, {items.IncrementalUpdates} incremental updates, {items.DirtyRows} rows");
                }
        }

        [Test]
        public void AISnakesMostlySurviveAndStayInsideTheRegion()
        {
            using var t = new SnakeTestWorld(aiPerRegion: 60, foodPerChunk: 30);
            t.Step(300); // ten seconds
            var region = t.Runtime.Regions[0];
            var heads = t.World.Column(SnakeKeys.Head);
            int count = t.World.Table(SnakeKeys.Snake).Count;
            for (int i = 0; i < count; i++)
            {
                Assert.GreaterOrEqual(heads[i].x, region.Min.x);
                Assert.LessOrEqual(heads[i].x, region.Max.x);
            }
            Assert.AreEqual(60, count, "dead AI are replaced");
        }

        [Test]
        public void PortalMovesThePlayerToTheSmallMapAndFreezesTheBigOne()
        {
            using var t = new SnakeTestWorld(aiPerRegion: 10, foodPerChunk: 20);
            t.Step(5);
            t.StartPlayer();
            var portal = t.Runtime.Portals[0];
            t.Place(t.Game.Player, portal.Position - new float2(8, 0), new float2(1, 0));
            t.Game.Command = new PlayerCommand { Direction = new float2(1, 0) };

            // Remember a frozen AI's head.
            int aiRow = -1;
            var infos = t.World.Column(SnakeKeys.Info);
            for (int i = 0; i < t.World.Table(SnakeKeys.Snake).Count; i++)
                if (infos[i].Has(SnakeFlags.AI)) { aiRow = i; break; }
            var aiHandle = t.World.Table(SnakeKeys.Snake).Handles[aiRow];

            for (int i = 0; i < 30 && t.Game.ActiveRegion == 0; i++) t.Step();
            Assert.AreEqual(1, t.Game.ActiveRegion);
            Assert.AreEqual(1, t.World.Column(SnakeKeys.Info)[t.PlayerRow].Region);
            Assert.Less(math.distance(t.World.Column(SnakeKeys.Head)[t.PlayerRow], portal.Arrival), 10f);

            float2 frozen = t.World.Column(SnakeKeys.Head)[t.Row(aiHandle)];
            t.Step(30);
            Assert.AreEqual(frozen, t.World.Column(SnakeKeys.Head)[t.Row(aiHandle)], "big-map snakes do not move while the player is away");
            Assert.AreEqual(GameFlow.Playing, t.Game.Flow);

            int inSmall = 0;
            infos = t.World.Column(SnakeKeys.Info);
            for (int i = 0; i < t.World.Table(SnakeKeys.Snake).Count; i++)
                if (infos[i].Region == 1 && infos[i].Has(SnakeFlags.AI)) inSmall++;
            Assert.AreEqual(10, inSmall, "the small map got its own AI population");
            var foods = t.World.Column(SnakeKeys.FoodPosition);
            var small = t.Runtime.Regions[1];
            for (int i = 0; i < t.World.Table(SnakeKeys.Food).Count; i++)
                Assert.IsTrue(math.all(foods[i] >= small.Min) && math.all(foods[i] <= small.Max), "only small-map food exists");
        }

        [Test]
        public void SameSeedAndInputsGiveTheSameWorld()
        {
            float Run()
            {
                using var t = new SnakeTestWorld(aiPerRegion: 40, foodPerChunk: 30, propsPerChunk: 1, seed: 7);
                t.Step(10);
                t.StartPlayer();
                for (int i = 0; i < 200; i++)
                {
                    t.Game.Command = new PlayerCommand { Direction = new float2(math.cos(i * 0.05f), math.sin(i * 0.05f)), Boost = i % 50 < 10 };
                    t.Step();
                }
                float hash = 0f;
                var heads = t.World.Column(SnakeKeys.Head);
                var masses = t.World.Column(SnakeKeys.Mass);
                for (int i = 0; i < t.World.Table(SnakeKeys.Snake).Count; i++)
                    hash += heads[i].x * 0.37f + heads[i].y * 0.11f + masses[i] * (i + 1);
                return hash + t.World.Table(SnakeKeys.Food).Count;
            }
            Assert.AreEqual(Run(), Run());
        }

        [Test]
        public void RestartResetsTheWorld()
        {
            using var t = new SnakeTestWorld(aiPerRegion: 20, foodPerChunk: 20);
            t.Step(10);
            t.StartPlayer();
            t.Step(10);
            t.Session.Restart();
            Assert.AreEqual(0, t.World.Table(SnakeKeys.Snake).Count);
            Assert.AreEqual(GameFlow.Attract, t.Game.Flow);
            t.Step(5);
            Assert.AreEqual(20, t.World.Table(SnakeKeys.Snake).Count);
            Assert.Greater(t.World.Table(SnakeKeys.Food).Count, 0);
        }

        [Test]
        public void TheWorldAroundThePlayerIsLively()
        {
            using var t = new SnakeTestWorld(aiPerRegion: 150, foodPerChunk: 150, propsPerChunk: 1);
            t.Step(30);
            int firstId = t.Game.NextSnakeId;
            t.Step(600);
            int inWindow = 0;
            var infos = t.World.Column(SnakeKeys.Info);
            int count = t.World.Table(SnakeKeys.Snake).Count;
            for (int i = 0; i < count; i++)
                if (infos[i].Has(SnakeFlags.InWindow)) inWindow++;
            TestContext.Progress.WriteLine($"in window {inWindow}, respawned {t.Game.NextSnakeId - firstId} in 20 s");
            Assert.That(inWindow, NUnit.Framework.Is.InRange(30, 100), "design budget: about 60 snakes around the player");
            Assert.Greater(t.Game.NextSnakeId - firstId, 0, "AI fight and die, and are replaced");
        }

        [Test]
        public void SteadyStateSnakeTicksDoNotAllocate()
        {
            using var t = new SnakeTestWorld(aiPerRegion: 60, foodPerChunk: 30, propsPerChunk: 1);
            t.Step(20);
            t.StartPlayer();
            t.Step(20);
            Assert.That(() =>
            {
                for (int i = 0; i < 60; i++) t.Session.Step();
            }, Is.Not.AllocatingGCMemory());
        }
    }
}
