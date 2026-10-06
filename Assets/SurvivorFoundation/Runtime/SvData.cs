using System;
using Unity.Mathematics;

namespace SurvivorFoundation
{
    public enum SvFlow : byte { Menu, Playing, LevelUp, Dead, Won }

    public enum SvVariant : byte { Classic, GuardBeacon }
    public enum SvLossReason : byte { None, HeroFell, BeaconLost }

    public enum SvCommandKind : byte { Start, Choose, Menu }

    public struct SvCommand
    {
        public SvCommandKind Kind;
        public int Argument;
    }

    /// <summary>Hero upgrades offered on level-up: four weapons and four passives (levels 0..MaxLevel).</summary>
    public enum Upgrade : byte { Bolt, Nova, Spiral, Orbit, Speed, Vitality, Magnet, Might }

    [Flags]
    public enum EnemyFlags : byte { None = 0, Dead = 1, Elite = 2, Shooter = 4 }

    public struct EnemyInfo
    {
        public byte Kind;            // 1.. into the enemy table
        public EnemyFlags Flags;
        public float Radius, Speed, Damage;
        public float Hp, MaxHp;
        public float Flash;          // seconds of hit flash
        public float Timer, Angle;   // shooters: pattern emitter state
        public bool Has(EnemyFlags f) => (Flags & f) != 0;
    }

    public enum BulletTeam : byte { Hero, Enemy }

    /// <summary>Bullet look (presentation).</summary>
    public enum BulletVisual : byte { Bolt, Nova, Spiral, EnemyOrb }

    /// <summary>A pooled (handle-free) bullet.</summary>
    public struct BulletInfo
    {
        public float2 Velocity;
        public float Radius, Damage, Life;
        public BulletTeam Team;
        public BulletVisual Visual;
        public short Pierce;         // extra enemies it passes through
        public int LastHit;          // enemy row hit last (pierce never hits it twice in a row)
    }

    /// <summary>A pooled experience gem.</summary>
    public struct GemInfo
    {
        public int Value;
        public bool Magnet;          // flying to the hero
    }

    public struct SvHit
    {
        public int Target;
        public float Damage;
        public float2 Knock;
    }

    public struct BulletSpawn
    {
        public float2 Position, Velocity;
        public float Radius, Damage, Life;
        public BulletTeam Team;
        public BulletVisual Visual;
        public short Pierce;
    }

    public struct SvDeath
    {
        public float2 Position;
        public int Xp;
        public byte Kind;
        public bool Elite;
    }

    public enum SvFeedbackKind : byte { Hit, Death, Gem, LevelUp, HeroHurt, Shoot, Nova }

    public struct SvFeedback
    {
        public SvFeedbackKind Kind;
        public float2 Position;
        public float Value;
        public byte Enemy;
    }

    /// <summary>Blittable enemy definition (index = kind - 1).</summary>
    public struct EnemyDef
    {
        public float4 Color;
        public float Radius, Speed, Hp, Damage;
        public int Xp;
        public float SpawnFrom, Weight;
        public bool Shooter;
        public float KeepDistance;
        public SPF.L2.Combat.PatternEmitter Pattern;
    }

    [Serializable]
    public struct SvSettings
    {
        public float ArenaHalf;
        public float HeroSpeed, HeroHp, HeroRadius, HurtInvulnerable;
        public float SpawnRadius, SpawnPerSecond, SpawnGrowth, EliteEvery;
        public int MaxEnemies;
        public float PickupRadius, MagnetRadius, GemSpeed;
        public float XpBase, XpPerLevel;
        public float BoltCooldown, BoltDamage, BoltSpeed;
        public float NovaCooldown, NovaDamage;
        public float SpiralInterval, SpiralDamage;
        public float OrbitRadius, OrbitDps;
        public float EnemyBulletDamage;
        // Opt-in original guard example; zero/default preserves classic Survivor.
        public SvVariant Variant;
        public float2 BeaconPosition;
        public float BeaconHp, BeaconRadius, GuardAggroRadius;
        public int GuardDurationTicks, BeaconHurtCooldownTicks;
        public SvAnnularSkill AnnularSkill;
        public int ReorderInterval;      // ticks between spatial sorts of the enemy table (0 = off)
        public float GridCell;
    }
}
