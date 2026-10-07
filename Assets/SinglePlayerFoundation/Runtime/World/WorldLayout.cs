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
    public sealed class WorldLayout : IDisposable
    {
        bool m_Consumed;
        bool m_Disposed;

        internal readonly List<TableSpec> Tables = new List<TableSpec>();
        internal readonly List<(AccessKey key, object resource)> Resources = new List<(AccessKey, object)>();

        /// <summary>Capacity of the per-tick destroy queue.</summary>
        public int DestroyQueueCapacity { get; set; } = 4096;

        /// <summary>Declares a table, or raises the capacity of an existing declaration.</summary>
        public TableSpec Table(TableKey key, int capacity)
        {
            ThrowIfConsumed();
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

        internal readonly HashSet<int> LevelResources = new HashSet<int>();

        /// <summary>
        /// Registers a world-owned resource. IDisposable resources are disposed with the world.
        /// <paramref name="levelScoped"/> resources (must be <see cref="IResettableResource"/>) are reset by
        /// <see cref="SimWorld.ClearLevel"/>, together with level-scoped tables.
        /// </summary>
        public void Resource<T>(ResourceKey<T> key, T resource, bool levelScoped = false) where T : class
        {
            ThrowIfConsumed();
            if (resource == null) throw new ArgumentNullException(nameof(resource));
            if (levelScoped && !(resource is IResettableResource))
                throw new ArgumentException($"Level-scoped resource {key} must implement IResettableResource.");
            foreach (var entry in Resources)
            {
                if (entry.key == key)
                    throw new InvalidOperationException($"Resource {key} is registered twice.");
            }
            // Finish scope metadata first, so any rejected registration keeps caller ownership.
            if (levelScoped) LevelResources.Add(key.Id);
            try { Resources.Add((key, resource)); }
            catch
            {
                if (levelScoped) LevelResources.Remove(key.Id);
                throw;
            }
        }

        internal void ThrowIfConsumed()
        {
            if (m_Consumed || m_Disposed)
                throw new InvalidOperationException("A world layout can only transfer its resources once.");
        }

        internal void TransferOwnership() => m_Consumed = true;

        /// <summary>Releases accepted resources if construction has not transferred them to a world.
        /// Use a scope when declaring a layout directly. Disposal after successful world creation is a no-op.</summary>
        public void Dispose()
        {
            if (m_Consumed || m_Disposed) return;
            m_Disposed = true;
            Exception failure = null;
            for (int i = 0; i < Resources.Count; i++)
            {
                bool duplicate = false;
                for (int j = 0; j < i; j++)
                    if (ReferenceEquals(Resources[i].resource, Resources[j].resource)) { duplicate = true; break; }
                if (!duplicate && Resources[i].resource is IDisposable resource)
                    CleanupErrors.Try(resource.Dispose, ref failure);
            }
            Resources.Clear();
            CleanupErrors.ThrowIfAny(failure);
        }

        public sealed class TableSpec
        {
            internal readonly List<Action<SimTable>> ColumnFactories = new List<Action<SimTable>>();
            readonly HashSet<int> m_ColumnIds = new HashSet<int>();

            public TableKey Key { get; }
            public int Capacity { get; internal set; }
            public bool TrackChanges { get; private set; }
            public bool IsLevelScoped { get; private set; }
            public bool IsPooled { get; private set; }

            /// <summary>Handle-free rows removed by dead flags and compacted every tick (see <see cref="SimTable.IsPooled"/>).</summary>
            public TableSpec Pooled()
            {
                IsPooled = true;
                return this;
            }

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

            /// <summary>
            /// Entities of this table belong to the current level (monsters, pickups, projectiles):
            /// <see cref="SimWorld.ClearLevel"/> removes them all at once.
            /// </summary>
            public TableSpec LevelScoped()
            {
                IsLevelScoped = true;
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
