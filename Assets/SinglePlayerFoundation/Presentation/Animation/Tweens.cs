using System.Collections.Generic;
using Unity.Mathematics;

namespace SPF.Presentation.Animation
{
    public enum Ease : byte { Linear, InQuad, OutQuad, InOutQuad, OutCubic, OutBack, OutBounce, OutElastic }

    public static class Easing
    {
        public static float Apply(Ease ease, float t)
        {
            t = math.saturate(t);
            switch (ease)
            {
                case Ease.InQuad: return t * t;
                case Ease.OutQuad: return 1f - (1f - t) * (1f - t);
                case Ease.InOutQuad: return t < 0.5f ? 2f * t * t : 1f - 2f * (1f - t) * (1f - t);
                case Ease.OutCubic: { float u = 1f - t; return 1f - u * u * u; }
                case Ease.OutBack: { const float c1 = 1.70158f, c3 = c1 + 1f; float u = t - 1f; return 1f + c3 * u * u * u + c1 * u * u; }
                case Ease.OutBounce:
                {
                    const float n = 7.5625f, d = 2.75f;
                    if (t < 1f / d) return n * t * t;
                    if (t < 2f / d) { t -= 1.5f / d; return n * t * t + 0.75f; }
                    if (t < 2.5f / d) { t -= 2.25f / d; return n * t * t + 0.9375f; }
                    t -= 2.625f / d; return n * t * t + 0.984375f;
                }
                case Ease.OutElastic:
                    return t <= 0f ? 0f : t >= 1f ? 1f : math.pow(2f, -10f * t) * math.sin((t * 10f - 0.75f) * (2f * math.PI / 3f)) + 1f;
                default: return t;
            }
        }
    }

    /// <summary>
    /// Presentation animation of game objects identified by an int (gem ids, cards, UI items): each
    /// (target, channel) holds a value (position, scale, alpha...) driven by queued tweens with delays and
    /// easing; finished tweens hold their end value. <see cref="Busy"/> tells input layers to wait (turn-based
    /// games play a move's animation before accepting the next). The simulation never depends on it.
    /// </summary>
    public sealed class TweenPlayer
    {
        struct Tween
        {
            public float2 From, To;
            public float Start, Duration;
            public Ease Ease;
            public bool FromCurrent;   // start from the value at its start time (chains)
        }

        readonly Dictionary<long, List<Tween>> m_Tweens = new Dictionary<long, List<Tween>>();
        readonly Dictionary<long, float2> m_Values = new Dictionary<long, float2>();
        readonly List<long> m_Keys = new List<long>();

        /// <summary>Animation clock (seconds).</summary>
        public float Time { get; private set; }

        /// <summary>Latest end time of any queued tween.</summary>
        public float EndTime { get; private set; }

        public bool Busy => Time < EndTime;

        static long Key(int target, int channel) => ((long)target << 8) | (uint)(channel & 0xFF);

        public void Set(int target, int channel, float2 value)
        {
            long key = Key(target, channel);
            m_Values[key] = value;
            if (m_Tweens.TryGetValue(key, out var list)) list.Clear();
        }

        /// <summary>Queues a tween to <paramref name="to"/>; starts <paramref name="delay"/> seconds from now. Returns its end time (relative).</summary>
        public float To(int target, int channel, float2 to, float duration, float delay = 0f, Ease ease = Ease.OutQuad)
        {
            long key = Key(target, channel);
            if (!m_Tweens.TryGetValue(key, out var list)) { list = new List<Tween>(); m_Tweens[key] = list; m_Keys.Add(key); }
            var tween = new Tween { To = to, Start = Time + math.max(delay, 0f), Duration = math.max(duration, 1e-4f), Ease = ease, FromCurrent = true };
            list.Add(tween);
            EndTime = math.max(EndTime, tween.Start + tween.Duration);
            return delay + duration;
        }

        public bool TryGet(int target, int channel, out float2 value) => m_Values.TryGetValue(Key(target, channel), out value);

        public float2 Get(int target, int channel, float2 fallback) => m_Values.TryGetValue(Key(target, channel), out var v) ? v : fallback;

        public void Advance(float dt)
        {
            Time += math.max(dt, 0f);
            for (int k = 0; k < m_Keys.Count; k++)
            {
                long key = m_Keys[k];
                var list = m_Tweens[key];
                if (list.Count == 0) continue;
                float2 value = m_Values.TryGetValue(key, out var v) ? v : float2.zero;
                int done = 0;
                for (int i = 0; i < list.Count; i++)
                {
                    var t = list[i];
                    if (Time < t.Start) break;   // later tweens wait for this one
                    if (t.FromCurrent) { t.From = value; t.FromCurrent = false; list[i] = t; }
                    float p = (Time - t.Start) / t.Duration;
                    value = math.lerp(t.From, t.To, Easing.Apply(t.Ease, p));
                    if (p >= 1f) { value = t.To; done++; } else break;
                }
                if (done > 0) list.RemoveRange(0, done);
                m_Values[key] = value;
            }
        }

        /// <summary>Jumps every tween to its end (skip animations).</summary>
        public void Finish() => Advance(math.max(EndTime - Time, 0f) + 1e-3f);

        public void Remove(int target, int channel)
        {
            long key = Key(target, channel);
            m_Values.Remove(key);
            if (m_Tweens.TryGetValue(key, out var list)) list.Clear();
        }

        public void Clear()
        {
            m_Tweens.Clear();
            m_Values.Clear();
            m_Keys.Clear();
            EndTime = Time;
        }
    }
}
