using System;
using System.Runtime.CompilerServices;
using SPF.Contracts;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SPF.L1.Spatial
{
    /// <summary>
    /// One circle registered in a grid: 16 bytes, so four entries fill a 64-byte cache line exactly and an
    /// entry is one 128-bit load. <see cref="Owner"/> and <see cref="Data"/> are free for the producer
    /// (e.g. snake row / node index) and are packed into one word: each must be in [0, 65535].
    /// </summary>
    public struct GridEntry
    {
        public const int MaxOwner = 0xFFFF;
        public const int MaxData = 0xFFFF;

        public float2 Position;
        public float Radius;
        uint m_OwnerData;

        public int Owner
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => (int)(m_OwnerData & 0xFFFFu);
            [MethodImpl(MethodImplOptions.AggressiveInlining)] set => m_OwnerData = (m_OwnerData & 0xFFFF0000u) | ((uint)value & 0xFFFFu);
        }

        public int Data
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get => (int)(m_OwnerData >> 16);
            [MethodImpl(MethodImplOptions.AggressiveInlining)] set => m_OwnerData = (m_OwnerData & 0xFFFFu) | ((uint)value << 16);
        }
    }

    /// <summary>Callback for grid queries; return false to stop the query early.</summary>
    public interface IGridVisitor
    {
        bool Visit(in GridEntry entry);
    }

    /// <summary>
    /// Dense uniform grid over a rectangular window, rebuilt every tick with a counting sort
    /// (no hashing, no atomics, deterministic order). Producers fill <see cref="Staging"/> and set
    /// <see cref="StagingCount"/>; <see cref="ScheduleBuild"/> sorts entries by cell; queries use
    /// <see cref="GridReader"/>. Entries outside the window are dropped and counted.
    /// Used both as the fine window grid around the player and as the coarse whole-region grid.
    /// <para>
    /// Optional large-radius layer: a query must reach as far as the largest entry radius, so a few big
    /// entries would widen every query. With <c>largeRadius</c> &gt; 0, entries above it go to a second,
    /// coarser grid (cells <c>largeCellScale</c> times bigger) and each layer is queried with its own
    /// maximum radius. Readers see one grid; visiting order is small layer first, then large.
    /// </para>
    /// </summary>
    public sealed class SpatialGrid : IDisposable, IResettableResource, IJobData, ISnapshotResource
    {
        NativeArray<GridEntry> m_Staging;
        NativeArray<int> m_StagingCount;
        NativeArray<GridEntry> m_Entries;
        NativeArray<int> m_CellStart;
        NativeArray<GridEntry> m_LargeEntries;
        NativeArray<int> m_LargeCellStart;
        NativeArray<int> m_Stats;

        public SpatialGrid(int2 dimensions, float cellSize, int capacity, float largeRadius = 0f, int largeCellScale = 4)
        {
            if (math.any(dimensions <= 0)) throw new ArgumentOutOfRangeException(nameof(dimensions));
            if (!math.isfinite(cellSize) || cellSize <= 0f) throw new ArgumentOutOfRangeException(nameof(cellSize));
            if (capacity < 0 || capacity > GridEntry.MaxOwner + 1) throw new ArgumentOutOfRangeException(nameof(capacity), "entry owners are 16-bit");
            if (largeCellScale < 1) throw new ArgumentOutOfRangeException(nameof(largeCellScale));
            Dimensions = dimensions;
            CellSize = cellSize;
            LargeRadius = largeRadius;
            LargeCellScale = largeRadius > 0f ? largeCellScale : 1;
            LargeDimensions = largeRadius > 0f ? (dimensions + largeCellScale - 1) / largeCellScale : new int2(1, 1);
            int cells = dimensions.x * dimensions.y;
            m_Staging = new NativeArray<GridEntry>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            m_StagingCount = new NativeArray<int>(1, Allocator.Persistent);
            m_Entries = new NativeArray<GridEntry>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            m_CellStart = new NativeArray<int>(cells + 1, Allocator.Persistent);
            // Allocated (tiny) even when disabled: jobs may not hold unassigned containers.
            m_LargeEntries = new NativeArray<GridEntry>(largeRadius > 0f ? capacity : 1, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            m_LargeCellStart = new NativeArray<int>(LargeDimensions.x * LargeDimensions.y + 1, Allocator.Persistent);
            m_Stats = new NativeArray<int>(StatCount, Allocator.Persistent);
        }

        internal const int StatEntryCount = 0;
        internal const int StatDropped = 1;
        internal const int StatMaxRadiusBits = 2;
        internal const int StatLargeCount = 3;
        internal const int StatLargeMaxRadiusBits = 4;
        const int StatCount = 5;

        public int2 Dimensions { get; }
        public float CellSize { get; }
        public int Capacity => m_Staging.Length;

        /// <summary>Entries with a larger radius go to the coarse layer (0 = single layer).</summary>
        public float LargeRadius { get; }
        public int LargeCellScale { get; }
        public int2 LargeDimensions { get; }

        /// <summary>World position of the grid's minimum corner. Set on the main thread before producers run.</summary>
        public float2 Origin { get; set; }

        public float2 Size => (float2)Dimensions * CellSize;

        /// <summary>Origin used by the last scheduled build; readers use it so a moved window never mismatches built data.</summary>
        public float2 BuiltOrigin { get; private set; }

        public NativeArray<GridEntry> Staging => m_Staging;
        public NativeArray<int> StagingCount => m_StagingCount;

        /// <summary>Valid after the build job completed.</summary>
        public int EntryCount => m_Stats[StatEntryCount];
        /// <summary>Requested entries beyond capacity plus entries outside the fine window.</summary>
        public int DroppedLastBuild => m_Stats[StatDropped];
        public int LargeEntryCount => m_Stats[StatLargeCount];

        /// <summary>
        /// Recentres the window on a focus point with hysteresis: the origin only moves (in whole cells)
        /// when the focus leaves the central half of the window. Returns true when it moved.
        /// </summary>
        public bool Follow(float2 focus, float2 regionMin, float2 regionMax)
        {
            float2 origin = FollowOrigin(Origin, Size, CellSize, focus, regionMin, regionMax);
            bool moved = math.any(origin != Origin);
            Origin = origin;
            return moved;
        }

        /// <summary>
        /// Window origin after following <paramref name="focus"/>: unchanged while the focus stays in the
        /// middle half, otherwise re-centred on it (cell-aligned, clamped to the region).
        /// </summary>
        public static float2 FollowOrigin(float2 origin, float2 size, float cellSize, float2 focus, float2 regionMin, float2 regionMax)
        {
            float2 local = focus - origin;
            if (math.all(local >= size * 0.25f) && math.all(local <= size * 0.75f))
                return origin;
            float2 centred = math.floor((focus - size * 0.5f) / cellSize) * cellSize;
            float2 maxOrigin = math.max(regionMax - size, regionMin);
            return math.clamp(centred, math.floor(regionMin / cellSize) * cellSize, math.ceil(maxOrigin / cellSize) * cellSize);
        }

        public JobHandle ScheduleBuild(JobHandle dependency)
        {
            BuiltOrigin = Origin;
            return new BuildJob
            {
                Staging = m_Staging,
                StagingCount = m_StagingCount,
                Entries = m_Entries,
                CellStart = m_CellStart,
                LargeEntries = m_LargeEntries,
                LargeCellStart = m_LargeCellStart,
                Stats = m_Stats,
                Origin = Origin,
                WindowMax = Origin + Size,
                InvCellSize = 1f / CellSize,
                Dimensions = Dimensions,
                LargeRadius = LargeRadius > 0f ? LargeRadius : float.MaxValue,
                InvLargeCellSize = 1f / (CellSize * LargeCellScale),
                LargeDimensions = LargeDimensions,
            }.Schedule(dependency);
        }

        public GridReader AsReader() => new GridReader(m_Entries, m_CellStart, m_LargeEntries, m_LargeCellStart, m_Stats,
            BuiltOrigin, CellSize, Dimensions, CellSize * LargeCellScale, LargeDimensions);

        public void OnReset()
        {
            m_StagingCount[0] = 0;
            for (int i = 0; i < StatCount; i++) m_Stats[i] = 0;
            for (int i = 0; i < m_CellStart.Length; i++) m_CellStart[i] = 0;
            for (int i = 0; i < m_LargeCellStart.Length; i++) m_LargeCellStart[i] = 0;
        }

        /// <summary>Saves the built grid (readers of the next tick query it before it is rebuilt) and staging.</summary>
        public void WriteSnapshot(System.IO.BinaryWriter writer)
        {
            writer.Write(Origin.x); writer.Write(Origin.y);
            writer.Write(BuiltOrigin.x); writer.Write(BuiltOrigin.y);
            NativeIO.Write(writer, m_Stats);
            NativeIO.Write(writer, m_StagingCount);
            NativeIO.Write(writer, m_Staging, math.min(m_StagingCount[0], m_Staging.Length));
            NativeIO.Write(writer, m_CellStart);
            NativeIO.Write(writer, m_Entries, m_CellStart[m_CellStart.Length - 1]);
            NativeIO.Write(writer, m_LargeCellStart);
            NativeIO.Write(writer, m_LargeEntries, m_LargeCellStart[m_LargeCellStart.Length - 1]);
        }

        public void ReadSnapshot(System.IO.BinaryReader reader)
        {
            Origin = new float2(reader.ReadSingle(), reader.ReadSingle());
            BuiltOrigin = new float2(reader.ReadSingle(), reader.ReadSingle());
            NativeIO.ReadAll(reader, m_Stats);
            NativeIO.ReadAll(reader, m_StagingCount);
            NativeIO.Read(reader, m_Staging);
            NativeIO.ReadAll(reader, m_CellStart);
            if (NativeIO.Read(reader, m_Entries) != m_CellStart[m_CellStart.Length - 1])
                throw new System.IO.InvalidDataException("Spatial grid snapshot entry count mismatch.");
            NativeIO.ReadAll(reader, m_LargeCellStart);
            if (NativeIO.Read(reader, m_LargeEntries) != m_LargeCellStart[m_LargeCellStart.Length - 1])
                throw new System.IO.InvalidDataException("Spatial grid snapshot large entry count mismatch.");
        }

        public void Dispose()
        {
            if (m_Staging.IsCreated) m_Staging.Dispose();
            if (m_StagingCount.IsCreated) m_StagingCount.Dispose();
            if (m_Entries.IsCreated) m_Entries.Dispose();
            if (m_CellStart.IsCreated) m_CellStart.Dispose();
            if (m_LargeEntries.IsCreated) m_LargeEntries.Dispose();
            if (m_LargeCellStart.IsCreated) m_LargeCellStart.Dispose();
            if (m_Stats.IsCreated) m_Stats.Dispose();
        }

        /// <summary>
        /// Counting sort by cell, per layer: count, inclusive prefix sum, then a reverse scatter that
        /// decrements each cell's end cursor. Iterating entries backwards keeps the order within a cell
        /// stable (staging order) and leaves the cursors at each cell's start: two passes over the cells
        /// and two over the entries.
        /// </summary>
        [BurstCompile(CompileSynchronously = true)]
        struct BuildJob : IJob
        {
            [ReadOnly] public NativeArray<GridEntry> Staging;
            public NativeArray<int> StagingCount;
            public NativeArray<GridEntry> Entries;
            public NativeArray<int> CellStart;
            public NativeArray<GridEntry> LargeEntries;
            public NativeArray<int> LargeCellStart;
            public NativeArray<int> Stats;
            public float2 Origin, WindowMax;
            public float InvCellSize;
            public int2 Dimensions;
            public float LargeRadius;
            public float InvLargeCellSize;
            public int2 LargeDimensions;

            public void Execute()
            {
                int requested = math.max(0, StagingCount[0]);
                int count = math.min(requested, Staging.Length);
                int cells = Dimensions.x * Dimensions.y;
                int largeCells = LargeDimensions.x * LargeDimensions.y;
                for (int c = 0; c < cells; c++) CellStart[c] = 0;
                for (int c = 0; c < largeCells; c++) LargeCellStart[c] = 0;

                // Pass 1: per-cell counts.
                int dropped = requested - count, large = 0;
                float maxRadius = 0f, maxLarge = 0f;
                for (int i = 0; i < count; i++)
                {
                    var e = Staging[i];
                    bool isLarge = e.Radius > LargeRadius;
                    int index = isLarge ? CellIndex(e.Position, InvLargeCellSize, LargeDimensions) : CellIndex(e.Position, InvCellSize, Dimensions);
                    if (index < 0) { dropped++; continue; }
                    if (isLarge)
                    {
                        LargeCellStart[index]++;
                        large++;
                        maxLarge = math.max(maxLarge, e.Radius);
                    }
                    else
                    {
                        CellStart[index]++;
                        maxRadius = math.max(maxRadius, e.Radius);
                    }
                }

                // Pass 2: inclusive prefix sums -> start[c] = end of cell c.
                int running = 0;
                for (int c = 0; c < cells; c++) { running += CellStart[c]; CellStart[c] = running; }
                CellStart[cells] = running;
                running = 0;
                for (int c = 0; c < largeCells; c++) { running += LargeCellStart[c]; LargeCellStart[c] = running; }
                LargeCellStart[largeCells] = running;

                // Pass 3: reverse stable scatter; afterwards start[c] = start of cell c.
                if (large == 0)
                {
                    for (int i = count - 1; i >= 0; i--)
                    {
                        var e = Staging[i];
                        if (e.Radius > LargeRadius) continue; // no large entries survived the counting pass
                        int index = CellIndex(e.Position, InvCellSize, Dimensions);
                        if (index >= 0) Entries[--CellStart[index]] = e;
                    }
                }
                else
                {
                    for (int i = count - 1; i >= 0; i--)
                    {
                        var e = Staging[i];
                        if (e.Radius > LargeRadius)
                        {
                            int index = CellIndex(e.Position, InvLargeCellSize, LargeDimensions);
                            if (index >= 0) LargeEntries[--LargeCellStart[index]] = e;
                        }
                        else
                        {
                            int index = CellIndex(e.Position, InvCellSize, Dimensions);
                            if (index >= 0) Entries[--CellStart[index]] = e;
                        }
                    }
                }

                Stats[StatEntryCount] = CellStart[cells] + LargeCellStart[largeCells];
                Stats[StatDropped] = dropped;
                Stats[StatMaxRadiusBits] = math.asint(maxRadius);
                Stats[StatLargeCount] = large;
                Stats[StatLargeMaxRadiusBits] = math.asint(maxLarge);
                StagingCount[0] = 0;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            int CellIndex(float2 position, float invCellSize, int2 dimensions)
            {
                // The coarse layer may round its dimensions up; it must not extend the accepted window.
                if (!math.all(position >= Origin) || !math.all(position < WindowMax)) return -1;
                // Reciprocal multiplication can round an accepted upper-edge point to dimensions.
                // Once the world-window check passed, keep that point in the last valid cell.
                int2 cell = math.clamp((int2)math.floor((position - Origin) * invCellSize), 0, dimensions - 1);
                return cell.y * dimensions.x + cell.x;
            }
        }
    }

    /// <summary>Optional diagnostic counters. EntriesExamined precedes the circle filter; VisitorCalls follows it.</summary>
    public struct GridQueryStats
    {
        public long CellsVisited, CellsPruned, EntriesExamined, VisitorCalls;
    }

    interface IGridQueryMetrics
    {
        void Cell(bool pruned);
        void Entry();
        void Visit();
    }
    struct NoGridQueryMetrics : IGridQueryMetrics
    {
        public void Cell(bool pruned) { }
        public void Entry() { }
        public void Visit() { }
    }
    struct CountGridQueryMetrics : IGridQueryMetrics
    {
        public GridQueryStats Value;
        public void Cell(bool pruned) { Value.CellsVisited++; if (pruned) Value.CellsPruned++; }
        public void Entry() { Value.EntriesExamined++; }
        public void Visit() { Value.VisitorCalls++; }
    }

    /// <summary>Read-only view of a built grid for use in jobs.</summary>
    public struct GridReader
    {
        [ReadOnly] NativeArray<GridEntry> m_Entries;
        [ReadOnly] NativeArray<int> m_CellStart;
        [ReadOnly] NativeArray<GridEntry> m_LargeEntries;
        [ReadOnly] NativeArray<int> m_LargeCellStart;
        [ReadOnly] NativeArray<int> m_Stats;
        float2 m_Origin;
        float m_CellSize;
        float m_InvCellSize;
        int2 m_Dimensions;
        float m_InvLargeCellSize;
        int2 m_LargeDimensions;

        internal GridReader(NativeArray<GridEntry> entries, NativeArray<int> cellStart, NativeArray<GridEntry> largeEntries,
            NativeArray<int> largeCellStart, NativeArray<int> stats, float2 origin, float cellSize, int2 dimensions,
            float largeCellSize, int2 largeDimensions)
        {
            m_Entries = entries;
            m_CellStart = cellStart;
            m_LargeEntries = largeEntries;
            m_LargeCellStart = largeCellStart;
            m_Stats = stats;
            m_Origin = origin;
            m_CellSize = cellSize;
            m_InvCellSize = 1f / cellSize;
            m_Dimensions = dimensions;
            m_InvLargeCellSize = 1f / largeCellSize;
            m_LargeDimensions = largeDimensions;
        }

        public float2 Origin => m_Origin;
        public float2 Max => m_Origin + (float2)m_Dimensions * m_CellSize;

        /// <summary>Largest radius over both layers.</summary>
        public float MaxEntryRadius => math.max(math.asfloat(m_Stats[SpatialGrid.StatMaxRadiusBits]), math.asfloat(m_Stats[SpatialGrid.StatLargeMaxRadiusBits]));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Covers(float2 position) => math.all(position >= m_Origin) && math.all(position < Max);

        /// <summary>Visits every entry whose circle overlaps the query circle.</summary>
        public void Query<TVisitor>(float2 center, float radius, ref TVisitor visitor) where TVisitor : struct, IGridVisitor
        {
            if (!QueryLayer(m_Entries, m_CellStart, m_InvCellSize, m_Dimensions,
                    radius + math.asfloat(m_Stats[SpatialGrid.StatMaxRadiusBits]), center, radius, ref visitor))
                return;
            if (m_Stats[SpatialGrid.StatLargeCount] > 0)
                QueryLayer(m_LargeEntries, m_LargeCellStart, m_InvLargeCellSize, m_LargeDimensions,
                    radius + math.asfloat(m_Stats[SpatialGrid.StatLargeMaxRadiusBits]), center, radius, ref visitor);
        }

        bool QueryLayer<TVisitor>(NativeArray<GridEntry> entries, NativeArray<int> cellStart, float invCellSize, int2 dimensions,
            float reach, float2 center, float radius, ref TVisitor visitor) where TVisitor : struct, IGridVisitor
        {
            int2 min = math.max((int2)math.floor((center - reach - m_Origin) * invCellSize), 0);
            min = ClampAcceptedLowerCell(min, center - reach, dimensions);
            int2 max = math.min((int2)math.floor((center + reach - m_Origin) * invCellSize), dimensions - 1);
            for (int y = min.y; y <= max.y; y++)
            {
                int row = y * dimensions.x;
                for (int x = min.x; x <= max.x; x++)
                {
                    int cell = row + x;
                    int end = cellStart[cell + 1];
                    for (int i = cellStart[cell]; i < end; i++)
                    {
                        var e = entries[i];
                        float r = radius + e.Radius;
                        if (math.distancesq(center, e.Position) < r * r && !visitor.Visit(e))
                            return false;
                    }
                }
            }
            return true;
        }

        /// <summary>Opt-in conservative cell pruning; same visits/order as Query. Benchmark before adoption.
        /// Existing callers retain Query and do not enable this experimental optimization.</summary>
        public void QueryPruned<TVisitor>(float2 center, float radius, ref TVisitor visitor) where TVisitor : struct, IGridVisitor
        {
            var metrics = new NoGridQueryMetrics();
            QueryCore(center, radius, ref visitor, true, ref metrics);
        }

        /// <summary>Separate diagnostic pass; counters are not required by the uninstrumented timing path.</summary>
        public void QueryMeasured<TVisitor>(float2 center, float radius, ref TVisitor visitor, ref GridQueryStats stats, bool pruneCells = false)
            where TVisitor : struct, IGridVisitor
        {
            var metrics = new CountGridQueryMetrics { Value = stats };
            QueryCore(center, radius, ref visitor, pruneCells, ref metrics);
            stats = metrics.Value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void QueryCore<TVisitor, TMetrics>(float2 center, float radius, ref TVisitor visitor, bool pruneCells, ref TMetrics metrics)
            where TVisitor : struct, IGridVisitor where TMetrics : struct, IGridQueryMetrics
        {
            if (!QueryLayer(m_Entries, m_CellStart, m_InvCellSize, m_Dimensions,
                    radius + math.asfloat(m_Stats[SpatialGrid.StatMaxRadiusBits]), center, radius, ref visitor, pruneCells, ref metrics))
                return;
            if (m_Stats[SpatialGrid.StatLargeCount] > 0)
                QueryLayer(m_LargeEntries, m_LargeCellStart, m_InvLargeCellSize, m_LargeDimensions,
                    radius + math.asfloat(m_Stats[SpatialGrid.StatLargeMaxRadiusBits]), center, radius, ref visitor, pruneCells, ref metrics);
        }

        bool QueryLayer<TVisitor, TMetrics>(NativeArray<GridEntry> entries, NativeArray<int> cellStart, float invCellSize, int2 dimensions,
            float reach, float2 center, float radius, ref TVisitor visitor, bool pruneCells, ref TMetrics metrics)
            where TVisitor : struct, IGridVisitor where TMetrics : struct, IGridQueryMetrics
        {
            int2 min = math.max((int2)math.floor((center - reach - m_Origin) * invCellSize), 0);
            min = ClampAcceptedLowerCell(min, center - reach, dimensions);
            int2 max = math.min((int2)math.floor((center + reach - m_Origin) * invCellSize), dimensions - 1);
            for (int y = min.y; y <= max.y; y++)
            {
                int row = y * dimensions.x;
                for (int x = min.x; x <= max.x; x++)
                {
                    int cell = row + x;
                    int start = cellStart[cell], end = cellStart[cell + 1];
                    // Empty cells need no geometry. Expand by several ulps of world coordinates so
                    // reciprocal/floor and FMA rounding cannot make a boundary cell disappear.
                    bool pruned = pruneCells && start < end && CellOutsideCircle(x, y, invCellSize, center, reach);
                    metrics.Cell(pruned);
                    if (pruned) continue;
                    for (int i = start; i < end; i++)
                    {
                        metrics.Entry();
                        var e = entries[i];
                        float r = radius + e.Radius;
                        if (math.distancesq(center, e.Position) < r * r)
                        {
                            metrics.Visit();
                            if (!visitor.Visit(e)) return false;
                        }
                    }
                }
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        int2 ClampAcceptedLowerCell(int2 cell, float2 point, int2 dimensions)
        {
            // Match the builder's upper-edge clamp only when the query bound is inside the
            // accepted window. A rectangle wholly beyond the window still has an empty range.
            return math.select(cell, math.min(cell, dimensions - 1), point < Max);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool CellOutsideCircle(int x, int y, float invCellSize, float2 center, float reach)
        {
            float cellSize = 1f / invCellSize;
            float2 offset = new float2(x, y) * cellSize;
            float2 lower = m_Origin + offset;
            float2 upper = lower + cellSize;
            // Include the large operands too: origin + offset may cancel near world zero.
            float2 scale = math.max(math.abs(m_Origin), math.abs(offset));
            scale = math.max(scale, math.max(math.abs(lower), math.max(math.abs(upper), math.abs(center))));
            float magnitude = math.max(1f, math.cmax(scale));
            float padding = magnitude * 0.000001f + math.abs(cellSize) * 0.000001f;
            float2 nearest = math.clamp(center, lower - padding, upper + padding);
            float conservativeReach = reach + padding;
            // NaN/overflow disables pruning rather than rejecting an entry.
            return math.distancesq(center, nearest) > conservativeReach * conservativeReach;
        }

        /// <summary>Visits every entry whose centre lies in the cells touched by the rectangle (no distance test).</summary>
        public void QueryCells<TVisitor>(float2 rectMin, float2 rectMax, ref TVisitor visitor) where TVisitor : struct, IGridVisitor
        {
            if (!CellsLayer(m_Entries, m_CellStart, m_InvCellSize, m_Dimensions, rectMin, rectMax, ref visitor))
                return;
            if (m_Stats[SpatialGrid.StatLargeCount] > 0)
                CellsLayer(m_LargeEntries, m_LargeCellStart, m_InvLargeCellSize, m_LargeDimensions, rectMin, rectMax, ref visitor);
        }

        bool CellsLayer<TVisitor>(NativeArray<GridEntry> entries, NativeArray<int> cellStart, float invCellSize, int2 dimensions,
            float2 rectMin, float2 rectMax, ref TVisitor visitor) where TVisitor : struct, IGridVisitor
        {
            int2 min = math.max((int2)math.floor((rectMin - m_Origin) * invCellSize), 0);
            min = ClampAcceptedLowerCell(min, rectMin, dimensions);
            int2 max = math.min((int2)math.floor((rectMax - m_Origin) * invCellSize), dimensions - 1);
            for (int y = min.y; y <= max.y; y++)
            {
                int row = y * dimensions.x;
                for (int x = min.x; x <= max.x; x++)
                {
                    int cell = row + x;
                    int end = cellStart[cell + 1];
                    for (int i = cellStart[cell]; i < end; i++)
                        if (!visitor.Visit(entries[i]))
                            return false;
                }
            }
            return true;
        }
    }
}
