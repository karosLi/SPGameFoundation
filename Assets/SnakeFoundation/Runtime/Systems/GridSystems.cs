using SPF.Contracts;
using SPF.L1.Body;
using SPF.L1.Spatial;
using SPF.Runtime.Scheduling;
using SPF.Runtime.World;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SnakeFoundation.Systems
{
    /// <summary>Bit layout of <see cref="GridEntry.Data"/> for snake entries (16 bits available).</summary>
    static class GridBits
    {
        /// <summary>Set on entries of invulnerable snakes.</summary>
        public const int ProtectedBit = 1 << 15;
        /// <summary>Node index of a body entry (0 = head).</summary>
        public const int NodeMask = ProtectedBit - 1;
    }

    /// <summary>
    /// SpatialBuild phase, body grid (window): every body node of snakes overlapping the window;
    /// Data = node index with <see cref="GridBits.ProtectedBit"/> for invulnerable snakes.
    /// Staging is filled at deterministic offsets so the sorted grid is identical run to run.
    /// The item and head grids are separate systems with narrow access declarations, so they are
    /// built in parallel with AI / movement instead of waiting for the body update.
    /// </summary>
    sealed class BodyGridSystem : SimSystemBase
    {
        NativeArray<int> m_NodeOffsets;
        NativeArray<int> m_NodeCounts;

        public override SimPhase Phase => SimPhase.SpatialBuild;

        public override void Declare(AccessDeclaration access) => access
            .Read(SnakeKeys.Snake).Read(SnakeKeys.Head).Read(SnakeKeys.Trail).Read(SnakeKeys.Radius)
            .Read(SnakeKeys.Info).Read(SnakeKeys.Bounds).Read(SnakeKeys.Bodies)
            .Write(SnakeKeys.BodyGrid);

        public override void OnCreate(SimWorld world)
        {
            int capacity = world.Table(SnakeKeys.Snake).Capacity;
            m_NodeOffsets = new NativeArray<int>(capacity, Allocator.Persistent);
            m_NodeCounts = new NativeArray<int>(capacity, Allocator.Persistent);
        }

        public override void OnDestroy(SimWorld world)
        {
            if (m_NodeOffsets.IsCreated) m_NodeOffsets.Dispose();
            if (m_NodeCounts.IsCreated) m_NodeCounts.Dispose();
        }

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var config = world.Resource(SnakeKeys.Config);
            int activeRegion = world.Resource(SnakeKeys.Game).ActiveRegion;
            int snakes = context.Count(SnakeKeys.Snake);
            var bodyGrid = world.Resource(SnakeKeys.BodyGrid);

            var layout = new LayoutBodyJob
            {
                Head = context.Column(SnakeKeys.Head),
                Trail = context.Column(SnakeKeys.Trail),
                Radius = context.Column(SnakeKeys.Radius),
                Info = context.Column(SnakeKeys.Info),
                Bounds = context.Column(SnakeKeys.Bounds),
                Offsets = m_NodeOffsets,
                Counts = m_NodeCounts,
                StagingCount = bodyGrid.StagingCount,
                Capacity = bodyGrid.Capacity,
                SnakeCount = snakes,
                WindowMin = bodyGrid.Origin,
                WindowMax = bodyGrid.Origin + bodyGrid.Size,
                ActiveRegion = activeRegion,
                Settings = config.Settings,
            }.Schedule(dependency);

            var fillBody = new FillBodyJob
            {
                Head = context.Column(SnakeKeys.Head),
                Trail = context.Column(SnakeKeys.Trail),
                Radius = context.Column(SnakeKeys.Radius),
                Info = context.Column(SnakeKeys.Info),
                Points = world.Resource(SnakeKeys.Bodies).Points,
                Offsets = m_NodeOffsets,
                Counts = m_NodeCounts,
                Staging = bodyGrid.Staging,
                Settings = config.Settings,
            }.Schedule(snakes, 8, layout);
            return bodyGrid.ScheduleBuild(fillBody);
        }

        [BurstCompile(CompileSynchronously = true)]
        struct LayoutBodyJob : IJob
        {
            [ReadOnly] public NativeArray<float2> Head;
            [ReadOnly] public NativeArray<TrailState> Trail;
            [ReadOnly] public NativeArray<float> Radius;
            [ReadOnly] public NativeArray<SnakeInfo> Info;
            [ReadOnly] public NativeArray<float4> Bounds;
            public NativeArray<int> Offsets;
            public NativeArray<int> Counts;
            public NativeArray<int> StagingCount;
            public int Capacity;
            public int SnakeCount;
            public float2 WindowMin;
            public float2 WindowMax;
            public int ActiveRegion;
            public SnakeSettings Settings;

            public void Execute()
            {
                int total = 0;
                for (int i = 0; i < SnakeCount; i++)
                {
                    var info = Info[i];
                    var b = Bounds[i];
                    bool overlaps = b.x <= WindowMax.x && b.y <= WindowMax.y && b.z >= WindowMin.x && b.w >= WindowMin.y;
                    int nodes = 0;
                    if (info.Region == ActiveRegion && !info.Has(SnakeFlags.Dead) && overlaps)
                    {
                        float spacing = Settings.NodeSpacing(Radius[i]);
                        nodes = math.min(TrailMath.NodeCount(Trail[i], Head[i], Settings.TrailSpacing, spacing), Capacity - total);
                    }
                    Offsets[i] = total;
                    Counts[i] = nodes;
                    total += nodes;
                }
                StagingCount[0] = total;
            }
        }

        [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
        struct FillBodyJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float2> Head;
            [ReadOnly] public NativeArray<TrailState> Trail;
            [ReadOnly] public NativeArray<float> Radius;
            [ReadOnly] public NativeArray<SnakeInfo> Info;
            [ReadOnly] public NativeArray<float2> Points;
            [ReadOnly] public NativeArray<int> Offsets;
            [ReadOnly] public NativeArray<int> Counts;
            [NativeDisableParallelForRestriction] public NativeArray<GridEntry> Staging;
            public SnakeSettings Settings;

            public void Execute(int i)
            {
                int count = Counts[i];
                if (count == 0) return;
                int offset = Offsets[i];
                float radius = Radius[i];
                float spacing = Settings.NodeSpacing(radius);
                float2 head = Head[i];
                var trail = Trail[i];
                float gap = math.length(head - trail.Last);
                int flags = Info[i].Protection > 0f ? GridBits.ProtectedBit : 0;
                for (int n = 0; n < count; n++)
                {
                    Staging[offset + n] = new GridEntry
                    {
                        Position = TrailMath.SampleAtArc(trail, Points, head, gap, n * spacing, Settings.TrailSpacing),
                        Radius = radius,
                        Owner = i,
                        Data = n | flags,
                    };
                }
            }
        }
    }

    /// <summary>
    /// SpatialBuild phase, item grid (window): food then props (Data 0 / 1). Items only change
    /// structurally on the main thread (spawn / eat / stream), so the grid is rebuilt only when either
    /// table's <see cref="SimTable.Version"/> or the window origin changed since the last build.
    /// </summary>
    sealed class ItemGridSystem : SimSystemBase, IResettableSystem
    {
        uint m_FoodVersion, m_PropVersion;
        float2 m_Origin;
        bool m_Built;

        public override SimPhase Phase => SimPhase.SpatialBuild;

        /// <summary>Builds skipped because nothing changed (diagnostics / tests).</summary>
        public long SkippedBuilds { get; private set; }

        public void OnReset(SimWorld world) => m_Built = false;

        public override void Declare(AccessDeclaration access) => access
            .Read(SnakeKeys.Food).Read(SnakeKeys.FoodPosition).Read(SnakeKeys.FoodInfo)
            .Read(SnakeKeys.Prop).Read(SnakeKeys.PropPosition).Read(SnakeKeys.PropInfo)
            .Write(SnakeKeys.ItemGrid);

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var itemGrid = world.Resource(SnakeKeys.ItemGrid);
            uint foodVersion = world.Table(SnakeKeys.Food).Version;
            uint propVersion = world.Table(SnakeKeys.Prop).Version;
            if (m_Built && foodVersion == m_FoodVersion && propVersion == m_PropVersion && math.all(itemGrid.Origin == m_Origin))
            {
                SkippedBuilds++;
                return dependency;
            }
            m_Built = true;
            m_FoodVersion = foodVersion;
            m_PropVersion = propVersion;
            m_Origin = itemGrid.Origin;

            int foodCount = context.Count(SnakeKeys.Food);
            int propCount = context.Count(SnakeKeys.Prop);
            int total = math.min(foodCount + propCount, itemGrid.Capacity);
            var fillItems = new FillItemsJob
            {
                FoodPosition = context.Column(SnakeKeys.FoodPosition),
                FoodInfo = context.Column(SnakeKeys.FoodInfo),
                PropPosition = context.Column(SnakeKeys.PropPosition),
                PropInfo = context.Column(SnakeKeys.PropInfo),
                FoodCount = foodCount,
                Staging = itemGrid.Staging,
            }.Schedule(total, 256, dependency);
            var setItemCount = new SetCountJob { Count = itemGrid.StagingCount, Value = total }.Schedule(fillItems);
            return itemGrid.ScheduleBuild(setItemCount);
        }

        [BurstCompile(CompileSynchronously = true)]
        struct FillItemsJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float2> FoodPosition;
            [ReadOnly] public NativeArray<FoodInfo> FoodInfo;
            [ReadOnly] public NativeArray<float2> PropPosition;
            [ReadOnly] public NativeArray<PropInfo> PropInfo;
            public int FoodCount;
            [NativeDisableParallelForRestriction] public NativeArray<GridEntry> Staging;

            public void Execute(int i)
            {
                if (i < FoodCount)
                {
                    Staging[i] = new GridEntry { Position = FoodPosition[i], Radius = FoodInfo[i].Radius, Owner = i, Data = 0 };
                }
                else
                {
                    int p = i - FoodCount;
                    Staging[i] = new GridEntry { Position = PropPosition[p], Radius = PropInfo[p].Radius, Owner = p, Data = 1 };
                }
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        struct SetCountJob : IJob
        {
            public NativeArray<int> Count;
            public int Value;
            public void Execute() => Count[0] = Value;
        }
    }

    /// <summary>SpatialBuild phase, head grid (whole region, coarse): one entry per active snake.</summary>
    sealed class HeadGridSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.SpatialBuild;

        public override void Declare(AccessDeclaration access) => access
            .Read(SnakeKeys.Snake).Read(SnakeKeys.Head).Read(SnakeKeys.Radius).Read(SnakeKeys.Info)
            .Write(SnakeKeys.HeadGrid);

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var headGrid = world.Resource(SnakeKeys.HeadGrid);
            var fillHeads = new FillHeadsJob
            {
                Head = context.Column(SnakeKeys.Head),
                Radius = context.Column(SnakeKeys.Radius),
                Info = context.Column(SnakeKeys.Info),
                Staging = headGrid.Staging,
                StagingCount = headGrid.StagingCount,
                SnakeCount = context.Count(SnakeKeys.Snake),
                ActiveRegion = world.Resource(SnakeKeys.Game).ActiveRegion,
            }.Schedule(dependency);
            return headGrid.ScheduleBuild(fillHeads);
        }

        [BurstCompile(CompileSynchronously = true)]
        struct FillHeadsJob : IJob
        {
            [ReadOnly] public NativeArray<float2> Head;
            [ReadOnly] public NativeArray<float> Radius;
            [ReadOnly] public NativeArray<SnakeInfo> Info;
            public NativeArray<GridEntry> Staging;
            public NativeArray<int> StagingCount;
            public int SnakeCount;
            public int ActiveRegion;

            public void Execute()
            {
                int n = 0;
                for (int i = 0; i < SnakeCount && n < Staging.Length; i++)
                {
                    var info = Info[i];
                    if (info.Region != ActiveRegion || info.Has(SnakeFlags.Dead)) continue;
                    Staging[n++] = new GridEntry { Position = Head[i], Radius = Radius[i], Owner = i, Data = info.Protection > 0f ? GridBits.ProtectedBit : 0 };
                }
                StagingCount[0] = n;
            }
        }
    }
}
