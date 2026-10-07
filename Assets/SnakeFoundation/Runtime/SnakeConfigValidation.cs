using System;
using Unity.Mathematics;
using static SPF.Runtime.Configuration.RuntimeConfigValidation;

namespace SnakeFoundation
{
    public sealed partial class SnakeRuntimeConfig
    {
        /// <summary>Cold validation, before any native allocation. Does not normalize authored values.</summary>
        public static void Validate(SnakeConfig source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            ValidateValues(Required(source.Movement, "Snake.Movement"));
            ValidateValues(Required(source.Body, "Snake.Body"));
            ValidateValues(Required(source.Food, "Snake.Food"));
            ValidateValues(Required(source.Collision, "Snake.Collision"));
            ValidateValues(Required(source.AI, "Snake.AI"));
            ValidateValues(Required(source.Skill, "Snake.Skill"));
            ValidateValues(Required(source.Capacity, "Snake.Capacity"));
            Required(source.Buffs, "Snake.Buffs");
            for (int i = 0; i < source.Buffs.Count; i++)
                ValidateValues(Required(source.Buffs[i], "Snake.Buffs entry"));
            Required(source.Props, "Snake.Props");
            for (int i = 0; i < source.Props.Count; i++)
                ValidateValues(Required(source.Props[i], "Snake.Props entry"));
            Required(source.Regions, "Snake.Regions");
            for (int i = 0; i < source.Regions.Count; i++)
                ValidateValues(Required(source.Regions[i], "Snake.Regions entry"));
            Required(source.Portals, "Snake.Portals");
            for (int i = 0; i < source.Portals.Count; i++)
                ValidateValues(Required(source.Portals[i], "Snake.Portals entry"));
            Required(source.Skins, "Snake.Skins");
            for (int i = 0; i < source.Skins.Count; i++)
                ValidateValues(Required(source.Skins[i], "Snake.Skins entry"));
            Required(source.AINames, "Snake.AINames");
            var c = source.Capacity;
            Require(c.Snakes > 0 && c.Snakes <= 65536 && c.Food > 0 && c.Props > 0 && c.Projectiles > 0, "Snake table capacities");
            Require(c.BodyGridEntries > 0 && c.BodyGridEntries <= 65536, "Snake body-grid capacity");
            Require((long)c.Food + c.Props <= 65536, "Snake item-grid capacity");
            Require(c.AverageTrailPoints > 0 && c.MaxTrailPoints >= 16 && math.ispow2(c.MaxTrailPoints), "Snake trail capacity");
            int points = Length((long)c.Snakes * c.AverageTrailPoints, "Snake trail points");
            Length((long)points + c.Snakes, "Snake body-grid keys");
            Length((long)c.EventQueue * 4, "Snake eat queue");
            Require(c.GridCells > 0 && c.GridCellSize > 0 && c.ItemGridCellSize > 0 && c.ChunkSize > 0, "Snake grid sizes");
            float window = c.GridCells * c.GridCellSize;
            Grid(window, window, c.BodyGridCellSize > 0 ? c.BodyGridCellSize : c.GridCellSize, "Snake body grid");
            Grid(window, window, c.ItemGridCellSize, "Snake item grid");
            Require(source.Regions.Count > 0 && source.Regions.Count <= 256, "Snake region count (byte identity)");
            Require(source.Buffs.Count <= 255 && source.Props.Count <= 255, "Snake byte definition identity");
            Require(source.Skill.HitBuffKind >= 0 && source.Skill.HitBuffKind <= source.Buffs.Count, "Snake skill buff reference");
            foreach (var p in source.Props)
                Require(p.BuffKind >= 0 && p.BuffKind <= source.Buffs.Count, "Snake prop buff reference");
            float2 largest = 0f;
            foreach (var r in source.Regions)
            {
                float2 size = (float2)r.Max - (float2)r.Min;
                Grid(size.x, size.y, c.ChunkSize, "Snake region chunks");
                largest = math.max(largest, size);
            }
            Grid(largest.x, largest.y, c.ChunkSize, "Snake head grid");
            foreach (var p in source.Portals)
                Require(p.FromRegion >= 0 && p.FromRegion < source.Regions.Count && p.ToRegion >= 0 && p.ToRegion < source.Regions.Count, "Snake portal region reference");
        }

        static void ValidateValues(SnakeConfig.MovementSection value)
        {
            Finite("Snake.MovementSection", value.BaseSpeed, value.BoostSpeed, value.TurnRate, value.TurnReferenceRadius, value.TurnFalloff, value.MinBoostMass, value.BoostDrainPerSecond, value.BoostDropMass);
        }

        static void ValidateValues(SnakeConfig.BodySection value)
        {
            Finite("Snake.BodySection", value.TrailSpacing, value.TrailSpacingPerRadius, value.NodeSpacingFactor, value.StartMass, value.MinMass, value.BaseLength, value.LengthPerMass, value.MaxLength, value.BaseRadius, value.RadiusPerSqrtMass, value.MaxRadius);
        }

        static void ValidateValues(SnakeConfig.FoodSection value)
        {
            Finite("Snake.FoodSection", value.MassPerFoodValue, value.ValueMin, value.ValueMax, value.BaseRadius, value.RadiusPerSqrtValue, value.EatRange, value.DeathDropRatio);
        }

        static void ValidateValues(SnakeConfig.CollisionSection value)
        {
            Finite("Snake.CollisionSection", value.HeadOnTolerance, value.SpawnProtection, value.PortalProtection, value.HitRadiusScale);
        }

        static void ValidateValues(SnakeConfig.AISection value)
        {
            Finite("Snake.AISection", value.ThreatRadius, value.HuntRadius, value.FoodSearchRadius, value.WanderRadius, value.FarGrowthPerSecond, value.StartMassMin, value.StartMassMax, value.MaxMass, value.MinSpawnDistanceFromPlayer, value.SpawnNearFocusRatio, value.SpawnRingMin, value.SpawnRingMax, value.WanderTowardFocusChance, value.FocusWanderRadius);
        }

        static void ValidateValues(SnakeConfig.SkillSection value)
        {
            Finite("Snake.SkillSection", value.Cooldown, value.MassCost, value.MinMass, value.ProjectileSpeed, value.ProjectileLife, value.ProjectileRadius, value.HitMassLoss);
        }

        static void ValidateValues(SnakeConfig.CapacitySection value)
        {
            Finite("Snake.CapacitySection", value.GridCellSize, value.BodyGridCellSize, value.ItemGridCellSize, value.ChunkSize);
        }

        static void ValidateValues(SnakeConfig.BuffEntry value)
        {
            Finite("Snake.BuffEntry", value.Value, value.Duration);
        }

        static void ValidateValues(SnakeConfig.PropEntry value)
        {
            Finite("Snake.PropEntry", value.Radius, value.SpawnWeight);
        }

        static void ValidateValues(SnakeConfig.RegionEntry value)
        {
            Finite("Snake.RegionEntry", value.FoodDensity);
            Finite("Snake region bounds", value.Min.x, value.Min.y, value.Max.x, value.Max.y);
        }

        static void ValidateValues(SnakeConfig.PortalEntry value)
        {
            Finite("Snake.PortalEntry", value.Radius);
            Finite("Snake portal position", value.Position.x, value.Position.y, value.Arrival.x, value.Arrival.y);
        }

        static void ValidateValues(SnakeConfig.SkinEntry value)
        {
            Finite("Snake.SkinEntry", value.Alpha);
        }

    }
}
