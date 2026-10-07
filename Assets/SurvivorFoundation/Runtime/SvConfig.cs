using System;
using System.Collections.Generic;
using SPF.L2.Combat;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace SurvivorFoundation
{
    /// <summary>Authoring config of the bullet-heaven validation game (defaults are playable).</summary>
    [CreateAssetMenu(menuName = "SPF/Survivor/Config", fileName = "SurvivorConfig")]
    public sealed class SvConfig : ScriptableObject
    {
        public SvSettings Settings = DefaultSettings();
        public bool MobileSkills;
        public bool WeaponCombat;
        public SvPulseDefinition ComposedPulse;
        public SPF.L2.Weapons.WeaponProfile[] WeaponProfiles;
        public List<EnemyEntry> Enemies = EnemyEntry.Defaults();
        public CapacitySection Capacity = new CapacitySection();

        [Serializable]
        public sealed class EnemyEntry
        {
            public string Name;
            public Color Color = Color.white;
            public float Radius = 0.4f, Speed = 2f, Hp = 10f, Damage = 5f;
            public int Xp = 1;
            [Tooltip("Seconds into the run before this enemy starts spawning")] public float SpawnFrom;
            public float Weight = 1f;
            public bool Shooter;
            [Tooltip("Shooters stop at this distance from the hero")] public float KeepDistance = 6f;
            public PatternEmitter Pattern;

            public static List<EnemyEntry> Defaults() => new List<EnemyEntry>
            {
                new EnemyEntry { Name = "Bat", Color = new Color(0.55f, 0.35f, 0.75f), Radius = 0.3f, Speed = 3.3f, Hp = 6f, Damage = 4f, Xp = 1, SpawnFrom = 0f, Weight = 5f },
                new EnemyEntry { Name = "Zombie", Color = new Color(0.45f, 0.7f, 0.4f), Radius = 0.42f, Speed = 1.7f, Hp = 22f, Damage = 8f, Xp = 2, SpawnFrom = 15f, Weight = 3f },
                new EnemyEntry { Name = "Brute", Color = new Color(0.75f, 0.35f, 0.3f), Radius = 0.75f, Speed = 1.3f, Hp = 110f, Damage = 15f, Xp = 8, SpawnFrom = 60f, Weight = 0.6f },
                new EnemyEntry
                {
                    Name = "Hex Mage", Color = new Color(0.35f, 0.55f, 0.95f), Radius = 0.4f, Speed = 1.9f, Hp = 30f, Damage = 6f, Xp = 4, SpawnFrom = 30f, Weight = 0.8f,
                    Shooter = true, KeepDistance = 6.5f, Pattern = PatternEmitter.Spiral(3, 4f, 0.3f, 0.35f),
                },
            };
        }

        [Serializable]
        public sealed class CapacitySection
        {
            public int Enemies = 4096;
            public int Bullets = 32768;
            public int Gems = 8192;
            public int Events = 16384;
        }

        public static SvSettings DefaultSettings() => new SvSettings
        {
            ArenaHalf = 150f,
            HeroSpeed = 5f, HeroHp = 100f, HeroRadius = 0.35f, HurtInvulnerable = 0.4f,
            SpawnRadius = 17f, SpawnPerSecond = 2.5f, SpawnGrowth = 0.12f, EliteEvery = 60f,
            MaxEnemies = 3000,
            PickupRadius = 0.6f, MagnetRadius = 2.5f, GemSpeed = 12f,
            XpBase = 5f, XpPerLevel = 4f,
            BoltCooldown = 0.7f, BoltDamage = 10f, BoltSpeed = 14f,
            NovaCooldown = 2.4f, NovaDamage = 8f,
            SpiralInterval = 0.12f, SpiralDamage = 6f,
            OrbitRadius = 2.3f, OrbitDps = 30f,
            EnemyBulletDamage = 7f,
            // Measured on the Mac at 3000 enemies: no gain (the table fits in cache), so off by default;
            // the benchmark keeps the A/B for larger tables and cache-starved mobile CPUs.
            ReorderInterval = 0,
            GridCell = 2f,
        };

        /// <summary>Original example inspired by a horde/rings reference, not a reconstruction of its rules.</summary>
        public static SvConfig CreateGuardExample()
        {
            var config = CreateDefault();
            var s = config.Settings;
            s.Variant = SvVariant.GuardBeacon;
            s.ArenaHalf = 18f;
            s.BeaconPosition = new float2(0f, -3.5f);
            s.BeaconHp = 250f; s.BeaconRadius = 0.9f; s.GuardAggroRadius = 3.5f;
            s.GuardDurationTicks = 90 * 30; s.BeaconHurtCooldownTicks = 15;
            s.SpawnRadius = 12f; s.SpawnPerSecond = 4f; s.SpawnGrowth = 0.07f;
            s.MaxEnemies = 768; s.EliteEvery = 30f;
            s.AnnularSkill = new SvAnnularSkill { Enabled = true, RadiusA = 2.4f, RadiusB = 5.4f, HalfWidth = 0.24f, DamagePerSecond = 38f, TickInterval = 6 };
            config.Settings = s;
            // A modest pool for the runnable mobile example; capacities remain authorable.
            config.Capacity.Enemies = 1024; config.Capacity.Bullets = 4096;
            config.Capacity.Gems = 2048; config.Capacity.Events = 4096;
            return config;
        }

        /// <summary>Original simulation example: two periodic crossing blade paths, one damage per target per pulse.
        /// No presentation is installed by this factory. Classic projectile policy remains unchanged.</summary>
        public static SvConfig CreateCrossedBladeExample()
        {
            var config = CreateDefault();
            config.Settings.CrossedBlades = new SvCrossedBlades
            {
                Enabled = true, TickInterval = 12, MaxTargets = 256,
                Reach = 5f, HalfWidth = 0.35f, DamagePerPulse = 16f,
            };
            return config;
        }

        /// <summary>Portrait-first horde example with manual pulse and aimed blink skills.</summary>
        public static SvConfig CreateMobileCombatExample()
        {
            var config = CreateGuardExample();
            config.MobileSkills = true;
            return config;
        }

        /// <summary>Dense mobile sword-horde configuration on the same Survivor module, pools and jobs.</summary>
        public static SvConfig CreateFlyingSwordExample()
        {
            var config = CreateDefault();
            config.MobileSkills = true;
            var s = config.Settings;
            s.Variant = SvVariant.FlyingSwordHorde; s.FlyingSwords = SvFlyingSwords.Default;
            s.ArenaHalf = 18f; s.SpawnRadius = 10.5f; s.SpawnPerSecond = 11f; s.SpawnGrowth = .10f;
            s.MaxEnemies = 768; s.EliteEvery = 25f; s.HeroHp = 140f;
            s.MagnetRadius = 4f; s.XpBase = 8f; s.XpPerLevel = 6f;
            config.Settings = s;
            config.Capacity.Enemies = 1024; config.Capacity.Bullets = 1024;
            config.Capacity.Gems = 2048; config.Capacity.Events = 4096;
            config.Enemies[0].Hp = 18; config.Enemies[0].Speed = 2.1f;
            config.Enemies[1].SpawnFrom = 6f; config.Enemies[1].Hp = 40;
            config.Enemies[2].SpawnFrom = 25f; config.Enemies[2].Hp = 160;
            config.Enemies[3].SpawnFrom = 18f;
            return config;
        }

        public static SvConfig CreateWeaponCombatExample()
        {
            var config = CreateMobileCombatExample(); config.WeaponCombat = true;
            config.WeaponProfiles = SPF.L2.Weapons.WeaponProfiles.CreateDefaults(30);
            var settings = config.Settings; settings.AnnularSkill.Enabled = false; settings.SpawnPerSecond = 3f; settings.SpawnGrowth = .045f; settings.HeroHp = 160; config.Settings = settings;
            return config;
        }

        public static SvConfig CreateComposedPulseExample(bool repulse = true)
        {
            var config = CreateWeaponCombatExample();
            config.ComposedPulse = repulse ? SvPulseDefinition.Repulse : SvPulseDefinition.Wide;
            return config;
        }

        public static SvConfig CreateDefault()
        {
            var config = CreateInstance<SvConfig>();
            config.hideFlags = HideFlags.DontSave;
            return config;
        }
    }

    /// <summary>Config baked for jobs: settings + enemy table.</summary>
    public sealed class SvRuntime : IDisposable
    {
        public SvSettings Settings;
        public bool MobileSkills;
        public NativeArray<EnemyDef> Enemies;
        public string[] EnemyNames;

        public static SvRuntime Bake(SvConfig source)
        {
            source.Settings.FlyingSwords.Validate();
            var r = new SvRuntime { Settings = source.Settings, MobileSkills = source.MobileSkills };
            int n = source.Enemies.Count;
            r.Enemies = new NativeArray<EnemyDef>(math.max(n, 1), Allocator.Persistent);
            r.EnemyNames = new string[n];
            for (int i = 0; i < n; i++)
            {
                var e = source.Enemies[i];
                r.EnemyNames[i] = e.Name;
                r.Enemies[i] = new EnemyDef
                {
                    Color = new float4(e.Color.r, e.Color.g, e.Color.b, 1f), Radius = e.Radius, Speed = e.Speed, Hp = e.Hp, Damage = e.Damage,
                    Xp = e.Xp, SpawnFrom = e.SpawnFrom, Weight = e.Weight, Shooter = e.Shooter, KeepDistance = e.KeepDistance, Pattern = e.Pattern,
                };
            }
            return r;
        }

        public int EnemyKinds => EnemyNames.Length;

        public void Dispose()
        {
            if (Enemies.IsCreated) Enemies.Dispose();
        }
    }
}
