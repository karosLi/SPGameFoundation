using System;
using SPF.Contracts;

namespace SPF.Runtime.World
{
    /// <summary>
    /// Development check that systems only touch what they declared: while a system's OnTick runs, reading
    /// an undeclared column (or an undeclared <see cref="IJobData"/> resource) throws, because the dependency
    /// tracker cannot order or protect data it was not told about (a job may still be writing it). Barrier
    /// systems (no declarations) may touch anything: everything before them has completed. Compiled out of
    /// release players.
    /// </summary>
    public sealed class AccessGuard
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || SPF_DOTNET_HARNESS
        public static bool Enabled = true;
#else
        public static bool Enabled = false;
#endif
        bool[] m_Allowed;
        string m_System;

        /// <summary>Violations seen (when <see cref="Throw"/> is false, they are only counted).</summary>
        public int Violations { get; private set; }
        public string LastViolation { get; private set; }
        public bool Throw { get; set; } = true;

        internal void Begin(bool[] allowed, string system)
        {
            m_Allowed = Enabled ? allowed : null;
            m_System = system;
        }

        internal void End() => m_Allowed = null;

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

        void Report(string message)
        {
            Violations++;
            LastViolation = message;
            if (Throw) throw new InvalidOperationException(message);
        }
    }
}
