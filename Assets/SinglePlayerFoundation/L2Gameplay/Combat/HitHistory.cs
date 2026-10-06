using SPF.Contracts;
using Unity.Collections;

namespace SPF.L2.Combat
{
    public enum HitRecordResult : byte { Added, Duplicate, Full, InvalidTarget, InvalidScope }

    /// <summary>State of one bounded attack/pulse. Owner is world-local; Null is allowed for a
    /// caller-owned singleton ability. Pulse 0 is inactive. Persist this AND the active storage prefix.</summary>
    public struct HitHistoryState
    {
        public EntityHandle Owner;
        public uint Pulse;
        public int Count;
    }

    /// <summary>Allocation-free full-handle deduplication over caller-owned disjoint slices. One writer
    /// per scope; no atomics, eviction, growth or liveness lookup. Rows must be translated at query time.
    /// Full means reject damage. For queued damage: Check, successfully enqueue, then TryRecord, with no
    /// intervening writer. Never record a failed enqueue. Owner/pulse reuse requires Begin or Release.</summary>
    public static class HitHistory
    {
        public static bool IsValid(NativeArray<EntityHandle> storage, int offset, int capacity, in HitHistoryState state) =>
            storage.IsCreated && offset >= 0 && capacity >= 0 && offset <= storage.Length &&
            capacity <= storage.Length - offset && state.Count >= 0 && state.Count <= capacity;

        /// <summary>Idempotent for the same owner and pulse; a new scope clears the old prefix.
        /// False reports bad bounds or reserved pulse 0 without changing anything.</summary>
        public static bool Begin(NativeArray<EntityHandle> storage, int offset, int capacity,
            ref HitHistoryState state, EntityHandle owner, uint pulse)
        {
            if (pulse == 0 || !IsValid(storage, offset, capacity, state)) return false;
            if (state.Owner == owner && state.Pulse == pulse) return true;
            Release(storage, offset, capacity, ref state);
            state.Owner = owner;
            state.Pulse = pulse;
            return true;
        }

        public static bool Release(NativeArray<EntityHandle> storage, int offset, int capacity, ref HitHistoryState state)
        {
            if (!IsValid(storage, offset, capacity, state)) return false;
            for (int i = 0; i < state.Count; i++) storage[offset + i] = default;
            state = default;
            return true;
        }

        /// <summary>Added means eligible, without mutation. Duplicate takes precedence over Full.</summary>
        public static HitRecordResult Check(NativeArray<EntityHandle> storage, int offset, int capacity,
            in HitHistoryState state, EntityHandle target)
        {
            if (!IsValid(storage, offset, capacity, state) || state.Pulse == 0) return HitRecordResult.InvalidScope;
            if (target.Index < 0 || target.Generation <= 0) return HitRecordResult.InvalidTarget;
            for (int i = 0; i < state.Count; i++)
                if (storage[offset + i] == target) return HitRecordResult.Duplicate;
            return state.Count == capacity ? HitRecordResult.Full : HitRecordResult.Added;
        }

        public static HitRecordResult TryRecord(NativeArray<EntityHandle> storage, int offset, int capacity,
            ref HitHistoryState state, EntityHandle target)
        {
            var result = Check(storage, offset, capacity, state, target);
            if (result == HitRecordResult.Added) storage[offset + state.Count++] = target;
            return result;
        }
    }
}
