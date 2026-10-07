using System;
using System.Collections.Generic;
using UnityEngine;

namespace SPF.Presentation.Audio
{
    public enum SoundBus { Sfx, Music, Ui }

    /// <summary>Fixed one-shot voices plus exactly two music lanes. Registration/load is cold;
    /// playback/update never allocates. Imported clips are borrowed; generated clips owned here.
    /// Game pause drops transient SFX (UI remains usable); background pauses music and drops all SFX.</summary>
    public sealed class SoundPlayer : MonoBehaviour
    {
        struct Sound { public AudioClip Clip; public SoundBus Bus; public float Volume; public int Priority; public bool Owned; }
        struct Playing { public float Volume; public SoundBus Bus; }
        readonly List<Sound> m_Sounds = new List<Sound>(32);
        readonly Dictionary<string, int> m_Names = new Dictionary<string, int>(32);
        readonly float[] m_BusVolume = { 1f, .55f, 1f };
        readonly bool[] m_BusMuted = new bool[3];
        readonly MusicCrossfade m_Fade = new MusicCrossfade();
        readonly AudioSource[] m_Music = new AudioSource[2];
        readonly int[] m_MusicBound = { -1, -1 };
        AudioSource[] m_Sources;
        Playing[] m_Playing;
        VoicePool m_Pool;
        float m_Master = .8f;
        bool m_Muted, m_Paused, m_Background, m_FocusLost, m_MusicPaused;
        public const int MaxVoices = 32, MaxSounds = 64;
        public static SoundPlayer Create(Transform parent, int voices = 16)
        {
            var go = new GameObject("Sound"); go.transform.SetParent(parent, false);
            var player = go.AddComponent<SoundPlayer>(); player.Initialize(voices); return player;
        }
        public void Initialize(int voices)
        {
            if (voices < 1 || voices > MaxVoices) throw new ArgumentOutOfRangeException(nameof(voices));
            if (m_Pool != null)
            { if (m_Pool.VoiceCount != voices) throw new InvalidOperationException("Audio capacity is fixed after initialization."); return; }
            m_Pool = new VoicePool(voices); m_Sources = new AudioSource[voices]; m_Playing = new Playing[voices];
            for (int i = 0; i < voices; i++) m_Sources[i] = NewSource(false);
            for (int i = 0; i < 2; i++) m_Music[i] = NewSource(true);
        }
        AudioSource NewSource(bool music)
        {
            var source = gameObject.AddComponent<AudioSource>(); source.playOnAwake = false;
            source.spatialBlend = 0; source.loop = music; source.priority = music ? 32 : 128; return source;
        }
        public VoicePool Pool => m_Pool;
        public MusicCrossfade Music => m_Fade;
        public int SoundCount => m_Sounds.Count;
        public int Played { get; private set; }
        public int FailedClips { get; private set; }
        public int LastPlayed { get; private set; } = -1;
        public bool Paused => m_Paused;
        public bool Background => m_Background || m_FocusLost;
        public AudioSource VoiceSource(int voice) => m_Sources[voice];
        public AudioSource MusicSource(int lane) => m_Music[lane];
        public float Master { get => m_Master; set { m_Master = Unit(value); Refresh(); } }
        public bool Muted { get => m_Muted; set { m_Muted = value; Refresh(); } }
        public float GetVolume(SoundBus bus) => m_BusVolume[BusIndex(bus)];
        public void SetVolume(SoundBus bus, float volume) { m_BusVolume[BusIndex(bus)] = Unit(volume); Refresh(); }
        public bool GetMuted(SoundBus bus) => m_BusMuted[BusIndex(bus)];
        public void SetMuted(SoundBus bus, bool muted) { m_BusMuted[BusIndex(bus)] = muted; Refresh(); }
        public int Register(string name, in SfxDef def, SoundBus bus = SoundBus.Sfx, int maxVoices = 3, float minInterval = .04f, int priority = 0)
        {
            ValidateRegistration(name, bus, minInterval);
            return Add(name, SfxSynth.CreateClip(name, def), true, bus, 1, maxVoices, minInterval, priority);
        }
        public int Register(string name, AudioClip clip, SoundBus bus = SoundBus.Sfx, float volume = 1, int maxVoices = 3, float minInterval = .04f, int priority = 0)
        { ValidateRegistration(name, bus, minInterval); return Add(name, clip, false, bus, Unit(volume), maxVoices, minInterval, priority); }
        void ValidateRegistration(string name, SoundBus bus, float interval)
        {
            if (m_Pool == null) throw new InvalidOperationException("Initialize before registering audio.");
            if (string.IsNullOrEmpty(name) || m_Names.ContainsKey(name)) throw new ArgumentException("Audio names must be unique and nonempty.");
            if (m_Sounds.Count >= MaxSounds) throw new InvalidOperationException("Audio bank capacity exhausted.");
            BusIndex(bus); if (!Finite(interval) || interval < 0) throw new ArgumentOutOfRangeException(nameof(interval));
        }
        int Add(string name, AudioClip clip, bool owned, SoundBus bus, float volume, int maxVoices, float interval, int priority)
        {
            int id = m_Pool.AddSound(maxVoices, interval);
            m_Sounds.Add(new Sound { Clip = clip, Bus = bus, Volume = volume, Priority = priority, Owned = owned }); m_Names.Add(name, id); return id;
        }
        public int Find(string name) => name != null && m_Names.TryGetValue(name, out int id) ? id : -1;
        public bool Play(int sound, float volume = 1, float pitch = 1, float pan = 0)
        {
            if (m_Pool == null || !isActiveAndEnabled || sound < 0 || sound >= m_Sounds.Count || m_Muted || Background || !Finite(volume) || volume <= 0 || !Finite(pitch) || !Finite(pan)) return false;
            var s = m_Sounds[sound];
            if (s.Bus == SoundBus.Music || m_BusMuted[(int)s.Bus] || (m_Paused && s.Bus != SoundBus.Ui)) return false;
            if (s.Clip == null || s.Clip.loadState != AudioDataLoadState.Loaded) { FailedClips++; return false; }
            pitch = Mathf.Clamp(pitch, .25f, 3f);
            int voice = m_Pool.Acquire(sound, Time.unscaledTime, s.Clip.length / pitch, s.Priority);
            if (voice < 0) return false;
            var source = m_Sources[voice]; source.Stop(); source.clip = s.Clip; source.pitch = pitch;
            source.panStereo = Mathf.Clamp(pan, -1, 1); source.priority = Mathf.Clamp(128 - s.Priority * 16, 0, 256);
            m_Playing[voice] = new Playing { Volume = s.Volume * Unit(volume), Bus = s.Bus };
            source.volume = Effective(s.Bus, m_Playing[voice].Volume); source.Play(); Played++; LastPlayed = sound; return true;
        }
        /// <summary>Explicit 2D distance rolloff, independent of global listener/3D settings.</summary>
        public bool PlayAt(int sound, Vector2 at, Vector2 listener, float audibleRadius = 18, float volume = 1, float pitch = 1)
        {
            if (!Finite(audibleRadius) || audibleRadius <= 0) return false;
            float dx = at.x - listener.x, dy = at.y - listener.y;
            return Play(sound, volume * DistanceGain(at, listener, audibleRadius), pitch, dx / audibleRadius * .7f);
        }
        public static float DistanceGain(Vector2 at, Vector2 listener, float audibleRadius = 18)
        {
            if (!Finite(audibleRadius) || audibleRadius <= 0 || !Finite(at.x) || !Finite(at.y) || !Finite(listener.x) || !Finite(listener.y)) return 0;
            float dx = at.x - listener.x, dy = at.y - listener.y;
            return Mathf.Clamp01(1 - Mathf.Sqrt(dx * dx + dy * dy) / audibleRadius);
        }
        public bool SetMusic(int sound, float seconds = 1.2f)
        {
            if (m_Pool == null || !isActiveAndEnabled || sound < -1 || sound >= m_Sounds.Count) return false;
            if (sound >= 0 && (m_Sounds[sound].Bus != SoundBus.Music || m_Sounds[sound].Clip == null || m_Sounds[sound].Clip.loadState == AudioDataLoadState.Failed)) { FailedClips++; return false; }
            bool changed = m_Fade.Request(sound, seconds); ApplyMusic(); return changed;
        }
        public void TickAudio(float deltaTime)
        {
            if (!isActiveAndEnabled || m_Pool == null) return;
            if (!m_Paused && !Background) m_Fade.Advance(deltaTime);
            ApplyMusic();
        }
        void Update() => TickAudio(Time.unscaledDeltaTime);
        void ApplyMusic()
        {
            if (m_Pool == null) return;
            bool pause = m_Paused || Background;
            for (int i = 0; i < 2; i++)
            {
                var source = m_Music[i]; int track = m_Fade.Track(i);
                if (track != m_MusicBound[i])
                {
                    source.Stop(); source.clip = track < 0 ? null : m_Sounds[track].Clip; m_MusicBound[i] = track;
                    if (track >= 0) { source.volume = Effective(SoundBus.Music, m_Fade.Gain(i) * m_Sounds[track].Volume); source.Play(); if (pause) source.Pause(); }
                }
                else if (pause != m_MusicPaused) { if (pause) source.Pause(); else source.UnPause(); }
                source.volume = Effective(SoundBus.Music, m_Fade.Gain(i) * (track < 0 ? 0 : m_Sounds[track].Volume));
            }
            m_MusicPaused = pause;
        }
        public void SetPaused(bool paused)
        { if (m_Paused == paused) return; m_Paused = paused; if (paused) StopTransient(false); ApplyMusic(); }
        public void SetBackground(bool paused)
        { m_Background = paused; if (Background) StopTransient(true); ApplyMusic(); }
        void OnApplicationPause(bool paused) => SetBackground(paused);
        void OnApplicationFocus(bool focused) { m_FocusLost = !focused; if (Background) StopTransient(true); ApplyMusic(); }
        public void StopTransient(bool includeUi = true)
        {
            if (m_Pool == null) return;
            for (int i = 0; i < m_Sources.Length; i++)
                if (includeUi || m_Playing[i].Bus != SoundBus.Ui) { m_Sources[i].Stop(); m_Sources[i].clip = null; m_Pool.Stop(i); }
            if (includeUi) m_Pool.Clear();
        }
        public void StopSound(int sound)
        {
            if (m_Pool == null) return;
            for (int i = 0; i < m_Sources.Length; i++)
                if (m_Pool.SoundOf(i) == sound) { m_Sources[i].Stop(); m_Sources[i].clip = null; m_Pool.Stop(i); }
        }
        public void ResetPlayback()
        { StopTransient(); m_Fade.Clear(); ApplyMusic(); }
        void OnDisable() => ResetPlayback();
        float Effective(SoundBus bus, float volume) => m_Muted || m_BusMuted[(int)bus] ? 0 : Mathf.Clamp01(m_Master * m_BusVolume[(int)bus] * volume);
        void Refresh()
        {
            if (m_Sources == null) return;
            for (int i = 0; i < m_Sources.Length; i++) m_Sources[i].volume = Effective(m_Playing[i].Bus, m_Playing[i].Volume);
            ApplyMusic();
        }
        void OnDestroy()
        {
            ResetPlayback();
            foreach (var s in m_Sounds) if (s.Owned) RenderObjects.Destroy(s.Clip);
            m_Sounds.Clear(); m_Names.Clear();
        }
        static int BusIndex(SoundBus bus) { int i = (int)bus; if (i < 0 || i > 2) throw new ArgumentOutOfRangeException(nameof(bus)); return i; }
        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        static float Unit(float v) => Finite(v) ? Mathf.Clamp01(v) : 0;
    }
}
