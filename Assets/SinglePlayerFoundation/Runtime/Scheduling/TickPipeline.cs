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

        public bool HasPendingTick { get; private set; }
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

            foreach (var entry in m_Systems)
                entry.System.OnCreate(world);
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
            if (HasPendingTick)
                throw new InvalidOperationException("EndTick must be called before the next BeginTick.");

            m_BeginTimestamp = Stopwatch.GetTimestamp();
            s_BeginMarker.Begin();

            s_PlaybackMarker.Begin();
            m_World.PlaybackDestroys();
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
                var handle = entry.System.OnTick(context, dependency);
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

            s_EndMarker.Begin();
            long waitStart = Stopwatch.GetTimestamp();
            m_Pending.Complete();
            long waitEnd = Stopwatch.GetTimestamp();
            m_Pending = default;
            m_Tracker.Reset();
            m_World.Sync();
            HasPendingTick = false;
            s_EndMarker.End();

            m_Stats.RecordSync(waitEnd - waitStart, Stopwatch.GetTimestamp() - m_BeginTimestamp);
        }

        /// <summary>Completes outstanding work and resets systems that support it.</summary>
        public void Reset()
        {
            EndTick();
            foreach (var entry in m_Systems)
                (entry.System as IResettableSystem)?.OnReset(m_World);
            m_Stats.Reset();
        }

        public void Dispose()
        {
            EndTick();
            for (int i = m_Systems.Length - 1; i >= 0; i--)
                m_Systems[i].System.OnDestroy(m_World);
        }

        struct SystemEntry
        {
            public readonly ISimSystem System;
            public readonly AccessDeclaration Access;
            public readonly int RegistrationIndex;
            public readonly string Name;
            public ProfilerMarker Marker;

            public SystemEntry(ISimSystem system, AccessDeclaration access, int registrationIndex)
            {
                System = system;
                Access = access;
                RegistrationIndex = registrationIndex;
                Name = system.GetType().Name;
                Marker = new ProfilerMarker("SPF." + Name);
            }
        }
    }
}
