using System;
using System.Collections.Generic;
using SPF.Contracts;

namespace SPF.Runtime.Composition
{
    /// <summary>Optional cold metadata. Return null for a legacy composition. DescribeComposition must
    /// not allocate native resources or mutate module state; this is not a runtime service locator.</summary>
    public interface ICompositionManifestProvider
    {
        ModuleManifest DescribeComposition();
    }

    public readonly struct ModuleCapability
    {
        public string Id { get; }
        public int Version { get; }
        public ModuleCapability(string id, int version) { Id = id; Version = version; }
    }

    /// <summary>Only a hook requirement, not a claim of complete/portable saves or content compatibility.</summary>
    public enum ResourceSaveRequirement { Unspecified, SnapshotHook }

    /// <summary>Immutable description of an intended table/resource registration. Does not own an object.
    /// Resource ownership still transfers only on successful WorldLayout.Resource, then to SimWorld.</summary>
    public sealed class ModuleDataDeclaration
    {
        public AccessKey Key { get; }
        public int SchemaVersion { get; }
        public bool IsTable { get; }
        public bool IsOwner { get; }
        public bool LevelScoped { get; }
        public bool Pooled { get; }
        public int Capacity { get; }
        public int MaxCapacity { get; }
        public string CapacitySource { get; }
        public ResourceSaveRequirement SaveRequirement { get; }
        internal Type ResourceType { get; }

        ModuleDataDeclaration(AccessKey key, int schemaVersion, bool table, bool owner,
            bool levelScoped, bool pooled, int capacity, string capacitySource,
            Type resourceType, ResourceSaveRequirement saveRequirement, int maxCapacity = 0)
        {
            Key = key; SchemaVersion = schemaVersion; IsTable = table; IsOwner = owner;
            LevelScoped = levelScoped; Pooled = pooled; Capacity = capacity;
            CapacitySource = capacitySource; ResourceType = resourceType; SaveRequirement = saveRequirement; MaxCapacity = maxCapacity;
        }

        /// <summary>Table capacity requests merge by maximum, as in WorldLayout. An extension must
        /// agree with its declared owner on schema, scope and pooled storage. maxCapacity zero leaves the
        /// legacy maximum merge unbounded; a positive value rejects a larger declared merge.</summary>
        public static ModuleDataDeclaration Table(TableKey key, int schemaVersion, int capacity,
            string capacitySource, bool owner = true, bool levelScoped = false, bool pooled = false, int maxCapacity = 0) =>
            new ModuleDataDeclaration(key, schemaVersion, true, owner, levelScoped, pooled,
                capacity, capacitySource, null, ResourceSaveRequirement.Unspecified, maxCapacity);

        /// <summary>Capacity is an optional documented primary bound, not a byte budget or an allocator.
        /// Zero means no capacity claimed. A resource registration always has one declared module owner.</summary>
        public static ModuleDataDeclaration Resource<T>(ResourceKey<T> key, int schemaVersion,
            bool levelScoped = false, ResourceSaveRequirement save = ResourceSaveRequirement.Unspecified,
            int capacity = 0, string capacitySource = null) where T : class =>
            new ModuleDataDeclaration(key, schemaVersion, false, true, levelScoped, false,
                capacity, capacitySource, typeof(T), save);
    }

    /// <summary>Immutable, opt-in and deliberately partial. StableId/SchemaVersion describe this
    /// manifest, not raw snapshot bytes. Capabilities use exact versions; module order is never changed.</summary>
    public sealed class ModuleManifest
    {
        public string StableId { get; }
        public int SchemaVersion { get; }
        public IReadOnlyList<ModuleCapability> Provides { get; }
        public IReadOnlyList<ModuleCapability> Requires { get; }
        public IReadOnlyList<ModuleDataDeclaration> Data { get; }

        public ModuleManifest(string stableId, int schemaVersion,
            ModuleCapability[] provides = null, ModuleCapability[] requires = null,
            ModuleDataDeclaration[] data = null)
        {
            StableId = stableId; SchemaVersion = schemaVersion;
            Provides = Freeze(provides); Requires = Freeze(requires); Data = Freeze(data);
        }

        static IReadOnlyList<T> Freeze<T>(T[] source) =>
            Array.AsReadOnly(source == null ? Array.Empty<T>() : (T[])source.Clone());
    }
}
