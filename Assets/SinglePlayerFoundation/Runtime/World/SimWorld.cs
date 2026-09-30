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
        readonly List<ISyncResource> m_SyncResources = new List<ISyncResource>();
        readonly List<IResettableResource> m_ResettableResources = new List<IResettableResource>();
        readonly DestroyQueue m_DestroyQueue;

        public EntityRegistry Registry { get; }
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
                var table = new SimTable(spec.Key, m_Tables.Count, spec.Capacity);
                foreach (var addColumn in spec.ColumnFactories)
                    addColumn(table);
                if (spec.TrackChanges)
                    table.EnableChangeTracking();
                m_Tables.Add(table);
                m_TablesByKey.Add(spec.Key.Id, table);
                totalCapacity += spec.Capacity;
            }
            Registry = new EntityRegistry(Math.Max(1, totalCapacity));

            m_DestroyQueue = new DestroyQueue(layout.DestroyQueueCapacity);
            AddResource(DestroyQueueKey, m_DestroyQueue);
            foreach (var (key, resource) in layout.Resources)
                AddResource(key, resource);
        }

        void AddResource(AccessKey key, object resource)
        {
            m_Resources.Add(key.Id, resource);
            m_ResourceList.Add(resource);
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
            return (T)resource;
        }

        public bool HasResource(AccessKey key) => m_Resources.ContainsKey(key.Id);

        // ---- Structural changes: main thread, ApplyCommands phase only. ----

        /// <summary>Creates an entity with a zeroed row. Returns Null (and counts a failure) when full.</summary>
        public EntityHandle CreateEntity(TableKey key, out int row)
        {
            var table = Table(key);
            row = -1;
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

        /// <summary>Applies queued destroys. Stale or duplicate handles are ignored.</summary>
        internal void PlaybackDestroys()
        {
            var queue = m_DestroyQueue.Queue;
            int count = queue.Count;
            for (int i = 0; i < count; i++)
                DestroyEntity(queue[i]);
            m_DestroyQueue.RecordAndClear();
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

        public void Dispose()
        {
            foreach (var resource in m_ResourceList)
                (resource as IDisposable)?.Dispose();
            m_ResourceList.Clear();
            m_Resources.Clear();
            foreach (var table in m_Tables)
                table.Dispose();
            m_Tables.Clear();
            m_TablesByKey.Clear();
            Registry.Dispose();
        }
    }

    /// <summary>Deferred entity destruction requested from jobs or main-thread systems.</summary>
    public sealed class DestroyQueue : IDisposable, IResettableResource
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

        public void Dispose() => m_Queue.Dispose();
    }
}
