using System;
using SPF.Contracts;
using SPF.L2.Combat;
using Unity.Mathematics;
using SPF.L2.Stats;

namespace RpgFoundation
{
    /// <summary>Meaning of the eight <see cref="SPF.L2.Stats.StatBlock"/> slots in this game.</summary>
    public static class Stat
    {
        public const int MaxHealth = 0, Attack = 1, Armour = 2, Speed = 3, AttackRate = 4, Crit = 5, Regen = 6, Range = 7;
        public const int MaxMana = 8, ManaRegen = 9, SkillPower = 10;
    }

    /// <summary>Modifier sources (ModifierSet.Source).</summary>
    public static class ModSource
    {
        public const byte Weapon = 1, Armour = 2, Haste = 3, Slow = 4;
    }

    /// <summary>Input button indices (InputFrame bits): attack, four skill slots, potion.</summary>
    public static class RpgButton
    {
        public const int Attack = 0, Skill1 = 1, Skill2 = 2, Skill3 = 3, Skill4 = 4, Potion = 5;
        /// <summary>First skill slot (kept for callers that only use slot 1).</summary>
        public const int Skill = Skill1;
        public const int SkillSlots = 4;
    }

    /// <summary>Weapon families: they decide reach, arc, timing and whether the attack is a projectile.</summary>
    public enum WeaponKind : byte { Sword, Axe, Spear, Bow, Staff, Claw, Hammer }

    public enum SkillKind : byte { None, Projectile, Dash, Whirlwind, Nova, Slam }

    /// <summary>What an actor carries: weapon family and up to four skills (skill ids, 0 = empty).</summary>
    public struct Loadout
    {
        public WeaponKind Weapon;
        public byte S0, S1, S2, S3;

        public byte Skill(int slot) => slot == 0 ? S0 : slot == 1 ? S1 : slot == 2 ? S2 : S3;

        public void SetSkill(int slot, byte skill)
        {
            switch (slot) { case 0: S0 = skill; break; case 1: S1 = skill; break; case 2: S2 = skill; break; default: S3 = skill; break; }
        }
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
        Elite = 16,
    }

    /// <summary>Elite monster modifiers (several can combine).</summary>
    [Flags]
    public enum Affix : byte
    {
        None = 0,
        Swift = 1,      // much faster
        Tough = 2,      // more health and armour
        Burning = 4,    // hits set the hero on fire
        Venomous = 8,   // hits poison
        Frenzied = 16,  // attacks much faster
    }

    public struct ActorInfo
    {
        public int Id;
        public byte Kind;          // 0 = hero, 1.. = monster kind (index + 1 into Config.Monsters)
        public Team Team;
        public ActorFlags Flags;
        public Affix Affixes;
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

    /// <summary>Requested action this tick (input / AI). Attack uses the weapon (melee or projectile).</summary>
    public enum ActorAction : byte { None, Attack, Skill }

    /// <summary>
    /// Timed action phase, simulated (so damage lands when the animation strikes) and read by presentation
    /// to pick animations: wind-up → strike → recover for attacks; cast → release for skills; channel for
    /// whirlwind; dash; stagger after a heavy hit.
    /// </summary>
    public enum ActionPhase : byte { None, Windup, Recover, Cast, Channel, Dash, Stagger }

    public struct CombatState
    {
        public Cooldown Attack;
        public Cooldown Potion;
        public float4 SkillCooldown;       // per slot, seconds
        public ActorAction Action;         // request
        public byte RequestSlot;           // skill slot of a Skill request
        public ActionPhase Phase;
        public float PhaseTime, PhaseDuration;
        public byte PhaseSkill;            // skill id being cast / channelled
        public byte PhaseTicks;            // channel ticks done
        public float2 Aim;                 // direction locked when the action started
        public float2 Knockback;           // velocity, decays
        public float Invulnerable;         // seconds (dash)
        public bool PropInReach;           // hero: a breakable prop is in reach (swing even with no enemy)
        public float HitFlash;

        public float PhaseProgress => PhaseDuration > 0f ? math.saturate(PhaseTime / PhaseDuration) : 1f;
        public bool Busy => Phase != ActionPhase.None && Phase != ActionPhase.Recover;
    }

    /// <summary>Damage over time. Burn refreshes (strongest wins); poison stacks up to three applications.</summary>
    public enum StatusKind : byte { None, Burn, Poison }

    /// <summary>A status a hit applies: damage per second for a duration.</summary>
    public struct StatusHit
    {
        public StatusKind Kind;
        public float Dps, Duration;

        public static StatusHit From(StatusKind kind, float hitDamage, float power, float duration) =>
            kind == StatusKind.None || duration <= 0f ? default
            : new StatusHit { Kind = kind, Dps = hitDamage * power / duration, Duration = duration };
    }

    /// <summary>Active damage-over-time effects on an actor, and the status its own hits apply.</summary>
    public struct StatusState
    {
        public float Burn, BurnDps;        // seconds left, damage per second
        public float Poison, PoisonDps;
        public float Tick;                 // seconds to the next damage tick
        /// <summary>Status this actor's own hits apply (total damage = hit x power, over the duration).</summary>
        public StatusKind OnHit;
        public float OnHitPower, OnHitDuration;

        public StatusHit HitStatus(float hitDamage) => StatusHit.From(OnHit, hitDamage, OnHitPower, OnHitDuration);

        public const float TickInterval = 0.5f;
        public const int PoisonStacks = 3;

        public bool Burning => Burn > 0f;
        public bool Poisoned => Poison > 0f;

        public void Apply(in StatusHit hit)
        {
            if (hit.Kind == StatusKind.None) return;
            // Damage lands at the end of each interval (a fresh status first ticks after one interval).
            if (!Burning && !Poisoned) Tick = TickInterval;
            switch (hit.Kind)
            {
                case StatusKind.Burn:
                    Burn = math.max(Burn, hit.Duration);
                    BurnDps = math.max(Burn > 0f && BurnDps > 0f ? BurnDps : 0f, hit.Dps);
                    break;
                case StatusKind.Poison:
                    PoisonDps = Poison > 0f ? math.min(PoisonDps + hit.Dps, hit.Dps * PoisonStacks) : hit.Dps;
                    Poison = math.max(Poison, hit.Duration);
                    break;
            }
        }
    }

    /// <summary>Projectile look (presentation) and impact flavour.</summary>
    public enum ProjectileVisual : byte { Arrow, Fireball, Bolt, Spit }

    public struct ProjectileInfo
    {
        public float2 Velocity;
        public float Radius;
        public float Life;
        public float Damage;
        public float CritChance;
        public float Knockback;
        public float ExplodeRadius;     // > 0: area damage on impact
        public int Pierce;              // extra targets it passes through
        public int LastHit;             // row hit last (pierce never hits the same actor twice in a row)
        public Team Team;
        public int OwnerId;
        public ProjectileVisual Visual;
        public StatusHit Status;
    }

    public enum ItemKind : byte { None = 0, Gold = 1, Potion = 2, Gear = 3 }

    public struct ItemInfo
    {
        public ItemKind Kind;
        public int Value;        // gold amount, potion count, gear id
        public float Radius;
        public float Age;
    }

    /// <summary>Dungeon furniture: chests (open on touch), barrels (break when struck), spike traps (cycle up and down).</summary>
    public enum PropKind : byte { None = 0, Chest = 1, Barrel = 2, Spikes = 3 }

    public struct PropInfo
    {
        public PropKind Kind;
        public bool Active;     // chest opened, spikes up
        public float Timer;     // spikes: position in the cycle
        public float Radius;
    }

    public enum GearSlot : byte { Weapon = 0, Armour = 1 }

    // ---- Events between jobs and main-thread systems ----

    /// <summary>What dealt a hit (presentation picks the impact effect).</summary>
    public enum HitSource : byte { Weapon, Projectile, Explosion, Whirlwind, Nova, Slam, Burn, Poison, Trap }

    public struct HitEvent
    {
        public int AttackerId;
        public int TargetRow;
        public float Damage;      // before the target's armour
        public float CritChance;
        public float2 Position;
        public float2 Direction;  // push direction
        public float Knockback;   // impulse (velocity) before the target's weight
        public float Stagger;     // seconds of stagger for light targets
        public SPF.L2.Stats.Modifier Mod;   // Source 0 = none (e.g. frost slow)
        public StatusHit Status;            // damage over time to apply (Kind None = nothing)
        public bool IgnoreArmour;           // status ticks: armour does not reduce them, no crits
        public HitSource Source;
    }

    public struct ProjectileRequest
    {
        public float2 Position;
        public float2 Direction;
        public float Speed;
        public float Radius;
        public float Damage;
        public float CritChance;
        public float Knockback;
        public float ExplodeRadius;
        public int Pierce;
        public float Life;
        public Team Team;
        public int OwnerId;
        public ProjectileVisual Visual;
        public StatusHit Status;
    }

    public struct DeathEvent
    {
        public byte Kind;
        public float2 Position;
        public int Floor;
        public bool Boss;
        public bool Elite;
    }

    public enum FeedbackKind : byte
    {
        Damage, Crit, HeroHurt, Heal, Gold, Item, LevelUp, Death, Stairs,
        Swing, Explosion, Nova, Whirlwind, SlamWarning, Slam, Dash, Cast, Mana, Chest, Barrel, Spikes,
    }

    /// <summary>For presentation (damage numbers, impacts, skill visuals); drained by the renderer.</summary>
    public struct FeedbackEvent
    {
        public FeedbackKind Kind;
        public float2 Position;
        public float Value;        // amount, radius or duration depending on the kind
        public float2 Direction;
        public byte Actor;         // actor kind (0 hero, monster kind) for deaths / swings
        public WeaponKind Weapon;
        public HitSource Source;
    }

    public enum RpgFlow : byte { Menu, Playing, FloorClear, Dead, Victory }

    public enum RpgCommandKind : byte { NewGame, Continue, Descend, Retry, Menu, Equip, UsePotion, Buy }

    public struct RpgCommand
    {
        public RpgCommandKind Kind;
        public int Argument;
    }
}
