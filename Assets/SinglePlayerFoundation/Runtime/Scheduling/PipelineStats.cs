using System;
using System.Diagnostics;
using SPF.Contracts;

namespace SPF.Runtime.Scheduling
{
    /// <summary>
    /// Main-thread timings of the pipeline, smoothed with an exponential moving average.
    /// Worker-thread time of individual jobs is visible in the Unity Profiler via the SPF.* markers.
    /// </summary>
    public sealed class PipelineStats
    {
        const float Smoothing = 0.1f;
        static readonly double s_TicksToMs = 1000.0 / Stopwatch.Frequency;

        readonly string[] m_SystemNames;
        readonly float[] m_SystemScheduleMs;
        readonly float[] m_PhaseScheduleMs = new float[SimPhases.Count];
        readonly float[] m_PhaseAccumulator = new float[SimPhases.Count];

        public PipelineStats(string[] systemNames)
        {
            m_SystemNames = systemNames;
            m_SystemScheduleMs = new float[systemNames.Length];
        }

        /// <summary>Main-thread cost of BeginTick (playback + scheduling).</summary>
        public float ScheduleMs { get; private set; }

        /// <summary>Time EndTick blocked waiting for jobs; high values mean workers are the bottleneck.</summary>
        public float SyncWaitMs { get; private set; }

        /// <summary>Wall time from BeginTick to the end of EndTick.</summary>
        public float TickWallMs { get; private set; }

        public long TickCount { get; private set; }

        public int SystemCount => m_SystemNames.Length;
        public string SystemName(int index) => m_SystemNames[index];
        public float SystemScheduleMs(int index) => m_SystemScheduleMs[index];
        public float PhaseScheduleMs(SimPhase phase) => m_PhaseScheduleMs[(int)phase];

        internal void RecordSchedule(int systemIndex, SimPhase phase, long elapsedTicks)
        {
            float ms = (float)(elapsedTicks * s_TicksToMs);
            m_SystemScheduleMs[systemIndex] = Smooth(m_SystemScheduleMs[systemIndex], ms);
            m_PhaseAccumulator[(int)phase] += ms;
        }

        internal void RecordScheduleTotal(long elapsedTicks)
        {
            ScheduleMs = Smooth(ScheduleMs, (float)(elapsedTicks * s_TicksToMs));
            for (int i = 0; i < SimPhases.Count; i++)
            {
                m_PhaseScheduleMs[i] = Smooth(m_PhaseScheduleMs[i], m_PhaseAccumulator[i]);
                m_PhaseAccumulator[i] = 0f;
            }
        }

        internal void RecordSync(long waitTicks, long wallTicks)
        {
            SyncWaitMs = Smooth(SyncWaitMs, (float)(waitTicks * s_TicksToMs));
            TickWallMs = Smooth(TickWallMs, (float)(wallTicks * s_TicksToMs));
            TickCount++;
        }

        internal void Reset()
        {
            Array.Clear(m_SystemScheduleMs, 0, m_SystemScheduleMs.Length);
            Array.Clear(m_PhaseScheduleMs, 0, m_PhaseScheduleMs.Length);
            Array.Clear(m_PhaseAccumulator, 0, m_PhaseAccumulator.Length);
            ScheduleMs = SyncWaitMs = TickWallMs = 0f;
            TickCount = 0;
        }

        float Smooth(float previous, float sample) => TickCount == 0 ? sample : previous + (sample - previous) * Smoothing;
    }
}
