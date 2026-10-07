using System.Runtime.CompilerServices;
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
        /// <summary>Head grid: set on entries of invulnerable snakes.</summary>
        public const int ProtectedBit = 1 << 15;
        /// <summary>Body grid: set on a snake's head entry (body nodes have Data 0).</summary>
        public const int HeadBit = 1 << 14;
    }

    /// <summary>
    /// SpatialBuild phase, body grid (window), updated incrementally.
    /// <para>
    /// Body nodes are anchored to the trail: a snake has one node on every k-th trail point
    /// (global index g with g % k == 0, k = <see cref="SnakeSettings.NodeStep"/>), keyed by the point's
    /// slot in the <see cref="BodyStore"/>, plus one head entry (key TotalPoints + row,
    /// <see cref="GridBits.HeadBit"/>). Trail points never move once pushed, so per tick a snake only adds
    /// the nodes that appeared at the head end and removes the ones that expired at the tail: O(speed)
    /// instead of O(length). The snake is re-inserted ("resync") when its row changed (spawn, swap-back
    /// move), its trail was rewritten (<see cref="TrailState.Version"/>), its node step changed, it
    /// entered / left the window or the active state, or its radius left the stored band. Everything is
    /// rebuilt when the window moved or after a reset.
    /// </para>
    /// <para>
    /// Entries store a radius rounded up (×<see cref="RadiusBand"/>) so it stays valid while the snake
    /// grows a little; queries do the exact test with the Radius column (see ContactSystem), and
    /// protection is read from Info at query time, so neither forces a resync.
    /// Removals of all snakes run before insertions: a slab freed this tick may already belong to another
    /// snake, whose new keys must not be removed by the old owner's cleanup. Single job, deterministic.
    /// </para>
    /// </summary>
    sealed class BodyGridSystem : SimSystemBase
    {
        public const float RadiusBand = 1.05f;
        const float ResyncBelow = 1f / 1.1f;

        public struct NodeRecord
        {
            public int Start, Capacity;
            public uint Version;
            public uint Lo;
            public int Nodes;
            public int Step;
            public float Radius;
            public bool Active;
        }

        public const int StatResyncs = 0, StatAdded = 1, StatRemoved = 2, StatSlots = 3;

        ChangeLog m_Log;
        NativeArray<NodeRecord> m_Records;
        NativeArray<uint> m_AddFrom;
        NativeArray<byte> m_Dirty;
        NativeArray<int> m_Stats;
        int m_PrevCount;
        bool m_Built;

        public override SimPhase Phase => SimPhase.SpatialBuild;

        /// <summary>Diagnostics / tests (main thread, after the tick completed).</summary>
        public long FullRebuilds { get; private set; }
        public int LastResyncs => m_Stats[StatResyncs];
        public int LastAdded => m_Stats[StatAdded];
        public int LastRemoved => m_Stats[StatRemoved];

        /// <summary>Nodes currently stored for a row (tests).</summary>
        public NodeRecord Record(int row) => m_Records[row];

        public override void Declare(AccessDeclaration access) => access
            .Read(SnakeKeys.Snake).Read(SnakeKeys.Head).Read(SnakeKeys.Trail).Read(SnakeKeys.Radius)
            .Read(SnakeKeys.Info).Read(SnakeKeys.Bounds).Read(SnakeKeys.Bodies)
            .Write(SnakeKeys.BodyGrid);

        public override void OnCreate(SimWorld world)
        {
            try
            {
                var table = world.Table(SnakeKeys.Snake);
                m_Records = new NativeArray<NodeRecord>(table.Capacity, Allocator.Persistent);
                m_AddFrom = new NativeArray<uint>(table.Capacity, Allocator.Persistent);
                m_Dirty = new NativeArray<byte>(table.Capacity, Allocator.Persistent);
                m_Stats = new NativeArray<int>(StatSlots, Allocator.Persistent);
                m_Log = table.CreateChangeLog();
            }
            catch (System.Exception failure)
            {
                // OnCreate still owns partial buffers; the pipeline rolls back only completed systems.
                CleanupErrors.Try(ReleaseBuffers, ref failure);
                throw;
            }
        }

        public override void OnDestroy(SimWorld world) => ReleaseBuffers();

        void ReleaseBuffers()
        {
            System.Exception failure = null;
            if (m_Records.IsCreated) CleanupErrors.Try(() => m_Records.Dispose(), ref failure);
            if (m_AddFrom.IsCreated) CleanupErrors.Try(() => m_AddFrom.Dispose(), ref failure);
            if (m_Dirty.IsCreated) CleanupErrors.Try(() => m_Dirty.Dispose(), ref failure);
            if (m_Stats.IsCreated) CleanupErrors.Try(() => m_Stats.Dispose(), ref failure);
            CleanupErrors.ThrowIfAny(failure);
        }

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var config = world.Resource(SnakeKeys.Config);
            var grid = world.Resource(SnakeKeys.BodyGrid);
            var bodies = world.Resource(SnakeKeys.Bodies);
            int snakes = context.Count(SnakeKeys.Snake);

            bool full = !m_Built || m_Log.All || math.any(grid.Origin != grid.BuiltOrigin);
            if (!full)
                for (int i = 0; i < m_Log.Count; i++)
                    m_Dirty[m_Log[i]] = 1;
            m_Log.Clear();
            m_Built = true;
            int rows = full ? m_Records.Length : math.max(snakes, m_PrevCount);
            m_PrevCount = snakes;
            if (full) FullRebuilds++;
            grid.MarkBuilt();

            return new UpdateJob
            {
                Grid = grid.AsWriter(),
                Head = context.Column(SnakeKeys.Head),
                Trail = context.Column(SnakeKeys.Trail),
                Radius = context.Column(SnakeKeys.Radius),
                Info = context.Column(SnakeKeys.Info),
                Bounds = context.Column(SnakeKeys.Bounds),
                Points = bodies.Points,
                Records = m_Records,
                AddFrom = m_AddFrom,
                Dirty = m_Dirty,
                Stats = m_Stats,
                Rows = rows,
                SnakeCount = snakes,
                HeadKeyBase = bodies.TotalPoints,
                Full = full,
                WindowMin = grid.Origin,
                WindowMax = grid.Origin + grid.Size,
                ActiveRegion = world.Resource(SnakeKeys.Game).ActiveRegion,
                Settings = config.Settings,
            }.Schedule(dependency);
        }

        [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
        struct UpdateJob : IJob
        {
            public CellListGrid.Writer Grid;
            [ReadOnly] public NativeArray<float2> Head;
            [ReadOnly] public NativeArray<TrailState> Trail;
            [ReadOnly] public NativeArray<float> Radius;
            [ReadOnly] public NativeArray<SnakeInfo> Info;
            [ReadOnly] public NativeArray<float4> Bounds;
            [ReadOnly] public NativeArray<float2> Points;
            public NativeArray<NodeRecord> Records;
            public NativeArray<uint> AddFrom;
            public NativeArray<byte> Dirty;
            public NativeArray<int> Stats;
            public int Rows, SnakeCount, HeadKeyBase;
            public bool Full;
            public float2 WindowMin, WindowMax;
            public int ActiveRegion;
            public SnakeSettings Settings;

            public void Execute()
            {
                int resyncs = 0, added = 0, removed = 0;
                if (Full)
                {
                    Grid.Clear();
                    for (int row = 0; row < Records.Length; row++)
                    {
                        Records[row] = default;
                        Dirty[row] = 0;
                    }
                }

                // Pass 1: decide the new node range of every row, remove what is no longer wanted.
                for (int row = 0; row < Rows; row++)
                {
                    var old = Records[row];
                    var next = Desired(row, old);
                    bool resync = Dirty[row] != 0 || !next.Active || !old.Active || old.Version != next.Version ||
                                  old.Start != next.Start || old.Capacity != next.Capacity || old.Step != next.Step;
                    Dirty[row] = 0;
                    if (next.Active && !resync)
                    {
                        float r = Radius[row];
                        resync = r > old.Radius || r < old.Radius * ResyncBelow;
                    }
                    if (resync && next.Active)
                    {
                        next.Radius = Radius[row] * RadiusBand;
                        resyncs++;
                    }
                    else if (!resync)
                        next.Radius = old.Radius;

                    uint step = (uint)math.max(old.Step, 1);
                    if (old.Active)
                    {
                        // Keep [keepLo, oldHi]; on resync nothing is kept.
                        uint oldHi = old.Lo + (uint)(old.Nodes - 1) * step;
                        uint keepLo = resync || old.Nodes == 0 ? oldHi + step : math.max(next.Lo, old.Lo);
                        for (uint g = old.Lo; old.Nodes > 0 && g < keepLo && g <= oldHi; g += step, removed++)
                            Grid.Remove(Key(old, g));
                        if (!next.Active)
                            Grid.Remove(HeadKeyBase + row);
                        AddFrom[row] = resync || old.Nodes == 0 ? next.Lo : math.max(oldHi + step, next.Lo);
                    }
                    else
                        AddFrom[row] = next.Lo;
                    Records[row] = next;
                }

                // Pass 2: insert new nodes and move the heads.
                for (int row = 0; row < Rows; row++)
                {
                    var rec = Records[row];
                    if (!rec.Active) continue;
                    if (rec.Nodes > 0)
                    {
                        uint hi = rec.Lo + (uint)(rec.Nodes - 1) * (uint)rec.Step;
                        for (uint g = AddFrom[row]; g <= hi; g += (uint)rec.Step, added++)
                            Grid.Set(Key(rec, g), new GridEntry { Position = Points[Key(rec, g)], Radius = rec.Radius, Owner = row, Data = 0 });
                    }
                    Grid.Set(HeadKeyBase + row, new GridEntry { Position = Head[row], Radius = rec.Radius, Owner = row, Data = GridBits.HeadBit });
                }
                Stats[StatResyncs] = resyncs;
                Stats[StatAdded] = added;
                Stats[StatRemoved] = removed;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static int Key(in NodeRecord rec, uint g) => rec.Start + (int)(g & (uint)(rec.Capacity - 1));

            NodeRecord Desired(int row, in NodeRecord old)
            {
                var rec = default(NodeRecord);
                if (row >= SnakeCount) return rec;
                var info = Info[row];
                var b = Bounds[row];
                var trail = Trail[row];
                bool overlaps = b.x <= WindowMax.x && b.y <= WindowMax.y && b.z >= WindowMin.x && b.w >= WindowMin.y;
                if (info.Region != ActiveRegion || info.Has(SnakeFlags.Dead) || !overlaps || !trail.IsAllocated || trail.Count <= 0)
                    return rec;
                bool sameTrail = old.Active && old.Version == trail.Version && old.Start == trail.Start && old.Capacity == trail.Capacity;
                int step = Settings.NodeStep(Radius[row], trail.Spacing, sameTrail ? old.Step : 0);
                uint k = (uint)step;
                uint oldest = trail.Pushed - (uint)trail.Count;
                uint lo = (oldest + k - 1) / k * k;
                uint hi = (trail.Pushed - 1) / k * k;
                rec.Active = true;
                rec.Start = trail.Start;
                rec.Capacity = trail.Capacity;
                rec.Version = trail.Version;
                rec.Step = step;
                rec.Lo = lo;
                rec.Nodes = hi >= lo ? (int)((hi - lo) / k) + 1 : 0;
                return rec;
            }
        }
    }

    /// <summary>
    /// SpatialBuild phase, item grid (window): food then props (Data 0 / 1), keyed by row (props after
    /// the food capacity). Items only change structurally on the main thread (spawn / eat / stream), so the
    /// <see cref="CellListGrid"/> is updated incrementally from this system's own change logs: every changed
    /// row is re-inserted from its current data, rows freed by swap-back removal (≥ the new count) are
    /// removed. A full rebuild happens when the window moved, after a reset (log "All"), or when so many
    /// rows changed that rebuilding is cheaper.
    /// </summary>
    sealed class ItemGridSystem : SimSystemBase
    {
        ChangeLog m_FoodLog, m_PropLog;
        NativeArray<int> m_Dirty;
        int m_FoodCapacity;
        int m_PrevFood, m_PrevProps;
        bool m_Built;

        public override SimPhase Phase => SimPhase.SpatialBuild;

        /// <summary>Diagnostics / tests.</summary>
        public long FullRebuilds { get; private set; }
        public long IncrementalUpdates { get; private set; }
        public long DirtyRows { get; private set; }

        public override void Declare(AccessDeclaration access) => access
            .Read(SnakeKeys.Food).Read(SnakeKeys.FoodPosition).Read(SnakeKeys.FoodInfo)
            .Read(SnakeKeys.Prop).Read(SnakeKeys.PropPosition).Read(SnakeKeys.PropInfo)
            .Write(SnakeKeys.ItemGrid);

        public override void OnCreate(SimWorld world)
        {
            var food = world.Table(SnakeKeys.Food);
            var props = world.Table(SnakeKeys.Prop);
            m_FoodLog = food.CreateChangeLog();
            m_PropLog = props.CreateChangeLog();
            m_FoodCapacity = food.Capacity;
            m_Dirty = new NativeArray<int>(food.Capacity + props.Capacity, Allocator.Persistent);
        }

        public override void OnDestroy(SimWorld world)
        {
            if (m_Dirty.IsCreated) m_Dirty.Dispose();
        }

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var grid = world.Resource(SnakeKeys.ItemGrid);
            int foodCount = context.Count(SnakeKeys.Food);
            int propCount = context.Count(SnakeKeys.Prop);

            bool full = !m_Built || m_FoodLog.All || m_PropLog.All || math.any(grid.Origin != grid.BuiltOrigin);
            int dirty = 0;
            if (!full)
            {
                dirty = Collect(m_FoodLog, foodCount, m_PrevFood, 0, dirty);
                dirty = Collect(m_PropLog, propCount, m_PrevProps, m_FoodCapacity, dirty);
                // Re-inserting more than ~a quarter of the items costs about as much as a rebuild.
                full = dirty > (foodCount + propCount) / 4;
            }
            m_FoodLog.Clear();
            m_PropLog.Clear();
            m_Built = true;
            grid.MarkBuilt();
            m_PrevFood = foodCount;
            m_PrevProps = propCount;
            if (full) FullRebuilds++;
            else if (dirty > 0) { IncrementalUpdates++; DirtyRows += dirty; }
            else return dependency;

            return new UpdateJob
            {
                Grid = grid.AsWriter(),
                FoodPosition = context.Column(SnakeKeys.FoodPosition),
                FoodInfo = context.Column(SnakeKeys.FoodInfo),
                PropPosition = context.Column(SnakeKeys.PropPosition),
                PropInfo = context.Column(SnakeKeys.PropInfo),
                FoodCount = foodCount,
                PropCount = propCount,
                FoodCapacity = m_FoodCapacity,
                Full = full,
                Dirty = m_Dirty,
                DirtyCount = dirty,
            }.Schedule(dependency);
        }

        int Collect(ChangeLog log, int count, int previousCount, int keyOffset, int dirty)
        {
            for (int i = 0; i < log.Count && dirty < m_Dirty.Length; i++)
                m_Dirty[dirty++] = keyOffset + log[i];
            // Rows past the new end were vacated by swap-back removal (their marked destination row was
            // re-inserted above with the moved item); drop their stale entries.
            for (int row = count; row < previousCount && dirty < m_Dirty.Length; row++)
                m_Dirty[dirty++] = keyOffset + row;
            return dirty;
        }

        [BurstCompile(CompileSynchronously = true)]
        struct UpdateJob : IJob
        {
            public CellListGrid.Writer Grid;
            [ReadOnly] public NativeArray<float2> FoodPosition;
            [ReadOnly] public NativeArray<FoodInfo> FoodInfo;
            [ReadOnly] public NativeArray<float2> PropPosition;
            [ReadOnly] public NativeArray<PropInfo> PropInfo;
            [ReadOnly] public NativeArray<int> Dirty;
            public int FoodCount, PropCount, FoodCapacity, DirtyCount;
            public bool Full;

            public void Execute()
            {
                if (Full)
                {
                    Grid.Clear();
                    for (int row = 0; row < FoodCount; row++) SetFood(row);
                    for (int row = 0; row < PropCount; row++) SetProp(row);
                    return;
                }
                for (int i = 0; i < DirtyCount; i++)
                {
                    int key = Dirty[i];
                    if (key < FoodCapacity)
                    {
                        if (key < FoodCount) SetFood(key); else Grid.Remove(key);
                    }
                    else
                    {
                        int row = key - FoodCapacity;
                        if (row < PropCount) SetProp(row); else Grid.Remove(key);
                    }
                }
            }

            void SetFood(int row) =>
                Grid.Set(row, new GridEntry { Position = FoodPosition[row], Radius = FoodInfo[row].Radius, Owner = row, Data = 0 });

            void SetProp(int row) =>
                Grid.Set(FoodCapacity + row, new GridEntry { Position = PropPosition[row], Radius = PropInfo[row].Radius, Owner = row, Data = 1 });
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
