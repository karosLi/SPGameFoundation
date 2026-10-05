using System;
using System.Collections.Generic;
using SPF.L2.Buffs;
using SPF.L2.Growth;
using SPF.L2.Props;
using SPF.L2.Skills;
using Unity.Mathematics;
using UnityEngine;

namespace SnakeFoundation
{
    /// <summary>
    /// Designer-facing configuration of the snake mode. Baked at session start into a
    /// <see cref="SnakeRuntimeConfig"/> (blittable settings + native arrays) that jobs read directly.
    /// Every field has a sensible default so a config created in code is immediately playable.
    /// </summary>
    [CreateAssetMenu(menuName = "SPF/Snake/Snake Config", fileName = "SnakeConfig")]
    public sealed class SnakeConfig : ScriptableObject
    {
        public MovementSection Movement = new MovementSection();
        public BodySection Body = new BodySection();
        public FoodSection Food = new FoodSection();
        public CollisionSection Collision = new CollisionSection();
        public AISection AI = new AISection();
        public SkillSection Skill = new SkillSection();
        public List<BuffEntry> Buffs = BuffEntry.Defaults();
        public List<PropEntry> Props = PropEntry.Defaults();
        public List<RegionEntry> Regions = RegionEntry.Defaults();
        public List<PortalEntry> Portals = PortalEntry.Defaults();
        public List<SkinEntry> Skins = SkinEntry.Defaults();
        public CapacitySection Capacity = new CapacitySection();
        public List<string> AINames = new List<string>(DefaultNames);

        public static SnakeConfig CreateDefault()
        {
            var config = CreateInstance<SnakeConfig>();
            config.name = "SnakeConfig (default)";
            return config;
        }

        [Serializable]
        public sealed class MovementSection
        {
            [Min(0.1f)] public float BaseSpeed = 9f;
            [Min(0.1f)] public float BoostSpeed = 18f;
            [Tooltip("Radians per second at reference radius")] public float TurnRate = 4.5f;
            public float TurnReferenceRadius = 0.6f;
            [Range(0f, 2f)] public float TurnFalloff = 0.35f;
            public float MinBoostMass = 12f;
            public float BoostDrainPerSecond = 3f;
            [Tooltip("Mass accumulated by boosting before a food pellet is dropped at the tail")] public float BoostDropMass = 1f;
        }

        [Serializable]
        public sealed class BodySection
        {
            [Tooltip("Distance between trail points")] public float TrailSpacing = 0.4f;
            [Tooltip("Body node spacing as a fraction of radius")] public float NodeSpacingFactor = 0.55f;
            public float StartMass = 10f;
            public float MinMass = 6f;
            public float BaseLength = 6f;
            public float LengthPerMass = 0.35f;
            public float MaxLength = 360f;
            public float BaseRadius = 0.5f;
            public float RadiusPerSqrtMass = 0.09f;
            public float MaxRadius = 4f;
        }

        [Serializable]
        public sealed class FoodSection
        {
            public int FoodPerChunk = 150;
            public float MassPerFoodValue = 0.4f;
            public float ValueMin = 1f;
            public float ValueMax = 2.5f;
            public float BaseRadius = 0.22f;
            public float RadiusPerSqrtValue = 0.12f;
            public float EatRange = 0.35f;
            [Range(0f, 1f)] public float DeathDropRatio = 0.7f;
            public int MaxDropsPerDeath = 60;
            public int ReplenishChunksPerTick = 8;
            public int ReplenishPerChunkPerTick = 4;
            public int PropsPerChunk = 1;
        }

        [Serializable]
        public sealed class CollisionSection
        {
            [Range(0f, 0.5f)] public float HeadOnTolerance = 0.05f;
            public float SpawnProtection = 2f;
            public float PortalProtection = 2f;
            [Range(0.5f, 1.2f)] public float HitRadiusScale = 0.8f;
        }

        [Serializable]
        public sealed class AISection
        {
            public int SnakesPerRegion = 150;
            public int SpawnsPerTick = 10;
            [Tooltip("Ticks between AI decisions (adaptive quality may raise it)")] public int DecisionIntervalTicks = 6;
            public float ThreatRadius = 30f;
            public float HuntRadius = 25f;
            public float FoodSearchRadius = 18f;
            public float WanderRadius = 150f;
            public float FarGrowthPerSecond = 0.15f;
            public float StartMassMin = 10f;
            public float StartMassMax = 60f;
            public float MaxMass = 400f;
            public float MinSpawnDistanceFromPlayer = 60f;
            [Tooltip("Share of AI spawned in a ring around the player / camera focus so the action stays nearby")]
            [Range(0f, 1f)] public float SpawnNearFocusRatio = 0.4f;
            public float SpawnRingMin = 120f;
            public float SpawnRingMax = 500f;
            [Tooltip("Chance that a wandering AI picks a target near the focus")]
            [Range(0f, 1f)] public float WanderTowardFocusChance = 0.4f;
            public float FocusWanderRadius = 350f;
        }

        [Serializable]
        public sealed class SkillSection
        {
            public float Cooldown = 3f;
            public float MassCost = 2f;
            public float MinMass = 20f;
            public float ProjectileSpeed = 40f;
            public float ProjectileLife = 1.2f;
            public float ProjectileRadius = 0.5f;
            [Tooltip("Buff kind (1-based index into Buffs) applied on hit")] public int HitBuffKind = 4;
            public float HitMassLoss = 4f;
        }

        [Serializable]
        public sealed class CapacitySection
        {
            public int Snakes = 320;
            [Tooltip("Worst case: small map, density 1.4 x 150 per chunk over ~100 window chunks = 21k")]
            public int Food = 24000;
            public int Props = 512;
            public int Projectiles = 512;
            public int AverageTrailPoints = 768;
            public int MaxTrailPoints = 1024;
            public int BodyGridEntries = 65536;
            public int GridCells = 256;
            [Tooltip("Window size = GridCells x GridCellSize (both grids cover the same window)")]
            public float GridCellSize = 4f;
            [Tooltip("Body grid cell size (0 = GridCellSize). Larger cells: cheaper rebuild, more entries per query. 8 m measured 0.229 vs 0.270 ms per tick at 4 m (query cost unchanged)")]
            public float BodyGridCellSize = 8f;
            [Tooltip("Body nodes with a larger radius go to a coarse second grid layer so one big snake does not widen every query (0 = single layer)")]
            public float LargeBodyRadius = 0f;
            [Tooltip("Item grid cell size; the item grid covers the same window as the body grid with fewer, larger cells (items are small and static, eat / food-scan queries are wide)")]
            public float ItemGridCellSize = 8f;
            public float ChunkSize = 125f;
            public int EventQueue = 4096;
        }

        [Serializable]
        public sealed class BuffEntry
        {
            public string Name;
            public BuffStat Stat;
            public float Value;
            public float Duration;

            public static List<BuffEntry> Defaults() => new List<BuffEntry>
            {
                new BuffEntry { Name = "Speed", Stat = BuffStat.SpeedMultiplier, Value = 1.35f, Duration = 6f },
                new BuffEntry { Name = "Magnet", Stat = BuffStat.MagnetRadius, Value = 6f, Duration = 8f },
                new BuffEntry { Name = "Shield", Stat = BuffStat.Shield, Value = 1f, Duration = 10f },
                new BuffEntry { Name = "Slow", Stat = BuffStat.SpeedMultiplier, Value = 0.6f, Duration = 2.5f },
            };
        }

        [Serializable]
        public sealed class PropEntry
        {
            public string Name;
            [Tooltip("1-based index into Buffs")] public int BuffKind;
            public float Radius = 0.9f;
            public float SpawnWeight = 1f;

            public static List<PropEntry> Defaults() => new List<PropEntry>
            {
                new PropEntry { Name = "Speed", BuffKind = 1, SpawnWeight = 1f },
                new PropEntry { Name = "Magnet", BuffKind = 2, SpawnWeight = 1f },
                new PropEntry { Name = "Shield", BuffKind = 3, SpawnWeight = 0.5f },
            };
        }

        [Serializable]
        public sealed class RegionEntry
        {
            public string Name;
            public Vector2 Min;
            public Vector2 Max;
            [Tooltip("Food density multiplier relative to Food.FoodPerChunk")] public float FoodDensity = 1f;

            public static List<RegionEntry> Defaults() => new List<RegionEntry>
            {
                new RegionEntry { Name = "Mainland", Min = new Vector2(-3750, -3750), Max = new Vector2(3750, 3750), FoodDensity = 1f },
                // A quarter of the mainland's area (half the side length).
                new RegionEntry { Name = "Arena", Min = new Vector2(-1875, -1875), Max = new Vector2(1875, 1875), FoodDensity = 1.4f },
            };
        }

        [Serializable]
        public sealed class PortalEntry
        {
            public int FromRegion;
            public Vector2 Position;
            public int ToRegion;
            public Vector2 Arrival;
            public float Radius = 6f;

            public static List<PortalEntry> Defaults() => new List<PortalEntry>
            {
                new PortalEntry { FromRegion = 0, Position = new Vector2(120, 0), ToRegion = 1, Arrival = new Vector2(0, -40) },
                new PortalEntry { FromRegion = 0, Position = new Vector2(-1500, 1500), ToRegion = 1, Arrival = new Vector2(0, -40) },
                new PortalEntry { FromRegion = 1, Position = new Vector2(0, 120), ToRegion = 0, Arrival = new Vector2(120, -40) },
            };
        }

        [Serializable]
        public sealed class SkinEntry
        {
            public string Name;
            public Color32 Primary = new Color32(90, 200, 250, 255);
            public Color32 Secondary = new Color32(40, 120, 200, 255);
            public SkinBlend Blend = SkinBlend.Opaque;
            [Range(0.05f, 1f)] public float Alpha = 1f;
            public SkinShape Shape = SkinShape.Nodes;
            [Tooltip("Stripe period in nodes (0 = none)")] public int Stripe = 3;
            [Tooltip("Used instead of translucency when the translucent budget is exceeded")] public Color32 OpaqueFallback = new Color32(70, 160, 220, 255);

            public static List<SkinEntry> Defaults() => new List<SkinEntry>
            {
                new SkinEntry { Name = "Sky", Primary = new Color32(90, 200, 250, 255), Secondary = new Color32(40, 120, 200, 255) },
                new SkinEntry { Name = "Lime", Primary = new Color32(170, 230, 80, 255), Secondary = new Color32(90, 160, 40, 255), Shape = SkinShape.Strip },
                new SkinEntry { Name = "Candy", Primary = new Color32(250, 120, 170, 255), Secondary = new Color32(255, 230, 240, 255), Stripe = 2 },
                new SkinEntry { Name = "Ghost", Primary = new Color32(200, 220, 255, 255), Secondary = new Color32(150, 170, 255, 255), Blend = SkinBlend.Translucent, Alpha = 0.55f, Shape = SkinShape.Strip, OpaqueFallback = new Color32(120, 130, 170, 255) },
                new SkinEntry { Name = "Amber", Primary = new Color32(255, 190, 60, 255), Secondary = new Color32(200, 110, 30, 255) },
                new SkinEntry { Name = "Jelly", Primary = new Color32(120, 255, 200, 255), Secondary = new Color32(60, 200, 160, 255), Blend = SkinBlend.Translucent, Alpha = 0.6f, OpaqueFallback = new Color32(80, 170, 140, 255) },
                new SkinEntry { Name = "Violet", Primary = new Color32(170, 120, 255, 255), Secondary = new Color32(100, 60, 200, 255), Shape = SkinShape.Strip, Stripe = 4 },
                new SkinEntry { Name = "Ember", Primary = new Color32(255, 110, 70, 255), Secondary = new Color32(255, 200, 80, 255), Blend = SkinBlend.Additive, Alpha = 0.9f },
            };
        }

        static readonly string[] DefaultNames =
        {
            "Noodle", "Zigzag", "Slinky", "Ripple", "Comet", "Pixel", "Mango", "Orbit", "Blitz", "Dune",
            "Echo", "Fizz", "Glint", "Hush", "Ivy", "Jolt", "Kiwi", "Lumen", "Moss", "Nova",
        };

        public SnakeRuntimeConfig Bake() => new SnakeRuntimeConfig(this);
    }

    public enum SkinBlend : byte
    {
        Opaque = 0,
        Translucent = 1,
        Additive = 2,
    }

    public enum SkinShape : byte
    {
        Nodes = 0,
        Strip = 1,
    }
}
