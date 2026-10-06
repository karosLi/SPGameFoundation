using Unity.Mathematics;

namespace SPF.L2.Combat
{
    /// <summary>Allocation-free relative-motion swept-circle time of impact. Finite coordinates/radii
    /// required. Contacts are closed (including tangency/endpoints), initial overlap is t=0.
    /// False means no contact in [0,1]; its output fraction must not be used.</summary>
    public static class CombatSweep
    {
        public static bool Circles(float2 from, float2 to, float radius, float2 targetFrom,
            float2 targetTo, float targetRadius, out float fraction) =>
            PointCircle(from - targetFrom, to - targetTo, float2.zero,
                math.max(0f, radius) + math.max(0f, targetRadius), out fraction);

        /// <summary>Point against a stationary circle, also suitable for an already combined radius.
        /// Radius must be nonnegative. Keeps the original Shooter sweep arithmetic/order.</summary>
        public static bool PointCircle(float2 from, float2 to, float2 center, float radius, out float fraction)
        {
            float2 d = to - from, m = from - center;
            float c = math.dot(m, m) - radius * radius;
            if (c <= 0f) { fraction = 0f; return true; }
            float a = math.dot(d, d), b = math.dot(m, d), discriminant = b * b - a * c;
            if (a <= 1e-12f || b > 0f || discriminant < 0f) { fraction = 0f; return false; }
            fraction = (-b - math.sqrt(discriminant)) / a;
            return fraction >= 0f && fraction <= 1f;
        }
    }
}
