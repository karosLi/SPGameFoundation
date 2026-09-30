using System;
using SPF.Contracts;
using SPF.L1.Spatial;
using Unity.Mathematics;

namespace SPF.L2.Elements
{
    /// <summary>
    /// Tracks how many elements (food, props…) each chunk of a region holds. Only chunks inside the
    /// active window have real entities; the rest keep a stored count that is instantiated when the
    /// window reaches them. Main-thread only, allocation-free after construction.
    /// </summary>
    public sealed class ChunkPopulation : IResettableResource
    {
        readonly int[] m_Stored;
        readonly int[] m_Live;
        readonly bool[] m_Active;
        readonly int[] m_ActiveList;
        readonly int[] m_Entering;
        readonly int[] m_Leaving;
        readonly int m_TargetPerChunk;
        int m_ActiveCount;
        int m_Cursor;

        public ChunkPopulation(ChunkLayout layout, int targetPerChunk)
        {
            Layout = layout;
            m_TargetPerChunk = targetPerChunk;
            int n = layout.ChunkCount;
            m_Stored = new int[n];
            m_Live = new int[n];
            m_Active = new bool[n];
            m_ActiveList = new int[n];
            m_Entering = new int[n];
            m_Leaving = new int[n];
            OnReset();
        }

        public ChunkLayout Layout { get; }
        public int TargetPerChunk => m_TargetPerChunk;
        public int ActiveCount => m_ActiveCount;
        public int EnteringCount { get; private set; }
        public int LeavingCount { get; private set; }
        public int Entering(int i) => m_Entering[i];
        public int Leaving(int i) => m_Leaving[i];
        public bool IsActive(int chunk) => m_Active[chunk];
        public int Live(int chunk) => m_Live[chunk];
        public int Stored(int chunk) => m_Stored[chunk];

        /// <summary>Total elements in the region, instantiated or not.</summary>
        public long Total
        {
            get
            {
                long total = 0;
                for (int i = 0; i < m_Stored.Length; i++) total += m_Active[i] ? m_Live[i] : m_Stored[i];
                return total;
            }
        }

        /// <summary>
        /// Sets the active window. Afterwards Entering/Leaving list the chunks whose entities must be
        /// spawned (use <see cref="TakeStored"/>) or despawned (use <see cref="Store"/>).
        /// </summary>
        public void SetWindow(float2 min, float2 max)
        {
            int2 from = Layout.CoordOf(min);
            int2 to = Layout.CoordOf(max);
            EnteringCount = 0;
            LeavingCount = 0;

            for (int i = 0; i < m_ActiveCount; i++)
            {
                int chunk = m_ActiveList[i];
                int2 c = Layout.CoordOfIndex(chunk);
                if (math.any(c < from) || math.any(c > to))
                    m_Leaving[LeavingCount++] = chunk;
            }
            for (int i = 0; i < LeavingCount; i++)
                m_Active[m_Leaving[i]] = false;

            m_ActiveCount = 0;
            for (int y = from.y; y <= to.y; y++)
            for (int x = from.x; x <= to.x; x++)
            {
                int chunk = Layout.IndexOf(new int2(x, y));
                if (!m_Active[chunk])
                {
                    m_Active[chunk] = true;
                    m_Entering[EnteringCount++] = chunk;
                }
                m_ActiveList[m_ActiveCount++] = chunk;
            }
        }

        /// <summary>Moves a chunk's stored count to live; returns how many entities to spawn.</summary>
        public int TakeStored(int chunk)
        {
            int n = m_Stored[chunk];
            m_Stored[chunk] = 0;
            return n;
        }

        /// <summary>An entity of an inactive (leaving) chunk was despawned; remember it.</summary>
        public void Store(int chunk)
        {
            m_Stored[chunk]++;
            if (m_Live[chunk] > 0) m_Live[chunk]--;
        }

        public void OnSpawned(int chunk) => m_Live[chunk]++;

        public void OnRemoved(int chunk)
        {
            if (m_Live[chunk] > 0) m_Live[chunk]--;
        }

        /// <summary>Adds value to a chunk that is not instantiated (e.g. drops outside the window).</summary>
        public void AddStored(int chunk, int count) => m_Stored[chunk] += count;

        /// <summary>
        /// Round-robin over active chunks: returns the next chunk below target and its deficit
        /// (at most maxPerChunk), or -1 after scanning maxChunks chunks.
        /// </summary>
        public int NextDeficit(int maxChunks, int maxPerChunk, out int deficit)
        {
            deficit = 0;
            if (m_ActiveCount == 0) return -1;
            for (int n = 0; n < maxChunks; n++)
            {
                m_Cursor = (m_Cursor + 1) % m_ActiveCount;
                int chunk = m_ActiveList[m_Cursor];
                int missing = m_TargetPerChunk - m_Live[chunk];
                if (missing > 0)
                {
                    deficit = math.min(missing, maxPerChunk);
                    return chunk;
                }
            }
            return -1;
        }

        public void OnReset()
        {
            for (int i = 0; i < m_Stored.Length; i++)
            {
                m_Stored[i] = m_TargetPerChunk;
                m_Live[i] = 0;
                m_Active[i] = false;
            }
            m_ActiveCount = 0;
            m_Cursor = 0;
            EnteringCount = 0;
            LeavingCount = 0;
        }

        /// <summary>Deactivates everything, converting live counts back to stored (region switch).</summary>
        public void DeactivateAll()
        {
            for (int i = 0; i < m_ActiveCount; i++)
            {
                int chunk = m_ActiveList[i];
                m_Stored[chunk] += m_Live[chunk];
                m_Live[chunk] = 0;
                m_Active[chunk] = false;
            }
            m_ActiveCount = 0;
        }
    }
}
