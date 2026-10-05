using System;
using System.Collections.Generic;
using UnityEngine;

namespace RpgFoundation
{
    /// <summary>Designer-facing configuration of the dungeon RPG. Baked into <see cref="RpgRuntimeConfig"/> at session start.</summary>
    [CreateAssetMenu(menuName = "SPF/RPG/Config", fileName = "RpgConfig")]
    public sealed class RpgConfig : ScriptableObject
    {
        public DungeonSection Dungeon = new DungeonSection();
        public HeroSection Hero = new HeroSection();
        public List<MonsterEntry> Monsters = MonsterEntry.Defaults();
        public List<WeaponEntry> Weapons = WeaponEntry.Defaults();
        public List<SkillEntry> Skills = SkillEntry.Defaults();
        public LootSection Loot = new LootSection();
        public CapacitySection Capacity = new CapacitySection();

        public static RpgConfig CreateDefault()
        {
            var config = CreateInstance<RpgConfig>();
            config.hideFlags = HideFlags.DontSave;
            return config;
        }

        [Serializable]
        public sealed class DungeonSection
        {
            public int Width = 64;
            public int Height = 64;
            public float TileSize = 1f;
            public int RoomsMin = 7;
            public int RoomsMax = 11;
            public int RoomSizeMin = 5;
            public int RoomSizeMax = 11;
            [Tooltip("Monsters per room tile area (floor 1); grows by MonsterGrowthPerFloor")]
            public float MonsterDensity = 0.028f;
            public float MonsterGrowthPerFloor = 0.12f;
            [Tooltip("Monster health / attack multiplier per floor above the first")]
            public float ScalingPerFloor = 0.18f;
            [Tooltip("Every Nth floor the stairs room holds a boss")]
            public int BossEvery = 3;
            [Tooltip("Floor whose stairs end the run (victory)")]
            public int FinalFloor = 9;
            [Tooltip("Chance that a room's pack is led by an elite (from EliteFromFloor on)")]
            public float EliteChance = 0.3f;
            public int EliteFromFloor = 2;
            [Tooltip("Elite health / attack / XP multipliers")]
            public float EliteHealth = 2.5f, EliteAttack = 1.35f, EliteXp = 3f;
        }

        [Serializable]
        public sealed class HeroSection
        {
            public float Radius = 0.4f;
            public float Health = 100f, HealthPerLevel = 15f;
            public float Attack = 12f, AttackPerLevel = 2.5f;
            public float Armour = 5f, ArmourPerLevel = 1f;
            public float Speed = 5f;
            public float AttackRate = 2f;
            public float Crit = 0.1f;
            public float CritMultiplier = 2f;
            public float Regen = 0.6f;
            public float Range = 1.3f;
            [Tooltip("Cosine of the melee arc half angle")]
            public float ArcCos = 0.25f;
            public float Mana = 50f, ManaPerLevel = 6f, ManaRegen = 3f;
            [Tooltip("Skill ids (1-based into Skills) in hero slots 1..4")]
            public int[] SkillSlots = { 1, 2, 3, 4 };
            public float PotionHeal = 0.5f;
            public float PotionCooldown = 1f;
            public int StartPotions = 2;
            public float XpBase = 40f, XpGrowth = 1.35f;
            public int MaxLevel = 30;
            public float PickupRadius = 1.1f;
        }

        [Serializable]
        public sealed class MonsterEntry
        {
            public string Name;
            public Color Color;
            public float Radius, Health, Attack, Armour, Speed, AttackRate, Range, Aggro, Xp;
            public bool Ranged;
            public float ProjectileSpeed;
            [Tooltip("Relative spawn weight in ordinary rooms (0 = only placed explicitly, e.g. bosses)")]
            public float SpawnWeight;
            public bool Boss;
            public WeaponKind Weapon;
            [Tooltip("Skill id (1-based into Skills), 0 = none")]
            public int Skill;
            [Tooltip("Damage over time its hits apply: total = hit damage x StatusPower over StatusDuration")]
            public StatusKind Status;
            public float StatusPower, StatusDuration;

            public static List<MonsterEntry> Defaults() => new List<MonsterEntry>
            {
                new MonsterEntry { Name = "Slime", Color = new Color(0.35f, 0.85f, 0.4f), Radius = 0.42f, Health = 34f, Attack = 7f, Armour = 0f, Speed = 2.4f, AttackRate = 0.9f, Range = 0.6f, Aggro = 7f, Xp = 8f, SpawnWeight = 5f, Weapon = WeaponKind.Claw },
                new MonsterEntry { Name = "Skeleton Archer", Color = new Color(0.9f, 0.88f, 0.78f), Radius = 0.38f, Health = 26f, Attack = 9f, Armour = 2f, Speed = 2.8f, AttackRate = 0.7f, Range = 6f, Aggro = 9f, Xp = 12f, Ranged = true, ProjectileSpeed = 10f, SpawnWeight = 3f, Weapon = WeaponKind.Bow },
                new MonsterEntry { Name = "Brute", Color = new Color(0.85f, 0.3f, 0.25f), Radius = 0.6f, Health = 90f, Attack = 15f, Armour = 8f, Speed = 2f, AttackRate = 0.6f, Range = 0.8f, Aggro = 6f, Xp = 22f, SpawnWeight = 1.5f, Weapon = WeaponKind.Hammer },
                new MonsterEntry { Name = "Warden", Color = new Color(0.65f, 0.35f, 0.95f), Radius = 0.95f, Health = 420f, Attack = 22f, Armour = 15f, Speed = 2.3f, AttackRate = 0.8f, Range = 1.1f, Aggro = 10f, Xp = 140f, SpawnWeight = 0f, Boss = true, Weapon = WeaponKind.Hammer, Skill = 5 },
                new MonsterEntry { Name = "Toxic Slime", Color = new Color(0.65f, 0.9f, 0.2f), Radius = 0.44f, Health = 40f, Attack = 6f, Armour = 1f, Speed = 2.2f, AttackRate = 0.8f, Range = 0.6f, Aggro = 7f, Xp = 14f, SpawnWeight = 2f, Weapon = WeaponKind.Claw, Status = StatusKind.Poison, StatusPower = 0.9f, StatusDuration = 4f },
                new MonsterEntry { Name = "Fire Imp", Color = new Color(0.95f, 0.45f, 0.15f), Radius = 0.34f, Health = 22f, Attack = 7f, Armour = 0f, Speed = 3.2f, AttackRate = 0.6f, Range = 5f, Aggro = 9f, Xp = 15f, Ranged = true, ProjectileSpeed = 8f, SpawnWeight = 1.5f, Weapon = WeaponKind.Staff, Status = StatusKind.Burn, StatusPower = 0.7f, StatusDuration = 3f },
            };
        }

        [Serializable]
        public sealed class WeaponEntry
        {
            public string Name;
            public WeaponKind Kind;
            [Tooltip("Damage = attack x this")] public float DamageMul = 1f;
            public float AttackRate = 1.5f;
            [Tooltip("Reach beyond the wielder's radius (melee) or projectile life x speed (ranged)")] public float Range = 1.2f;
            [Tooltip("Cosine of the half arc (melee): 1 = a line, -1 = all around")] public float ArcCos = 0.25f;
            public float Windup = 0.15f, Recover = 0.2f;
            public float Knockback = 2.5f;
            public float Stagger = 0.15f;
            public bool Ranged;
            public float ProjectileSpeed = 14f, ProjectileRadius = 0.15f;
            public int Pierce;
            public ProjectileVisual Visual;
            [Tooltip("Hero weapons drop as loot; monster weapons (claw, hammer) do not")] public bool Lootable = true;

            public static List<WeaponEntry> Defaults() => new List<WeaponEntry>
            {
                new WeaponEntry { Name = "Sword", Kind = WeaponKind.Sword, DamageMul = 1f, AttackRate = 2f, Range = 1.2f, ArcCos = 0.25f, Windup = 0.12f, Recover = 0.16f, Knockback = 2.5f },
                new WeaponEntry { Name = "Axe", Kind = WeaponKind.Axe, DamageMul = 1.65f, AttackRate = 1.1f, Range = 1.25f, ArcCos = -0.25f, Windup = 0.26f, Recover = 0.28f, Knockback = 5f, Stagger = 0.3f },
                new WeaponEntry { Name = "Spear", Kind = WeaponKind.Spear, DamageMul = 1.2f, AttackRate = 1.6f, Range = 2.2f, ArcCos = 0.85f, Windup = 0.16f, Recover = 0.2f, Knockback = 3.5f, Stagger = 0.2f },
                new WeaponEntry { Name = "Bow", Kind = WeaponKind.Bow, DamageMul = 0.85f, AttackRate = 1.8f, Range = 9f, Windup = 0.2f, Recover = 0.1f, Knockback = 1.5f, Ranged = true, ProjectileSpeed = 17f, ProjectileRadius = 0.14f, Visual = ProjectileVisual.Arrow },
                new WeaponEntry { Name = "Staff", Kind = WeaponKind.Staff, DamageMul = 1.05f, AttackRate = 1.3f, Range = 8f, Windup = 0.22f, Recover = 0.15f, Knockback = 2f, Ranged = true, ProjectileSpeed = 11f, ProjectileRadius = 0.22f, Pierce = 2, Visual = ProjectileVisual.Bolt },
                new WeaponEntry { Name = "Claw", Kind = WeaponKind.Claw, DamageMul = 1f, AttackRate = 1f, Range = 0.6f, ArcCos = 0f, Windup = 0.35f, Recover = 0.35f, Knockback = 1.5f, Lootable = false },
                new WeaponEntry { Name = "Hammer", Kind = WeaponKind.Hammer, DamageMul = 1f, AttackRate = 0.8f, Range = 1f, ArcCos = -0.2f, Windup = 0.45f, Recover = 0.45f, Knockback = 6f, Stagger = 0.3f, Lootable = false },
            };
        }

        [Serializable]
        public sealed class SkillEntry
        {
            public string Name;
            public SkillKind Kind;
            public float ManaCost, Cooldown, CastTime;
            [Tooltip("Damage = attack x skill power x this")] public float Power = 1f;
            public float Radius, Duration, Speed, Knockback;
            [Tooltip("Speed multiplier delta applied to targets (e.g. -0.5 = 50% slower) for SlowDuration")]
            public float Slow, SlowDuration;
            [Tooltip("Damage over time applied to the targets: total = hit damage x StatusPower over StatusDuration")]
            public StatusKind Status;
            public float StatusPower, StatusDuration;
            public int UnlockLevel = 1;

            public static List<SkillEntry> Defaults() => new List<SkillEntry>
            {
                new SkillEntry { Name = "Fireball", Kind = SkillKind.Projectile, ManaCost = 12f, Cooldown = 2.5f, CastTime = 0.25f, Power = 2f, Radius = 1.4f, Speed = 14f, Knockback = 4f, Status = StatusKind.Burn, StatusPower = 0.5f, StatusDuration = 3f, UnlockLevel = 1 },
                new SkillEntry { Name = "Dash", Kind = SkillKind.Dash, ManaCost = 8f, Cooldown = 3f, Duration = 0.22f, Speed = 16f, UnlockLevel = 2 },
                new SkillEntry { Name = "Whirlwind", Kind = SkillKind.Whirlwind, ManaCost = 20f, Cooldown = 6f, Power = 0.7f, Radius = 2f, Duration = 0.9f, Knockback = 3f, UnlockLevel = 4 },
                new SkillEntry { Name = "Frost Nova", Kind = SkillKind.Nova, ManaCost = 25f, Cooldown = 9f, CastTime = 0.3f, Power = 0.8f, Radius = 4f, Knockback = 2f, Slow = -0.5f, SlowDuration = 3f, UnlockLevel = 6 },
                new SkillEntry { Name = "Ground Slam", Kind = SkillKind.Slam, Cooldown = 6f, CastTime = 0.9f, Power = 2.2f, Radius = 3f, Knockback = 8f, UnlockLevel = 1 },
            };
        }

        [Serializable]
        public sealed class LootSection
        {
            [Tooltip("Chance a monster drops anything")] public float DropChance = 0.55f;
            public float GoldWeight = 6f, PotionWeight = 2f, GearWeight = 1.2f;
            public int GoldMin = 3, GoldMax = 12;
            [Tooltip("Bosses always drop gear of the next tier and a potion")] public bool BossGuaranteedGear = true;
            public float WeaponAttackPerTier = 5f;
            public float ArmourPerTier = 4f, HealthPerTier = 12f;
            public int GearTiers = 6;
        }

        [Serializable]
        public sealed class CapacitySection
        {
            public int Actors = 512;
            public int Projectiles = 256;
            public int Items = 512;
            public float GridCellSize = 2f;
            public int EventQueue = 2048;
        }
    }
}
