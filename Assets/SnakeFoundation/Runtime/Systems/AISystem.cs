using SPF.Contracts;
using SPF.L1.Geometry;
using SPF.L1.Spatial;
using SPF.Runtime.Scheduling;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SnakeFoundation.Systems
{
    /// <summary>
    /// Decide phase. Two Burst jobs over all snakes:
    /// decision (time-sliced, utility scoring of flee / hunt / seek food / wander) and
    /// steering (every tick, context steering over 12 directions with grid probes for avoidance).
    /// Uses last tick's grids, which stay valid until this tick's SpatialBuild.
    /// </summary>
    sealed class AISystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.Decide;

        public override void Declare(AccessDeclaration access) => access
            .Read(SnakeKeys.Head).Read(SnakeKeys.Heading).Read(SnakeKeys.Mass).Read(SnakeKeys.Radius)
            .Read(SnakeKeys.Speed).Read(SnakeKeys.Info)
            .Read(SnakeKeys.BodyGrid).Read(SnakeKeys.ItemGrid).Read(SnakeKeys.HeadGrid)
            .Read(SnakeKeys.FoodInfo)
            .Write(SnakeKeys.AI).Write(SnakeKeys.Control);

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var config = world.Resource(SnakeKeys.Config);
            var game = world.Resource(SnakeKeys.Game);
            var s = config.Settings;
            int snakes = context.Count(SnakeKeys.Snake);
            var region = config.Regions[game.ActiveRegion];
            var bodies = world.Resource(SnakeKeys.BodyGrid).AsReader();
            int interval = math.max(s.AIDecisionIntervalTicks, world.Resource(SnakeKeys.Quality).AIDecisionIntervalTicks);

            var decide = new DecisionJob
            {
                Head = context.Column(SnakeKeys.Head),
                Heading = context.Column(SnakeKeys.Heading),
                Mass = context.Column(SnakeKeys.Mass),
                Info = context.Column(SnakeKeys.Info),
                FoodInfo = context.Column(SnakeKeys.FoodInfo),
                AI = context.Column(SnakeKeys.AI),
                Heads = world.Resource(SnakeKeys.HeadGrid).AsReader(),
                Items = world.Resource(SnakeKeys.ItemGrid).AsReader(),
                Settings = s,
                Region = region,
                ActiveRegion = game.ActiveRegion,
                Tick = context.Time.Tick,
                Seed = context.Seed,
                Interval = interval,
                Focus = game.Focus,
            }.Schedule(snakes, 16, dependency);

            return new SteerJob
            {
                Head = context.Column(SnakeKeys.Head),
                Heading = context.Column(SnakeKeys.Heading),
                Mass = context.Column(SnakeKeys.Mass),
                Radius = context.Column(SnakeKeys.Radius),
                Speed = context.Column(SnakeKeys.Speed),
                Info = context.Column(SnakeKeys.Info),
                AI = context.Column(SnakeKeys.AI),
                Control = context.Column(SnakeKeys.Control),
                Bodies = bodies,
                Settings = s,
                Region = region,
                ActiveRegion = game.ActiveRegion,
                Tick = context.Time.Tick,
                Seed = context.Seed,
            }.Schedule(snakes, 8, decide);
        }

        struct HeadScan : IGridVisitor
        {
            public int Self;
            public float2 Head;
            public float Mass;
            [ReadOnly] public NativeArray<float2> Heading;
            [ReadOnly] public NativeArray<float> Masses;
            public int Threat;
            public float ThreatDistSq;
            public int Prey;
            public float PreyDistSq;

            public bool Visit(in GridEntry entry)
            {
                if (entry.Owner == Self) return true;
                float other = Masses[entry.Owner];
                float2 offset = Head - entry.Position;
                float d2 = math.lengthsq(offset);
                if (other > Mass * 1.2f)
                {
                    // Only a threat if it is coming towards us.
                    bool approaching = math.dot(Heading[entry.Owner], offset) > 0f;
                    if (approaching && d2 < ThreatDistSq)
                    {
                        Threat = entry.Owner;
                        ThreatDistSq = d2;
                    }
                }
                else if (other < Mass * 0.7f && d2 < PreyDistSq && (entry.Data & GridBits.ProtectedBit) == 0)
                {
                    Prey = entry.Owner;
                    PreyDistSq = d2;
                }
                return true;
            }
        }

        struct FoodScan : IGridVisitor
        {
            public float2 Head;
            [ReadOnly] public NativeArray<FoodInfo> FoodInfo;
            public float BestScore;
            public float2 Best;

            public bool Visit(in GridEntry entry)
            {
                // Props count as valuable food for the AI.
                float value = entry.Data == 0 ? FoodInfo[entry.Owner].Value : 6f;
                float score = value / (math.distance(Head, entry.Position) + 2f);
                if (score > BestScore)
                {
                    BestScore = score;
                    Best = entry.Position;
                }
                return true;
            }
        }

        [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
        struct DecisionJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float2> Head;
            [ReadOnly] public NativeArray<float2> Heading;
            [ReadOnly] public NativeArray<float> Mass;
            [ReadOnly] public NativeArray<SnakeInfo> Info;
            [ReadOnly] public NativeArray<FoodInfo> FoodInfo;
            public NativeArray<AIState> AI;
            public GridReader Heads;
            public GridReader Items;
            public SnakeSettings Settings;
            public RegionDef Region;
            public int ActiveRegion;
            public uint Tick;
            public uint Seed;
            public int Interval;
            public float2 Focus;

            public void Execute(int i)
            {
                var info = Info[i];
                if (!info.Has(SnakeFlags.AI) || info.Region != ActiveRegion || info.Has(SnakeFlags.Dead))
                    return;
                var ai = AI[i];
                if (Tick < ai.NextDecisionTick)
                    return;

                var random = SimRandom.Create(Seed, Tick, (uint)info.Id);
                float2 head = Head[i];
                float mass = Mass[i];
                bool inWindow = Items.Covers(head);
                ai.TargetRow = -1;
                ai.ThreatDistance = float.MaxValue;

                var scan = new HeadScan
                {
                    Self = i, Head = head, Mass = mass, Heading = Heading, Masses = Mass,
                    Threat = -1, ThreatDistSq = Settings.ThreatRadius * Settings.ThreatRadius * (0.5f + ai.Caution),
                    Prey = -1, PreyDistSq = Settings.HuntRadius * Settings.HuntRadius * ai.Aggression,
                };
                Heads.Query(head, math.max(Settings.ThreatRadius * 1.5f, Settings.HuntRadius), ref scan);

                if (scan.Threat >= 0)
                {
                    float2 away = math.normalizesafe(head - Head[scan.Threat], Heading[i]);
                    ai.Intent = AIIntent.Flee;
                    ai.Target = head + away * 40f;
                    ai.TargetRow = scan.Threat;
                    ai.ThreatDistance = math.sqrt(scan.ThreatDistSq);
                }
                else if (scan.Prey >= 0 && random.NextFloat() < ai.Aggression)
                {
                    float2 preyHead = Head[scan.Prey];
                    float dist = math.sqrt(scan.PreyDistSq);
                    ai.Intent = AIIntent.Hunt;
                    ai.Target = preyHead + Heading[scan.Prey] * (dist * 0.6f + 4f);
                    ai.TargetRow = scan.Prey;
                }
                else
                {
                    var food = new FoodScan { Head = head, FoodInfo = FoodInfo, BestScore = 0f };
                    if (inWindow)
                        Items.Query(head, Settings.FoodSearchRadius * (0.6f + ai.Greed * 0.6f), ref food);
                    if (food.BestScore > 0f)
                    {
                        ai.Intent = AIIntent.SeekFood;
                        ai.Target = food.Best;
                    }
                    else
                    {
                        ai.Intent = AIIntent.Wander;
                        float2 dir = GeoMath.FromAngle(random.NextFloat(0f, 2f * math.PI));
                        float2 target = random.NextFloat() < Settings.WanderTowardFocusChance
                            ? Focus + dir * random.NextFloat(0f, Settings.FocusWanderRadius)
                            : head + dir * random.NextFloat(0.3f, 1f) * Settings.WanderRadius;
                        ai.Target = math.clamp(target, Region.Min + 40f, Region.Max - 40f);
                    }
                }

                ai.NextDecisionTick = Tick + (uint)Interval + (uint)random.NextInt(0, math.max(Interval / 2, 1));
                AI[i] = ai;
            }
        }

        struct DangerProbe : IGridVisitor
        {
            public int Self;
            public bool Hit;

            public bool Visit(in GridEntry entry)
            {
                if (entry.Owner == Self) return true;
                Hit = true;
                return false;
            }
        }

        [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
        struct SteerJob : IJobParallelFor
        {
            const int Directions = 12;
            const float StepCos = 0.8660254f;   // cos(360 / Directions degrees)
            const float StepSin = 0.5f;         // sin(360 / Directions degrees)

            /// <summary>Fixed-size per-snake scratch (direction + one scalar per candidate), no allocation.</summary>
            struct DirectionSet
            {
                float4 m_X0, m_X1, m_X2, m_Y0, m_Y1, m_Y2, m_V0, m_V1, m_V2;

                public void Set(int k, float2 dir, float value)
                {
                    int lane = k & 3;
                    switch (k >> 2)
                    {
                        case 0: m_X0[lane] = dir.x; m_Y0[lane] = dir.y; m_V0[lane] = value; break;
                        case 1: m_X1[lane] = dir.x; m_Y1[lane] = dir.y; m_V1[lane] = value; break;
                        default: m_X2[lane] = dir.x; m_Y2[lane] = dir.y; m_V2[lane] = value; break;
                    }
                }

                public float2 Dir(int k)
                {
                    int lane = k & 3;
                    switch (k >> 2)
                    {
                        case 0: return new float2(m_X0[lane], m_Y0[lane]);
                        case 1: return new float2(m_X1[lane], m_Y1[lane]);
                        default: return new float2(m_X2[lane], m_Y2[lane]);
                    }
                }

                public float Value(int k)
                {
                    int lane = k & 3;
                    switch (k >> 2)
                    {
                        case 0: return m_V0[lane];
                        case 1: return m_V1[lane];
                        default: return m_V2[lane];
                    }
                }

                /// <summary>Unvisited candidate with the highest value (lowest k on ties).</summary>
                public int NextByValue(int visitedMask)
                {
                    int bestK = -1;
                    float bestValue = float.MinValue;
                    for (int k = 0; k < Directions; k++)
                    {
                        if ((visitedMask & (1 << k)) != 0) continue;
                        float v = Value(k);
                        if (v > bestValue) { bestValue = v; bestK = k; }
                    }
                    return bestK;
                }
            }

            [ReadOnly] public NativeArray<float2> Head;
            [ReadOnly] public NativeArray<float2> Heading;
            [ReadOnly] public NativeArray<float> Mass;
            [ReadOnly] public NativeArray<float> Radius;
            [ReadOnly] public NativeArray<float> Speed;
            [ReadOnly] public NativeArray<SnakeInfo> Info;
            [ReadOnly] public NativeArray<AIState> AI;
            public NativeArray<SnakeControl> Control;
            public GridReader Bodies;
            public SnakeSettings Settings;
            public RegionDef Region;
            public int ActiveRegion;
            public uint Tick;
            public uint Seed;

            public void Execute(int i)
            {
                var info = Info[i];
                if (!info.Has(SnakeFlags.AI) || info.Region != ActiveRegion || info.Has(SnakeFlags.Dead))
                    return;

                var ai = AI[i];
                float2 head = Head[i];
                float2 heading = Heading[i];
                float radius = Radius[i];
                float speed = math.max(Speed[i], Settings.BaseSpeed);
                float2 desired = math.normalizesafe(ai.Target - head, heading);
                bool inWindow = Bodies.Covers(head);

                float near = radius * 2f + speed * 0.25f;
                float far = radius * 2f + speed * 0.8f;
                float2 wallMin = Region.Min + radius * 2f, wallMax = Region.Max - radius * 2f;

                // Candidate directions: heading rotated by (k - Directions/2) * step, built by incremental
                // rotation (one sincos per snake instead of one per direction).
                var dirs = new DirectionSet();
                float2 dir = -heading;                          // k = 0 is straight back (-180 degrees)
                for (int k = 0; k < Directions; k++)
                {
                    dirs.Set(k, dir, 0.75f * math.max(0f, math.dot(dir, desired)) + 0.25f * math.max(0f, math.dot(dir, heading)));
                    dir = math.normalize(new float2(dir.x * StepCos - dir.y * StepSin, dir.x * StepSin + dir.y * StepCos));
                }

                // score = interest - 1.5 * danger <= interest, so directions are probed in descending
                // interest and probing stops once no remaining direction can beat the best score. The
                // result equals a full scan (ties keep the lower k), but far fewer grid queries run:
                // usually only the first one or two directions are probed.
                float2 best = desired;
                float bestScore = float.MinValue;
                int bestK = int.MaxValue;
                int probed = 0;                                   // bit k set = danger[k] known
                var danger = new DirectionSet();
                for (int step = 0; step < Directions; step++)
                {
                    int k = dirs.NextByValue(probed);
                    float interest = dirs.Value(k);
                    if (interest < bestScore)
                        break;
                    float d = Danger(i, head, dirs.Dir(k), near, far, radius, wallMin, wallMax, inWindow);
                    danger.Set(k, float2.zero, d);
                    probed |= 1 << k;
                    float score = interest - d * 1.5f;
                    if (score > bestScore || (score == bestScore && k < bestK)) { bestScore = score; best = dirs.Dir(k); bestK = k; }
                }
                if (bestScore < -0.5f)
                {
                    // Everything probed is dangerous: fall back to the least dangerous direction overall.
                    float leastDanger = float.MaxValue;
                    float2 safest = heading;
                    for (int k = 0; k < Directions; k++)
                    {
                        float d = (probed & (1 << k)) != 0
                            ? danger.Value(k)
                            : Danger(i, head, dirs.Dir(k), near, far, radius, wallMin, wallMax, inWindow);
                        if (d < leastDanger) { leastDanger = d; safest = dirs.Dir(k); }
                    }
                    best = safest;
                }

                bool boost = false;
                bool skill = false;
                float mass = Mass[i];
                if (ai.Intent == AIIntent.Flee && ai.ThreatDistance < 14f && mass > Settings.MinBoostMass)
                    boost = true;
                else if (ai.Intent == AIIntent.Hunt)
                {
                    float dist = math.distance(head, ai.Target);
                    boost = dist < 18f && mass > Settings.MinBoostMass * 1.5f;
                    var random = SimRandom.Create(Seed, Tick, (uint)info.Id ^ 0xA11u);
                    skill = dist < 25f && mass > Settings.Skill.MinMass && random.NextFloat() < 0.05f;
                }

                Control[i] = new SnakeControl { TargetDirection = best, Boost = boost, UseSkill = skill };
            }

            float Danger(int self, float2 head, float2 dir, float near, float far, float radius, float2 wallMin, float2 wallMax, bool inWindow)
            {
                if (Probe(self, head + dir * near, radius, wallMin, wallMax, inWindow))
                    return 1f;
                return Probe(self, head + dir * far, radius, wallMin, wallMax, inWindow) ? 0.6f : 0f;
            }

            bool Probe(int self, float2 point, float radius, float2 wallMin, float2 wallMax, bool inWindow)
            {
                if (math.any(point < wallMin) || math.any(point > wallMax))
                    return true;
                if (!inWindow)
                    return false;
                var probe = new DangerProbe { Self = self };
                Bodies.Query(point, radius * 1.1f, ref probe);
                return probe.Hit;
            }
        }
    }
}
