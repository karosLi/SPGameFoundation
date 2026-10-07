using System.Collections.Generic;
using SPF.Contracts;

namespace SPF.Runtime.Scheduling
{
    /// <summary>
    /// Read / write set of one system. A system that declares nothing is treated as a barrier:
    /// all previously scheduled work is completed before it runs (so main-thread code may touch any
    /// data) and everything after it waits for it. Declarations describe the fixed pipeline: callers
    /// must not retain and mutate them after Declare returns.
    /// </summary>
    public sealed class AccessDeclaration
    {
        internal readonly List<int> Reads = new List<int>();
        internal readonly List<int> Writes = new List<int>();
        internal readonly List<string> Names = new List<string>();
        internal readonly Dictionary<int, string> KeyNames = new Dictionary<int, string>();

        internal bool IsBarrier => Reads.Count == 0 && Writes.Count == 0;

        internal int MaxId
        {
            get
            {
                int max = -1;
                foreach (var id in Reads) if (id > max) max = id;
                foreach (var id in Writes) if (id > max) max = id;
                return max;
            }
        }

        public AccessDeclaration Read(AccessKey key)
        {
            if (!Reads.Contains(key.Id) && !Writes.Contains(key.Id))
            {
                Reads.Add(key.Id);
                KeyNames[key.Id] = key.Name;
                Names.Add("R " + key.Name);
            }
            return this;
        }

        /// <summary>Write implies read.</summary>
        public AccessDeclaration Write(AccessKey key)
        {
            if (Writes.Contains(key.Id))
                return this;
            if (Reads.Remove(key.Id))
                Names.Remove("R " + key.Name);
            Writes.Add(key.Id);
            KeyNames[key.Id] = key.Name;
            Names.Add("W " + key.Name);
            return this;
        }
    }
}
