using SPF.Contracts;
using SPF.Contracts.Combat;
using Unity.Mathematics;

namespace SPF.L2.Combat
{
    /// <summary>Derived shape profile for the existing weapon adapters, not a new save/config schema.
    /// Ground contact is circular; belt hurt height is an independent interval. Boundary is closed,
    /// geometry padding is zero. Broad-phase padding belongs to the caller and is not a hurtbox epsilon.</summary>
    public struct ProjectileCollisionProfile
    {
        public float Radius, TargetGroundRadius, HurtBottom, HurtTop;
        public CombatBoundaryRule Boundary => CombatBoundaryRule.Closed;
        public bool SweepGroundHeight(float2 from, float2 to, float height, float2 targetFrom,
            float2 targetTo, float previousHeight, float currentHeight, out float fraction) =>
            GroundCombatQueries.SweepProjectile(from, to, height, height, Radius, targetFrom, targetTo,
                previousHeight, currentHeight, TargetGroundRadius, HurtBottom, HurtTop, out fraction);
    }

    public static class CombatCollisionPolicy
    {
        public static CombatContactReason Filter(EntityHandle owner, EntityHandle target, int ownerTeam,
            int targetTeam, bool dead, bool invulnerable)
        {
            if (owner == target) return CombatContactReason.Self;
            if (ownerTeam == targetTeam) return CombatContactReason.Friendly;
            if (dead) return CombatContactReason.Dead;
            if (invulnerable) return CombatContactReason.Invulnerable;
            return CombatContactReason.Candidate;
        }
        public static bool Before(float fraction, EntityHandle target, float bestFraction, EntityHandle bestTarget, bool hasBest)
            => !hasBest || fraction < bestFraction || fraction == bestFraction &&
                (target.Index < bestTarget.Index || target.Index == bestTarget.Index && target.Generation < bestTarget.Generation);
        public static CombatContactReason HistoryReason(HitRecordResult result) =>
            result == HitRecordResult.Full ? CombatContactReason.HistoryFull :
            result == HitRecordResult.InvalidTarget ? CombatContactReason.InvalidTarget :
            result == HitRecordResult.InvalidScope ? CombatContactReason.InvalidScope : CombatContactReason.Duplicate;
    }
}
