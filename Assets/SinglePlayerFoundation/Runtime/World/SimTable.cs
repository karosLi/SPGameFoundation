using System;
using System.Collections.Generic;
using SPF.Contracts;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace SPF.Runtime.World
{
    /// <summary>
    /// Dense structure-of-arrays storage for one entity kind. Rows [0, Count) are live; removal is
    /// swap-back so rows stay contiguous for jobs. Capacity is fixed at creation (no growth in a session).
    /// Structural changes (Add / RemoveAtSwapBack / Clear) are main-thread only, while no jobs run.
    /// </summary>
    public sealed unsafe partial class SimTable : IDisposable
    {
        readonly Dictionary<int, IColumn> m_Columns = new Dictionary<int, IColumn>();
        readonly List<IColumn> m_ColumnList = new List<IColumn>();
        NativeArray<EntityHandle> m_Handles;

        ChangeLog m_Changes;
        readonly List<ChangeLog> m_Logs = new List<ChangeLog>();

        public TableKey Key { get; }
        public int TableIndex { get; }
        public int Capacity { get; }
        public int Count { get; private set; }

        /// <summary>
        /// Incremented by every structural change (add, remove / swap-back, clear) and by
        /// <see cref="MarkChanged"/>. Lets consumers skip rebuilding derived data (e.g. a spatial grid of
        /// static items) when nothing changed. In-place column writes must call MarkChanged to count.
        /// </summary>
        public uint Version { get; private set; }

        /// <summary>Entity handle of each row. Access is declared through the TableKey.</summary>
        public NativeArray<EntityHandle> Handles => m_Handles;

        Column<byte> m_Dead;

        internal SimTable(TableKey key, int tableIndex, int capacity, bool pooled = false)
        {
            Key = key;
            TableIndex = tableIndex;
            Capacity = capacity;
            m_Handles = new NativeArray<EntityHandle>(capacity, Allocator.Persistent);
            if (pooled)
            {
                // The dead flags are an ordinary (hidden) column: they move, reset and snapshot with the rows.
                try
                {
                    m_Dead = new Column<byte>(capacity);
                    m_ColumnList.Add(m_Dead);
                }
                catch (Exception failure)
                {
                    if (m_Dead != null) CleanupErrors.Try(m_Dead.Dispose, ref failure);
                    CleanupErrors.Try(() => m_Handles.Dispose(), ref failure);
                    throw;
                }
            }
        }

        /// <summary>
        /// Pooled tables hold handle-free rows (bullets, particles, pickups): no registry slot, created with
        /// <see cref="SimWorld.Spawn"/>, removed by setting <see cref="DeadFlags"/> (from jobs, declared as a
        /// write of the table key) and compacted in order at the start of the next tick.
        /// </summary>
        public bool IsPooled => m_Dead != null;

        /// <summary>Pooled tables: 1 = remove this row at the next compaction. Writable from parallel jobs (own row).</summary>
        public NativeArray<byte> DeadFlags => m_Dead != null ? m_Dead.Data : throw new InvalidOperationException($"Table {Key} is not pooled.");

        /// <summary>
        /// Rows created, removed or moved since the last <see cref="ChangeLog.Clear"/>, when change tracking
        /// is enabled for the table (used to update GPU mirrors incrementally). Null otherwise.
        /// </summary>
        public ChangeLog Changes => m_Changes;

        internal void EnableChangeTracking()
        {
            if (m_Changes == null)
                m_Changes = CreateChangeLog();
        }

        /// <summary>
        /// Adds an independent change log (each consumer clears its own). Used by derived structures that
        /// update incrementally, e.g. a spatial index of the table's rows.
        /// </summary>
        public ChangeLog CreateChangeLog()
        {
            var log = new ChangeLog(Capacity);
            m_Logs.Add(log);
            return log;
        }

        void Mark(int row)
        {
            for (int i = 0; i < m_Logs.Count; i++) m_Logs[i].Mark(row);
        }

        void MarkAll()
        {
            for (int i = 0; i < m_Logs.Count; i++) m_Logs[i].MarkAll();
        }

        /// <summary>Marks a row whose data changed outside of structural operations (main thread).</summary>
        public void MarkChanged(int row)
        {
            Version++;
            Mark(row);
        }

        internal void AddColumn<T>(ColumnKey<T> key) where T : unmanaged
        {
            if (key.Table != Key)
                throw new ArgumentException($"Column {key} belongs to table {key.Table}, not {Key}.");
            if (m_Columns.ContainsKey(key.Id))
                return;
            var column = new Column<T>(Capacity);
            try
            {
                m_Columns.Add(key.Id, column);
                m_ColumnList.Add(column);
            }
            catch (Exception failure)
            {
                m_Columns.Remove(key.Id);
                CleanupErrors.Try(column.Dispose, ref failure);
                throw;
            }
        }

        public bool HasColumn(AccessKey key) => m_Columns.ContainsKey(key.Id);

        internal AccessGuard Guard;

        /// <summary>
        /// Legacy writable full-capacity alias; Read OR Write declarations permit acquisition for
        /// compatibility. Cached arrays and unsafe pointers bypass permission checks. Prefer ReadColumn
        /// or WriteColumn in new code. Only [0, Count) is meaningful; no getter completes jobs.
        /// </summary>
        public NativeArray<T> Column<T>(ColumnKey<T> key) where T : unmanaged
        {
            Guard?.CheckColumn(key);
            return GetColumn(key);
        }

        /// <summary>Read-only full-capacity native view. Read or Write declaration is required in a declared system.</summary>
        public NativeArray<T>.ReadOnly ReadColumn<T>(ColumnKey<T> key) where T : unmanaged
        {
            Guard?.CheckColumn(key);
            return GetColumn(key).AsReadOnly();
        }

        /// <summary>Writable full-capacity native alias. Requires Write in a declared system; does not complete jobs.</summary>
        public NativeArray<T> WriteColumn<T>(ColumnKey<T> key) where T : unmanaged
        {
            Guard?.CheckWriteColumn(key);
            return GetColumn(key);
        }

        NativeArray<T> GetColumn<T>(ColumnKey<T> key) where T : unmanaged
        {
            if (!m_Columns.TryGetValue(key.Id, out var column))
                throw new ArgumentException($"Table {Key} has no column {key}. Declare it in the module's DeclareData.");
            return ((Column<T>)column).Data;
        }

        /// <summary>Appends a zero-initialised row. Returns -1 when the table is full.</summary>
        internal int Add(EntityHandle handle)
        {
            Guard?.CheckStructural("CreateEntity");
            if (Count >= Capacity)
                return -1;
            int row = Count++;
            Version++;
            m_Handles[row] = handle;
            Mark(row);
            for (int i = 0; i < m_ColumnList.Count; i++)
                m_ColumnList[i].Reset(row);
            return row;
        }

        /// <summary>
        /// Removes a row by moving the last row into it. Returns the handle of the moved entity,
        /// or Null when the removed row was the last one.
        /// </summary>
        internal EntityHandle RemoveAtSwapBack(int row)
        {
            Guard?.CheckStructural("DestroyEntity");
            int last = Count - 1;
            Count = last;
            Version++;
            Mark(row);
            if (row == last)
                return EntityHandle.Null;

            m_Handles[row] = m_Handles[last];
            for (int i = 0; i < m_ColumnList.Count; i++)
                m_ColumnList[i].Move(last, row);
            return m_Handles[row];
        }

        internal int AddPooled()
        {
            Guard?.CheckStructural("Spawn");
            if (Count >= Capacity)
                return -1;
            int row = Count++;
            Version++;
            m_Handles[row] = EntityHandle.Null;
            Mark(row);
            for (int i = 0; i < m_ColumnList.Count; i++)
                m_ColumnList[i].Reset(row);
            return row;
        }

        /// <summary>Pooled tables: appends up to <paramref name="count"/> zeroed rows; returns the first row (rows [start, Count)).</summary>
        internal int AddPooledRange(int count, out int added)
        {
            Guard?.CheckStructural("SpawnRange");
            int start = Count;
            added = Math.Max(0, Math.Min(count, Capacity - Count));
            if (added == 0) return start;
            for (int i = 0; i < m_ColumnList.Count; i++)
            {
                var col = m_ColumnList[i];
                UnsafeUtility.MemClear(col.Pointer + (long)start * col.ElementSize, (long)added * col.ElementSize);
            }
            UnsafeUtility.MemClear((byte*)m_Handles.GetUnsafePtr() + (long)start * sizeof(EntityHandle), (long)added * sizeof(EntityHandle));
            Count += added;
            Version++;
            if (m_Logs.Count > 0)
                for (int row = start; row < Count; row++) Mark(row);
            return start;
        }

        NativeArray<ColumnRef> ColumnRefs(Allocator allocator)
        {
            var refs = new NativeArray<ColumnRef>(m_ColumnList.Count + 1, allocator);
            for (int i = 0; i < m_ColumnList.Count; i++)
                refs[i] = new ColumnRef { Pointer = m_ColumnList[i].Pointer, Size = m_ColumnList[i].ElementSize };
            refs[m_ColumnList.Count] = new ColumnRef { Pointer = (byte*)m_Handles.GetUnsafePtr(), Size = sizeof(EntityHandle) };
            return refs;
        }

        /// <summary>Pooled tables: drops dead rows, keeping the survivors' order. Returns the rows removed.</summary>
        internal int Compact()
        {
            Guard?.CheckStructural("CompactPools");
            if (m_Dead == null || Count == 0) return 0;
            var refs = ColumnRefs(Allocator.TempJob);
            var result = new NativeArray<int>(1, Allocator.TempJob);
            new CompactJob { Dead = m_Dead.Pointer, Columns = refs, Count = Count, Result = result }.Run();
            int removed = Count - result[0];
            refs.Dispose();
            result.Dispose();
            if (removed > 0)
            {
                Count -= removed;
                Version++;
                MarkAll();
            }
            return removed;
        }

        /// <summary>Reorders rows [0, Count): new row i holds old row order[i]. Returns the handle of each new row.</summary>
        internal void Permute(NativeArray<int> order)
        {
            Guard?.CheckStructural("SortRows");
            int maxSize = sizeof(EntityHandle);
            for (int i = 0; i < m_ColumnList.Count; i++) maxSize = Math.Max(maxSize, m_ColumnList[i].ElementSize);
            var refs = ColumnRefs(Allocator.TempJob);
            var scratch = new NativeArray<byte>(maxSize * Math.Max(Count, 1), Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            new GatherJob { Columns = refs, Order = order, Scratch = (byte*)scratch.GetUnsafePtr() }.Run();
            refs.Dispose();
            scratch.Dispose();
            Version++;
            MarkAll();
        }

        internal void Clear()
        {
            Guard?.CheckStructural("Clear table");
            Count = 0;
            Version++;
            MarkAll();
        }

        internal void WriteSnapshot(System.IO.BinaryWriter writer)
        {
            writer.Write(Capacity);
            writer.Write(m_ColumnList.Count);
            NativeIO.Write(writer, m_Handles, Count);
            for (int i = 0; i < m_ColumnList.Count; i++)
                m_ColumnList[i].WriteSnapshot(writer, Count);
        }

        /// <summary>Restores rows [0, Count); every change log reports "everything changed".</summary>
        internal void ReadSnapshot(System.IO.BinaryReader reader)
        {
            Guard?.CheckStructural("ReadSnapshot");
            if (reader.ReadInt32() != Capacity || reader.ReadInt32() != m_ColumnList.Count)
                throw new System.IO.InvalidDataException($"Snapshot of table {Key} has a different capacity or column set.");
            Count = NativeIO.Read(reader, m_Handles);
            for (int i = 0; i < m_ColumnList.Count; i++)
                if (m_ColumnList[i].ReadSnapshot(reader) != Count)
                    throw new System.IO.InvalidDataException($"Snapshot of table {Key} has columns of different lengths.");
            Version++;
            MarkAll();
        }

        public void Dispose()
        {
            Guard?.CheckStructural("Dispose table");
            Exception failure = null;
            for (int i = 0; i < m_ColumnList.Count; i++)
                CleanupErrors.Try(m_ColumnList[i].Dispose, ref failure);
            m_ColumnList.Clear();
            m_Columns.Clear();
            if (m_Handles.IsCreated) CleanupErrors.Try(() => m_Handles.Dispose(), ref failure);
            CleanupErrors.ThrowIfAny(failure);
        }
    }

    /// <summary>Deduplicated list of changed rows. Rows ≥ the table's Count mean "removed".</summary>
    public sealed class ChangeLog
    {
        readonly int[] m_Rows;
        readonly bool[] m_Marked;

        internal ChangeLog(int capacity)
        {
            m_Rows = new int[capacity];
            m_Marked = new bool[capacity];
        }

        public int Count { get; private set; }

        /// <summary>True when everything must be treated as changed (reset / overflow).</summary>
        public bool All { get; private set; } = true;

        public int this[int index] => m_Rows[index];

        internal void Mark(int row)
        {
            if (All || m_Marked[row]) return;
            m_Marked[row] = true;
            m_Rows[Count++] = row;
        }

        internal void MarkAll() => All = true;

        public void Clear()
        {
            for (int i = 0; i < Count; i++)
                m_Marked[m_Rows[i]] = false;
            Count = 0;
            All = false;
        }
    }
}
