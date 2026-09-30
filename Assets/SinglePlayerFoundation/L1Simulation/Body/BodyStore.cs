using System;
using SPF.Contracts;
using Unity.Collections;
using Unity.Mathematics;

namespace SPF.L1.Body
{
    /// <summary>
    /// One pool of trail points shared by every chain (snake). Slabs come in power-of-two size classes
    /// with a free list per class, so growth never fragments beyond one class and nothing is allocated
    /// during a session. Allocation and resize are main-thread only (ApplyCommands phase).
    /// </summary>
    public sealed class BodyStore : IDisposable, IResettableResource
    {
        public const int MinSlab = 16;

        readonly int m_MinShift;
        readonly int m_MaxShift;
        readonly int[][] m_FreeLists;
        readonly int[] m_FreeCounts;
        int m_Top;
        NativeArray<float2> m_Points;

        public BodyStore(int totalPoints, int maxSlab)
        {
            if (!math.ispow2(maxSlab) || maxSlab < MinSlab)
                throw new ArgumentException("maxSlab must be a power of two ≥ 16", nameof(maxSlab));
            m_Points = new NativeArray<float2>(totalPoints, Allocator.Persistent);
            m_MinShift = math.tzcnt(MinSlab);
            m_MaxShift = math.tzcnt(maxSlab);
            int classes = m_MaxShift - m_MinShift + 1;
            m_FreeLists = new int[classes][];
            m_FreeCounts = new int[classes];
            for (int c = 0; c < classes; c++)
                m_FreeLists[c] = new int[totalPoints / (MinSlab << c) + 1];
        }

        public NativeArray<float2> Points => m_Points;
        public int TotalPoints => m_Points.Length;
        public int MaxSlab => 1 << m_MaxShift;
        public int UsedPoints { get; private set; }

        public static int SlabFor(int points, int maxSlab) =>
            math.min(math.max(math.ceilpow2(math.max(points, 1)), MinSlab), maxSlab);

        public bool TryAllocate(int minCapacity, out TrailState trail)
        {
            trail = default;
            int size = SlabFor(minCapacity, MaxSlab);
            int cls = math.tzcnt(size) - m_MinShift;
            int start;
            if (m_FreeCounts[cls] > 0)
                start = m_FreeLists[cls][--m_FreeCounts[cls]];
            else if (m_Top + size <= m_Points.Length)
            {
                start = m_Top;
                m_Top += size;
            }
            else
                return false;

            trail.Start = start;
            trail.Capacity = size;
            UsedPoints += size;
            return true;
        }

        public void Free(ref TrailState trail)
        {
            if (!trail.IsAllocated)
                return;
            int cls = math.tzcnt(trail.Capacity) - m_MinShift;
            m_FreeLists[cls][m_FreeCounts[cls]++] = trail.Start;
            UsedPoints -= trail.Capacity;
            trail = default;
        }

        /// <summary>
        /// Moves the trail into a slab that fits <paramref name="minCapacity"/> points, keeping the kept
        /// points and their global indices. Returns false (trail unchanged) when the pool is exhausted.
        /// </summary>
        public bool Resize(ref TrailState trail, int minCapacity)
        {
            int size = SlabFor(minCapacity, MaxSlab);
            if (size == trail.Capacity)
                return true;
            if (!TryAllocate(size, out var moved))
                return false;

            moved.Pushed = trail.Pushed;
            moved.Last = trail.Last;
            moved.Count = math.min(trail.Count, moved.Capacity);
            for (int i = 0; i < moved.Count; i++)
            {
                uint g = trail.Pushed - 1 - (uint)i;
                m_Points[moved.Slot(g)] = m_Points[trail.Slot(g)];
            }
            Free(ref trail);
            trail = moved;
            return true;
        }

        public void OnReset()
        {
            m_Top = 0;
            UsedPoints = 0;
            Array.Clear(m_FreeCounts, 0, m_FreeCounts.Length);
        }

        public void Dispose()
        {
            if (m_Points.IsCreated) m_Points.Dispose();
        }
    }
}
