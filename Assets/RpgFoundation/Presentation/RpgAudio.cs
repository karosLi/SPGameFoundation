using System.Collections.Generic;
using SPF.Presentation.Audio;
using SPF.Shell.CameraRig;
using Unity.Mathematics;
using UnityEngine;

namespace RpgFoundation.Presentation
{
    /// <summary>
    /// Sound for the RPG: every sound is synthesized at start-up (no audio assets) and triggered by the
    /// same feedback events that drive the visual effects. Pans by screen position, quietens far-away
    /// events, varies pitch so repeated hits do not sound mechanical, and respects voice limits.
    /// </summary>
    public sealed class RpgAudio : MonoBehaviour
    {
        struct Pending
        {
            public float Time;
            public int Sound;
            public float Volume, Pitch, Pan;
        }

        SoundPlayer m_Player;
        readonly List<Pending> m_Pending = new List<Pending>();
        Unity.Mathematics.Random m_Random = new Unity.Mathematics.Random(0x5EED);

        int m_Hit, m_Crit, m_Hurt, m_Heal, m_Gold, m_Item, m_LevelUp, m_Death, m_BossDeath, m_HeroDeath, m_Stairs;
        int m_Chest, m_Barrel, m_Spikes;
        int m_SwingLight, m_SwingHeavy, m_Bow, m_Explosion, m_Nova, m_Whirlwind, m_SlamWarning, m_Slam, m_Dash, m_Cast, m_NoMana;
        public int Click { get; private set; }

        public SoundPlayer Player => m_Player;
        public FollowCamera2D Camera { get; set; }
        /// <summary>Monster table (boss deaths get their own sound).</summary>
        public RpgRuntimeConfig Config { get; set; }

        /// <summary>Half the visible width in world units, for panning (set from the camera each frame when known).</summary>
        public float HalfWidth { get; set; } = 9f;

        public static RpgAudio Create(Transform parent, RpgWorldRenderer renderer, FollowCamera2D camera)
        {
            var go = new GameObject("RpgAudio");
            go.transform.SetParent(parent, false);
            var audio = go.AddComponent<RpgAudio>();
            audio.Camera = camera;
            audio.Build();
            if (renderer != null) renderer.Feedback += audio.OnFeedback;
            return audio;
        }

        void Build()
        {
            m_Player = SoundPlayer.Create(transform, voices: 20);
            var p = m_Player;
            m_Hit = p.Register("hit", SfxDef.Create(SfxWave.Square, 260f, 70f, 0.09f, 0.45f).WithNoise(0.45f, 0.3f), maxVoices: 3, minInterval: 0.035f);
            m_Crit = p.Register("crit", SfxDef.Create(SfxWave.Square, 520f, 90f, 0.16f, 0.55f).WithNoise(0.35f, 0.2f).WithDuty(0.3f), maxVoices: 2, minInterval: 0.05f, priority: 1);
            m_Hurt = p.Register("hurt", SfxDef.Create(SfxWave.Saw, 190f, 60f, 0.2f, 0.55f).WithNoise(0.2f, 0.4f), maxVoices: 1, minInterval: 0.12f, priority: 2);
            m_Heal = p.Register("heal", SfxDef.Create(SfxWave.Sine, 440f, 990f, 0.35f, 0.5f).WithVibrato(0.4f, 14f), maxVoices: 1, priority: 2);
            m_Gold = p.Register("gold", SfxDef.Create(SfxWave.Square, 1320f, 1980f, 0.09f, 0.3f).WithDuty(0.25f).WithEnvelope(0.002f, 0.02f, 0.7f, 0.05f), maxVoices: 2, minInterval: 0.06f);
            m_Item = p.Register("item", SfxDef.Create(SfxWave.Triangle, 660f, 1760f, 0.25f, 0.55f).WithVibrato(0.6f, 18f), maxVoices: 1, priority: 2);
            m_LevelUp = p.Register("levelup", SfxDef.Create(SfxWave.Square, 392f, 1568f, 0.6f, 0.45f).WithDuty(0.25f).WithVibrato(0.5f, 9f).WithEnvelope(0.01f, 0.2f, 0.7f, 0.25f), maxVoices: 1, priority: 3);
            m_Death = p.Register("death", SfxDef.Create(SfxWave.Square, 180f, 45f, 0.28f, 0.45f).WithNoise(0.55f, 0.45f), maxVoices: 3, minInterval: 0.05f);
            m_BossDeath = p.Register("bossdeath", SfxDef.Create(SfxWave.Saw, 140f, 25f, 1.1f, 0.65f).WithNoise(0.5f, 0.6f).WithVibrato(1.5f, 7f), maxVoices: 1, priority: 3);
            m_HeroDeath = p.Register("herodeath", SfxDef.Create(SfxWave.Triangle, 330f, 55f, 1.0f, 0.6f).WithVibrato(1f, 5f), maxVoices: 1, priority: 4);
            m_Stairs = p.Register("stairs", SfxDef.Create(SfxWave.Triangle, 330f, 1320f, 0.45f, 0.5f).WithVibrato(0.3f, 10f), maxVoices: 1, priority: 3);
            m_SwingLight = p.Register("swing", SfxDef.Create(SfxWave.Noise, 5200f, 1800f, 0.11f, 0.3f).WithNoise(1f, 0.55f).WithEnvelope(0.02f, 0.03f, 0.6f, 0.06f), maxVoices: 3, minInterval: 0.04f);
            m_SwingHeavy = p.Register("swingheavy", SfxDef.Create(SfxWave.Noise, 2600f, 700f, 0.18f, 0.38f).WithNoise(1f, 0.7f).WithEnvelope(0.03f, 0.05f, 0.6f, 0.09f), maxVoices: 2, minInterval: 0.05f);
            m_Bow = p.Register("bow", SfxDef.Create(SfxWave.Triangle, 900f, 300f, 0.1f, 0.4f).WithNoise(0.3f, 0.3f), maxVoices: 3, minInterval: 0.04f);
            m_Explosion = p.Register("explosion", SfxDef.Create(SfxWave.Noise, 900f, 120f, 0.55f, 0.75f).WithNoise(1f, 0.8f).WithEnvelope(0.003f, 0.1f, 0.6f, 0.4f), maxVoices: 2, minInterval: 0.06f, priority: 1);
            m_Nova = p.Register("nova", SfxDef.Create(SfxWave.Sine, 1800f, 300f, 0.4f, 0.5f).WithNoise(0.4f, 0.2f).WithVibrato(0.8f, 30f), maxVoices: 1, priority: 1);
            m_Whirlwind = p.Register("whirlwind", SfxDef.Create(SfxWave.Noise, 1500f, 3500f, 0.7f, 0.4f).WithNoise(1f, 0.75f).WithVibrato(3f, 10f).WithEnvelope(0.08f, 0.2f, 0.8f, 0.3f), maxVoices: 1, priority: 1);
            m_SlamWarning = p.Register("slamwarn", SfxDef.Create(SfxWave.Square, 110f, 160f, 0.5f, 0.4f).WithVibrato(2f, 12f).WithEnvelope(0.05f, 0.1f, 0.8f, 0.15f), maxVoices: 1, priority: 2);
            m_Slam = p.Register("slam", SfxDef.Create(SfxWave.Noise, 300f, 40f, 0.6f, 0.8f).WithNoise(1f, 0.88f).WithEnvelope(0.002f, 0.12f, 0.6f, 0.4f), maxVoices: 1, priority: 2);
            m_Dash = p.Register("dash", SfxDef.Create(SfxWave.Noise, 1200f, 4800f, 0.16f, 0.35f).WithNoise(1f, 0.5f), maxVoices: 1);
            m_Cast = p.Register("cast", SfxDef.Create(SfxWave.Sine, 500f, 1250f, 0.18f, 0.35f).WithVibrato(0.5f, 25f), maxVoices: 2, minInterval: 0.05f);
            m_NoMana = p.Register("nomana", SfxDef.Create(SfxWave.Square, 180f, 150f, 0.12f, 0.3f).WithDuty(0.2f), maxVoices: 1, minInterval: 0.3f, priority: 1);
            m_Chest = p.Register("chest", SfxDef.Create(SfxWave.Square, 523f, 1046f, 0.35f, 0.4f).WithDuty(0.25f).WithVibrato(0.8f, 16f), maxVoices: 1, priority: 2);
            m_Barrel = p.Register("barrel", SfxDef.Create(SfxWave.Noise, 700f, 150f, 0.22f, 0.55f).WithNoise(1f, 0.6f).WithEnvelope(0.002f, 0.05f, 0.5f, 0.15f), maxVoices: 2, minInterval: 0.05f);
            m_Spikes = p.Register("spikes", SfxDef.Create(SfxWave.Saw, 900f, 400f, 0.12f, 0.35f).WithNoise(0.5f, 0.2f), maxVoices: 2, minInterval: 0.08f);
            Click = p.Register("click", SfxDef.Create(SfxWave.Square, 1100f, 900f, 0.04f, 0.25f).WithEnvelope(0.001f, 0.01f, 0.5f, 0.02f), SoundBus.Ui, maxVoices: 2, minInterval: 0.02f, priority: 5);
        }

        public void PlayUi(int sound) => m_Player.Play(sound);

        void Play(int sound, float2 at, float volume = 1f, float pitch = 1f, float delay = 0f, float jitter = 0.06f)
        {
            float pan = 0f;
            if (Camera != null)
            {
                float2 offset = at - (float2)(Vector2)Camera.transform.position;
                pan = math.clamp(offset.x / HalfWidth, -1f, 1f) * 0.7f;
                float distance = math.length(offset) / HalfWidth;
                volume *= math.saturate(1.25f - 0.45f * distance);   // off-screen events are quieter
            }
            pitch *= 1f + m_Random.NextFloat(-jitter, jitter);
            if (delay > 0.005f) m_Pending.Add(new Pending { Time = Time.unscaledTime + delay, Sound = sound, Volume = volume, Pitch = pitch, Pan = pan });
            else m_Player.Play(sound, volume, pitch, pan);
        }

        void Update()
        {
            var view = Camera != null ? Camera.GetComponent<UnityEngine.Camera>() : null;
            if (view != null && view.orthographic)
                HalfWidth = Mathf.Max(1f, view.orthographicSize * view.aspect);
            float now = Time.unscaledTime;
            for (int i = m_Pending.Count - 1; i >= 0; i--)
            {
                if (m_Pending[i].Time > now) continue;
                var p = m_Pending[i];
                m_Player.Play(p.Sound, p.Volume, p.Pitch, p.Pan);
                m_Pending.RemoveAt(i);
            }
        }

        public void OnFeedback(FeedbackEvent e)
        {
            switch (e.Kind)
            {
                case FeedbackKind.Damage:
                    Play(m_Hit, e.Position, 0.8f, e.Source == HitSource.Weapon ? 1f : 1.15f);
                    break;
                case FeedbackKind.Crit: Play(m_Crit, e.Position); break;
                case FeedbackKind.HeroHurt: Play(m_Hurt, e.Position); break;
                case FeedbackKind.Heal: Play(m_Heal, e.Position, 1f, 1f, 0f, 0f); break;
                case FeedbackKind.Gold: Play(m_Gold, e.Position, 0.8f); break;
                case FeedbackKind.Item: Play(m_Item, e.Position); break;
                case FeedbackKind.LevelUp: Play(m_LevelUp, e.Position, 1f, 1f, 0f, 0f); break;
                case FeedbackKind.Stairs: Play(m_Stairs, e.Position, 1f, 1f, 0f, 0f); break;
                case FeedbackKind.Death:
                    if (e.Actor == 0) Play(m_HeroDeath, e.Position, 1f, 1f, 0f, 0f);
                    else if (Config != null && e.Actor <= Config.Monsters.Length && Config.Monsters[e.Actor - 1].Boss) Play(m_BossDeath, e.Position, 1f, 1f, 0f, 0f);
                    else Play(m_Death, e.Position, 0.8f, 1.2f - e.Value * 0.4f);
                    break;
                case FeedbackKind.Swing:
                {
                    bool heavy = e.Weapon == WeaponKind.Axe || e.Weapon == WeaponKind.Hammer;
                    int sound = e.Weapon == WeaponKind.Bow ? m_Bow : e.Weapon == WeaponKind.Staff ? m_Cast : heavy ? m_SwingHeavy : m_SwingLight;
                    float pitch = e.Weapon == WeaponKind.Claw ? 1.3f : e.Weapon == WeaponKind.Spear ? 1.1f : 1f;
                    Play(sound, e.Position, e.Actor == 0 ? 0.9f : 0.6f, pitch, e.Value);
                    break;
                }
                case FeedbackKind.Explosion: Play(m_Explosion, e.Position); break;
                case FeedbackKind.Nova: Play(m_Nova, e.Position); break;
                case FeedbackKind.Whirlwind: Play(m_Whirlwind, e.Position); break;
                case FeedbackKind.SlamWarning: Play(m_SlamWarning, e.Position, 1f, 1f, 0f, 0f); break;
                case FeedbackKind.Slam: Play(m_Slam, e.Position, 1f, 1f, 0f, 0.03f); break;
                case FeedbackKind.Dash: Play(m_Dash, e.Position); break;
                case FeedbackKind.Cast: Play(m_Cast, e.Position, 0.8f); break;
                case FeedbackKind.Chest: Play(m_Chest, e.Position, 1f, 1f, 0f, 0f); break;
                case FeedbackKind.Barrel: Play(m_Barrel, e.Position); break;
                case FeedbackKind.Spikes: Play(m_Spikes, e.Position, 0.7f); break;
                case FeedbackKind.Mana: Play(m_NoMana, e.Position, 1f, 1f, 0f, 0f); break;
            }
        }
    }
}
