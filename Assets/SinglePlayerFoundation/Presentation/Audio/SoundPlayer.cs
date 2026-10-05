using System.Collections.Generic;
using UnityEngine;

namespace SPF.Presentation.Audio
{
    /// <summary>Volume groups; the effective volume is master × bus × sound × call.</summary>
    public enum SoundBus { Sfx, Music, Ui }

    /// <summary>
    /// Pooled one-shot sound playback: a fixed set of AudioSources driven by a <see cref="VoicePool"/>,
    /// sounds registered once (procedural <see cref="SfxDef"/> or clips), volume per bus, mute. Uses
    /// unscaled time, so UI sounds work while the game is paused.
    /// </summary>
    public sealed class SoundPlayer : MonoBehaviour
    {
        struct Sound
        {
            public AudioClip Clip;
            public SoundBus Bus;
            public float Volume;
            public int Priority;
            public bool Owned;
        }

        struct Playing
        {
            public float Volume;
            public SoundBus Bus;
        }

        readonly List<Sound> m_Sounds = new List<Sound>();
        readonly Dictionary<string, int> m_Names = new Dictionary<string, int>();
        readonly float[] m_BusVolume = { 1f, 1f, 1f };
        AudioSource[] m_Sources;
        Playing[] m_Playing;
        VoicePool m_Pool;
        float m_Master = 1f;
        bool m_Muted;

        public static SoundPlayer Create(Transform parent, int voices = 16)
        {
            var go = new GameObject("Sound");
            go.transform.SetParent(parent, false);
            var player = go.AddComponent<SoundPlayer>();
            player.Initialize(voices);
            return player;
        }

        public void Initialize(int voices)
        {
            m_Pool = new VoicePool(voices);
            m_Sources = new AudioSource[voices];
            m_Playing = new Playing[voices];
            for (int i = 0; i < voices; i++)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                m_Sources[i] = source;
            }
        }

        public VoicePool Pool => m_Pool;
        public int SoundCount => m_Sounds.Count;
        /// <summary>Sounds started (diagnostics, tests).</summary>
        public int Played { get; private set; }
        public int LastPlayed { get; private set; } = -1;

        public float Master { get => m_Master; set { m_Master = Mathf.Clamp01(value); Refresh(); } }
        public bool Muted { get => m_Muted; set { m_Muted = value; Refresh(); } }
        public float GetVolume(SoundBus bus) => m_BusVolume[(int)bus];
        public void SetVolume(SoundBus bus, float volume) { m_BusVolume[(int)bus] = Mathf.Clamp01(volume); Refresh(); }

        /// <summary>Synthesizes and registers a sound. Returns its id.</summary>
        public int Register(string name, in SfxDef def, SoundBus bus = SoundBus.Sfx, int maxVoices = 3, float minInterval = 0.04f, int priority = 0)
        {
            return Add(name, SfxSynth.CreateClip(name, def), true, bus, 1f, maxVoices, minInterval, priority);
        }

        public int Register(string name, AudioClip clip, SoundBus bus = SoundBus.Sfx, float volume = 1f, int maxVoices = 3, float minInterval = 0.04f, int priority = 0)
        {
            return Add(name, clip, false, bus, volume, maxVoices, minInterval, priority);
        }

        int Add(string name, AudioClip clip, bool owned, SoundBus bus, float volume, int maxVoices, float minInterval, int priority)
        {
            int id = m_Pool.AddSound(maxVoices, minInterval);
            m_Sounds.Add(new Sound { Clip = clip, Bus = bus, Volume = volume, Priority = priority, Owned = owned });
            m_Names[name] = id;
            return id;
        }

        public int Find(string name) => m_Names.TryGetValue(name, out int id) ? id : -1;

        /// <summary>Plays a registered sound; returns false when muted or limited (voice limit, retrigger interval).</summary>
        public bool Play(int sound, float volume = 1f, float pitch = 1f, float pan = 0f)
        {
            if (sound < 0 || sound >= m_Sounds.Count || m_Muted || volume <= 0f) return false;
            var s = m_Sounds[sound];
            pitch = Mathf.Clamp(pitch, 0.25f, 3f);
            float length = s.Clip != null ? s.Clip.length / pitch : 0.1f;
            int voice = m_Pool.Acquire(sound, Time.unscaledTime, length, s.Priority);
            if (voice < 0) return false;
            var source = m_Sources[voice];
            source.Stop();
            source.clip = s.Clip;
            source.pitch = pitch;
            source.panStereo = Mathf.Clamp(pan, -1f, 1f);
            m_Playing[voice] = new Playing { Volume = s.Volume * volume, Bus = s.Bus };
            source.volume = Effective(voice);
            source.Play();
            Played++;
            LastPlayed = sound;
            return true;
        }

        float Effective(int voice) => m_Muted ? 0f : Mathf.Clamp01(m_Master * m_BusVolume[(int)m_Playing[voice].Bus] * m_Playing[voice].Volume);

        void Refresh()
        {
            if (m_Sources == null) return;
            for (int i = 0; i < m_Sources.Length; i++) m_Sources[i].volume = Effective(i);
        }

        void OnDestroy()
        {
            foreach (var s in m_Sounds)
                if (s.Owned) RenderObjects.Destroy(s.Clip);
            m_Sounds.Clear();
        }
    }
}
