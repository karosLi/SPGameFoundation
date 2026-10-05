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

        int m_RequestedTicks;

        /// <summary>
        /// Turn-based / event-driven games (puzzles, card and board games): the clock does not advance with
        /// time; <see cref="Update"/> only runs ticks asked for with <see cref="RequestTicks"/> (e.g. one per
        /// player move), still through the normal (overlapped) scheduling.
        /// </summary>
        public bool ManualClock { get; set; }

        /// <summary>With <see cref="ManualClock"/>: runs this many more ticks at the next updates.</summary>
        public void RequestTicks(int ticks = 1) => m_RequestedTicks += System.Math.Max(ticks, 0);

        public int PendingTicks => m_RequestedTicks;

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

            int ticks;
            if (ManualClock)
            {
                ticks = System.Math.Min(m_RequestedTicks, m_Clock.MaxTicksPerFrame);
                m_RequestedTicks -= ticks;
            }
            else ticks = m_Clock.Advance(deltaSeconds);
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

        /// <summary>
        /// Writes the whole simulation (clock position, world, system state) after completing the tick in
        /// flight. Restoring it into a session built from the same mode and seed continues identically.
        /// </summary>
        public void WriteSnapshot(System.IO.BinaryWriter writer)
        {
            ThrowIfDisposed();
            Pipeline.EndTick();
            writer.Write(m_Clock.NextTickIndex);
            writer.Write(m_Clock.Elapsed);
            World.WriteSnapshot(writer);
            Pipeline.WriteSnapshot(writer);
        }

        /// <summary>
        /// Restores a snapshot (see <see cref="WriteSnapshot"/>). On invalid data it throws and the session
        /// is restarted, so it is never left half-restored. The session state (running / paused) is kept.
        /// </summary>
        public void ReadSnapshot(System.IO.BinaryReader reader)
        {
            ThrowIfDisposed();
            Pipeline.EndTick();
            try
            {
                uint tick = reader.ReadUInt32();
                double elapsed = reader.ReadDouble();
                World.ReadSnapshot(reader);
                Pipeline.ReadSnapshot(reader);
                m_Clock.Restore(tick, elapsed);
            }
            catch
            {
                var state = State;
                Restart();
                State = state;
                throw;
            }
        }

        public byte[] CaptureSnapshot()
        {
            using var buffer = new System.IO.MemoryStream();
            using (var writer = new System.IO.BinaryWriter(buffer))
                WriteSnapshot(writer);
            return buffer.ToArray();
        }

        public void RestoreSnapshot(byte[] snapshot)
        {
            using var reader = new System.IO.BinaryReader(new System.IO.MemoryStream(snapshot));
            ReadSnapshot(reader);
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
