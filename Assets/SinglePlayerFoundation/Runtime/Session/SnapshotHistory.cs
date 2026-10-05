using System.Collections.Generic;
using System.IO;

namespace SPF.Runtime.Session
{
    /// <summary>
    /// Undo / redo on whole-session snapshots (puzzles, turn-based games, level editors, "rewind"):
    /// <see cref="Record"/> after each completed move, <see cref="Undo"/> / <see cref="Redo"/> restore exactly.
    /// Keeps at most <see cref="Capacity"/> states (oldest dropped) and reuses their buffers, so steady play
    /// allocates nothing once the history is full.
    /// </summary>
    public sealed class SnapshotHistory
    {
        readonly SimSession m_Session;
        readonly List<byte[]> m_States = new List<byte[]>();
        readonly List<int> m_Lengths = new List<int>();
        readonly MemoryStream m_Buffer = new MemoryStream();
        int m_Current = -1;

        public SnapshotHistory(SimSession session, int capacity = 64)
        {
            m_Session = session;
            Capacity = System.Math.Max(capacity, 2);
        }

        public int Capacity { get; }
        public int Count => m_States.Count;
        public bool CanUndo => m_Current > 0;
        public bool CanRedo => m_Current >= 0 && m_Current < m_States.Count - 1;

        /// <summary>Total bytes held.</summary>
        public long Bytes
        {
            get { long b = 0; foreach (int l in m_Lengths) b += l; return b; }
        }

        /// <summary>Stores the current state as the newest entry (drops any redo branch).</summary>
        public void Record()
        {
            m_Buffer.SetLength(0);
            using (var writer = new BinaryWriter(m_Buffer, System.Text.Encoding.UTF8, true))
                m_Session.WriteSnapshot(writer);
            int length = (int)m_Buffer.Length;
            // Drop the redo branch, then the oldest beyond capacity, recycling their arrays.
            byte[] reuse = null;
            while (m_States.Count > m_Current + 1) { reuse = m_States[m_States.Count - 1]; m_States.RemoveAt(m_States.Count - 1); m_Lengths.RemoveAt(m_Lengths.Count - 1); }
            if (m_States.Count >= Capacity) { reuse = m_States[0]; m_States.RemoveAt(0); m_Lengths.RemoveAt(0); }
            if (reuse == null || reuse.Length < length) reuse = new byte[length];
            System.Array.Copy(m_Buffer.GetBuffer(), reuse, length);
            m_States.Add(reuse);
            m_Lengths.Add(length);
            m_Current = m_States.Count - 1;
        }

        public bool Undo()
        {
            if (!CanUndo) return false;
            Restore(--m_Current);
            return true;
        }

        public bool Redo()
        {
            if (!CanRedo) return false;
            Restore(++m_Current);
            return true;
        }

        public void Clear()
        {
            m_States.Clear();
            m_Lengths.Clear();
            m_Current = -1;
        }

        void Restore(int index)
        {
            using var reader = new BinaryReader(new MemoryStream(m_States[index], 0, m_Lengths[index], false));
            m_Session.ReadSnapshot(reader);
        }
    }
}
