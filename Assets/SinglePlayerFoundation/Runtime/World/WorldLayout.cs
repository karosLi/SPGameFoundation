using System;
using System.Collections.Generic;
using SPF.Contracts;

namespace SPF.Runtime.World
{
    /// <summary>
    /// Collects the data every gameplay module needs before the world is allocated: tables with their
    /// capacity and columns, and shared resources. Modules may extend tables declared by other modules
    /// (extension columns, Docs/Architecture.md §6.3).
    /// </summary>
    public sealed class WorldLayout
    {
        internal readonly List<TableSpec> Tables = new List<TableSpec>();
        internal readonly List<(AccessKey key, object resource)> Resources = new List<(AccessKey, object)>();

        /// <summary>Capacity of the per-tick destroy queue.</summary>
        public int DestroyQueueCapacity { get; set; } = 4096;

        /// <summary>Declares a table, or raises the capacity of an existing declaration.</summary>
        public TableSpec Table(TableKey key, int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            foreach (var spec in Tables)
            {
                if (spec.Key == key)
                {
                    spec.Capacity = Math.Max(spec.Capacity, capacity);
                    return spec;
                }
            }
            var created = new TableSpec(key, capacity);
            Tables.Add(created);
            return created;
        }

        /// <summary>Registers a world-owned resource. IDisposable resources are disposed with the world.</summary>
        public void Resource<T>(ResourceKey<T> key, T resource) where T : class
        {
            foreach (var entry in Resources)
            {
                if (entry.key == key)
                    throw new InvalidOperationException($"Resource {key} is registered twice.");
            }
            Resources.Add((key, resource ?? throw new ArgumentNullException(nameof(resource))));
        }

        public sealed class TableSpec
        {
            internal readonly List<Action<SimTable>> ColumnFactories = new List<Action<SimTable>>();
            readonly HashSet<int> m_ColumnIds = new HashSet<int>();

            public TableKey Key { get; }
            public int Capacity { get; internal set; }
            public bool TrackChanges { get; private set; }

            internal TableSpec(TableKey key, int capacity)
            {
                Key = key;
                Capacity = capacity;
            }

            /// <summary>Record changed rows (see <see cref="SimTable.Changes"/>).</summary>
            public TableSpec TrackChangedRows()
            {
                TrackChanges = true;
                return this;
            }

            public TableSpec Column<T>(ColumnKey<T> key) where T : unmanaged
            {
                if (key.Table != Key)
                    throw new ArgumentException($"Column {key} belongs to table {key.Table}, not {Key}.");
                if (m_ColumnIds.Add(key.Id))
                    ColumnFactories.Add(table => table.AddColumn(key));
                return this;
            }
        }
    }
}
