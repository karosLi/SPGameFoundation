using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace SPF.L1.Geometry
{
    /// <summary>Burst-friendly 2D geometry helpers. Distances are compared squared wherever possible.</summary>
    public static class GeoMath
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CirclesOverlap(float2 a, float ra, float2 b, float rb)
        {
            float r = ra + rb;
            return math.distancesq(a, b) < r * r;
        }

        /// <summary>Squared distance from point p to segment ab.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float DistanceSqPointSegment(float2 p, float2 a, float2 b)
        {
            float2 ab = b - a;
            float lenSq = math.lengthsq(ab);
            float t = lenSq > 1e-12f ? math.saturate(math.dot(p - a, ab) / lenSq) : 0f;
            return math.distancesq(p, a + ab * t);
        }

        /// <summary>Does a circle moving from a to b (swept capsule) touch a static circle.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool SweptCircleHits(float2 a, float2 b, float radius, float2 center, float otherRadius)
        {
            float r = radius + otherRadius;
            return DistanceSqPointSegment(center, a, b) < r * r;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float2 SafeNormalize(float2 v, float2 fallback)
        {
            float lenSq = math.lengthsq(v);
            return lenSq > 1e-12f ? v * math.rsqrt(lenSq) : fallback;
        }

        /// <summary>2D cross product (z of the 3D cross).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Cross(float2 a, float2 b) => a.x * b.y - a.y * b.x;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float2 Rotate(float2 v, float radians)
        {
            math.sincos(radians, out float s, out float c);
            return new float2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float2 FromAngle(float radians)
        {
            math.sincos(radians, out float s, out float c);
            return new float2(c, s);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Contains(float2 min, float2 max, float2 p) => math.all(p >= min) && math.all(p <= max);
    }
}
