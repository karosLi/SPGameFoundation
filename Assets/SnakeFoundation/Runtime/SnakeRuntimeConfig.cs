using System;
using SPF.Contracts;
using SPF.L2.Buffs;
using SPF.L2.Growth;
using SPF.L2.Props;
using SPF.L2.Skills;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace SnakeFoundation
{
    /// <summary>Blittable settings passed by value into jobs.</summary>
    public struct SnakeSettings
    {
        // Movement
        public float BaseSpeed, BoostSpeed, TurnRate, TurnReferenceRadius, TurnFalloff;
        public float MinBoostMass, BoostDrainPerSecond, BoostDropMass;
        // Body
        public float TrailSpacing, NodeSpacingFactor, StartMass, MinMass;
        public GrowthCurve Growth;
        public int MaxTrailPoints;
        // Food
        public float MassPerFoodValue, FoodValueMin, FoodValueMax, FoodBaseRadius, FoodRadiusPerSqrtValue, EatRange, DeathDropRatio;
        public int MaxDropsPerDeath, ReplenishChunksPerTick, ReplenishPerChunkPerTick, PropsPerChunk, FoodPerChunk;
        // Collision
        public float HeadOnTolerance, SpawnProtection, PortalProtection, HitRadiusScale;
        // AI
        public int AIPerRegion, AISpawnsPerTick, AIDecisionIntervalTicks;
        public float ThreatRadius, HuntRadius, FoodSearchRadius, WanderRadius, FarGrowthPerSecond;
        public float AIStartMassMin, AIStartMassMax, AIMaxMass, MinSpawnDistanceFromPlayer;
        public float SpawnNearFocusRatio, SpawnRingMin, SpawnRingMax, WanderTowardFocusChance, FocusWanderRadius;
        // Skill
        public SkillDefinition Skill;

        public float FoodRadius(float value) => FoodBaseRadius + FoodRadiusPerSqrtValue * math.sqrt(math.max(value, 0f));
        public float NodeSpacing(float radius) => math.max(radius * NodeSpacingFactor, TrailSpacing);
    }

    public struct RegionDef
    {
        public float2 Min;
        public float2 Max;
        public float FoodDensity;

        public float2 Center => (Min + Max) * 0.5f;
        public float2 Size => Max - Min;
    }

    public struct PortalDef
    {
        public int FromRegion;
        public float2 Position;
        public int ToRegion;
        public float2 Arrival;
        public float Radius;
    }

    /// <summary>Buff lookup for jobs (kind is 1-based; kind 0 is "empty").</summary>
    public struct BuffTable : IBuffDefinitions
    {
        [ReadOnly] public NativeArray<BuffDefinition> Definitions;

        public BuffDefinition Get(byte kind) =>
            kind > 0 && kind <= Definitions.Length ? Definitions[kind - 1] : default;
    }

    public struct Skin
    {
        public Color32 Primary;
        public Color32 Secondary;
        public Color32 OpaqueFallback;
        public SkinBlend Blend;
        public SkinShape Shape;
        public float Alpha;
        public int Stripe;
    }

    /// <summary>Baked, read-only configuration shared by all snake systems (world resource).</summary>
    public sealed class SnakeRuntimeConfig : IDisposable
    {
        public SnakeRuntimeConfig(SnakeConfig source)
        {
            var m = source.Movement;
            var b = source.Body;
            var f = source.Food;
            var c = source.Collision;
            var a = source.AI;
            var s = source.Skill;
            Settings = new SnakeSettings
            {
                BaseSpeed = m.BaseSpeed, BoostSpeed = m.BoostSpeed, TurnRate = m.TurnRate,
                TurnReferenceRadius = m.TurnReferenceRadius, TurnFalloff = m.TurnFalloff,
                MinBoostMass = m.MinBoostMass, BoostDrainPerSecond = m.BoostDrainPerSecond, BoostDropMass = math.max(m.BoostDropMass, 0.1f),
                TrailSpacing = math.max(b.TrailSpacing, 0.05f), NodeSpacingFactor = b.NodeSpacingFactor,
                StartMass = b.StartMass, MinMass = b.MinMass,
                Growth = new GrowthCurve
                {
                    BaseLength = b.BaseLength, LengthPerMass = b.LengthPerMass, MaxLength = b.MaxLength,
                    BaseRadius = b.BaseRadius, RadiusPerSqrtMass = b.RadiusPerSqrtMass, MaxRadius = b.MaxRadius,
                },
                MaxTrailPoints = source.Capacity.MaxTrailPoints,
                MassPerFoodValue = f.MassPerFoodValue, FoodValueMin = f.ValueMin, FoodValueMax = f.ValueMax,
                FoodBaseRadius = f.BaseRadius, FoodRadiusPerSqrtValue = f.RadiusPerSqrtValue, EatRange = f.EatRange,
                DeathDropRatio = f.DeathDropRatio, MaxDropsPerDeath = f.MaxDropsPerDeath,
                ReplenishChunksPerTick = f.ReplenishChunksPerTick, ReplenishPerChunkPerTick = f.ReplenishPerChunkPerTick,
                PropsPerChunk = f.PropsPerChunk, FoodPerChunk = f.FoodPerChunk,
                HeadOnTolerance = c.HeadOnTolerance, SpawnProtection = c.SpawnProtection, PortalProtection = c.PortalProtection,
                HitRadiusScale = c.HitRadiusScale,
                AIPerRegion = a.SnakesPerRegion, AISpawnsPerTick = a.SpawnsPerTick, AIDecisionIntervalTicks = math.max(1, a.DecisionIntervalTicks),
                ThreatRadius = a.ThreatRadius, HuntRadius = a.HuntRadius, FoodSearchRadius = a.FoodSearchRadius,
                WanderRadius = a.WanderRadius, FarGrowthPerSecond = a.FarGrowthPerSecond,
                AIStartMassMin = a.StartMassMin, AIStartMassMax = a.StartMassMax, AIMaxMass = a.MaxMass,
                MinSpawnDistanceFromPlayer = a.MinSpawnDistanceFromPlayer,
                SpawnNearFocusRatio = a.SpawnNearFocusRatio, SpawnRingMin = a.SpawnRingMin, SpawnRingMax = math.max(a.SpawnRingMax, a.SpawnRingMin),
                WanderTowardFocusChance = a.WanderTowardFocusChance, FocusWanderRadius = a.FocusWanderRadius,
                Skill = new SkillDefinition
                {
                    Cooldown = s.Cooldown, MassCost = s.MassCost, MinMass = s.MinMass, ProjectileSpeed = s.ProjectileSpeed,
                    ProjectileLife = s.ProjectileLife, ProjectileRadius = s.ProjectileRadius,
                    HitBuffKind = (byte)math.clamp(s.HitBuffKind, 0, 255), HitMassLoss = s.HitMassLoss,
                },
            };

            Buffs = new NativeArray<BuffDefinition>(math.max(source.Buffs.Count, 1), Allocator.Persistent);
            for (int i = 0; i < source.Buffs.Count; i++)
                Buffs.Set(i, new BuffDefinition { Stat = source.Buffs[i].Stat, Value = source.Buffs[i].Value, Duration = source.Buffs[i].Duration });

            Props = new NativeArray<PropDefinition>(math.max(source.Props.Count, 1), Allocator.Persistent);
            PropCount = source.Props.Count;
            float totalWeight = 0f;
            for (int i = 0; i < source.Props.Count; i++)
            {
                var p = source.Props[i];
                Props.Set(i, new PropDefinition { BuffKind = (byte)p.BuffKind, Radius = p.Radius, SpawnWeight = p.SpawnWeight });
                totalWeight += math.max(p.SpawnWeight, 0f);
            }
            PropWeightTotal = totalWeight;

            if (source.Regions.Count == 0)
                throw new ArgumentException("SnakeConfig needs at least one region.");
            Regions = new NativeArray<RegionDef>(source.Regions.Count, Allocator.Persistent);
            RegionNames = new string[source.Regions.Count];
            for (int i = 0; i < source.Regions.Count; i++)
            {
                var r = source.Regions[i];
                Regions.Set(i, new RegionDef { Min = r.Min, Max = r.Max, FoodDensity = r.FoodDensity });
                RegionNames[i] = r.Name;
            }

            Portals = new NativeArray<PortalDef>(math.max(source.Portals.Count, 1), Allocator.Persistent);
            PortalCount = source.Portals.Count;
            for (int i = 0; i < source.Portals.Count; i++)
            {
                var p = source.Portals[i];
                Portals.Set(i, new PortalDef { FromRegion = p.FromRegion, Position = p.Position, ToRegion = p.ToRegion, Arrival = p.Arrival, Radius = p.Radius });
            }

            Skins = new Skin[math.max(source.Skins.Count, 1)];
            for (int i = 0; i < source.Skins.Count; i++)
            {
                var k = source.Skins[i];
                Skins[i] = new Skin
                {
                    Primary = k.Primary, Secondary = k.Secondary, OpaqueFallback = k.OpaqueFallback,
                    Blend = k.Blend, Shape = k.Shape, Alpha = k.Alpha, Stripe = k.Stripe,
                };
            }
            if (source.Skins.Count == 0)
                Skins[0] = new Skin { Primary = new Color32(255, 255, 255, 255), Secondary = new Color32(200, 200, 200, 255), Alpha = 1f };

            Names = source.AINames.Count > 0 ? source.AINames.ToArray() : new[] { "Snake" };
            Capacity = source.Capacity;
        }

        public SnakeSettings Settings;
        public NativeArray<BuffDefinition> Buffs { get; }
        public NativeArray<PropDefinition> Props { get; }
        public int PropCount { get; }
        public float PropWeightTotal { get; }
        public NativeArray<RegionDef> Regions { get; }
        public string[] RegionNames { get; }
        public NativeArray<PortalDef> Portals { get; }
        public int PortalCount { get; }
        public Skin[] Skins { get; }
        public string[] Names { get; }
        public SnakeConfig.CapacitySection Capacity { get; }

        public BuffTable BuffTable => new BuffTable { Definitions = Buffs };

        public void Dispose()
        {
            if (Buffs.IsCreated) Buffs.Dispose();
            if (Props.IsCreated) Props.Dispose();
            if (Regions.IsCreated) Regions.Dispose();
            if (Portals.IsCreated) Portals.Dispose();
        }
    }
}
