using Unity.Mathematics;

namespace SPF.Contracts.Combat
{
    /// <summary>Settled, positive health loss, never a request. Position is the accepted world-space
    /// presentation anchor; Target is the real registry handle, including generation. Consumers must
    /// not look up a newer row/position. Sequence belongs to one journal revision, not a save.</summary>
    public struct AppliedDamageFact
    {
        public EntityHandle Target;
        public float2 Position;
        public float Amount;
        public bool Critical;
        public long Tick;
        public ulong Sequence;
    }

    /// <summary>Independent, allocation-free journal reader. A revision change invalidates its history.</summary>
    public struct DamageFactCursor
    {
        public long JournalId;
        public uint Revision;
        public ulong NormalSequence, CriticalSequence;
        public ulong Sequence;
        public ulong Missed;
    }
}
