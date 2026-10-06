using System.Runtime.CompilerServices;
using SPF.L1.Geometry;
using Unity.Mathematics;

namespace SPF.L2.Combat
{
    /// <summary>
    /// CPU-authoritative, allocation-free skill-area tests for finite world coordinates. Boundaries are
    /// inclusive: exact tangency is a hit. Negative radii clamp to zero; swapped annulus radii normalize.
    /// Broad-phase bounds enclose the area itself: expand by the maximum target radius when querying
    /// a grid which stores target centres only. A hit test does not deduplicate damage or decide hit order.
    /// </summary>
    public static class CombatShapes
    {
        /// <summary>A finite, round-ended beam (capsule), including a zero-length circular burst.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool BeamHitsCircle(float2 start, float2 end, float halfWidth, float2 target, float targetRadius)
        {
            float radius = math.max(0f, halfWidth) + math.max(0f, targetRadius);
            return GeoMath.DistanceSqPointSegment(target, start, end) <= radius * radius;
        }

        /// <summary>A closed ring band. Targets wholly inside the hole are excluded; touching it hits.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool AnnulusHitsCircle(float2 origin, float innerRadius, float outerRadius, float2 target, float targetRadius)
        {
            float inner = math.max(0f, math.min(innerRadius, outerRadius));
            float outer = math.max(0f, math.max(innerRadius, outerRadius));
            float radius = math.max(0f, targetRadius);
            float distanceSq = math.distancesq(origin, target);
            float far = outer + radius;
            float near = math.max(0f, inner - radius);
            return distanceSq <= far * far && distanceSq >= near * near;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void BeamBounds(float2 start, float2 end, float halfWidth, out float2 min, out float2 max)
        {
            float radius = math.max(0f, halfWidth);
            min = math.min(start, end) - radius;
            max = math.max(start, end) + radius;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AnnulusBounds(float2 origin, float innerRadius, float outerRadius, out float2 min, out float2 max)
        {
            float radius = math.max(0f, math.max(innerRadius, outerRadius));
            min = origin - radius;
            max = origin + radius;
        }
    }
}
