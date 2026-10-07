#if UNITY_EDITOR && !SPF_DOTNET_HARNESS
using System;
using System.IO;
using System.Text;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using SPF.L1.Physics;

namespace SPF.Editor
{
    /// <summary>Invoked only by the separately authorized, one-time CI setup workflow.</summary>
    public static class CiBurstSetup
    {
        const string SafetyKey = "BurstSafetyChecks";
        const string OutputDirectory = "BurstSetupArtifacts";

        sealed class EditorStore : CiBurstPreferenceTransaction.IStore
        {
            public CiBurstPreferenceTransaction.Value Read(string key) =>
                new CiBurstPreferenceTransaction.Value(EditorPrefs.HasKey(key), EditorPrefs.GetBool(key, false));
            public void Write(string key, CiBurstPreferenceTransaction.Value value)
            {
                if (value.Exists) EditorPrefs.SetBool(key, value.Enabled);
                else EditorPrefs.DeleteKey(key);
            }
            public CiBurstPreferenceTransaction.Value SafetySession
            {
                get
                {
                    bool withFalse = SessionState.GetBool(SafetyKey, false);
                    bool withTrue = SessionState.GetBool(SafetyKey, true);
                    return new CiBurstPreferenceTransaction.Value(withFalse == withTrue, withFalse);
                }
                set
                {
                    if (value.Exists) SessionState.SetBool(SafetyKey, value.Enabled);
                    else SessionState.EraseBool(SafetyKey);
                }
            }
            public bool CompilationEnabled
            {
                get => BurstCompiler.Options.EnableBurstCompilation;
                set => BurstCompiler.Options.EnableBurstCompilation = value;
            }
            public bool BackendEnabled => BurstCompiler.IsEnabled && BurstCompiler.Options.IsEnabled && JobsUtility.JobCompilerEnabled;
        }

        public static void Inspect()
        {
            RequireBatchMode();
            Save("before.txt", Snapshot());
        }

        public static void EnableApproved()
        {
            // The transaction checks the exact flag before touching preferences.
            var report = new StringBuilder();
            try
            {
                RequireBatchMode();
                if (Array.IndexOf(Environment.GetCommandLineArgs(), CiBurstPreferenceTransaction.ApprovalFlag) < 0)
                    throw new InvalidOperationException("Missing exact Burst setup approval flag.");
                report.AppendLine("Before activation:").AppendLine(Snapshot());
                CiBurstPreferenceTransaction.Enable(Application.isBatchMode, Environment.GetCommandLineArgs(), new EditorStore());
                report.AppendLine("After activation:").AppendLine(Snapshot());
            }
            catch (Exception error)
            {
                report.AppendLine("Activation failed:").AppendLine(error.ToString());
                throw;
            }
            finally { Save("activation.txt", report.ToString()); }
            // On macOS, automatic -batchmode/-quit can exit without flushing EditorPrefs.
            // Request the public Editor exit path only after activation and evidence succeeded.
            EditorApplication.Exit(0);
        }

        public static void VerifyFreshProcess()
        {
            RequireBatchMode();
            var report = new StringBuilder(Snapshot());
            try
            {
                var store = new EditorStore();
                if (!store.Read("BurstCompilation").Matches(new CiBurstPreferenceTransaction.Value(true, true)) ||
                    !store.CompilationEnabled || !store.BackendEnabled)
                    throw new InvalidOperationException("Fresh Editor did not retain enabled Burst compilation.");
                using (var result = new NativeArray<int>(1, Allocator.TempJob))
                {
                    var probe = new IndependentProbe { Result = result };
                    probe.Execute(); int direct = result[0];
                    probe.Run(); int run = result[0];
                    probe.Schedule().Complete(); int scheduled = result[0];
                    report.AppendLine($"Independent native witness: direct={direct}; Run={run}; Schedule={scheduled}");
                    if (direct != 0 || run != 1 || scheduled != 1)
                        throw new InvalidOperationException("Independent managed/native control must be 0/1/1.");
                }
                using (var world = new PhysicsWorld2D(16))
                {
                    world.AddCircle(new float2(0f, 3f), 0.5f);
                    world.Step(1f / 60f);
                    report.AppendLine($"Actual physics first step Burst={world.LastStepExecutedWithBurst}");
                    if (!world.LastStepExecutedWithBurst)
                        throw new InvalidOperationException("Actual physics did not execute with Burst.");
                }
                report.AppendLine("Fresh process native verification passed.");
            }
            catch (Exception error) { report.AppendLine(error.ToString()); throw; }
            finally { Save("verified.txt", report.ToString()); }
        }

        [BurstCompile(CompileSynchronously = true)]
        struct IndependentProbe : IJob
        {
            public NativeArray<int> Result;
            public void Execute() { bool native = true; MarkManaged(ref native); Result[0] = native ? 1 : 0; }
            [BurstDiscard] static void MarkManaged(ref bool native) { native = false; }
        }

        static string Snapshot()
        {
            var store = new EditorStore();
            var text = new StringBuilder($"Unity={Application.unityVersion}; process={System.Diagnostics.Process.GetCurrentProcess().Id}\n");
            foreach (string key in CiBurstPreferenceTransaction.PreferenceKeys) text.AppendLine(key + ": " + store.Read(key));
            text.AppendLine(SafetyKey + " (session): " + store.SafetySession);
            text.AppendLine($"CompilationEnabled={store.CompilationEnabled}; BackendEnabled={store.BackendEnabled}");
            text.AppendLine($"SafetyChecks={BurstCompiler.Options.EnableBurstSafetyChecks}; Synchronous={BurstCompiler.Options.EnableBurstCompileSynchronously}; Debug={BurstCompiler.Options.EnableBurstDebug}; ForceSafety={BurstCompiler.Options.ForceEnableBurstSafetyChecks}");
            return text.ToString();
        }
        static void RequireBatchMode()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("CI Burst setup only runs in batch mode.");
        }
        static void Save(string name, string text)
        {
            Directory.CreateDirectory(OutputDirectory);
            File.WriteAllText(Path.Combine(OutputDirectory, name), text);
            Debug.Log(text);
        }
    }
}
#endif
