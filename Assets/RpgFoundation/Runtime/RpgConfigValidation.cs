using System;
using Unity.Mathematics;
using static SPF.Runtime.Configuration.RuntimeConfigValidation;

namespace RpgFoundation
{
    public sealed partial class RpgRuntimeConfig
    {
        /// <summary>Cold validation, before any native allocation. Does not normalize authored values.</summary>
        public static void Validate(RpgConfig source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            ValidateValues(Required(source.Dungeon, "Rpg.Dungeon"));
            ValidateValues(Required(source.Hero, "Rpg.Hero"));
            ValidateValues(Required(source.Loot, "Rpg.Loot"));
            ValidateValues(Required(source.Capacity, "Rpg.Capacity"));
            Required(source.Monsters, "Rpg.Monsters");
            for (int i = 0; i < source.Monsters.Count; i++)
                ValidateValues(Required(source.Monsters[i], "Rpg.Monsters entry"));
            Required(source.Weapons, "Rpg.Weapons");
            for (int i = 0; i < source.Weapons.Count; i++)
                ValidateValues(Required(source.Weapons[i], "Rpg.Weapons entry"));
            Required(source.Skills, "Rpg.Skills");
            for (int i = 0; i < source.Skills.Count; i++)
                ValidateValues(Required(source.Skills[i], "Rpg.Skills entry"));
            var c = source.Capacity;
            var d = source.Dungeon;
            var l = source.Loot;
            Require(c.Actors > 0 && c.Actors <= 65536 && c.Projectiles > 0 && c.Items > 0 && c.Props > 0 && c.EventQueue > 0, "RPG table/queue capacities");
            Length((long)c.Actors + c.Projectiles, "RPG destroy queue");
            Length((long)d.Width * d.Height, "RPG map cells");
            Grid(d.Width * d.TileSize, d.Height * d.TileSize, c.GridCellSize, "RPG actor grid");
            Require(d.Width > 0 && d.Height > 0 && d.TileSize > 0, "RPG map dimensions");
            Require(d.RoomsMin > 0 && d.RoomsMax >= d.RoomsMin && d.RoomsMax < int.MaxValue, "RPG room count range");
            Require(d.RoomSizeMin >= 2 && d.RoomSizeMax >= d.RoomSizeMin && d.RoomSizeMax <= Math.Min(d.Width, d.Height) - 4, "RPG room size range");
            Require(d.BarrelsPerRoom >= 0 && d.BarrelsPerRoom < int.MaxValue && d.TrapsPerRoom >= 0 && d.TrapsPerRoom < int.MaxValue, "RPG room prop ranges");
            Require(l.GoldMin <= l.GoldMax && l.GoldMax < int.MaxValue, "RPG gold range");
            Require(source.Monsters.Count <= 255 && source.Skills.Count <= 255, "RPG byte definition identity");
            int lootWeapons = 0;
            int weaponKinds = Enum.GetValues(typeof(WeaponKind)).Length;
            foreach (var w in source.Weapons)
            {
                Require((int)w.Kind < weaponKinds, "RPG weapon kind");
                if (w.Lootable) lootWeapons++;
            }
            Length(1L + ((long)lootWeapons + 1) * l.GearTiers, "RPG gear table");
            Require(l.GearTiers > 0, "RPG gear tiers");
            foreach (var m in source.Monsters)
            {
                Require((int)m.Weapon < weaponKinds, "RPG monster weapon");
                Require(m.Skill >= 0 && m.Skill <= source.Skills.Count, "RPG monster skill reference");
            }
            if (source.Hero.SkillSlots != null)
                foreach (int skill in source.Hero.SkillSlots)
                    Require(skill >= 0 && skill <= source.Skills.Count, "RPG hero skill reference");
        }

        static void ValidateValues(RpgConfig.DungeonSection value)
        {
            Finite("Rpg.DungeonSection", value.TileSize, value.MonsterDensity, value.MonsterGrowthPerFloor, value.ScalingPerFloor, value.EliteChance, value.EliteHealth, value.EliteAttack, value.EliteXp, value.ChestChance, value.TrapDamage, value.SpikeCycle, value.SpikesUp);
        }

        static void ValidateValues(RpgConfig.HeroSection value)
        {
            Finite("Rpg.HeroSection", value.Radius, value.Health, value.HealthPerLevel, value.Attack, value.AttackPerLevel, value.Armour, value.ArmourPerLevel, value.Speed, value.AttackRate, value.Crit, value.CritMultiplier, value.Regen, value.Range, value.ArcCos, value.Mana, value.ManaPerLevel, value.ManaRegen, value.PotionHeal, value.PotionCooldown, value.XpBase, value.XpGrowth, value.PickupRadius);
        }

        static void ValidateValues(RpgConfig.MonsterEntry value)
        {
            Finite("Rpg.MonsterEntry", value.Radius, value.Health, value.Attack, value.Armour, value.Speed, value.AttackRate, value.Range, value.Aggro, value.Xp, value.ProjectileSpeed, value.SpawnWeight, value.StatusPower, value.StatusDuration);
            Finite("RPG monster color", value.Color.r, value.Color.g, value.Color.b);
        }

        static void ValidateValues(RpgConfig.WeaponEntry value)
        {
            Finite("Rpg.WeaponEntry", value.DamageMul, value.AttackRate, value.Range, value.ArcCos, value.Windup, value.Recover, value.Knockback, value.Stagger, value.ProjectileSpeed, value.ProjectileRadius);
        }

        static void ValidateValues(RpgConfig.SkillEntry value)
        {
            Finite("Rpg.SkillEntry", value.ManaCost, value.Cooldown, value.CastTime, value.Power, value.Radius, value.Duration, value.Speed, value.Knockback, value.Slow, value.SlowDuration, value.StatusPower, value.StatusDuration);
        }

        static void ValidateValues(RpgConfig.LootSection value)
        {
            Finite("Rpg.LootSection", value.DropChance, value.GoldWeight, value.PotionWeight, value.GearWeight, value.WeaponAttackPerTier, value.ArmourPerTier, value.HealthPerTier);
        }

        static void ValidateValues(RpgConfig.CapacitySection value)
        {
            Finite("Rpg.CapacitySection", value.GridCellSize);
        }

    }
}
