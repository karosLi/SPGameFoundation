using SPF.Contracts;
using Unity.Mathematics;

namespace SurvivorFoundation.Presentation
{
    /// <summary>Presentation phase follows a stable entity identity, never its compacted table row.
    /// Pure math is Burst-compatible and independent of simulation RNG / quality settings.</summary>
    public static class SvVisualMotion
    {
        public static float Phase(EntityHandle handle)
        {
            uint seed = ((uint)handle.Index + 1u) * 0x9e3779b9u ^ (uint)handle.Generation * 0x85ebca6bu;
            seed ^= seed >> 16; seed *= 0x7feb352du; seed ^= seed >> 15;
            return (seed & 0xffffu) * (2f * math.PI / 65536f);
        }
        public static int Frame(float time, EntityHandle handle, bool moving, bool smooth)
            => smooth && !moving ? 0 : ((int)(time * 5f + Phase(handle) / math.PI)) & 1;
        public static float Wobble(float time, EntityHandle handle, bool moving)
            => moving ? math.sin(time * 7f + Phase(handle)) * 0.025f : 0f;
    }
}
