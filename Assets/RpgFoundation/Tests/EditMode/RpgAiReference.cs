// Frozen production reference from commit 1c9731a. Do not share decision predicates with the new policy.
// Compiled only in the test assembly; never selected by shipped gameplay.
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

namespace RpgFoundation.Tests
{
    sealed class LegacyMonsterAISystem : SimSystemBase
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
                        if (skill > 0 && combat.SkillCooldown.x <= 0f && sees && dist < Skills[skill - 1].Radius * 0.85f)
                        {
                            // Area skill (the boss's slam): telegraphed cast when the hero is close.
                            combat.Action = ActorAction.Skill;
                            combat.RequestSlot = 0;
                        }
                        else if (def.Ranged)
                        {
                            // Archers: keep between half and full range, shoot when in sight.
                            if (sees && dist < reach * 0.45f) intent = -math.normalizesafe(toHero);
                            else if (!inRange) intent = Approach(p, toHero, dist, sees);
                            if (inRange && sees) combat.Action = ActorAction.Attack;
                        }
                        else
                        {
                            if (!inRange) intent = Approach(p, toHero, dist, sees);
                            else combat.Action = ActorAction.Attack;
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

    // Test-only composition wrapper: same Id/data/system order, one frozen executor substitution.
    sealed class LegacyAiModule : SPF.Runtime.Composition.IGameplayModule
    {
        readonly SPF.Runtime.Composition.IGameplayModule m_Inner;
        public LegacyAiModule(SPF.Runtime.Composition.IGameplayModule inner) => m_Inner = inner;
        public string Id => m_Inner.Id;
        public void DeclareData(SPF.Runtime.World.WorldLayout layout) => m_Inner.DeclareData(layout);
        public void RegisterSystems(SPF.Runtime.Composition.SystemRegistry registry)
        {
            var original = new SPF.Runtime.Composition.SystemRegistry(); m_Inner.RegisterSystems(original);
            foreach (var system in original.Systems)
                registry.Add(system.GetType().Name == "MonsterAISystem" ? new LegacyMonsterAISystem() : system);
        }
    }
}
