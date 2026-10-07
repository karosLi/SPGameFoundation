using System;
using System.Collections.Generic;
using SPF.Contracts;

namespace SPF.Runtime.Composition
{
    /// <summary>Cold validation of optional declarations, before legacy DeclareData can acquire resources.
    /// Does not inspect or allocate a World, reorder modules/systems, or certify undeclared configuration.</summary>
    public static class CompositionPreflight
    {
        internal static bool HasProvider(IReadOnlyList<IGameplayModule> modules)
        {
            for (int i = 0; i < modules.Count; i++)
                if (modules[i] is ICompositionManifestProvider) return true;
            return false;
        }

        public static CompositionReport Validate(IReadOnlyList<IGameplayModule> modules, SessionSettings settings)
        {
            settings.Validate();
            if (modules == null) throw new ArgumentNullException(nameof(modules));
            var manifests = new List<ModuleManifest>();
            var errors = new List<CompositionDiagnostic>();
            var summaries = new List<CompositionDataSummary>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            int legacyCount = 0;
            // Check every slot before invoking even a metadata callback.
            foreach (var module in modules)
            {
                if (module == null) throw new ArgumentException("Mode contains an empty module slot.");
                if (!ids.Add(module.Id)) throw new ArgumentException($"Module '{module.Id}' is listed twice.");
            }
            foreach (var module in modules)
            {
                var manifest = (module as ICompositionManifestProvider)?.DescribeComposition();
                if (manifest == null) { legacyCount++; continue; }
                manifests.Add(manifest);
            }
            ids.Clear();
            for (int i = 0; i < manifests.Count; i++)
            {
                var manifest = manifests[i];
                if (string.IsNullOrWhiteSpace(manifest.StableId) || !ids.Add(manifest.StableId))
                    Error(errors, manifest, "module", "stable module ID must be nonempty and unique.");
                if (manifest.SchemaVersion <= 0) Error(errors, manifest, "module", "manifest schema must be positive.");
            }
            ValidateCapabilities(manifests, errors);
            ValidateData(manifests, errors, summaries);
            return new CompositionReport(manifests, errors, summaries, legacyCount);
        }

        static void ValidateCapabilities(List<ModuleManifest> manifests, List<CompositionDiagnostic> errors)
        {
            var providers = new Dictionary<string, (int module, int version)>(StringComparer.Ordinal);
            for (int i = 0; i < manifests.Count; i++)
                foreach (var capability in manifests[i].Provides)
                {
                    if (!ValidCapability(capability))
                    { Error(errors, manifests[i], capability.Id, "capability ID/version must be nonempty/positive."); continue; }
                    if (providers.ContainsKey(capability.Id))
                        Error(errors, manifests[i], capability.Id, "duplicate capability provider.");
                    else providers.Add(capability.Id, (i, capability.Version));
                }
            // Edges only express declared module dependencies. Self-provided requirements add no edge.
            // This topological check does not produce an installation order or alter existing order.
            var edges = new bool[manifests.Count, manifests.Count];
            var incoming = new int[manifests.Count];
            for (int i = 0; i < manifests.Count; i++)
            {
                var requirements = new HashSet<string>(StringComparer.Ordinal);
                foreach (var requirement in manifests[i].Requires)
                {
                    if (!ValidCapability(requirement))
                    { Error(errors, manifests[i], requirement.Id, "capability ID/version must be nonempty/positive."); continue; }
                    if (!requirements.Add(requirement.Id))
                        Error(errors, manifests[i], requirement.Id, "duplicate capability requirement.");
                    if (!providers.TryGetValue(requirement.Id, out var provider))
                        Error(errors, manifests[i], requirement.Id, "missing capability provider (legacy modules make no claims).");
                    else
                    {
                        if (provider.version != requirement.Version)
                            Error(errors, manifests[i], requirement.Id, $"capability version {provider.version} does not match required version {requirement.Version}.");
                        if (provider.module != i && !edges[provider.module, i])
                        { edges[provider.module, i] = true; incoming[i]++; }
                    }
                }
            }
            var ready = new Queue<int>();
            for (int i = 0; i < incoming.Length; i++) if (incoming[i] == 0) ready.Enqueue(i);
            while (ready.Count != 0)
            {
                int provider = ready.Dequeue();
                for (int i = 0; i < incoming.Length; i++)
                    if (edges[provider, i] && --incoming[i] == 0) ready.Enqueue(i);
            }
            for (int i = 0; i < incoming.Length; i++)
                if (incoming[i] != 0) Error(errors, manifests[i], "requires", "dependency cycle blocks this module.");
        }

        sealed class DataGroup
        {
            internal readonly ModuleDataDeclaration First;
            internal readonly List<(ModuleManifest module, ModuleDataDeclaration data)> Entries =
                new List<(ModuleManifest, ModuleDataDeclaration)>();
            internal DataGroup(ModuleDataDeclaration first) { First = first; }
        }

        static void ValidateData(List<ModuleManifest> manifests, List<CompositionDiagnostic> errors,
            List<CompositionDataSummary> summaries)
        {
            var groups = new List<DataGroup>();
            var byName = new Dictionary<string, DataGroup>(StringComparer.Ordinal);
            foreach (var module in manifests)
            {
                var keys = new HashSet<int>();
                foreach (var data in module.Data)
                {
                    if (data == null) { Error(errors, module, "data", "null data declaration."); continue; }
                    if (data.Key == null || string.IsNullOrWhiteSpace(data.Key.Name))
                    { Error(errors, module, "data", "declaration key must have a nonempty name."); continue; }
                    if (!keys.Add(data.Key.Id)) Error(errors, module, data.Key.Name, "duplicate data declaration within module.");
                    if (data.SchemaVersion <= 0) Error(errors, module, data.Key.Name, "data schema must be positive.");
                    if (data.Capacity < 0 || (data.IsTable && data.Capacity == 0) || data.MaxCapacity < 0)
                        Error(errors, module, data.Key.Name, "invalid capacity declaration.");
                    if (data.Capacity > 0 && string.IsNullOrWhiteSpace(data.CapacitySource))
                        Error(errors, module, data.Key.Name, "capacity source must be explicit.");
                    if (data.SaveRequirement != ResourceSaveRequirement.Unspecified && data.SaveRequirement != ResourceSaveRequirement.SnapshotHook)
                        Error(errors, module, data.Key.Name, "unknown save requirement.");
                    if (!data.IsTable)
                    {
                        // Closed generic key type metadata only; no discovery, object construction or lookup.
                        if (data.LevelScoped && !typeof(IResettableResource).IsAssignableFrom(data.ResourceType))
                            Error(errors, module, data.Key.Name, "level scope requires IResettableResource on the declared resource type.");
                        if (data.SaveRequirement == ResourceSaveRequirement.SnapshotHook && !typeof(ISnapshotResource).IsAssignableFrom(data.ResourceType))
                            Error(errors, module, data.Key.Name, "save claim requires ISnapshotResource on the declared resource type.");
                    }
                    if (!byName.TryGetValue(data.Key.Name, out var group))
                    {
                        group = new DataGroup(data); byName.Add(data.Key.Name, group); groups.Add(group);
                    }
                    group.Entries.Add((module, data));
                }
            }
            foreach (var group in groups)
            {
                var first = group.First;
                string owner = null;
                int ownerCount = 0, capacity = 0;
                foreach (var entry in group.Entries)
                {
                    var data = entry.data;
                    if (!ReferenceEquals(first.Key, data.Key))
                        Error(errors, entry.module, data.Key.Name, "same name has different runtime key identity.");
                    if (first.SchemaVersion != data.SchemaVersion || first.IsTable != data.IsTable)
                        Error(errors, entry.module, data.Key.Name, "conflicting data schema or storage kind.");
                    if (first.LevelScoped != data.LevelScoped || first.Pooled != data.Pooled)
                        Error(errors, entry.module, data.Key.Name, "conflicting scope or pooled storage declaration.");
                    if (data.IsOwner) { ownerCount++; owner = entry.module.StableId; }
                    capacity = Math.Max(capacity, data.Capacity);
                }
                if (!first.IsTable && group.Entries.Count > 1)
                    Error(errors, group.Entries[0].module, first.Key.Name, "duplicate resource registration; accepted registration is the sole owner.");
                if (ownerCount != 1)
                    Error(errors, group.Entries[0].module, first.Key.Name, "declaration requires exactly one module owner.");
                foreach (var entry in group.Entries)
                    if (entry.data.MaxCapacity > 0 && capacity > entry.data.MaxCapacity)
                        Error(errors, entry.module, first.Key.Name, $"merged capacity {capacity} exceeds declared capacity budget {entry.data.MaxCapacity}.");
                summaries.Add(new CompositionDataSummary(first.Key.Name, owner, capacity, first.IsTable));
            }
        }

        static bool ValidCapability(ModuleCapability capability) =>
            !string.IsNullOrWhiteSpace(capability.Id) && capability.Version > 0;
        static void Error(List<CompositionDiagnostic> errors, ModuleManifest module, string key, string message) =>
            errors.Add(new CompositionDiagnostic(module.StableId, key, message));
    }
}
