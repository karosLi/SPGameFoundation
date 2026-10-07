using Unity.Mathematics;

namespace SPF.Presentation
{
    /// <summary>One bounded, presentation-only cursor per view. An already presented interpolation
    /// position cannot move backward within the same authoritative tick. A new tick begins its own
    /// previous/current interval; a session restore/reset revision or explicit discontinuity clears
    /// the cursor. Never writes to a simulation clock or snapshot.</summary>
    public struct MonotonicInterpolation
    {
        uint m_Tick, m_TimelineRevision;
        float m_Alpha;
        bool m_Initialized;
        public float Alpha => m_Alpha;

        public float Resolve(uint tick, uint timelineRevision, float requested, bool discontinuity = false)
        {
            float alpha = math.isfinite(requested) ? math.saturate(requested) : 0f;
            if (!m_Initialized || discontinuity || tick != m_Tick || timelineRevision != m_TimelineRevision)
            {
                m_Initialized = true; m_Tick = tick; m_TimelineRevision = timelineRevision; m_Alpha = alpha;
            }
            else m_Alpha = math.max(m_Alpha, alpha);
            return m_Alpha;
        }

        public void Reset() => this = default;
    }
}
