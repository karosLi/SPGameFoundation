using System;
using System.Collections.Generic;
using SPF.Contracts;
using Unity.Collections;

namespace SPF.Runtime.World
{
    /// <summary>
    /// Dense structure-of-arrays storage for one entity kind. Rows [0, Count) are live; removal is
    /// swap-back so rows stay contiguous for jobs. Capacity is fixed at creation (no growth in a session).
    /// Structural changes (Add / RemoveAtSwapBack / Clear) are main-thread only, while no jobs run.
    /// </summary>
    public sealed class SimTable : IDisposable
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

        internal SimTable(TableKey key, int tableIndex, int capacity)
        {
            Key = key;
            TableIndex = tableIndex;
            Capacity = capacity;
            m_Handles = new NativeArray<EntityHandle>(capacity, Allocator.Persistent);
        }

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
            m_Columns.Add(key.Id, column);
            m_ColumnList.Add(column);
        }

        public bool HasColumn(AccessKey key) => m_Columns.ContainsKey(key.Id);

        /// <summary>Full-capacity array of the column; only [0, Count) is meaningful.</summary>
        internal AccessGuard Guard;

        public NativeArray<T> Column<T>(ColumnKey<T> key) where T : unmanaged
        {
            Guard?.CheckColumn(key);
            if (!m_Columns.TryGetValue(key.Id, out var column))
                throw new ArgumentException($"Table {Key} has no column {key}. Declare it in the module's DeclareData.");
            return ((Column<T>)column).Data;
        }

        /// <summary>Appends a zero-initialised row. Returns -1 when the table is full.</summary>
        internal int Add(EntityHandle handle)
        {
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

        internal void Clear()
        {
            Count = 0;
            Version++;
            MarkAll();
        }

        public void Dispose()
        {
            for (int i = 0; i < m_ColumnList.Count; i++)
                m_ColumnList[i].Dispose();
            m_ColumnList.Clear();
            m_Columns.Clear();
            if (m_Handles.IsCreated) m_Handles.Dispose();
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
