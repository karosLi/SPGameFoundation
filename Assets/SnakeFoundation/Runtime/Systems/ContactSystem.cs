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
                Bodies = world.Resource(SnakeKeys.BodyGrid).AsReader(),
                Hits = hits.AsWriter(),
                Destroy = world.Resource(SimWorld.DestroyQueueKey).AsWriter(),
                DeltaTime = context.Time.DeltaTime,
                Region = config.Regions[game.ActiveRegion],
            }.Schedule(context.Count(SnakeKeys.Projectile), 32, dependency);

            return JobHandle.CombineDependencies(contacts, projectiles);
        }

        struct FirstOtherBody : IGridVisitor
        {
            public int Self;
            public int Other;
            public int Node;

            public bool Visit(in GridEntry entry)
            {
                if (entry.Owner == Self || (entry.Data & GridSystem.ProtectedBit) != 0)
                    return true;
                Other = entry.Owner;
                Node = entry.Data & GridSystem.NodeMask;
                return false;
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

        [BurstCompile(FloatMode = FloatMode.Fast)]
        struct SnakeContactJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float2> Head;
            [ReadOnly] public NativeArray<float> Radius;
            [ReadOnly] public NativeArray<SnakeInfo> Info;
            [ReadOnly] public NativeArray<EffectiveStats> Stats;
            public NativeArray<SnakeContact> Contact;
            public GridReader Bodies;
            public GridReader Items;
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
                    var probe = new FirstOtherBody { Self = i, Other = -1 };
                    Bodies.Query(head, radius * Settings.HitRadiusScale, ref probe);
                    if (probe.Other >= 0)
                    {
                        contact.Kind = probe.Node == 0 ? ContactKind.Head : ContactKind.Body;
                        contact.OtherRow = probe.Other;
                    }
                }
                Contact[i] = contact;

                var eat = new CollectItems { Self = i, Eats = Eats };
                Items.Query(head, radius + Settings.EatRange + Stats[i].MagnetRadius, ref eat);
            }
        }

        struct FirstSweptHit : IGridVisitor
        {
            public float2 From, To;
            public float Radius;
            public int OwnerId;
            [ReadOnly] public NativeArray<SnakeInfo> Info;
            public int Hit;

            public bool Visit(in GridEntry entry)
            {
                if ((entry.Data & GridSystem.ProtectedBit) != 0 || Info[entry.Owner].Id == OwnerId)
                    return true;
                if (!GeoMath.SweptCircleHits(From, To, Radius, entry.Position, entry.Radius))
                    return true;
                Hit = entry.Owner;
                return false;
            }
        }

        [BurstCompile(FloatMode = FloatMode.Fast)]
        struct ProjectileJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<EntityHandle> Handles;
            public NativeArray<float2> Position;
            public NativeArray<ProjectileState> State;
            [ReadOnly] public NativeArray<SnakeInfo> SnakeInfo;
            public GridReader Bodies;
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

                var sweep = new FirstSweptHit { From = from, To = to, Radius = state.Radius, OwnerId = state.OwnerId, Info = SnakeInfo, Hit = -1 };
                float2 mid = (from + to) * 0.5f;
                Bodies.Query(mid, math.length(to - from) * 0.5f + state.Radius, ref sweep);
                if (sweep.Hit >= 0)
                    Hits.TryAdd(new ProjectileHit { ProjectileRow = i, SnakeRow = sweep.Hit, Position = to });
            }
        }
    }
}
