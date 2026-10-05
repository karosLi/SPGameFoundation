using System;
using System.Collections.Generic;
using SPF.Contracts;
using SPF.Contracts.Collections;
using Unity.Collections;

namespace SPF.Runtime.World
{
    /// <summary>
    /// Owns all simulation state of a session: entity tables, the handle registry, the deferred destroy
    /// queue and shared resources. Everything is allocated once from a <see cref="WorldLayout"/>.
    /// </summary>
    public sealed class SimWorld : IDisposable
    {
        /// <summary>Destroy requests written by jobs during a tick, applied at the next ApplyCommands.</summary>
        public static readonly ResourceKey<DestroyQueue> DestroyQueueKey = new ResourceKey<DestroyQueue>("World.DestroyQueue");

        readonly List<SimTable> m_Tables = new List<SimTable>();
        readonly Dictionary<int, SimTable> m_TablesByKey = new Dictionary<int, SimTable>();
        readonly Dictionary<int, object> m_Resources = new Dictionary<int, object>();
        readonly List<object> m_ResourceList = new List<object>();
        readonly List<AccessKey> m_ResourceKeys = new List<AccessKey>();
        readonly List<ISyncResource> m_SyncResources = new List<ISyncResource>();
        readonly List<IResettableResource> m_ResettableResources = new List<IResettableResource>();
        readonly DestroyQueue m_DestroyQueue;
        readonly List<SimTable> m_LevelTables = new List<SimTable>();
        readonly List<SimTable> m_PooledTables = new List<SimTable>();
        readonly List<IResettableResource> m_LevelResources = new List<IResettableResource>();

        public EntityRegistry Registry { get; }

        /// <summary>Development check of undeclared accesses during system scheduling (see <see cref="AccessGuard"/>).</summary>
        public AccessGuard Guard { get; } = new AccessGuard();
        public IReadOnlyList<SimTable> Tables => m_Tables;
        public uint Seed { get; }

        /// <summary>Entities that could not be created because a table or the registry was full.</summary>
        public int CreateFailures { get; private set; }

        public SimWorld(WorldLayout layout, uint seed)
        {
            Seed = seed;
            int totalCapacity = 0;
            foreach (var spec in layout.Tables)
            {
                if (m_Tables.Count >= short.MaxValue)
                    throw new InvalidOperationException("Too many tables.");
                var table = new SimTable(spec.Key, m_Tables.Count, spec.Capacity, spec.IsPooled);
                if (spec.IsPooled) m_PooledTables.Add(table);
                foreach (var addColumn in spec.ColumnFactories)
                    addColumn(table);
                if (spec.TrackChanges)
                    table.EnableChangeTracking();
                table.Guard = Guard;
                if (spec.IsLevelScoped) m_LevelTables.Add(table);
                m_Tables.Add(table);
                m_TablesByKey.Add(spec.Key.Id, table);
                totalCapacity += spec.Capacity;
            }
            Registry = new EntityRegistry(Math.Max(1, totalCapacity));

            m_DestroyQueue = new DestroyQueue(layout.DestroyQueueCapacity);
            AddResource(DestroyQueueKey, m_DestroyQueue);
            foreach (var (key, resource) in layout.Resources)
            {
                AddResource(key, resource);
                if (layout.LevelResources.Contains(key.Id))
                    m_LevelResources.Add((IResettableResource)resource);
            }
        }

        /// <summary>Incremented by every <see cref="ClearLevel"/> (presentation drops per-level caches).</summary>
        public int LevelVersion { get; private set; }

        /// <summary>
        /// Ends the current level: destroys every entity of the level-scoped tables (their handles become
        /// stale) and resets the level-scoped resources. Session-scoped data (the hero, progress, config)
        /// is untouched. Main thread, ApplyCommands phase or between ticks. Returns the entities removed.
        /// </summary>
        public int ClearLevel()
        {
            int removed = 0;
            foreach (var table in m_LevelTables)
            {
                var handles = table.Handles;
                // Last row first: the same handle release order as destroying the rows one by one.
                for (int row = table.Count - 1; row >= 0; row--)
                    if (Registry.Release(handles[row])) removed++;
                table.Clear();
            }
            for (int i = 0; i < m_LevelResources.Count; i++)
                m_LevelResources[i].OnReset();
            LevelVersion++;
            return removed;
        }

        void AddResource(AccessKey key, object resource)
        {
            m_Resources.Add(key.Id, resource);
            m_ResourceList.Add(resource);
            m_ResourceKeys.Add(key);
            if (resource is ISyncResource sync) m_SyncResources.Add(sync);
            if (resource is IResettableResource resettable) m_ResettableResources.Add(resettable);
        }

        public SimTable Table(TableKey key)
        {
            if (!m_TablesByKey.TryGetValue(key.Id, out var table))
                throw new ArgumentException($"Table {key} was not declared by any module.");
            return table;
        }

        public bool HasTable(TableKey key) => m_TablesByKey.ContainsKey(key.Id);

        public NativeArray<T> Column<T>(ColumnKey<T> key) where T : unmanaged => Table(key.Table).Column(key);

        public T Resource<T>(ResourceKey<T> key) where T : class
        {
            if (!m_Resources.TryGetValue(key.Id, out var resource))
                throw new ArgumentException($"Resource {key} was not registered by any module.");
            Guard.CheckResource(key, resource);
            return (T)resource;
        }

        public bool HasResource(AccessKey key) => m_Resources.ContainsKey(key.Id);

        // ---- Structural changes: main thread, ApplyCommands phase only. ----

        /// <summary>Creates an entity with a zeroed row. Returns Null (and counts a failure) when full.</summary>
        public EntityHandle CreateEntity(TableKey key, out int row)
        {
            var table = Table(key);
            row = -1;
            if (table.IsPooled)
                throw new InvalidOperationException($"Table {key} is pooled: use Spawn.");
            if (table.Count >= table.Capacity)
            {
                CreateFailures++;
                return EntityHandle.Null;
            }
            var handle = Registry.Allocate(table.TableIndex, table.Count);
            if (handle.IsNull)
            {
                CreateFailures++;
                return handle;
            }
            row = table.Add(handle);
            return handle;
        }

        /// <summary>Appends a zeroed row to a pooled table (no handle). Returns -1 (and counts a failure) when full.</summary>
        public int Spawn(TableKey key)
        {
            var table = Table(key);
            if (!table.IsPooled)
                throw new InvalidOperationException($"Table {key} is not pooled: use CreateEntity.");
            int row = table.AddPooled();
            if (row < 0) CreateFailures++;
            return row;
        }

        /// <summary>Compacts every pooled table (start of the tick, before systems run).</summary>
        internal void CompactPools()
        {
            for (int i = 0; i < m_PooledTables.Count; i++)
                m_PooledTables[i].Compact();
        }

        /// <summary>
        /// Reorders a table's rows by <paramref name="sortKeys"/> (one per row, ascending; ties keep the
        /// current order), keeping handles valid. Sorting by a spatial key (e.g. <c>Morton.Encode</c> of the
        /// grid cell) every few dozen ticks keeps neighbours close in memory, so the row lookups of spatial
        /// queries and the per-row jobs stay cache friendly. Main thread, between ticks or in ApplyCommands.
        /// </summary>
        public void SortRows(TableKey key, NativeArray<uint> sortKeys)
        {
            var table = Table(key);
            int n = table.Count;
            if (n < 2) return;
            if (sortKeys.Length < n) throw new ArgumentException("One sort key per row is needed.", nameof(sortKeys));
            var packed = new NativeArray<ulong>(n, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < n; i++) packed[i] = ((ulong)sortKeys[i] << 32) | (uint)i;
            packed.Sort();
            var order = new NativeArray<int>(n, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            bool changed = false;
            for (int i = 0; i < n; i++)
            {
                order[i] = (int)(packed[i] & 0xFFFFFFFFu);
                changed |= order[i] != i;
            }
            packed.Dispose();
            if (changed)
            {
                table.Permute(order);
                if (!table.IsPooled)
                {
                    var handles = table.Handles;
                    for (int i = 0; i < n; i++) Registry.SetRow(handles[i], i);
                }
            }
            order.Dispose();
        }

        public bool DestroyEntity(EntityHandle handle)
        {
            if (!Registry.TryResolve(handle, out int tableIndex, out int row))
                return false;
            var moved = m_Tables[tableIndex].RemoveAtSwapBack(row);
            if (!moved.IsNull)
                Registry.SetRow(moved, row);
            Registry.Release(handle);
            return true;
        }

        /// <summary>
        /// Applies queued destroys. Stale or duplicate handles are ignored. Parallel jobs push in
        /// thread-dependent order, so the queue is sorted first: swap-back row moves and handle reuse
        /// then depend only on which entities died, keeping the simulation deterministic.
        /// </summary>
        internal void PlaybackDestroys()
        {
            var items = m_DestroyQueue.Queue.AsArray();
            items.Sort(new HandleOrder());
            for (int i = 0; i < items.Length; i++)
                DestroyEntity(items[i]);
            m_DestroyQueue.RecordAndClear();
        }

        struct HandleOrder : IComparer<EntityHandle>
        {
            public int Compare(EntityHandle a, EntityHandle b) =>
                a.Index != b.Index ? a.Index.CompareTo(b.Index) : a.Generation.CompareTo(b.Generation);
        }

        internal void Sync()
        {
            for (int i = 0; i < m_SyncResources.Count; i++)
                m_SyncResources[i].OnSync();
        }

        /// <summary>Empties every table and invalidates all handles without releasing memory.</summary>
        public void Reset()
        {
            foreach (var table in m_Tables)
                table.Clear();
            Registry.Clear();
            CreateFailures = 0;
            for (int i = 0; i < m_ResettableResources.Count; i++)
                m_ResettableResources[i].OnReset();
        }

        // ---- Snapshots: between ticks only (no job running). ----

        const int SnapshotMagic = 0x57465053;   // "SPFW"
        const int SnapshotFormat = 1;

        /// <summary>
        /// Resources jobs use that cannot be snapshotted (no <see cref="ISnapshotResource"/>). A world with
        /// any of these cannot be saved mid-level; main-thread resources are the game's responsibility.
        /// </summary>
        public List<string> SnapshotGaps()
        {
            var gaps = new List<string>();
            for (int i = 0; i < m_ResourceList.Count; i++)
                if (m_ResourceList[i] is IJobData && !(m_ResourceList[i] is ISnapshotResource))
                    gaps.Add(m_ResourceKeys[i].Name);
            return gaps;
        }

        /// <summary>
        /// Writes the complete simulation state: every table's live rows, the handle registry and every
        /// <see cref="ISnapshotResource"/>. Configuration resources are not saved; the reading world must be
        /// built from the same layout and seed.
        /// </summary>
        public void WriteSnapshot(System.IO.BinaryWriter writer)
        {
            var gaps = SnapshotGaps();
            if (gaps.Count > 0)
                throw new NotSupportedException("These resources do not support snapshots: " + string.Join(", ", gaps));
            writer.Write(SnapshotMagic);
            writer.Write(SnapshotFormat);
            writer.Write(Seed);
            writer.Write(CreateFailures);
            writer.Write(m_Tables.Count);
            foreach (var table in m_Tables)
            {
                writer.Write(table.Key.Name);
                table.WriteSnapshot(writer);
            }
            Registry.WriteSnapshot(writer);
            NativeIO.WriteMarker(writer, "registry");
            for (int i = 0; i < m_ResourceList.Count; i++)
            {
                if (!(m_ResourceList[i] is ISnapshotResource resource)) continue;
                writer.Write(m_ResourceKeys[i].Name);
                resource.WriteSnapshot(writer);
                NativeIO.WriteMarker(writer, m_ResourceKeys[i].Name);
            }
            writer.Write("");
        }

        /// <summary>
        /// Restores a snapshot written by a world with the same layout and seed. Throws
        /// <see cref="System.IO.InvalidDataException"/> when it does not fit; the world is then left
        /// partially restored and must be <see cref="Reset"/> before further use.
        /// </summary>
        public void ReadSnapshot(System.IO.BinaryReader reader)
        {
            if (reader.ReadInt32() != SnapshotMagic || reader.ReadInt32() != SnapshotFormat)
                throw new System.IO.InvalidDataException("Not a world snapshot (or an unsupported format).");
            if (reader.ReadUInt32() != Seed)
                throw new System.IO.InvalidDataException("Snapshot was taken in a world with a different seed.");
            CreateFailures = reader.ReadInt32();
            if (reader.ReadInt32() != m_Tables.Count)
                throw new System.IO.InvalidDataException("Snapshot has a different table set.");
            foreach (var table in m_Tables)
            {
                if (reader.ReadString() != table.Key.Name)
                    throw new System.IO.InvalidDataException($"Snapshot table order differs at {table.Key}.");
                table.ReadSnapshot(reader);
            }
            Registry.ReadSnapshot(reader);
            NativeIO.ReadMarker(reader, "registry");
            for (int i = 0; i < m_ResourceList.Count; i++)
            {
                if (!(m_ResourceList[i] is ISnapshotResource resource)) continue;
                if (reader.ReadString() != m_ResourceKeys[i].Name)
                    throw new System.IO.InvalidDataException($"Snapshot resource order differs at {m_ResourceKeys[i]}.");
                resource.ReadSnapshot(reader);
                NativeIO.ReadMarker(reader, m_ResourceKeys[i].Name);
            }
            if (reader.ReadString() != "")
                throw new System.IO.InvalidDataException("Snapshot has more resources than this world.");
        }

        public void Dispose()
        {
            foreach (var resource in m_ResourceList)
                (resource as IDisposable)?.Dispose();
            m_ResourceList.Clear();
            m_ResourceKeys.Clear();
            m_Resources.Clear();
            foreach (var table in m_Tables)
                table.Dispose();
            m_Tables.Clear();
            m_TablesByKey.Clear();
            Registry.Dispose();
        }
    }

    /// <summary>Deferred entity destruction requested from jobs or main-thread systems.</summary>
    public sealed class DestroyQueue : IDisposable, IResettableResource, IJobData, ISnapshotResource
    {
        ParallelQueue<EntityHandle> m_Queue;

        public DestroyQueue(int capacity)
        {
            m_Queue = new ParallelQueue<EntityHandle>(capacity, Allocator.Persistent);
        }

        /// <summary>Destroy requests dropped because the queue was full (cumulative).</summary>
        public int TotalOverflow { get; private set; }

        internal ParallelQueue<EntityHandle> Queue => m_Queue;

        public ParallelQueue<EntityHandle>.Writer AsWriter() => m_Queue.AsWriter();

        /// <summary>Main-thread request.</summary>
        public void Request(EntityHandle handle) => m_Queue.TryAdd(handle);

        internal void RecordAndClear()
        {
            TotalOverflow += m_Queue.Overflow;
            m_Queue.Clear();
        }

        public void OnReset()
        {
            m_Queue.Clear();
            TotalOverflow = 0;
        }

        public void WriteSnapshot(System.IO.BinaryWriter writer)
        {
            writer.Write(TotalOverflow);
            m_Queue.WriteSnapshot(writer);
        }

        public void ReadSnapshot(System.IO.BinaryReader reader)
        {
            TotalOverflow = reader.ReadInt32();
            m_Queue.ReadSnapshot(reader);
        }

        public void Dispose() => m_Queue.Dispose();
    }
}
