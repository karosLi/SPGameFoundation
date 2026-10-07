using System;
using System.IO;

namespace RpgFoundation
{
    public sealed partial class RpgRuntimeConfig
    {
        /// <summary>Explicit, versioned cold content input for SaveCompatibilityDescriptor.Encode.
        /// Includes presentation values; does not alter raw saves or install a save adapter.
        /// Call only while this session-owned runtime configuration is alive and quiescent.</summary>
        public void WriteContent(BinaryWriter writer)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            if (!Monsters.IsCreated) throw new ObjectDisposedException(nameof(RpgRuntimeConfig));
            writer.Write("rpg.source-snapshot.v1");
            writer.Write(Settings.HeroRadius);
            writer.Write(Settings.HeroSpeed);
            writer.Write(Settings.HeroCrit);
            writer.Write(Settings.CritMultiplier);
            writer.Write(Settings.HeroRegen);
            writer.Write(Settings.ArcCos);
            writer.Write(Settings.HeroHealth);
            writer.Write(Settings.HeroHealthPerLevel);
            writer.Write(Settings.HeroAttack);
            writer.Write(Settings.HeroAttackPerLevel);
            writer.Write(Settings.HeroArmour);
            writer.Write(Settings.HeroArmourPerLevel);
            writer.Write(Settings.HeroMana);
            writer.Write(Settings.HeroManaPerLevel);
            writer.Write(Settings.HeroManaRegen);
            writer.Write(Settings.PotionHeal);
            writer.Write(Settings.PotionCooldown);
            writer.Write(Settings.PickupRadius);
            writer.Write(Settings.ScalingPerFloor);
            writer.Write(Settings.Levels.Base);
            writer.Write(Settings.Levels.Growth);
            writer.Write(Settings.Levels.MaxLevel);
            writer.Write(Dungeon.Width);
            writer.Write(Dungeon.Height);
            writer.Write(Dungeon.TileSize);
            writer.Write(Dungeon.RoomsMin);
            writer.Write(Dungeon.RoomsMax);
            writer.Write(Dungeon.RoomSizeMin);
            writer.Write(Dungeon.RoomSizeMax);
            writer.Write(Dungeon.MonsterDensity);
            writer.Write(Dungeon.MonsterGrowthPerFloor);
            writer.Write(Dungeon.ScalingPerFloor);
            writer.Write(Dungeon.BossEvery);
            writer.Write(Dungeon.FinalFloor);
            writer.Write(Dungeon.EliteChance);
            writer.Write(Dungeon.EliteFromFloor);
            writer.Write(Dungeon.EliteHealth);
            writer.Write(Dungeon.EliteAttack);
            writer.Write(Dungeon.EliteXp);
            writer.Write(Dungeon.ChestChance);
            writer.Write(Dungeon.BarrelsPerRoom);
            writer.Write(Dungeon.TrapsFromFloor);
            writer.Write(Dungeon.TrapsPerRoom);
            writer.Write(Dungeon.TrapDamage);
            writer.Write(Dungeon.SpikeCycle);
            writer.Write(Dungeon.SpikesUp);
            writer.Write(Loot.DropChance);
            writer.Write(Loot.GoldWeight);
            writer.Write(Loot.PotionWeight);
            writer.Write(Loot.GearWeight);
            writer.Write(Loot.GoldMin);
            writer.Write(Loot.GoldMax);
            writer.Write(Loot.BossGuaranteedGear);
            writer.Write(Loot.WeaponAttackPerTier);
            writer.Write(Loot.ArmourPerTier);
            writer.Write(Loot.HealthPerTier);
            writer.Write(Loot.GearTiers);
            writer.Write(Capacity.Actors);
            writer.Write(Capacity.Projectiles);
            writer.Write(Capacity.Items);
            writer.Write(Capacity.Props);
            writer.Write(Capacity.GridCellSize);
            writer.Write(Capacity.EventQueue);
            writer.Write(UseDecisionTree);
            writer.Write(StartPotions);
            writer.Write(Monsters.Length);
            for (int i = 0; i < Monsters.Length; i++)
            {
                var value = Monsters[i];
                writer.Write(value.Color.x);
                writer.Write(value.Color.y);
                writer.Write(value.Color.z);
                writer.Write(value.Color.w);
                writer.Write(value.Radius);
                writer.Write(value.Health);
                writer.Write(value.Attack);
                writer.Write(value.Armour);
                writer.Write(value.Speed);
                writer.Write(value.AttackRate);
                writer.Write(value.Range);
                writer.Write(value.Aggro);
                writer.Write(value.Xp);
                writer.Write(value.ProjectileSpeed);
                writer.Write(value.SpawnWeight);
                writer.Write(value.Ranged);
                writer.Write(value.Boss);
                writer.Write((int)value.Weapon);
                writer.Write(value.Skill);
                writer.Write((int)value.Status);
                writer.Write(value.StatusPower);
                writer.Write(value.StatusDuration);
            }
            writer.Write(Weapons.Length);
            for (int i = 0; i < Weapons.Length; i++)
            {
                var value = Weapons[i];
                writer.Write((int)value.Kind);
                writer.Write(value.DamageMul);
                writer.Write(value.AttackRate);
                writer.Write(value.Range);
                writer.Write(value.ArcCos);
                writer.Write(value.Windup);
                writer.Write(value.Recover);
                writer.Write(value.Knockback);
                writer.Write(value.Stagger);
                writer.Write(value.Ranged);
                writer.Write(value.ProjectileSpeed);
                writer.Write(value.ProjectileRadius);
                writer.Write(value.Pierce);
                writer.Write((int)value.Visual);
            }
            writer.Write(Skills.Length);
            for (int i = 0; i < Skills.Length; i++)
            {
                var value = Skills[i];
                writer.Write((int)value.Kind);
                writer.Write(value.ManaCost);
                writer.Write(value.Cooldown);
                writer.Write(value.CastTime);
                writer.Write(value.Power);
                writer.Write(value.Radius);
                writer.Write(value.Duration);
                writer.Write(value.Speed);
                writer.Write(value.Knockback);
                writer.Write(value.Slow);
                writer.Write(value.SlowDuration);
                writer.Write((int)value.Status);
                writer.Write(value.StatusPower);
                writer.Write(value.StatusDuration);
                writer.Write(value.UnlockLevel);
            }
            writer.Write(Gear.Length);
            for (int i = 0; i < Gear.Length; i++)
            {
                var value = Gear[i];
                writer.Write((int)value.Slot);
                writer.Write(value.Tier);
                writer.Write((int)value.Weapon);
                writer.Write(value.Attack);
                writer.Write(value.Armour);
                writer.Write(value.Health);
            }
            writer.Write(CombatDecisionProgram.Length);
            for (int i = 0; i < CombatDecisionProgram.Length; i++)
            {
                var value = CombatDecisionProgram[i];
                writer.Write((int)value.Kind);
                writer.Write(value.Required);
                writer.Write(value.Forbidden);
                writer.Write(value.Pass);
                writer.Write(value.Fail);
                writer.Write(value.Action);
            }
            writer.Write(HeroSkillSlots.Length);
            for (int i = 0; i < HeroSkillSlots.Length; i++)
            {
                var value = HeroSkillSlots[i];
                writer.Write(value);
            }
            writer.Write(LootWeapons.Length);
            for (int i = 0; i < LootWeapons.Length; i++)
            {
                var value = LootWeapons[i];
                writer.Write((int)value);
            }
            writer.Write(MonsterNames.Length);
            foreach (var value in MonsterNames)
            {
                writer.Write(value != null);
                if (value != null) writer.Write(value);
            }
            writer.Write(WeaponNames.Length);
            foreach (var value in WeaponNames)
            {
                writer.Write(value != null);
                if (value != null) writer.Write(value);
            }
            writer.Write(SkillNames.Length);
            foreach (var value in SkillNames)
            {
                writer.Write(value != null);
                if (value != null) writer.Write(value);
            }
        }
    }
}
