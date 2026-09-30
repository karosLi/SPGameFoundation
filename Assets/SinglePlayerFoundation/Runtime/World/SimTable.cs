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

        public TableKey Key { get; }
        public int TableIndex { get; }
        public int Capacity { get; }
        public int Count { get; private set; }

        /// <summary>Entity handle of each row. Access is declared through the TableKey.</summary>
        public NativeArray<EntityHandle> Handles => m_Handles;

        internal SimTable(TableKey key, int tableIndex, int capacity)
        {
            Key = key;
            TableIndex = tableIndex;
            Capacity = capacity;
            m_Handles = new NativeArray<EntityHandle>(capacity, Allocator.Persistent);
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
        public NativeArray<T> Column<T>(ColumnKey<T> key) where T : unmanaged
        {
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
            m_Handles[row] = handle;
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
            if (row == last)
                return EntityHandle.Null;

            m_Handles[row] = m_Handles[last];
            for (int i = 0; i < m_ColumnList.Count; i++)
                m_ColumnList[i].Move(last, row);
            return m_Handles[row];
        }

        internal void Clear() => Count = 0;

        public void Dispose()
        {
            for (int i = 0; i < m_ColumnList.Count; i++)
                m_ColumnList[i].Dispose();
            m_ColumnList.Clear();
            m_Columns.Clear();
            if (m_Handles.IsCreated) m_Handles.Dispose();
        }
    }
}
