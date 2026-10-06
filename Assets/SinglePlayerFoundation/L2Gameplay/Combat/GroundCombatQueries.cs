using Unity.Mathematics;

namespace SPF.L2.Combat
{
    /// <summary>A belt-scroller hurt volume: X/depth ground plane and an independent vertical interval.
    /// Projection and render quality must never participate in this collision test.</summary>
    public struct GroundHurtBox
    {
        public float2 Ground;
        public float HalfWidth, HalfDepth, Bottom, Top;
    }

    public static class GroundCombatQueries
    {
        /// <summary>Closed contacts, including tangencies. Probe X and height may come from a sampled
        /// striking bone; its ground depth remains that of the attacker, not its projected screen Y.</summary>
        public static bool ProbeOverlaps(float x, float depth, float height, float radius, float depthReach, in GroundHurtBox hurt)
        {
            if (radius < 0f || depthReach < 0f || hurt.HalfWidth < 0f || hurt.HalfDepth < 0f || hurt.Top < hurt.Bottom) return false;
            return math.abs(x - hurt.Ground.x) <= hurt.HalfWidth + radius &&
                math.abs(depth - hurt.Ground.y) <= hurt.HalfDepth + depthReach &&
                height + radius >= hurt.Bottom && height - radius <= hurt.Top;
        }

        /// <summary>Simultaneous local separation contribution. Caller sums from an immutable grid,
        /// clamps total displacement, then writes a separate output buffer to avoid pair-order mutation.</summary>
        public static float2 Separation(float2 self, float2 other, float minimum, int selfOrder, int otherOrder)
        {
            float2 delta = self - other;
            float square = math.lengthsq(delta);
            if (square >= minimum * minimum) return float2.zero;
            if (square < 0.000001f) return new float2(selfOrder < otherOrder ? -minimum * .5f : minimum * .5f, 0f);
            float distance = math.sqrt(square);
            return delta * ((minimum - distance) * .5f / distance);
        }
    }
}
