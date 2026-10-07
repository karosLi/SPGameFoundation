using System;
using System.Collections.Generic;
using System.Diagnostics;
using SPF.Contracts;
using SPF.Runtime.World;
using Unity.Jobs;
using Unity.Profiling;

namespace SPF.Runtime.Scheduling
{
    /// <summary>
    /// Runs one simulation tick as a single job graph: <see cref="BeginTick"/> applies deferred
    /// structural changes and schedules every system in phase order; <see cref="EndTick"/> is the only
    /// sync point (Complete + resource sync). Between the two the main thread is free.
    /// </summary>
    public sealed class TickPipeline : IDisposable
    {
        static readonly ProfilerMarker s_BeginMarker = new ProfilerMarker("SPF.Tick.Schedule");
        static readonly ProfilerMarker s_EndMarker = new ProfilerMarker("SPF.Tick.Sync");
        static readonly ProfilerMarker s_PlaybackMarker = new ProfilerMarker("SPF.Tick.PlaybackDestroys");

        readonly SimWorld m_World;
        readonly SystemEntry[] m_Systems;
        readonly DependencyTracker m_Tracker;
        readonly PipelineStats m_Stats;
        JobHandle m_Pending;
        long m_BeginTimestamp;
        int m_InitializedSystems;
        bool m_Disposed;
        bool m_Disposing;
        bool m_EndingTick;

        public bool HasPendingTick { get; private set; }
        /// <summary>True after all initialized systems have received their final cleanup attempt.</summary>
        public bool IsDisposed => m_Disposed && !m_Disposing;
        public PipelineStats Stats => m_Stats;
        public TickTime LastTickTime { get; private set; }

        public TickPipeline(SimWorld world, IReadOnlyList<ISimSystem> systems)
        {
            m_World = world;

            var entries = new List<SystemEntry>(systems.Count);
            int maxKey = SimWorld.DestroyQueueKey.Id;
            for (int i = 0; i < systems.Count; i++)
            {
                var system = systems[i] ?? throw new ArgumentNullException(nameof(systems), $"System #{i} is null.");
                var access = new AccessDeclaration();
                system.Declare(access);
                maxKey = Math.Max(maxKey, access.MaxId);
                entries.Add(new SystemEntry(system, access, i));
            }
            // Stable sort: phase, then order, then registration order.
            entries.Sort((a, b) =>
            {
                int c = ((int)a.System.Phase).CompareTo((int)b.System.Phase);
                if (c != 0) return c;
                c = a.System.Order.CompareTo(b.System.Order);
                return c != 0 ? c : a.RegistrationIndex.CompareTo(b.RegistrationIndex);
            });
            m_Systems = entries.ToArray();
            m_Tracker = new DependencyTracker(maxKey + 1);

            var names = new string[m_Systems.Length];
            for (int i = 0; i < m_Systems.Length; i++)
                names[i] = m_Systems[i].Name;
            m_Stats = new PipelineStats(names);

            try
            {
                foreach (var entry in m_Systems)
                {
                    entry.System.OnCreate(world);
                    m_InitializedSystems++;
                }
            }
            catch (Exception failure)
            {
                m_Disposed = true;
                DestroyInitialized(ref failure);
                throw;
            }
        }

        /// <summary>
        /// Diagnostics only: completes each system's jobs right after scheduling it so
        /// <see cref="PipelineStats.SystemExecuteMs"/> gets per-system execution time. This removes
        /// overlap between systems, so tick time is higher than in normal (pipelined) operation.
        /// </summary>
        public bool SerialProfiling { get; set; }

        public int SystemCount => m_Systems.Length;
        public ISimSystem GetSystem(int index) => m_Systems[index].System;
        public IReadOnlyList<string> GetAccessNames(int index) => m_Systems[index].Access.Names;

        public void BeginTick(TickTime time)
        {
            ThrowIfDisposed();
            if (HasPendingTick)
                throw new InvalidOperationException("EndTick must be called before the next BeginTick.");

            m_BeginTimestamp = Stopwatch.GetTimestamp();
            s_BeginMarker.Begin();

            s_PlaybackMarker.Begin();
            m_World.PlaybackDestroys();
            m_World.CompactPools();
            s_PlaybackMarker.End();

            var context = new SimContext(m_World, time);
            try
            {
                ScheduleSystems(context);
            }
            catch
            {
                // A system threw mid-schedule: finish what was already scheduled so no job is left running
                // unowned (it would make every later access to its data throw), then report the error.
                m_Tracker.All.Complete();
                m_Tracker.Reset();
                m_World.Sync();
                s_BeginMarker.End();
                throw;
            }

            m_Pending = m_Tracker.All;
            JobHandle.ScheduleBatchedJobs();
            HasPendingTick = true;
            LastTickTime = time;

            s_BeginMarker.End();
            m_Stats.RecordScheduleTotal(Stopwatch.GetTimestamp() - m_BeginTimestamp);
        }

        void ScheduleSystems(in SimContext context)
        {
            for (int i = 0; i < m_Systems.Length; i++)
            {
                ref var entry = ref m_Systems[i];
                long start = Stopwatch.GetTimestamp();
                entry.Marker.Begin();
                var dependency = m_Tracker.GetDependency(entry.Access);
                // A system that declares nothing is a barrier, typically main-thread work (spawning,
                // rules, flow) that touches arbitrary data directly: finish everything scheduled before it
                // so it never reads a container a job is still writing.
                if (entry.Access.IsBarrier)
                    dependency.Complete();
                JobHandle handle;
                m_World.Guard.Begin(entry.Allowed, entry.Name);
                try { handle = entry.System.OnTick(context, dependency); }
                finally { m_World.Guard.End(); }
                if (SerialProfiling)
                {
                    // Attribute worker time to this system: its jobs (still internally parallel) run now.
                    handle.Complete();
                    m_Stats.RecordExecute(i, Stopwatch.GetTimestamp() - start);
                }
                m_Tracker.Record(entry.Access, handle);
                entry.Marker.End();
                m_Stats.RecordSchedule(i, entry.System.Phase, Stopwatch.GetTimestamp() - start);
            }
        }

        public void EndTick()
        {
            if (!HasPendingTick)
                return;

            if (m_EndingTick) throw new InvalidOperationException("EndTick cannot be reentered during resource sync.");
            m_EndingTick = true;
            s_EndMarker.Begin();
            long waitStart = Stopwatch.GetTimestamp();
            try
            {
                m_Pending.Complete();
                long waitEnd = Stopwatch.GetTimestamp();
                m_Pending = default;
                m_Tracker.Reset();
                try
                {
                    m_World.Sync();
                    m_Stats.RecordSync(waitEnd - waitStart, Stopwatch.GetTimestamp() - m_BeginTimestamp);
                }
                // Keep BeginTick blocked throughout OnSync, but distinguish callback failure from
                // failed job completion so final cleanup can safely release native storage.
                finally { HasPendingTick = false; }
            }
            finally { m_EndingTick = false; s_EndMarker.End(); }
        }

        /// <summary>Completes outstanding work and resets systems that support it.</summary>
        public void Reset()
        {
            ThrowIfDisposed();
            EndTick();
            foreach (var entry in m_Systems)
                (entry.System as IResettableSystem)?.OnReset(m_World);
            m_Stats.Reset();
        }

        /// <summary>State of every <see cref="ISnapshotSystem"/> (between ticks).</summary>
        public void WriteSnapshot(System.IO.BinaryWriter writer)
        {
            ThrowIfDisposed();
            EndTick();
            foreach (var entry in m_Systems)
            {
                if (!(entry.System is ISnapshotSystem system)) continue;
                writer.Write(entry.Name);
                system.WriteSnapshot(writer);
                NativeIO.WriteMarker(writer, entry.Name);
            }
            writer.Write("");
        }

        public void ReadSnapshot(System.IO.BinaryReader reader)
        {
            ThrowIfDisposed();
            EndTick();
            foreach (var entry in m_Systems)
            {
                if (!(entry.System is ISnapshotSystem system)) continue;
                if (reader.ReadString() != entry.Name)
                    throw new System.IO.InvalidDataException($"Snapshot system order differs at {entry.Name}.");
                system.ReadSnapshot(reader, m_World);
                NativeIO.ReadMarker(reader, entry.Name);
            }
            if (reader.ReadString() != "")
                throw new System.IO.InvalidDataException("Snapshot has more system states than this pipeline.");
        }

        public void Dispose()
        {
            if (m_Disposed || m_Disposing) return;
            m_Disposing = true;
            try
            {
                Exception failure = null;
                CleanupErrors.Try(EndTick, ref failure);
                // A failed JobHandle.Complete leaves safety unestablished. Keep ownership for a retry;
                // an OnSync failure after completion does not prevent system/world cleanup.
                if (HasPendingTick) CleanupErrors.ThrowIfAny(failure);
                m_Disposed = true;
                DestroyInitialized(ref failure);
                CleanupErrors.ThrowIfAny(failure);
            }
            finally { m_Disposing = false; }
        }

        void DestroyInitialized(ref Exception failure)
        {
            while (m_InitializedSystems > 0)
            {
                var system = m_Systems[--m_InitializedSystems].System;
                CleanupErrors.Try(() => system.OnDestroy(m_World), ref failure);
            }
        }

        void ThrowIfDisposed()
        {
            if (m_Disposed || m_Disposing) throw new ObjectDisposedException(nameof(TickPipeline));
        }

        struct SystemEntry
        {
            public readonly ISimSystem System;
            public readonly AccessDeclaration Access;
            public readonly int RegistrationIndex;
            public readonly string Name;
            public readonly bool[] Allowed;   // null for barriers (may touch anything)
            public ProfilerMarker Marker;

            public SystemEntry(ISimSystem system, AccessDeclaration access, int registrationIndex)
            {
                System = system;
                Access = access;
                if (access.IsBarrier) Allowed = null;
                else
                {
                    Allowed = new bool[access.MaxId + 1];
                    foreach (int id in access.Reads) Allowed[id] = true;
                    foreach (int id in access.Writes) Allowed[id] = true;
                }
                RegistrationIndex = registrationIndex;
                Name = system.GetType().Name;
                Marker = new ProfilerMarker("SPF." + Name);
            }
        }
    }
}
