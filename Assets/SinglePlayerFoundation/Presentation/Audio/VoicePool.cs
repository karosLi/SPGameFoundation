using System;
using System.Collections.Generic;

namespace SPF.Presentation.Audio
{
    /// <summary>
    /// Voice allocation for a fixed set of audio sources (pure logic, no Unity objects). A sound has a
    /// voice limit and a minimum retrigger interval, so a hundred hits in one frame play once or twice
    /// instead of saturating the mixer. When every voice is busy the oldest voice of no higher priority
    /// is stolen; otherwise the request is rejected.
    /// </summary>
    public sealed class VoicePool
    {
        struct Voice
        {
            public int Sound;
            public float Start, End;
            public int Priority;
        }

        struct Limits
        {
            public int MaxVoices;
            public float MinInterval;
            public float LastPlay;
        }

        readonly Voice[] m_Voices;
        readonly List<Limits> m_Limits = new List<Limits>();

        public VoicePool(int voices)
        {
            if (voices <= 0) throw new ArgumentOutOfRangeException(nameof(voices));
            m_Voices = new Voice[voices];
            for (int i = 0; i < voices; i++) m_Voices[i].Sound = -1;
        }

        public int VoiceCount => m_Voices.Length;
        public int Rejected { get; private set; }
        public int Stolen { get; private set; }

        /// <summary>Adds a sound; returns its id.</summary>
        public int AddSound(int maxVoices, float minInterval)
        {
            m_Limits.Add(new Limits { MaxVoices = Math.Max(1, maxVoices), MinInterval = Math.Max(0f, minInterval), LastPlay = float.NegativeInfinity });
            return m_Limits.Count - 1;
        }

        public int SoundOf(int voice) => m_Voices[voice].Sound;

        public int ActiveVoices(float now)
        {
            int n = 0;
            foreach (var v in m_Voices) if (v.Sound >= 0 && v.End > now) n++;
            return n;
        }

        /// <summary>Voice to play <paramref name="sound"/> on, or -1 when it must not play now.</summary>
        public int Acquire(int sound, float now, float duration, int priority = 0)
        {
            var limits = m_Limits[sound];
            if (now - limits.LastPlay < limits.MinInterval) { Rejected++; return -1; }

            int same = 0, oldestSame = -1, free = -1, victim = -1;
            for (int i = 0; i < m_Voices.Length; i++)
            {
                ref var v = ref m_Voices[i];
                bool active = v.Sound >= 0 && v.End > now;
                if (!active) { if (free < 0) free = i; continue; }
                if (v.Sound == sound)
                {
                    same++;
                    if (oldestSame < 0 || v.Start < m_Voices[oldestSame].Start) oldestSame = i;
                }
                if (v.Priority <= priority && (victim < 0 || v.Priority < m_Voices[victim].Priority
                    || (v.Priority == m_Voices[victim].Priority && v.Start < m_Voices[victim].Start)))
                    victim = i;
            }

            int voice;
            if (same >= limits.MaxVoices) { voice = oldestSame; Stolen++; }   // restart its oldest instance
            else if (free >= 0) voice = free;
            else if (victim >= 0) { voice = victim; Stolen++; }
            else { Rejected++; return -1; }

            m_Voices[voice] = new Voice { Sound = sound, Start = now, End = now + duration, Priority = priority };
            limits.LastPlay = now;
            m_Limits[sound] = limits;
            return voice;
        }
    }
}
