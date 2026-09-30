using System.Threading;

namespace SPF.Contracts
{
    /// <summary>
    /// Identity of a piece of simulation data (table, column or resource). Systems declare reads and
    /// writes against keys; the scheduler turns those declarations into job dependencies.
    /// Keys are meant to be created once as static readonly fields; each gets a process-wide dense Id.
    /// </summary>
    public abstract class AccessKey
    {
        static int s_Count;

        public readonly int Id;
        public readonly string Name;

        protected AccessKey(string name)
        {
            Id = Interlocked.Increment(ref s_Count) - 1;
            Name = name;
        }

        /// <summary>Number of keys created so far; upper bound for Id.</summary>
        public static int Count => Volatile.Read(ref s_Count);

        public override string ToString() => Name;
    }

    /// <summary>Entity table. Declaring access to a table covers its entity-handle column.</summary>
    public sealed class TableKey : AccessKey
    {
        public TableKey(string name) : base(name) { }
    }

    /// <summary>One SoA column of a table, typed by its element.</summary>
    public sealed class ColumnKey<T> : AccessKey where T : unmanaged
    {
        public readonly TableKey Table;

        public ColumnKey(TableKey table, string name) : base(table.Name + "." + name)
        {
            Table = table;
        }
    }

    /// <summary>A world-owned object shared between systems (queues, grids, snapshots, config).</summary>
    public sealed class ResourceKey<T> : AccessKey where T : class
    {
        public ResourceKey(string name) : base(name) { }
    }
}
