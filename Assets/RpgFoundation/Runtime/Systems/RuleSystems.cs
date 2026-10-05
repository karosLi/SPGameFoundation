using SPF.Contracts;
using SPF.Contracts.Collections;
using SPF.L2.Stats;
using Unity.Burst;
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
    /// Resolve (Burst job, so the tick keeps overlapping rendering): applies the queued hits in a
    /// deterministic order (parallel jobs queue them in thread order), rolls damage, provokes monsters,
    /// marks the dead (monsters: death event + destroy request; the hero: Dead flag). Everything
    /// structural or main-thread (loot, XP, flow, projectile spawns) happens at the start of the next
    /// tick in ApplyCommands, where no job is running.
    /// </summary>
    sealed class ResolveSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.Resolve;

        public override void Declare(AccessDeclaration access) => access
            .Read(RpgKeys.Actor).Read(RpgKeys.Stats).Read(RpgKeys.Position)
            .Write(RpgKeys.Info).Write(RpgKeys.Health).Write(RpgKeys.Combat).Write(RpgKeys.Brain).Write(RpgKeys.Mods).Write(RpgKeys.Status)
            .Write(RpgKeys.Hits).Write(RpgKeys.Deaths).Write(RpgKeys.Feedback).Write(SimWorld.DestroyQueueKey);

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
            return new ResolveJob
            {
                Hits = world.Resource(RpgKeys.Hits).Raw,
                Deaths = world.Resource(RpgKeys.Deaths).Raw,
                Feedback = world.Resource(RpgKeys.Feedback).Raw,
                Destroy = world.Resource(SimWorld.DestroyQueueKey).AsWriter(),
                Handles = context.Handles(RpgKeys.Actor),
                Info = context.Column(RpgKeys.Info),
                Health = context.Column(RpgKeys.Health),
                Stats = context.Column(RpgKeys.Stats),
                Combat = context.Column(RpgKeys.Combat),
                Brain = context.Column(RpgKeys.Brain),
                Mods = context.Column(RpgKeys.Mods),
                Status = context.Column(RpgKeys.Status),
                Position = context.Column(RpgKeys.Position),
                Count = context.Count(RpgKeys.Actor),
                CritMultiplier = world.Resource(RpgKeys.Config).Settings.CritMultiplier,
                Floor = game.Profile.Floor,
                Seed = context.Seed,
                Tick = context.Time.Tick,
            }.Schedule(dependency);
        }

        [BurstCompile(CompileSynchronously = true)]
        struct ResolveJob : IJob
        {
            public ParallelQueue<HitEvent> Hits;
            public ParallelQueue<DeathEvent> Deaths;
            public ParallelQueue<FeedbackEvent> Feedback;
            public ParallelQueue<EntityHandle>.Writer Destroy;
            [ReadOnly] public NativeArray<EntityHandle> Handles;
            public NativeArray<ActorInfo> Info;
            public NativeArray<Health> Health;
            [ReadOnly] public NativeArray<StatBlock> Stats;
            public NativeArray<CombatState> Combat;
            public NativeArray<Brain> Brain;
            public NativeArray<ModifierSet> Mods;
            public NativeArray<StatusState> Status;
            [ReadOnly] public NativeArray<float2> Position;
            public int Count, Floor;
            public float CritMultiplier;
            public uint Seed, Tick;

            public void Execute()
            {
                var hits = Hits.AsArray();
                hits.Sort(new HitOrder());
                for (int h = 0; h < hits.Length; h++)
                {
                    var hit = hits[h];
                    int row = hit.TargetRow;
                    if (row < 0 || row >= Count) continue;
                    var info = Info[row];
                    if (info.Has(ActorFlags.Dead)) continue;
                    var c = Combat[row];
                    if (c.Invulnerable > 0f) continue;   // dashing: i-frames
                    var random = SimRandom.Create(Seed, Tick, (uint)h * 7919u + 13u);
                    var roll = hit.IgnoreArmour
                        ? new DamageRoll { Amount = hit.Damage }
                        : CombatMath.Roll(hit.Damage, Stats[row][Stat.Armour], hit.CritChance, CritMultiplier, 0.12f, ref random);
                    var health = Health[row];
                    health.Current -= roll.Amount;
                    Health[row] = health;
                    bool hero = info.Has(ActorFlags.Hero);
                    bool boss = info.Has(ActorFlags.Boss);
                    if (hit.Status.Kind != StatusKind.None)
                    {
                        var status = Status[row];
                        status.Apply(hit.Status);
                        Status[row] = status;
                    }
                    if (!hit.IgnoreArmour) c.HitFlash = 0.15f;
                    // Knockback: lighter (smaller) actors fly further; bosses barely move.
                    float weight = math.max(info.Radius * info.Radius * 6f, 0.5f) * (boss ? 4f : 1f);
                    c.Knockback += hit.Direction * (hit.Knockback * (roll.Critical ? 1.5f : 1f) / weight);
                    // Stagger interrupts monsters' attacks (not bosses, not the hero: the player keeps control).
                    if (!hero && !boss && hit.Stagger > 0f && (roll.Critical || hit.Stagger >= 0.2f || c.Phase != ActionPhase.Windup))
                    {
                        c.Phase = ActionPhase.Stagger;
                        c.PhaseTime = 0f;
                        c.PhaseDuration = hit.Stagger * (roll.Critical ? 1.6f : 1f);
                    }
                    Combat[row] = c;
                    if (hit.Mod.Source != 0)
                    {
                        var mods = Mods[row];
                        mods.Apply(hit.Mod);
                        Mods[row] = mods;
                    }
                    Feedback.TryAdd(new FeedbackEvent
                    {
                        Kind = hero ? FeedbackKind.HeroHurt : roll.Critical ? FeedbackKind.Crit : FeedbackKind.Damage,
                        Position = hit.Position,
                        Value = roll.Amount,
                        Direction = hit.Direction,
                        Actor = info.Kind,
                        Source = hit.Source,
                    });
                    if (!hero)
                    {
                        var b = Brain[row];
                        b.Provoked = 4f;
                        if (b.State == AIState.Idle || b.State == AIState.Return) { b.State = AIState.Chase; b.StateTime = 0f; }
                        Brain[row] = b;
                    }
                    if (health.Current > 0f) continue;
                    info.Flags |= ActorFlags.Dead;
                    Info[row] = info;
                    Feedback.TryAdd(new FeedbackEvent { Kind = FeedbackKind.Death, Position = Position[row], Value = info.Radius, Actor = info.Kind, Direction = hit.Direction });
                    if (!hero)
                    {
                        Deaths.TryAdd(new DeathEvent { Kind = info.Kind, Position = Position[row], Floor = Floor, Boss = info.Has(ActorFlags.Boss) });
                        Destroy.TryAdd(Handles[row]);
                    }
                }
                Hits.Clear();
            }
        }
    }

    /// <summary>ApplyCommands (main thread): spawns the projectiles requested by last tick's combat jobs.</summary>
    sealed class ProjectileSpawnSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.ApplyCommands;
        public override int Order => 20;
        public override void Declare(AccessDeclaration access) { }

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var requests = world.Resource(RpgKeys.ProjectileRequests);
            for (int i = 0; i < requests.Count; i++)
                RpgSpawner.SpawnProjectile(world, requests[i]);
            requests.Clear();
            return dependency;
        }
    }

    /// <summary>
    /// ApplyCommands (main thread, start of the tick after the resolve job): the hero's death ends the
    /// run; experience and level-ups, kill counts, loot drops; then item pickups by the hero.
    /// </summary>
    sealed class RewardSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.ApplyCommands;
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
            if (heroAlive && world.Column(RpgKeys.Info)[heroRow].Has(ActorFlags.Dead))
            {
                heroAlive = false;
                if (game.Flow == RpgFlow.Playing)
                {
                    game.Flow = RpgFlow.Dead;
                    game.Message = "You died";
                    game.Version++;
                }
            }

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
            var baseStats = RpgSpawner.HeroBaseStats(config, profile.Level, profile.Weapon);
            world.Column(RpgKeys.Loadout).Set(row, config.HeroLoadout(profile.Level, profile.Weapon));
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
                Drop(world, death.Position, ItemKind.Gear, RandomGear(config, gearTier, ref random), ref random);
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
                : kind == ItemKind.Gear ? RandomGear(config, gearTier, ref random)
                : 1;
            Drop(world, death.Position, kind, value, ref random);
        }

        static int RandomGear(RpgRuntimeConfig config, int tier, ref Random random)
        {
            // Half armour, half one of the lootable weapon families.
            if (random.NextBool() || config.LootWeapons.Length == 0) return config.ArmourId(tier);
            return config.GearId(config.LootWeapons[random.NextInt(config.LootWeapons.Length)], tier);
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
            // Another weapon family is a play-style choice: it goes to the bag unless the hand is empty.
            if (current != 0 && def.Slot == GearSlot.Weapon && config.Gear[current].Weapon != def.Weapon) return false;
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
            // The weapon family sets attack rate and reach; the loadout tells combat what to swing.
            var baseStats = RpgSpawner.HeroBaseStats(config, profile.Level, profile.Weapon);
            world.Column(RpgKeys.BaseStats).Set(heroRow, baseStats);
            var loadout = world.Column(RpgKeys.Loadout);
            var l = loadout[heroRow];
            l.Weapon = config.HeroWeapon(profile.Weapon);
            loadout[heroRow] = l;
            // Keep the health fraction when max health changes.
            var stats = m.Evaluate(baseStats);
            world.Column(RpgKeys.Stats).Set(heroRow, stats);
            var healths = world.Column(RpgKeys.Health);
            var h = healths[heroRow];
            float fraction = h.Fraction;
            healths[heroRow] = new Health { Max = stats[Stat.MaxHealth], Current = stats[Stat.MaxHealth] * fraction };
        }
    }

    /// <summary>ApplyCommands (main thread): reaching the stairs (with the boss dead) clears the floor; the last floor wins the run.</summary>
    sealed class StairsSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.ApplyCommands;
        public override int Order => 15;
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
