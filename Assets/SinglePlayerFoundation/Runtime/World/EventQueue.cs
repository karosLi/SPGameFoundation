using System;
using SPF.Contracts;
using SPF.Contracts.Collections;
using Unity.Collections;

namespace SPF.Runtime.World
{
    /// <summary>
    /// World resource wrapping a <see cref="ParallelQueue{T}"/>: jobs append through <see cref="AsWriter"/>,
    /// a later system (or the main thread after sync) reads and clears it. Overflow is counted, never fatal.
    /// </summary>
    public sealed class EventQueue<T> : IDisposable, IResettableResource where T : unmanaged
    {
        ParallelQueue<T> m_Queue;

        public EventQueue(int capacity)
        {
            m_Queue = new ParallelQueue<T>(capacity, Allocator.Persistent);
        }

        public int Count => m_Queue.Count;
        public int Capacity => m_Queue.Capacity;
        public long TotalOverflow { get; private set; }
        public T this[int index] => m_Queue[index];

        /// <summary>Stored items (valid until Clear). Sortable in place.</summary>
        public NativeArray<T> AsArray() => m_Queue.AsArray();

        public ParallelQueue<T>.Writer AsWriter() => m_Queue.AsWriter();

        /// <summary>Raw queue for jobs that read and clear it (single consumer).</summary>
        public ParallelQueue<T> Raw => m_Queue;

        public bool TryAdd(in T item) => m_Queue.TryAdd(item);

        public void Clear()
        {
            TotalOverflow += m_Queue.Overflow;
            m_Queue.Clear();
        }

        public void OnReset()
        {
            m_Queue.Clear();
            TotalOverflow = 0;
        }

        public void Dispose() => m_Queue.Dispose();
    }
}
