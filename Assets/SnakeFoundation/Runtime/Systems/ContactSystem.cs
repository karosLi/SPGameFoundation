using SPF.Contracts;
using SPF.Contracts.Collections;
using SPF.L1.Geometry;
using SPF.L1.Spatial;
using SPF.L2.Buffs;
using SPF.Runtime.Scheduling;
using SPF.Runtime.World;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SnakeFoundation.Systems
{
    /// <summary>
    /// Collision phase (detection only, rules live in <see cref="ResolveSystem"/>): head vs other bodies
    /// and heads, head vs walls, head vs items (eat candidates), projectiles vs bodies.
    /// </summary>
    sealed class ContactSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.Collision;

        public override void Declare(AccessDeclaration access) => access
            .Read(SnakeKeys.Head).Read(SnakeKeys.Radius).Read(SnakeKeys.Info).Read(SnakeKeys.Stats)
            .Read(SnakeKeys.BodyGrid).Read(SnakeKeys.ItemGrid)
            .Write(SnakeKeys.Contact).Write(SnakeKeys.Eats)
            .Read(SnakeKeys.Projectile).Write(SnakeKeys.ProjectilePosition).Write(SnakeKeys.ProjectileState)
            .Write(SnakeKeys.Hits).Write(SimWorld.DestroyQueueKey);

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var config = world.Resource(SnakeKeys.Config);
            var game = world.Resource(SnakeKeys.Game);
            var eats = world.Resource(SnakeKeys.Eats);
            var hits = world.Resource(SnakeKeys.Hits);

            var contacts = new SnakeContactJob
            {
                Head = context.Column(SnakeKeys.Head),
                Radius = context.Column(SnakeKeys.Radius),
                Info = context.Column(SnakeKeys.Info),
                Stats = context.Column(SnakeKeys.Stats),
                Contact = context.Column(SnakeKeys.Contact),
                Bodies = world.Resource(SnakeKeys.BodyGrid).AsReader(),
                Items = world.Resource(SnakeKeys.ItemGrid).AsReader(),
                Eats = eats.AsWriter(),
                Settings = config.Settings,
                Region = config.Regions[game.ActiveRegion],
                ActiveRegion = game.ActiveRegion,
            }.Schedule(context.Count(SnakeKeys.Snake), 16, dependency);

            var projectiles = new ProjectileJob
            {
                Handles = context.Handles(SnakeKeys.Projectile),
                Position = context.Column(SnakeKeys.ProjectilePosition),
                State = context.Column(SnakeKeys.ProjectileState),
                SnakeInfo = context.Column(SnakeKeys.Info),
                SnakeRadius = context.Column(SnakeKeys.Radius),
                Bodies = world.Resource(SnakeKeys.BodyGrid).AsReader(),
                Hits = hits.AsWriter(),
                Destroy = world.Resource(SimWorld.DestroyQueueKey).AsWriter(),
                DeltaTime = context.Time.DeltaTime,
                Region = config.Regions[game.ActiveRegion],
            }.Schedule(context.Count(SnakeKeys.Projectile), 32, dependency);

            return JobHandle.CombineDependencies(contacts, projectiles);
        }

        /// <summary>
        /// Classifies a head's contact with other snakes: another snake's head entry in reach means a
        /// head-on contact, otherwise any body node means a body hit; within a class the lowest row wins,
        /// so the result does not depend on cell order. The grid's entry radius is rounded up (see
        /// <see cref="BodyGridSystem"/>), so the exact test uses the Radius column; protection is read
        /// from Info.
        /// </summary>
        struct FirstOtherBody : IGridVisitor
        {
            public int Self;
            public float2 Center;
            public float Reach;
            [ReadOnly] public NativeArray<float> Radius;
            [ReadOnly] public NativeArray<SnakeInfo> Info;
            public int Other;
            public bool IsHead;

            public bool Visit(in GridEntry entry)
            {
                int owner = entry.Owner;
                if (owner == Self) return true;
                bool head = (entry.Data & GridBits.HeadBit) != 0;
                // Cheap rejections first: this entry cannot improve the current result.
                if (Other >= 0 && (IsHead && !head || head == IsHead && owner >= Other)) return true;
                float r = Reach + Radius[owner];
                if (math.distancesq(Center, entry.Position) >= r * r || Info[owner].Protection > 0f)
                    return true;
                Other = owner;
                IsHead = head;
                return !(IsHead && Other == 0);
            }
        }

        struct CollectItems : IGridVisitor
        {
            public int Self;
            public ParallelQueue<EatCandidate>.Writer Eats;

            public bool Visit(in GridEntry entry)
            {
                Eats.TryAdd(new EatCandidate
                {
                    TargetType = entry.Data == 0 ? EatTarget.Food : EatTarget.Prop,
                    TargetRow = entry.Owner,
                    SnakeRow = Self,
                });
                return true;
            }
        }

        [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
        struct SnakeContactJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float2> Head;
            [ReadOnly] public NativeArray<float> Radius;
            [ReadOnly] public NativeArray<SnakeInfo> Info;
            [ReadOnly] public NativeArray<EffectiveStats> Stats;
            public NativeArray<SnakeContact> Contact;
            public CellListReader Bodies;
            public CellListReader Items;
            public ParallelQueue<EatCandidate>.Writer Eats;
            public SnakeSettings Settings;
            public RegionDef Region;
            public int ActiveRegion;

            public void Execute(int i)
            {
                var info = Info[i];
                var contact = default(SnakeContact);
                if (info.Region != ActiveRegion || info.Has(SnakeFlags.Dead) || !info.Has(SnakeFlags.InWindow))
                {
                    Contact[i] = contact;
                    return;
                }

                float2 head = Head[i];
                float radius = Radius[i];

                if (math.any(head < Region.Min + radius * 0.5f) || math.any(head > Region.Max - radius * 0.5f))
                {
                    contact.Kind = ContactKind.Wall;
                    contact.OtherRow = -1;
                }
                else if (info.Protection <= 0f)
                {
                    float reach = radius * Settings.HitRadiusScale;
                    var probe = new FirstOtherBody { Self = i, Center = head, Reach = reach, Radius = Radius, Info = Info, Other = -1 };
                    Bodies.Query(head, reach, ref probe);
                    if (probe.Other >= 0)
                    {
                        contact.Kind = probe.IsHead ? ContactKind.Head : ContactKind.Body;
                        contact.OtherRow = probe.Other;
                    }
                }
                Contact[i] = contact;

                var eat = new CollectItems { Self = i, Eats = Eats };
                Items.Query(head, radius + Settings.EatRange + Stats[i].MagnetRadius, ref eat);
            }
        }

        /// <summary>Earliest body hit along the projectile's sweep (then lowest row), independent of cell order.</summary>
        struct FirstSweptHit : IGridVisitor
        {
            public float2 From, To;
            public float Radius;
            public int OwnerId;
            [ReadOnly] public NativeArray<SnakeInfo> Info;
            [ReadOnly] public NativeArray<float> SnakeRadius;
            public int Hit;
            public float HitT;

            public bool Visit(in GridEntry entry)
            {
                var info = Info[entry.Owner];
                if (info.Protection > 0f || info.Id == OwnerId)
                    return true;
                if (!GeoMath.SweptCircleHits(From, To, Radius, entry.Position, SnakeRadius[entry.Owner]))
                    return true;
                float2 d = To - From;
                float lenSq = math.lengthsq(d);
                float t = lenSq > 1e-8f ? math.saturate(math.dot(entry.Position - From, d) / lenSq) : 0f;
                if (Hit < 0 || t < HitT || (t == HitT && entry.Owner < Hit))
                {
                    Hit = entry.Owner;
                    HitT = t;
                }
                return true;
            }
        }

        [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
        struct ProjectileJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<EntityHandle> Handles;
            public NativeArray<float2> Position;
            public NativeArray<ProjectileState> State;
            [ReadOnly] public NativeArray<SnakeInfo> SnakeInfo;
            [ReadOnly] public NativeArray<float> SnakeRadius;
            public CellListReader Bodies;
            public ParallelQueue<ProjectileHit>.Writer Hits;
            public ParallelQueue<EntityHandle>.Writer Destroy;
            public float DeltaTime;
            public RegionDef Region;

            public void Execute(int i)
            {
                var state = State[i];
                float2 from = Position[i];
                float2 to = from + state.Velocity * DeltaTime;
                state.PreviousPosition = from;
                state.Life -= DeltaTime;
                Position[i] = to;
                State[i] = state;

                if (state.Life <= 0f || math.any(to < Region.Min) || math.any(to > Region.Max))
                {
                    Destroy.TryAdd(Handles[i]);
                    return;
                }

                var sweep = new FirstSweptHit { From = from, To = to, Radius = state.Radius, OwnerId = state.OwnerId, Info = SnakeInfo, SnakeRadius = SnakeRadius, Hit = -1 };
                float2 mid = (from + to) * 0.5f;
                Bodies.Query(mid, math.length(to - from) * 0.5f + state.Radius, ref sweep);
                if (sweep.Hit >= 0)
                    Hits.TryAdd(new ProjectileHit { ProjectileRow = i, SnakeRow = sweep.Hit, Position = to });
            }
        }
    }
}
