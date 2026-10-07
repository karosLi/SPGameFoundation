using System;
using Unity.Collections;
using Unity.Mathematics;

namespace SPF.Contracts.Combat
{
    public enum CombatShapeKind : byte { Circle, Capsule, Box, Line, Point }
    public enum CombatShapeRole : byte { Body, Hurt, Attack, Motion, Accepted, Rejected }
    public enum CombatContactReason : byte { Accepted, Self, Friendly, Dead, Invulnerable, GroundMiss, HeightMiss, Duplicate, HistoryFull, QueueFull, Candidate, InvalidTarget, InvalidScope }
    public enum CombatBoundaryRule : byte { Strict, Closed }
    public enum CombatDamageOutcome : byte { None, Queued, Applied }

    /// <summary>Read-only presentation primitive, already projected by its host. Never drives damage.</summary>
    public struct CombatDebugShape
    {
        public CombatShapeKind Kind;
        public CombatShapeRole Role;
        public float2 A, B, Scale;
        public float Radius;
    }
    public struct CombatContactTrace
    {
        public EntityHandle Owner, Target;
        public long Tick;
        public int Scope;
        public CombatContactReason Reason;
        public CombatDamageOutcome DamageOutcome;
        public float2 From, To, Contact;
        public float Height, Radius, Fraction;
    }

    /// <summary>Optional unsaved diagnostics: fixed capacity, reject newest on overflow, no hot allocation.
    /// Disabled by default. Toggle during setup; tracing never changes whether gameplay accepts a hit.</summary>
    public sealed class CombatTraceBuffer : IDisposable
    {
        NativeArray<CombatContactTrace> m_Entries;
        public NativeArray<CombatContactTrace> Entries => m_Entries;
        public bool Enabled;
        public int Count { get; private set; }
        public int Dropped { get; private set; }
        public long Tick { get; private set; }
        public float MaxTargetMovement { get; set; }
        public int MovementRowsExamined { get; set; }
        public CombatContactTrace LastAccepted { get; private set; }
        public bool HasAccepted { get; private set; }
        public CombatTraceBuffer(int capacity = 256)
        {
            if (capacity < 1 || capacity > 4096) throw new ArgumentOutOfRangeException(nameof(capacity));
            m_Entries = new NativeArray<CombatContactTrace>(capacity, Allocator.Persistent);
        }
        public void Begin(long tick)
        {
            if (!Enabled || tick == Tick) return;
            Tick = tick; Count = Dropped = MovementRowsExamined = 0; MaxTargetMovement = 0;
        }
        public void Record(CombatContactTrace trace)
        {
            if (!Enabled) return;
            trace.Tick = Tick;
            if (trace.Reason == CombatContactReason.Accepted && trace.DamageOutcome != CombatDamageOutcome.None) { LastAccepted = trace; HasAccepted = true; }
            if (Count >= Entries.Length) { if (Dropped < int.MaxValue) Dropped++; return; }
            m_Entries[Count++] = trace;
        }
        public void Clear() { Count = Dropped = MovementRowsExamined = 0; Tick = -1; MaxTargetMovement = 0; HasAccepted = false; LastAccepted = default; }
        public void Dispose() { if (m_Entries.IsCreated) m_Entries.Dispose(); }
    }
}
