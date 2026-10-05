using System;
using SPF.Contracts;
using SPF.L2.Progression;
using Unity.Collections;
using Unity.Mathematics;

namespace RpgFoundation
{
    /// <summary>Blittable per-monster definition (jobs read it from a NativeArray).</summary>
    public struct MonsterDef
    {
        public float4 Color;
        public float Radius, Health, Attack, Armour, Speed, AttackRate, Range, Aggro, Xp;
        public float ProjectileSpeed;
        public float SpawnWeight;
        public bool Ranged, Boss;
    }

    public struct GearDef
    {
        public GearSlot Slot;
        public int Tier;
        public float Attack, Armour, Health;
    }

    /// <summary>Blittable settings used by jobs.</summary>
    public struct RpgSettings
    {
        public float HeroRadius, HeroSpeed, HeroAttackRate, HeroCrit, CritMultiplier, HeroRegen, HeroRange, ArcCos;
        public float HeroHealth, HeroHealthPerLevel, HeroAttack, HeroAttackPerLevel, HeroArmour, HeroArmourPerLevel;
        public float FireballSpeed, FireballCooldown, FireballDamage, PotionHeal, PotionCooldown, PickupRadius;
        public float ScalingPerFloor;
        public LevelCurve Levels;
    }

    /// <summary>Config baked for the session: settings, monster / gear tables, names.</summary>
    public sealed class RpgRuntimeConfig : IDisposable
    {
        public RpgSettings Settings;
        public NativeArray<MonsterDef> Monsters;
        public GearDef[] Gear;
        public string[] MonsterNames;
        public RpgConfig.DungeonSection Dungeon;
        public RpgConfig.LootSection Loot;
        public RpgConfig.CapacitySection Capacity;
        public int StartPotions;

        public static RpgRuntimeConfig Bake(RpgConfig source)
        {
            var h = source.Hero;
            var config = new RpgRuntimeConfig
            {
                Settings = new RpgSettings
                {
                    HeroRadius = h.Radius, HeroSpeed = h.Speed, HeroAttackRate = h.AttackRate, HeroCrit = h.Crit,
                    CritMultiplier = h.CritMultiplier, HeroRegen = h.Regen, HeroRange = h.Range, ArcCos = h.ArcCos,
                    HeroHealth = h.Health, HeroHealthPerLevel = h.HealthPerLevel, HeroAttack = h.Attack, HeroAttackPerLevel = h.AttackPerLevel,
                    HeroArmour = h.Armour, HeroArmourPerLevel = h.ArmourPerLevel,
                    FireballSpeed = h.FireballSpeed, FireballCooldown = h.FireballCooldown, FireballDamage = h.FireballDamage,
                    PotionHeal = h.PotionHeal, PotionCooldown = h.PotionCooldown, PickupRadius = h.PickupRadius,
                    ScalingPerFloor = source.Dungeon.ScalingPerFloor,
                    Levels = new LevelCurve { Base = h.XpBase, Growth = h.XpGrowth, MaxLevel = h.MaxLevel },
                },
                Dungeon = source.Dungeon,
                Loot = source.Loot,
                Capacity = source.Capacity,
                StartPotions = h.StartPotions,
            };
            int n = source.Monsters.Count;
            config.Monsters = new NativeArray<MonsterDef>(math.max(n, 1), Allocator.Persistent);
            config.MonsterNames = new string[n];
            for (int i = 0; i < n; i++)
            {
                var m = source.Monsters[i];
                config.MonsterNames[i] = m.Name;
                config.Monsters[i] = new MonsterDef
                {
                    Color = new float4(m.Color.r, m.Color.g, m.Color.b, 1f),
                    Radius = m.Radius, Health = m.Health, Attack = m.Attack, Armour = m.Armour, Speed = m.Speed,
                    AttackRate = m.AttackRate, Range = m.Range, Aggro = m.Aggro, Xp = m.Xp, ProjectileSpeed = m.ProjectileSpeed,
                    SpawnWeight = m.SpawnWeight, Ranged = m.Ranged, Boss = m.Boss,
                };
            }
            // Gear ids: 0 = none; then weapon / armour per tier.
            var l = source.Loot;
            config.Gear = new GearDef[1 + 2 * l.GearTiers];
            for (int t = 1; t <= l.GearTiers; t++)
            {
                config.Gear[t * 2 - 1] = new GearDef { Slot = GearSlot.Weapon, Tier = t, Attack = l.WeaponAttackPerTier * t };
                config.Gear[t * 2] = new GearDef { Slot = GearSlot.Armour, Tier = t, Armour = l.ArmourPerTier * t, Health = l.HealthPerTier * t };
            }
            return config;
        }

        public int GearId(GearSlot slot, int tier) => math.clamp(tier, 1, Loot.GearTiers) * 2 - (slot == GearSlot.Weapon ? 1 : 0);

        public string GearName(int id)
        {
            if (id <= 0 || id >= Gear.Length) return "-";
            var g = Gear[id];
            return g.Slot == GearSlot.Weapon ? $"Sword +{g.Tier} (ATK +{g.Attack:0})" : $"Mail +{g.Tier} (ARM +{g.Armour:0}, HP +{g.Health:0})";
        }

        public int BossKind
        {
            get
            {
                for (int i = 0; i < Monsters.Length; i++) if (Monsters[i].Boss) return i + 1;
                return 0;
            }
        }

        public void Dispose()
        {
            if (Monsters.IsCreated) Monsters.Dispose();
        }
    }
}
