using System.Collections.Generic;
using SPF.Contracts;
using SPF.Contracts.Collections;
using SPF.L1.Geometry;
using SPF.L1.Spatial;
using SPF.L2.Combat;
using SPF.Runtime.Scheduling;
using SPF.Runtime.World;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

namespace SurvivorFoundation.Systems
{
    // ---------------------------------------------------------------- ApplyCommands (main thread)

    /// <summary>Flow commands: start a run, pick a level-up upgrade, back to the menu.</summary>
    sealed class FlowSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.ApplyCommands;
        public override void Declare(AccessDeclaration access) { }

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var game = world.Resource(SvKeys.Game);
            var s = world.Resource(SvKeys.Config).Settings;
            while (game.Commands.Count > 0)
            {
                var command = game.Commands.Dequeue();
                switch (command.Kind)
                {
                    case SvCommandKind.Start:
                        world.ClearLevel();
                        StartRun(game, s);
                        break;
                    case SvCommandKind.Choose:
                        if (game.Flow != SvFlow.LevelUp || command.Argument < 0 || command.Argument >= game.ChoiceCountOffered) break;
                        Apply(game, s, (Upgrade)game.Choices[command.Argument]);
                        game.PendingLevels--;
                        game.Flow = SvFlow.Playing;
                        if (game.PendingLevels > 0) OfferChoices(game, context.Seed, context.Time.Tick);
                        game.Version++;
                        break;
                    case SvCommandKind.Menu:
                        world.ClearLevel();
                        game.Flow = SvFlow.Menu;
                        game.Version++;
                        break;
                }
            }
            return dependency;
        }

        static void StartRun(SvGameState g, in SvSettings s)
        {
            System.Array.Clear(g.Upgrades, 0, g.Upgrades.Length);
            g.Upgrades[(int)Upgrade.Bolt] = 1;
            g.Hero = g.HeroPrev = float2.zero;
            g.Facing = new float2(1f, 0f);
            g.MaxHp = g.Hp = SvRules.MaxHp(s, g);
            g.Invulnerable = 0f;
            g.Level = 1; g.Xp = 0; g.Kills = 0; g.Time = 0f;
            g.SpawnAccumulator = 0f; g.NextElite = s.EliteEvery;
            g.BoltTimer = g.NovaTimer = g.SpiralTimer = g.SpiralAngle = g.OrbitAngle = 0f;
            g.PendingLevels = 0;
            g.Flow = SvFlow.Playing;
            g.Version++;
        }

        internal static void Apply(SvGameState g, in SvSettings s, Upgrade upgrade)
        {
            int i = (int)upgrade;
            g.Upgrades[i] = math.min(g.Upgrades[i] + 1, SvGameState.MaxLevel);
            if (upgrade == Upgrade.Vitality)
            {
                float max = SvRules.MaxHp(s, g);
                g.Hp += max - g.MaxHp;
                g.MaxHp = max;
            }
        }

        /// <summary>Three distinct upgrades that are not maxed yet (deterministic per tick).</summary>
        internal static void OfferChoices(SvGameState g, uint seed, uint tick)
        {
            var random = SimRandom.Create(seed, tick, 0xC401u);
            int n = 0;
            for (int guard = 0; guard < 64 && n < SvGameState.ChoiceCount; guard++)
            {
                int u = random.NextInt(SvGameState.UpgradeCount);
                if (g.Upgrades[u] >= SvGameState.MaxLevel) continue;
                bool dup = false;
                for (int k = 0; k < n; k++) dup |= g.Choices[k] == u;
                if (!dup) g.Choices[n++] = u;
            }
            g.ChoiceCountOffered = n;
            g.Flow = n > 0 ? SvFlow.LevelUp : SvFlow.Playing;
            if (n == 0) g.PendingLevels = 0;
        }
    }

    /// <summary>Deaths drop gems, collected gems give XP (level-ups), queued hero damage is applied.</summary>
    sealed class RewardSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.ApplyCommands;
        public override int Order => 5;
        public override void Declare(AccessDeclaration access) { }

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var game = world.Resource(SvKeys.Game);
            var s = world.Resource(SvKeys.Config).Settings;
            var deaths = world.Resource(SvKeys.Deaths);
            var collected = world.Resource(SvKeys.Collected);
            var damage = world.Resource(SvKeys.HeroDamage);
            var feedback = world.Resource(SvKeys.Feedback);
            bool playing = game.Flow == SvFlow.Playing;

            if (playing && deaths.Count > 0)
            {
                int start = world.SpawnRange(SvKeys.Gem, deaths.Count, out int added);
                var positions = world.Column(SvKeys.GemPosition);
                var infos = world.Column(SvKeys.GemInfo);
                for (int i = 0; i < added; i++)
                {
                    var d = deaths[i];
                    positions[start + i] = d.Position;
                    infos[start + i] = new GemInfo { Value = d.Xp };
                }
                game.Kills += deaths.Count;
            }
            deaths.Clear();

            if (playing)
            {
                int xp = 0;
                for (int i = 0; i < collected.Count; i++) xp += collected[i];
                if (xp > 0)
                {
                    game.Xp += xp;
                    while (game.Xp >= SvRules.XpToNext(s, game.Level))
                    {
                        game.Xp -= SvRules.XpToNext(s, game.Level);
                        game.Level++;
                        game.PendingLevels++;
                        feedback.TryAdd(new SvFeedback { Kind = SvFeedbackKind.LevelUp, Position = game.Hero, Value = game.Level });
                    }
                    game.Version++;
                }

                // The strongest hit this tick, then a short invulnerability (a swarm does not delete the hero).
                float worst = 0f;
                for (int i = 0; i < damage.Count; i++) worst = math.max(worst, damage[i]);
                if (worst > 0f && game.Invulnerable <= 0f)
                {
                    game.Hp -= worst;
                    game.Invulnerable = s.HurtInvulnerable;
                    feedback.TryAdd(new SvFeedback { Kind = SvFeedbackKind.HeroHurt, Position = game.Hero, Value = worst });
                    if (game.Hp <= 0f) { game.Hp = 0f; game.Flow = SvFlow.Dead; }
                    game.Version++;
                }
                if (game.Flow == SvFlow.Playing && game.PendingLevels > 0)
                {
                    FlowSystem.OfferChoices(game, context.Seed, context.Time.Tick);
                    game.Version++;
                }
            }
            collected.Clear();
            damage.Clear();
            return dependency;
        }
    }

    /// <summary>
    /// Spawning: enemy waves around the hero (rate grows over time, an elite every minute), the hero's
    /// weapons, then every bullet requested (by the hero here, by shooters' jobs last tick) in one bulk
    /// pooled spawn filled by a Burst job. Every few dozen ticks the enemy table is sorted by Morton cell.
    /// </summary>
    sealed class SpawnSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.ApplyCommands;
        public override int Order => 10;
        public override void Declare(AccessDeclaration access) { }

        public long Reorders { get; private set; }

        struct QueueSink : IBulletSink
        {
            public ParallelQueue<BulletSpawn> Queue;
            public float Damage, Radius, Life;
            public short Pierce;
            public BulletVisual Visual;
            public BulletTeam Team;

            public void Emit(float2 position, float2 direction, float speed) => Queue.TryAdd(new BulletSpawn
            {
                Position = position, Velocity = direction * speed, Radius = Radius, Damage = Damage, Life = Life, Team = Team, Visual = Visual, Pierce = Pierce,
            });
        }

        struct SpawnOrder : IComparer<BulletSpawn>
        {
            public int Compare(BulletSpawn a, BulletSpawn b)
            {
                int c = math.asuint(a.Position.x).CompareTo(math.asuint(b.Position.x));
                if (c != 0) return c;
                c = math.asuint(a.Position.y).CompareTo(math.asuint(b.Position.y));
                if (c != 0) return c;
                c = math.asuint(a.Velocity.x).CompareTo(math.asuint(b.Velocity.x));
                if (c != 0) return c;
                c = math.asuint(a.Velocity.y).CompareTo(math.asuint(b.Velocity.y));
                return c != 0 ? c : ((int)a.Team).CompareTo((int)b.Team);
            }
        }

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var game = world.Resource(SvKeys.Game);
            var config = world.Resource(SvKeys.Config);
            var s = config.Settings;
            var spawns = world.Resource(SvKeys.BulletSpawns);
            if (game.Flow != SvFlow.Playing)
            {
                spawns.Clear();
                return dependency;
            }
            float dt = context.Time.DeltaTime;
            game.Time += dt;
            SpawnEnemies(world, config, game, context.Seed, context.Time.Tick, dt);
            FireWeapons(world, s, game, spawns.Raw, dt);
            SpawnBullets(world, spawns);
            if (s.ReorderInterval > 0 && context.Time.Tick % (uint)s.ReorderInterval == 0)
                SortEnemies(world, s);
            return dependency;
        }

        static void SpawnEnemies(SimWorld world, SvRuntime config, SvGameState game, uint seed, uint tick, float dt)
        {
            var s = config.Settings;
            var random = SimRandom.Create(seed, tick, 0x5A11u);
            game.SpawnAccumulator += (s.SpawnPerSecond + s.SpawnGrowth * game.Time) * dt;
            int alive = world.Table(SvKeys.Enemy).Count;
            while (game.SpawnAccumulator >= 1f)
            {
                game.SpawnAccumulator -= 1f;
                if (alive >= s.MaxEnemies) continue;
                int kind = PickKind(config, game.Time, ref random);
                if (kind > 0 && !SpawnEnemy(world, config, kind, SpawnPoint(s, game.Hero, ref random), false).IsNull) alive++;
            }
            if (s.EliteEvery > 0f && game.Time >= game.NextElite)
            {
                game.NextElite += s.EliteEvery;
                int brute = 0;
                for (int k = 0; k < config.Enemies.Length; k++) if (config.Enemies[k].Radius > config.Enemies[math.max(brute - 1, 0)].Radius) brute = k + 1;
                if (brute > 0) SpawnEnemy(world, config, brute, SpawnPoint(s, game.Hero, ref random), true);
            }
        }

        static float2 SpawnPoint(in SvSettings s, float2 hero, ref Random random)
        {
            float angle = random.NextFloat(math.PI * 2f);
            math.sincos(angle, out float sin, out float cos);
            return math.clamp(hero + new float2(cos, sin) * (s.SpawnRadius + random.NextFloat(3f)), -s.ArenaHalf, s.ArenaHalf);
        }

        static int PickKind(SvRuntime config, float time, ref Random random)
        {
            float total = 0f;
            for (int k = 0; k < config.EnemyKinds; k++) if (time >= config.Enemies[k].SpawnFrom) total += config.Enemies[k].Weight;
            if (total <= 0f) return 0;
            float r = random.NextFloat(total);
            for (int k = 0; k < config.EnemyKinds; k++)
            {
                if (time < config.Enemies[k].SpawnFrom) continue;
                r -= config.Enemies[k].Weight;
                if (r < 0f) return k + 1;
            }
            return 0;
        }

        public static EntityHandle SpawnEnemy(SimWorld world, SvRuntime config, int kind, float2 position, bool elite)
        {
            var handle = world.CreateEntity(SvKeys.Enemy, out int row);
            if (handle.IsNull) return handle;
            var def = config.Enemies[kind - 1];
            float hp = def.Hp * (elite ? 6f : 1f);
            world.Column(SvKeys.Position).Set(row, position);
            world.Column(SvKeys.PrevPosition).Set(row, position);
            world.Column(SvKeys.Info).Set(row, new EnemyInfo
            {
                Kind = (byte)kind, Radius = def.Radius * (elite ? 1.4f : 1f), Speed = def.Speed * (elite ? 1.15f : 1f), Damage = def.Damage * (elite ? 1.5f : 1f),
                Hp = hp, MaxHp = hp, Flags = (elite ? EnemyFlags.Elite : 0) | (def.Shooter ? EnemyFlags.Shooter : 0),
                Timer = def.Pattern.Interval * 0.5f,
            });
            return handle;
        }

        static void FireWeapons(SimWorld world, in SvSettings s, SvGameState g, ParallelQueue<BulletSpawn> queue, float dt)
        {
            float might = SvRules.Might(g);
            var sink = new QueueSink { Queue = queue, Team = BulletTeam.Hero, Radius = 0.18f, Life = 1.6f };
            // Magic bolt: an aimed fan at the nearest enemy.
            int bolts = SvRules.BoltCount(g);
            g.BoltTimer -= dt;
            if (bolts > 0 && g.BoltTimer <= 0f)
            {
                g.BoltTimer += s.BoltCooldown / (1f + 0.15f * (bolts - 1));
                if (Nearest(world, g.Hero, 13f, out float2 target))
                {
                    sink.Damage = s.BoltDamage * might; sink.Visual = BulletVisual.Bolt; sink.Pierce = (short)(bolts / 3);
                    PatternEmitter.AimedFan(bolts, 0.12f * (bolts - 1), s.BoltSpeed, 1f).Fire(0f, g.Hero, target, ref sink);
                    g.Facing = math.normalizesafe(target - g.Hero, g.Facing);
                }
            }
            int nova = SvRules.NovaBullets(g);
            if (nova > 0)
            {
                sink.Damage = s.NovaDamage * might; sink.Visual = BulletVisual.Nova; sink.Pierce = 1; sink.Life = 1.1f;
                float angle = 0f;
                PatternEmitter.Ring(nova, 10f, s.NovaCooldown).Update(ref g.NovaTimer, ref angle, dt, g.Hero, g.Hero, ref sink);
            }
            int arms = SvRules.SpiralArms(g);
            if (arms > 0)
            {
                sink.Damage = s.SpiralDamage * might; sink.Visual = BulletVisual.Spiral; sink.Pierce = 0; sink.Life = 1.4f;
                PatternEmitter.Spiral(arms, 9f, s.SpiralInterval, 0.23f).Update(ref g.SpiralTimer, ref g.SpiralAngle, dt, g.Hero, g.Hero, ref sink);
            }
            g.OrbitAngle = (g.OrbitAngle + dt * 3.2f) % (math.PI * 2f);
        }

        static bool Nearest(SimWorld world, float2 from, float reach, out float2 target)
        {
            var visitor = new NearestVisitor { From = from, Best = reach * reach, Row = -1 };
            world.Resource(SvKeys.EnemyGrid).AsReader().Query(from, reach, ref visitor);
            target = visitor.Target;
            return visitor.Row >= 0;
        }

        struct NearestVisitor : IGridVisitor
        {
            public float2 From, Target;
            public float Best;
            public int Row;

            public bool Visit(in GridEntry e)
            {
                float d = math.distancesq(e.Position, From);
                if (d < Best || (d == Best && e.Owner < Row)) { Best = d; Row = e.Owner; Target = e.Position; }
                return true;
            }
        }

        static void SpawnBullets(SimWorld world, EventQueue<BulletSpawn> spawns)
        {
            int n = spawns.Count;
            if (n == 0) return;
            var requests = spawns.AsArray();
            // Shooter jobs append in thread order: sort so bullet rows (and everything after) are deterministic.
            requests.Sort(new SpawnOrder());
            int start = world.SpawnRange(SvKeys.Bullet, n, out int added);
            new FillBulletsJob
            {
                Requests = requests, Start = start,
                Positions = world.Column(SvKeys.BulletPosition),
                Infos = world.Column(SvKeys.BulletInfo),
            }.Schedule(added, 256).Complete();
            spawns.Clear();
        }

        [BurstCompile(CompileSynchronously = true)]
        struct FillBulletsJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<BulletSpawn> Requests;
            public int Start;
            [NativeDisableParallelForRestriction] public NativeArray<float2> Positions;
            [NativeDisableParallelForRestriction] public NativeArray<BulletInfo> Infos;

            public void Execute(int i)
            {
                var r = Requests[i];
                Positions[Start + i] = r.Position;
                Infos[Start + i] = new BulletInfo
                {
                    Velocity = r.Velocity, Radius = r.Radius, Damage = r.Damage, Life = r.Life, Team = r.Team, Visual = r.Visual, Pierce = r.Pierce, LastHit = -1,
                };
            }
        }

        void SortEnemies(SimWorld world, in SvSettings s)
        {
            var table = world.Table(SvKeys.Enemy);
            int n = table.Count;
            if (n < 64) return;
            var keys = new NativeArray<uint>(n, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            new MortonKeysJob { Positions = world.Column(SvKeys.Position), Keys = keys, Origin = -s.ArenaHalf, Cell = s.GridCell * 4f }.Schedule(n, 512).Complete();
            world.SortRows(SvKeys.Enemy, keys);
            keys.Dispose();
            Reorders++;
        }

        [BurstCompile(CompileSynchronously = true)]
        struct MortonKeysJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float2> Positions;
            public NativeArray<uint> Keys;
            public float Origin, Cell;

            public void Execute(int i) => Keys[i] = Morton.Encode(Positions[i], new float2(Origin), Cell);
        }
    }

    // ---------------------------------------------------------------- Input (main thread)

    sealed class HeroSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.Input;
        public override void Declare(AccessDeclaration access) { }

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var game = world.Resource(SvKeys.Game);
            var s = world.Resource(SvKeys.Config).Settings;
            var input = game.Input;
            game.Input.Pressed = 0;
            game.HeroPrev = game.Hero;
            if (game.Flow != SvFlow.Playing) return dependency;
            float dt = context.Time.DeltaTime;
            float2 move = input.Move;
            if (math.lengthsq(move) > 1f) move = math.normalize(move);
            game.Hero = math.clamp(game.Hero + move * SvRules.HeroSpeed(s, game) * dt, -s.ArenaHalf, s.ArenaHalf);
            if (math.lengthsq(move) > 1e-4f) game.Facing = math.normalize(move);
            game.Invulnerable = math.max(game.Invulnerable - dt, 0f);
            return dependency;
        }
    }

    // ---------------------------------------------------------------- Decide (Burst, parallel)

    /// <summary>Enemies: seek the hero (shooters hold their distance and fire patterns), crowd separation, contact damage.</summary>
    sealed class EnemySystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.Decide;

        public override void Declare(AccessDeclaration access) => access
            .Read(SvKeys.EnemyGrid).Write(SvKeys.Position).Write(SvKeys.PrevPosition).Write(SvKeys.Info)
            .Write(SvKeys.BulletSpawns).Write(SvKeys.HeroDamage);

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var game = world.Resource(SvKeys.Game);
            if (game.Flow != SvFlow.Playing) return dependency;
            var config = world.Resource(SvKeys.Config);
            return new EnemyJob
            {
                Position = context.Column(SvKeys.Position),
                Prev = context.Column(SvKeys.PrevPosition),
                Info = context.Column(SvKeys.Info),
                Grid = context.Resource(SvKeys.EnemyGrid).AsReader(),
                Defs = config.Enemies,
                Spawns = context.Resource(SvKeys.BulletSpawns).AsWriter(),
                HeroDamage = context.Resource(SvKeys.HeroDamage).AsWriter(),
                Hero = game.Hero,
                HeroRadius = config.Settings.HeroRadius,
                BulletDamage = config.Settings.EnemyBulletDamage,
                ArenaHalf = config.Settings.ArenaHalf,
                DeltaTime = context.Time.DeltaTime,
            }.Schedule(context.Count(SvKeys.Enemy), 64, dependency);
        }

        struct Separation : IGridVisitor
        {
            public float2 Position;
            public float Radius;
            public int Self;
            public float2 Push;
            public int Neighbours;

            public bool Visit(in GridEntry e)
            {
                if (e.Owner == Self) return true;
                float2 d = Position - e.Position;
                float r = Radius + e.Radius;
                float len2 = math.lengthsq(d);
                if (len2 >= r * r) return true;
                float len = math.sqrt(len2);
                Push += (len > 1e-4f ? d / len : new float2(Self & 1, 1 - (Self & 1))) * (r - len);
                return ++Neighbours < 8;   // enough to resolve a crowd; bounds the work in dense packs
            }
        }

        struct OrbSink : IBulletSink
        {
            public ParallelQueue<BulletSpawn>.Writer Spawns;
            public float Damage;

            public void Emit(float2 position, float2 direction, float speed) => Spawns.TryAdd(new BulletSpawn
            {
                Position = position, Velocity = direction * speed, Radius = 0.2f, Damage = Damage, Life = 6f, Team = BulletTeam.Enemy, Visual = BulletVisual.EnemyOrb,
            });
        }

        [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
        struct EnemyJob : IJobParallelFor
        {
            public NativeArray<float2> Position;
            public NativeArray<float2> Prev;
            public NativeArray<EnemyInfo> Info;
            public GridReader Grid;
            [ReadOnly] public NativeArray<EnemyDef> Defs;
            public ParallelQueue<BulletSpawn>.Writer Spawns;
            public ParallelQueue<float>.Writer HeroDamage;
            public float2 Hero;
            public float HeroRadius, BulletDamage, ArenaHalf, DeltaTime;

            public void Execute(int i)
            {
                var info = Info[i];
                float2 p = Position[i];
                Prev[i] = p;
                if (info.Has(EnemyFlags.Dead)) return;
                info.Flash = math.max(info.Flash - DeltaTime, 0f);
                float2 to = Hero - p;
                float dist = math.length(to);
                float2 dir = dist > 1e-4f ? to / dist : float2.zero;
                float2 velocity = dir * info.Speed;
                if (info.Has(EnemyFlags.Shooter))
                {
                    var def = Defs[info.Kind - 1];
                    if (dist < def.KeepDistance) velocity = dist < def.KeepDistance - 1.5f ? -dir * info.Speed * 0.6f : float2.zero;
                    if (dist < 15f)
                    {
                        var sink = new OrbSink { Spawns = Spawns, Damage = BulletDamage };
                        def.Pattern.Update(ref info.Timer, ref info.Angle, DeltaTime, p, Hero, ref sink);
                    }
                }
                var sep = new Separation { Position = p, Radius = info.Radius, Self = i };
                Grid.Query(p, info.Radius * 2f + 0.5f, ref sep);
                p += (velocity + sep.Push * 6f) * DeltaTime;
                Position[i] = math.clamp(p, -ArenaHalf, ArenaHalf);
                if (dist < info.Radius + HeroRadius) HeroDamage.TryAdd(info.Damage);
                Info[i] = info;
            }
        }
    }

    // ---------------------------------------------------------------- Move (Burst, parallel)

    sealed class BulletSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.Move;

        public override void Declare(AccessDeclaration access) => access
            .Write(SvKeys.Bullet).Write(SvKeys.BulletPosition).Write(SvKeys.BulletInfo);

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            if (world.Resource(SvKeys.Game).Flow != SvFlow.Playing) return dependency;
            return new MoveJob
            {
                Position = context.Column(SvKeys.BulletPosition),
                Info = context.Column(SvKeys.BulletInfo),
                Dead = world.Table(SvKeys.Bullet).DeadFlags,
                ArenaHalf = world.Resource(SvKeys.Config).Settings.ArenaHalf + 5f,
                DeltaTime = context.Time.DeltaTime,
            }.Schedule(context.Count(SvKeys.Bullet), 512, dependency);
        }

        [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
        struct MoveJob : IJobParallelFor
        {
            public NativeArray<float2> Position;
            public NativeArray<BulletInfo> Info;
            public NativeArray<byte> Dead;
            public float ArenaHalf, DeltaTime;

            public void Execute(int i)
            {
                var b = Info[i];
                b.Life -= DeltaTime;
                float2 p = Position[i] + b.Velocity * DeltaTime;
                Position[i] = p;
                Info[i] = b;
                if (b.Life <= 0f || math.any(math.abs(p) > ArenaHalf)) Dead[i] = 1;
            }
        }
    }

    // ---------------------------------------------------------------- SpatialBuild

    sealed class EnemyGridSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.SpatialBuild;

        public override void Declare(AccessDeclaration access) => access.Read(SvKeys.Position).Read(SvKeys.Info).Write(SvKeys.EnemyGrid);

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var grid = context.Resource(SvKeys.EnemyGrid);
            var game = world.Resource(SvKeys.Game);
            // The window follows the hero (the arena is larger than the grid).
            var s = world.Resource(SvKeys.Config).Settings;
            grid.Follow(game.Hero, new float2(-s.ArenaHalf - 4f), new float2(s.ArenaHalf + 4f));
            var fill = new FillJob
            {
                Position = context.Column(SvKeys.Position),
                Info = context.Column(SvKeys.Info),
                Staging = grid.Staging,
                StagingCount = grid.StagingCount,
                Count = context.Count(SvKeys.Enemy),
            }.Schedule(dependency);
            return grid.ScheduleBuild(fill);
        }

        [BurstCompile(CompileSynchronously = true)]
        struct FillJob : IJob
        {
            [ReadOnly] public NativeArray<float2> Position;
            [ReadOnly] public NativeArray<EnemyInfo> Info;
            public NativeArray<GridEntry> Staging;
            public NativeArray<int> StagingCount;
            public int Count;

            public void Execute()
            {
                int n = 0;
                for (int i = 0; i < Count && n < Staging.Length; i++)
                {
                    var info = Info[i];
                    if (info.Has(EnemyFlags.Dead)) continue;
                    Staging[n++] = new GridEntry { Position = Position[i], Radius = info.Radius, Owner = i };
                }
                StagingCount[0] = n;
            }
        }
    }

    // ---------------------------------------------------------------- Collision (Burst, parallel)

    /// <summary>Bullets against enemies (swept) and the hero, orbiting blades, gem magnet and pickup.</summary>
    sealed class CollideSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.Collision;

        public override void Declare(AccessDeclaration access) => access
            .Read(SvKeys.EnemyGrid)
            .Write(SvKeys.Bullet).Read(SvKeys.BulletPosition).Write(SvKeys.BulletInfo)
            .Write(SvKeys.Gem).Write(SvKeys.GemPosition).Write(SvKeys.GemInfo)
            .Write(SvKeys.Hits).Write(SvKeys.HeroDamage).Write(SvKeys.Collected);

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var game = world.Resource(SvKeys.Game);
            if (game.Flow != SvFlow.Playing) return dependency;
            var s = world.Resource(SvKeys.Config).Settings;
            var grid = context.Resource(SvKeys.EnemyGrid).AsReader();
            var hits = context.Resource(SvKeys.Hits).AsWriter();
            float dt = context.Time.DeltaTime;
            var bullets = new BulletHitJob
            {
                Position = context.Column(SvKeys.BulletPosition),
                Info = context.Column(SvKeys.BulletInfo),
                Dead = world.Table(SvKeys.Bullet).DeadFlags,
                Grid = grid,
                Hits = hits,
                HeroDamage = context.Resource(SvKeys.HeroDamage).AsWriter(),
                Hero = game.Hero,
                HeroRadius = s.HeroRadius,
                DeltaTime = dt,
            }.Schedule(context.Count(SvKeys.Bullet), 256, dependency);
            var orbit = new OrbitJob
            {
                Grid = grid,
                Hits = hits,
                Hero = game.Hero,
                Blades = SvRules.OrbitBlades(game),
                Angle = game.OrbitAngle,
                Radius = s.OrbitRadius,
                Damage = s.OrbitDps * SvRules.Might(game) * dt,
            }.Schedule(bullets);   // both write the hit queue: chained
            var gems = new GemJob
            {
                Position = context.Column(SvKeys.GemPosition),
                Info = context.Column(SvKeys.GemInfo),
                Dead = world.Table(SvKeys.Gem).DeadFlags,
                Collected = context.Resource(SvKeys.Collected).AsWriter(),
                Hero = game.Hero,
                Magnet = SvRules.Magnet(s, game),
                Pickup = s.PickupRadius,
                Speed = s.GemSpeed,
                DeltaTime = dt,
            }.Schedule(context.Count(SvKeys.Gem), 256, dependency);
            return JobHandle.CombineDependencies(orbit, gems);
        }

        struct FirstHit : IGridVisitor
        {
            public float2 From, To;
            public float Radius, T;
            public int Row, Skip;

            public bool Visit(in GridEntry e)
            {
                if (e.Owner == Skip) return true;
                if (!GeoMath.SweptCircleHits(From, To, Radius, e.Position, e.Radius)) return true;
                float2 d = To - From;
                float lenSq = math.lengthsq(d);
                float t = lenSq > 1e-8f ? math.saturate(math.dot(e.Position - From, d) / lenSq) : 0f;
                if (Row < 0 || t < T || (t == T && e.Owner < Row)) { Row = e.Owner; T = t; }
                return true;
            }
        }

        [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
        struct BulletHitJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float2> Position;
            public NativeArray<BulletInfo> Info;
            public NativeArray<byte> Dead;
            public GridReader Grid;
            public ParallelQueue<SvHit>.Writer Hits;
            public ParallelQueue<float>.Writer HeroDamage;
            public float2 Hero;
            public float HeroRadius, DeltaTime;

            public void Execute(int i)
            {
                if (Dead[i] != 0) return;
                var b = Info[i];
                float2 to = Position[i];
                if (b.Team == BulletTeam.Enemy)
                {
                    float r = b.Radius + HeroRadius;
                    if (math.distancesq(to, Hero) <= r * r) { HeroDamage.TryAdd(b.Damage); Dead[i] = 1; }
                    return;
                }
                float2 from = to - b.Velocity * DeltaTime;
                var hit = new FirstHit { From = from, To = to, Radius = b.Radius, Row = -1, Skip = b.LastHit };
                Grid.Query((from + to) * 0.5f, math.length(to - from) * 0.5f + b.Radius, ref hit);
                if (hit.Row < 0) return;
                Hits.TryAdd(new SvHit { Target = hit.Row, Damage = b.Damage, Knock = math.normalizesafe(b.Velocity) * 0.12f });
                if (b.Pierce > 0) { b.Pierce--; b.LastHit = hit.Row; Info[i] = b; }
                else Dead[i] = 1;
            }
        }

        struct BladeHits : IGridVisitor
        {
            public float2 Centre;
            public float Radius, Damage;
            public ParallelQueue<SvHit>.Writer Hits;

            public bool Visit(in GridEntry e)
            {
                float r = Radius + e.Radius;
                if (math.distancesq(Centre, e.Position) <= r * r)
                    Hits.TryAdd(new SvHit { Target = e.Owner, Damage = Damage, Knock = math.normalizesafe(e.Position - Centre) * 0.05f });
                return true;
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        struct OrbitJob : IJob
        {
            public GridReader Grid;
            public ParallelQueue<SvHit>.Writer Hits;
            public float2 Hero;
            public int Blades;
            public float Angle, Radius, Damage;

            public void Execute()
            {
                for (int k = 0; k < Blades; k++)
                {
                    math.sincos(Angle + k * math.PI * 2f / Blades, out float s, out float c);
                    var visitor = new BladeHits { Centre = Hero + new float2(c, s) * Radius, Radius = 0.45f, Damage = Damage, Hits = Hits };
                    Grid.Query(visitor.Centre, 1.5f, ref visitor);
                }
            }
        }

        [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
        struct GemJob : IJobParallelFor
        {
            public NativeArray<float2> Position;
            public NativeArray<GemInfo> Info;
            public NativeArray<byte> Dead;
            public ParallelQueue<int>.Writer Collected;
            public float2 Hero;
            public float Magnet, Pickup, Speed, DeltaTime;

            public void Execute(int i)
            {
                if (Dead[i] != 0) return;
                var g = Info[i];
                float2 p = Position[i];
                float2 to = Hero - p;
                float d2 = math.lengthsq(to);
                if (d2 <= Pickup * Pickup) { Collected.TryAdd(g.Value); Dead[i] = 1; return; }
                if (!g.Magnet && d2 <= Magnet * Magnet) { g.Magnet = true; Info[i] = g; }
                if (g.Magnet)
                {
                    float d = math.sqrt(d2);
                    Position[i] = p + to / d * math.min(Speed * DeltaTime, d);
                }
            }
        }
    }

    // ---------------------------------------------------------------- Resolve (Burst)

    sealed class ResolveSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.Resolve;

        public override void Declare(AccessDeclaration access) => access
            .Read(SvKeys.Enemy).Write(SvKeys.Position).Write(SvKeys.Info)
            .Write(SvKeys.Hits).Write(SvKeys.Deaths).Write(SvKeys.Feedback).Write(SimWorld.DestroyQueueKey);

        struct HitOrder : IComparer<SvHit>
        {
            public int Compare(SvHit a, SvHit b)
            {
                int c = a.Target.CompareTo(b.Target);
                if (c != 0) return c;
                c = a.Damage.CompareTo(b.Damage);
                if (c != 0) return c;
                c = a.Knock.x.CompareTo(b.Knock.x);
                return c != 0 ? c : a.Knock.y.CompareTo(b.Knock.y);
            }
        }

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            return new ResolveJob
            {
                Hits = context.Resource(SvKeys.Hits).Raw,
                Deaths = context.Resource(SvKeys.Deaths).Raw,
                Feedback = context.Resource(SvKeys.Feedback).Raw,
                Destroy = context.Resource(SimWorld.DestroyQueueKey).AsWriter(),
                Handles = context.Handles(SvKeys.Enemy),
                Position = context.Column(SvKeys.Position),
                Info = context.Column(SvKeys.Info),
                Defs = world.Resource(SvKeys.Config).Enemies,
                Count = context.Count(SvKeys.Enemy),
            }.Schedule(dependency);
        }

        [BurstCompile(CompileSynchronously = true)]
        struct ResolveJob : IJob
        {
            public ParallelQueue<SvHit> Hits;
            public ParallelQueue<SvDeath> Deaths;
            public ParallelQueue<SvFeedback> Feedback;
            public ParallelQueue<EntityHandle>.Writer Destroy;
            [ReadOnly] public NativeArray<EntityHandle> Handles;
            public NativeArray<float2> Position;
            public NativeArray<EnemyInfo> Info;
            [ReadOnly] public NativeArray<EnemyDef> Defs;
            public int Count;

            public void Execute()
            {
                var hits = Hits.AsArray();
                hits.Sort(new HitOrder());
                for (int h = 0; h < hits.Length; h++)
                {
                    var hit = hits[h];
                    if ((uint)hit.Target >= (uint)Count) continue;
                    var info = Info[hit.Target];
                    if (info.Has(EnemyFlags.Dead)) continue;
                    info.Hp -= hit.Damage;
                    info.Flash = 0.1f;
                    Position[hit.Target] += hit.Knock / math.max(info.Radius * 2f, 0.5f);
                    if (info.Hp <= 0f)
                    {
                        info.Flags |= EnemyFlags.Dead;
                        bool elite = info.Has(EnemyFlags.Elite);
                        int xp = Defs[info.Kind - 1].Xp * (elite ? 10 : 1);
                        Deaths.TryAdd(new SvDeath { Position = Position[hit.Target], Xp = xp, Kind = info.Kind, Elite = elite });
                        Destroy.TryAdd(Handles[hit.Target]);
                        Feedback.TryAdd(new SvFeedback { Kind = SvFeedbackKind.Death, Position = Position[hit.Target], Value = info.Radius, Enemy = info.Kind });
                    }
                    Info[hit.Target] = info;
                }
                Hits.Clear();
            }
        }
    }
}
