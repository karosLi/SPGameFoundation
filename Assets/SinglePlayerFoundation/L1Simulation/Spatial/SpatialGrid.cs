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
    /// </summary>
    public sealed class SpatialGrid : IDisposable, IResettableResource
    {
        NativeArray<GridEntry> m_Staging;
        NativeArray<int> m_StagingCount;
        NativeArray<GridEntry> m_Entries;
        NativeArray<int> m_CellStart;
        NativeArray<int> m_Stats;

        public SpatialGrid(int2 dimensions, float cellSize, int capacity)
        {
            if (math.any(dimensions <= 0)) throw new ArgumentOutOfRangeException(nameof(dimensions));
            if (cellSize <= 0f) throw new ArgumentOutOfRangeException(nameof(cellSize));
            if (capacity > GridEntry.MaxOwner + 1) throw new ArgumentOutOfRangeException(nameof(capacity), "entry owners are 16-bit");
            Dimensions = dimensions;
            CellSize = cellSize;
            int cells = dimensions.x * dimensions.y;
            m_Staging = new NativeArray<GridEntry>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            m_StagingCount = new NativeArray<int>(1, Allocator.Persistent);
            m_Entries = new NativeArray<GridEntry>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            m_CellStart = new NativeArray<int>(cells + 1, Allocator.Persistent);
            m_Stats = new NativeArray<int>(StatCount, Allocator.Persistent);
        }

        const int StatEntryCount = 0;
        const int StatDropped = 1;
        const int StatMaxRadiusBits = 2;
        const int StatCount = 3;

        public int2 Dimensions { get; }
        public float CellSize { get; }
        public int Capacity => m_Staging.Length;

        /// <summary>World position of the grid's minimum corner. Set on the main thread before producers run.</summary>
        public float2 Origin { get; set; }

        public float2 Size => (float2)Dimensions * CellSize;

        /// <summary>Origin used by the last scheduled build; readers use it so a moved window never mismatches built data.</summary>
        public float2 BuiltOrigin { get; private set; }

        public NativeArray<GridEntry> Staging => m_Staging;
        public NativeArray<int> StagingCount => m_StagingCount;

        /// <summary>Valid after the build job completed.</summary>
        public int EntryCount => m_Stats[StatEntryCount];
        public int DroppedLastBuild => m_Stats[StatDropped];

        /// <summary>
        /// Recentres the window on a focus point with hysteresis: the origin only moves (in whole cells)
        /// when the focus leaves the central half of the window. Returns true when it moved.
        /// </summary>
        public bool Follow(float2 focus, float2 regionMin, float2 regionMax)
        {
            float2 size = Size;
            float2 local = focus - Origin;
            if (math.all(local >= size * 0.25f) && math.all(local <= size * 0.75f))
                return false;
            float2 origin = math.floor((focus - size * 0.5f) / CellSize) * CellSize;
            float2 maxOrigin = math.max(regionMax - size, regionMin);
            origin = math.clamp(origin, math.floor(regionMin / CellSize) * CellSize, math.ceil(maxOrigin / CellSize) * CellSize);
            bool moved = math.any(origin != Origin);
            Origin = origin;
            return moved;
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
                Stats = m_Stats,
                Origin = Origin,
                InvCellSize = 1f / CellSize,
                Dimensions = Dimensions,
            }.Schedule(dependency);
        }

        public GridReader AsReader() => new GridReader(m_Entries, m_CellStart, m_Stats, BuiltOrigin, CellSize, Dimensions);

        public void OnReset()
        {
            m_StagingCount[0] = 0;
            for (int i = 0; i < StatCount; i++) m_Stats[i] = 0;
            for (int i = 0; i < m_CellStart.Length; i++) m_CellStart[i] = 0;
        }

        public void Dispose()
        {
            if (m_Staging.IsCreated) m_Staging.Dispose();
            if (m_StagingCount.IsCreated) m_StagingCount.Dispose();
            if (m_Entries.IsCreated) m_Entries.Dispose();
            if (m_CellStart.IsCreated) m_CellStart.Dispose();
            if (m_Stats.IsCreated) m_Stats.Dispose();
        }

        /// <summary>
        /// Counting sort by cell: count, inclusive prefix sum, then a reverse scatter that decrements each
        /// cell's end cursor. Iterating entries backwards keeps the order within a cell stable (staging
        /// order), and the cursors end up exactly at each cell's start, so no restore pass or per-entry
        /// cell cache is needed: two passes over the cells and two over the entries.
        /// </summary>
        [BurstCompile(CompileSynchronously = true)]
        struct BuildJob : IJob
        {
            [ReadOnly] public NativeArray<GridEntry> Staging;
            public NativeArray<int> StagingCount;
            public NativeArray<GridEntry> Entries;
            public NativeArray<int> CellStart;
            public NativeArray<int> Stats;
            public float2 Origin;
            public float InvCellSize;
            public int2 Dimensions;

            public void Execute()
            {
                int count = math.min(StagingCount[0], Staging.Length);
                int cells = Dimensions.x * Dimensions.y;
                for (int c = 0; c < cells; c++)
                    CellStart[c] = 0;

                // Pass 1: per-cell counts.
                int dropped = 0;
                float maxRadius = 0f;
                for (int i = 0; i < count; i++)
                {
                    var e = Staging[i];
                    int index = CellIndex(e.Position);
                    if (index < 0)
                    {
                        dropped++;
                        continue;
                    }
                    CellStart[index]++;
                    maxRadius = math.max(maxRadius, e.Radius);
                }

                // Pass 2: inclusive prefix sum -> CellStart[c] = end of cell c.
                int running = 0;
                for (int c = 0; c < cells; c++)
                {
                    running += CellStart[c];
                    CellStart[c] = running;
                }
                CellStart[cells] = running;

                // Pass 3: reverse stable scatter; afterwards CellStart[c] = start of cell c.
                for (int i = count - 1; i >= 0; i--)
                {
                    var e = Staging[i];
                    int index = CellIndex(e.Position);
                    if (index < 0) continue;
                    Entries[--CellStart[index]] = e;
                }

                Stats[StatEntryCount] = count - dropped;
                Stats[StatDropped] = dropped;
                Stats[StatMaxRadiusBits] = math.asint(maxRadius);
                StagingCount[0] = 0;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            int CellIndex(float2 position)
            {
                int2 cell = (int2)math.floor((position - Origin) * InvCellSize);
                if (math.any(cell < 0) || math.any(cell >= Dimensions))
                    return -1;
                return cell.y * Dimensions.x + cell.x;
            }
        }
    }

    /// <summary>Read-only view of a built grid for use in jobs.</summary>
    public struct GridReader
    {
        [ReadOnly] NativeArray<GridEntry> m_Entries;
        [ReadOnly] NativeArray<int> m_CellStart;
        [ReadOnly] NativeArray<int> m_Stats;
        float2 m_Origin;
        float m_CellSize;
        float m_InvCellSize;
        int2 m_Dimensions;

        internal GridReader(NativeArray<GridEntry> entries, NativeArray<int> cellStart, NativeArray<int> stats,
            float2 origin, float cellSize, int2 dimensions)
        {
            m_Entries = entries;
            m_CellStart = cellStart;
            m_Stats = stats;
            m_Origin = origin;
            m_CellSize = cellSize;
            m_InvCellSize = 1f / cellSize;
            m_Dimensions = dimensions;
        }

        public float2 Origin => m_Origin;
        public float2 Max => m_Origin + (float2)m_Dimensions * m_CellSize;
        public float MaxEntryRadius => math.asfloat(m_Stats[2]);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Covers(float2 position) => math.all(position >= m_Origin) && math.all(position < Max);

        /// <summary>Visits every entry whose circle overlaps the query circle.</summary>
        public void Query<TVisitor>(float2 center, float radius, ref TVisitor visitor) where TVisitor : struct, IGridVisitor
        {
            float reach = radius + MaxEntryRadius;
            int2 min = math.max((int2)math.floor((center - reach - m_Origin) * m_InvCellSize), 0);
            int2 max = math.min((int2)math.floor((center + reach - m_Origin) * m_InvCellSize), m_Dimensions - 1);
            for (int y = min.y; y <= max.y; y++)
            {
                int row = y * m_Dimensions.x;
                for (int x = min.x; x <= max.x; x++)
                {
                    int cell = row + x;
                    int end = m_CellStart[cell + 1];
                    for (int i = m_CellStart[cell]; i < end; i++)
                    {
                        var e = m_Entries[i];
                        float r = radius + e.Radius;
                        if (math.distancesq(center, e.Position) < r * r && !visitor.Visit(e))
                            return;
                    }
                }
            }
        }

        /// <summary>Visits every entry whose centre lies in the cells touched by the rectangle (no distance test).</summary>
        public void QueryCells<TVisitor>(float2 rectMin, float2 rectMax, ref TVisitor visitor) where TVisitor : struct, IGridVisitor
        {
            int2 min = math.max((int2)math.floor((rectMin - m_Origin) * m_InvCellSize), 0);
            int2 max = math.min((int2)math.floor((rectMax - m_Origin) * m_InvCellSize), m_Dimensions - 1);
            for (int y = min.y; y <= max.y; y++)
            {
                int row = y * m_Dimensions.x;
                for (int x = min.x; x <= max.x; x++)
                {
                    int cell = row + x;
                    int end = m_CellStart[cell + 1];
                    for (int i = m_CellStart[cell]; i < end; i++)
                        if (!visitor.Visit(m_Entries[i]))
                            return;
                }
            }
        }
    }
}
