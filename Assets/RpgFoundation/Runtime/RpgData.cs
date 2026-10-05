using System;
using SPF.Contracts;
using SPF.L2.Combat;
using Unity.Mathematics;

namespace RpgFoundation
{
    /// <summary>Meaning of the eight <see cref="SPF.L2.Stats.StatBlock"/> slots in this game.</summary>
    public static class Stat
    {
        public const int MaxHealth = 0, Attack = 1, Armour = 2, Speed = 3, AttackRate = 4, Crit = 5, Regen = 6, Range = 7;
    }

    /// <summary>Modifier sources (ModifierSet.Source).</summary>
    public static class ModSource
    {
        public const byte Weapon = 1, Armour = 2, Haste = 3, Slow = 4;
    }

    /// <summary>Input button indices (InputFrame bits).</summary>
    public static class RpgButton
    {
        public const int Attack = 0, Skill = 1, Potion = 2;
    }

    public enum Team : byte { Hero = 0, Monsters = 1 }

    [Flags]
    public enum ActorFlags : byte
    {
        None = 0,
        Hero = 1,
        Dead = 2,
        Boss = 4,
        Ranged = 8,
    }

    public struct ActorInfo
    {
        public int Id;
        public byte Kind;          // 0 = hero, 1.. = monster kind (index + 1 into Config.Monsters)
        public Team Team;
        public ActorFlags Flags;
        public float Radius;
        public bool Has(ActorFlags f) => (Flags & f) != 0;
    }

    public enum AIState : byte { Idle, Chase, Attack, Return }

    public struct Brain
    {
        public AIState State;
        public float2 Home;
        public float2 WanderTarget;
        public float StateTime;
        public float LostSight;     // seconds without line of sight while chasing
        public float Provoked;      // seconds of forced aggro after being hit
    }

    public enum ActorAction : byte { None, Melee, Shoot, Skill }

    public struct CombatState
    {
        public Cooldown Attack;
        public Cooldown Skill;
        public Cooldown Potion;
        public ActorAction Action;
        public float HitFlash;
    }

    public struct ProjectileInfo
    {
        public float2 Velocity;
        public float Radius;
        public float Life;
        public float Damage;
        public float CritChance;
        public Team Team;
        public int OwnerId;
        public bool Fireball;
    }

    public enum ItemKind : byte { None = 0, Gold = 1, Potion = 2, Gear = 3 }

    public struct ItemInfo
    {
        public ItemKind Kind;
        public int Value;        // gold amount, potion count, gear id
        public float Radius;
        public float Age;
    }

    public enum GearSlot : byte { Weapon = 0, Armour = 1 }

    // ---- Events between jobs and main-thread systems ----

    public struct HitEvent
    {
        public int AttackerId;
        public int TargetRow;
        public float Damage;      // before the target's armour
        public float CritChance;
        public float2 Position;
        public bool Projectile;
    }

    public struct ProjectileRequest
    {
        public float2 Position;
        public float2 Direction;
        public float Speed;
        public float Damage;
        public float CritChance;
        public Team Team;
        public int OwnerId;
        public bool Fireball;
    }

    public struct DeathEvent
    {
        public byte Kind;
        public float2 Position;
        public int Floor;
        public bool Boss;
    }

    public enum FeedbackKind : byte { Damage, Crit, HeroHurt, Heal, Gold, Item, LevelUp, Death, Stairs }

    /// <summary>For presentation (damage numbers, flashes); drained by the renderer.</summary>
    public struct FeedbackEvent
    {
        public FeedbackKind Kind;
        public float2 Position;
        public float Value;
    }

    public enum RpgFlow : byte { Menu, Playing, FloorClear, Dead, Victory }

    public enum RpgCommandKind : byte { NewGame, Continue, Descend, Retry, Menu, Equip, UsePotion }

    public struct RpgCommand
    {
        public RpgCommandKind Kind;
        public int Argument;
    }
}
