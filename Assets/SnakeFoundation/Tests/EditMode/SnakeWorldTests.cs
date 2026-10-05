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

        struct CollectBody : SPF.L1.Spatial.IGridVisitor
        {
            public System.Collections.Generic.List<(int owner, bool head, float2 position, float radius)> Entries;
            public bool Visit(in SPF.L1.Spatial.GridEntry entry)
            {
                Entries.Add((entry.Owner, (entry.Data & SnakeFoundation.Systems.GridBits.HeadBit) != 0, entry.Position, entry.Radius));
                return true;
            }
        }

        static SnakeFoundation.Systems.BodyGridSystem BodyGridSystem(SnakeTestWorld t)
        {
            for (int i = 0; i < t.Session.Pipeline.SystemCount; i++)
                if (t.Session.Pipeline.GetSystem(i) is SnakeFoundation.Systems.BodyGridSystem body)
                    return body;
            throw new AssertionException("no body grid system");
        }

        /// <summary>
        /// The body grid is updated incrementally (nodes added at the head end, removed at the tail);
        /// after spawns, deaths (swap-back moves), growth and respacing it must hold exactly one head entry
        /// per active snake plus a node on every k-th kept trail point, at the trail point's position.
        /// </summary>
        [Test]
        public void BodyGridHoldsExactlyTheAnchoredNodes()
        {
            using var t = new SnakeTestWorld(aiPerRegion: 120, foodPerChunk: 60, seed: 7);
            t.StartPlayer();
            var system = BodyGridSystem(t);
            var s = t.Runtime.Settings;
            long resyncs = 0, added = 0;
            for (int round = 0; round < 8; round++)
            {
                t.Step(round == 0 ? 40 : 25);
                resyncs += system.LastResyncs;
                added += system.LastAdded;
                var grid = t.World.Resource(SnakeKeys.BodyGrid);
                var reader = grid.AsReader();
                var visitor = new CollectBody { Entries = new System.Collections.Generic.List<(int, bool, float2, float)>() };
                reader.QueryCells(reader.Origin, reader.Max, ref visitor);

                var table = t.World.Table(SnakeKeys.Snake);
                var heads = t.World.Column(SnakeKeys.Head);
                var trails = t.World.Column(SnakeKeys.Trail);
                var radii = t.World.Column(SnakeKeys.Radius);
                var infos = t.World.Column(SnakeKeys.Info);
                var points = t.World.Resource(SnakeKeys.Bodies).Points;
                var expected = new System.Collections.Generic.List<(int, bool, float2)>();
                for (int row = 0; row < table.Count; row++)
                {
                    var rec = system.Record(row);
                    var info = infos[row];
                    if (!rec.Active)
                    {
                        Assert.IsFalse(info.Region == t.Game.ActiveRegion && !info.Has(SnakeFlags.Dead) && reader.Covers(heads[row]),
                            $"row {row}: an alive snake with its head in the window has nodes");
                        continue;
                    }
                    var trail = trails[row];
                    Assert.AreEqual(trail.Version, rec.Version, $"row {row}");
                    Assert.AreEqual(trail.Start, rec.Start);
                    Assert.GreaterOrEqual(rec.Radius, radii[row], "stored radius bounds the real one");
                    Assert.LessOrEqual(math.abs(rec.Step * trail.Spacing - s.NodeSpacing(radii[row])), trail.Spacing * 0.6f + 1e-4f, "node step follows the radius");
                    uint oldest = trail.Pushed - (uint)trail.Count;
                    for (uint g = oldest; g < trail.Pushed; g++)
                        if (g % (uint)rec.Step == 0)
                            expected.Add((row, false, points[trail.Slot(g)]));
                    expected.Add((row, true, heads[row]));
                }
                var actual = new System.Collections.Generic.List<(int, bool, float2)>();
                foreach (var e in visitor.Entries)
                {
                    actual.Add((e.owner, e.head, e.position));
                    Assert.AreEqual(system.Record(e.owner).Radius, e.radius, "entry radius is the stored band radius");
                }
                // Positions outside the window are not stored: compare only entries the grid can hold.
                expected.RemoveAll(e => !reader.Covers(e.Item3));
                System.Comparison<(int, bool, float2)> order = (a, b) =>
                    a.Item1 != b.Item1 ? a.Item1.CompareTo(b.Item1) : a.Item2 != b.Item2 ? a.Item2.CompareTo(b.Item2) :
                    a.Item3.x != b.Item3.x ? a.Item3.x.CompareTo(b.Item3.x) : a.Item3.y.CompareTo(b.Item3.y);
                expected.Sort(order);
                actual.Sort(order);
                Assert.AreEqual(expected.Count, actual.Count, $"round {round}: entry count");
                for (int i = 0; i < expected.Count; i++)
                    Assert.AreEqual(expected[i], actual[i], $"round {round} entry {i}");
            }
            TestContext.WriteLine($"body grid: {system.FullRebuilds} full rebuilds; sampled ticks resynced {resyncs} snakes, added {added} nodes");
        }

        /// <summary>A snake that grows thick gets a wider trail spacing (fewer points per metre of body).</summary>
        [Test]
        public void TrailSpacingGrowsWithRadius()
        {
            using var t = new SnakeTestWorld(foodPerChunk: 0);
            t.StartPlayer();
            var player = t.Game.Player;
            var s = t.Runtime.Settings;
            int row = t.Row(player);
            float thin = t.World.Column(SnakeKeys.Trail)[row].Spacing;
            Assert.AreEqual(s.TrailSpacingFor(s.Growth.Radius(t.World.Column(SnakeKeys.Mass)[row])), thin, 1e-5f);

            t.World.Column(SnakeKeys.Mass).Set(row, 4000f);
            t.Step(3);
            row = t.Row(player);
            var trail = t.World.Column(SnakeKeys.Trail)[row];
            float radius = s.Growth.Radius(t.World.Column(SnakeKeys.Mass)[row]);
            Assert.Greater(trail.Spacing, thin, "respaced for the thicker body");
            Assert.IsFalse(s.NeedsRespace(trail.Spacing, radius));
            Assert.AreEqual(s.TrailSpacingFor(radius), trail.Spacing, 1e-5f);
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
