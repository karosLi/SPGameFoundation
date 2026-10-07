using SPF.Presentation.Animation;
using SPF.Presentation.Sprites;

namespace RpgFoundation.Presentation
{
    /// <summary>Read-only adapter of RPG action phases to authored sprite strips. Stored per registry
    /// slot by the renderer; generations reset all visual history without adding snapshot state.</summary>
    public struct RpgActorAnimation
    {
        public int Generation { get; private set; }
        public CharacterClip Clip { get; private set; }
        public int Frame { get; private set; }
        public float StridePhase => m_Locomotion.Phase;
        public float Time => m_Locomotion.Time;
        SpriteLocomotionClock m_Locomotion;
        float m_DeathTime;

        public void Sample(int generation, CharacterArt art, in CombatState action, SkillKind skill,
            bool dead, float speed, float runSpeed, float dt)
        {
            if (Generation != generation) { this = default; Generation = generation; }
            bool locomoting = !dead && (action.Phase == ActionPhase.None || action.Phase == ActionPhase.Dash
                || action.Phase == ActionPhase.Recover && action.PhaseSkill != 0);
            m_Locomotion.Advance(dt, speed, runSpeed, enabled: locomoting);
            float progress = action.PhaseProgress;
            if (dead)
            {
                Clip = CharacterClip.Death;
                m_DeathTime += dt > 0f ? dt : 0f;
                Frame = art.Clip(Clip).FrameAt(m_DeathTime);
            }
            else if (action.Phase == ActionPhase.Stagger || action.HitFlash > 0.08f)
            {
                Clip = CharacterClip.Hit;
                Frame = art.Clip(Clip).FrameAtProgress(action.Phase == ActionPhase.Stagger ? progress : 1f - action.HitFlash / 0.15f);
            }
            else if (action.Phase == ActionPhase.Windup || action.Phase == ActionPhase.Recover && action.PhaseSkill == 0)
            {
                Clip = CharacterClip.Attack;
                Frame = art.Clip(Clip).FrameAtProgress(action.Phase == ActionPhase.Windup ? progress * 0.5f : 0.5f + progress * 0.5f);
            }
            else if (action.Phase == ActionPhase.Cast || action.Phase == ActionPhase.Channel)
            {
                Clip = skill == SkillKind.Whirlwind ? CharacterClip.Channel : skill == SkillKind.Nova ? CharacterClip.AreaCast
                    : skill == SkillKind.Slam ? CharacterClip.SlamCast : CharacterClip.Cast;
                Frame = action.Phase == ActionPhase.Channel ? art.Clip(Clip).FrameAt(action.PhaseTime)
                    : art.Clip(Clip).FrameAtProgress(progress);
            }
            else
            {
                Clip = m_Locomotion.State == GameplayLocomotionState.Run ? CharacterClip.Run
                    : m_Locomotion.State == GameplayLocomotionState.Walk ? CharacterClip.Walk : CharacterClip.Idle;
                Frame = Clip == CharacterClip.Idle ? art.Clip(Clip).FrameAt(m_Locomotion.Time) : m_Locomotion.Frame(art.Clip(Clip));
            }
        }
    }
}
