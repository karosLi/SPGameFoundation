using System;
using System.Collections.Generic;
using SPF.L2.Progression;
using SPF.L2.AI;
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
        public WeaponKind Weapon;
        public byte Skill;
        public StatusKind Status;
        public float StatusPower, StatusDuration;
    }

    /// <summary>Blittable weapon family definition, indexed by <see cref="WeaponKind"/>.</summary>
    public struct WeaponDef
    {
        public WeaponKind Kind;
        public float DamageMul, AttackRate, Range, ArcCos, Windup, Recover, Knockback, Stagger;
        public bool Ranged;
        public float ProjectileSpeed, ProjectileRadius;
        public int Pierce;
        public ProjectileVisual Visual;
    }

    /// <summary>Blittable skill definition, indexed by skill id - 1.</summary>
    public struct SkillDef
    {
        public SkillKind Kind;
        public float ManaCost, Cooldown, CastTime, Power, Radius, Duration, Speed, Knockback, Slow, SlowDuration;
        public StatusKind Status;
        public float StatusPower, StatusDuration;
        public int UnlockLevel;
    }

    public struct GearDef
    {
        public GearSlot Slot;
        public int Tier;
        public WeaponKind Weapon;
        public float Attack, Armour, Health;
    }

    /// <summary>Blittable settings used by jobs.</summary>
    public struct RpgSettings
    {
        public float HeroRadius, HeroSpeed, HeroCrit, CritMultiplier, HeroRegen, ArcCos;
        public float HeroHealth, HeroHealthPerLevel, HeroAttack, HeroAttackPerLevel, HeroArmour, HeroArmourPerLevel;
        public float HeroMana, HeroManaPerLevel, HeroManaRegen;
        public float PotionHeal, PotionCooldown, PickupRadius;
        public float ScalingPerFloor;
        public LevelCurve Levels;
    }

    /// <summary>Config baked for the session: settings, monster / weapon / skill / gear tables, names.</summary>
    public sealed class RpgRuntimeConfig : IDisposable
    {
        public RpgSettings Settings;
        public bool UseDecisionTree;
        public NativeArray<MonsterDef> Monsters;
        public NativeArray<DecisionNode> CombatDecisionProgram; // immutable derived configuration, not snapshot state
        public NativeArray<WeaponDef> Weapons;     // indexed by WeaponKind
        public NativeArray<SkillDef> Skills;       // indexed by skill id - 1
        public GearDef[] Gear;
        public string[] MonsterNames, WeaponNames, SkillNames;
        public int[] HeroSkillSlots;
        public WeaponKind[] LootWeapons;
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
                    HeroRadius = h.Radius, HeroSpeed = h.Speed, HeroCrit = h.Crit, CritMultiplier = h.CritMultiplier,
                    HeroRegen = h.Regen, ArcCos = h.ArcCos,
                    HeroHealth = h.Health, HeroHealthPerLevel = h.HealthPerLevel, HeroAttack = h.Attack, HeroAttackPerLevel = h.AttackPerLevel,
                    HeroArmour = h.Armour, HeroArmourPerLevel = h.ArmourPerLevel,
                    HeroMana = h.Mana, HeroManaPerLevel = h.ManaPerLevel, HeroManaRegen = h.ManaRegen,
                    PotionHeal = h.PotionHeal, PotionCooldown = h.PotionCooldown, PickupRadius = h.PickupRadius,
                    ScalingPerFloor = source.Dungeon.ScalingPerFloor,
                    Levels = new LevelCurve { Base = h.XpBase, Growth = h.XpGrowth, MaxLevel = h.MaxLevel },
                },
                UseDecisionTree = source.UseDecisionTree,
                Dungeon = source.Dungeon,
                Loot = source.Loot,
                Capacity = source.Capacity,
                StartPotions = h.StartPotions,
                CombatDecisionProgram = RpgDecisions.CreateProgram(),
                HeroSkillSlots = h.SkillSlots ?? new int[0],
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
                    SpawnWeight = m.SpawnWeight, Ranged = m.Ranged, Boss = m.Boss, Weapon = m.Weapon, Skill = (byte)math.max(m.Skill, 0),
                    Status = m.Status, StatusPower = m.StatusPower, StatusDuration = m.StatusDuration,
                };
            }

            int kinds = Enum.GetValues(typeof(WeaponKind)).Length;
            config.Weapons = new NativeArray<WeaponDef>(kinds, Allocator.Persistent);
            config.WeaponNames = new string[kinds];
            var loot = new List<WeaponKind>();
            foreach (var w in source.Weapons)
            {
                int k = (int)w.Kind;
                config.WeaponNames[k] = w.Name;
                config.Weapons[k] = new WeaponDef
                {
                    Kind = w.Kind, DamageMul = w.DamageMul, AttackRate = w.AttackRate, Range = w.Range, ArcCos = w.ArcCos,
                    Windup = w.Windup, Recover = w.Recover, Knockback = w.Knockback, Stagger = w.Stagger, Ranged = w.Ranged,
                    ProjectileSpeed = w.ProjectileSpeed, ProjectileRadius = w.ProjectileRadius, Pierce = w.Pierce, Visual = w.Visual,
                };
                if (w.Lootable) loot.Add(w.Kind);
            }
            config.LootWeapons = loot.ToArray();

            int skills = source.Skills.Count;
            config.Skills = new NativeArray<SkillDef>(math.max(skills, 1), Allocator.Persistent);
            config.SkillNames = new string[skills];
            for (int i = 0; i < skills; i++)
            {
                var s = source.Skills[i];
                config.SkillNames[i] = s.Name;
                config.Skills[i] = new SkillDef
                {
                    Kind = s.Kind, ManaCost = s.ManaCost, Cooldown = s.Cooldown, CastTime = s.CastTime, Power = s.Power, Radius = s.Radius,
                    Duration = s.Duration, Speed = s.Speed, Knockback = s.Knockback, Slow = s.Slow, SlowDuration = s.SlowDuration,
                    Status = s.Status, StatusPower = s.StatusPower, StatusDuration = s.StatusDuration,
                    UnlockLevel = s.UnlockLevel,
                };
            }

            // Gear ids: 0 = none; per tier: one id per lootable weapon family, then the armour.
            var l = source.Loot;
            int perTier = config.LootWeapons.Length + 1;
            config.Gear = new GearDef[1 + perTier * l.GearTiers];
            for (int t = 1; t <= l.GearTiers; t++)
            {
                int baseId = 1 + (t - 1) * perTier;
                for (int k = 0; k < config.LootWeapons.Length; k++)
                    config.Gear[baseId + k] = new GearDef { Slot = GearSlot.Weapon, Tier = t, Weapon = config.LootWeapons[k], Attack = l.WeaponAttackPerTier * t };
                config.Gear[baseId + perTier - 1] = new GearDef { Slot = GearSlot.Armour, Tier = t, Armour = l.ArmourPerTier * t, Health = l.HealthPerTier * t };
            }
            return config;
        }

        /// <summary>Gear id of a sword (weapon slot) or armour of a tier.</summary>
        public int GearId(GearSlot slot, int tier) => slot == GearSlot.Weapon ? GearId(WeaponKind.Sword, tier) : ArmourId(tier);

        public int GearId(WeaponKind weapon, int tier)
        {
            int perTier = LootWeapons.Length + 1;
            int t = math.clamp(tier, 1, Loot.GearTiers);
            int k = Array.IndexOf(LootWeapons, weapon);
            return k < 0 ? 0 : 1 + (t - 1) * perTier + k;
        }

        public int ArmourId(int tier)
        {
            int perTier = LootWeapons.Length + 1;
            return 1 + (math.clamp(tier, 1, Loot.GearTiers) - 1) * perTier + perTier - 1;
        }

        /// <summary>Weapon family the hero fights with for an equipped gear id (no weapon: sword).</summary>
        public WeaponKind HeroWeapon(int gearId) => gearId > 0 && gearId < Gear.Length && Gear[gearId].Slot == GearSlot.Weapon ? Gear[gearId].Weapon : WeaponKind.Sword;

        public string GearName(int id)
        {
            if (id <= 0 || id >= Gear.Length) return "-";
            var g = Gear[id];
            return g.Slot == GearSlot.Weapon ? $"{WeaponNames[(int)g.Weapon]} +{g.Tier} (ATK +{g.Attack:0})" : $"Mail +{g.Tier} (ARM +{g.Armour:0}, HP +{g.Health:0})";
        }

        public int BossKind
        {
            get
            {
                for (int i = 0; i < Monsters.Length; i++) if (Monsters[i].Boss) return i + 1;
                return 0;
            }
        }

        /// <summary>Hero loadout for a level: equipped weapon family, skills unlocked so far in their slots.</summary>
        public Loadout HeroLoadout(int level, int weaponGear)
        {
            var loadout = new Loadout { Weapon = HeroWeapon(weaponGear) };
            for (int slot = 0; slot < RpgButton.SkillSlots && slot < HeroSkillSlots.Length; slot++)
            {
                int id = HeroSkillSlots[slot];
                if (id > 0 && id <= Skills.Length && Skills[id - 1].UnlockLevel <= level)
                    loadout.SetSkill(slot, (byte)id);
            }
            return loadout;
        }

        public void Dispose()
        {
            if (Monsters.IsCreated) Monsters.Dispose();
            if (Weapons.IsCreated) Weapons.Dispose();
            if (Skills.IsCreated) Skills.Dispose();
            if (CombatDecisionProgram.IsCreated) CombatDecisionProgram.Dispose();
        }
    }
}
