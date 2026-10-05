using System.Collections.Generic;
using SPF.Contracts;
using SPF.Runtime.Scheduling;
using SPF.Runtime.World;
using Unity.Jobs;
using Unity.Mathematics;

namespace RpgFoundation.Systems
{
    /// <summary>
    /// ApplyCommands (main thread): game flow. Builds a floor from the hero profile (new game, continue,
    /// descend, retry): clears the tables, generates the map, spawns the hero and the monsters.
    /// </summary>
    sealed class FloorSystem : SimSystemBase
    {
        readonly List<Room> m_Rooms = new List<Room>();

        public override SimPhase Phase => SimPhase.ApplyCommands;
        public override void Declare(AccessDeclaration access) { }

        public IReadOnlyList<Room> Rooms => m_Rooms;

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var game = world.Resource(RpgKeys.Game);
            var config = world.Resource(RpgKeys.Config);
            while (game.FlowCommands.Count > 0)
            {
                var command = game.FlowCommands.Dequeue();
                switch (command.Kind)
                {
                    case RpgCommandKind.NewGame:
                    case RpgCommandKind.Continue:
                        BuildFloor(world, config, game, context.Seed);
                        break;
                    case RpgCommandKind.Descend:
                        if (game.Flow != RpgFlow.FloorClear) break;
                        StoreHeroHealth(world, game);
                        game.Profile.Floor++;
                        BuildFloor(world, config, game, context.Seed);
                        break;
                    case RpgCommandKind.Retry:
                        game.Profile.CopyFrom(game.FloorStart);
                        BuildFloor(world, config, game, context.Seed);
                        break;
                    case RpgCommandKind.Menu:
                        Clear(world);
                        game.Hero = EntityHandle.Null;
                        game.Flow = RpgFlow.Menu;
                        game.Version++;
                        break;
                }
            }
            return dependency;
        }

        static void StoreHeroHealth(SimWorld world, RpgGameState game)
        {
            if (!world.Registry.TryResolve(game.Hero, out _, out int row)) return;
            game.Profile.HealthFraction = world.Column(RpgKeys.Health)[row].Fraction;
        }

        static void Clear(SimWorld world)
        {
            RpgSpawner.DestroyAll(world, RpgKeys.Actor);
            RpgSpawner.DestroyAll(world, RpgKeys.Projectile);
            RpgSpawner.DestroyAll(world, RpgKeys.Item);
            world.Resource(RpgKeys.Hits).Clear();
            world.Resource(RpgKeys.ProjectileRequests).Clear();
            world.Resource(RpgKeys.Deaths).Clear();
        }

        void BuildFloor(SimWorld world, RpgRuntimeConfig config, RpgGameState game, uint sessionSeed)
        {
            Clear(world);
            var map = world.Resource(RpgKeys.Map);
            var profile = game.Profile;
            var d = config.Dungeon;
            DungeonGenerator.Generate(map, profile.RunSeed, profile.Floor, d, m_Rooms, out var start, out var stairs, out int stairsRoom);
            var view = map.AsView();
            game.StartCell = start;
            game.StairsCell = stairs;
            game.FinalFloor = d.FinalFloor;
            game.Hero = RpgSpawner.SpawnHero(world, config, profile, view.CenterOf(start));

            var random = new Random(math.hash(new uint3(profile.RunSeed, (uint)profile.Floor, 0x51u)) | 1u);
            int floor = profile.Floor;
            float density = d.MonsterDensity * (1f + d.MonsterGrowthPerFloor * (floor - 1));
            int spawned = 0;
            for (int r = 1; r < m_Rooms.Count; r++)
            {
                var room = m_Rooms[r];
                int count = (int)math.round(room.Area * density);
                for (int i = 0; i < count; i++)
                {
                    int kind = PickKind(config, ref random);
                    if (kind == 0) break;
                    float2 p = view.CenterOf(room.Min + new int2(random.NextInt(1, room.Size.x - 1), random.NextInt(1, room.Size.y - 1)));
                    if (!RpgSpawner.SpawnMonster(world, config, kind, p, floor).IsNull) spawned++;
                }
            }
            game.BossAlive = false;
            int boss = config.BossKind;
            if (boss > 0 && d.BossEvery > 0 && floor % d.BossEvery == 0)
            {
                var room = m_Rooms[stairsRoom];
                float2 p = view.CenterOf(stairs) + new float2(0f, 1.5f);
                if (!RpgSpawner.SpawnMonster(world, config, boss, p, floor).IsNull) { spawned++; game.BossAlive = true; }
            }
            game.MonstersAlive = spawned;
            game.FloorMonsters = spawned;
            game.FloorStart.CopyFrom(profile);
            world.Resource(RpgKeys.ActorGrid).Origin = map.Origin;
            game.Flow = RpgFlow.Playing;
            game.Message = game.BossAlive ? "A Warden guards the stairs" : $"Floor {floor}";
            game.FloorBuilds++;
            game.Version++;
        }

        static int PickKind(RpgRuntimeConfig config, ref Random random)
        {
            float total = 0f;
            for (int i = 0; i < config.Monsters.Length; i++) total += config.Monsters[i].SpawnWeight;
            if (total <= 0f) return 0;
            float r = random.NextFloat(total);
            for (int i = 0; i < config.Monsters.Length; i++)
            {
                r -= config.Monsters[i].SpawnWeight;
                if (r < 0f) return i + 1;
            }
            return 0;
        }
    }

    /// <summary>Input (main thread): the hero's intent from the latched input; potions.</summary>
    sealed class HeroControlSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.Input;

        public override void Declare(AccessDeclaration access) => access
            .Write(RpgKeys.MoveIntent).Write(RpgKeys.Facing).Write(RpgKeys.Combat).Write(RpgKeys.Health)
            .Read(RpgKeys.Stats).Write(RpgKeys.Feedback);

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var game = world.Resource(RpgKeys.Game);
            var input = game.Input;
            game.Input.Pressed = 0;   // consumed by this tick
            if (!world.Registry.TryResolve(game.Hero, out _, out int row))
                return dependency;
            dependency.Complete();
            var intents = world.Column(RpgKeys.MoveIntent);
            var combat = world.Column(RpgKeys.Combat);
            bool playing = game.Flow == RpgFlow.Playing;
            float2 move = playing ? input.Move : float2.zero;
            if (math.lengthsq(move) > 1f) move = math.normalize(move);
            intents[row] = move;
            if (math.lengthsq(move) > 1e-4f) world.Column(RpgKeys.Facing).Set(row, math.normalize(move));

            var c = combat[row];
            c.Action = !playing ? ActorAction.None
                : input.WasPressed(RpgButton.Skill) ? ActorAction.Skill
                : input.IsHeld(RpgButton.Attack) || input.WasPressed(RpgButton.Attack) ? ActorAction.Melee
                : ActorAction.None;
            if (playing && input.WasPressed(RpgButton.Potion))
                TryDrinkPotion(world, game, row, ref c);
            combat[row] = c;
            return dependency;
        }

        internal static bool TryDrinkPotion(SimWorld world, RpgGameState game, int row, ref CombatState combat)
        {
            var config = world.Resource(RpgKeys.Config);
            var healths = world.Column(RpgKeys.Health);
            var h = healths[row];
            if (game.Profile.Potions <= 0 || !combat.Potion.Ready || h.Current >= h.Max) return false;
            float heal = h.Max * config.Settings.PotionHeal;
            h.Current = math.min(h.Max, h.Current + heal);
            healths[row] = h;
            combat.Potion.Start(config.Settings.PotionCooldown);
            game.Profile.Potions--;
            game.Version++;
            world.Resource(RpgKeys.Feedback).TryAdd(new FeedbackEvent { Kind = FeedbackKind.Heal, Position = world.Column(RpgKeys.Position)[row], Value = heal });
            return true;
        }
    }

    /// <summary>Input: rebuilds the flow field towards the hero when the hero changes tile (Burst job).</summary>
    sealed class FlowFieldSystem : SimSystemBase
    {
        int2 m_Goal = new int2(-1);
        uint m_MapVersion = uint.MaxValue;

        public override SimPhase Phase => SimPhase.Input;
        public override int Order => 10;

        public override void Declare(AccessDeclaration access) => access.Read(RpgKeys.Position).Read(RpgKeys.Map).Write(RpgKeys.Flow);

        public long Builds { get; private set; }

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var game = world.Resource(RpgKeys.Game);
            if (!world.Registry.TryResolve(game.Hero, out _, out int row))
                return dependency;
            var map = world.Resource(RpgKeys.Map);
            var view = map.AsView();
            int2 cell = view.CellOf(world.Column(RpgKeys.Position)[row]);
            if (math.all(cell == m_Goal) && map.Version == m_MapVersion)
                return dependency;
            m_Goal = cell;
            m_MapVersion = map.Version;
            Builds++;
            System.Span<int2> goals = stackalloc int2[1];
            goals[0] = cell;
            return world.Resource(RpgKeys.Flow).ScheduleBuild(view, goals, dependency);
        }
    }
}
