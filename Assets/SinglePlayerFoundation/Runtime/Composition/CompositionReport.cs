using System;
using System.Collections.Generic;

namespace SPF.Runtime.Composition
{
    public sealed class CompositionDiagnostic
    {
        public string ModuleId { get; }
        public string Key { get; }
        public string Message { get; }
        internal CompositionDiagnostic(string moduleId, string key, string message)
        { ModuleId = moduleId; Key = key; Message = message; }
        public override string ToString() => $"Module '{ModuleId}', key '{Key}': {Message}";
    }

    /// <summary>Declared ownership/capacity only. No resource has been accepted or allocated yet.</summary>
    public sealed class CompositionDataSummary
    {
        public string Key { get; }
        public string OwnerModuleId { get; }
        public int Capacity { get; }
        public bool IsTable { get; }
        internal CompositionDataSummary(string key, string owner, int capacity, bool table)
        { Key = key; OwnerModuleId = owner; Capacity = capacity; IsTable = table; }
    }

    public sealed class CompositionReport
    {
        public IReadOnlyList<ModuleManifest> Manifests { get; }
        public IReadOnlyList<CompositionDiagnostic> Errors { get; }
        public IReadOnlyList<CompositionDataSummary> Data { get; }
        public int LegacyModuleCount { get; }
        public bool IsValid => Errors.Count == 0;
        internal CompositionReport(List<ModuleManifest> manifests, List<CompositionDiagnostic> errors,
            List<CompositionDataSummary> data, int legacyCount)
        { Manifests = manifests.AsReadOnly(); Errors = errors.AsReadOnly(); Data = data.AsReadOnly(); LegacyModuleCount = legacyCount; }
        public void ThrowIfInvalid()
        {
            if (!IsValid) throw new ArgumentException("Composition preflight failed: " + string.Join("; ", Errors));
        }
    }
}
