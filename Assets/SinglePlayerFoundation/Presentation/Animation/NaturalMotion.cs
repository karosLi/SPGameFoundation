using SPF.L1.Skeleton;
using Unity.Collections;
using Unity.Mathematics;

namespace SPF.Presentation.Animation
{
    /// <summary>Presentation-only step controller. Ground samples and root motion are inputs, never outputs.</summary>
    public struct FootPlantState
    {
        public float Phase;
        public float2 Position, Plant, SwingStart, SwingEnd, PreviousRoot;
        public bool Initialized;
        public bool InStance => Phase < NaturalMotion.Stance;
    }

    public struct SmoothedAimState
    {
        public float2 Target;
        public bool Initialized;
    }

    /// <summary>Blittable, allocation-free math suitable for a Burst caller. Metres, seconds, Y-up.</summary>
    public static class NaturalMotion
    {
        public const float Stance = .62f;
        public const float Period = .92f;
        public const float SwingHeight = .21f;
        public const float MaxStepSeconds = .25f;
        public static float Ease(float t) { t = math.saturate(t); return t * t * t * (10f + t * (-15f + 6f * t)); }

        public static void InitializeFoot(ref FootPlantState foot, float2 root, float2 planted, float phase = 0)
        {
            foot = new FootPlantState { Initialized = true, Phase = math.frac(phase), Position = planted,
                Plant = planted, SwingStart = planted, SwingEnd = planted, PreviousRoot = root };
        }

        /// <summary>
        /// Stance keeps a world-space anchor. At lift-off, predict ONE bounded landing from velocity;
        /// the swing target then stays fixed, so frame rate cannot chase a moving goal. Root/velocity must
        /// describe continuous motion; after teleporting, explicitly InitializeFoot again. dt is bounded
        /// to .25 s (large pauses intentionally do not fast-forward a visible character).
        /// </summary>
        public static void StepFoot(ref FootPlantState foot, float2 root, float2 velocity, float sideOffset, float dt, float groundY)
        {
            if (!foot.Initialized) InitializeFoot(ref foot, root, new float2(root.x + sideOffset, groundY));
            dt = math.clamp(dt, 0, MaxStepSeconds);
            float consumed = 0, remaining = dt;
            // At most two transitions for a bounded dt. Constant loop bound is Burst friendly.
            for (int segment = 0; segment < 3 && remaining > 0; segment++)
            {
                bool stance = foot.InStance;
                float boundary = stance ? Stance : 1f;
                float until = math.max(0, (boundary - foot.Phase) * Period);
                float step = math.min(remaining, until);
                foot.Phase += step / Period;
                consumed += step; remaining -= step;
                if (step >= until - 1e-6f)
                {
                    float2 atEvent = math.lerp(foot.PreviousRoot, root, dt > 0 ? consumed / dt : 1);
                    if (stance)
                    {
                        foot.Phase = Stance;
                        foot.SwingStart = foot.Plant;
                        float lead = Period * (1f - Stance + Stance * .5f);
                        foot.SwingEnd = new float2(atEvent.x + math.clamp(velocity.x * lead, -.42f, .42f) + sideOffset, groundY);
                    }
                    else { foot.Phase = 0; foot.Plant = foot.SwingEnd; }
                }
            }
            if (foot.InStance) foot.Position = foot.Plant;
            else
            {
                float u = (foot.Phase - Stance) / (1f - Stance);
                foot.Position = math.lerp(foot.SwingStart, foot.SwingEnd, Ease(u));
                // Zero vertical speed at both contact and lift; peak is early enough to clear the ground.
                float arc = math.sin(math.PI * u);
                foot.Position.y += SwingHeight * arc * arc;
            }
            foot.PreviousRoot = root;
        }

        /// <summary>Exact exponential response for a held target; independent of update frequency.</summary>
        public static float2 SmoothAim(ref SmoothedAimState state, float2 target, float dt, float response = 14f)
        {
            if (!state.Initialized) { state.Initialized = true; state.Target = target; }
            else state.Target = math.lerp(state.Target, target, 1f - math.exp(-math.max(0, response) * math.max(0, dt)));
            return state.Target;
        }

        public static float2 ModelPoint(float2 world, float2 root, float facing, float scale)
        {
            float2 p = (world - root) / math.max(.001f, scale);
            p.x *= facing < 0 ? -1 : 1;
            return p;
        }

        /// <summary>Apply a world-space smoothed aim through Skeleton2D's existing local two-bone solver.</summary>
        public static void Aim(in SkeletonView rig, NativeArray<BoneLocal> pose, int upper, int lower,
            float2 target, float2 root, float facing, float scale, float bend = 1, int at = 0)
        {
            Skeletal.TwoBoneIK(rig, pose, upper, lower, ModelPoint(target, root, facing, scale), bend < 0 ? -1 : 1, at);
        }

        /// <summary>Analytic, continuous straight-line gait used for the explicitly bounded shadow pose library.</summary>
        public static float2 CanonicalFoot(float phase, float offset, float speed = .65f)
        {
            float p = math.frac(phase + offset);
            float distance = speed * Period;
            float x;
            float y = 0;
            if (p < Stance) x = distance * (Stance * .5f - p);
            else
            {
                float u = (p - Stance) / (1 - Stance);
                // World-space smooth swing; subtract continuous root travel in character space.
                x = -distance * Stance * .5f + distance * Ease(u) - distance * (p - Stance);
                float arc = math.sin(math.PI * u); y = SwingHeight * arc * arc;
            }
            return new float2(x, y + .075f);
        }

        public static float FitOrthographic(float width, float height, float aspect) => math.max(height * .5f, width * .5f / math.max(.1f, aspect));
    }
}
