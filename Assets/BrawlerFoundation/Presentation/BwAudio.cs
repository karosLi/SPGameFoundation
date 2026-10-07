using SPF.Contracts.Weapons;
using SPF.Presentation;
using SPF.Presentation.Audio;
using SPF.Runtime.Session;
using UnityEngine;

namespace BrawlerFoundation.Presentation
{
    /// <summary>Uses settled fighter HP for hurt/heal, the existing feedback broadcast for classic
    /// combat, and an independent authoritative weapon-ring cursor for weapon combat.</summary>
    [DefaultExecutionOrder(600)]
    public sealed class BwAudio : MonoBehaviour
    {
        BwRenderer m_Renderer;
        SanctuaryAudioBank m_Bank;
        WeaponAudioCursor m_Cursor;
        readonly ImpactAudioBudget m_Impacts = new ImpactAudioBudget();
        ViewTimelineStamp m_Timeline;
        SimSession m_Session;
        float m_Hp;
        bool m_HasHp, m_Attached;
        int m_SuppressFrame = -1;
        public SoundPlayer Player { get; private set; }
        public WeaponAudioCursor Cursor => m_Cursor;
        public static BwAudio Create(Transform parent, BwRenderer renderer)
        {
            var go = new GameObject("BwAudio"); go.transform.SetParent(parent, false);
            var audio = go.AddComponent<BwAudio>(); audio.Player = SoundPlayer.Create(go.transform, 16);
            audio.m_Bank = new SanctuaryAudioBank(audio.Player); audio.m_Cursor = new WeaponAudioCursor(audio.Player, audio.m_Bank); audio.Bind(renderer); return audio;
        }
        public void Confirm() => Player.Play(m_Bank.Confirm);
        public void Cancel() => Player.Play(m_Bank.Cancel);
        public void Bind(BwRenderer renderer)
        { Detach(); m_Renderer = renderer; m_Timeline.Reset(); m_Session = null; m_Cursor?.Reset(); m_Impacts.Reset(); if (isActiveAndEnabled) { Attach(); RefreshContext(); } }
        void Attach() { if (!m_Attached && m_Renderer != null) { m_Renderer.Feedback += OnFeedback; m_Attached = true; } }
        void Detach() { if (m_Attached && m_Renderer != null) m_Renderer.Feedback -= OnFeedback; m_Attached = false; }
        void OnEnable() { Attach(); m_Timeline.Reset(); }
        void OnDisable() { Detach(); m_Cursor?.Reset(); m_Impacts.Reset(); Player?.ResetPlayback(); m_Timeline.Reset(); m_Session = null; }
        void OnDestroy() => Detach();
        bool RefreshContext()
        {
            var session = m_Renderer != null && m_Renderer.Host != null ? m_Renderer.Host.Session : null;
            if (session == null || session.State == SessionState.Disposed) { Player?.ResetPlayback(); m_Cursor?.Reset(); m_Impacts.Reset(); m_Timeline.Reset(); m_Session = null; return false; }
            session.Sync(); var world = session.World; var state = world.Resource(BwKeys.Game);
            bool changed = m_Timeline.Update(session, session.TimelineRevision, world.LevelVersion); m_Session = session;
            if (changed) { m_Cursor.Reset(); m_Impacts.Reset(); Player.ResetPlayback(); m_HasHp = false; m_SuppressFrame = Time.frameCount; }
            Player.SetPaused(session.State != SessionState.Running);
            Player.SetMusic(state.Flow == BwFlow.Fighting ? m_Bank.Combat : m_Bank.Exploration);
            if (world.HasResource(BwWeapons.Key))
            {
                var weapon = world.Resource(BwWeapons.Key);
                m_Cursor.Synchronize(session, session.TimelineRevision, world.LevelVersion, weapon, weapon.Revision, weapon.Tick, weapon.Owner, weapon.Equipment.CueSequence);
            }
            return true;
        }
        void LateUpdate() => Pump();
        public void Pump()
        {
            if (!isActiveAndEnabled || !RefreshContext()) return;
            var world = m_Session.World; var state = world.Resource(BwKeys.Game);
            var fighters = world.Column(BwKeys.Info); bool found = false;
            for (int i = 0; i < world.Table(BwKeys.Fighter).Count; i++)
            {
                if (fighters[i].Team != 0) continue; float hp = fighters[i].Hp; found = true;
                if (m_HasHp && state.Flow == BwFlow.Fighting && Time.frameCount != m_SuppressFrame)
                { if (hp < m_Hp) Player.Play(m_Bank.Hurt); else if (hp > m_Hp) Player.Play(m_Bank.Heal); }
                m_HasHp = true; m_Hp = hp; break;
            }
            if (!found || state.Flow == BwFlow.Menu) m_HasHp = false;
            if (!world.HasResource(BwWeapons.Key)) { FlushImpacts(); return; }
            var weapon = world.Resource(BwWeapons.Key);
            Vector2 listener = m_Renderer.Camera != null ? (Vector2)m_Renderer.Camera.transform.position : Vector2.zero;
            Vector2 origin = listener;
            if (world.Registry.TryResolve(weapon.Owner, out _, out int row)) { var point = world.Column(BwKeys.Position)[row]; origin = new Vector2(point.x, point.y); }
            m_Cursor.ObserveMelee(weapon.View(1), origin, listener);
            for (int i = 0; i < weapon.CueCount; i++)
            {
                var cue = weapon.Cues[i]; cue.Position = BwBeltRules.Project(cue.Position, cue.Height); cue.Height = 0; uint before = m_Cursor.Sequence;
                m_Cursor.Submit(cue, weapon.Profile(cue.ContentId).Family, listener, playImpact: false);
                if (before != m_Cursor.Sequence && (cue.Kind & WeaponCueKind.Impact) != 0)
                    RequestImpact(weapon.Profile(cue.ContentId).Family == WeaponActionFamily.Thrust || weapon.Profile(cue.ContentId).Family == WeaponActionFamily.Cast, new Vector2(cue.Position.x, cue.Position.y));
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
            Player.PlayAt(heavy ? m_Bank.HeavyImpact : m_Bank.LightImpact, at, listener, volume: .75f);
        }
        void OnFeedback(BwFeedback e)
        {
            if (!isActiveAndEnabled || !RefreshContext() || Time.frameCount == m_SuppressFrame) return;
            switch (e.Kind)
            {
                case BwFeedbackKind.Swing: Player.Play(m_Bank.Knife); break;
                case BwFeedbackKind.Hit: RequestImpact(false, new Vector2(e.Position.x, e.Position.y)); break;
                case BwFeedbackKind.KO: RequestImpact(true, new Vector2(e.Position.x, e.Position.y)); break;
                case BwFeedbackKind.Wave: Player.Play(m_Bank.Equip); break;
                case BwFeedbackKind.Lose: Player.Play(m_Bank.Hurt); break;
            }
        }
    }
}
