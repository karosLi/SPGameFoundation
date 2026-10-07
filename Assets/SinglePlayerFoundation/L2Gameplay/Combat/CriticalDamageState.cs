using System;
using System.IO;
using SPF.Contracts;
using SPF.Runtime.World;
using Unity.Collections;
using Unity.Mathematics;

namespace SPF.L2.Combat
{
    /// <summary>Explicit deterministic authored rule, no RNG. Every Nth actually damaging outgoing
    /// accepted hit is critical. Zero interval disables critical damage but still emits normal facts.</summary>
    [Serializable]
    public struct CriticalDamageRule
    {
        public int EveryNthHit;
        public float Multiplier;
        public static CriticalDamageRule Default => new CriticalDamageRule { EveryNthHit = 4, Multiplier = 2 };
        public static CriticalDamageRule NormalOnly => new CriticalDamageRule { EveryNthHit = 0, Multiplier = 1 };
        public void Validate()
        {
            if (EveryNthHit < 0 || EveryNthHit > 1000000 || !math.isfinite(Multiplier) || Multiplier < 1 || Multiplier > 100)
                throw new ArgumentOutOfRangeException(nameof(EveryNthHit));
        }
        public void Write(BinaryWriter writer) { writer.Write(EveryNthHit); writer.Write(Multiplier); }
    }

    /// <summary>Optional authoritative critical cadence, separate from disposable presentation facts.
    /// Phase is snapshotted; overflow of diagnostic hit count never changes the cadence.</summary>
    public sealed class CriticalDamageState : IDisposable, IJobData, IResettableResource, ISnapshotResource
    {
        public static readonly ResourceKey<CriticalDamageState> Key = new ResourceKey<CriticalDamageState>("Combat.CriticalDamage.V1");
        const int Magic = 0x43525431;
        internal struct State { public int Phase; public ulong Accepted; }
        NativeArray<State> m_State;
        public CriticalDamageRule Rule { get; }
        public ulong AcceptedHits => m_State[0].Accepted;
        public int Phase => m_State[0].Phase;
        public CriticalDamageState(CriticalDamageRule rule) { rule.Validate(); Rule = rule; m_State = new NativeArray<State>(1, Allocator.Persistent); }
        public Writer AsWriter() => new Writer(m_State, Rule);
        public float Apply(float requested, float hp, out bool critical) => AsWriter().Apply(requested, hp, out critical);
        public void OnReset() => m_State[0] = default;
        public void WriteSnapshot(BinaryWriter writer)
        { writer.Write(Magic); writer.Write(1); Rule.Write(writer); writer.Write(Phase); writer.Write(AcceptedHits); }
        public void ReadSnapshot(BinaryReader reader)
        {
            if (reader.ReadInt32() != Magic || reader.ReadInt32() != 1 || reader.ReadInt32() != Rule.EveryNthHit || reader.ReadSingle() != Rule.Multiplier)
                throw new InvalidDataException("Critical damage rule/schema differs.");
            var value = new State { Phase = reader.ReadInt32(), Accepted = reader.ReadUInt64() };
            if (value.Phase < 0 || value.Phase >= math.max(1, Rule.EveryNthHit)) throw new InvalidDataException("Invalid critical damage cadence.");
            m_State[0] = value;
        }
        public void Dispose() { if (m_State.IsCreated) m_State.Dispose(); }
        public struct Writer
        {
            NativeArray<State> m_State;
            CriticalDamageRule m_Rule;
            internal Writer(NativeArray<State> state, CriticalDamageRule rule) { m_State = state; m_Rule = rule; }
            public float Apply(float requested, float hp, out bool critical)
            {
                critical = false;
                if (!math.isfinite(requested) || !math.isfinite(hp) || requested <= 0 || hp <= 0) return 0;
                var state = m_State[0];
                bool nextCritical = m_Rule.EveryNthHit > 0 && state.Phase + 1 == m_Rule.EveryNthHit;
                double candidate = (double)requested * (nextCritical ? m_Rule.Multiplier : 1f);
                float bounded = candidate >= hp ? hp : (float)candidate;
                float applied = hp - math.max(0, hp - bounded);
                if (applied <= 0) return 0;
                if (m_Rule.EveryNthHit > 0) state.Phase = nextCritical ? 0 : state.Phase + 1;
                if (state.Accepted < ulong.MaxValue) state.Accepted++;
                m_State[0] = state; critical = nextCritical; return applied;
            }
        }
    }
}
