using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace SPF.L1.Spatial
{
    /// <summary>
    /// Z-order (Morton) codes: interleaved cell coordinates, so cells close in 2D are mostly close in the
    /// 1D order. Sorting rows by the code of their cell keeps spatial neighbours together in memory
    /// (see <c>SimWorld.SortRows</c>).
    /// </summary>
    public static class Morton
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static uint Spread(uint x)
        {
            x &= 0xFFFF;
            x = (x | (x << 8)) & 0x00FF00FF;
            x = (x | (x << 4)) & 0x0F0F0F0F;
            x = (x | (x << 2)) & 0x33333333;
            x = (x | (x << 1)) & 0x55555555;
            return x;
        }

        /// <summary>Code of a cell with coordinates in [0, 65535] (clamped).</summary>
        public static uint Encode(int2 cell)
        {
            var c = (uint2)math.clamp(cell, 0, 0xFFFF);
            return Spread(c.x) | (Spread(c.y) << 1);
        }

        /// <summary>Code of the cell containing <paramref name="position"/> on a grid starting at <paramref name="origin"/>.</summary>
        public static uint Encode(float2 position, float2 origin, float cellSize) =>
            Encode((int2)math.floor((position - origin) / cellSize));
    }
}
