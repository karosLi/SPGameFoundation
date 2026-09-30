namespace SPF.L2.Collision
{
    public enum CollisionOutcome : byte
    {
        None = 0,
        FirstDies = 1,
        SecondDies = 2,
        BothDie = 3,
    }

    /// <summary>Generic rules for chain-vs-chain contact, shared by chain-based modes.</summary>
    public static class ChainCollisionRules
    {
        /// <summary>Heads touching: the lighter chain dies; near-equal masses (within tolerance) both die.</summary>
        public static CollisionOutcome HeadOn(float massA, float massB, float relativeTolerance)
        {
            float max = massA > massB ? massA : massB;
            float diff = massA - massB;
            if (diff < 0f) diff = -diff;
            if (max <= 0f || diff <= max * relativeTolerance)
                return CollisionOutcome.BothDie;
            return massA < massB ? CollisionOutcome.FirstDies : CollisionOutcome.SecondDies;
        }
    }
}
