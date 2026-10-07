using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using SPF.Contracts;

namespace SPF.Runtime.Scheduling
{
    /// <summary>Cold registration provenance, recorded by the composer, never inferred from a namespace.</summary>
    public readonly struct SystemRegistrationSource
    {
        readonly string m_ModuleId;
        readonly string m_ModuleType;
        public string ModuleId => m_ModuleId ?? "unknown";
        public string ModuleType => m_ModuleType ?? "unknown";
        public bool IsKnown => m_ModuleId != null && m_ModuleType != null;
        public SystemRegistrationSource(string moduleId, string moduleType)
        { m_ModuleId = moduleId; m_ModuleType = moduleType; }
    }

    public readonly struct ExecutionPlanKey
    {
        /// <summary>Process-local key identity; names, rather than IDs, are used in portable exports.</summary>
        public int Id { get; }
        public string Name { get; }
        internal ExecutionPlanKey(int id, string name) { Id = id; Name = name; }
    }

    /// <summary>Immutable metadata captured at construction, with no reference to the system or its mutable declaration.</summary>
    public sealed class ExecutionPlanSystem
    {
        public int ScheduleIndex { get; }
        public int RegistrationIndex { get; }
        public string SystemType { get; }
        public SystemRegistrationSource Source { get; }
        public SimPhase Phase { get; }
        public int Order { get; }
        public IReadOnlyList<ExecutionPlanKey> Reads { get; }
        public IReadOnlyList<ExecutionPlanKey> Writes { get; }
        public bool IsBarrier => Reads.Count == 0 && Writes.Count == 0;
        /// <summary>Declare does not expose Complete calls made inside system code.</summary>
        public string InternalCompletion => "unknown";

        internal ExecutionPlanSystem(int scheduleIndex, int registrationIndex, ISimSystem system,
            AccessDeclaration access, SystemRegistrationSource source)
        {
            ScheduleIndex = scheduleIndex; RegistrationIndex = registrationIndex;
            SystemType = system.GetType().FullName ?? system.GetType().Name;
            Source = source; Phase = system.Phase; Order = system.Order;
            Reads = CopyKeys(access.Reads, access); Writes = CopyKeys(access.Writes, access);
        }

        static ReadOnlyCollection<ExecutionPlanKey> CopyKeys(List<int> ids, AccessDeclaration access)
        {
            var keys = new ExecutionPlanKey[ids.Count];
            for (int i = 0; i < ids.Count; i++) keys[i] = new ExecutionPlanKey(ids[i], access.KeyNames[ids[i]]);
            return Array.AsReadOnly(keys);
        }
    }

    public enum ExecutionDependencyReason { WriteToRead, ReadToWrite, WriteToWrite, PreviousBarrier, BarrierAwaitsEarlierWork }

    /// <summary>A declared dependency input to the scheduler, not evidence of a nonempty native JobHandle or a measured wait.</summary>
    public readonly struct ExecutionPlanDependency
    {
        public int SourceIndex { get; }
        public int TargetIndex { get; }
        public ExecutionDependencyReason Reason { get; }
        /// <summary>-1 when the reason is a barrier rather than a data key.</summary>
        public int KeyId { get; }
        public string KeyName { get; }
        internal ExecutionPlanDependency(int source, int target, ExecutionDependencyReason reason, int keyId = -1, string keyName = null)
        { SourceIndex = source; TargetIndex = target; Reason = reason; KeyId = keyId; KeyName = keyName; }
    }

    public enum ExecutionCompletionReason { Barrier, SerialProfiling, EndTick, SchedulingFailureRecovery }

    public readonly struct ExecutionPlanCompletion
    {
        /// <summary>-1 means a pipeline-wide completion path.</summary>
        public int SystemIndex { get; }
        public ExecutionCompletionReason Reason { get; }
        public string Detail { get; }
        internal ExecutionPlanCompletion(int index, ExecutionCompletionReason reason, string detail)
        { SystemIndex = index; Reason = reason; Detail = detail; }
    }

    /// <summary>
    /// Explicitly requested cold diagnostic. Call order, declared Job dependency inputs and framework
    /// Complete call sites are separate. No topological reorder, implicit phase barrier, Declare,
    /// scheduling, resource access or synchronization occurs here. System-internal waits stay unknown.
    /// This construction-time snapshot does not validate unsupported post-Declare mutation.
    /// </summary>
    public sealed class ExecutionPlan
    {
        public IReadOnlyList<ExecutionPlanSystem> Systems { get; }
        public IReadOnlyList<ExecutionPlanDependency> Dependencies { get; }
        public IReadOnlyList<ExecutionPlanCompletion> Completions { get; }
        public bool SerialProfiling { get; }

        internal ExecutionPlan(ExecutionPlanSystem[] systems, bool serialProfiling)
        {
            Systems = Array.AsReadOnly(systems);
            SerialProfiling = serialProfiling;
            var edges = new List<ExecutionPlanDependency>();
            var completions = new List<ExecutionPlanCompletion>();
            var lastWrites = new Dictionary<int, int>();
            var readers = new Dictionary<int, List<int>>();
            int lastBarrier = -1;
            // Mirror DependencyTracker's symbolic inputs, including its intentionally retained
            // last-write/reader records across barriers. Do not simplify into a different scheduler.
            for (int i = 0; i < systems.Length; i++)
            {
                var system = systems[i];
                if (system.IsBarrier)
                {
                    for (int previous = 0; previous < i; previous++)
                        edges.Add(new ExecutionPlanDependency(previous, i, ExecutionDependencyReason.BarrierAwaitsEarlierWork));
                    completions.Add(new ExecutionPlanCompletion(i, ExecutionCompletionReason.Barrier,
                        "Before OnTick: Complete all previously recorded handles because the declaration is empty."));
                    lastBarrier = i;
                }
                else
                {
                    if (lastBarrier >= 0)
                        edges.Add(new ExecutionPlanDependency(lastBarrier, i, ExecutionDependencyReason.PreviousBarrier));
                    foreach (var key in system.Reads)
                        if (lastWrites.TryGetValue(key.Id, out int writer))
                            edges.Add(new ExecutionPlanDependency(writer, i, ExecutionDependencyReason.WriteToRead, key.Id, key.Name));
                    foreach (var key in system.Writes)
                    {
                        if (lastWrites.TryGetValue(key.Id, out int writer))
                            edges.Add(new ExecutionPlanDependency(writer, i, ExecutionDependencyReason.WriteToWrite, key.Id, key.Name));
                        if (readers.TryGetValue(key.Id, out var keyReaders))
                            foreach (int reader in keyReaders)
                                edges.Add(new ExecutionPlanDependency(reader, i, ExecutionDependencyReason.ReadToWrite, key.Id, key.Name));
                    }
                    foreach (var key in system.Reads)
                    {
                        if (!readers.TryGetValue(key.Id, out var keyReaders))
                            readers.Add(key.Id, keyReaders = new List<int>());
                        keyReaders.Add(i);
                    }
                    foreach (var key in system.Writes)
                    {
                        lastWrites[key.Id] = i;
                        if (readers.TryGetValue(key.Id, out var keyReaders)) keyReaders.Clear();
                    }
                }
                if (serialProfiling)
                    completions.Add(new ExecutionPlanCompletion(i, ExecutionCompletionReason.SerialProfiling,
                        "After OnTick: Complete the returned handle for diagnostic per-system timing; normal overlap is removed."));
            }
            completions.Add(new ExecutionPlanCompletion(-1, ExecutionCompletionReason.EndTick,
                "EndTick completes owned unrecorded work, if any, then all recorded work before resource Sync. Reset, snapshot and Dispose call EndTick; failed completion retains ownership for retry."));
            completions.Add(new ExecutionPlanCompletion(-1, ExecutionCompletionReason.SchedulingFailureRecovery,
                "On a scheduling exception, attempt the same owned-work completion path; a failed Complete retains pending handles for EndTick/Dispose retry. Private work never returned by OnTick remains the system's responsibility."));
            Dependencies = edges.AsReadOnly(); Completions = completions.AsReadOnly();
        }

        /// <summary>Deterministic text, formatted only on request; numeric key IDs are intentionally not exported.</summary>
        public string ToText()
        {
            var text = new StringBuilder();
            text.Append("SPF execution plan v1\nCall order: Phase -> Order -> registration. Phase changes do not Complete.\n");
            text.Append("Declared dependency inputs are not measured waits; empty/forwarded handles and system-internal Complete are not observed.\n");
            text.Append("SerialProfiling=").Append(SerialProfiling ? "true" : "false").Append('\n');
            foreach (var system in Systems)
            {
                text.Append("system ").Append(Number(system.ScheduleIndex)).Append(' ').Append(Quote(system.SystemType));
                text.Append(" source=").Append(system.Source.IsKnown ? Quote(system.Source.ModuleId) : "unknown");
                text.Append(" moduleType=").Append(Quote(system.Source.ModuleType));
                text.Append(" phase=").Append(system.Phase).Append(" order=").Append(Number(system.Order));
                text.Append(" registration=").Append(Number(system.RegistrationIndex));
                text.Append(" barrier=").Append(system.IsBarrier ? "true" : "false").Append(" internal Complete=unknown\n");
                foreach (var key in system.Reads) text.Append("  read ").Append(Quote(key.Name)).Append('\n');
                foreach (var key in system.Writes) text.Append("  write ").Append(Quote(key.Name)).Append('\n');
            }
            foreach (var edge in Dependencies)
            {
                text.Append("dependency ").Append(Number(edge.SourceIndex)).Append(" -> ").Append(Number(edge.TargetIndex));
                text.Append(' ').Append(edge.Reason);
                if (edge.KeyName != null) text.Append(" key=").Append(Quote(edge.KeyName));
                text.Append('\n');
            }
            foreach (var complete in Completions)
                text.Append("complete ").Append(complete.Reason).Append(" system=").Append(Number(complete.SystemIndex))
                    .Append(' ').Append(complete.Detail).Append('\n');
            return text.ToString();
        }

        /// <summary>Graphviz DOT: dashed gray call order, solid declared dependencies, orange Complete call sites.</summary>
        public string ToDot()
        {
            var text = new StringBuilder("digraph SPFExecutionPlan {\n  rankdir=LR;\n  label=\"Dashed: call order; solid: declared dependencies; orange: framework Complete. Internal Complete unknown.\";\n");
            foreach (var system in Systems)
            {
                var label = new StringBuilder().Append(Number(system.ScheduleIndex)).Append(": ").Append(system.SystemType)
                    .Append("\nsource=").Append(system.Source.ModuleId).Append(" (").Append(system.Source.ModuleType).Append(')')
                    .Append('\n').Append(system.Phase).Append('/').Append(Number(system.Order))
                    .Append(" reg=").Append(Number(system.RegistrationIndex));
                foreach (var key in system.Reads) label.Append("\nread ").Append(key.Name);
                foreach (var key in system.Writes) label.Append("\nwrite ").Append(key.Name);
                label.Append("\ninternal Complete=unknown");
                text.Append("  s").Append(Number(system.ScheduleIndex)).Append(" [label=").Append(Quote(label.ToString())).Append("];\n");
                if (system.ScheduleIndex > 0)
                    text.Append("  s").Append(Number(system.ScheduleIndex - 1)).Append(" -> s").Append(Number(system.ScheduleIndex))
                        .Append(" [style=dashed,color=gray,label=\"call order\"];\n");
            }
            foreach (var edge in Dependencies)
                text.Append("  s").Append(Number(edge.SourceIndex)).Append(" -> s").Append(Number(edge.TargetIndex)).Append(" [label=")
                    .Append(Quote(edge.Reason + (edge.KeyName == null ? "" : ": " + edge.KeyName))).Append("];\n");
            for (int i = 0; i < Completions.Count; i++)
            {
                var complete = Completions[i];
                text.Append("  c").Append(Number(i)).Append(" [shape=note,color=orange,label=")
                    .Append(Quote(complete.Reason + "\n" + complete.Detail)).Append("];\n");
                if (complete.SystemIndex >= 0)
                    text.Append("  c").Append(Number(i)).Append(" -> s").Append(Number(complete.SystemIndex))
                        .Append(" [style=dotted,color=orange,arrowhead=none];\n");
            }
            return text.Append("}\n").ToString();
        }

        static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
        static string Quote(string value) => "\"" + (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"")
            .Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t") + "\"";
    }
}
