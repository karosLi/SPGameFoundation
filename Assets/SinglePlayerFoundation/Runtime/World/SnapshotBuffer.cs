using System;
using Unity.Collections;

namespace SPF.Runtime.World
{
    /// <summary>
    /// Triple-buffered per-tick output for presentation. The Snapshot phase writes <see cref="Write"/>;
    /// at the end of the tick buffers rotate so presentation can interpolate between
    /// <see cref="Previous"/> and <see cref="Current"/> with the clock's alpha.
    /// Rows are matched by index: writers that need stable interpolation should also store an id.
    /// </summary>
    public sealed class SnapshotBuffer<T> : ISyncResource, IResettableResource, IDisposable where T : unmanaged
    {
        NativeArray<T> m_Previous;
        NativeArray<T> m_Current;
        NativeArray<T> m_Write;
        NativeArray<int> m_WriteCount;

        public SnapshotBuffer(int capacity)
        {
            m_Previous = new NativeArray<T>(capacity, Allocator.Persistent);
            m_Current = new NativeArray<T>(capacity, Allocator.Persistent);
            m_Write = new NativeArray<T>(capacity, Allocator.Persistent);
            m_WriteCount = new NativeArray<int>(1, Allocator.Persistent);
        }

        public int Capacity => m_Write.Length;

        /// <summary>Destination for the tick being simulated (full capacity).</summary>
        public NativeArray<T> Write => m_Write;

        /// <summary>Jobs store the number of written rows in element 0.</summary>
        public NativeArray<int> WriteCount => m_WriteCount;

        public NativeArray<T> Previous => m_Previous.GetSubArray(0, PreviousCount);
        public NativeArray<T> Current => m_Current.GetSubArray(0, CurrentCount);
        public int PreviousCount { get; private set; }
        public int CurrentCount { get; private set; }

        /// <summary>Incremented on every rotation; presentation can detect new data.</summary>
        public uint Version { get; private set; }

        public void OnSync()
        {
            var recycled = m_Previous;
            m_Previous = m_Current;
            m_Current = m_Write;
            m_Write = recycled;
            PreviousCount = CurrentCount;
            CurrentCount = Math.Min(Math.Max(m_WriteCount[0], 0), m_Current.Length);
            m_WriteCount[0] = 0;
            Version++;
        }

        public void OnReset()
        {
            PreviousCount = 0;
            CurrentCount = 0;
            m_WriteCount[0] = 0;
        }

        public void Dispose()
        {
            if (m_Previous.IsCreated) m_Previous.Dispose();
            if (m_Current.IsCreated) m_Current.Dispose();
            if (m_Write.IsCreated) m_Write.Dispose();
            if (m_WriteCount.IsCreated) m_WriteCount.Dispose();
        }
    }
}
