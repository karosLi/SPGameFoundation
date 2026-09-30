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
            float dot = math.clamp(math.dot(heading, target), -1f, 1f);
            float angle = math.acos(dot);
            if (angle <= maxRadians)
                return target;
            float side = GeoMath.Cross(heading, target);
            float sign = side >= 0f ? 1f : -1f;
            return math.normalize(GeoMath.Rotate(heading, maxRadians * sign));
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
