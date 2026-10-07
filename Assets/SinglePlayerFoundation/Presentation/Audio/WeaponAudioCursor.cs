using SPF.Contracts;
using SPF.Contracts.Weapons;
using UnityEngine;

namespace SPF.Presentation.Audio
{
    /// <summary>Independent read-only cursor over a single weapon owner's retained cue ring.
    /// Session, same-tick restore, level, bank identity/revision, backwards tick, and generation
    /// changes prime to the current sequence so retained historical cues never replay.</summary>
    public sealed class WeaponAudioCursor
    {
        readonly SoundPlayer m_Player;
        readonly SanctuaryAudioBank m_Bank;
        ViewTimelineStamp m_Timeline;
        object m_Weapon;
        uint m_Revision, m_Sequence, m_DrawPulse, m_MeleePulse;
        bool m_PrimeMelee, m_HasAction;
        WeaponViewState m_Action;
        long m_Tick;
        EntityHandle m_Owner;
        bool m_Bound;
        public uint Sequence => m_Sequence;
        public int Accepted { get; private set; }
        public int RangedReleasesPlayed { get; private set; }
        public int LastCueSound { get; private set; } = -1;
        public int LastRangedReleaseSound { get; private set; } = -1;
        public WeaponAudioCursor(SoundPlayer player, SanctuaryAudioBank bank) { m_Player = player; m_Bank = bank; }
        public bool Synchronize(object session, uint timelineRevision, int levelVersion, object weapon, uint weaponRevision, long tick, EntityHandle owner, uint currentSequence)
        {
            bool changed = !m_Bound || !m_Timeline.Matches(session, timelineRevision, levelVersion) || !ReferenceEquals(m_Weapon, weapon)
                || m_Revision != weaponRevision || tick < m_Tick || m_Owner != owner;
            m_Timeline.Update(session, timelineRevision, levelVersion); m_Weapon = weapon; m_Revision = weaponRevision; m_Tick = tick; m_Owner = owner;
            if (changed) { m_Player.StopTransient(); m_Sequence = currentSequence; m_DrawPulse = 0; m_PrimeMelee = true; m_HasAction = false; m_Bound = true; }
            return changed;
        }
        /// <summary>Melee has no Release event in the legacy ring. Observe the simulation-owned
        /// contact marker at alpha=1, never an interpolated pose or guessed wall-clock delay.</summary>
        public bool ObserveMelee(in WeaponViewState state, Vector2 at, Vector2 listener)
        {
            if (!m_Bound) return false;
            m_Action = state; m_HasAction = true;
            if (m_DrawPulse != 0 && (state.ActionPulse != m_DrawPulse || state.Family != WeaponActionFamily.Draw ||
                (state.Stage != WeaponStage.Windup && state.Stage != WeaponStage.Active) || state.Phase >= state.ReleasePhase))
            { m_Player.StopSound(m_Bank.BowDraw); m_DrawPulse = 0; }
            if (m_PrimeMelee)
            { m_PrimeMelee = false; m_MeleePulse = state.Stage == WeaponStage.Windup ? 0 : state.ActionPulse; return false; }
            if (state.ActionPulse == 0 || state.ActionPulse == m_MeleePulse ||
                (state.Family != WeaponActionFamily.Slash && state.Family != WeaponActionFamily.Thrust) ||
                (state.Stage != WeaponStage.Active && state.Stage != WeaponStage.Recovery) || state.Phase < state.ContactPhase) return false;
            m_MeleePulse = state.ActionPulse;
            return m_Player.PlayAt(state.Family == WeaponActionFamily.Thrust ? m_Bank.Sword : m_Bank.Knife, at, listener);
        }
        public bool Submit(in WeaponCue cue, WeaponActionFamily family, Vector2 listener, bool playImpact = true)
        {
            if (!m_Bound || cue.Owner != m_Owner || cue.Sequence == 0 || unchecked((int)(cue.Sequence - m_Sequence)) <= 0) return false;
            m_Sequence = cue.Sequence; Accepted++;
            int sound = -1;
            if ((cue.Kind & WeaponCueKind.Equip) != 0) sound = m_Bank.Equip;
            else if ((cue.Kind & WeaponCueKind.Impact) != 0) { if (!playImpact) return false; sound = family == WeaponActionFamily.Thrust || family == WeaponActionFamily.Cast ? m_Bank.HeavyImpact : m_Bank.LightImpact; }
            else if ((cue.Kind & WeaponCueKind.Release) != 0) sound = family == WeaponActionFamily.Draw ? m_Bank.BowRelease : family == WeaponActionFamily.Cast ? m_Bank.Staff : family == WeaponActionFamily.Thrust ? m_Bank.Sword : m_Bank.Knife;
            else if ((cue.Kind & WeaponCueKind.Begin) != 0 && family == WeaponActionFamily.Draw && m_HasAction &&
                m_Action.ActionPulse == cue.ActionPulse && m_Action.Family == WeaponActionFamily.Draw &&
                (m_Action.Stage == WeaponStage.Windup || m_Action.Stage == WeaponStage.Active) && m_Action.Phase < m_Action.ReleasePhase)
            { sound = m_Bank.BowDraw; m_DrawPulse = cue.ActionPulse; }
            // Cancellation is silence, not a fictional release. Stop the bounded draw cue immediately.
            if ((cue.Kind & (WeaponCueKind.Cancel | WeaponCueKind.Release | WeaponCueKind.Equip)) != 0 && cue.ActionPulse == m_DrawPulse) { m_Player.StopSound(m_Bank.BowDraw); m_DrawPulse = 0; }
            bool played = sound >= 0 && m_Player.PlayAt(sound, new Vector2(cue.Position.x, cue.Position.y + cue.Height), listener);
            if (played)
            {
                LastCueSound = sound;
                if ((cue.Kind & WeaponCueKind.Release) != 0 && (family == WeaponActionFamily.Draw || family == WeaponActionFamily.Cast)) { RangedReleasesPlayed++; LastRangedReleaseSound = sound; }
            }
            return played;
        }
        public void Reset() { m_Bound = false; m_Weapon = null; m_Timeline.Reset(); m_Sequence = 0; m_Player.StopTransient(); }
    }
}
