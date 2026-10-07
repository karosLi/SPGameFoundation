using System;
using System.IO;
using SPF.Contracts;
using SPF.Contracts.Combat;
using Unity.Collections;
using Unity.Mathematics;

namespace SPF.Runtime.World
{
    /// <summary>Optional fixed ring of settled facts. Single ordered simulation writer; any number of
    /// independent read-only consumers after Sync. Fixed normal/critical lanes overwrite their own oldest facts, never damage. Normal
    /// pressure cannot evict a critical fact. Readers merge retained lanes in global sequence order.
    /// Output is discarded on reset/restore (including same-tick restore); no events enter snapshots.
    /// Binding starts at the tail by default so disabled/rebound views cannot replay old feedback.</summary>
    public sealed class AppliedDamageJournal : IDisposable, IJobData, IResettableResource, ISnapshotResource
    {
        public static readonly ResourceKey<AppliedDamageJournal> Key = new ResourceKey<AppliedDamageJournal>("Combat.AppliedDamageJournal.V1");
        public const int DefaultCapacity = 512;
        internal struct State { public ulong Sequence, NormalWritten, CriticalWritten; public int NormalCount, CriticalCount, HighWater; }
        static long s_NextId;
        readonly long m_Id = System.Threading.Interlocked.Increment(ref s_NextId);
        NativeArray<AppliedDamageFact> m_Facts;
        NativeArray<State> m_State;
        public int Capacity => m_Facts.Length;
        public int NormalCapacity => Capacity - CriticalCapacity;
        public int CriticalCapacity => math.max(1, Capacity / 4);
        public int Count => m_State[0].NormalCount + m_State[0].CriticalCount;
        public int HighWater => m_State[0].HighWater;
        public ulong AcceptedNormal => m_State[0].NormalWritten;
        public ulong AcceptedCritical => m_State[0].CriticalWritten;
        public ulong OverwrittenNormal => AcceptedNormal - (ulong)m_State[0].NormalCount;
        public ulong OverwrittenCritical => AcceptedCritical - (ulong)m_State[0].CriticalCount;
        public ulong LatestSequence => m_State[0].Sequence;
        public ulong Overwritten => OverwrittenNormal + OverwrittenCritical;
        public uint Revision { get; private set; }
        public AppliedDamageJournal(int capacity = DefaultCapacity)
        {
            if (capacity < 2 || capacity > 65536) throw new ArgumentOutOfRangeException(nameof(capacity));
            m_Facts = new NativeArray<AppliedDamageFact>(capacity, Allocator.Persistent);
            try { m_State = new NativeArray<State>(1, Allocator.Persistent); }
            catch { m_Facts.Dispose(); throw; }
            OnReset();
        }
        public Writer AsWriter() => new Writer(m_Facts, m_State, NormalCapacity);
        public DamageFactCursor CreateCursor(bool includeBuffered = false)
        {
            var state = m_State[0];
            return new DamageFactCursor { JournalId = m_Id, Revision = Revision,
                Sequence = includeBuffered ? 0 : state.Sequence,
                NormalSequence = includeBuffered ? state.NormalWritten - (ulong)state.NormalCount : state.NormalWritten,
                CriticalSequence = includeBuffered ? state.CriticalWritten - (ulong)state.CriticalCount : state.CriticalWritten };
        }
        public bool TryRead(ref DamageFactCursor cursor, out AppliedDamageFact fact)
        {
            fact = default;
            if (cursor.JournalId != m_Id || cursor.Revision != Revision) { cursor = CreateCursor(); return false; }
            var state = m_State[0];
            CatchUp(ref cursor.NormalSequence, state.NormalWritten - (ulong)state.NormalCount, ref cursor.Missed);
            CatchUp(ref cursor.CriticalSequence, state.CriticalWritten - (ulong)state.CriticalCount, ref cursor.Missed);
            bool normal = cursor.NormalSequence < state.NormalWritten, critical = cursor.CriticalSequence < state.CriticalWritten;
            if (!normal && !critical) return false;
            var n = normal ? m_Facts[(int)(cursor.NormalSequence % (ulong)NormalCapacity)] : default;
            var c = critical ? m_Facts[NormalCapacity + (int)(cursor.CriticalSequence % (ulong)CriticalCapacity)] : default;
            if (normal && (!critical || n.Sequence < c.Sequence)) { fact = n; cursor.NormalSequence++; }
            else { fact = c; cursor.CriticalSequence++; }
            cursor.Sequence = fact.Sequence;
            return true;
        }
        static void CatchUp(ref ulong position, ulong earliest, ref ulong missed)
        {
            if (position >= earliest) return;
            ulong lost = earliest - position;
            missed = lost > ulong.MaxValue - missed ? ulong.MaxValue : missed + lost;
            position = earliest;
        }
        public void OnReset()
        {
            m_State[0] = default;
            unchecked { Revision++; if (Revision == 0) Revision = 1; }
        }
        public void WriteSnapshot(BinaryWriter writer) { }
        public void ReadSnapshot(BinaryReader reader) => OnReset();
        public void Dispose() { if (m_Facts.IsCreated) m_Facts.Dispose(); if (m_State.IsCreated) m_State.Dispose(); }

        /// <summary>Burst-compatible single-writer view. Publishing does not validate liveness after
        /// settlement: a lethal hit still needs its original handle after destroy is queued.</summary>
        public struct Writer
        {
            NativeArray<AppliedDamageFact> m_Facts;
            NativeArray<State> m_State;
            int m_NormalCapacity;
            internal Writer(NativeArray<AppliedDamageFact> facts, NativeArray<State> state, int normalCapacity) { m_Facts = facts; m_State = state; m_NormalCapacity = normalCapacity; }
            public bool Publish(EntityHandle target, float2 position, float amount, bool critical, long tick)
            {
                if (target.Index < 0 || target.Generation == 0 || !math.all(math.isfinite(position)) || !math.isfinite(amount) || amount <= 0 || tick < 0) return false;
                var state = m_State[0];
                // Sequence exhaustion cannot wrap into a duplicate event. It is unreachable in normal
                // play, but explicit rejection is safer than changing the gameplay or reader epoch.
                if (state.Sequence == ulong.MaxValue) return false;
                state.Sequence++;
                int index;
                if (critical)
                {
                    int capacity = m_Facts.Length - m_NormalCapacity;
                    index = m_NormalCapacity + (int)(state.CriticalWritten % (ulong)capacity);
                    state.CriticalWritten++;
                    state.CriticalCount = math.min(capacity, state.CriticalCount + 1);
                }
                else
                {
                    index = (int)(state.NormalWritten % (ulong)m_NormalCapacity);
                    state.NormalWritten++;
                    state.NormalCount = math.min(m_NormalCapacity, state.NormalCount + 1);
                }
                state.HighWater = math.max(state.HighWater, state.NormalCount + state.CriticalCount);
                m_Facts[index] = new AppliedDamageFact
                { Target = target, Position = position, Amount = amount, Critical = critical, Tick = tick, Sequence = state.Sequence };
                m_State[0] = state;
                return true;
            }
        }
    }
}
