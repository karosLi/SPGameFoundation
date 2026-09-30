namespace SPF.L2.Skills
{
    /// <summary>A projectile skill: cooldown and cost, what it fires and what a hit does.</summary>
    public struct SkillDefinition
    {
        public float Cooldown;
        public float MassCost;
        public float MinMass;
        public float ProjectileSpeed;
        public float ProjectileLife;
        public float ProjectileRadius;
        public byte HitBuffKind;
        public float HitMassLoss;
    }

    public static class SkillRules
    {
        public static bool CanCast(in SkillDefinition skill, float cooldownRemaining, float mass) =>
            cooldownRemaining <= 0f && mass >= skill.MinMass && mass > skill.MassCost;
    }
}
