using System;
using System.Runtime.CompilerServices;
using SPF.Contracts;
using Unity.Collections;
using Unity.Mathematics;

namespace SPF.L1.Spatial
{
    /// <summary>
    /// Uniform grid over a window that is updated incrementally, for many mostly static items (food) where
    /// only a few rows change per tick; <see cref="SpatialGrid"/> (counting sort, rebuilt every tick) suits
    /// moving data. Every entry is addressed by a caller-chosen key (e.g. table row): set / remove are O(1).
    /// <para>
    /// Storage is an unrolled list per cell: a chain of blocks of <see cref="BlockSize"/> entries
    /// (16 B each, so one block is one 64-byte cache line). All blocks of a cell are full except its last,
    /// so a query reads each cell as a few contiguous runs instead of chasing one pointer per entry.
    /// Removing an entry moves the cell's last entry into the hole (order within a cell changes, but only
    /// as a function of the operations, so it stays deterministic). Moving the window requires a full
    /// rebuild (<see cref="Writer.Clear"/> then re-insert). Queries match <see cref="GridReader"/>.
    /// </para>
    /// </summary>
    public sealed class CellListGrid : IDisposable, IResettableResource, IJobData, ISnapshotResource
    {
        public const int BlockSize = 4;
        const int BlockShift = 2, LaneMask = BlockSize - 1;
        internal const int StatMaxRadiusBits = 0, StatCount = 1, StatBlockTop = 2, StatFreeBlocks = 3, StatSlots = 4;

        NativeArray<int> m_CellHead, m_CellTail, m_CellCount;
        NativeArray<GridEntry> m_Slots;
        NativeArray<int> m_SlotKey;
        NativeArray<int> m_BlockNext, m_BlockPrev;
        NativeArray<int> m_KeySlot;
        NativeArray<int> m_FreeBlocks;
        NativeArray<int> m_Stats;

        /// <param name="capacity">Maximum entries.</param>
        /// <param name="keyCapacity">Keys are in [0, keyCapacity).</param>
        public CellListGrid(int2 dimensions, float cellSize, int capacity, int keyCapacity)
        {
            if (math.any(dimensions <= 0)) throw new ArgumentOutOfRangeException(nameof(dimensions));
            if (cellSize <= 0f) throw new ArgumentOutOfRangeException(nameof(cellSize));
            if (capacity > GridEntry.MaxOwner + 1) throw new ArgumentOutOfRangeException(nameof(capacity), "entry owners are 16-bit");
            Dimensions = dimensions;
            CellSize = cellSize;
            Capacity = capacity;
            int cells = dimensions.x * dimensions.y;
            // Worst case: every occupied cell has a partial block, plus full blocks for the rest.
            int blocks = math.min(capacity, cells) + (capacity + BlockSize - 1) / BlockSize;
            m_CellHead = new NativeArray<int>(cells, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            m_CellTail = new NativeArray<int>(cells, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            m_CellCount = new NativeArray<int>(cells, Allocator.Persistent);
            // The legacy snapshot writes these complete buffers, including unused tails. Initialize
            // them once so fresh worlds never persist allocator contents. Clear() stays proportional
            // to cells + keys; the snapshot layout and preservation of restored bytes are unchanged.
            m_Slots = new NativeArray<GridEntry>(blocks * BlockSize, Allocator.Persistent);
            m_SlotKey = new NativeArray<int>(blocks * BlockSize, Allocator.Persistent);
            m_BlockNext = new NativeArray<int>(blocks, Allocator.Persistent);
            m_BlockPrev = new NativeArray<int>(blocks, Allocator.Persistent);
            m_KeySlot = new NativeArray<int>(keyCapacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            m_FreeBlocks = new NativeArray<int>(blocks, Allocator.Persistent);
            m_Stats = new NativeArray<int>(StatSlots, Allocator.Persistent);
            AsWriter().Clear();
        }

        public int2 Dimensions { get; }
        public float CellSize { get; }
        public int Capacity { get; }
        public int KeyCapacity => m_KeySlot.Length;
        public float2 Size => (float2)Dimensions * CellSize;

        /// <summary>
        /// Window origin used by the next update. Changing it invalidates the contents: the owner clears
        /// and re-inserts everything, then calls <see cref="MarkBuilt"/>.
        /// </summary>
        public float2 Origin { get; set; }

        /// <summary>
        /// Origin the current contents were built for. Readers use it, so jobs that run between a window
        /// move and the next update (e.g. AI on last tick's grid) still find what is stored.
        /// </summary>
        public float2 BuiltOrigin { get; private set; }

        /// <summary>Records that the contents now match <see cref="Origin"/> (call when scheduling the update).</summary>
        public void MarkBuilt() => BuiltOrigin = Origin;

        /// <summary>Same re-centring rule as <see cref="SpatialGrid.Follow"/>. Returns true when the window moved.</summary>
        public bool Follow(float2 focus, float2 regionMin, float2 regionMax)
        {
            var origin = SpatialGrid.FollowOrigin(Origin, Size, CellSize, focus, regionMin, regionMax);
            bool moved = math.any(origin != Origin);
            Origin = origin;
            return moved;
        }

        /// <summary>Entries currently stored (valid when no job is writing).</summary>
        public int Count => m_Stats[StatCount];

        public Writer AsWriter() => new Writer
        {
            CellHead = m_CellHead, CellTail = m_CellTail, CellCount = m_CellCount,
            Slots = m_Slots, SlotKey = m_SlotKey, BlockNext = m_BlockNext, BlockPrev = m_BlockPrev,
            KeySlot = m_KeySlot, FreeBlocks = m_FreeBlocks, Stats = m_Stats,
            Origin = Origin, InvCellSize = 1f / CellSize, Dimensions = Dimensions, Capacity = Capacity,
        };

        public CellListReader AsReader() => new CellListReader(m_CellHead, m_CellCount, m_Slots, m_BlockNext, m_Stats, BuiltOrigin, CellSize, Dimensions);

        public void OnReset() => AsWriter().Clear();

        /// <summary>Saves the whole structure: block layout and free list decide future insertion order.</summary>
        public void WriteSnapshot(System.IO.BinaryWriter writer)
        {
            writer.Write(Origin.x); writer.Write(Origin.y);
            writer.Write(BuiltOrigin.x); writer.Write(BuiltOrigin.y);
            NativeIO.Write(writer, m_CellHead);
            NativeIO.Write(writer, m_CellTail);
            NativeIO.Write(writer, m_CellCount);
            NativeIO.Write(writer, m_Slots);
            NativeIO.Write(writer, m_SlotKey);
            NativeIO.Write(writer, m_BlockNext);
            NativeIO.Write(writer, m_BlockPrev);
            NativeIO.Write(writer, m_KeySlot);
            NativeIO.Write(writer, m_FreeBlocks);
            NativeIO.Write(writer, m_Stats);
        }

        public void ReadSnapshot(System.IO.BinaryReader reader)
        {
            Origin = new float2(reader.ReadSingle(), reader.ReadSingle());
            BuiltOrigin = new float2(reader.ReadSingle(), reader.ReadSingle());
            NativeIO.ReadAll(reader, m_CellHead);
            NativeIO.ReadAll(reader, m_CellTail);
            NativeIO.ReadAll(reader, m_CellCount);
            NativeIO.ReadAll(reader, m_Slots);
            NativeIO.ReadAll(reader, m_SlotKey);
            NativeIO.ReadAll(reader, m_BlockNext);
            NativeIO.ReadAll(reader, m_BlockPrev);
            NativeIO.ReadAll(reader, m_KeySlot);
            NativeIO.ReadAll(reader, m_FreeBlocks);
            NativeIO.ReadAll(reader, m_Stats);
        }

        public void Dispose()
        {
            if (m_CellHead.IsCreated) m_CellHead.Dispose();
            if (m_CellTail.IsCreated) m_CellTail.Dispose();
            if (m_CellCount.IsCreated) m_CellCount.Dispose();
            if (m_Slots.IsCreated) m_Slots.Dispose();
            if (m_SlotKey.IsCreated) m_SlotKey.Dispose();
            if (m_BlockNext.IsCreated) m_BlockNext.Dispose();
            if (m_BlockPrev.IsCreated) m_BlockPrev.Dispose();
            if (m_KeySlot.IsCreated) m_KeySlot.Dispose();
            if (m_FreeBlocks.IsCreated) m_FreeBlocks.Dispose();
            if (m_Stats.IsCreated) m_Stats.Dispose();
        }

        /// <summary>Mutating view for one job (single writer).</summary>
        public struct Writer
        {
            public NativeArray<int> CellHead, CellTail, CellCount;
            public NativeArray<GridEntry> Slots;
            public NativeArray<int> SlotKey;
            public NativeArray<int> BlockNext, BlockPrev;
            public NativeArray<int> KeySlot;
            public NativeArray<int> FreeBlocks;
            public NativeArray<int> Stats;
            public float2 Origin;
            public float InvCellSize;
            public int2 Dimensions;
            public int Capacity;

            /// <summary>Removes everything (window moved, reset).</summary>
            public void Clear()
            {
                for (int c = 0; c < CellHead.Length; c++)
                {
                    CellHead[c] = -1;
                    CellTail[c] = -1;
                    CellCount[c] = 0;
                }
                for (int k = 0; k < KeySlot.Length; k++) KeySlot[k] = -1;
                Stats[StatMaxRadiusBits] = 0;
                Stats[StatCount] = 0;
                Stats[StatBlockTop] = 0;
                Stats[StatFreeBlocks] = 0;
            }

            /// <summary>Removes the entry stored under <paramref name="key"/>, if any.</summary>
            public void Remove(int key)
            {
                int slot = KeySlot[key];
                if (slot < 0) return;
                KeySlot[key] = -1;
                int cell = CellOf(Slots[slot].Position);
                int count = CellCount[cell] - 1;
                int tail = CellTail[cell];
                int last = (tail << BlockShift) + (count & LaneMask);
                if (last != slot)
                {
                    // Fill the hole with the cell's last entry so every block but the tail stays full.
                    Slots[slot] = Slots[last];
                    int movedKey = SlotKey[last];
                    SlotKey[slot] = movedKey;
                    KeySlot[movedKey] = slot;
                }
                CellCount[cell] = count;
                if ((count & LaneMask) == 0)
                {
                    // The tail block became empty: unlink and free it.
                    int prev = BlockPrev[tail];
                    CellTail[cell] = prev;
                    if (prev >= 0) BlockNext[prev] = -1; else CellHead[cell] = -1;
                    int free = Stats[StatFreeBlocks];
                    FreeBlocks[free] = tail;
                    Stats[StatFreeBlocks] = free + 1;
                }
                Stats[StatCount] = Stats[StatCount] - 1;
            }

            /// <summary>
            /// Stores <paramref name="entry"/> under <paramref name="key"/> (replacing an existing one).
            /// Entries outside the window are not stored; returns false for them or when full.
            /// </summary>
            public bool Set(int key, in GridEntry entry)
            {
                Remove(key);
                int cell = CellOf(entry.Position);
                if (cell < 0 || Stats[StatCount] >= Capacity)
                    return false;
                int count = CellCount[cell];
                int lane = count & LaneMask;
                int tail = CellTail[cell];
                if (lane == 0)
                {
                    // Tail full (or no block yet): link a new block.
                    int block;
                    int free = Stats[StatFreeBlocks];
                    if (free > 0)
                    {
                        block = FreeBlocks[free - 1];
                        Stats[StatFreeBlocks] = free - 1;
                    }
                    else
                    {
                        block = Stats[StatBlockTop];
                        if (block >= BlockNext.Length) return false;
                        Stats[StatBlockTop] = block + 1;
                    }
                    BlockNext[block] = -1;
                    BlockPrev[block] = tail;
                    if (tail >= 0) BlockNext[tail] = block; else CellHead[cell] = block;
                    CellTail[cell] = block;
                    tail = block;
                }
                int slot = (tail << BlockShift) + lane;
                Slots[slot] = entry;
                SlotKey[slot] = key;
                KeySlot[key] = slot;
                CellCount[cell] = count + 1;
                Stats[StatCount] = Stats[StatCount] + 1;
                // Grows only; reset on Clear. A stale (too large) maximum only widens queries.
                if (entry.Radius > math.asfloat(Stats[StatMaxRadiusBits]))
                    Stats[StatMaxRadiusBits] = math.asint(entry.Radius);
                return true;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            int CellOf(float2 position)
            {
                int2 cell = (int2)math.floor((position - Origin) * InvCellSize);
                if (math.any(cell < 0) || math.any(cell >= Dimensions))
                    return -1;
                return cell.y * Dimensions.x + cell.x;
            }
        }
    }

    /// <summary>Read-only view of a <see cref="CellListGrid"/> for jobs; same queries as <see cref="GridReader"/>.</summary>
    public struct CellListReader
    {
        [ReadOnly] NativeArray<int> m_CellHead;
        [ReadOnly] NativeArray<int> m_CellCount;
        [ReadOnly] NativeArray<GridEntry> m_Slots;
        [ReadOnly] NativeArray<int> m_BlockNext;
        [ReadOnly] NativeArray<int> m_Stats;
        float2 m_Origin;
        float m_CellSize;
        float m_InvCellSize;
        int2 m_Dimensions;

        internal CellListReader(NativeArray<int> cellHead, NativeArray<int> cellCount, NativeArray<GridEntry> slots, NativeArray<int> blockNext,
            NativeArray<int> stats, float2 origin, float cellSize, int2 dimensions)
        {
            m_CellHead = cellHead;
            m_CellCount = cellCount;
            m_Slots = slots;
            m_BlockNext = blockNext;
            m_Stats = stats;
            m_Origin = origin;
            m_CellSize = cellSize;
            m_InvCellSize = 1f / cellSize;
            m_Dimensions = dimensions;
        }

        public float2 Origin => m_Origin;
        public float2 Max => m_Origin + (float2)m_Dimensions * m_CellSize;
        public float MaxEntryRadius => math.asfloat(m_Stats[CellListGrid.StatMaxRadiusBits]);

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
                    int remaining = m_CellCount[cell];
                    for (int block = m_CellHead[cell]; remaining > 0; block = m_BlockNext[block])
                    {
                        int start = block * CellListGrid.BlockSize;
                        int end = start + math.min(remaining, CellListGrid.BlockSize);
                        remaining -= CellListGrid.BlockSize;
                        for (int i = start; i < end; i++)
                        {
                            var e = m_Slots[i];
                            float r = radius + e.Radius;
                            if (math.distancesq(center, e.Position) < r * r && !visitor.Visit(e))
                                return;
                        }
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
                {
                    int cell = row + x;
                    int remaining = m_CellCount[cell];
                    for (int block = m_CellHead[cell]; remaining > 0; block = m_BlockNext[block])
                    {
                        int start = block * CellListGrid.BlockSize;
                        int end = start + math.min(remaining, CellListGrid.BlockSize);
                        remaining -= CellListGrid.BlockSize;
                        for (int i = start; i < end; i++)
                            if (!visitor.Visit(m_Slots[i]))
                                return;
                    }
                }
            }
        }
    }
}
