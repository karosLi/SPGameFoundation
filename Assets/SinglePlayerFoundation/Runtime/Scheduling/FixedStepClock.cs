using System;
using SPF.Contracts;

namespace SPF.Runtime.Scheduling
{
    /// <summary>
    /// Converts variable frame time into a whole number of fixed simulation ticks. Frames that would
    /// need more than MaxTicksPerFrame ticks drop the excess (the game slows down instead of spiralling).
    /// </summary>
    public sealed class FixedStepClock
    {
        double m_Accumulator;
        uint m_NextTick;
        double m_Elapsed;

        public FixedStepClock(int tickRate, int maxTicksPerFrame)
        {
            if (tickRate <= 0) throw new ArgumentOutOfRangeException(nameof(tickRate));
            if (maxTicksPerFrame <= 0) throw new ArgumentOutOfRangeException(nameof(maxTicksPerFrame));
            TickRate = tickRate;
            StepSeconds = 1.0 / tickRate;
            MaxTicksPerFrame = maxTicksPerFrame;
        }

        public int TickRate { get; }
        public double StepSeconds { get; }
        public int MaxTicksPerFrame { get; }

        /// <summary>Interpolation factor between the previous and current snapshot, in [0, 1).</summary>
        public float Alpha => (float)(m_Accumulator / StepSeconds);

        /// <summary>Ticks dropped because a frame took too long (cumulative).</summary>
        public long DroppedTicks { get; private set; }

        /// <summary>Adds frame time; returns how many ticks to run now.</summary>
        public int Advance(float deltaSeconds)
        {
            if (deltaSeconds > 0f)
                m_Accumulator += deltaSeconds;

            int ticks = (int)(m_Accumulator / StepSeconds);
            if (ticks > MaxTicksPerFrame)
            {
                DroppedTicks += ticks - MaxTicksPerFrame;
                ticks = MaxTicksPerFrame;
                m_Accumulator = Math.Min(m_Accumulator - ticks * StepSeconds, StepSeconds * 0.999);
            }
            else
            {
                m_Accumulator -= ticks * StepSeconds;
            }
            return ticks;
        }

        /// <summary>Time of the next tick to simulate; advances the tick counter.</summary>
        public TickTime NextTick()
        {
            var time = new TickTime(m_NextTick, (float)StepSeconds, m_Elapsed);
            m_NextTick++;
            m_Elapsed += StepSeconds;
            return time;
        }

        /// <summary>Index of the next tick to simulate.</summary>
        public uint NextTickIndex => m_NextTick;

        /// <summary>Simulated time at the start of the next tick.</summary>
        public double Elapsed => m_Elapsed;

        /// <summary>Continues from a saved position (snapshot restore); pending frame time is dropped.</summary>
        public void Restore(uint nextTick, double elapsed)
        {
            m_Accumulator = 0;
            m_NextTick = nextTick;
            m_Elapsed = elapsed;
        }

        public void Reset()
        {
            m_Accumulator = 0;
            m_NextTick = 0;
            m_Elapsed = 0;
            DroppedTicks = 0;
        }
    }
}
