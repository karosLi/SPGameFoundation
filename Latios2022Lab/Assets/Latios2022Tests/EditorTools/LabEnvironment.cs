using System;
using System.IO;
using System.Linq;
using Unity.Burst;
using UnityEditor;
using UnityEditor.Compilation;
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
            string expectedPackagePath = Environment.GetEnvironmentVariable("LATIOS_LAB_PACKAGE_PATH");
            if (!string.IsNullOrEmpty(expectedPackagePath))
            {
                // The launcher has already bound this physical package to the experiment gate.
                var latios = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                    .Single(p => p.name == "com.latios.latiosframework");
                if (latios.source.ToString() != "Local" ||
                    !string.Equals(Path.GetFullPath(latios.resolvedPath), expectedPackagePath, StringComparison.Ordinal))
                    throw new InvalidOperationException("Unity did not load the gated local Latios package.");
            }
        }

        public static void CaptureEnvironment()
        {
            Verify();
            string output = Environment.GetEnvironmentVariable("LATIOS_LAB_OUTPUT");
            if (string.IsNullOrEmpty(output)) throw new InvalidOperationException("Use Tools/lab.py to select an evidence directory.");
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var lockPath = Path.Combine(root, "Packages/packages-lock.json");
            if (!File.Exists(lockPath)) throw new InvalidOperationException("Unity has not produced an actual package lock.");
            var packages = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages().Select(p => new PackageRecord {
                name = p.name, version = p.version, source = p.source.ToString(), packageId = p.packageId,
                resolvedPath = p.resolvedPath
            }).OrderBy(p => p.name).ToArray();
            using var world = LabWorld.Create("S1a environment inventory", true);
            world.Update();
            // World.Systems forbids IEnumerable<T> enumeration in pinned Entities 1.3.5.
            var systems = world.Systems;
            var systemNames = new string[systems.Count];
            for (int i = 0; i < systemNames.Length; i++)
                systemNames[i] = systems[i].GetType().FullName;
            Array.Sort(systemNames);
            var data = new EnvironmentRecord {
                editor = Application.unityVersion,
                cpu = SystemInfo.processorType,
                os = SystemInfo.operatingSystem,
                buildTarget = EditorUserBuildSettings.activeBuildTarget.ToString(),
                api = PlayerSettings.GetApiCompatibilityLevel(BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget)).ToString(),
                defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget)),
                burstEnabled = BurstCompiler.IsEnabled,
                burstSafety = BurstCompiler.Options.EnableBurstSafetyChecks,
                experimentId = Environment.GetEnvironmentVariable("LATIOS_LAB_EXPERIMENT_ID") ?? "",
                experimentArm = Environment.GetEnvironmentVariable("LATIOS_LAB_EXPERIMENT_ARM") ?? "",
                experimentRecordSha256 = Environment.GetEnvironmentVariable("LATIOS_LAB_EXPERIMENT_RECORD_SHA256") ?? "",
                packages = packages,
                compiledAssemblies = CompilationPipeline.GetAssemblies().Select(a => a.name).OrderBy(a => a).ToArray(),
                // Managed systems only. This list is not a complete unmanaged-system execution trace.
                installedManagedSystems = systemNames,
                executedScope = "Owned Core world with initialization ECB and empty scene ordering group; empty QVVS update. Psyshock is tested separately via arrays. No scene streaming or optional renderer/audio installer."
            };
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "environment.json"), JsonUtility.ToJson(data, true));
            File.Copy(lockPath, Path.Combine(output, "packages-lock.json"));
        }

        [Serializable] private class PackageRecord { public string name, version, source, packageId, resolvedPath; }
        [Serializable] private class EnvironmentRecord
        {
            public string editor, cpu, os, buildTarget, api, defines, executedScope;
            public string experimentId, experimentArm, experimentRecordSha256;
            public bool burstEnabled, burstSafety;
            public PackageRecord[] packages;
            public string[] compiledAssemblies, installedManagedSystems;
        }
    }
}
