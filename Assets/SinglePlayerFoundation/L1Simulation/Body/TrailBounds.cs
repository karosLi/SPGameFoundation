using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Mathematics;

namespace SPF.L1.Body
{
    /// <summary>
    /// Incremental bounding boxes of trails. Every complete block of <see cref="BlockSize"/> trail points
    /// (global indices [16b, 16b + 16)) has its AABB stored in <see cref="BodyStore.BlockBounds"/> at a slot
    /// parallel to the trail's slab, written once when the block completes. A trail's bounds are then the
    /// union of about Count / 16 stored boxes plus a direct scan of the newest partial block, instead of
    /// a scan over every point each tick. Blocks are rebuilt when the trail was rewritten
    /// (<see cref="TrailState.Version"/> changed). The oldest block may still include a few expired points,
    /// so bounds are conservative by at most one block of path.
    /// </summary>
    public static class TrailBounds
    {
        public const int BlockShift = 4;
        public const int BlockSize = 1 << BlockShift;   // = BodyStore.MinSlab, so every slab holds whole blocks

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int Slot(in TrailState trail, uint block) =>
            (trail.Start >> BlockShift) + (int)(block & (uint)((trail.Capacity >> BlockShift) - 1));

        static float4 Scan(in TrailState trail, NativeArray<float2> points, uint from, uint to, float4 box)
        {
            for (uint g = from; g < to; g++)
            {
                float2 p = points[trail.Slot(g)];
                box = new float4(math.min(box.xy, p), math.max(box.zw, p));
            }
            return box;
        }

        static readonly float4 Empty = new float4(float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);

        /// <summary>Recomputes the boxes of every complete block in the kept range (after a rewrite).</summary>
        public static void Rebuild(in TrailState trail, NativeArray<float2> points, NativeArray<float4> blocks)
        {
            if (trail.Count <= 0) return;
            uint oldest = trail.Pushed - (uint)trail.Count;
            for (uint b = oldest >> BlockShift; b < trail.Pushed >> BlockShift; b++)
                blocks[Slot(trail, b)] = Scan(trail, points, math.max(b << BlockShift, oldest), (b + 1) << BlockShift, Empty);
        }

        /// <summary>Stores the boxes of blocks completed by the points pushed since <paramref name="pushedBefore"/>.</summary>
        public static void Append(in TrailState trail, NativeArray<float2> points, NativeArray<float4> blocks, uint pushedBefore)
        {
            uint first = pushedBefore >> BlockShift, end = trail.Pushed >> BlockShift;
            if (first >= end) return;
            uint ring = (uint)(trail.Capacity >> BlockShift);
            if (end - first > ring) first = end - ring;
            uint oldest = trail.Pushed - (uint)trail.Count;
            for (uint b = first; b < end; b++)
                blocks[Slot(trail, b)] = Scan(trail, points, math.max(b << BlockShift, oldest), (b + 1) << BlockShift, Empty);
        }

        /// <summary>AABB (min.xy, max.zw) of the kept points and the head.</summary>
        public static float4 Compute(in TrailState trail, NativeArray<float2> points, NativeArray<float4> blocks, float2 head)
        {
            var box = new float4(head, head);
            if (trail.Count <= 0) return box;
            uint oldest = trail.Pushed - (uint)trail.Count;
            uint end = trail.Pushed >> BlockShift;
            uint ring = (uint)(trail.Capacity >> BlockShift);
            for (uint b = oldest >> BlockShift; b < end; b++)
            {
                if (end - b > ring)
                {
                    // Its slot was reused by a newer block (only possible for the oldest block of a full slab).
                    box = Scan(trail, points, math.max(b << BlockShift, oldest), (b + 1) << BlockShift, box);
                    continue;
                }
                float4 stored = blocks[Slot(trail, b)];
                box = new float4(math.min(box.xy, stored.xy), math.max(box.zw, stored.zw));
            }
            return Scan(trail, points, math.max(end << BlockShift, oldest), trail.Pushed, box);
        }
    }
}
