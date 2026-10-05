using System;
using System.Runtime.CompilerServices;
using SPF.Contracts;
using Unity.Collections;
using Unity.Mathematics;

namespace SPF.L1.Spatial
{
    /// <summary>
    /// Uniform grid over a window that is updated incrementally: every cell holds a doubly linked list of
    /// nodes and every entry is addressed by a caller-chosen key (e.g. table row), so an entry can be
    /// removed or re-inserted in O(1). Meant for many mostly static items (food) where only a few rows
    /// change per tick; <see cref="SpatialGrid"/> (counting sort, rebuilt every tick) suits moving data.
    /// List order within a cell depends only on the order of operations, so it is deterministic when the
    /// operations are. Queries match <see cref="GridReader"/> semantics. Moving the window requires a full
    /// rebuild (<see cref="Writer.Clear"/> then re-insert).
    /// </summary>
    public sealed class CellListGrid : IDisposable, IResettableResource
    {
        const int StatMaxRadiusBits = 0, StatCount = 1, StatNodeTop = 2, StatFreeCount = 3, StatSlots = 4;

        NativeArray<int> m_CellHead;
        NativeArray<GridEntry> m_Entries;
        NativeArray<int> m_Next, m_Prev, m_Cell;
        NativeArray<int> m_KeyNode;
        NativeArray<int> m_Free;
        NativeArray<int> m_Stats;

        public CellListGrid(int2 dimensions, float cellSize, int capacity, int keyCapacity)
        {
            if (math.any(dimensions <= 0)) throw new ArgumentOutOfRangeException(nameof(dimensions));
            if (cellSize <= 0f) throw new ArgumentOutOfRangeException(nameof(cellSize));
            if (capacity > GridEntry.MaxOwner + 1) throw new ArgumentOutOfRangeException(nameof(capacity), "entry owners are 16-bit");
            Dimensions = dimensions;
            CellSize = cellSize;
            m_CellHead = new NativeArray<int>(dimensions.x * dimensions.y, Allocator.Persistent);
            m_Entries = new NativeArray<GridEntry>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            m_Next = new NativeArray<int>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            m_Prev = new NativeArray<int>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            m_Cell = new NativeArray<int>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            m_KeyNode = new NativeArray<int>(keyCapacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            m_Free = new NativeArray<int>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            m_Stats = new NativeArray<int>(StatSlots, Allocator.Persistent);
            AsWriter().Clear();
        }

        public int2 Dimensions { get; }
        public float CellSize { get; }
        public int Capacity => m_Entries.Length;
        public int KeyCapacity => m_KeyNode.Length;
        public float2 Size => (float2)Dimensions * CellSize;

        /// <summary>Window origin. Changing it invalidates the contents: clear and re-insert everything.</summary>
        public float2 Origin { get; set; }

        /// <summary>Entries currently stored (valid when no job is writing).</summary>
        public int Count => m_Stats[StatCount];

        public Writer AsWriter() => new Writer
        {
            CellHead = m_CellHead, Entries = m_Entries, Next = m_Next, Prev = m_Prev, Cell = m_Cell,
            KeyNode = m_KeyNode, Free = m_Free, Stats = m_Stats,
            Origin = Origin, InvCellSize = 1f / CellSize, Dimensions = Dimensions,
        };

        public CellListReader AsReader() => new CellListReader(m_CellHead, m_Entries, m_Next, m_Stats, Origin, CellSize, Dimensions);

        public void OnReset() => AsWriter().Clear();

        public void Dispose()
        {
            if (m_CellHead.IsCreated) m_CellHead.Dispose();
            if (m_Entries.IsCreated) m_Entries.Dispose();
            if (m_Next.IsCreated) m_Next.Dispose();
            if (m_Prev.IsCreated) m_Prev.Dispose();
            if (m_Cell.IsCreated) m_Cell.Dispose();
            if (m_KeyNode.IsCreated) m_KeyNode.Dispose();
            if (m_Free.IsCreated) m_Free.Dispose();
            if (m_Stats.IsCreated) m_Stats.Dispose();
        }

        /// <summary>Mutating view for one job (single writer).</summary>
        public struct Writer
        {
            public NativeArray<int> CellHead;
            public NativeArray<GridEntry> Entries;
            public NativeArray<int> Next, Prev, Cell;
            public NativeArray<int> KeyNode;
            public NativeArray<int> Free;
            public NativeArray<int> Stats;
            public float2 Origin;
            public float InvCellSize;
            public int2 Dimensions;

            /// <summary>Removes everything (window moved, reset).</summary>
            public void Clear()
            {
                for (int c = 0; c < CellHead.Length; c++) CellHead[c] = -1;
                for (int k = 0; k < KeyNode.Length; k++) KeyNode[k] = -1;
                Stats[StatMaxRadiusBits] = 0;
                Stats[StatCount] = 0;
                Stats[StatNodeTop] = 0;
                Stats[StatFreeCount] = 0;
            }

            /// <summary>Removes the entry stored under <paramref name="key"/>, if any.</summary>
            public void Remove(int key)
            {
                int node = KeyNode[key];
                if (node < 0) return;
                KeyNode[key] = -1;
                int prev = Prev[node], next = Next[node];
                if (prev >= 0) Next[prev] = next; else CellHead[Cell[node]] = next;
                if (next >= 0) Prev[next] = prev;
                int free = Stats[StatFreeCount];
                Free[free] = node;
                Stats[StatFreeCount] = free + 1;
                Stats[StatCount] = Stats[StatCount] - 1;
            }

            /// <summary>
            /// Stores <paramref name="entry"/> under <paramref name="key"/> (replacing an existing one).
            /// Entries outside the window are not stored; returns false for them or when full.
            /// </summary>
            public bool Set(int key, in GridEntry entry)
            {
                Remove(key);
                int2 cell = (int2)math.floor((entry.Position - Origin) * InvCellSize);
                if (math.any(cell < 0) || math.any(cell >= Dimensions))
                    return false;
                int node;
                int free = Stats[StatFreeCount];
                if (free > 0)
                {
                    node = Free[free - 1];
                    Stats[StatFreeCount] = free - 1;
                }
                else
                {
                    node = Stats[StatNodeTop];
                    if (node >= Entries.Length) return false;
                    Stats[StatNodeTop] = node + 1;
                }
                int index = cell.y * Dimensions.x + cell.x;
                int head = CellHead[index];
                Entries[node] = entry;
                Cell[node] = index;
                Prev[node] = -1;
                Next[node] = head;
                if (head >= 0) Prev[head] = node;
                CellHead[index] = node;
                KeyNode[key] = node;
                Stats[StatCount] = Stats[StatCount] + 1;
                // Grows only; reset on Clear. A stale (too large) maximum only widens queries.
                if (entry.Radius > math.asfloat(Stats[StatMaxRadiusBits]))
                    Stats[StatMaxRadiusBits] = math.asint(entry.Radius);
                return true;
            }
        }
    }

    /// <summary>Read-only view of a <see cref="CellListGrid"/> for jobs; same queries as <see cref="GridReader"/>.</summary>
    public struct CellListReader
    {
        [ReadOnly] NativeArray<int> m_CellHead;
        [ReadOnly] NativeArray<GridEntry> m_Entries;
        [ReadOnly] NativeArray<int> m_Next;
        [ReadOnly] NativeArray<int> m_Stats;
        float2 m_Origin;
        float m_CellSize;
        float m_InvCellSize;
        int2 m_Dimensions;

        internal CellListReader(NativeArray<int> cellHead, NativeArray<GridEntry> entries, NativeArray<int> next, NativeArray<int> stats,
            float2 origin, float cellSize, int2 dimensions)
        {
            m_CellHead = cellHead;
            m_Entries = entries;
            m_Next = next;
            m_Stats = stats;
            m_Origin = origin;
            m_CellSize = cellSize;
            m_InvCellSize = 1f / cellSize;
            m_Dimensions = dimensions;
        }

        public float2 Origin => m_Origin;
        public float2 Max => m_Origin + (float2)m_Dimensions * m_CellSize;
        public float MaxEntryRadius => math.asfloat(m_Stats[0]);

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
                    for (int node = m_CellHead[row + x]; node >= 0; node = m_Next[node])
                    {
                        var e = m_Entries[node];
                        float r = radius + e.Radius;
                        if (math.distancesq(center, e.Position) < r * r && !visitor.Visit(e))
                            return;
                    }
                }
            }
        }

        /// <summary>Visits every entry stored in the cells touched by the rectangle (no distance test).</summary>
        public void QueryCells<TVisitor>(float2 rectMin, float2 rectMax, ref TVisitor visitor) where TVisitor : struct, IGridVisitor
        {
            int2 min = math.max((int2)math.floor((rectMin - m_Origin) * m_InvCellSize), 0);
            int2 max = math.min((int2)math.floor((rectMax - m_Origin) * m_InvCellSize), m_Dimensions - 1);
            for (int y = min.y; y <= max.y; y++)
            {
                int row = y * m_Dimensions.x;
                for (int x = min.x; x <= max.x; x++)
                    for (int node = m_CellHead[row + x]; node >= 0; node = m_Next[node])
                        if (!visitor.Visit(m_Entries[node]))
                            return;
            }
        }
    }
}
