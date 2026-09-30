using Unity.Jobs;

namespace SPF.Runtime.Scheduling
{
    /// <summary>
    /// Turns read / write declarations into JobHandle chains: readers wait for the last writer,
    /// writers wait for the last writer and every reader since. Independent systems run in parallel.
    /// Allocation-free per tick.
    /// </summary>
    internal sealed class DependencyTracker
    {
        readonly JobHandle[] m_LastWrite;
        readonly JobHandle[] m_Readers;
        JobHandle m_All;
        JobHandle m_LastBarrier;

        public DependencyTracker(int keyCount)
        {
            m_LastWrite = new JobHandle[keyCount];
            m_Readers = new JobHandle[keyCount];
        }

        /// <summary>Everything scheduled this tick.</summary>
        public JobHandle All => m_All;

        public JobHandle GetDependency(AccessDeclaration access)
        {
            if (access.IsBarrier)
                return m_All;

            JobHandle dependency = m_LastBarrier;
            var reads = access.Reads;
            for (int i = 0; i < reads.Count; i++)
                dependency = JobHandle.CombineDependencies(dependency, m_LastWrite[reads[i]]);
            var writes = access.Writes;
            for (int i = 0; i < writes.Count; i++)
            {
                int id = writes[i];
                dependency = JobHandle.CombineDependencies(dependency, m_LastWrite[id], m_Readers[id]);
            }
            return dependency;
        }

        public void Record(AccessDeclaration access, JobHandle handle)
        {
            m_All = JobHandle.CombineDependencies(m_All, handle);
            if (access.IsBarrier)
            {
                m_LastBarrier = handle;
                return;
            }

            var reads = access.Reads;
            for (int i = 0; i < reads.Count; i++)
                m_Readers[reads[i]] = JobHandle.CombineDependencies(m_Readers[reads[i]], handle);
            var writes = access.Writes;
            for (int i = 0; i < writes.Count; i++)
            {
                m_LastWrite[writes[i]] = handle;
                m_Readers[writes[i]] = default;
            }
        }

        /// <summary>Call after All has completed.</summary>
        public void Reset()
        {
            System.Array.Clear(m_LastWrite, 0, m_LastWrite.Length);
            System.Array.Clear(m_Readers, 0, m_Readers.Length);
            m_All = default;
            m_LastBarrier = default;
        }
    }
}
