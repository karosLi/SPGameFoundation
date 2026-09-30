using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace SPF.Contracts
{
    /// <summary>
    /// Reproducible random streams derived from (session seed, tick, stream). Any job can create its own
    /// generator per element without shared state, so results do not depend on thread scheduling.
    /// </summary>
    public static class SimRandom
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Random Create(uint seed, uint tick, uint stream)
        {
            uint h = math.hash(new uint3(seed, tick, stream));
            return new Random(h == 0 ? 0x6E624EB7u : h);
        }
    }
}
