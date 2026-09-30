using System.Runtime.CompilerServices;
using SPF.L1.Geometry;
using Unity.Mathematics;

namespace SPF.L1.Movement
{
    public static class Steering
    {
        /// <summary>
        /// Rotates a unit heading toward a unit target direction by at most maxRadians.
        /// Returns a unit vector. Handles the exactly-opposite case by turning left.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float2 TurnTowards(float2 heading, float2 target, float maxRadians)
        {
            // angle <= max  <=>  dot >= cos(max): one sincos, no acos. Rotating a unit vector keeps it
            // unit; the renormalisation only stops drift from accumulating over many ticks.
            if (maxRadians >= math.PI)
                return target;
            math.sincos(maxRadians, out float s, out float c);
            if (math.dot(heading, target) >= c)
                return target;
            if (GeoMath.Cross(heading, target) < 0f)
                s = -s;
            float2 turned = new float2(heading.x * c - heading.y * s, heading.x * s + heading.y * c);
            return turned * math.rsqrt(math.lengthsq(turned));
        }

        /// <summary>Turn rate that decreases with body radius: big snakes turn slower.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float TurnRateForRadius(float baseTurnRate, float radius, float referenceRadius, float falloff)
        {
            float ratio = math.max(radius / math.max(referenceRadius, 1e-3f), 1f);
            return baseTurnRate / (1f + (ratio - 1f) * falloff);
        }
    }
}
