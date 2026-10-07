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

        /// <summary>Earliest simultaneous ground-circle and vertical contact over one tick.
        /// Ground positions and heights interpolate linearly and independently for both bodies.
        /// hurtBottom/hurtTop are offsets from the target's height; radius is the projectile's
        /// ground and vertical radius, targetRadius is its target's ground body radius. A target's
        /// visual width/depth never substitutes for that radius. Contacts are closed, with no
        /// geometric epsilon. Finite inputs are required; invalid geometry returns false. False
        /// means no joint contact in [0,1], and its fraction must not be used. Keep all interval
        /// comparisons in double before converting the earliest joint time to float.</summary>
        public static bool SweepProjectile(float2 from, float2 to, float heightFrom, float heightTo,
            float radius, float2 targetFrom, float2 targetTo, float targetHeightFrom,
            float targetHeightTo, float targetRadius, float hurtBottom, float hurtTop, out float fraction)
        {
            fraction = 0f;
            if (!(radius >= 0f && targetRadius >= 0f && hurtBottom <= hurtTop)) return false;
            if (!CombatSweep.CircleContactInterval(from, to, radius, targetFrom, targetTo,
                targetRadius, out double entry, out double exit)) return false;
            double height = (double)heightFrom - targetHeightFrom;
            double change = ((double)heightTo - heightFrom) - ((double)targetHeightTo - targetHeightFrom);
            double bottom = (double)hurtBottom - radius, top = (double)hurtTop + radius;
            if (change == 0d)
            {
                if (height < bottom || height > top) return false;
            }
            else
            {
                double heightEntry = (bottom - height) / change;
                double heightExit = (top - height) / change;
                if (heightEntry > heightExit)
                {
                    double swap = heightEntry; heightEntry = heightExit; heightExit = swap;
                }
                entry = math.max(entry, heightEntry);
                exit = math.min(exit, heightExit);
                if (entry > exit) return false;
            }
            fraction = (float)entry;
            return true;
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
