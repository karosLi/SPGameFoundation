using System;
using SPF.Contracts;
using Unity.Jobs;

namespace SPF.Runtime.World
{
    /// <summary>
    /// Per-world development checks for declared access and pipeline-owned work. ReadColumn returns a
    /// read-only view; WriteColumn requires Write (which also permits reads). Legacy writable arrays,
    /// cached aliases, raw registry/resource references and private unreturned jobs remain escape hatches.
    /// This is not a replacement for native Jobs safety. Enabled by default only in development builds.
    /// </summary>
    public sealed class AccessGuard
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || SPF_DOTNET_HARNESS
        public static bool Enabled = true;
#else
        public static bool Enabled = false;
#endif
        bool[] m_Allowed;
        bool[] m_Writable;
        int m_OutstandingReturns;
        object m_ActivePipeline;
        string m_System;

        /// <summary>Violations seen. Access violations can be count-only; structural violations always throw when enabled.</summary>
        public int Violations { get; private set; }
        public string LastViolation { get; private set; }
        public bool Throw { get; set; } = true;

        // Public constructors may share a World, but only one pipeline may schedule or hold a
        // pending tick at once. This ownership rule is unconditional, not a development diagnostic.
        internal void ClaimPipeline(object pipeline)
        {
            if (m_ActivePipeline != null)
                throw new InvalidOperationException("This World already has an active or pending pipeline. Complete its tick before beginning another pipeline.");
            m_ActivePipeline = pipeline;
        }

        internal void ReleasePipeline(object pipeline)
        {
            if (ReferenceEquals(m_ActivePipeline, pipeline)) m_ActivePipeline = null;
        }

        internal void Begin(bool[] allowed, bool[] writable, string system)
        {
            m_Allowed = Enabled ? allowed : null;
            m_Writable = Enabled ? writable : null;
            m_System = system;
        }

        internal void End() { m_Allowed = null; m_Writable = null; }

        // Track ownership even while checks are disabled so toggling diagnostics cannot invent a safe
        // window. A non-default returned handle remains borrowed until a successful explicit Complete;
        // IsCompleted alone is not sufficient to return NativeContainer ownership to the main thread.
        internal bool RecordReturnedWork(JobHandle handle, JobHandle dependency = default)
        {
            // Returning the incoming dependency unchanged introduces no new work. The tracker can
            // still hold old non-default handles after a barrier completed them; do not reborrow those.
            if (handle.Equals(default(JobHandle)) || handle.Equals(dependency)) return false;
            m_OutstandingReturns++;
            return true;
        }

        internal void CompleteReturnedWork(JobHandle original)
        {
            if (!original.Equals(default(JobHandle))) m_OutstandingReturns--;
        }

        internal void CompleteAllWork() => m_OutstandingReturns = 0;

        internal void CheckStructural(string operation)
        {
            if (!Enabled || m_OutstandingReturns == 0) return;
            Report($"{operation} cannot mutate world structure while pipeline-owned jobs are outstanding. " +
                "Use a completed barrier or Sync before structural changes; a phase label is not a safety window.", true);
        }

        internal void CheckWriteColumn(AccessKey column)
        {
            var writable = m_Writable;
            if (writable == null || (column.Id < writable.Length && writable[column.Id])) return;
            Report($"{m_System} requested Write access to column {column.Name} without declaring access.Write (Read is insufficient).");
        }

        internal void CheckColumn(AccessKey column)
        {
            var allowed = m_Allowed;
            if (allowed == null || (column.Id < allowed.Length && allowed[column.Id])) return;
            Report($"{m_System} accessed column {column.Name} without declaring it (Declare: access.Read/Write).");
        }

        internal void CheckResource(AccessKey key, object resource)
        {
            var allowed = m_Allowed;
            if (allowed == null || !(resource is IJobData) || (key.Id < allowed.Length && allowed[key.Id])) return;
            Report($"{m_System} accessed job data {key.Name} without declaring it (Declare: access.Read/Write).");
        }

        void Report(string message, bool structural = false)
        {
            Violations++;
            LastViolation = message;
            if (Throw || structural) throw new InvalidOperationException(message);
        }
    }
}
