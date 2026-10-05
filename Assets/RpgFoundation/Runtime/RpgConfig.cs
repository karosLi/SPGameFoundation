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
            public float FireballSpeed = 14f;
            public float FireballCooldown = 3f;
            public float FireballDamage = 2.2f;
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

            public static List<MonsterEntry> Defaults() => new List<MonsterEntry>
            {
                new MonsterEntry { Name = "Slime", Color = new Color(0.35f, 0.85f, 0.4f), Radius = 0.42f, Health = 34f, Attack = 7f, Armour = 0f, Speed = 2.4f, AttackRate = 0.9f, Range = 0.6f, Aggro = 7f, Xp = 8f, SpawnWeight = 5f },
                new MonsterEntry { Name = "Skeleton Archer", Color = new Color(0.9f, 0.88f, 0.78f), Radius = 0.38f, Health = 26f, Attack = 9f, Armour = 2f, Speed = 2.8f, AttackRate = 0.7f, Range = 6f, Aggro = 9f, Xp = 12f, Ranged = true, ProjectileSpeed = 10f, SpawnWeight = 3f },
                new MonsterEntry { Name = "Brute", Color = new Color(0.85f, 0.3f, 0.25f), Radius = 0.6f, Health = 90f, Attack = 15f, Armour = 8f, Speed = 2f, AttackRate = 0.6f, Range = 0.8f, Aggro = 6f, Xp = 22f, SpawnWeight = 1.5f },
                new MonsterEntry { Name = "Warden", Color = new Color(0.65f, 0.35f, 0.95f), Radius = 0.95f, Health = 420f, Attack = 22f, Armour = 15f, Speed = 2.3f, AttackRate = 0.8f, Range = 1.1f, Aggro = 10f, Xp = 140f, SpawnWeight = 0f, Boss = true },
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
