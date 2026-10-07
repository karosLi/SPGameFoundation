using UnityEngine;

namespace SPF.Presentation.Audio
{
    /// <summary>Coalesces one audible impact per presented frame across the legacy feedback and
    /// weapon-cue paths. Heavy wins; no ambiguous event-position/source matching or second drain.
    /// HitNumbers may be disabled: weapon impacts and legacy deaths still request this same budget.</summary>
    public sealed class ImpactAudioBudget
    {
        int m_Frame = int.MinValue;
        bool m_Pending, m_Emitted, m_Heavy;
        Vector2 m_At;
        float m_Gain;
        public int Merged { get; private set; }
        public void Request(int frame, bool heavy, Vector2 at, float audibleGain = 1)
        {
            if (float.IsNaN(audibleGain) || float.IsInfinity(audibleGain) || audibleGain <= 0) return;
            if (frame != m_Frame) { m_Frame = frame; m_Pending = m_Emitted = m_Heavy = false; }
            if (m_Emitted) { Merged++; return; }
            if (m_Pending) Merged++;
            if (!m_Pending || (heavy && !m_Heavy) || (heavy == m_Heavy && audibleGain > m_Gain)) { m_At = at; m_Gain = audibleGain; }
            m_Heavy |= heavy; m_Pending = true;
        }
        public bool TryTake(int frame, out bool heavy, out Vector2 at)
        {
            heavy = m_Heavy; at = m_At;
            if (frame != m_Frame || !m_Pending || m_Emitted) return false;
            m_Pending = false; m_Emitted = true; return true;
        }
        public void Reset() { m_Frame = int.MinValue; m_Pending = m_Emitted = m_Heavy = false; }
    }
}
