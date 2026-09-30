using System;

namespace SPF.Contracts
{
    /// <summary>
    /// Stable reference to an entity. Index addresses a registry slot; Generation detects stale handles
    /// after the slot is recycled. The default value (generation 0) is never alive.
    /// </summary>
    public readonly struct EntityHandle : IEquatable<EntityHandle>
    {
        public readonly int Index;
        public readonly int Generation;

        public static EntityHandle Null => default;

        public EntityHandle(int index, int generation)
        {
            Index = index;
            Generation = generation;
        }

        public bool IsNull => Generation == 0;

        public bool Equals(EntityHandle other) => Index == other.Index && Generation == other.Generation;
        public override bool Equals(object obj) => obj is EntityHandle other && Equals(other);
        public override int GetHashCode() => (Index * 397) ^ Generation;
        public static bool operator ==(EntityHandle a, EntityHandle b) => a.Equals(b);
        public static bool operator !=(EntityHandle a, EntityHandle b) => !a.Equals(b);
        public override string ToString() => IsNull ? "Entity(null)" : $"Entity({Index}:{Generation})";
    }
}
