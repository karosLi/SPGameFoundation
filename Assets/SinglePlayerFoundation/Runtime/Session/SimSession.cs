using System;
using System.Collections.Generic;
using SPF.Runtime.Composition;
using SPF.Runtime.Scheduling;
using SPF.Runtime.World;

namespace SPF.Runtime.Session
{
    public enum SessionState
    {
        Created,
        Running,
        Paused,
        Disposed,
    }

    /// <summary>
    /// One play session: world + pipeline + fixed-step clock. Drive it with <see cref="Update"/> every
    /// frame and call <see cref="Sync"/> before presentation reads snapshots (see SessionHost).
    /// </summary>
    public sealed class SimSession : IDisposable
    {
        readonly FixedStepClock m_Clock;

        public SimWorld World { get; }
        public TickPipeline Pipeline { get; }
        public SessionState State { get; private set; }
        public FixedStepClock Clock => m_Clock;

        /// <summary>Ticks simulated during the last Update call.</summary>
        public int TicksLastFrame { get; private set; }

        /// <summary>Snapshot interpolation factor for presentation.</summary>
        public float InterpolationAlpha => m_Clock.Alpha;

        public SimSession(IReadOnlyList<IGameplayModule> modules, SessionSettings settings, uint seed)
        {
            World = WorldComposer.BuildWorld(modules, settings, seed);
            try
            {
                Pipeline = WorldComposer.BuildPipeline(modules, World);
            }
            catch
            {
                World.Dispose();
                throw;
            }
            m_Clock = new FixedStepClock(settings.TickRate, settings.MaxTicksPerFrame);
            State = SessionState.Created;
        }

        public static SimSession Create(ModeDefinition mode, uint seed)
        {
            var modules = new List<IGameplayModule>(mode.Modules.Count);
            foreach (var module in mode.Modules)
                modules.Add(module);
            return new SimSession(modules, mode.Settings, seed);
        }

        public void Start()
        {
            ThrowIfDisposed();
            State = SessionState.Running;
        }

        public void Pause()
        {
            if (State != SessionState.Running) return;
            Pipeline.EndTick();
            State = SessionState.Paused;
        }

        public void Resume()
        {
            if (State == SessionState.Paused) State = SessionState.Running;
        }

        /// <summary>
        /// Advances the clock and runs the due ticks. The last tick is left in flight so its jobs
        /// overlap other main-thread work until <see cref="Sync"/>.
        /// </summary>
        public void Update(float deltaSeconds)
        {
            TicksLastFrame = 0;
            if (State != SessionState.Running)
                return;

            int ticks = m_Clock.Advance(deltaSeconds);
            for (int i = 0; i < ticks; i++)
            {
                Pipeline.EndTick();
                Pipeline.BeginTick(m_Clock.NextTick());
            }
            TicksLastFrame = ticks;
        }

        /// <summary>Completes the in-flight tick; snapshots are readable afterwards.</summary>
        public void Sync() => Pipeline.EndTick();

        /// <summary>Runs exactly one complete tick, ignoring the clock (tests, replays, fast-forward).</summary>
        public void Step()
        {
            ThrowIfDisposed();
            Pipeline.EndTick();
            Pipeline.BeginTick(m_Clock.NextTick());
            Pipeline.EndTick();
        }

        /// <summary>Returns to the initial state reusing all memory.</summary>
        public void Restart()
        {
            ThrowIfDisposed();
            Pipeline.Reset();
            World.Reset();
            m_Clock.Reset();
            State = SessionState.Running;
        }

        public void Dispose()
        {
            if (State == SessionState.Disposed) return;
            Pipeline.Dispose();
            World.Dispose();
            State = SessionState.Disposed;
        }

        void ThrowIfDisposed()
        {
            if (State == SessionState.Disposed)
                throw new ObjectDisposedException(nameof(SimSession));
        }
    }
}
