using NUnit.Framework;
using SPF.Contracts;
using SPF.L2.Buffs;
using SPF.L2.Collision;
using Unity.Mathematics;

namespace SnakeFoundation.Tests
{
    public class SnakeGameplayTests
    {
        [Test]
        public void StartSpawnsPlayerAndCommandSteersIt()
        {
            using var t = new SnakeTestWorld();
            Assert.AreEqual(GameFlow.Attract, t.Game.Flow);
            t.StartPlayer();
            Assert.AreEqual(GameFlow.Playing, t.Game.Flow);
            Assert.GreaterOrEqual(t.PlayerRow, 0);

            t.Place(t.Game.Player, new float2(0, 0), new float2(1, 0));
            t.Game.Command = new PlayerCommand { Direction = new float2(0, 1) };
            t.Step(30); // one second
            var heading = t.World.Column(SnakeKeys.Heading)[t.PlayerRow];
            Assert.Greater(heading.y, 0.99f, "turned to the commanded direction");
            var head = t.World.Column(SnakeKeys.Head)[t.PlayerRow];
            Assert.Greater(head.y, 3f);
        }

        [Test]
        public void EatingFoodGrowsTheSnakeAndRemovesTheFood()
        {
            using var t = new SnakeTestWorld();
            t.StartPlayer();
            t.Place(t.Game.Player, new float2(0, 0), new float2(1, 0));
            float massBefore = t.World.Column(SnakeKeys.Mass)[t.PlayerRow];
            var population = t.World.Resource(SnakeKeys.Populations).Food[0];
            for (int i = 1; i <= 5; i++)
                Assert.IsTrue(SnakeSpawner.SpawnFood(t.World, population, t.Runtime.Settings, new float2(i * 1.5f, 0), 2f, 0));
            Assert.AreEqual(5, t.World.Table(SnakeKeys.Food).Count);

            t.Step(45);

            Assert.AreEqual(0, t.World.Table(SnakeKeys.Food).Count, "all pellets on the path were eaten");
            float expected = massBefore + 5 * 2f * t.Runtime.Settings.MassPerFoodValue;
            Assert.AreEqual(expected, t.World.Column(SnakeKeys.Mass)[t.PlayerRow], 0.01f);
        }

        [Test]
        public void HittingTheWallEndsTheGameAndDropsFood()
        {
            using var t = new SnakeTestWorld();
            t.StartPlayer();
            var region = t.Runtime.Regions[0];
            t.Place(t.Game.Player, new float2(region.Max.x - 3f, 0), new float2(1, 0));
            t.Step(20);
            Assert.AreEqual(GameFlow.GameOver, t.Game.Flow);
            Assert.AreEqual(DeathCause.Wall, t.Game.LastDeathCause);
            Assert.IsTrue(t.Game.Player.IsNull);
            Assert.Greater(t.World.Table(SnakeKeys.Food).Count, 0, "the body turned into food");
            Assert.AreEqual(0, t.World.Table(SnakeKeys.Snake).Count);
        }

        [Test]
        public void RunningIntoAnotherBodyKillsAndCreditsTheOwner()
        {
            using var t = new SnakeTestWorld();
            t.StartPlayer();
            // AI lies horizontally from (20,0) back to the left; player comes from below heading up into it.
            var ai = t.SpawnAI(new float2(20, 10), new float2(1, 0), 60f);
            t.World.Column(SnakeKeys.Control).Set(t.Row(ai), new SnakeControl { TargetDirection = new float2(1, 0) });
            t.Place(t.Game.Player, new float2(15, 5), new float2(0, 1));
            t.Game.Command = new PlayerCommand { Direction = new float2(0, 1) };
            t.Step(30);
            Assert.AreEqual(GameFlow.GameOver, t.Game.Flow);
            Assert.AreEqual(DeathCause.Body, t.Game.LastDeathCause);
            int aiRow = t.Row(ai);
            Assert.GreaterOrEqual(aiRow, 0, "the AI survived");
            Assert.AreEqual(1, t.World.Column(SnakeKeys.Info)[aiRow].Kills);
        }

        [Test]
        public void HeadOnTheLighterSnakeDies()
        {
            Assert.AreEqual(CollisionOutcome.FirstDies, ChainCollisionRules.HeadOn(10f, 20f, 0.05f));
            Assert.AreEqual(CollisionOutcome.SecondDies, ChainCollisionRules.HeadOn(30f, 20f, 0.05f));
            Assert.AreEqual(CollisionOutcome.BothDie, ChainCollisionRules.HeadOn(20f, 20.5f, 0.05f));

            using var t = new SnakeTestWorld();
            var heavy = t.SpawnAI(new float2(0, 0), new float2(1, 0), 80f);
            var light = t.SpawnAI(new float2(6, 0), new float2(-1, 0), 20f);
            t.World.Column(SnakeKeys.Control).Set(t.Row(heavy), new SnakeControl { TargetDirection = new float2(1, 0) });
            t.World.Column(SnakeKeys.Control).Set(t.Row(light), new SnakeControl { TargetDirection = new float2(-1, 0) });
            // AI steering would dodge; freeze decisions by making both non-AI for this scenario.
            foreach (var h in new[] { heavy, light })
            {
                var info = t.World.Column(SnakeKeys.Info)[t.Row(h)];
                info.Flags &= ~SnakeFlags.AI;
                t.World.Column(SnakeKeys.Info).Set(t.Row(h), info);
            }
            t.Step(20);
            Assert.GreaterOrEqual(t.Row(heavy), 0);
            Assert.AreEqual(-1, t.Row(light));
        }

        /// <summary>
        /// Head-on outcome must not depend on approach direction or row order: touching heads also overlap
        /// each other's neck nodes, and the contact used to be classified by whichever cell was scanned first.
        /// </summary>
        [TestCase(0, false)] [TestCase(90, false)] [TestCase(180, false)] [TestCase(270, false)]
        [TestCase(0, true)] [TestCase(90, true)] [TestCase(180, true)] [TestCase(270, true)]
        [TestCase(45, false)] [TestCase(225, true)]
        public void HeadOnOutcomeIsIndependentOfDirection(int degrees, bool lightFirst)
        {
            // Heads start already overlapping deeply (as after a boost or a diagonal brush), so each head
            // also overlaps the other snake's first neck nodes on the very first contact tick.
            const float Gap = 1.2f;
            using var t = new SnakeTestWorld();
            float a = math.radians(degrees);
            float2 dir = new float2(math.cos(a), math.sin(a));
            // Just past a 4-unit cell boundary: for 180/270 degrees the light head lands right above the
            // boundary and its neck in the cell before it, which a cell-order scan visits first.
            float2 origin = new float2(41.5f, 41.5f);
            EntityHandle heavy, light;
            if (lightFirst)
            {
                light = t.SpawnAI(origin + dir * Gap, -dir, 20f);
                heavy = t.SpawnAI(origin, dir, 80f);
            }
            else
            {
                heavy = t.SpawnAI(origin, dir, 80f);
                light = t.SpawnAI(origin + dir * Gap, -dir, 20f);
            }
            foreach (var h in new[] { heavy, light })
            {
                int row = t.Row(h);
                var info = t.World.Column(SnakeKeys.Info)[row];
                info.Flags &= ~SnakeFlags.AI;
                t.World.Column(SnakeKeys.Info).Set(row, info);
                t.World.Column(SnakeKeys.Control).Set(row, new SnakeControl { TargetDirection = h == heavy ? dir : -dir });
            }
            t.Step(20);
            Assert.GreaterOrEqual(t.Row(heavy), 0, "the heavier snake wins a head-on collision");
            Assert.AreEqual(-1, t.Row(light));
            Assert.AreEqual(1, t.World.Column(SnakeKeys.Info)[t.Row(heavy)].Kills);
        }

        [Test]
        public void ShieldAbsorbsOneLethalHit()
        {
            using var t = new SnakeTestWorld();
            t.StartPlayer();
            var region = t.Runtime.Regions[0];
            t.Place(t.Game.Player, new float2(region.Max.x - 3f, 0), new float2(1, 0));
            var buffs = t.World.Column(SnakeKeys.Buffs)[t.PlayerRow];
            buffs.Apply(3, 10f); // Shield (default config)
            t.World.Column(SnakeKeys.Buffs).Set(t.PlayerRow, buffs);
            t.Step(20);
            Assert.AreEqual(GameFlow.Playing, t.Game.Flow, "the shield saved the player");
            Assert.IsFalse(t.World.Column(SnakeKeys.Buffs)[t.PlayerRow].Has(3), "and was consumed");
        }

        [Test]
        public void BoostDrainsMassAndDropsFood()
        {
            using var t = new SnakeTestWorld();
            t.StartPlayer();
            t.Place(t.Game.Player, new float2(0, 0), new float2(1, 0));
            t.World.Column(SnakeKeys.Mass).Set(t.PlayerRow, 50f);
            t.Game.Command = new PlayerCommand { Direction = new float2(1, 0), Boost = true };
            t.Step(60);
            Assert.Less(t.World.Column(SnakeKeys.Mass)[t.PlayerRow], 50f - 4f);
            Assert.Greater(t.World.Table(SnakeKeys.Food).Count, 2);
            Assert.AreEqual(t.Runtime.Settings.BoostSpeed, t.World.Column(SnakeKeys.Speed)[t.PlayerRow], 0.01f);
        }

        [Test]
        public void SkillProjectileSlowsTheTarget()
        {
            using var t = new SnakeTestWorld();
            t.StartPlayer();
            t.Place(t.Game.Player, new float2(0, 0), new float2(1, 0));
            t.World.Column(SnakeKeys.Mass).Set(t.PlayerRow, 60f);
            var target = t.SpawnAI(new float2(12, 8), new float2(0, -1), 40f);
            var info = t.World.Column(SnakeKeys.Info)[t.Row(target)];
            info.Flags &= ~SnakeFlags.AI;
            t.World.Column(SnakeKeys.Info).Set(t.Row(target), info);
            t.World.Column(SnakeKeys.Control).Set(t.Row(target), new SnakeControl { TargetDirection = new float2(0, 1) });
            t.Place(target, new float2(12, 8), new float2(0, 1)); // body hangs down across y = 0 .. 8

            t.Game.Command = new PlayerCommand { Direction = new float2(1, 0), Skill = true };
            t.Step(2);
            t.Game.Command = new PlayerCommand { Direction = new float2(0, -1) };
            bool slowed = false;
            for (int i = 0; i < 20 && !slowed; i++)
            {
                t.Step();
                int row = t.Row(target);
                slowed = row >= 0 && t.World.Column(SnakeKeys.Buffs)[row].Has(4);
            }
            Assert.IsTrue(slowed, "the projectile hit and applied Slow");
            Assert.Less(t.World.Column(SnakeKeys.Mass)[t.PlayerRow], 60f, "casting cost mass");
        }

        [Test]
        public void PickingUpAPropAppliesItsBuff()
        {
            using var t = new SnakeTestWorld();
            t.StartPlayer();
            t.Place(t.Game.Player, new float2(0, 0), new float2(1, 0));
            var random = new Random(3);
            var props = t.World.Resource(SnakeKeys.Populations).Props[0];
            Assert.IsTrue(SnakeSpawner.SpawnProp(t.World, t.Runtime, props, new float2(3, 0), ref random));
            byte kind = t.World.Column(SnakeKeys.PropInfo)[0].Kind;
            byte buff = t.Runtime.Props[kind - 1].BuffKind;
            t.Step(15);
            Assert.AreEqual(0, t.World.Table(SnakeKeys.Prop).Count);
            Assert.IsTrue(t.World.Column(SnakeKeys.Buffs)[t.PlayerRow].Has(buff));
        }

        [Test]
        public void BuffSetRefreshesAndExpires()
        {
            var set = new BuffSet();
            set.Apply(1, 2f);
            set.Apply(1, 5f);
            int count = 0;
            for (int i = 0; i < BuffSet.Capacity; i++) if (set.Get(i).Kind == 1) count++;
            Assert.AreEqual(1, count, "re-applying refreshes instead of stacking");
            set.Tick(4.9f);
            Assert.IsTrue(set.Has(1));
            set.Tick(0.2f);
            Assert.IsFalse(set.Has(1));
        }
    }
}
