using System;
using SPF.Contracts;
using Unity.Collections;

namespace SPF.Runtime.World
{
    /// <summary>
    /// Maps entity handles to (table, row). Slots are recycled through a free list; the generation
    /// is bumped on release so stale handles fail to resolve. Main thread only; jobs receive a
    /// read-only <see cref="EntityLookup"/>.
    /// </summary>
    public sealed class EntityRegistry : IDisposable
    {
        NativeArray<int> m_Generations;
        NativeArray<short> m_Tables;
        NativeArray<int> m_Rows;
        NativeArray<int> m_FreeSlots;
        int m_FreeCount;
        int m_Used;

        public int Capacity => m_Generations.Length;
        public int AliveCount { get; private set; }

        public EntityRegistry(int capacity)
        {
            m_Generations = new NativeArray<int>(capacity, Allocator.Persistent);
            m_Tables = new NativeArray<short>(capacity, Allocator.Persistent);
            m_Rows = new NativeArray<int>(capacity, Allocator.Persistent);
            m_FreeSlots = new NativeArray<int>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        /// <summary>Allocates a handle; returns Null when every slot is in use.</summary>
        public EntityHandle Allocate(int tableIndex, int row)
        {
            int slot;
            if (m_FreeCount > 0)
                slot = m_FreeSlots[--m_FreeCount];
            else if (m_Used < Capacity)
                slot = m_Used++;
            else
                return EntityHandle.Null;

            int generation = m_Generations[slot] + 1;
            m_Generations[slot] = generation;
            m_Tables[slot] = (short)tableIndex;
            m_Rows[slot] = row;
            AliveCount++;
            return new EntityHandle(slot, generation);
        }

        public bool IsAlive(EntityHandle handle)
        {
            return handle.Generation != 0
                && (uint)handle.Index < (uint)m_Used
                && m_Generations[handle.Index] == handle.Generation
                && m_Tables[handle.Index] >= 0;
        }

        public bool TryResolve(EntityHandle handle, out int tableIndex, out int row)
        {
            if (!IsAlive(handle))
            {
                tableIndex = -1;
                row = -1;
                return false;
            }
            tableIndex = m_Tables[handle.Index];
            row = m_Rows[handle.Index];
            return true;
        }

        internal void SetRow(EntityHandle handle, int row) => m_Rows[handle.Index] = row;

        public bool Release(EntityHandle handle)
        {
            if (!IsAlive(handle))
                return false;
            m_Tables[handle.Index] = -1;
            m_FreeSlots[m_FreeCount++] = handle.Index;
            AliveCount--;
            return true;
        }

        /// <summary>Invalidates every handle (generations keep increasing so old handles stay dead).</summary>
        public void Clear()
        {
            for (int i = 0; i < m_Used; i++)
            {
                if (m_Tables[i] >= 0)
                    m_Generations[i] = m_Generations[i] + 1;
                m_Tables[i] = -1;
            }
            m_FreeCount = 0;
            for (int i = m_Used - 1; i >= 0; i--)
                m_FreeSlots[m_FreeCount++] = i;
            AliveCount = 0;
        }

        public EntityLookup AsLookup() => new EntityLookup(m_Generations, m_Tables, m_Rows);

        public void Dispose()
        {
            if (m_Generations.IsCreated) m_Generations.Dispose();
            if (m_Tables.IsCreated) m_Tables.Dispose();
            if (m_Rows.IsCreated) m_Rows.Dispose();
            if (m_FreeSlots.IsCreated) m_FreeSlots.Dispose();
        }
    }

    /// <summary>Read-only handle resolution usable inside Burst jobs.</summary>
    public struct EntityLookup
    {
        [ReadOnly] NativeArray<int> m_Generations;
        [ReadOnly] NativeArray<short> m_Tables;
        [ReadOnly] NativeArray<int> m_Rows;

        internal EntityLookup(NativeArray<int> generations, NativeArray<short> tables, NativeArray<int> rows)
        {
            m_Generations = generations;
            m_Tables = tables;
            m_Rows = rows;
        }

        public bool TryResolve(EntityHandle handle, out int tableIndex, out int row)
        {
            if (handle.Generation != 0 && (uint)handle.Index < (uint)m_Generations.Length
                && m_Generations[handle.Index] == handle.Generation && m_Tables[handle.Index] >= 0)
            {
                tableIndex = m_Tables[handle.Index];
                row = m_Rows[handle.Index];
                return true;
            }
            tableIndex = -1;
            row = -1;
            return false;
        }
    }
}
