using SPF.Contracts;
using SPF.L1.Movement;
using SPF.L2.Buffs;
using SPF.Runtime.Scheduling;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SnakeFoundation.Systems
{
    /// <summary>Move phase: buffs → effective stats, steering, speed / boost, head integration, far-snake LOD.</summary>
    sealed class MovementSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.Move;

        public override void Declare(AccessDeclaration access) => access
            .Read(SnakeKeys.Control)
            .Write(SnakeKeys.Head).Write(SnakeKeys.PrevHead).Write(SnakeKeys.Heading)
            .Write(SnakeKeys.Speed).Write(SnakeKeys.Mass).Write(SnakeKeys.Info)
            .Write(SnakeKeys.Buffs).Write(SnakeKeys.Stats)
            .Read(SnakeKeys.Radius)
            .Read(SnakeKeys.BodyGrid);   // window bounds (edge margin)

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var config = world.Resource(SnakeKeys.Config);
            var game = world.Resource(SnakeKeys.Game);
            var grid = world.Resource(SnakeKeys.BodyGrid);
            return new MoveJob
            {
                Control = context.ReadColumn(SnakeKeys.Control),
                Radius = context.ReadColumn(SnakeKeys.Radius),
                Head = context.WriteColumn(SnakeKeys.Head),
                PrevHead = context.WriteColumn(SnakeKeys.PrevHead),
                Heading = context.WriteColumn(SnakeKeys.Heading),
                Speed = context.WriteColumn(SnakeKeys.Speed),
                Mass = context.WriteColumn(SnakeKeys.Mass),
                Info = context.WriteColumn(SnakeKeys.Info),
                Buffs = context.WriteColumn(SnakeKeys.Buffs),
                Stats = context.WriteColumn(SnakeKeys.Stats),
                BuffTable = config.BuffTable,
                Settings = config.Settings,
                Region = config.Regions[game.ActiveRegion],
                ActiveRegion = game.ActiveRegion,
                // Heads within EdgeMargin of an open window side could touch bodies just outside the
                // window, which the grid does not contain; treat them as far (no collisions) instead of
                // letting them pass through. Sides on the region wall are not inset: walls still kill.
                WindowMin = grid.Origin,
                WindowMax = grid.Origin + grid.Size,
                EdgeMargin = WindowEdgeMargin,
                DeltaTime = context.Time.DeltaTime,
            }.Schedule(context.Count(SnakeKeys.Snake), 32, dependency);
        }

        /// <summary>Larger than any contact reach (hit radius + largest node radius, eat range, magnet).</summary>
        public const float WindowEdgeMargin = 12f;

        [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
        struct MoveJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<SnakeControl>.ReadOnly Control;
            [ReadOnly] public NativeArray<float>.ReadOnly Radius;
            public NativeArray<float2> Head;
            public NativeArray<float2> PrevHead;
            public NativeArray<float2> Heading;
            public NativeArray<float> Speed;
            public NativeArray<float> Mass;
            public NativeArray<SnakeInfo> Info;
            public NativeArray<BuffSet> Buffs;
            public NativeArray<EffectiveStats> Stats;
            public BuffTable BuffTable;
            public SnakeSettings Settings;
            public RegionDef Region;
            public int ActiveRegion;
            public float2 WindowMin;
            public float2 WindowMax;
            public float EdgeMargin;
            public float DeltaTime;

            public void Execute(int i)
            {
                var info = Info[i];
                float2 head = Head[i];
                PrevHead[i] = head;
                if (info.Region != ActiveRegion || info.Has(SnakeFlags.Dead))
                    return;

                var buffs = Buffs[i];
                buffs.Tick(DeltaTime);
                var stats = buffs.Evaluate(BuffTable);
                Buffs[i] = buffs;
                Stats[i] = stats;

                info.Protection = math.max(info.Protection - DeltaTime, 0f);
                info.PortalCooldown = math.max(info.PortalCooldown - DeltaTime, 0f);
                info.SkillCooldown = math.max(info.SkillCooldown - DeltaTime, 0f);

                var s = Settings;
                float radius = Radius[i];
                float mass = Mass[i];
                var control = Control[i];
                float2 heading = Heading[i];
                float turn = Steering.TurnRateForRadius(s.TurnRate, radius, s.TurnReferenceRadius, s.TurnFalloff) * stats.TurnMultiplier;
                heading = Steering.TurnTowards(heading, math.normalizesafe(control.TargetDirection, heading), turn * DeltaTime);

                bool boost = control.Boost && mass > s.MinBoostMass;
                float speed = (boost ? s.BoostSpeed : s.BaseSpeed) * stats.SpeedMultiplier;
                head += heading * speed * DeltaTime;

                if (boost)
                {
                    float drain = s.BoostDrainPerSecond * DeltaTime * stats.BoostCostMultiplier;
                    mass -= drain;
                    info.BoostDropAccumulator += drain;
                    info.Flags |= SnakeFlags.Boosting;
                }
                else
                {
                    info.Flags &= ~SnakeFlags.Boosting;
                }

                float2 innerMin = math.select(WindowMin + EdgeMargin, WindowMin, WindowMin <= Region.Min);
                float2 innerMax = math.select(WindowMax - EdgeMargin, WindowMax, WindowMax >= Region.Max);
                bool inWindow = math.all(head >= innerMin) && math.all(head < innerMax);
                if (inWindow)
                {
                    info.Flags |= SnakeFlags.InWindow;
                }
                else
                {
                    info.Flags &= ~SnakeFlags.InWindow;
                    // Far LOD: no collisions or food, so reflect off walls and grow statistically.
                    float2 min = Region.Min + radius, max = Region.Max - radius;
                    bool2 low = head < min, high = head > max;
                    if (math.any(low | high))
                    {
                        heading = math.select(heading, -heading, low | high);
                        head = math.clamp(head, min, max);
                    }
                    if (info.Has(SnakeFlags.AI))
                        mass = math.min(mass + s.FarGrowthPerSecond * DeltaTime, s.AIMaxMass);
                }

                Head[i] = head;
                Heading[i] = heading;
                Speed[i] = speed;
                Mass[i] = math.max(mass, s.MinMass);
                Info[i] = info;
            }
        }
    }
}
