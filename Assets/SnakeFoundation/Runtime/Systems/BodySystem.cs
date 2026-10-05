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
            .Write(SnakeKeys.Bounds).Write(SnakeKeys.Info)
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
                Info = context.Column(SnakeKeys.Info),
                Points = world.Resource(SnakeKeys.Bodies).Points,
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
            public NativeArray<SnakeInfo> Info;
            // Each snake writes only inside its own slab.
            [NativeDisableParallelForRestriction] public NativeArray<float2> Points;
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

                // Bounds from every 4th kept point (spacing ≪ radius, so the error is below the padding).
                float2 min = head, max = head;
                uint newest = trail.Pushed - 1;
                for (int k = 0; k < trail.Count; k += 4)
                {
                    float2 p = Points[trail.Slot(newest - (uint)k)];
                    min = math.min(min, p);
                    max = math.max(max, p);
                }
                float pad = radius + trail.Spacing * 4f;
                Bounds[i] = new float4(min - pad, max + pad);

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
