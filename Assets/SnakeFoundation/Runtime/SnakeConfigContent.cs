using System;
using System.IO;

namespace SnakeFoundation
{
    public sealed partial class SnakeRuntimeConfig
    {
        /// <summary>Explicit, versioned cold content input for SaveCompatibilityDescriptor.Encode.
        /// Includes presentation values; does not alter raw saves or install a save adapter.
        /// Call only while this session-owned runtime configuration is alive and quiescent.</summary>
        public void WriteContent(BinaryWriter writer)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            if (!Buffs.IsCreated) throw new ObjectDisposedException(nameof(SnakeRuntimeConfig));
            writer.Write("snake.source-snapshot.v1");
            writer.Write(Settings.BaseSpeed);
            writer.Write(Settings.BoostSpeed);
            writer.Write(Settings.TurnRate);
            writer.Write(Settings.TurnReferenceRadius);
            writer.Write(Settings.TurnFalloff);
            writer.Write(Settings.MinBoostMass);
            writer.Write(Settings.BoostDrainPerSecond);
            writer.Write(Settings.BoostDropMass);
            writer.Write(Settings.TrailSpacing);
            writer.Write(Settings.TrailSpacingPerRadius);
            writer.Write(Settings.NodeSpacingFactor);
            writer.Write(Settings.StartMass);
            writer.Write(Settings.MinMass);
            writer.Write(Settings.Growth.BaseLength);
            writer.Write(Settings.Growth.LengthPerMass);
            writer.Write(Settings.Growth.MaxLength);
            writer.Write(Settings.Growth.BaseRadius);
            writer.Write(Settings.Growth.RadiusPerSqrtMass);
            writer.Write(Settings.Growth.MaxRadius);
            writer.Write(Settings.MaxTrailPoints);
            writer.Write(Settings.MassPerFoodValue);
            writer.Write(Settings.FoodValueMin);
            writer.Write(Settings.FoodValueMax);
            writer.Write(Settings.FoodBaseRadius);
            writer.Write(Settings.FoodRadiusPerSqrtValue);
            writer.Write(Settings.EatRange);
            writer.Write(Settings.DeathDropRatio);
            writer.Write(Settings.MaxDropsPerDeath);
            writer.Write(Settings.ReplenishChunksPerTick);
            writer.Write(Settings.ReplenishPerChunkPerTick);
            writer.Write(Settings.PropsPerChunk);
            writer.Write(Settings.FoodPerChunk);
            writer.Write(Settings.HeadOnTolerance);
            writer.Write(Settings.SpawnProtection);
            writer.Write(Settings.PortalProtection);
            writer.Write(Settings.HitRadiusScale);
            writer.Write(Settings.AIPerRegion);
            writer.Write(Settings.AISpawnsPerTick);
            writer.Write(Settings.AIDecisionIntervalTicks);
            writer.Write(Settings.ThreatRadius);
            writer.Write(Settings.HuntRadius);
            writer.Write(Settings.FoodSearchRadius);
            writer.Write(Settings.WanderRadius);
            writer.Write(Settings.FarGrowthPerSecond);
            writer.Write(Settings.AIStartMassMin);
            writer.Write(Settings.AIStartMassMax);
            writer.Write(Settings.AIMaxMass);
            writer.Write(Settings.MinSpawnDistanceFromPlayer);
            writer.Write(Settings.SpawnNearFocusRatio);
            writer.Write(Settings.SpawnRingMin);
            writer.Write(Settings.SpawnRingMax);
            writer.Write(Settings.WanderTowardFocusChance);
            writer.Write(Settings.FocusWanderRadius);
            writer.Write(Settings.Skill.Cooldown);
            writer.Write(Settings.Skill.MassCost);
            writer.Write(Settings.Skill.MinMass);
            writer.Write(Settings.Skill.ProjectileSpeed);
            writer.Write(Settings.Skill.ProjectileLife);
            writer.Write(Settings.Skill.ProjectileRadius);
            writer.Write(Settings.Skill.HitBuffKind);
            writer.Write(Settings.Skill.HitMassLoss);
            writer.Write(Capacity.Snakes);
            writer.Write(Capacity.Food);
            writer.Write(Capacity.Props);
            writer.Write(Capacity.Projectiles);
            writer.Write(Capacity.AverageTrailPoints);
            writer.Write(Capacity.MaxTrailPoints);
            writer.Write(Capacity.BodyGridEntries);
            writer.Write(Capacity.GridCells);
            writer.Write(Capacity.GridCellSize);
            writer.Write(Capacity.BodyGridCellSize);
            writer.Write(Capacity.ItemGridCellSize);
            writer.Write(Capacity.ChunkSize);
            writer.Write(Capacity.EventQueue);
            writer.Write(PropCount);
            writer.Write(PropWeightTotal);
            writer.Write(PortalCount);
            writer.Write(Buffs.Length);
            for (int i = 0; i < Buffs.Length; i++)
            {
                var value = Buffs[i];
                writer.Write((int)value.Stat);
                writer.Write(value.Value);
                writer.Write(value.Duration);
            }
            writer.Write(Props.Length);
            for (int i = 0; i < Props.Length; i++)
            {
                var value = Props[i];
                writer.Write(value.BuffKind);
                writer.Write(value.Radius);
                writer.Write(value.SpawnWeight);
            }
            writer.Write(Regions.Length);
            for (int i = 0; i < Regions.Length; i++)
            {
                var value = Regions[i];
                writer.Write(value.Min.x);
                writer.Write(value.Min.y);
                writer.Write(value.Max.x);
                writer.Write(value.Max.y);
                writer.Write(value.FoodDensity);
                writer.Write(value.Size.x);
                writer.Write(value.Size.y);
            }
            writer.Write(Portals.Length);
            for (int i = 0; i < Portals.Length; i++)
            {
                var value = Portals[i];
                writer.Write(value.FromRegion);
                writer.Write(value.Position.x);
                writer.Write(value.Position.y);
                writer.Write(value.ToRegion);
                writer.Write(value.Arrival.x);
                writer.Write(value.Arrival.y);
                writer.Write(value.Radius);
            }
            writer.Write(Skins.Length);
            for (int i = 0; i < Skins.Length; i++)
            {
                var value = Skins[i];
                writer.Write(value.Primary.r);
                writer.Write(value.Primary.g);
                writer.Write(value.Primary.b);
                writer.Write(value.Primary.a);
                writer.Write(value.Secondary.r);
                writer.Write(value.Secondary.g);
                writer.Write(value.Secondary.b);
                writer.Write(value.Secondary.a);
                writer.Write(value.OpaqueFallback.r);
                writer.Write(value.OpaqueFallback.g);
                writer.Write(value.OpaqueFallback.b);
                writer.Write(value.OpaqueFallback.a);
                writer.Write((int)value.Blend);
                writer.Write((int)value.Shape);
                writer.Write(value.Alpha);
                writer.Write(value.Stripe);
            }
            writer.Write(RegionNames.Length);
            foreach (var value in RegionNames)
            {
                writer.Write(value != null);
                if (value != null) writer.Write(value);
            }
            writer.Write(Names.Length);
            foreach (var value in Names)
            {
                writer.Write(value != null);
                if (value != null) writer.Write(value);
            }
        }
    }
}
