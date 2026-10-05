using SPF.Contracts.Collections;
using SPF.Contracts;
using SPF.L1.Body;
using SPF.Runtime.Scheduling;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SnakeFoundation.Systems
{
    /// <summary>Body phase: size from mass, trail advance, interpolation arc, bounds, boost drops.</summary>
    sealed class BodySystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.Body;

        public override void Declare(AccessDeclaration access) => access
            .Read(SnakeKeys.Head).Read(SnakeKeys.PrevHead).Read(SnakeKeys.Mass)
            .Write(SnakeKeys.Trail).Write(SnakeKeys.PrevArc).Write(SnakeKeys.Radius).Write(SnakeKeys.Length)
            .Write(SnakeKeys.Bounds).Write(SnakeKeys.BoundsVersion).Write(SnakeKeys.Info)
            .Write(SnakeKeys.Bodies).Write(SnakeKeys.FoodSpawns);

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var config = world.Resource(SnakeKeys.Config);
            return new BodyJob
            {
                Head = context.Column(SnakeKeys.Head),
                PrevHead = context.Column(SnakeKeys.PrevHead),
                Mass = context.Column(SnakeKeys.Mass),
                Trail = context.Column(SnakeKeys.Trail),
                PrevArc = context.Column(SnakeKeys.PrevArc),
                Radius = context.Column(SnakeKeys.Radius),
                Length = context.Column(SnakeKeys.Length),
                Bounds = context.Column(SnakeKeys.Bounds),
                BoundsVersion = context.Column(SnakeKeys.BoundsVersion),
                Info = context.Column(SnakeKeys.Info),
                Points = world.Resource(SnakeKeys.Bodies).Points,
                BlockBounds = world.Resource(SnakeKeys.Bodies).BlockBounds,
                FoodSpawns = world.Resource(SnakeKeys.FoodSpawns).AsWriter(),
                Settings = config.Settings,
                ActiveRegion = world.Resource(SnakeKeys.Game).ActiveRegion,
            }.Schedule(context.Count(SnakeKeys.Snake), 16, dependency);
        }

        [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
        struct BodyJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float2> Head;
            [ReadOnly] public NativeArray<float2> PrevHead;
            [ReadOnly] public NativeArray<float> Mass;
            public NativeArray<TrailState> Trail;
            public NativeArray<float> PrevArc;
            public NativeArray<float> Radius;
            public NativeArray<float> Length;
            public NativeArray<float4> Bounds;
            public NativeArray<uint> BoundsVersion;
            public NativeArray<SnakeInfo> Info;
            // Each snake writes only inside its own slab.
            [NativeDisableParallelForRestriction] public NativeArray<float2> Points;
            [NativeDisableParallelForRestriction] public NativeArray<float4> BlockBounds;
            public ParallelQueue<FoodSpawnRequest>.Writer FoodSpawns;
            public SnakeSettings Settings;
            public int ActiveRegion;

            public void Execute(int i)
            {
                var info = Info[i];
                if (info.Region != ActiveRegion || info.Has(SnakeFlags.Dead))
                {
                    PrevArc[i] = math.length(Head[i] - Trail[i].Last);
                    return;
                }

                var s = Settings;
                float mass = Mass[i];
                float length = s.Growth.Length(mass);
                float radius = s.Growth.Radius(mass);
                float2 head = Head[i];
                var trail = Trail[i];

                uint pushedBefore = trail.Pushed;
                float gapBefore = math.length(PrevHead[i] - trail.Last);
                TrailMath.Advance(ref trail, Points, head, TrailMath.PointsForLength(length, trail.Spacing));
                PrevArc[i] = TrailMath.PreviousArc(pushedBefore, gapBefore, trail);

                // Bounds from per-16-point block boxes: only blocks completed this tick are scanned.
                if (BoundsVersion[i] != trail.Version)
                {
                    TrailBounds.Rebuild(trail, Points, BlockBounds);
                    BoundsVersion[i] = trail.Version;
                }
                else
                    TrailBounds.Append(trail, Points, BlockBounds, pushedBefore);
                float4 box = TrailBounds.Compute(trail, Points, BlockBounds, head);
                // One spacing of slack: renderers interpolate the tail up to a tick behind.
                float pad = radius + trail.Spacing;
                Bounds[i] = new float4(box.xy - pad, box.zw + pad);

                if (info.BoostDropAccumulator >= s.BoostDropMass)
                {
                    info.BoostDropAccumulator -= s.BoostDropMass;
                    float2 tail = TrailMath.SampleBehind(trail, Points, head, TrailMath.BodyLength(trail, head));
                    FoodSpawns.TryAdd(new FoodSpawnRequest
                    {
                        Position = tail,
                        Value = s.BoostDropMass / math.max(s.MassPerFoodValue, 1e-3f) * 0.8f,
                        Color = (uint)(info.Skin & 7),
                    });
                }

                Trail[i] = trail;
                Radius[i] = radius;
                Length[i] = length;
                Info[i] = info;
            }
        }
    }
}
