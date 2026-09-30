using System;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace SPF.Contracts.Collections
{
    /// <summary>
    /// Fixed-capacity queue that many job threads can append to. Unlike NativeList.ParallelWriter,
    /// appending past capacity is safe in release builds: the item is dropped and counted as overflow.
    /// Read it on the main thread after the writing jobs have completed.
    /// </summary>
    public struct ParallelQueue<T> : IDisposable where T : unmanaged
    {
        NativeArray<T> m_Items;
        NativeArray<int> m_Counter;

        public ParallelQueue(int capacity, Allocator allocator)
        {
            m_Items = new NativeArray<T>(capacity, allocator, NativeArrayOptions.UninitializedMemory);
            m_Counter = new NativeArray<int>(1, allocator);
        }

        public bool IsCreated => m_Items.IsCreated;
        public int Capacity => m_Items.Length;

        /// <summary>Number of stored items (never above Capacity).</summary>
        public int Count => Math.Min(m_Counter[0], m_Items.Length);

        /// <summary>Number of items dropped because the queue was full.</summary>
        public int Overflow => Math.Max(0, m_Counter[0] - m_Items.Length);

        public T this[int index] => m_Items[index];

        /// <summary>Stored items; valid until the next Clear.</summary>
        public NativeArray<T> AsArray() => m_Items.GetSubArray(0, Count);

        public void Clear() => m_Counter[0] = 0;

        /// <summary>Single-threaded append for main-thread producers.</summary>
        public bool TryAdd(in T item)
        {
            int index = m_Counter[0];
            m_Counter[0] = index + 1;
            if (index >= m_Items.Length)
                return false;
            m_Items[index] = item;
            return true;
        }

        public Writer AsWriter() => new Writer(m_Items, m_Counter);

        public void Dispose()
        {
            if (m_Items.IsCreated) m_Items.Dispose();
            if (m_Counter.IsCreated) m_Counter.Dispose();
        }

        public unsafe struct Writer
        {
            [NativeDisableParallelForRestriction] NativeArray<T> m_Items;
            [NativeDisableParallelForRestriction] NativeArray<int> m_Counter;

            internal Writer(NativeArray<T> items, NativeArray<int> counter)
            {
                m_Items = items;
                m_Counter = counter;
            }

            public bool TryAdd(in T item)
            {
                int* counter = (int*)m_Counter.GetUnsafePtr();
                int index = Interlocked.Increment(ref *counter) - 1;
                if (index >= m_Items.Length)
                    return false;
                m_Items[index] = item;
                return true;
            }
        }
    }
}
