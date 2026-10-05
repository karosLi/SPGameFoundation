using Unity.Mathematics;

namespace SPF.L2.Combat
{
    public struct Health
    {
        public float Current;
        public float Max;
        public bool IsDead => Current <= 0f;
        public float Fraction => Max > 0f ? math.saturate(Current / Max) : 0f;
    }

    /// <summary>Outcome of one hit, for rules and presentation (damage numbers).</summary>
    public struct DamageRoll
    {
        public float Amount;
        public bool Critical;
    }

    public static class CombatMath
    {
        /// <summary>
        /// Armour mitigation with diminishing returns: damage × k / (k + armour). Armour equal to
        /// <paramref name="armourConstant"/> halves damage; it never reaches zero.
        /// </summary>
        public static float Mitigate(float damage, float armour, float armourConstant = 50f) =>
            damage * armourConstant / (armourConstant + math.max(armour, 0f));

        /// <summary>
        /// Rolls one hit: attack ± <paramref name="spread"/> (fraction), critical with the given chance
        /// (× <paramref name="critMultiplier"/>), then armour. At least <paramref name="minimum"/> damage.
        /// Deterministic for a given random state.
        /// </summary>
        public static DamageRoll Roll(float attack, float armour, float critChance, float critMultiplier, float spread, ref Random random, float minimum = 1f)
        {
            float raw = attack * (1f + random.NextFloat(-spread, spread));
            bool crit = random.NextFloat() < critChance;
            if (crit) raw *= critMultiplier;
            return new DamageRoll { Amount = math.max(minimum, Mitigate(raw, armour)), Critical = crit };
        }

        /// <summary>True when <paramref name="target"/> lies within a melee arc (range, half-angle cosine) from origin facing <paramref name="facing"/>.</summary>
        public static bool InArc(float2 origin, float2 facing, float2 target, float targetRadius, float range, float cosHalfAngle)
        {
            float2 d = target - origin;
            float distSq = math.lengthsq(d);
            float reach = range + targetRadius;
            if (distSq > reach * reach) return false;
            if (distSq < targetRadius * targetRadius) return true;   // overlapping: always hit
            return math.dot(d * math.rsqrt(distSq), math.normalizesafe(facing, new float2(1f, 0f))) >= cosHalfAngle;
        }
    }

    /// <summary>Cooldown timer stored in a column: Ready when Remaining ≤ 0.</summary>
    public struct Cooldown
    {
        public float Remaining;
        public bool Ready => Remaining <= 0f;
        public void Tick(float dt) => Remaining = math.max(Remaining - dt, 0f);
        public void Start(float seconds) => Remaining = seconds;
    }
}
