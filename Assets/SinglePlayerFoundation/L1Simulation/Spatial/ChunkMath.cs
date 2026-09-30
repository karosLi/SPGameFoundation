using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace SPF.L1.Spatial
{
    /// <summary>Fixed-size square chunks over a region; the unit of loading, spawning and LOD.</summary>
    public readonly struct ChunkLayout
    {
        public readonly float2 RegionMin;
        public readonly float ChunkSize;
        public readonly int2 Dimensions;

        public ChunkLayout(float2 regionMin, float2 regionSize, float chunkSize)
        {
            RegionMin = regionMin;
            ChunkSize = chunkSize;
            Dimensions = (int2)math.ceil(regionSize / chunkSize);
        }

        public int ChunkCount => Dimensions.x * Dimensions.y;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int2 CoordOf(float2 position) =>
            math.clamp((int2)math.floor((position - RegionMin) / ChunkSize), 0, Dimensions - 1);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int IndexOf(int2 coord) => coord.y * Dimensions.x + coord.x;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int IndexOf(float2 position) => IndexOf(CoordOf(position));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int2 CoordOfIndex(int index) => new int2(index % Dimensions.x, index / Dimensions.x);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float2 MinOf(int2 coord) => RegionMin + (float2)coord * ChunkSize;

        /// <summary>Inclusive chunk range overlapped by a rectangle.</summary>
        public void Range(float2 min, float2 max, out int2 from, out int2 to)
        {
            from = CoordOf(min);
            to = CoordOf(max);
        }
    }
}
