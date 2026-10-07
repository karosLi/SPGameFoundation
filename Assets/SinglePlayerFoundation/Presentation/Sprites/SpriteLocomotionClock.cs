using SPF.Presentation.Animation;
using Unity.Mathematics;

namespace SPF.Presentation.Sprites
{
    /// <summary>Presentation-only gait phase shared by sprite walk/run strips. Changing speed or strip
    /// never restarts the stride. Call with zero dt when the session is paused, including host suspension.</summary>
    public struct SpriteLocomotionClock
    {
        GameplayLocomotionClassifier m_Classifier;
        public GameplayLocomotionState State => m_Classifier.State;
        public float Phase { get; private set; }
        public float Time { get; private set; }

        public void Advance(float dt, float speed, float runSpeed, bool grounded = true, bool enabled = true)
        {
            if (!math.isfinite(dt) || dt <= 0f) return;
            speed = math.isfinite(speed) ? math.max(0f, speed) : 0f;
            runSpeed = math.isfinite(runSpeed) ? math.max(0.1f, runSpeed) : 1f;
            Time += dt;
            var state = m_Classifier.Step(speed, runSpeed * 0.72f, runSpeed * 0.60f, grounded, enabled);
            if (state == GameplayLocomotionState.Walk || state == GameplayLocomotionState.Run)
                Phase = math.frac(Phase + dt * math.clamp(3f * speed / runSpeed, 0.15f, 4.5f));
        }

        public int Frame(in SpriteClip clip) => clip.FrameAtProgress(Phase);
    }
}
