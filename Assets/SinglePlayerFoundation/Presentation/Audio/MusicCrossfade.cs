using System;

namespace SPF.Presentation.Audio
{
    /// <summary>Two bounded lanes, constant-sum ramps. Retargets from current gains; repeated requests
    /// do not restart playback/fades. A third track replaces the quieter lane, never creates a third.</summary>
    public sealed class MusicCrossfade
    {
        readonly int[] m_Tracks = { -1, -1 };
        readonly float[] m_Gains = new float[2];
        readonly float[] m_Start = new float[2];
        readonly float[] m_Target = new float[2];
        float m_Elapsed, m_Duration;
        public int Requested { get; private set; } = -1;
        public int Track(int lane) => m_Tracks[lane];
        public float Gain(int lane) => m_Gains[lane];
        public bool Transitioning => m_Elapsed < m_Duration;
        public bool Request(int track, float seconds)
        {
            if (track < -1 || !Finite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
            if (Requested == track) return false;
            Requested = track;
            int lane = -1;
            if (track >= 0)
            {
                if (m_Tracks[0] == track) lane = 0;
                else if (m_Tracks[1] == track) lane = 1;
                else
                {
                    lane = m_Tracks[0] < 0 ? 0 : m_Tracks[1] < 0 ? 1 : m_Gains[0] <= m_Gains[1] ? 0 : 1;
                    m_Tracks[lane] = track;
                    m_Gains[lane] = 0f;
                }
            }
            for (int i = 0; i < 2; i++) { m_Start[i] = m_Gains[i]; m_Target[i] = i == lane ? 1f : 0f; }
            m_Duration = seconds; m_Elapsed = 0;
            if (seconds == 0) Advance(0);
            return true;
        }
        public void Advance(float dt)
        {
            if (!Finite(dt) || dt < 0) throw new ArgumentOutOfRangeException(nameof(dt));
            m_Elapsed = Math.Min(m_Duration, m_Elapsed + dt);
            float t = m_Duration > 0 ? m_Elapsed / m_Duration : 1;
            // Smoothstep has zero slope at each end while retaining the current gain on retarget.
            t = t * t * (3f - 2f * t);
            for (int i = 0; i < 2; i++)
            {
                m_Gains[i] = m_Start[i] + (m_Target[i] - m_Start[i]) * t;
                if (m_Elapsed >= m_Duration && m_Target[i] == 0) m_Tracks[i] = -1;
            }
        }
        public void Clear()
        {
            Requested = -1; m_Duration = m_Elapsed = 0;
            for (int i = 0; i < 2; i++) { m_Tracks[i] = -1; m_Gains[i] = m_Start[i] = m_Target[i] = 0; }
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
