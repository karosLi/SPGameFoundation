using Unity.Mathematics;

namespace SPF.L2.Combat
{
    /// <summary>Allocation-free relative-motion swept-circle time of impact. Finite coordinates/radii
    /// required. Contacts are closed (including tangency/endpoints), initial overlap is t=0.
    /// False means no contact in [0,1]; its output fraction must not be used. Double intermediates avoid
    /// a world-unit-dependent motion cutoff and float square underflow; the returned fraction is float.
    /// No epsilon expands the geometry. Input float precision still bounds representable positions.</summary>
    public static class CombatSweep
    {
        public static bool Circles(float2 from, float2 to, float radius, float2 targetFrom,
            float2 targetTo, float targetRadius, out float fraction) =>
            Solve((double)from.x - targetFrom.x, (double)from.y - targetFrom.y,
                ((double)to.x - from.x) - ((double)targetTo.x - targetFrom.x),
                ((double)to.y - from.y) - ((double)targetTo.y - targetFrom.y),
                (double)math.max(0f, radius) + math.max(0f, targetRadius), out fraction);

        /// <summary>Point against a stationary circle, also suitable for an already combined radius.
        /// Radius must be nonnegative. Every nonzero representable displacement is considered.</summary>
        public static bool PointCircle(float2 from, float2 to, float2 center, float radius, out float fraction)
            => Solve((double)from.x - center.x, (double)from.y - center.y,
                (double)to.x - from.x, (double)to.y - from.y, radius, out fraction);

        static bool Solve(double mx, double my, double dx, double dy, double radius, out float fraction)
        {
            fraction = 0f;
            double rr = radius * radius, c = mx * mx + my * my - rr;
            if (c <= 0d) return true;
            double a = dx * dx + dy * dy, b = mx * dx + my * dy;
            if (a == 0d || b >= 0d) return false;
            // In 2D this equals b*b - a*c, without subtracting two large longitudinal terms.
            double cross = mx * dy - my * dx;
            double discriminant = a * rr - cross * cross;
            if (discriminant < 0d) return false;
            // Equivalent entering root, avoiding cancellation when starting just outside the circle.
            double entry = c / (-b + math.sqrt(discriminant));
            if (!(entry >= 0d && entry <= 1d)) return false;
            fraction = (float)entry;
            return true;
        }

        /// <summary>Compatibility-only original Shooter arithmetic, including its a &lt;= 1e-12 cutoff.
        /// Keep frozen/replayed legacy rules here; new consumers should use PointCircle/Circles.</summary>
        public static bool PointCircleLegacy(float2 from, float2 to, float2 center, float radius, out float fraction)
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
