using System;
using SPF.Contracts;
using Unity.Mathematics;

namespace SnakeFoundation
{
    [Flags]
    public enum SnakeFlags : byte
    {
        None = 0,
        Player = 1 << 0,
        AI = 1 << 1,
        Dead = 1 << 2,
        Boosting = 1 << 3,
        InWindow = 1 << 4,
    }

    /// <summary>Per-snake identity and slow-changing state.</summary>
    public struct SnakeInfo
    {
        public int Id;
        public byte Region;
        public SnakeFlags Flags;
        public ushort Skin;
        public int Kills;
        public float Protection;      // seconds of spawn / portal invulnerability left
        public float PortalCooldown;
        public float BoostDropAccumulator;
        public float SkillCooldown;

        public bool Has(SnakeFlags flag) => (Flags & flag) != 0;
    }

    /// <summary>Steering request for this tick, from player input or AI.</summary>
    public struct SnakeControl
    {
        public float2 TargetDirection;
        public bool Boost;
        public bool UseSkill;
    }

    public enum ContactKind : byte
    {
        None = 0,
        Body = 1,
        Head = 2,
        Wall = 3,
    }

    /// <summary>Written by the contact job at the snake's own row, consumed by resolve in the same tick.</summary>
    public struct SnakeContact
    {
        public ContactKind Kind;
        public int OtherRow;
    }

    public enum AIIntent : byte
    {
        Wander = 0,
        SeekFood = 1,
        Flee = 2,
        Hunt = 3,
    }

    public struct AIState
    {
        public AIIntent Intent;
        public float2 Target;
        public int TargetRow;
        public uint NextDecisionTick;
        public float Aggression;
        public float Greed;
        public float Caution;
        public float ThreatDistance;
    }

    public struct FoodInfo
    {
        public float Value;
        public float Radius;
        public int Chunk;
        public uint Color;
    }

    public struct PropInfo
    {
        public byte Kind;     // index into prop definitions (1-based; 0 unused)
        public float Radius;
        public int Chunk;
    }

    public struct ProjectileState
    {
        public float2 PreviousPosition;
        public float2 Velocity;
        public float Life;
        public float Radius;
        public int OwnerId;
        public int OwnerRow;
        public byte Skill;
    }

    /// <summary>What the player wants this tick (written on the main thread by an input source).</summary>
    [Serializable]
    public struct PlayerCommand : IEquatable<PlayerCommand>
    {
        public float2 Direction;   // zero = keep heading
        public bool Boost;
        public bool Skill;

        public bool Equals(PlayerCommand other) => Direction.Equals(other.Direction) && Boost == other.Boost && Skill == other.Skill;
    }

    // ---- Event / request payloads exchanged between jobs and main-thread systems. ----

    public enum DeathCause : byte
    {
        Wall = 0,
        Body = 1,
        HeadOn = 2,
    }

    public struct DeathEvent
    {
        public EntityHandle Victim;
        public EntityHandle Killer;
        public DeathCause Cause;
    }

    public enum EatTarget : byte
    {
        Food = 0,
        Prop = 1,
    }

    public struct EatCandidate : IComparable<EatCandidate>
    {
        public EatTarget TargetType;
        public int TargetRow;
        public int SnakeRow;

        public int CompareTo(EatCandidate other)
        {
            int c = ((int)TargetType).CompareTo((int)other.TargetType);   // no Enum.CompareTo: it boxes (Burst / IL2CPP)
            if (c != 0) return c;
            c = TargetRow.CompareTo(other.TargetRow);
            return c != 0 ? c : SnakeRow.CompareTo(other.SnakeRow);
        }
    }

    public struct ProjectileHit : IComparable<ProjectileHit>
    {
        public int ProjectileRow;
        public int SnakeRow;
        public float2 Position;

        public int CompareTo(ProjectileHit other)
        {
            int c = ProjectileRow.CompareTo(other.ProjectileRow);
            return c != 0 ? c : SnakeRow.CompareTo(other.SnakeRow);
        }
    }

    public struct FoodSpawnRequest
    {
        public float2 Position;
        public float Value;
        public uint Color;
    }

    public struct ProjectileSpawnRequest
    {
        public float2 Position;
        public float2 Velocity;
        public int OwnerId;
        public int OwnerRow;
        public byte Skill;
    }

    public enum FeedbackKind : byte
    {
        Eat = 0,
        Death = 1,
        Kill = 2,
        Pickup = 3,
        Hit = 4,
        SkillCast = 5,
        Portal = 6,
    }

    /// <summary>Things presentation / audio may react to. Accumulates until presentation consumes it.</summary>
    public struct FeedbackEvent
    {
        public FeedbackKind Kind;
        public float2 Position;
        public float Size;
        public int SnakeId;
        public bool InvolvesPlayer;
    }
}
