using System;
using System.IO;
using System.Linq;
using Unity.Burst;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Latios2022Lab
{
    public static class LabEnvironment
    {
        public const string EditorVersion = LabRunPolicy.EditorVersion;
        public static bool ExpectBurst => LabRunPolicy.ExpectedBurst;

        public static void Verify()
        {
            LabRunPolicy.VerifyRequestedMode();
            if (new DirectoryInfo(Application.dataPath).Parent.Name != "Latios2022Lab")
                throw new InvalidOperationException("This entry point only runs in the dedicated Latios2022Lab project.");
            var target = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
            if (PlayerSettings.GetApiCompatibilityLevel(target) != ApiCompatibilityLevel.NET_Standard)
                throw new InvalidOperationException("The lab requires .NET Standard API compatibility.");
            if (EditorSettings.enterPlayModeOptionsEnabled &&
                (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) != 0)
                throw new InvalidOperationException("The lab's reentry gate requires domain reload enabled.");
        }

        public static void CaptureEnvironment()
        {
            Verify();
            string output = Environment.GetEnvironmentVariable("LATIOS_LAB_OUTPUT");
            if (string.IsNullOrEmpty(output)) throw new InvalidOperationException("Use Tools/lab.py to select an evidence directory.");
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var lockPath = Path.Combine(root, "Packages/packages-lock.json");
            if (!File.Exists(lockPath)) throw new InvalidOperationException("Unity has not produced an actual package lock.");
            var packages = PackageInfo.GetAllRegisteredPackages().Select(p => new PackageRecord {
                name = p.name, version = p.version, source = p.source.ToString(), packageId = p.packageId
            }).OrderBy(p => p.name).ToArray();
            using var world = LabWorld.Create("S1a environment inventory", true);
            world.Update();
            var data = new EnvironmentRecord {
                editor = Application.unityVersion,
                cpu = SystemInfo.processorType,
                os = SystemInfo.operatingSystem,
                buildTarget = EditorUserBuildSettings.activeBuildTarget.ToString(),
                api = PlayerSettings.GetApiCompatibilityLevel(BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget)).ToString(),
                defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget)),
                burstEnabled = BurstCompiler.IsEnabled,
                burstSafety = BurstCompiler.Options.EnableBurstSafetyChecks,
                packages = packages,
                compiledAssemblies = CompilationPipeline.GetAssemblies().Select(a => a.name).OrderBy(a => a).ToArray(),
                // Managed systems only. This list is not a complete unmanaged-system execution trace.
                installedManagedSystems = world.Systems.Select(s => s.GetType().FullName).OrderBy(s => s).ToArray(),
                executedScope = "Owned Core world and empty QVVS world update; Psyshock is tested separately via arrays. No optional renderer/audio installer."
            };
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "environment.json"), JsonUtility.ToJson(data, true));
            File.Copy(lockPath, Path.Combine(output, "packages-lock.json"));
        }

        [Serializable] private class PackageRecord { public string name, version, source, packageId; }
        [Serializable] private class EnvironmentRecord
        {
            public string editor, cpu, os, buildTarget, api, defines, executedScope;
            public bool burstEnabled, burstSafety;
            public PackageRecord[] packages;
            public string[] compiledAssemblies, installedManagedSystems;
        }
    }
}
