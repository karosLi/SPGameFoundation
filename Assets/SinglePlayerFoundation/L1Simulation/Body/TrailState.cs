using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Mathematics;

namespace SPF.L1.Body
{
    /// <summary>
    /// Ring buffer of path points behind a chain head, stored in a slab of <see cref="BodyStore"/>.
    /// Points are pushed exactly <c>spacing</c> apart along the path the head travelled, so point
    /// <c>g</c> (global index, counting from the first point ever pushed) sits at arc length
    /// <c>g * spacing</c>. Sampling any body position is O(1) and needs no per-node state.
    /// </summary>
    public struct TrailState
    {
        /// <summary>First slot of this trail's slab in BodyStore.Points.</summary>
        public int Start;

        /// <summary>Slab size, power of two.</summary>
        public int Capacity;

        /// <summary>Total points ever pushed. The newest point has global index Pushed - 1.</summary>
        public uint Pushed;

        /// <summary>Points kept alive for the body (≤ Capacity, ≤ Pushed).</summary>
        public int Count;

        /// <summary>Copy of the newest point.</summary>
        public float2 Last;

        /// <summary>
        /// Bumped whenever existing points are rewritten or moved (reset, slab resize, teleport), so
        /// mirrors (e.g. a GPU copy) know to re-upload instead of appending new points.
        /// </summary>
        public uint Version;

        public bool IsAllocated => Capacity > 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Slot(uint globalIndex) => Start + (int)(globalIndex & (uint)(Capacity - 1));
    }

    public static class TrailMath
    {
        /// <summary>Number of trail points needed to represent a body of the given arc length.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int PointsForLength(float length, float spacing) => (int)math.ceil(length / spacing) + 2;

        /// <summary>
        /// Pushes points toward the head until the head is less than one spacing away from the newest
        /// point. Writes only inside this trail's slab, so trails of different chains can advance in parallel.
        /// </summary>
        public static void Advance(ref TrailState trail, NativeArray<float2> points, float2 head, float spacing, int desiredCount)
        {
            float2 delta = head - trail.Last;
            float lengthSq = math.lengthsq(delta);
            float spacingSq = spacing * spacing;
            // Guard against teleports producing thousands of points in one tick.
            int budget = trail.Capacity;
            while (lengthSq >= spacingSq && budget-- > 0)
            {
                float2 next = trail.Last + delta * (spacing * math.rsqrt(lengthSq));
                points[trail.Slot(trail.Pushed)] = next;
                trail.Pushed++;
                trail.Last = next;
                delta = head - next;
                lengthSq = math.lengthsq(delta);
            }
            if (lengthSq >= spacingSq)
            {
                // Budget exhausted (teleport): restart the trail at the head.
                Reset(ref trail, points, head, math.normalizesafe(-delta, new float2(-1f, 0f)), spacing, math.min(desiredCount, trail.Capacity));
                return;
            }
            trail.Count = math.min(math.min(desiredCount, trail.Capacity), (int)math.min(trail.Pushed, (uint)int.MaxValue));
        }

        /// <summary>Lays the trail as a straight line behind the head (spawn, teleport).</summary>
        public static void Reset(ref TrailState trail, NativeArray<float2> points, float2 head, float2 backDirection, float spacing, int count)
        {
            count = math.clamp(count, 1, trail.Capacity);
            float2 back = math.normalizesafe(backDirection, new float2(-1f, 0f));
            // Oldest first so the newest point ends up closest to the head.
            for (int i = count - 1; i >= 0; i--)
            {
                points[trail.Slot(trail.Pushed)] = head + back * (spacing * i);
                trail.Pushed++;
            }
            trail.Last = head;
            trail.Count = count;
            trail.Version++;
        }

        /// <summary>Arc length of the body from the head to the oldest kept point.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float BodyLength(in TrailState trail, float2 head, float spacing) =>
            math.length(head - trail.Last) + math.max(trail.Count - 1, 0) * spacing;

        /// <summary>Position at arc distance <paramref name="distance"/> behind the head.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float2 SampleBehind(in TrailState trail, NativeArray<float2> points, float2 head, float distance, float spacing) =>
            SampleAtArc(trail, points, head, math.length(head - trail.Last), distance, spacing);

        /// <summary>
        /// General form used for interpolation: <paramref name="headArc"/> is the head's arc position
        /// relative to the newest point (the head gap for the current tick; can be negative for an
        /// interpolated head that lies before the newest point).
        /// </summary>
        public static float2 SampleAtArc(in TrailState trail, NativeArray<float2> points, float2 head, float headArc, float distance, float spacing)
        {
            float a = headArc - distance;
            if (a >= 0f)
                return headArc > 1e-6f ? math.lerp(trail.Last, head, a / headArc) : head;

            float along = -a / spacing;
            int k = (int)along;
            int maxK = trail.Count - 1;
            uint newest = trail.Pushed - 1;
            if (k >= maxK)
                return points[trail.Slot(newest - (uint)math.max(maxK, 0))];
            float2 p0 = points[trail.Slot(newest - (uint)k)];
            float2 p1 = points[trail.Slot(newest - (uint)k - 1)];
            return math.lerp(p0, p1, along - k);
        }

        /// <summary>
        /// Arc position of the previous tick's head relative to the current newest point, so renderers
        /// can interpolate between ticks: lerp(PrevArc, currentGap, alpha).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float PreviousArc(uint pushedBefore, float gapBefore, uint pushedAfter, float spacing) =>
            gapBefore - (pushedAfter - pushedBefore) * spacing;

        /// <summary>Number of body nodes (including the head) for a node spacing.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int NodeCount(in TrailState trail, float2 head, float spacing, float nodeSpacing) =>
            (int)(BodyLength(trail, head, spacing) / nodeSpacing) + 1;
    }
}
