using SPF.Contracts.Weapons;
using SPF.Presentation;
using SPF.Presentation.Audio;
using SPF.Runtime.Session;
using UnityEngine;

namespace SurvivorFoundation.Presentation
{
    /// <summary>Read-only audio adapter. Renderer owns feedback draining; the weapon ring has this
    /// independent cursor. No damage/weapon RNG is consumed and weapon hits are not played twice.</summary>
    [DefaultExecutionOrder(600)]
    public sealed class SvAudio : MonoBehaviour
    {
        SoundPlayer m_Player;
        SanctuaryAudioBank m_Bank;
        WeaponAudioCursor m_Cursor;
        readonly ImpactAudioBudget m_Impacts = new ImpactAudioBudget();
        SvRenderer m_Renderer;
        ViewTimelineStamp m_Timeline;
        SimSession m_Session;
        int m_SuppressFrame = -1;
        bool m_Attached;
        public int Click => m_Bank.Confirm;
        public int Cancel => m_Bank.Cancel;
        public SoundPlayer Player => m_Player;
        public WeaponAudioCursor Cursor => m_Cursor;
        public static SvAudio Create(Transform parent, SvRenderer renderer)
        {
            var go = new GameObject("SvAudio"); go.transform.SetParent(parent, false);
            var audio = go.AddComponent<SvAudio>(); audio.m_Player = SoundPlayer.Create(go.transform, 16);
            audio.m_Bank = new SanctuaryAudioBank(audio.m_Player); audio.m_Cursor = new WeaponAudioCursor(audio.m_Player, audio.m_Bank);
            audio.Bind(renderer); return audio;
        }
        public void Bind(SvRenderer renderer)
        {
            Detach(); m_Renderer = renderer; m_Timeline.Reset(); m_Session = null; m_Cursor?.Reset(); m_Impacts.Reset();
            if (isActiveAndEnabled) { Attach(); RefreshContext(); }
        }
        void Attach() { if (!m_Attached && m_Renderer != null) { m_Renderer.Feedback += OnFeedback; m_Attached = true; } }
        void Detach() { if (m_Attached && m_Renderer != null) m_Renderer.Feedback -= OnFeedback; m_Attached = false; }
        void OnEnable() { Attach(); m_Timeline.Reset(); }
        void OnDisable() { Detach(); m_Cursor?.Reset(); m_Impacts.Reset(); m_Player?.ResetPlayback(); m_Timeline.Reset(); m_Session = null; }
        void OnDestroy() => Detach();
        bool RefreshContext()
        {
            var session = m_Renderer != null && m_Renderer.Host != null ? m_Renderer.Host.Session : null;
            if (session == null || session.State == SessionState.Disposed) { m_Player?.ResetPlayback(); m_Cursor?.Reset(); m_Impacts.Reset(); m_Timeline.Reset(); m_Session = null; return false; }
            session.Sync(); var world = session.World; var state = world.Resource(SvKeys.Game);
            bool changed = m_Timeline.Update(session, session.TimelineRevision, world.LevelVersion);
            m_Session = session;
            if (changed) { m_Cursor.Reset(); m_Impacts.Reset(); m_Player.ResetPlayback(); m_SuppressFrame = Time.frameCount; }
            m_Player.SetPaused(session.State != SessionState.Running);
            m_Player.SetMusic(state.Flow == SvFlow.Playing ? m_Bank.Combat : m_Bank.Exploration);
            if (world.HasResource(SvWeapons.Key))
            {
                var weapon = world.Resource(SvWeapons.Key);
                m_Cursor.Synchronize(session, session.TimelineRevision, world.LevelVersion, weapon, weapon.Revision, weapon.Tick, weapon.Owner, weapon.Equipment.CueSequence);
            }
            return true;
        }
        void LateUpdate() => Pump();
        public void Pump()
        {
            if (!isActiveAndEnabled || !RefreshContext()) return;
            var world = m_Session.World;
            if (!world.HasResource(SvWeapons.Key)) { FlushImpacts(); return; }
            var weapon = world.Resource(SvWeapons.Key);
            Vector2 listener = m_Renderer.Camera != null ? (Vector2)m_Renderer.Camera.transform.position : Vector2.zero;
            var hero = world.Resource(SvKeys.Game).Hero;
            m_Cursor.ObserveMelee(weapon.View(1), new Vector2(hero.x, hero.y), listener);
            for (int i = 0; i < weapon.CueCount; i++)
            {
                var cue = weapon.Cues[i]; uint before = m_Cursor.Sequence;
                m_Cursor.Submit(cue, weapon.Profile(cue.ContentId).Family, listener, playImpact: false);
                if (before != m_Cursor.Sequence && (cue.Kind & WeaponCueKind.Impact) != 0)
                    RequestImpact(weapon.Profile(cue.ContentId).Family == WeaponActionFamily.Thrust || weapon.Profile(cue.ContentId).Family == WeaponActionFamily.Cast, new Vector2(cue.Position.x, cue.Position.y + cue.Height));
            }
            FlushImpacts();
        }
        void RequestImpact(bool heavy, Vector2 at)
        {
            Vector2 listener = m_Renderer.Camera != null ? (Vector2)m_Renderer.Camera.transform.position : Vector2.zero;
            m_Impacts.Request(Time.frameCount, heavy, at, SoundPlayer.DistanceGain(at, listener));
        }
        void FlushImpacts()
        {
            if (!m_Impacts.TryTake(Time.frameCount, out bool heavy, out Vector2 at)) return;
            Vector2 listener = m_Renderer.Camera != null ? (Vector2)m_Renderer.Camera.transform.position : Vector2.zero;
            m_Player.PlayAt(heavy ? m_Bank.HeavyImpact : m_Bank.LightImpact, at, listener, volume: .75f);
        }
        void OnFeedback(SvFeedback e)
        {
            if (!isActiveAndEnabled || !RefreshContext() || Time.frameCount == m_SuppressFrame) return;
            bool weapons = m_Session.World.HasResource(SvWeapons.Key);
            switch (e.Kind)
            {
                case SvFeedbackKind.Hit: RequestImpact(false, new Vector2(e.Position.x, e.Position.y)); break;
                case SvFeedbackKind.Death: RequestImpact(true, new Vector2(e.Position.x, e.Position.y)); break;
                case SvFeedbackKind.Gem: m_Player.Play(m_Bank.Equip, .3f); break;
                case SvFeedbackKind.LevelUp: m_Player.Play(m_Bank.Heal); break;
                case SvFeedbackKind.HeroHurt: m_Player.Play(m_Bank.Hurt); break;
                case SvFeedbackKind.Shoot: if (!weapons) m_Player.Play(m_Bank.BowRelease, .5f); break;
                case SvFeedbackKind.Nova: m_Player.Play(m_Bank.Staff); break;
            }
        }
    }
}
