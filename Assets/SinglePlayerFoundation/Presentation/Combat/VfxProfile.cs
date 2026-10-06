using System;
using Unity.Mathematics;

namespace SPF.Presentation.Combat
{
    /// <summary>Weapon-owned presentation data. Id is a caller-assigned binding, not a global weapon enum.
    /// Configure once at loading; effects copy this value and never hold a delegate or spawn an object.</summary>
    [Serializable]
    public struct VfxProfile
    {
        public int Id;
        public byte Priority, Sparks;
        public float Duration, GlowSize, RingSize, SparkTravel, MergeRadius, MergeSeconds;
        public float2 CoreSize;
        public float4 CoreColor, AccentColor;

        public static VfxProfile Impact => new VfxProfile
        {
            Id = 1, Priority = 1, Sparks = 4, Duration = 0.24f, CoreSize = new float2(0.45f),
            GlowSize = 0.9f, RingSize = 0.65f, SparkTravel = 0.55f, MergeRadius = 0.55f, MergeSeconds = 0.065f,
            CoreColor = new float4(1f, 0.96f, 0.76f, 1f), AccentColor = new float4(1f, 0.63f, 0.23f, 0.9f)
        };
        public static VfxProfile Muzzle
        {
            get { var p = Impact; p.Id = 2; p.Priority = 2; p.Duration = 0.09f; p.CoreSize = new float2(0.22f, 0.48f); p.RingSize = 0; p.GlowSize = 0.65f; p.Sparks = 0; p.MergeRadius = 0.1f; return p; }
        }
        public static VfxProfile Destruction
        {
            get { var p = Impact; p.Id = 3; p.Priority = 2; p.Duration = 0.42f; p.CoreSize = new float2(0.75f); p.RingSize = 1.6f; p.GlowSize = 1.6f; p.Sparks = 7; p.SparkTravel = 1.1f; return p; }
        }
        public static VfxProfile Electric
        {
            get { var p = Impact; p.Id = 4; p.CoreSize = new float2(0.3f); p.RingSize = 0.45f; p.GlowSize = 0.6f; p.Sparks = 3; p.SparkTravel = 0.38f; p.CoreColor = new float4(0.78f, 1f, 1f, 1f); p.AccentColor = new float4(0.18f, 0.8f, 1f, 0.8f); return p; }
        }
        public static VfxProfile Pulse
        {
            get { var p = Electric; p.Id = 6; p.Priority = 2; p.Duration = 0.4f; p.CoreSize = new float2(0.6f); p.RingSize = 3f; p.GlowSize = 1.6f; p.Sparks = 7; p.SparkTravel = 1.4f; return p; }
        }
        public static VfxProfile HeroHurt
        {
            get { var p = Impact; p.Id = 5; p.Priority = 3; p.RingSize = 1.2f; p.AccentColor = new float4(1f, 0.3f, 0.2f, 0.8f); return p; }
        }
    }

    /// <summary>Budget counts submitted transparent quad area / world-view area, including overlaps.
    /// It is a conservative fill-work proxy, not a hardware GPU time or exact fragment count.</summary>
    public struct VfxBudget
    {
        public int Active, Emissions, Sprites, SparksPerEffect;
        public float ScreenArea;
        public bool Glow;
        public static VfxBudget ForQuality(int quality)
        {
            switch (math.clamp(quality, 0, 3))
            {
                case 0: return new VfxBudget { Active = 96, Emissions = 48, Sprites = 384, SparksPerEffect = 7, ScreenArea = 0.30f, Glow = true };
                case 1: return new VfxBudget { Active = 64, Emissions = 32, Sprites = 256, SparksPerEffect = 4, ScreenArea = 0.20f, Glow = true };
                case 2: return new VfxBudget { Active = 40, Emissions = 20, Sprites = 128, SparksPerEffect = 2, ScreenArea = 0.12f };
                default: return new VfxBudget { Active = 24, Emissions = 12, Sprites = 48, SparksPerEffect = 0, ScreenArea = 0.07f };
            }
        }
    }

    public enum VfxAdmission : byte { Accepted, Merged, Duplicate, Dropped }
    public struct VfxDiagnostics
    {
        public int Accepted, Merged, Duplicates, Dropped, Evicted, Expired, Sprites, SpriteDrops;
        public float ScreenArea;
    }
}
