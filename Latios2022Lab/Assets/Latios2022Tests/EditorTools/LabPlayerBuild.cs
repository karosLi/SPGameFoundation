using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Latios2022Lab
{
    public static class LabPlayerBuild
    {
        public static void BuildSmokePlayer()
        {
            LabEnvironment.Verify();
            var target = EditorUserBuildSettings.activeBuildTarget;
            if (target != BuildTarget.StandaloneOSX && target != BuildTarget.StandaloneLinux64 && target != BuildTarget.StandaloneWindows64)
                throw new InvalidOperationException("First smoke build is a desktop IL2CPP control. Mobile builds need their separate platform gate.");
            var group = BuildPipeline.GetBuildTargetGroup(target);
            PlayerSettings.SetScriptingBackend(group, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetIl2CppCodeGeneration(UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(group), UnityEditor.Build.Il2CppCodeGeneration.OptimizeSize);
            Directory.CreateDirectory("Assets/Latios2022Tests/Generated");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Latios S1a smoke").AddComponent<LabPlayerSmoke>();
            const string scenePath = "Assets/Latios2022Tests/Generated/Smoke.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            var output = Environment.GetEnvironmentVariable("LATIOS_LAB_OUTPUT");
            if (string.IsNullOrEmpty(output)) throw new InvalidOperationException("Evidence directory is required.");
            var extension = target == BuildTarget.StandaloneOSX ? ".app" : target == BuildTarget.StandaloneWindows64 ? ".exe" : "";
            var location = Path.GetFullPath(Path.Combine("Builds", "IL2CPP", "Latios2022Lab" + extension));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { scenePath }, locationPathName = location, target = target,
                options = BuildOptions.Development
            });
            File.WriteAllText(Path.Combine(output, "build.txt"),
                "Target=" + target + "\nBackend=IL2CPP\nResult=" + report.summary.result +
                "\nErrors=" + report.summary.totalErrors + "\nWarnings=" + report.summary.totalWarnings +
                "\nOutput=" + location + "\nExecution=NOT_RUN\n");
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("IL2CPP smoke build failed. Preserve the log.");
        }
    }
}
