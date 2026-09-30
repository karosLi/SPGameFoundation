using System;
using SPF.Contracts;
using SPF.Runtime.Scheduling;
using Unity.Collections;
using Unity.Jobs;

namespace SnakeFoundation
{
    public enum ReplayMode
    {
        Off = 0,
        Record = 1,
        Play = 2,
    }

    public struct ReplayFrame
    {
        public PlayerCommand Command;
        public bool Start;
        /// <summary>
        /// Simulation-affecting quality knob in effect this tick. The adaptive quality controller changes
        /// it from frame timings, which are not reproducible, so it is part of the recorded input.
        /// </summary>
        public byte AIDecisionIntervalTicks;
    }

    /// <summary>
    /// Per-tick player input log. With the session seed it reproduces a run exactly (the simulation is
    /// deterministic), for bug reports and regression tests. Preallocated; recording stops when full.
    /// </summary>
    public sealed class ReplayBuffer : IDisposable, IResettableResource
    {
        NativeArray<ReplayFrame> m_Frames;

        public ReplayBuffer(int capacityTicks) => m_Frames = new NativeArray<ReplayFrame>(capacityTicks, Allocator.Persistent);

        public ReplayMode Mode { get; set; }
        public int Length { get; private set; }
        public int Capacity => m_Frames.Length;
        public bool Finished { get; private set; }

        public ReplayFrame this[int tick] => m_Frames[tick];

        internal void Write(int tick, in ReplayFrame frame)
        {
            if (tick < 0 || tick >= m_Frames.Length) return;
            m_Frames[tick] = frame;
            if (tick + 1 > Length) Length = tick + 1;
        }

        internal bool TryRead(int tick, out ReplayFrame frame)
        {
            if (tick >= 0 && tick < Length)
            {
                frame = m_Frames[tick];
                return true;
            }
            frame = default;
            Finished = true;
            return false;
        }

        /// <summary>Copies frames from another buffer (e.g. a saved recording) and switches to playback.</summary>
        public void LoadFrom(ReplayBuffer source)
        {
            int n = Math.Min(source.Length, m_Frames.Length);
            for (int i = 0; i < n; i++) m_Frames[i] = source.m_Frames[i];
            Length = n;
            Finished = false;
            Mode = ReplayMode.Play;
        }

        public ReplayFrame[] ToArray()
        {
            var frames = new ReplayFrame[Length];
            for (int i = 0; i < Length; i++) frames[i] = m_Frames[i];
            return frames;
        }

        public void Load(ReplayFrame[] frames)
        {
            int n = Math.Min(frames.Length, m_Frames.Length);
            for (int i = 0; i < n; i++) m_Frames[i] = frames[i];
            Length = n;
            Finished = false;
            Mode = ReplayMode.Play;
        }

        public void OnReset()
        {
            if (Mode == ReplayMode.Record) Length = 0;
            Finished = false;
        }

        public void Dispose()
        {
            if (m_Frames.IsCreated) m_Frames.Dispose();
        }
    }

    namespace Systems
    {
        /// <summary>ApplyCommands (first): records or replays the player's command and start requests.</summary>
        sealed class ReplaySystem : SimSystemBase
        {
            public override SimPhase Phase => SimPhase.ApplyCommands;
            public override int Order => -10;
            public override void Declare(AccessDeclaration access) { }

            public override JobHandle OnTick(in SimContext context, JobHandle dependency)
            {
                var replay = context.World.Resource(SnakeKeys.Replay);
                var game = context.World.Resource(SnakeKeys.Game);
                var quality = context.World.Resource(SnakeKeys.Quality);
                int tick = (int)context.Time.Tick;
                switch (replay.Mode)
                {
                    case ReplayMode.Record:
                        replay.Write(tick, new ReplayFrame
                        {
                            Command = game.Command,
                            Start = game.StartRequested,
                            AIDecisionIntervalTicks = (byte)System.Math.Clamp(quality.AIDecisionIntervalTicks, 1, 255),
                        });
                        break;
                    case ReplayMode.Play:
                        if (replay.TryRead(tick, out var frame))
                        {
                            game.Command = frame.Command;
                            if (frame.Start) game.RequestStart();
                            quality.AIDecisionIntervalTicks = System.Math.Max(1, (int)frame.AIDecisionIntervalTicks);
                        }
                        break;
                }
                return dependency;
            }
        }
    }
}
