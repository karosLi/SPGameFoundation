using System.IO;
using UnityEditor;
using UnityEngine;

namespace SnakeFoundation.Editor
{
    /// <summary>
    /// Device GPU benchmark: a development build of the snake scene with SPF_GPU_BENCH, which adds
    /// <see cref="SnakeFoundation.Game.GpuBenchmarkRunner"/> at start. Install, launch, wait ~1 minute:
    /// results appear on screen, in the device log ("=== GPU benchmark") and in
    /// persistentDataPath/gpu-benchmark.txt.
    /// </summary>
    [InitializeOnLoad]
    public static class GpuBenchmarkBuild
    {
        static GpuBenchmarkBuild()
        {
            // GPU frame times (FrameTimingManager) need Frame Timing Stats; it is cheap, keep it on.
            if (!PlayerSettings.enableFrameTimingStats)
                PlayerSettings.enableFrameTimingStats = true;
        }

        [MenuItem("SPF/Snake/Build GPU Benchmark (Android)")]
        public static void BuildAndroid() => Build(BuildTarget.Android, "Builds/GpuBenchmark/SnakeGpuBenchmark.apk");

        [MenuItem("SPF/Snake/Build GPU Benchmark (iOS)")]
        public static void BuildIOS() => Build(BuildTarget.iOS, "Builds/GpuBenchmark/iOS");

        static void Build(BuildTarget target, string path)
        {
            SnakeProjectSetup.ApplyMobileSettings();
            PlayerSettings.enableFrameTimingStats = true;
            if (!File.Exists(SnakeProjectSetup.ScenePath))
                SnakeProjectSetup.CreateScene();
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { SnakeProjectSetup.ScenePath },
                locationPathName = path,
                target = target,
                options = BuildOptions.Development,
                extraScriptingDefines = new[] { "SPF_GPU_BENCH" },
            });
            Debug.Log($"[SPF] GPU benchmark build {report.summary.result}: {path}");
        }
    }
}
