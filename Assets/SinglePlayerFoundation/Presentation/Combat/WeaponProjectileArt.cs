using SPF.Presentation.Sprites;
using Unity.Mathematics;
using UnityEngine;

namespace SPF.Presentation.Combat
{
    /// <summary>Original mobile-readable projectile silhouettes, packed only by opted-in hosts.
    /// One quad per projectile; art never defines hit geometry or changes the fixed-tick trajectory.</summary>
    public struct WeaponProjectileArt
    {
        // Authored arrow head apex is pixel (60,16) in the 64×32 source rectangle.
        // Its pivot is independent of the transparent rectangle and collision radius.
        public const float ArrowTipU = 60f / 64f;
        public int Bullet, Arrow, Spell;
        public static WeaponProjectileArt AddTo(SpriteAtlasBuilder atlas) => new WeaponProjectileArt
        {
            Bullet = atlas.Add(Draw(0)), Arrow = atlas.Add(Draw(1)), Spell = atlas.Add(Draw(2))
        };
        /// <summary>Move the quad so its authored arrow tip lands on a caller-projected forward
        /// collision boundary. Project the ground-space centre + direction * radius first;
        /// a normalized screen direction times a ground radius is wrong in depth-projected games.</summary>
        public static float2 ArrowCentre(float2 projectedForwardBoundary, float2 projectedDirection, float spriteWidth)
            => projectedForwardBoundary - math.normalizesafe(projectedDirection, new float2(1, 0)) * (spriteWidth * (ArrowTipU - .5f));
        /// <summary>0: compact bullet, 1: arrow with shaft/head/fletching, 2: compact spell core.
        /// +X is forward. ArrowCentre maps the authored tip pivot to collision's forward boundary;
        /// spell/bullet cores retain a centred pivot. No broad glow quad.</summary>
        public static PixelCanvas Draw(int kind)
        {
            const int sample = 3;
            int width = kind == 1 ? 64 : 32;
            var canvas = new PixelCanvas(width * sample, 32 * sample);
            for (int y = 0; y < canvas.Height; y++) for (int x = 0; x < canvas.Width; x++)
            {
                float px = (x + .5f) / sample, py = (y + .5f) / sample;
                float dy = math.abs(py - 16);
                Color32 color = default;
                if (kind == 1)
                {
                    bool shaft = px >= 7 && px <= 48 && dy <= 1.8f;
                    bool head = px >= 45 && px <= 60 && dy <= (60 - px) * .42f;
                    bool feather = px >= 7 && px <= 20 && dy <= 2 + (20 - px) * .42f && dy >= (20 - px) * .13f;
                    if (shaft || head || feather) color = new Color32(28, 43, 49, 255);
                    if (shaft && dy < .85f) color = new Color32(245, 207, 120, 255);
                    if (head && px >= 47 && dy < (59 - px) * .29f) color = new Color32(237, 249, 234, 255);
                    if (feather && px >= 9 && px <= 18 && dy < 1 + (19 - px) * .38f) color = new Color32(110, 215, 205, 255);
                }
                else
                {
                    float2 p = new float2((px - 16) / (kind == 0 ? 13 : 14), (py - 16) / (kind == 0 ? 5 : 14));
                    float radius = math.length(p);
                    if (radius < 1)
                    {
                        float alpha = math.saturate((1 - radius) * (kind == 0 ? 9 : 2.7f));
                        bool core = radius < (kind == 0 ? .66f : .40f);
                        color = kind == 0 ? new Color32(255, core ? (byte)246 : (byte)177, core ? (byte)192 : (byte)64, (byte)math.round(alpha * 255)) :
                            new Color32(core ? (byte)229 : (byte)70, core ? (byte)255 : (byte)188, 255, (byte)math.round(alpha * 255));
                    }
                }
                canvas.Set(x, y, color);
            }
            return SmoothSpriteArt.Downsample(canvas, sample);
        }
    }
}
