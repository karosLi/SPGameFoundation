using SPF.Contracts;
using SPF.L2.Combat;
using SPF.L2.Progression;
using SPF.Runtime.Scheduling;
using SPF.Runtime.World;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using System.Collections.Generic;

namespace RpgFoundation.Systems
{
    /// <summary>
    /// Resolve (main thread): applies hits in a deterministic order (parallel jobs queue them in thread
    /// order), rolls damage, provokes monsters, kills (death events, hero death ends the run), then spawns
    /// the requested projectiles.
    /// </summary>
    sealed class ResolveSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.Resolve;
        public override void Declare(AccessDeclaration access) { }

        struct HitOrder : IComparer<HitEvent>
        {
            public int Compare(HitEvent a, HitEvent b) =>
                a.TargetRow != b.TargetRow ? a.TargetRow.CompareTo(b.TargetRow)
                : a.AttackerId != b.AttackerId ? a.AttackerId.CompareTo(b.AttackerId)
                : a.Damage.CompareTo(b.Damage);
        }

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var game = world.Resource(RpgKeys.Game);
            var config = world.Resource(RpgKeys.Config);
            var hitQueue = world.Resource(RpgKeys.Hits);
            var feedback = world.Resource(RpgKeys.Feedback);
            var deaths = world.Resource(RpgKeys.Deaths);
            var infos = world.Column(RpgKeys.Info);
            var healths = world.Column(RpgKeys.Health);
            var stats = world.Column(RpgKeys.Stats);
            var combat = world.Column(RpgKeys.Combat);
            var brains = world.Column(RpgKeys.Brain);
            var positions = world.Column(RpgKeys.Position);
            var handles = world.Table(RpgKeys.Actor).Handles;
            int count = world.Table(RpgKeys.Actor).Count;

            var hits = hitQueue.AsArray();
            hits.Sort(new HitOrder());
            for (int h = 0; h < hits.Length; h++)
            {
                var hit = hits[h];
                int row = hit.TargetRow;
                if (row < 0 || row >= count) continue;
                var info = infos[row];
                if (info.Has(ActorFlags.Dead)) continue;
                var random = SimRandom.Create(context.Seed, context.Time.Tick, (uint)h * 7919u + 13u);
                var roll = CombatMath.Roll(hit.Damage, stats[row][Stat.Armour], hit.CritChance, config.Settings.CritMultiplier, 0.12f, ref random);
                var health = healths[row];
                health.Current -= roll.Amount;
                healths[row] = health;
                var c = combat[row];
                c.HitFlash = 0.15f;
                combat[row] = c;
                bool hero = info.Has(ActorFlags.Hero);
                feedback.TryAdd(new FeedbackEvent
                {
                    Kind = hero ? FeedbackKind.HeroHurt : roll.Critical ? FeedbackKind.Crit : FeedbackKind.Damage,
                    Position = hit.Position,
                    Value = roll.Amount,
                });
                if (!hero)
                {
                    var b = brains[row];
                    b.Provoked = 4f;
                    if (b.State == AIState.Idle || b.State == AIState.Return) { b.State = AIState.Chase; b.StateTime = 0f; }
                    brains[row] = b;
                }
                if (health.Current <= 0f)
                {
                    info.Flags |= ActorFlags.Dead;
                    infos[row] = info;
                    feedback.TryAdd(new FeedbackEvent { Kind = FeedbackKind.Death, Position = positions[row], Value = info.Radius });
                    if (hero)
                    {
                        game.Flow = RpgFlow.Dead;
                        game.Message = "You died";
                        game.Version++;
                    }
                    else
                    {
                        deaths.TryAdd(new DeathEvent { Kind = info.Kind, Position = positions[row], Floor = game.Profile.Floor, Boss = info.Has(ActorFlags.Boss) });
                        world.Resource(SimWorld.DestroyQueueKey).Request(handles[row]);
                    }
                }
            }
            hitQueue.Clear();

            var requests = world.Resource(RpgKeys.ProjectileRequests);
            for (int i = 0; i < requests.Count; i++)
                RpgSpawner.SpawnProjectile(world, requests[i]);
            requests.Clear();
            return dependency;
        }
    }

    /// <summary>
    /// Resolve (main thread, after <see cref="ResolveSystem"/>): experience and level-ups, kill counts,
    /// loot drops; then item pickups by the hero.
    /// </summary>
    sealed class RewardSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.Resolve;
        public override int Order => 10;
        public override void Declare(AccessDeclaration access) { }

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var game = world.Resource(RpgKeys.Game);
            var config = world.Resource(RpgKeys.Config);
            var deaths = world.Resource(RpgKeys.Deaths);
            var feedback = world.Resource(RpgKeys.Feedback);
            var profile = game.Profile;
            bool heroAlive = world.Registry.TryResolve(game.Hero, out _, out int heroRow);

            for (int i = 0; i < deaths.Count; i++)
            {
                var death = deaths[i];
                var def = config.Monsters[death.Kind - 1];
                game.MonstersAlive = math.max(game.MonstersAlive - 1, 0);
                if (death.Boss) game.BossAlive = false;
                profile.Kills++;
                int xp = (int)math.round(def.Xp * (1f + 0.25f * (death.Floor - 1)));
                int levels = config.Settings.Levels.AddXp(ref profile.Level, ref profile.Xp, xp);
                if (levels > 0 && heroAlive)
                    LevelUp(world, config, profile, heroRow, feedback);
                DropLoot(world, config, death, context.Seed, context.Time.Tick, (uint)i);
                game.Version++;
            }
            deaths.Clear();

            if (heroAlive && game.Flow == RpgFlow.Playing)
                Pickups(world, config, game, heroRow, feedback);
            return dependency;
        }

        static void LevelUp(SimWorld world, RpgRuntimeConfig config, HeroProfile profile, int row, EventQueue<FeedbackEvent> feedback)
        {
            var baseStats = RpgSpawner.HeroBaseStats(config.Settings, profile.Level);
            world.Column(RpgKeys.BaseStats).Set(row, baseStats);
            var stats = world.Column(RpgKeys.Mods)[row].Evaluate(baseStats);
            world.Column(RpgKeys.Stats).Set(row, stats);
            world.Column(RpgKeys.Health).Set(row, new Health { Current = stats[Stat.MaxHealth], Max = stats[Stat.MaxHealth] });
            feedback.TryAdd(new FeedbackEvent { Kind = FeedbackKind.LevelUp, Position = world.Column(RpgKeys.Position)[row], Value = profile.Level });
        }

        static void DropLoot(SimWorld world, RpgRuntimeConfig config, in DeathEvent death, uint seed, uint tick, uint index)
        {
            var l = config.Loot;
            var random = SimRandom.Create(seed, tick, 0x100u + index);
            int gearTier = math.clamp((death.Floor + 1) / 2 + (death.Boss ? 1 : 0), 1, l.GearTiers);
            if (death.Boss && l.BossGuaranteedGear)
            {
                Drop(world, death.Position, ItemKind.Gear, config.GearId(random.NextBool() ? GearSlot.Weapon : GearSlot.Armour, gearTier), ref random);
                Drop(world, death.Position, ItemKind.Potion, 1, ref random);
            }
            if (random.NextFloat() >= l.DropChance) return;
            System.Span<LootEntry> table = stackalloc LootEntry[3];
            table[0] = new LootEntry { Item = (int)ItemKind.Gold, Weight = l.GoldWeight, MinCount = l.GoldMin, MaxCount = l.GoldMax };
            table[1] = new LootEntry { Item = (int)ItemKind.Potion, Weight = l.PotionWeight, MinCount = 1, MaxCount = 1 };
            table[2] = new LootEntry { Item = (int)ItemKind.Gear, Weight = l.GearWeight, MinCount = 1, MaxCount = 1 };
            int pick = LootTable.Pick(table, ref random);
            if (pick < 0) return;
            var kind = (ItemKind)table[pick].Item;
            int value = kind == ItemKind.Gold ? LootTable.Count(table[pick], ref random) * (1 + death.Floor / 2)
                : kind == ItemKind.Gear ? config.GearId(random.NextBool() ? GearSlot.Weapon : GearSlot.Armour, gearTier)
                : 1;
            Drop(world, death.Position, kind, value, ref random);
        }

        static void Drop(SimWorld world, float2 at, ItemKind kind, int value, ref Random random)
        {
            float2 p = at + random.NextFloat2Direction() * random.NextFloat(0.1f, 0.6f);
            var map = world.Resource(RpgKeys.Map).AsView();
            RpgSpawner.SpawnItem(world, map.IsSolidAt(p) ? at : p, kind, value);
        }

        static void Pickups(SimWorld world, RpgRuntimeConfig config, RpgGameState game, int heroRow, EventQueue<FeedbackEvent> feedback)
        {
            var items = world.Table(RpgKeys.Item);
            var positions = world.Column(RpgKeys.ItemPosition);
            var infos = world.Column(RpgKeys.ItemInfo);
            float2 hero = world.Column(RpgKeys.Position)[heroRow];
            float reach = config.Settings.PickupRadius;
            var profile = game.Profile;
            // Backwards: swap-back removal only moves already-visited rows.
            for (int row = items.Count - 1; row >= 0; row--)
            {
                var info = infos[row];
                info.Age += 1f / 30f;
                infos[row] = info;
                if (info.Age < 0.4f || math.distancesq(positions[row], hero) > reach * reach) continue;
                switch (info.Kind)
                {
                    case ItemKind.Gold:
                        profile.Gold += info.Value;
                        feedback.TryAdd(new FeedbackEvent { Kind = FeedbackKind.Gold, Position = positions[row], Value = info.Value });
                        break;
                    case ItemKind.Potion:
                        profile.Potions += info.Value;
                        feedback.TryAdd(new FeedbackEvent { Kind = FeedbackKind.Item, Position = positions[row], Value = 0 });
                        break;
                    case ItemKind.Gear:
                        if (profile.Inventory.Count >= HeroProfile.MaxInventory && !AutoEquip(world, config, profile, heroRow, info.Value))
                            continue;   // bag full: leave it on the floor
                        if (!AutoEquip(world, config, profile, heroRow, info.Value))
                            profile.Inventory.Add(info.Value);
                        feedback.TryAdd(new FeedbackEvent { Kind = FeedbackKind.Item, Position = positions[row], Value = info.Value });
                        break;
                }
                game.Version++;
                world.DestroyEntity(items.Handles[row]);
            }
        }

        /// <summary>Equips <paramref name="gear"/> when its slot is empty or it is a higher tier; the old piece goes to the bag.</summary>
        static bool AutoEquip(SimWorld world, RpgRuntimeConfig config, HeroProfile profile, int heroRow, int gear)
        {
            var def = config.Gear[gear];
            int current = def.Slot == GearSlot.Weapon ? profile.Weapon : profile.Armour;
            if (current != 0 && config.Gear[current].Tier >= def.Tier) return false;
            if (current != 0)
            {
                if (profile.Inventory.Count >= HeroProfile.MaxInventory) return false;
                profile.Inventory.Add(current);
            }
            InventorySystem.Equip(world, config, profile, heroRow, gear);
            return true;
        }
    }

    /// <summary>ApplyCommands (main thread): equip from the bag (swaps with the equipped piece), potions from the UI.</summary>
    sealed class InventorySystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.ApplyCommands;
        public override int Order => 5;
        public override void Declare(AccessDeclaration access) { }

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var game = world.Resource(RpgKeys.Game);
            var config = world.Resource(RpgKeys.Config);
            bool hero = world.Registry.TryResolve(game.Hero, out _, out int heroRow) && game.Flow == RpgFlow.Playing;
            while (game.InventoryCommands.Count > 0)
            {
                var command = game.InventoryCommands.Dequeue();
                if (!hero) continue;
                var profile = game.Profile;
                if (command.Kind == RpgCommandKind.Equip && command.Argument >= 0 && command.Argument < profile.Inventory.Count)
                {
                    int gear = profile.Inventory[command.Argument];
                    var slot = config.Gear[gear].Slot;
                    int current = slot == GearSlot.Weapon ? profile.Weapon : profile.Armour;
                    profile.Inventory.RemoveAt(command.Argument);
                    if (current != 0) profile.Inventory.Insert(command.Argument, current);
                    Equip(world, config, profile, heroRow, gear);
                    game.Version++;
                }
                else if (command.Kind == RpgCommandKind.UsePotion)
                {
                    var combat = world.Column(RpgKeys.Combat);
                    var c = combat[heroRow];
                    HeroControlSystem.TryDrinkPotion(world, game, heroRow, ref c);
                    combat[heroRow] = c;
                }
            }
            return dependency;
        }

        internal static void Equip(SimWorld world, RpgRuntimeConfig config, HeroProfile profile, int heroRow, int gear)
        {
            if (config.Gear[gear].Slot == GearSlot.Weapon) profile.Weapon = gear; else profile.Armour = gear;
            var mods = world.Column(RpgKeys.Mods);
            var m = mods[heroRow];
            RpgSpawner.ApplyGear(ref m, config, profile.Weapon, profile.Armour);
            mods[heroRow] = m;
            // Keep the health fraction when max health changes.
            var stats = m.Evaluate(world.Column(RpgKeys.BaseStats)[heroRow]);
            world.Column(RpgKeys.Stats).Set(heroRow, stats);
            var healths = world.Column(RpgKeys.Health);
            var h = healths[heroRow];
            float fraction = h.Fraction;
            healths[heroRow] = new Health { Max = stats[Stat.MaxHealth], Current = stats[Stat.MaxHealth] * fraction };
        }
    }

    /// <summary>Resolve (main thread): reaching the stairs (with the boss dead) clears the floor; the last floor wins the run.</summary>
    sealed class StairsSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.Resolve;
        public override int Order => 30;
        public override void Declare(AccessDeclaration access) { }

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var game = world.Resource(RpgKeys.Game);
            if (game.Flow != RpgFlow.Playing || game.BossAlive) return dependency;
            if (!world.Registry.TryResolve(game.Hero, out _, out int row)) return dependency;
            var map = world.Resource(RpgKeys.Map).AsView();
            float2 hero = world.Column(RpgKeys.Position)[row];
            if (math.distance(hero, map.CenterOf(game.StairsCell)) > 0.9f) return dependency;
            game.Profile.HealthFraction = world.Column(RpgKeys.Health)[row].Fraction;
            bool final = game.Profile.Floor >= game.FinalFloor;
            game.Flow = final ? RpgFlow.Victory : RpgFlow.FloorClear;
            game.Message = final ? "Victory! The dungeon is cleared" : $"Floor {game.Profile.Floor} cleared";
            world.Resource(RpgKeys.Feedback).TryAdd(new FeedbackEvent { Kind = FeedbackKind.Stairs, Position = hero });
            game.Version++;
            return dependency;
        }
    }
}
