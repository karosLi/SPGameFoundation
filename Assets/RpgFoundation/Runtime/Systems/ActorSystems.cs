using SPF.Contracts;
using SPF.Contracts.Collections;
using SPF.L1.Navigation;
using SPF.L1.Spatial;
using SPF.L2.Combat;
using SPF.L2.AI;
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
            .Read(RpgKeys.Loadout)
            .Write(RpgKeys.Brain).Write(RpgKeys.MoveIntent).Write(RpgKeys.Combat).Write(RpgKeys.Facing);

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var game = world.Resource(RpgKeys.Game);
            bool hasHero = world.Registry.TryResolve(game.Hero, out _, out int heroRow) && game.Flow == RpgFlow.Playing;
            var map = world.Resource(RpgKeys.Map).AsView();
            var config = world.Resource(RpgKeys.Config);
            return new BrainJob
            {
                Loadout = context.Column(RpgKeys.Loadout),
                Skills = config.Skills,
                DecisionProgram = config.CombatDecisionProgram,
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
            [ReadOnly] public NativeArray<Loadout> Loadout;
            [ReadOnly] public NativeArray<SkillDef> Skills;
            [ReadOnly] public NativeArray<DecisionNode> DecisionProgram;
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
                        byte skill = Loadout[i].S0;
                        // Gather once using the original short-circuit predicate. No new world scans,
                        // random draws, cadence changes or action-timeline ownership are introduced.
                        bool skillReady = skill > 0 && combat.SkillCooldown.x <= 0f && sees && dist < Skills[skill - 1].Radius * 0.85f;
                        uint facts = RpgDecisions.Facts(skillReady, def.Ranged, sees, inRange, dist, reach);
                        var selected = RpgDecisions.Select(DecisionProgram, facts, out _);
                        if ((selected & RpgCombatIntent.Skill) != 0)
                        {
                            combat.Action = ActorAction.Skill;
                            combat.RequestSlot = 0;
                        }
                        else
                        {
                            if ((selected & RpgCombatIntent.Retreat) != 0) intent = -math.normalizesafe(toHero);
                            else if ((selected & RpgCombatIntent.Approach) != 0) intent = Approach(p, toHero, dist, sees);
                            if ((selected & RpgCombatIntent.Attack) != 0) combat.Action = ActorAction.Attack;
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
            .Write(RpgKeys.Stats).Write(RpgKeys.Mods).Write(RpgKeys.Combat).Write(RpgKeys.Health).Write(RpgKeys.Mana)
            .Read(RpgKeys.Loadout);

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
                Mana = context.Column(RpgKeys.Mana),
                Loadout = context.Column(RpgKeys.Loadout),
                Skills = world.Resource(RpgKeys.Config).Skills,
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
            public NativeArray<Health> Mana;
            [ReadOnly] public NativeArray<Loadout> Loadout;
            [ReadOnly] public NativeArray<SkillDef> Skills;
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
                combat.Potion.Tick(DeltaTime);
                combat.SkillCooldown = math.max(combat.SkillCooldown - DeltaTime, 0f);
                combat.HitFlash = math.max(combat.HitFlash - DeltaTime, 0f);
                combat.Invulnerable = math.max(combat.Invulnerable - DeltaTime, 0f);

                var h = Health[i];
                h.Max = stats[Stat.MaxHealth];
                h.Current = math.min(h.Max, h.Current + stats[Stat.Regen] * DeltaTime);
                Health[i] = h;
                var mana = Mana[i];
                mana.Max = stats[Stat.MaxMana];
                mana.Current = math.min(mana.Max, mana.Current + stats[Stat.ManaRegen] * DeltaTime);
                Mana[i] = mana;

                // Actions slow or replace walking: rooted while winding up / casting / staggered, a dash
                // moves along the locked aim at the skill's speed.
                float2 intent = Intent[i];
                float factor = combat.Phase switch
                {
                    ActionPhase.Windup => info.Has(ActorFlags.Hero) ? 0.45f : 0.15f,
                    ActionPhase.Recover => 0.6f,
                    ActionPhase.Cast => 0.15f,
                    ActionPhase.Channel => 0.75f,
                    ActionPhase.Stagger => 0f,
                    _ => 1f,
                };
                float2 velocity = intent * stats[Stat.Speed] * factor;
                if (combat.Phase == ActionPhase.Dash && combat.PhaseSkill > 0)
                    velocity = combat.Aim * Skills[combat.PhaseSkill - 1].Speed;
                velocity += combat.Knockback;
                combat.Knockback *= math.exp(-9f * DeltaTime);
                if (math.lengthsq(combat.Knockback) < 1e-4f) combat.Knockback = float2.zero;
                Combat[i] = combat;

                var sep = new Separation { Self = i, Position = p, Radius = info.Radius };
                Grid.Query(p, info.Radius, ref sep);
                // Push out of overlaps over a few ticks (soft); dashing actors slip through crowds.
                float2 delta = velocity * DeltaTime + (combat.Phase == ActionPhase.Dash ? float2.zero : sep.Push * 0.35f);
                Position[i] = Map.MoveCircle(p, delta, info.Radius);
                if (combat.Phase != ActionPhase.None && combat.Phase != ActionPhase.Stagger && math.lengthsq(combat.Aim) > 0f)
                    Facing[i] = combat.Aim;
                else if (math.lengthsq(intent) > 1e-4f && (info.Team == Team.Monsters || combat.Phase == ActionPhase.None))
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
    /// Collision (Burst, parallel): the action phase machine. Attacks wind up, strike (melee arc against
    /// enemies in reach with knockback / stagger, or a projectile for bows and staves) and recover; skills
    /// spend mana and go on cooldown, then cast (fireball, frost nova, the boss's telegraphed slam),
    /// channel (whirlwind: area hits every third of its duration) or dash (invulnerable). The hero aims at
    /// the nearest enemy. Hits are queued; rules apply in <see cref="ResolveSystem"/>.
    /// </summary>
    sealed class CombatSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.Collision;

        public override void Declare(AccessDeclaration access) => access
            .Read(RpgKeys.Position).Read(RpgKeys.Info).Read(RpgKeys.Stats).Read(RpgKeys.ActorGrid).Read(RpgKeys.Map).Read(RpgKeys.Loadout).Read(RpgKeys.Status)
            .Write(RpgKeys.Combat).Write(RpgKeys.Facing).Write(RpgKeys.Mana).Write(RpgKeys.Hits).Write(RpgKeys.ProjectileRequests).Write(RpgKeys.Feedback)
            .Read(RpgKeys.Projectile).Write(RpgKeys.ProjectilePosition).Write(RpgKeys.ProjectilePrev).Write(RpgKeys.ProjectileInfo)
            .Write(SimWorld.DestroyQueueKey);

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var config = world.Resource(RpgKeys.Config);
            var grid = world.Resource(RpgKeys.ActorGrid).AsReader();
            var hits = world.Resource(RpgKeys.Hits).AsWriter();
            var feedback = world.Resource(RpgKeys.Feedback).AsWriter();
            var actions = new ActionJob
            {
                Position = context.Column(RpgKeys.Position),
                Info = context.Column(RpgKeys.Info),
                Stats = context.Column(RpgKeys.Stats),
                Loadout = context.Column(RpgKeys.Loadout),
                Status = context.Column(RpgKeys.Status),
                Combat = context.Column(RpgKeys.Combat),
                Facing = context.Column(RpgKeys.Facing),
                Mana = context.Column(RpgKeys.Mana),
                Grid = grid,
                Hits = hits,
                Feedback = feedback,
                Requests = world.Resource(RpgKeys.ProjectileRequests).AsWriter(),
                Weapons = config.Weapons,
                Skills = config.Skills,
                Settings = config.Settings,
                DeltaTime = context.Time.DeltaTime,
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
                Feedback = feedback,
                Destroy = world.Resource(SimWorld.DestroyQueueKey).AsWriter(),
                DeltaTime = context.Time.DeltaTime,
            }.Schedule(context.Count(RpgKeys.Projectile), 16, actions);   // both write the hit queue: chained
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

        /// <summary>Hits every enemy in an arc (ArcCos = -1: full circle) with knockback away from the origin.</summary>
        struct AreaHits : IGridVisitor
        {
            public Team Team;
            public int AttackerId;
            public float2 Origin, Facing;
            public float Range, Cos, Damage, Crit, Knockback, Stagger;
            public Modifier Mod;
            public StatusHit Status;
            public HitSource Source;
            public ParallelQueue<HitEvent>.Writer Hits;
            public int Count;

            public bool Visit(in GridEntry e)
            {
                if (e.Data == (int)Team) return true;
                if (!CombatMath.InArc(Origin, Facing, e.Position, e.Radius, Range, Cos)) return true;
                Hits.TryAdd(new HitEvent
                {
                    AttackerId = AttackerId, TargetRow = e.Owner, Damage = Damage, CritChance = Crit, Position = e.Position,
                    Direction = math.normalizesafe(e.Position - Origin, Facing), Knockback = Knockback, Stagger = Stagger, Mod = Mod, Status = Status, Source = Source,
                });
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
            [ReadOnly] public NativeArray<Loadout> Loadout;
            [ReadOnly] public NativeArray<StatusState> Status;
            public NativeArray<CombatState> Combat;
            public NativeArray<float2> Facing;
            public NativeArray<Health> Mana;
            public GridReader Grid;
            public ParallelQueue<HitEvent>.Writer Hits;
            public ParallelQueue<FeedbackEvent>.Writer Feedback;
            public ParallelQueue<ProjectileRequest>.Writer Requests;
            [ReadOnly] public NativeArray<WeaponDef> Weapons;
            [ReadOnly] public NativeArray<SkillDef> Skills;
            public RpgSettings Settings;
            public float DeltaTime;

            public void Execute(int i)
            {
                var info = Info[i];
                if (info.Has(ActorFlags.Dead)) return;
                var c = Combat[i];
                var stats = Stats[i];
                var loadout = Loadout[i];
                var weapon = Weapons[(int)loadout.Weapon];
                float2 p = Position[i];
                bool hero = info.Has(ActorFlags.Hero);

                // 1. Advance the running phase.
                if (c.Phase != ActionPhase.None)
                {
                    c.PhaseTime += DeltaTime;
                    switch (c.Phase)
                    {
                        case ActionPhase.Windup:
                            if (c.PhaseTime >= c.PhaseDuration)
                            {
                                Strike(info, stats, weapon, p, c.Aim, Status[i]);
                                Enter(ref c, ActionPhase.Recover, weapon.Recover);
                            }
                            break;
                        case ActionPhase.Cast:
                            if (c.PhaseTime >= c.PhaseDuration)
                            {
                                Release(info, stats, c.PhaseSkill, p, c.Aim);
                                Enter(ref c, ActionPhase.Recover, 0.15f);
                            }
                            break;
                        case ActionPhase.Channel:
                        {
                            var skill = Skills[c.PhaseSkill - 1];
                            int due = (int)math.min(3f, math.floor(c.PhaseTime / math.max(skill.Duration, 1e-3f) * 3f) + 1f);
                            while (c.PhaseTicks < due)
                            {
                                c.PhaseTicks++;
                                Area(info, stats, p, c.Aim, skill.Radius, -1f, stats[Stat.Attack] * stats[Stat.SkillPower] * skill.Power, skill.Knockback, 0.1f, default, HitSource.Whirlwind);
                            }
                            if (c.PhaseTime >= c.PhaseDuration) Enter(ref c, ActionPhase.None, 0f);
                            break;
                        }
                        default:   // Recover, Dash, Stagger
                            if (c.PhaseTime >= c.PhaseDuration) Enter(ref c, ActionPhase.None, 0f);
                            break;
                    }
                }

                // 2. Start a requested action when free (recovering can be cancelled by a new action).
                bool free = c.Phase == ActionPhase.None || c.Phase == ActionPhase.Recover;
                if (free && c.Action == ActorAction.Skill)
                    TryStartSkill(i, info, stats, loadout, p, ref c);
                else if (free && c.Action == ActorAction.Attack && c.Attack.Ready)
                {
                    float reach = weapon.Ranged ? weapon.Range : stats[Stat.Range] + info.Radius + 1f;
                    c.Aim = hero ? Aim(p, info, reach, Facing[i]) : math.normalizesafe(Facing[i], new float2(1f, 0f));
                    // A hero with a melee weapon and nobody in reach does not swing (no wasted cooldown).
                    bool target = !hero || weapon.Ranged || c.PropInReach || HasEnemy(p, info, stats[Stat.Range] + info.Radius + 0.3f);
                    if (target)
                    {
                        float rate = math.max(stats[Stat.AttackRate], 0.05f);
                        // Faster attack rates shorten the wind-up too (animations stay in sync).
                        float speed = math.max(rate / math.max(weapon.AttackRate, 0.05f), 0.25f);
                        Enter(ref c, ActionPhase.Windup, weapon.Windup / speed);
                        c.PhaseSkill = 0;   // a weapon attack, not a skill
                        c.Attack.Start(1f / rate);
                        Facing[i] = c.Aim;
                        Feedback.TryAdd(new FeedbackEvent { Kind = FeedbackKind.Swing, Position = p, Direction = c.Aim, Actor = info.Kind, Weapon = loadout.Weapon, Value = c.PhaseDuration });
                    }
                }
                c.Action = ActorAction.None;
                Combat[i] = c;
            }

            void TryStartSkill(int i, in ActorInfo info, in StatBlock stats, in Loadout loadout, float2 p, ref CombatState c)
            {
                int slot = c.RequestSlot;
                byte id = loadout.Skill(slot);
                if (id == 0 || c.SkillCooldown[slot] > 0f) return;
                var skill = Skills[id - 1];
                var mana = Mana[i];
                if (mana.Current < skill.ManaCost) return;
                mana.Current -= skill.ManaCost;
                Mana[i] = mana;
                var cd = c.SkillCooldown;
                cd[slot] = skill.Cooldown;
                c.SkillCooldown = cd;
                bool hero = info.Has(ActorFlags.Hero);
                float2 facing = math.normalizesafe(Facing[i], new float2(1f, 0f));
                c.Aim = hero && skill.Kind == SkillKind.Projectile ? Aim(p, info, 12f, facing) : facing;
                c.PhaseSkill = id;
                switch (skill.Kind)
                {
                    case SkillKind.Dash:
                        Enter(ref c, ActionPhase.Dash, skill.Duration);
                        c.Invulnerable = skill.Duration + 0.05f;
                        Feedback.TryAdd(new FeedbackEvent { Kind = FeedbackKind.Dash, Position = p, Direction = c.Aim, Value = skill.Duration, Actor = info.Kind });
                        break;
                    case SkillKind.Whirlwind:
                        Enter(ref c, ActionPhase.Channel, skill.Duration);
                        Feedback.TryAdd(new FeedbackEvent { Kind = FeedbackKind.Whirlwind, Position = p, Value = skill.Radius, Actor = info.Kind });
                        break;
                    default:
                        Enter(ref c, ActionPhase.Cast, math.max(skill.CastTime, 0.01f));
                        Feedback.TryAdd(new FeedbackEvent
                        {
                            Kind = skill.Kind == SkillKind.Slam ? FeedbackKind.SlamWarning : FeedbackKind.Cast,
                            Position = p, Direction = c.Aim, Value = skill.Kind == SkillKind.Slam ? skill.Radius : skill.CastTime, Actor = info.Kind,
                        });
                        break;
                }
                c.PhaseSkill = id;
            }

            void Strike(in ActorInfo info, in StatBlock stats, in WeaponDef weapon, float2 p, float2 aim, in StatusState status)
            {
                float damage = stats[Stat.Attack] * weapon.DamageMul;
                var onHit = status.HitStatus(damage);
                if (weapon.Ranged)
                {
                    Requests.TryAdd(new ProjectileRequest
                    {
                        Position = p + aim * (info.Radius + 0.15f), Direction = aim, Speed = weapon.ProjectileSpeed, Radius = weapon.ProjectileRadius,
                        Damage = damage, CritChance = stats[Stat.Crit], Knockback = weapon.Knockback, Pierce = weapon.Pierce,
                        Life = weapon.Range / math.max(weapon.ProjectileSpeed, 0.1f), Team = info.Team, OwnerId = info.Id, Visual = weapon.Visual, Status = onHit,
                    });
                    return;
                }
                float cos = info.Has(ActorFlags.Hero) ? weapon.ArcCos : math.max(weapon.ArcCos, 0f);
                Area(info, stats, p, aim, stats[Stat.Range] + info.Radius, cos, damage, weapon.Knockback, weapon.Stagger, default, HitSource.Weapon, onHit);
            }

            void Release(in ActorInfo info, in StatBlock stats, byte id, float2 p, float2 aim)
            {
                var skill = Skills[id - 1];
                float damage = stats[Stat.Attack] * stats[Stat.SkillPower] * skill.Power;
                switch (skill.Kind)
                {
                    case SkillKind.Projectile:
                        Requests.TryAdd(new ProjectileRequest
                        {
                            Position = p + aim * (info.Radius + 0.2f), Direction = aim, Speed = skill.Speed, Radius = 0.3f, Damage = damage,
                            CritChance = stats[Stat.Crit], Knockback = skill.Knockback, ExplodeRadius = skill.Radius, Life = 2f,
                            Team = info.Team, OwnerId = info.Id, Visual = ProjectileVisual.Fireball,
                            Status = StatusHit.From(skill.Status, damage, skill.StatusPower, skill.StatusDuration),
                        });
                        break;
                    case SkillKind.Nova:
                        Area(info, stats, p, aim, skill.Radius, -1f, damage, skill.Knockback, 0.2f,
                            new Modifier { Source = ModSource.Slow, Stat = Stat.Speed, Op = ModifierOp.Multiply, Value = skill.Slow, Remaining = skill.SlowDuration }, HitSource.Nova);
                        Feedback.TryAdd(new FeedbackEvent { Kind = FeedbackKind.Nova, Position = p, Value = skill.Radius });
                        break;
                    case SkillKind.Slam:
                        Area(info, stats, p, aim, skill.Radius, -1f, damage, skill.Knockback, 0.4f, default, HitSource.Slam);
                        Feedback.TryAdd(new FeedbackEvent { Kind = FeedbackKind.Slam, Position = p, Value = skill.Radius });
                        break;
                }
            }

            void Area(in ActorInfo info, in StatBlock stats, float2 p, float2 aim, float range, float cos, float damage, float knockback, float stagger, Modifier mod, HitSource source, StatusHit status = default)
            {
                var area = new AreaHits
                {
                    Status = status,
                    Team = info.Team, AttackerId = info.Id, Origin = p, Facing = aim, Range = range, Cos = cos, Damage = damage,
                    Crit = stats[Stat.Crit], Knockback = knockback, Stagger = stagger, Mod = mod, Source = source, Hits = Hits,
                };
                Grid.Query(p, range, ref area);
            }

            bool HasEnemy(float2 p, in ActorInfo info, float reach)
            {
                var nearest = new Nearest { Team = info.Team, Position = p, Row = -1, BestSq = reach * reach };
                Grid.Query(p, reach, ref nearest);
                return nearest.Row >= 0;
            }

            float2 Aim(float2 p, in ActorInfo info, float reach, float2 fallback)
            {
                var nearest = new Nearest { Team = info.Team, Position = p, Row = -1, BestSq = reach * reach };
                Grid.Query(p, reach, ref nearest);
                return nearest.Row >= 0 ? math.normalizesafe(nearest.Target - p, fallback) : math.normalizesafe(fallback, new float2(1f, 0f));
            }

            static void Enter(ref CombatState c, ActionPhase phase, float duration)
            {
                c.Phase = phase;
                c.PhaseTime = 0f;
                c.PhaseDuration = duration;
                c.PhaseTicks = 0;
            }
        }

        struct FirstHit : IGridVisitor
        {
            public Team Team;
            public float2 From, To;
            public float Radius;
            public int Row;
            public int Skip;
            public float T;

            public bool Visit(in GridEntry e)
            {
                if (e.Data == (int)Team || e.Owner == Skip) return true;
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
            public ParallelQueue<FeedbackEvent>.Writer Feedback;
            public ParallelQueue<EntityHandle>.Writer Destroy;
            public float DeltaTime;

            public void Execute(int i)
            {
                var info = Projectile[i];
                float2 from = Position[i];
                float2 to = from + info.Velocity * DeltaTime;
                Prev[i] = from;
                info.Life -= DeltaTime;
                float2 dir = math.normalizesafe(info.Velocity);

                var hit = new FirstHit { Team = info.Team, From = from, To = to, Radius = info.Radius, Row = -1, Skip = info.LastHit };
                Grid.Query((from + to) * 0.5f, math.length(to - from) * 0.5f + info.Radius, ref hit);
                if (hit.Row >= 0)
                {
                    float2 at = math.lerp(from, to, hit.T);
                    // A wall between the shooter and the target blocks the hit.
                    if (Map.LineOfSight(from, at))
                    {
                        if (info.ExplodeRadius > 0f) { Explode(info, at, dir); Destroy.TryAdd(Handles[i]); Position[i] = at; Projectile[i] = info; return; }
                        Hits.TryAdd(new HitEvent
                        {
                            AttackerId = info.OwnerId, TargetRow = hit.Row, Damage = info.Damage, CritChance = info.CritChance, Position = at,
                            Direction = dir, Knockback = info.Knockback, Stagger = 0.1f, Status = info.Status, Source = HitSource.Projectile,
                        });
                        if (info.Pierce > 0)
                        {
                            info.Pierce--;
                            info.LastHit = hit.Row;
                        }
                        else
                        {
                            Destroy.TryAdd(Handles[i]);
                            Position[i] = at;
                            Projectile[i] = info;
                            return;
                        }
                    }
                }
                bool wall = !Map.LineOfSight(from, to);
                Position[i] = to;
                if (wall || info.Life <= 0f)
                {
                    if (info.ExplodeRadius > 0f) Explode(info, wall ? from : to, dir);
                    Destroy.TryAdd(Handles[i]);
                }
                Projectile[i] = info;
            }

            void Explode(in ProjectileInfo info, float2 at, float2 dir)
            {
                var area = new ExplosionHits { Team = info.Team, Info = info, Origin = at, Hits = Hits };
                Grid.Query(at, info.ExplodeRadius, ref area);
                Feedback.TryAdd(new FeedbackEvent { Kind = FeedbackKind.Explosion, Position = at, Value = info.ExplodeRadius, Direction = dir });
            }
        }

        struct ExplosionHits : IGridVisitor
        {
            public Team Team;
            public ProjectileInfo Info;
            public float2 Origin;
            public ParallelQueue<HitEvent>.Writer Hits;

            public bool Visit(in GridEntry e)
            {
                if (e.Data == (int)Team) return true;
                float r = Info.ExplodeRadius + e.Radius;
                if (math.distancesq(Origin, e.Position) > r * r) return true;
                Hits.TryAdd(new HitEvent
                {
                    AttackerId = Info.OwnerId, TargetRow = e.Owner, Damage = Info.Damage, CritChance = Info.CritChance, Position = e.Position,
                    Direction = math.normalizesafe(e.Position - Origin, new float2(1f, 0f)), Knockback = Info.Knockback, Stagger = 0.25f,
                    Status = Info.Status, Source = HitSource.Explosion,
                });
                return true;
            }
        }
    }
}
