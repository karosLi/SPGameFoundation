using SPF.Contracts;
using SPF.Contracts.Collections;
using SPF.L1.Navigation;
using SPF.L1.Spatial;
using SPF.L2.Combat;
using SPF.L2.Stats;
using SPF.Runtime.Scheduling;
using SPF.Runtime.World;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace RpgFoundation.Systems
{
    /// <summary>
    /// Decide (Burst, parallel): monster state machine. Idle monsters wander near home; seeing the hero
    /// (aggro range + line of sight) or being hit starts a chase along the flow field (straight at the
    /// hero when in sight and close); in range they stop and attack, archers keep their distance. A chase
    /// without sight for a while ends in a return home.
    /// </summary>
    sealed class MonsterAISystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.Decide;

        public override void Declare(AccessDeclaration access) => access
            .Read(RpgKeys.Position).Read(RpgKeys.Info).Read(RpgKeys.Stats).Read(RpgKeys.Map).Read(RpgKeys.Flow)
            .Write(RpgKeys.Brain).Write(RpgKeys.MoveIntent).Write(RpgKeys.Combat).Write(RpgKeys.Facing);

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var game = world.Resource(RpgKeys.Game);
            bool hasHero = world.Registry.TryResolve(game.Hero, out _, out int heroRow) && game.Flow == RpgFlow.Playing;
            var map = world.Resource(RpgKeys.Map).AsView();
            return new BrainJob
            {
                Position = context.Column(RpgKeys.Position),
                Info = context.Column(RpgKeys.Info),
                Stats = context.Column(RpgKeys.Stats),
                Brain = context.Column(RpgKeys.Brain),
                Intent = context.Column(RpgKeys.MoveIntent),
                Combat = context.Column(RpgKeys.Combat),
                Facing = context.Column(RpgKeys.Facing),
                Map = map,
                Flow = world.Resource(RpgKeys.Flow).AsView(map),
                HeroRow = hasHero ? heroRow : -1,
                Monsters = world.Resource(RpgKeys.Config).Monsters,
                DeltaTime = context.Time.DeltaTime,
                Seed = context.Seed,
                Tick = context.Time.Tick,
            }.Schedule(context.Count(RpgKeys.Actor), 16, dependency);
        }

        [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
        struct BrainJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float2> Position;
            [ReadOnly] public NativeArray<ActorInfo> Info;
            [ReadOnly] public NativeArray<StatBlock> Stats;
            public NativeArray<Brain> Brain;
            public NativeArray<float2> Intent;
            public NativeArray<CombatState> Combat;
            public NativeArray<float2> Facing;
            public TileMapView Map;
            public FlowFieldView Flow;
            public int HeroRow;
            [ReadOnly] public NativeArray<MonsterDef> Monsters;
            public float DeltaTime;
            public uint Seed, Tick;

            public void Execute(int i)
            {
                var info = Info[i];
                if (info.Team != Team.Monsters || info.Has(ActorFlags.Dead)) return;
                var def = Monsters[info.Kind - 1];
                var brain = Brain[i];
                var combat = Combat[i];
                float2 p = Position[i];
                float2 intent = float2.zero;
                combat.Action = ActorAction.None;
                brain.StateTime += DeltaTime;
                brain.Provoked = math.max(brain.Provoked - DeltaTime, 0f);

                bool hero = HeroRow >= 0;
                float2 heroPos = hero ? Position[HeroRow] : p;
                float2 toHero = heroPos - p;
                float dist = math.length(toHero);
                float heroRadius = hero ? Info[HeroRow].Radius : 0f;
                bool sees = hero && dist < def.Aggro * 1.6f && Map.LineOfSight(p, heroPos);
                float reach = Stats[i][Stat.Range] + info.Radius + heroRadius;

                switch (brain.State)
                {
                    case AIState.Idle:
                    case AIState.Return:
                    {
                        if (hero && (brain.Provoked > 0f || (sees && dist < def.Aggro)))
                        {
                            Enter(ref brain, AIState.Chase);
                            break;
                        }
                        float2 target = brain.State == AIState.Return ? brain.Home : brain.WanderTarget;
                        if (math.distancesq(p, target) < 0.25f || brain.StateTime > 6f)
                        {
                            if (brain.State == AIState.Return) Enter(ref brain, AIState.Idle);
                            var random = SimRandom.Create(Seed, Tick, (uint)info.Id);
                            float2 candidate = brain.Home + random.NextFloat2Direction() * random.NextFloat(0.5f, 3f);
                            brain.WanderTarget = Map.IsSolidAt(candidate) ? brain.Home : candidate;
                            brain.StateTime = 0f;
                        }
                        intent = math.normalizesafe(target - p) * (brain.State == AIState.Return ? 0.8f : 0.35f);
                        break;
                    }
                    case AIState.Chase:
                    case AIState.Attack:
                    {
                        if (!hero) { Enter(ref brain, AIState.Return); break; }
                        brain.LostSight = sees ? 0f : brain.LostSight + DeltaTime;
                        if (brain.LostSight > 4f && brain.Provoked <= 0f) { Enter(ref brain, AIState.Return); break; }
                        bool inRange = dist <= reach && (sees || dist < 1.5f);
                        if (def.Ranged)
                        {
                            // Archers: keep between half and full range, shoot when in sight.
                            if (sees && dist < reach * 0.45f) intent = -math.normalizesafe(toHero);
                            else if (!inRange) intent = Approach(p, toHero, dist, sees);
                            if (inRange) combat.Action = ActorAction.Shoot;
                        }
                        else
                        {
                            if (!inRange) intent = Approach(p, toHero, dist, sees);
                            else combat.Action = ActorAction.Melee;
                        }
                        brain.State = inRange ? AIState.Attack : AIState.Chase;
                        if (hero) Facing[i] = math.normalizesafe(toHero, Facing[i]);
                        break;
                    }
                }
                Brain[i] = brain;
                Intent[i] = intent;
                Combat[i] = combat;
            }

            float2 Approach(float2 p, float2 toHero, float dist, bool sees)
            {
                // Straight at the hero when visible and close; otherwise follow the flow field around walls.
                if (sees && dist < 5f) return math.normalizesafe(toHero);
                float2 flow = Flow.Direction(p);
                return math.lengthsq(flow) > 0f ? flow : math.normalizesafe(toHero);
            }

            static void Enter(ref Brain brain, AIState state)
            {
                brain.State = state;
                brain.StateTime = 0f;
                brain.LostSight = 0f;
            }
        }
    }

    /// <summary>
    /// Move (Burst, parallel): modifiers and stats, cooldowns, regeneration, then movement with crowd
    /// separation (previous tick's actor grid) and tile collision (slides along walls).
    /// </summary>
    sealed class MovementSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.Move;

        public override void Declare(AccessDeclaration access) => access
            .Read(RpgKeys.MoveIntent).Read(RpgKeys.Info).Read(RpgKeys.BaseStats).Read(RpgKeys.Map).Read(RpgKeys.ActorGrid)
            .Write(RpgKeys.Position).Write(RpgKeys.PrevPosition).Write(RpgKeys.Facing)
            .Write(RpgKeys.Stats).Write(RpgKeys.Mods).Write(RpgKeys.Combat).Write(RpgKeys.Health);

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            return new MoveJob
            {
                Intent = context.Column(RpgKeys.MoveIntent),
                Info = context.Column(RpgKeys.Info),
                BaseStats = context.Column(RpgKeys.BaseStats),
                Position = context.Column(RpgKeys.Position),
                PrevPosition = context.Column(RpgKeys.PrevPosition),
                Facing = context.Column(RpgKeys.Facing),
                Stats = context.Column(RpgKeys.Stats),
                Mods = context.Column(RpgKeys.Mods),
                Combat = context.Column(RpgKeys.Combat),
                Health = context.Column(RpgKeys.Health),
                Map = world.Resource(RpgKeys.Map).AsView(),
                Grid = world.Resource(RpgKeys.ActorGrid).AsReader(),
                DeltaTime = context.Time.DeltaTime,
            }.Schedule(context.Count(RpgKeys.Actor), 32, dependency);
        }

        struct Separation : IGridVisitor
        {
            public int Self;
            public float2 Position;
            public float Radius;
            public float2 Push;

            public bool Visit(in GridEntry e)
            {
                if (e.Owner == Self) return true;
                float2 d = Position - e.Position;
                float r = Radius + e.Radius;
                float distSq = math.lengthsq(d);
                if (distSq >= r * r) return true;
                float dist = math.sqrt(distSq);
                // Exactly coincident: deterministic tie-break by row.
                float2 dir = dist > 1e-5f ? d / dist : (Self < e.Owner ? new float2(1f, 0f) : new float2(-1f, 0f));
                Push += dir * (r - dist);
                return true;
            }
        }

        [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
        struct MoveJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float2> Intent;
            [ReadOnly] public NativeArray<ActorInfo> Info;
            [ReadOnly] public NativeArray<StatBlock> BaseStats;
            public NativeArray<float2> Position;
            public NativeArray<float2> PrevPosition;
            public NativeArray<float2> Facing;
            public NativeArray<StatBlock> Stats;
            public NativeArray<ModifierSet> Mods;
            public NativeArray<CombatState> Combat;
            public NativeArray<Health> Health;
            public TileMapView Map;
            public GridReader Grid;
            public float DeltaTime;

            public void Execute(int i)
            {
                var info = Info[i];
                float2 p = Position[i];
                PrevPosition[i] = p;
                if (info.Has(ActorFlags.Dead)) return;

                var mods = Mods[i];
                mods.Tick(DeltaTime);
                var stats = mods.Evaluate(BaseStats[i]);
                Mods[i] = mods;
                Stats[i] = stats;

                var combat = Combat[i];
                combat.Attack.Tick(DeltaTime);
                combat.Skill.Tick(DeltaTime);
                combat.Potion.Tick(DeltaTime);
                combat.HitFlash = math.max(combat.HitFlash - DeltaTime, 0f);
                Combat[i] = combat;

                var h = Health[i];
                h.Max = stats[Stat.MaxHealth];
                h.Current = math.min(h.Max, h.Current + stats[Stat.Regen] * DeltaTime);
                Health[i] = h;

                float2 intent = Intent[i];
                float2 velocity = intent * stats[Stat.Speed];
                var sep = new Separation { Self = i, Position = p, Radius = info.Radius };
                Grid.Query(p, info.Radius, ref sep);
                // Push out of overlaps over a few ticks (soft), heavier actors move less.
                float2 delta = velocity * DeltaTime + sep.Push * 0.35f;
                Position[i] = Map.MoveCircle(p, delta, info.Radius);
                if (info.Team == Team.Monsters && math.lengthsq(intent) > 1e-4f && combat.Action == ActorAction.None)
                    Facing[i] = math.normalize(intent);
            }
        }
    }

    /// <summary>SpatialBuild: actor grid (Owner = row, Data = team) for separation, melee and projectiles.</summary>
    sealed class ActorGridSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.SpatialBuild;

        public override void Declare(AccessDeclaration access) => access.Read(RpgKeys.Position).Read(RpgKeys.Info).Write(RpgKeys.ActorGrid);

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var grid = context.World.Resource(RpgKeys.ActorGrid);
            var fill = new FillJob
            {
                Position = context.Column(RpgKeys.Position),
                Info = context.Column(RpgKeys.Info),
                Staging = grid.Staging,
                StagingCount = grid.StagingCount,
                Count = context.Count(RpgKeys.Actor),
            }.Schedule(dependency);
            return grid.ScheduleBuild(fill);
        }

        [BurstCompile(CompileSynchronously = true)]
        struct FillJob : IJob
        {
            [ReadOnly] public NativeArray<float2> Position;
            [ReadOnly] public NativeArray<ActorInfo> Info;
            public NativeArray<GridEntry> Staging;
            public NativeArray<int> StagingCount;
            public int Count;

            public void Execute()
            {
                int n = 0;
                for (int i = 0; i < Count && n < Staging.Length; i++)
                {
                    var info = Info[i];
                    if (info.Has(ActorFlags.Dead)) continue;
                    Staging[n++] = new GridEntry { Position = Position[i], Radius = info.Radius, Owner = i, Data = (int)info.Team };
                }
                StagingCount[0] = n;
            }
        }
    }

    /// <summary>
    /// Collision (Burst, parallel): melee swings (arc against enemies in reach; the hero auto-aims at the
    /// nearest enemy), shots and the hero's fireball (as projectile requests), projectile flight with wall
    /// and actor hits. Hits are queued; rules apply in <see cref="ResolveSystem"/>.
    /// </summary>
    sealed class CombatSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.Collision;

        public override void Declare(AccessDeclaration access) => access
            .Read(RpgKeys.Position).Read(RpgKeys.Info).Read(RpgKeys.Stats).Read(RpgKeys.ActorGrid).Read(RpgKeys.Map)
            .Write(RpgKeys.Combat).Write(RpgKeys.Facing).Write(RpgKeys.Hits).Write(RpgKeys.ProjectileRequests)
            .Read(RpgKeys.Projectile).Write(RpgKeys.ProjectilePosition).Write(RpgKeys.ProjectilePrev).Write(RpgKeys.ProjectileInfo)
            .Write(SimWorld.DestroyQueueKey);

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var config = world.Resource(RpgKeys.Config);
            var grid = world.Resource(RpgKeys.ActorGrid).AsReader();
            var hits = world.Resource(RpgKeys.Hits).AsWriter();
            var melee = new ActionJob
            {
                Position = context.Column(RpgKeys.Position),
                Info = context.Column(RpgKeys.Info),
                Stats = context.Column(RpgKeys.Stats),
                Combat = context.Column(RpgKeys.Combat),
                Facing = context.Column(RpgKeys.Facing),
                Grid = grid,
                Hits = hits,
                Requests = world.Resource(RpgKeys.ProjectileRequests).AsWriter(),
                Monsters = config.Monsters,
                Settings = config.Settings,
            }.Schedule(context.Count(RpgKeys.Actor), 16, dependency);

            var projectiles = new ProjectileJob
            {
                Handles = context.Handles(RpgKeys.Projectile),
                Position = context.Column(RpgKeys.ProjectilePosition),
                Prev = context.Column(RpgKeys.ProjectilePrev),
                Projectile = context.Column(RpgKeys.ProjectileInfo),
                Grid = grid,
                Map = world.Resource(RpgKeys.Map).AsView(),
                Hits = hits,
                Destroy = world.Resource(SimWorld.DestroyQueueKey).AsWriter(),
                DeltaTime = context.Time.DeltaTime,
            }.Schedule(context.Count(RpgKeys.Projectile), 16, melee);   // both write the hit queue: chained
            return projectiles;
        }

        struct Nearest : IGridVisitor
        {
            public Team Team;
            public float2 Position;
            public int Row;
            public float BestSq;
            public float2 Target;

            public bool Visit(in GridEntry e)
            {
                if (e.Data == (int)Team) return true;
                float d = math.distancesq(Position, e.Position) - e.Radius * e.Radius;
                if (d < BestSq || (d == BestSq && e.Owner < Row)) { BestSq = d; Row = e.Owner; Target = e.Position; }
                return true;
            }
        }

        struct ArcHits : IGridVisitor
        {
            public Team Team;
            public int AttackerId;
            public float2 Origin, Facing;
            public float Range, Cos, Damage, Crit;
            public ParallelQueue<HitEvent>.Writer Hits;
            public int Count;

            public bool Visit(in GridEntry e)
            {
                if (e.Data == (int)Team) return true;
                if (!CombatMath.InArc(Origin, Facing, e.Position, e.Radius, Range, Cos)) return true;
                Hits.TryAdd(new HitEvent { AttackerId = AttackerId, TargetRow = e.Owner, Damage = Damage, CritChance = Crit, Position = e.Position });
                Count++;
                return true;
            }
        }

        [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
        struct ActionJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float2> Position;
            [ReadOnly] public NativeArray<ActorInfo> Info;
            [ReadOnly] public NativeArray<StatBlock> Stats;
            public NativeArray<CombatState> Combat;
            public NativeArray<float2> Facing;
            public GridReader Grid;
            public ParallelQueue<HitEvent>.Writer Hits;
            public ParallelQueue<ProjectileRequest>.Writer Requests;
            [ReadOnly] public NativeArray<MonsterDef> Monsters;
            public RpgSettings Settings;

            public void Execute(int i)
            {
                var info = Info[i];
                var c = Combat[i];
                if (info.Has(ActorFlags.Dead) || c.Action == ActorAction.None) return;
                var stats = Stats[i];
                float2 p = Position[i];
                float range = stats[Stat.Range] + info.Radius;
                bool hero = info.Has(ActorFlags.Hero);

                if (c.Action == ActorAction.Skill)
                {
                    if (c.Skill.Ready)
                    {
                        float2 dir = Aim(i, p, info, 12f, Facing[i]);
                        Requests.TryAdd(new ProjectileRequest
                        {
                            Position = p + dir * (info.Radius + 0.2f), Direction = dir, Speed = Settings.FireballSpeed,
                            Damage = stats[Stat.Attack] * Settings.FireballDamage, CritChance = stats[Stat.Crit],
                            Team = info.Team, OwnerId = info.Id, Fireball = true,
                        });
                        c.Skill.Start(Settings.FireballCooldown);
                    }
                }
                else if (c.Attack.Ready)
                {
                    if (c.Action == ActorAction.Shoot)
                    {
                        var def = Monsters[info.Kind - 1];
                        float2 dir = math.normalizesafe(Facing[i], new float2(1f, 0f));
                        Requests.TryAdd(new ProjectileRequest
                        {
                            Position = p + dir * (info.Radius + 0.15f), Direction = dir, Speed = def.ProjectileSpeed,
                            Damage = stats[Stat.Attack], CritChance = stats[Stat.Crit], Team = info.Team, OwnerId = info.Id,
                        });
                        c.Attack.Start(1f / math.max(stats[Stat.AttackRate], 0.05f));
                    }
                    else
                    {
                        float2 facing = hero ? Aim(i, p, info, range + 1f, Facing[i]) : Facing[i];
                        Facing[i] = facing;
                        var arc = new ArcHits
                        {
                            Team = info.Team, AttackerId = info.Id, Origin = p, Facing = facing, Range = range,
                            Cos = hero ? Settings.ArcCos : 0.5f, Damage = stats[Stat.Attack], Crit = stats[Stat.Crit], Hits = Hits,
                        };
                        Grid.Query(p, range, ref arc);
                        // Swinging at nothing still costs the swing (no free spam), but a hero with no target
                        // in reach does not swing at all.
                        if (arc.Count > 0 || !hero)
                            c.Attack.Start(1f / math.max(stats[Stat.AttackRate], 0.05f));
                    }
                }
                Combat[i] = c;
            }

            float2 Aim(int self, float2 p, in ActorInfo info, float reach, float2 fallback)
            {
                var nearest = new Nearest { Team = info.Team, Position = p, Row = -1, BestSq = reach * reach };
                Grid.Query(p, reach, ref nearest);
                return nearest.Row >= 0 ? math.normalizesafe(nearest.Target - p, fallback) : math.normalizesafe(fallback, new float2(1f, 0f));
            }
        }

        struct FirstHit : IGridVisitor
        {
            public Team Team;
            public float2 From, To;
            public float Radius;
            public int Row;
            public float T;

            public bool Visit(in GridEntry e)
            {
                if (e.Data == (int)Team) return true;
                if (!SPF.L1.Geometry.GeoMath.SweptCircleHits(From, To, Radius, e.Position, e.Radius)) return true;
                float2 d = To - From;
                float lenSq = math.lengthsq(d);
                float t = lenSq > 1e-8f ? math.saturate(math.dot(e.Position - From, d) / lenSq) : 0f;
                if (Row < 0 || t < T || (t == T && e.Owner < Row)) { Row = e.Owner; T = t; }
                return true;
            }
        }

        [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
        struct ProjectileJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<EntityHandle> Handles;
            public NativeArray<float2> Position;
            public NativeArray<float2> Prev;
            public NativeArray<ProjectileInfo> Projectile;
            public GridReader Grid;
            public TileMapView Map;
            public ParallelQueue<HitEvent>.Writer Hits;
            public ParallelQueue<EntityHandle>.Writer Destroy;
            public float DeltaTime;

            public void Execute(int i)
            {
                var info = Projectile[i];
                float2 from = Position[i];
                float2 to = from + info.Velocity * DeltaTime;
                Prev[i] = from;
                info.Life -= DeltaTime;
                Projectile[i] = info;

                var hit = new FirstHit { Team = info.Team, From = from, To = to, Radius = info.Radius, Row = -1 };
                Grid.Query((from + to) * 0.5f, math.length(to - from) * 0.5f + info.Radius, ref hit);
                bool wall = !Map.LineOfSight(from, to);
                if (hit.Row >= 0)
                {
                    float2 at = math.lerp(from, to, hit.T);
                    // A wall between the shooter and the target blocks the hit.
                    if (Map.LineOfSight(from, at))
                    {
                        Hits.TryAdd(new HitEvent { AttackerId = info.OwnerId, TargetRow = hit.Row, Damage = info.Damage, CritChance = info.CritChance, Position = at, Projectile = true });
                        Destroy.TryAdd(Handles[i]);
                        Position[i] = at;
                        return;
                    }
                }
                Position[i] = to;
                if (wall || info.Life <= 0f)
                    Destroy.TryAdd(Handles[i]);
            }
        }
    }
}
