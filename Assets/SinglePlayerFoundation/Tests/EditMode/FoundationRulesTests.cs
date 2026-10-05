using System.IO;
using NUnit.Framework;
using SPF.L1.Navigation;
using SPF.L1.Spatial;
using SPF.L2.Combat;
using SPF.L2.Progression;
using SPF.L2.Stats;
using SPF.Runtime.Persistence;
using Unity.Jobs;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

namespace SPF.Tests.EditMode
{
    /// <summary>Tile collision / line of sight, flow fields, stats, combat, progression and save files.</summary>
    public class FoundationRulesTests
    {
        static TileMap Room(int size = 12)
        {
            var map = new TileMap(new int2(size), 1f);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                map[new int2(x, y)] = (byte)(x == 0 || y == 0 || x == size - 1 || y == size - 1 ? 1 : 0);
            return map;
        }

        [Test]
        public void CirclesArePushedOutOfWallsAndDoNotTunnel()
        {
            using var map = Room();
            map[new int2(6, 1)] = 1; map[new int2(6, 2)] = 1; map[new int2(6, 3)] = 1;   // a wall stub
            var view = map.AsView();
            float2 pushed = view.ResolveCircle(new float2(1.1f, 5.5f), 0.4f);
            Assert.AreEqual(1.4f, pushed.x, 1e-4f, "pushed out of the left wall");
            // 20 m in one step towards the wall stub: sub-stepping stops it at the wall face.
            float2 moved = view.MoveCircle(new float2(4.5f, 2.5f), new float2(20f, 0f), 0.4f);
            Assert.Less(moved.x, 6f - 0.4f + 1e-3f, "did not pass through the wall");
            // Sliding: moving diagonally into the wall keeps the tangential motion.
            float2 slid = view.MoveCircle(new float2(5.5f, 2.5f), new float2(1f, 1f), 0.4f);
            Assert.Greater(slid.y, 3.3f);
            Assert.IsTrue(view.LineOfSight(new float2(2.5f, 5.5f), new float2(9.5f, 5.5f)));
            Assert.IsFalse(view.LineOfSight(new float2(4.5f, 2.5f), new float2(8.5f, 2.5f)), "the stub blocks sight");
            Assert.IsFalse(view.LineOfSight(new float2(2.5f, 2.5f), new float2(20f, 2.5f)), "outside the map is solid");
        }

        [Test]
        public void FlowFieldLeadsEveryReachableTileToTheGoal()
        {
            using var map = Room(16);
            // A maze-ish divider with one gap.
            for (int y = 1; y < 14; y++) map[new int2(8, y)] = 1;
            using var field = new FlowField(map.Size);
            var view = map.AsView();
            var goal = new int2(12, 3);
            field.ScheduleBuild(view, new[] { goal }, default).Complete();
            var flow = field.AsView(view);
            Assert.AreEqual(0, flow.DistanceAt(goal));
            Assert.AreEqual(FlowField.Unreachable, flow.DistanceAt(new int2(8, 5)), "walls are unreachable");
            // From every walkable tile, following the field reaches the goal without entering walls.
            for (int y = 1; y < 15; y++)
            for (int x = 1; x < 15; x++)
            {
                if (map[new int2(x, y)] != 0) continue;
                float2 p = view.CenterOf(new int2(x, y));
                int steps = 0;
                while (math.any(view.CellOf(p) != goal) && steps++ < 400)
                {
                    float2 dir = flow.Direction(p);
                    Assert.AreNotEqual(float2.zero, dir, $"stuck at {view.CellOf(p)} from ({x},{y})");
                    p = view.MoveCircle(p, dir * 0.25f, 0.3f);
                }
                Assert.Less(steps, 400, $"reached the goal from ({x},{y})");
            }
        }

        [Test]
        public void ModifiersStackRefreshAndExpire()
        {
            var baseStats = new StatBlock();
            baseStats[0] = 100f;   // health
            baseStats[1] = 10f;    // attack
            var set = new ModifierSet();
            Assert.IsTrue(set.Apply(new Modifier { Source = 1, Stat = 1, Op = ModifierOp.Add, Value = 5f, Remaining = -1f }));       // sword
            Assert.IsTrue(set.Apply(new Modifier { Source = 2, Stat = 1, Op = ModifierOp.Multiply, Value = 0.5f, Remaining = 3f }));  // rage
            var s = set.Evaluate(baseStats);
            Assert.AreEqual(22.5f, s[1], 1e-4f, "(10 + 5) x 1.5");
            Assert.AreEqual(100f, s[0]);
            set.Apply(new Modifier { Source = 2, Stat = 1, Op = ModifierOp.Multiply, Value = 0.5f, Remaining = 3f });
            set.Tick(2f);
            Assert.IsTrue(set.HasSource(2), "refreshing did not stack a second copy");
            set.Tick(1.5f);
            Assert.IsFalse(set.HasSource(2), "timed modifier expired");
            Assert.IsTrue(set.HasSource(1), "permanent one stays");
            set.RemoveSource(1);
            Assert.AreEqual(10f, set.Evaluate(baseStats)[1]);
            // A full set of permanent modifiers refuses more.
            for (byte i = 1; i <= ModifierSet.Capacity; i++) Assert.IsTrue(set.Apply(new Modifier { Source = i, Stat = 2, Value = 1f, Remaining = -1f }));
            Assert.IsFalse(set.Apply(new Modifier { Source = 99, Stat = 2, Value = 1f, Remaining = 5f }));
        }

        [Test]
        public void CombatMathIsDeterministicAndBounded()
        {
            Assert.AreEqual(50f, CombatMath.Mitigate(100f, 50f), 1e-4f);
            Assert.AreEqual(100f, CombatMath.Mitigate(100f, 0f), 1e-4f);
            var a = new Random(5);
            var b = new Random(5);
            for (int i = 0; i < 100; i++)
            {
                var x = CombatMath.Roll(20f, 10f, 0.2f, 2f, 0.1f, ref a);
                var y = CombatMath.Roll(20f, 10f, 0.2f, 2f, 0.1f, ref b);
                Assert.AreEqual(x.Amount, y.Amount);
                Assert.GreaterOrEqual(x.Amount, 1f);
            }
            Assert.IsTrue(CombatMath.InArc(float2.zero, new float2(1, 0), new float2(1.5f, 0.3f), 0.3f, 1.5f, 0.5f));
            Assert.IsFalse(CombatMath.InArc(float2.zero, new float2(1, 0), new float2(-1.5f, 0f), 0.3f, 1.5f, 0.5f), "behind");
            Assert.IsTrue(CombatMath.InArc(float2.zero, new float2(1, 0), new float2(-0.1f, 0f), 0.3f, 1.5f, 0.5f), "overlapping always hits");
        }

        [Test]
        public void LevelsAndLootFollowTheirTables()
        {
            var curve = new LevelCurve { Base = 100f, Growth = 1.5f, MaxLevel = 10 };
            int level = 1, xp = 0;
            Assert.AreEqual(2, curve.AddXp(ref level, ref xp, 100 + 150 + 10));
            Assert.AreEqual(3, level);
            Assert.AreEqual(10, xp);

            var table = new[]
            {
                new LootEntry { Item = -1, Weight = 6f },
                new LootEntry { Item = 1, Weight = 3f, MinCount = 1, MaxCount = 5 },
                new LootEntry { Item = 2, Weight = 1f, MinCount = 1, MaxCount = 1 },
            };
            var random = new Random(11);
            var counts = new int[3];
            for (int i = 0; i < 10000; i++) counts[LootTable.Pick(table, ref random)]++;
            Assert.AreEqual(6000, counts[0], 300);
            Assert.AreEqual(3000, counts[1], 300);
            Assert.AreEqual(1000, counts[2], 200);
            for (int i = 0; i < 100; i++)
            {
                int n = LootTable.Count(table[1], ref random);
                Assert.That(n, Is.InRange(1, 5));
            }
        }

        sealed class Profile : ISaveData
        {
            public int Version => 2;
            public int Level;
            public string Name = "";
            public int Gold;   // added in version 2

            public void Write(BinaryWriter w) { w.Write(Level); w.Write(Name); w.Write(Gold); }

            public bool Read(BinaryReader r, int version)
            {
                Level = r.ReadInt32();
                Name = r.ReadString();
                Gold = version >= 2 ? r.ReadInt32() : 0;
                return true;
            }
        }

        [Test]
        public void SaveFilesRoundTripAndRejectCorruption()
        {
            string dir = Path.Combine(Path.GetTempPath(), "spf-save-test-" + System.Guid.NewGuid().ToString("N"));
            var store = new ProfileStore(dir);
            try
            {
                Assert.IsFalse(store.Load("slot", new Profile()), "missing slot");
                store.Save("slot", new Profile { Level = 7, Name = "Hero", Gold = 123 });
                var loaded = new Profile();
                Assert.IsTrue(store.Load("slot", loaded));
                Assert.AreEqual(7, loaded.Level);
                Assert.AreEqual("Hero", loaded.Name);
                Assert.AreEqual(123, loaded.Gold);
                Assert.IsFalse(File.Exists(store.PathOf("slot") + ".tmp"), "temporary file replaced the slot");

                // Flip one payload byte: the checksum rejects the file.
                var bytes = File.ReadAllBytes(store.PathOf("slot"));
                bytes[bytes.Length - 1] ^= 0x5A;
                File.WriteAllBytes(store.PathOf("slot"), bytes);
                Assert.IsFalse(store.Load("slot", new Profile()), "corrupt file rejected");

                // Truncated file.
                File.WriteAllBytes(store.PathOf("slot"), new byte[] { 1, 2, 3 });
                Assert.IsFalse(store.Load("slot", new Profile()));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
