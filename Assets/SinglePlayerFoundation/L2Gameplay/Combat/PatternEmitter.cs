using Unity.Mathematics;

namespace SPF.L2.Combat
{
    /// <summary>Receives the bullets a pattern fires (implemented by the game as a struct: Burst, no allocation).</summary>
    public interface IBulletSink
    {
        void Emit(float2 position, float2 direction, float speed);
    }

    /// <summary>
    /// Bullet pattern for bullet-hell and shoot-'em-up games: every <see cref="Interval"/> seconds fires
    /// <see cref="Arms"/> evenly spread (or within <see cref="Spread"/>) volleys of <see cref="PerArm"/> bullets
    /// (a fan of <see cref="FanAngle"/>), the whole pattern rotating by <see cref="Spin"/> radians per shot
    /// (spirals) and optionally aimed at a target (aimed spreads). Covers rings, spirals, flowers, aimed
    /// fans and shotgun bursts with one deterministic, allocation-free struct.
    /// </summary>
    [System.Serializable]
    public struct PatternEmitter
    {
        public int Arms;          // directions around the emitter (1 = single stream)
        public float Spread;      // total angle the arms cover; 0 = full circle
        public int PerArm;        // bullets per arm and shot
        public float FanAngle;    // angle covered by one arm's bullets
        public float Speed;
        public float SpeedStep;   // extra speed per bullet within an arm (layered waves)
        public float Interval;    // seconds between shots
        public float Spin;        // rotation per shot (radians)
        public bool Aimed;        // centre the pattern on the target direction

        public static PatternEmitter Ring(int count, float speed, float interval) =>
            new PatternEmitter { Arms = count, PerArm = 1, Speed = speed, Interval = interval };

        public static PatternEmitter Spiral(int arms, float speed, float interval, float spin) =>
            new PatternEmitter { Arms = arms, PerArm = 1, Speed = speed, Interval = interval, Spin = spin };

        public static PatternEmitter AimedFan(int bullets, float fan, float speed, float interval) =>
            new PatternEmitter { Arms = 1, PerArm = bullets, FanAngle = fan, Speed = speed, Interval = interval, Aimed = true };

        /// <summary>Bullets one shot fires.</summary>
        public int BulletsPerShot => math.max(Arms, 1) * math.max(PerArm, 1);

        /// <summary>
        /// Advances the emitter's timer by <paramref name="dt"/> and fires every shot that came due (several
        /// when the interval is shorter than the step). <paramref name="angle"/> carries the spin between calls.
        /// Returns the number of shots fired.
        /// </summary>
        public int Update<TSink>(ref float timer, ref float angle, float dt, float2 origin, float2 target, ref TSink sink)
            where TSink : struct, IBulletSink
        {
            int shots = 0;
            timer -= dt;
            while (timer <= 0f && Interval > 0f && shots < 8)
            {
                timer += Interval;
                Fire(angle, origin, target, ref sink);
                angle += Spin;
                if (angle > math.PI * 2f) angle -= math.PI * 2f;
                shots++;
            }
            return shots;
        }

        /// <summary>Fires one shot at the given pattern rotation.</summary>
        public void Fire<TSink>(float rotation, float2 origin, float2 target, ref TSink sink) where TSink : struct, IBulletSink
        {
            int arms = math.max(Arms, 1), per = math.max(PerArm, 1);
            float baseAngle = rotation;
            if (Aimed)
            {
                float2 d = target - origin;
                baseAngle += math.lengthsq(d) > 1e-8f ? math.atan2(d.y, d.x) : 0f;
            }
            bool full = Spread <= 0f || Spread >= math.PI * 2f - 1e-4f;
            float armStep = full ? math.PI * 2f / arms : (arms > 1 ? Spread / (arms - 1) : 0f);
            float armStart = full ? 0f : -Spread * 0.5f;
            if (Aimed && full && arms > 1) armStart = 0f;
            float fanStep = per > 1 ? FanAngle / (per - 1) : 0f;
            float fanStart = per > 1 ? -FanAngle * 0.5f : 0f;
            for (int a = 0; a < arms; a++)
            for (int b = 0; b < per; b++)
            {
                float angle = baseAngle + armStart + a * armStep + fanStart + b * fanStep;
                math.sincos(angle, out float s, out float c);
                sink.Emit(origin, new float2(c, s), Speed + b * SpeedStep);
            }
        }
    }
}
