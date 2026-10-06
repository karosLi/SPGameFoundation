using SPF.Presentation.Sprites;
using Unity.Mathematics;
using UnityEngine;

namespace SPF.Presentation.Combat
{
    public struct CombatVfxUv { public float4 Core, Glow, Ring, Streak; }

    /// <summary>Four original analytic masks packed into the consumer's existing atlas at loading.</summary>
    public struct CombatVfxArt
    {
        public int Core, Glow, Ring, Streak;
        public static CombatVfxArt AddTo(SpriteAtlasBuilder atlas) => new CombatVfxArt
        {
            Core = atlas.Add(Mask(0)), Glow = atlas.Add(Mask(1)), Ring = atlas.Add(Mask(2)), Streak = atlas.Add(Mask(3))
        };
        public CombatVfxUv Resolve(SpriteSheet sheet) => new CombatVfxUv
        { Core = sheet[Core].Uv, Glow = sheet[Glow].Uv, Ring = sheet[Ring].Uv, Streak = sheet[Streak].Uv };

        public static PixelCanvas Mask(int kind)
        {
            const int size = 64;
            var c = new PixelCanvas(size, size);
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float2 p = (new float2(x, y) + 0.5f) / size * 2f - 1f;
                float r = math.length(p), alpha;
                if (kind == 1) { float f = math.saturate(1f - r); alpha = f * f * (3f - 2f * f); }
                else if (kind == 2) alpha = math.saturate(1f - math.abs(r - 0.78f) / 0.045f);
                else if (kind == 3) alpha = math.saturate((1f - math.abs(p.x)) * 5f) * math.saturate((0.65f * (1f - math.abs(p.x)) - math.abs(p.y)) * 12f);
                else
                {
                    float diamond = math.abs(p.x) + math.abs(p.y);
                    alpha = math.max(math.saturate((0.72f - diamond) * 10f), math.max(
                        math.saturate((0.08f - math.abs(p.x)) * 30f) * math.saturate((0.95f - math.abs(p.y)) * 6f),
                        math.saturate((0.08f - math.abs(p.y)) * 30f) * math.saturate((0.95f - math.abs(p.x)) * 6f)));
                }
                c.Pixels[y * size + x] = new Color32(255, 255, 255, (byte)math.round(alpha * 255f));
            }
            return c;
        }
    }
}
