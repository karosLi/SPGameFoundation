using SPF.Presentation.Sprites;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SurvivorFoundation.Presentation
{
    /// <summary>Stable back-to-front merge sort for alpha-covered actors. Caller owns scratch storage;
    /// ties preserve insertion order. Sorting packed instances preserves the same order on both tiers.</summary>
    [BurstCompile(CompileSynchronously = true)]
    public struct SvSpriteOrder : IJob
    {
        public NativeArray<PackedSprite> Sprites, Scratch;
        public int Count;
        public void Execute()
        {
            int count = math.min(Count, math.min(Sprites.Length, Scratch.Length));
            for (int width = 1; width < count; width *= 2)
            {
                for (int start = 0; start < count; start += width * 2)
                {
                    int mid = math.min(start + width, count), end = math.min(start + width * 2, count);
                    int a = start, b = mid;
                    for (int n = start; n < end; n++)
                        Scratch[n] = a < mid && (b >= end || Sprites[a].Depth >= Sprites[b].Depth) ? Sprites[a++] : Sprites[b++];
                }
                for (int n = 0; n < count; n++) Sprites[n] = Scratch[n];
            }
        }
    }
}
