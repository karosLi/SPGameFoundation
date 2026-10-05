using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using SPF.Contracts;
using SPF.L1.Navigation;
using SPF.L1.Spatial;
using SPF.Runtime.Persistence;
using Unity.Mathematics;

namespace RpgFoundation.Tests
{
    public class RpgGameTests
    {
        [Test]
        public void FloorsAreConnectedAndDeterministic()
        {
            var config = RpgConfig.CreateDefault();
            var rooms = new List<Room>();
            using var map = new TileMap(new int2(config.Dungeon.Width, config.Dungeon.Height), 1f);
            using var other = new TileMap(map.Size, 1f);
            using var field = new FlowField(map.Size);
            for (uint seed = 1; seed <= 6; seed++)
            for (int floor = 1; floor <= 4; floor++)
            {
                DungeonGenerator.Generate(map, seed, floor, config.Dungeon, rooms, out var start, out var stairs, out _);
                Assert.GreaterOrEqual(rooms.Count, config.Dungeon.RoomsMin - 2, "enough rooms placed");
                field.ScheduleBuild(map.AsView(), new[] { start }, default).Complete();
                int walkable = 0;
                for (int i = 0; i < map.Tiles.Length; i++)
                {
                    if (map.Tiles[i] != DungeonGenerator.Floor) continue;
                    walkable++;
                    Assert.AreNotEqual(FlowField.Unreachable, field.Distance[i], $"seed {seed} floor {floor}: tile {i} unreachable");
                }
                Assert.Greater(walkable, 300);
                Assert.Greater(field.Distance[stairs.y * map.Size.x + stairs.x], 10, "stairs are not next to the start");

                DungeonGenerator.Generate(other, seed, floor, config.Dungeon, rooms, out var start2, out var stairs2, out _);
                Assert.AreEqual(start, start2);
                Assert.AreEqual(stairs, stairs2);
                for (int i = 0; i < map.Tiles.Length; i++) Assert.AreEqual(map.Tiles[i], other.Tiles[i]);
            }
            UnityEngine.Object.DestroyImmediate(config);
        }

        [Test]
        public void NewGameBuildsTheFirstFloor()
        {
            using var t = new RpgTestWorld();
            Assert.AreEqual(RpgFlow.Playing, t.Game.Flow);
            Assert.GreaterOrEqual(t.HeroRow, 0, "hero spawned");
            Assert.Greater(t.Game.MonstersAlive, 5, "monsters spawned");
            var map = t.World.Resource(RpgKeys.Map).AsView();
            Assert.IsFalse(map.IsSolidAt(t.HeroPosition));
            var positions = t.World.Column(RpgKeys.Position);
            for (int i = 0; i < t.World.Table(RpgKeys.Actor).Count; i++)
                Assert.IsFalse(map.IsSolidAt(positions[i]), $"actor {i} inside a wall");
        }

        [Test]
        public void MonstersChaseAndHurtAnIdleHero()
        {
            using var t = new RpgTestWorld();
            t.ClearMonsters();
            t.SpawnMonster(1, t.FreeSpotNearHero(4f));   // slime in sight
            float before = t.World.Column(RpgKeys.Health)[t.HeroRow].Current;
            t.Step(150);
            float after = t.World.Column(RpgKeys.Health)[t.HeroRow].Current;
            Assert.Less(after, before - 5f, "the slime walked over and attacked");
            Assert.Greater(t.World.Resource(RpgKeys.Feedback).Count, 0, "damage feedback for presentation");
        }

        [Test]
        public void KillingMonstersGivesExperienceAndLoot()
        {
            using var t = new RpgTestWorld(tweak: c => { c.Loot.DropChance = 1f; c.Loot.GearWeight = 0f; c.Loot.PotionWeight = 0f; });
            t.ClearMonsters();
            for (int i = 0; i < 3; i++) t.SpawnMonster(1, t.FreeSpotNearHero(0.9f + i * 0.1f));
            t.Game.MonstersAlive = 3;
            for (int i = 0; i < 300 && t.Game.MonstersAlive > 0; i++)
            {
                t.Input(new InputFrame { Held = 1u << RpgButton.Attack });
                t.Step();
            }
            Assert.AreEqual(0, t.Game.MonstersAlive, "the hero killed all three slimes");
            Assert.AreEqual(3, t.Game.Profile.Kills);
            Assert.Greater(t.Game.Profile.Xp + (t.Game.Profile.Level - 1) * 1000, 0, "experience gained");
            // Walk over the drops: they are picked up on contact.
            for (int i = 0; i < 200 && t.World.Table(RpgKeys.Item).Count > 0; i++)
            {
                float2 item = t.World.Column(RpgKeys.ItemPosition)[0];
                t.Input(new InputFrame { Move = math.normalizesafe(item - t.HeroPosition) });
                t.Step();
            }
            Assert.Greater(t.Game.Profile.Gold, 0, "gold picked up");
            Assert.AreEqual(0, t.World.Table(RpgKeys.Item).Count, "all drops collected");
        }

        [Test]
        public void FireballsHitAtRangeButNotThroughWalls()
        {
            using var t = new RpgTestWorld();
            t.ClearMonsters();
            var target = t.SpawnMonster(3, t.FreeSpotNearHero(5f));   // brute, slow
            var row = t.Row(target);
            float before = t.World.Column(RpgKeys.Health)[row].Current;
            t.Input(new InputFrame { Pressed = 1u << RpgButton.Skill });
            t.Step(20);
            row = t.Row(target);
            Assert.GreaterOrEqual(row, 0);
            Assert.Less(t.World.Column(RpgKeys.Health)[row].Current, before, "the fireball hit");

            // A projectile fired straight into a wall disappears without hitting anything behind it.
            var map = t.World.Resource(RpgKeys.Map).AsView();
            float2 hero = t.HeroPosition;
            float2 wallDir = float2.zero;
            for (int k = 0; k < 4 && math.all(wallDir == 0f); k++)
            {
                float2 d = k == 0 ? new float2(1, 0) : k == 1 ? new float2(-1, 0) : k == 2 ? new float2(0, 1) : new float2(0, -1);
                for (float s = 1f; s < 12f; s += 0.5f)
                    if (map.IsSolidAt(hero + d * s)) { wallDir = d; break; }
            }
            RpgSpawner.SpawnProjectile(t.World, new ProjectileRequest { Position = hero, Direction = wallDir, Speed = 20f, Damage = 50f, Team = Team.Hero });
            int projectiles = t.World.Table(RpgKeys.Projectile).Count;
            Assert.Greater(projectiles, 0);
            t.Step(30);
            Assert.AreEqual(0, t.World.Table(RpgKeys.Projectile).Count, "projectiles stop at walls");
        }

        [Test]
        public void EquippingGearChangesStatsAndPotionsHeal()
        {
            using var t = new RpgTestWorld();
            t.ClearMonsters();
            float attack = t.World.Column(RpgKeys.Stats)[t.HeroRow][Stat.Attack];
            int sword = t.Runtime.GearId(GearSlot.Weapon, 3);
            t.Game.Profile.Inventory.Add(sword);
            t.Game.Send(RpgCommandKind.Equip, 0);
            t.Step(2);
            Assert.AreEqual(sword, t.Game.Profile.Weapon);
            Assert.AreEqual(attack + t.Runtime.Gear[sword].Attack, t.World.Column(RpgKeys.Stats)[t.HeroRow][Stat.Attack], 1e-3f);
            Assert.AreEqual(0, t.Game.Profile.Inventory.Count);

            var health = t.World.Column(RpgKeys.Health);
            var h = health[t.HeroRow];
            h.Current = h.Max * 0.2f;
            health[t.HeroRow] = h;
            int potions = t.Game.Profile.Potions;
            t.Input(new InputFrame { Pressed = 1u << RpgButton.Potion });
            t.Step();
            Assert.AreEqual(potions - 1, t.Game.Profile.Potions);
            Assert.Greater(health[t.HeroRow].Current, h.Max * 0.6f);
        }

        [Test]
        public void StairsClearTheFloorAndDescendingKeepsProgress()
        {
            using var t = new RpgTestWorld();
            t.ClearMonsters();
            t.Game.Profile.Gold = 77;
            var map = t.World.Resource(RpgKeys.Map).AsView();
            t.World.Column(RpgKeys.Position).Set(t.HeroRow, map.CenterOf(t.Game.StairsCell));
            t.Step();
            Assert.AreEqual(RpgFlow.FloorClear, t.Game.Flow);
            int builds = t.Game.FloorBuilds;
            t.Game.Send(RpgCommandKind.Descend);
            t.Step();
            Assert.AreEqual(RpgFlow.Playing, t.Game.Flow);
            Assert.AreEqual(2, t.Game.Profile.Floor);
            Assert.AreEqual(77, t.Game.Profile.Gold, "progress carried over");
            Assert.AreEqual(builds + 1, t.Game.FloorBuilds);
            Assert.AreEqual(2, t.Game.FloorStart.Floor, "floor start snapshot for retry / save");
        }

        [Test]
        public void DeathAndRetryRestoreTheFloorStart()
        {
            using var t = new RpgTestWorld();
            t.ClearMonsters();
            t.Game.Profile.Gold = 999;   // earned after the floor started: lost on retry
            var health = t.World.Column(RpgKeys.Health);
            var h = health[t.HeroRow];
            h.Current = 1f;
            health[t.HeroRow] = h;
            t.SpawnMonster(3, t.FreeSpotNearHero(1.2f));
            t.Step(120);
            Assert.AreEqual(RpgFlow.Dead, t.Game.Flow);
            t.Game.Send(RpgCommandKind.Retry);
            t.Step();
            Assert.AreEqual(RpgFlow.Playing, t.Game.Flow);
            Assert.AreEqual(0, t.Game.Profile.Gold);
            Assert.Greater(t.Game.MonstersAlive, 0, "floor rebuilt with its monsters");
        }

        [Test]
        public void ProfilesSaveAndContinueOnTheSameFloor()
        {
            string dir = Path.Combine(Path.GetTempPath(), "rpg-save-" + System.Guid.NewGuid().ToString("N"));
            var store = new ProfileStore(dir);
            try
            {
                int2 stairs;
                using (var t = new RpgTestWorld(runSeed: 1234))
                {
                    t.Game.Profile.Floor = 3;
                    t.Game.Profile.Gold = 50;
                    t.Game.Profile.Inventory.Add(t.Runtime.GearId(GearSlot.Armour, 2));
                    t.Game.Send(RpgCommandKind.Continue);
                    t.Step();
                    stairs = t.Game.StairsCell;
                    store.Save("hero", t.Game.FloorStart);
                }
                using (var t = new RpgTestWorld(start: false))
                {
                    Assert.IsTrue(store.Load("hero", t.Game.Profile));
                    t.Game.Send(RpgCommandKind.Continue);
                    t.Step();
                    Assert.AreEqual(3, t.Game.Profile.Floor);
                    Assert.AreEqual(50, t.Game.Profile.Gold);
                    Assert.AreEqual(1, t.Game.Profile.Inventory.Count);
                    Assert.AreEqual(stairs, t.Game.StairsCell, "same run seed and floor: same map");
                }
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        static (float2 hero, float health, int kills, int xp, int gold) Run(uint seed, int ticks)
        {
            using var t = new RpgTestWorld(seed: seed, runSeed: seed * 3 + 1);
            using var bot = new RpgBot();
            for (int i = 0; i < ticks && t.Game.Flow == RpgFlow.Playing; i++)
            {
                t.Input(bot.Think(t.World));
                t.Step();
            }
            int row = t.HeroRow;
            return (row >= 0 ? t.HeroPosition : float2.zero, row >= 0 ? t.World.Column(RpgKeys.Health)[row].Current : 0f,
                t.Game.Profile.Kills, t.Game.Profile.Xp, t.Game.Profile.Gold);
        }

        /// <summary>The bot plays a floor: it must make progress, and two runs with the same seed must match exactly.</summary>
        [Test]
        public void BotRunsAreDeterministicAndMakeProgress()
        {
            var a = Run(5, 1500);
            var b = Run(5, 1500);
            Assert.AreEqual(a, b, "same seed and inputs: identical outcome");
            Assert.Greater(a.kills, 3, "the bot fights its way through the floor");
            TestContext.WriteLine($"bot: kills {a.kills}, xp {a.xp}, gold {a.gold}, hero health {a.health:F1}");
        }

        /// <summary>The bot can finish floors: clear them (or reach the stairs) and descend.</summary>
        [Test]
        public void BotClearsFloors()
        {
            using var t = new RpgTestWorld(seed: 3, runSeed: 77);
            using var bot = new RpgBot();
            int cleared = 0;
            for (int i = 0; i < 9000 && cleared < 2; i++)
            {
                if (t.Game.Flow == RpgFlow.FloorClear) { cleared++; t.Game.Send(RpgCommandKind.Descend); }
                else if (t.Game.Flow == RpgFlow.Dead) t.Game.Send(RpgCommandKind.Retry);
                t.Input(bot.Think(t.World));
                t.Step();
            }
            TestContext.WriteLine($"floor {t.Game.Profile.Floor}, level {t.Game.Profile.Level}, kills {t.Game.Profile.Kills}, gold {t.Game.Profile.Gold}");
            Assert.AreEqual(2, cleared, "two floors cleared within five simulated minutes");
        }
    
        [Test]
        public void ElitesLeadPacksWithAffixesAndDropGear()
        {
            using var t = new RpgTestWorld(tweak: c => { c.Dungeon.EliteChance = 1f; c.Dungeon.EliteFromFloor = 1; c.Loot.DropChance = 0f; });
            var infos = t.World.Column(RpgKeys.Info);
            int elites = 0, eliteRow = -1;
            for (int row = 0; row < t.World.Table(RpgKeys.Actor).Count; row++)
            {
                if (!infos[row].Has(ActorFlags.Elite)) continue;
                elites++;
                eliteRow = row;
                Assert.AreNotEqual(Affix.None, infos[row].Affixes, "elites have affixes");
                var def = t.Runtime.Monsters[infos[row].Kind - 1];
                Assert.Greater(t.World.Column(RpgKeys.Health)[row].Max, def.Health * 2f, "elites are tougher");
            }
            Assert.Greater(elites, 2, "one elite per pack");

            var affixes = infos[eliteRow].Affixes;
            if ((affixes & (Affix.Burning | Affix.Venomous)) != 0)
                Assert.AreNotEqual(StatusKind.None, t.World.Column(RpgKeys.Status)[eliteRow].OnHit, "elemental affixes add a status");

            // Killing an elite: guaranteed gear and gold, triple XP.
            int xp = t.Game.Profile.Xp, level = t.Game.Profile.Level;
            var healths = t.World.Column(RpgKeys.Health);
            var h = healths[eliteRow]; h.Current = 0.5f; healths[eliteRow] = h;
            t.World.Resource(RpgKeys.Hits).TryAdd(new HitEvent { AttackerId = -1, TargetRow = eliteRow, Damage = 5f, IgnoreArmour = true });
            t.Step(2);
            bool gear = false, gold = false;
            var items = t.World.Column(RpgKeys.ItemInfo);
            for (int i = 0; i < t.World.Table(RpgKeys.Item).Count; i++)
            {
                gear |= items[i].Kind == ItemKind.Gear;
                gold |= items[i].Kind == ItemKind.Gold;
            }
            Assert.IsTrue(gear && gold, "elite loot");
            Assert.IsTrue(t.Game.Profile.Level > level || t.Game.Profile.Xp - xp >= 20, "elite XP");
        }

        [Test]
        public void RandomAffixesAreDistinctAndElementsExclusive()
        {
            var random = new Random(5);
            for (int i = 0; i < 200; i++)
            {
                var a = RpgSpawner.RandomAffixes(3, ref random);
                Assert.AreEqual(3, math.countbits((int)a));
                Assert.IsFalse((a & Affix.Burning) != 0 && (a & Affix.Venomous) != 0);
            }
        }
    
        static int CountProps(RpgTestWorld t, PropKind kind)
        {
            int n = 0;
            var infos = t.World.Column(RpgKeys.PropInfo);
            for (int i = 0; i < t.World.Table(RpgKeys.Prop).Count; i++) if (infos[i].Kind == kind) n++;
            return n;
        }

        static int CountItems(RpgTestWorld t)
        {
            return t.World.Table(RpgKeys.Item).Count;
        }

        [Test]
        public void FloorsHaveChestsBarrelsAndLaterTraps()
        {
            using var t = new RpgTestWorld(tweak: c => { c.Dungeon.ChestChance = 1f; c.Dungeon.BarrelsPerRoom = 4; });
            Assert.Greater(CountProps(t, PropKind.Chest), 3);
            Assert.Greater(CountProps(t, PropKind.Barrel), 3);
            Assert.AreEqual(0, CountProps(t, PropKind.Spikes), "no traps on floor 1");
            var map = t.World.Resource(RpgKeys.Map).AsView();
            var positions = t.World.Column(RpgKeys.PropPosition);
            for (int i = 0; i < t.World.Table(RpgKeys.Prop).Count; i++)
                Assert.IsFalse(map.IsSolidAt(positions[i]), "props stand on floor tiles");

            t.Game.FloorStart.Floor = 3;
            t.Game.Send(RpgCommandKind.Retry);   // rebuilds the floor from the floor-start profile
            t.Step();
            Assert.AreEqual(3, t.Game.Profile.Floor);
            Assert.Greater(CountProps(t, PropKind.Spikes), 0, "traps deeper down");
        }

        [Test]
        public void ChestsOpenOnTouchAndDropLoot()
        {
            using var t = new RpgTestWorld();
            t.ClearMonsters();
            RpgSpawner.SpawnProp(t.World, t.FreeSpotNearHero(0.5f), PropKind.Chest);
            int row = t.World.Table(RpgKeys.Prop).Count - 1;
            int items = CountItems(t);
            int gold = t.Game.Profile.Gold;
            t.Step();
            Assert.IsTrue(t.World.Column(RpgKeys.PropInfo)[row].Active, "opened");
            t.Step(30);
            Assert.Greater(t.Game.Profile.Gold + CountItems(t), gold + items, "gold dropped (and picked up)");
        }

        [Test]
        public void BarrelsBreakUnderTheHerosStrike()
        {
            using var t = new RpgTestWorld();
            t.ClearMonsters();
            int before = CountProps(t, PropKind.Barrel);
            var spot = t.FreeSpotNearHero(0.9f);
            RpgSpawner.SpawnProp(t.World, spot, PropKind.Barrel);
            var aim = math.normalize(spot - t.HeroPosition);
            for (int i = 0; i < 30; i++) { t.Input(new InputFrame { Held = 1u << RpgButton.Attack, Aim = aim, Move = aim * 0.02f }); t.Step(); }
            Assert.AreEqual(before, CountProps(t, PropKind.Barrel), "broken");
        }

        [Test]
        public void SpikesHurtWhoeverStandsOnThemWhenTheyRise()
        {
            using var t = new RpgTestWorld();
            t.ClearMonsters();
            RpgSpawner.SpawnProp(t.World, t.HeroPosition, PropKind.Spikes);
            float before = t.World.Column(RpgKeys.Health)[t.HeroRow].Current;
            bool rose = false;
            for (int i = 0; i < 90; i++)
            {
                t.Step();
                rose |= t.World.Column(RpgKeys.PropInfo)[t.World.Table(RpgKeys.Prop).Count - 1].Active;
            }
            Assert.IsTrue(rose, "the spikes cycle");
            Assert.Less(t.World.Column(RpgKeys.Health)[t.HeroRow].Current, before - 3f, "impaled");
        }
    
        [Test]
        public void TheMerchantSellsBetweenFloorsOnly()
        {
            using var t = new RpgTestWorld();
            var profile = t.Game.Profile;
            profile.Gold = 1000;
            int potions = profile.Potions;
            t.Game.Send(RpgCommandKind.Buy, 0);
            t.Step();
            Assert.AreEqual(potions, profile.Potions, "no shopping while monsters are about");

            t.Game.Flow = RpgFlow.FloorClear;
            var potion = RpgShop.Offer(t.Runtime, profile, 0);
            t.Game.Send(RpgCommandKind.Buy, 0);
            t.Game.Send(RpgCommandKind.Buy, 0);
            t.Step();
            Assert.AreEqual(potions + 2, profile.Potions, "potions are repeatable");
            Assert.AreEqual(1000 - 2 * potion.Price, profile.Gold);

            var armour = RpgShop.Offer(t.Runtime, profile, 1);
            Assert.AreEqual(GearSlot.Armour, t.Runtime.Gear[armour.Value].Slot);
            t.Game.Send(RpgCommandKind.Buy, 1);
            t.Step();
            Assert.AreEqual(armour.Value, profile.Armour, "better armour is equipped at once");
            int gold = profile.Gold;
            t.Game.Send(RpgCommandKind.Buy, 1);
            t.Step();
            Assert.AreEqual(gold, profile.Gold, "gear sells once per floor");
            Assert.IsTrue(RpgShop.Sold(t.Game.ShopBought, 1));

            profile.Gold = 1;
            t.Game.Send(RpgCommandKind.Buy, 2);
            t.Step();
            Assert.AreEqual(1, profile.Gold, "not enough gold");
            Assert.IsFalse(RpgShop.Sold(t.Game.ShopBought, 2));

            var weapon = RpgShop.Offer(t.Runtime, profile, 2);
            Assert.AreEqual(t.Runtime.HeroWeapon(profile.Weapon), t.Runtime.Gear[weapon.Value].Weapon, "the hero's weapon family");
        }
    }
}
