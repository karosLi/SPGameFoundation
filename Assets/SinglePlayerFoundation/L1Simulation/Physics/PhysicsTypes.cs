using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace SPF.L1.Physics
{
    public enum ShapeKind : byte { Circle, Box }

    public enum JointKind : byte
    {
        /// <summary>Pins two bodies together at an anchor (hinge).</summary>
        Revolute,
        /// <summary>Keeps the anchors exactly <see cref="Joint.Length"/> apart (rod).</summary>
        Rod,
        /// <summary>Keeps the anchors at most <see cref="Joint.Length"/> apart (rope, chain link).</summary>
        Rope,
        /// <summary>Soft distance constraint (<see cref="Joint.Frequency"/>, <see cref="Joint.DampingRatio"/>).</summary>
        Spring,
    }

    /// <summary>
    /// One rigid body (circle or box). Static and kinematic bodies have zero inverse mass and inertia; a kinematic
    /// body still moves by its velocity. Position is the centre of mass.
    /// </summary>
    public struct Body
    {
        public float2 Position;
        public float Angle;
        public float2 Velocity;
        public float AngularVelocity;
        public float InvMass, InvInertia;
        /// <summary>Box half extents; for a circle, x is the radius.</summary>
        public float2 Half;
        public float Friction, Restitution;
        public float LinearDamping, AngularDamping;
        public float GravityScale;
        public float2 Force;          // accumulated by the game, cleared every step
        public float Torque;
        public uint Layer, Mask;
        public int UserData;
        public float SleepTime;
        public ShapeKind Shape;
        public byte Alive;
        public byte Awake;
        public byte FixedRotation;

        public bool IsDynamic => InvMass > 0f;
        public float Radius => Shape == ShapeKind.Circle ? Half.x : math.length(Half);
        public float Mass => InvMass > 0f ? 1f / InvMass : 0f;
    }

    public struct Joint
    {
        public JointKind Kind;
        public byte Alive;
        public int A, B;
        public float2 LocalAnchorA, LocalAnchorB;
        public float Length;
        public float Frequency, DampingRatio;
        // Solver state (warm-started across steps).
        public float2 Impulse;
        public float2 RA, RB;
        public float2 Axis;
        public float Bias, Gamma, Mass;
        public float2x2 M;
        public byte Active;
    }

    /// <summary>One contact point of a manifold; <see cref="Feature"/> identifies it across steps (warm starting).</summary>
    public struct ContactPoint
    {
        public float2 Position;
        public float Separation;
        public float Pn, Pt;
        public float MassNormal, MassTangent;
        public float Bias;
        public float2 RA, RB;
        public uint Feature;
    }

    public struct Manifold
    {
        public int A, B;
        public float2 Normal;     // from A to B
        public int Count;
        public ContactPoint P0, P1;
        public float Friction, Restitution;
        public ulong Key => PairKey(A, B);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong PairKey(int a, int b) => a < b ? ((ulong)(uint)a << 32) | (uint)b : ((ulong)(uint)b << 32) | (uint)a;
    }

    /// <summary>A strong hit, reported after the step (damage, breaking, sounds).</summary>
    public struct ContactEvent
    {
        public int A, B;
        public float Impulse;
        public float2 Point, Normal;
    }

    public struct RayHit
    {
        public int Body;
        public float Fraction;
        public float2 Point, Normal;
    }

    public struct PhysicsSettings
    {
        public float2 Gravity;
        public int VelocityIterations;
        /// <summary>Fraction of penetration resolved per step (Baumgarte).</summary>
        public float BiasFactor;
        public float AllowedPenetration;
        /// <summary>Normal speed below which bounces are dropped (stops resting jitter).</summary>
        public float RestitutionThreshold;
        public float SleepLinear, SleepAngular, TimeToSleep;
        public bool AllowSleep;
        /// <summary>Start each step's solver from last step's impulses (fewer iterations for stable stacks).</summary>
        public bool WarmStarting;
        /// <summary>Total normal impulse on a manifold above which a <see cref="ContactEvent"/> is reported.</summary>
        public float EventImpulse;
        /// <summary>Upper bound for the speculative contact margin (anti tunnelling), in metres.</summary>
        public float MaxSpeculative;

        public static PhysicsSettings Default => new PhysicsSettings
        {
            Gravity = new float2(0f, -9.81f),
            VelocityIterations = 10,   // uniform stacks stand from 7; mixed stone/wood structures need ~10 to settle
            BiasFactor = 0.2f,
            AllowedPenetration = 0.01f,
            RestitutionThreshold = 1f,
            SleepLinear = 0.05f,
            SleepAngular = 0.05f,
            TimeToSleep = 0.5f,
            AllowSleep = true,
            WarmStarting = true,
            EventImpulse = 1f,
            MaxSpeculative = 2f,
        };
    }

    /// <summary>Per-step counters (written by the step job).</summary>
    public struct PhysicsStats
    {
        public int Bodies, AwakeBodies, Pairs, Manifolds, Events, Islands;
    }

    static class PhysicsMath
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Cross(float2 a, float2 b) => a.x * b.y - a.y * b.x;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float2 Cross(float w, float2 r) => new float2(-w * r.y, w * r.x);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float2x2 Rotation(float angle)
        {
            math.sincos(angle, out float s, out float c);
            return new float2x2(c, -s, s, c);   // columns (c, s) and (-s, c)
        }
    }
}
