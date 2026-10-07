using System;

namespace SPF.Editor
{
    /// <summary>Explicit, rollback-capable setup. Tests use a fake store, never EditorPrefs.</summary>
    public static class CiBurstPreferenceTransaction
    {
        public const string ApprovalFlag = "--spf-enable-approved-ci-burst-20261007";
        public static readonly string[] PreferenceKeys =
        {
            "BurstCompilation", "BurstCompileSynchronously", "BurstShowTimings",
            "BurstDebug", "BurstForceSafetyChecks"
        };

        public readonly struct Value
        {
            public readonly bool Exists, Enabled;
            public Value(bool exists, bool enabled) { Exists = exists; Enabled = enabled; }
            public bool Matches(Value other) => Exists == other.Exists && (!Exists || Enabled == other.Enabled);
            public override string ToString() => $"exists={Exists}, value={Enabled}";
        }

        public interface IStore
        {
            Value Read(string key);
            void Write(string key, Value value);
            Value SafetySession { get; set; }
            bool CompilationEnabled { get; set; }
            bool BackendEnabled { get; }
        }

        public static void Enable(bool batchMode, string[] arguments, IStore store)
        {
            // Reject even a read from the store until both explicit guards pass.
            if (!batchMode || Array.IndexOf(arguments, ApprovalFlag) < 0)
                throw new InvalidOperationException("Burst setup requires batch mode and the exact approval flag.");

            var before = new Value[PreferenceKeys.Length];
            for (int i = 0; i < before.Length; i++) before[i] = store.Read(PreferenceKeys[i]);
            Value safety = store.SafetySession;
            bool compilation = store.CompilationEnabled;
            try
            {
                store.Write(PreferenceKeys[0], new Value(true, true));
                if (!store.CompilationEnabled) store.CompilationEnabled = true;
                // Burst 1.8 saves every option whenever one global option changes.
                for (int i = 1; i < before.Length; i++) store.Write(PreferenceKeys[i], before[i]);
                store.SafetySession = safety;
                if (!store.Read(PreferenceKeys[0]).Matches(new Value(true, true)) ||
                    !store.CompilationEnabled || !store.BackendEnabled)
                    throw new InvalidOperationException("Burst activation did not enable the compiler and job backend.");
                for (int i = 1; i < before.Length; i++)
                    if (!store.Read(PreferenceKeys[i]).Matches(before[i]))
                        throw new InvalidOperationException("Burst setup changed another preference: " + PreferenceKeys[i]);
                if (!store.SafetySession.Matches(safety))
                    throw new InvalidOperationException("Burst setup changed session safety checks.");
            }
            catch (Exception failure)
            {
                // The public setter may itself have written prefs before throwing. Restore the
                // option first, then every persisted/session key after its save callback finishes.
                Exception rollbackFailure = null;
                try { store.CompilationEnabled = compilation; }
                catch (Exception e) { rollbackFailure = e; }
                for (int i = 0; i < before.Length; i++)
                    try { store.Write(PreferenceKeys[i], before[i]); }
                    catch (Exception e) { rollbackFailure = rollbackFailure ?? e; }
                try { store.SafetySession = safety; }
                catch (Exception e) { rollbackFailure = rollbackFailure ?? e; }
                if (rollbackFailure != null)
                    throw new AggregateException("Burst activation failed; rollback also reported an error.", failure, rollbackFailure);
                throw;
            }
        }
    }
}
