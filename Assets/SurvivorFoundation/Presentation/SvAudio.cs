using SPF.Presentation.Audio;
using UnityEngine;

namespace SurvivorFoundation.Presentation
{
    /// <summary>Synthesized sounds for the survivor game; voice limits keep hundreds of deaths per second audible but sane.</summary>
    public sealed class SvAudio : MonoBehaviour
    {
        SoundPlayer m_Player;
        int m_Death, m_Gem, m_LevelUp, m_Hurt;
        public int Click { get; private set; }
        public SoundPlayer Player => m_Player;

        public static SvAudio Create(Transform parent, SvRenderer renderer)
        {
            var go = new GameObject("SvAudio");
            go.transform.SetParent(parent, false);
            var audio = go.AddComponent<SvAudio>();
            var p = audio.m_Player = SoundPlayer.Create(go.transform, 12);
            audio.m_Death = p.Register("death", SfxDef.Create(SfxWave.Square, 300f, 80f, 0.12f, 0.3f).WithNoise(0.6f, 0.4f), maxVoices: 3, minInterval: 0.05f);
            audio.m_Gem = p.Register("gem", SfxDef.Create(SfxWave.Triangle, 1400f, 2100f, 0.06f, 0.25f), maxVoices: 2, minInterval: 0.04f);
            audio.m_LevelUp = p.Register("levelup", SfxDef.Create(SfxWave.Square, 392f, 1568f, 0.5f, 0.4f).WithDuty(0.25f).WithVibrato(0.5f, 9f), maxVoices: 1, priority: 3);
            audio.m_Hurt = p.Register("hurt", SfxDef.Create(SfxWave.Saw, 200f, 60f, 0.18f, 0.5f).WithNoise(0.2f, 0.4f), maxVoices: 1, minInterval: 0.15f, priority: 2);
            audio.Click = p.Register("click", SfxDef.Create(SfxWave.Square, 1100f, 900f, 0.04f, 0.25f), SoundBus.Ui, maxVoices: 2, priority: 5);
            if (renderer != null) renderer.Feedback += audio.OnFeedback;
            return audio;
        }

        void OnFeedback(SvFeedback e)
        {
            switch (e.Kind)
            {
                case SvFeedbackKind.Death: m_Player.Play(m_Death, 0.6f, 1f + (e.Enemy % 3) * 0.1f); break;
                case SvFeedbackKind.Gem: m_Player.Play(m_Gem, 0.5f); break;
                case SvFeedbackKind.LevelUp: m_Player.Play(m_LevelUp); break;
                case SvFeedbackKind.HeroHurt: m_Player.Play(m_Hurt); break;
            }
        }
    }
}
