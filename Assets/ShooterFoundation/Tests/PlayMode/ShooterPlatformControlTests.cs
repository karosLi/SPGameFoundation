#if UNITY_EDITOR && !SPF_DOTNET_HARNESS
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using SPF.Shell.Performance;
using Unity.Burst;
using Unity.Profiling;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ShooterFoundation.Tests.PlayMode
{
    internal struct PlatformSample
    {
        internal int SourceFrame, ReadFrame, ReaderFrame, Frames, Allocating, Count, Capacity, Gc0, Rate, VSync, Quality, EffectiveRate, Thermal, RenderInterval;
        internal long Bytes, Total, DirectBytes;
        internal bool Valid, Running, Wrapped, Idle, Charging, LowPower;
        internal float DeltaTime, TimeScale, Battery;
        internal double SourceTime, ReadTime;
    }

    public class ShooterPlatformControlTests
    {
        internal const int Warmup = 150, Window = 180, Controls = 24, Capacity = Window + Controls;
        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        [UnityTest, Explicit("Default-off platform measurement control; never a Shooter product allocation gate.")]
        public IEnumerator MatchedEmptyPlatformWindows()
        {
            if (Environment.GetEnvironmentVariable("SPF_SHOOTER_PLATFORM_CONTROL") != "1")
                Assert.Ignore("Use the dedicated platform-control launcher.");
            string evidence = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Artifacts", "GC", "platform-control"));
            Directory.CreateDirectory(evidence);
            File.WriteAllText(Path.Combine(evidence, "preflight.txt"), "profiler=" + Profiler.enabled + "\ndriver=" + ProfilerDriver.enabled
                + "\nprofileEditor=" + ProfilerDriver.profileEditor + "\ndeep=" + ProfilerDriver.deepProfiling
                + "\ncallstacks=" + Profiler.enableAllocationCallstacks + "\nbinary=" + Profiler.enableBinaryLog
                + "\nrequestedFps=" + Application.targetFrameRate + "\nvSync=" + QualitySettings.vSyncCount
                + "\ntimeScale=" + Time.timeScale.ToString("R", Invariant) + "\nrenderInterval=" + OnDemandRendering.renderFrameInterval + "\n");
            Assert.IsTrue(QuietProfiler(), "Profiling is active or configured for Editor/deep/stacks; refuse to change settings.");
            Assert.AreNotEqual(GraphicsDeviceType.Null, SystemInfo.graphicsDeviceType, "Graphics-enabled native control required.");
            Assert.IsTrue(BurstCompiler.IsEnabled && BurstCompiler.Options.EnableBurstCompilation, "Keep the ordinary Burst configuration.");
            int oldRate = Application.targetFrameRate, initialVSync = QualitySettings.vSyncCount;
            float initialScale = Time.timeScale;
            int initialRenderInterval = OnDemandRendering.renderFrameInterval;
            bool allValid = true;
            try
            {
                // Fixed order in one test avoids NUnit scheduling or extra framework wrappers.
                // A runs before any SPF governor or Shooter gameplay component is created.
                for (int condition = 0; condition < 2; condition++)
                {
                    string name = condition == 0 ? "A-raw-recorder" : "B-frame-governor";
                    string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Artifacts", "GC", "platform-control", name));
                    Directory.CreateDirectory(folder);
                    CheckComponentInventory(folder);
                    Assert.IsTrue(QuietProfiler(), "Profiler state changed; refuse platform control.");
                    var samples = new PlatformSample[Capacity];
                    var root = new GameObject("Shooter allocation platform control " + name);
                    var cameraRoot = new GameObject("Platform control empty camera");
                    FrameGovernor governor = null;
                    ShooterPlatformRawReader raw = null;
                    ShooterPlatformControlProbe probe = null;
                    bool complete = false;
                    int count = 0, startGc = GC.CollectionCount(0);
                    // This is a borrowed handle for B. Only its owning component disposes it.
                    ProfilerRecorder handle = default;
                    try
                    {
                        var camera = cameraRoot.AddComponent<Camera>();
                        camera.clearFlags = CameraClearFlags.SolidColor;
                        camera.backgroundColor = new Color(0.022f, 0.048f, 0.087f);
                        camera.orthographic = true; camera.orthographicSize = 9.6f;
                        Application.targetFrameRate = 60;
                        if (condition == 0)
                        {
                            raw = root.AddComponent<ShooterPlatformRawReader>();
                            handle = raw.Recorder;
                        }
                        else
                        {
                            governor = root.AddComponent<FrameGovernor>();
                            governor.SetFrameRates(60, 30); governor.ThrottleWhenIdle = true;
                            var field = typeof(FrameGovernor).GetField("m_Gc", BindingFlags.Instance | BindingFlags.NonPublic);
                            Assert.IsNotNull(field, "Governor counter changed; fail instead of substituting a different recorder.");
                            handle = (ProfilerRecorder)field.GetValue(governor); // Cold, once, before warmup.
                        }
                        probe = root.AddComponent<ShooterPlatformControlProbe>();
                        probe.Governor = governor;
                        probe.Warm();
                        Read(default, raw, governor, handle); // Warm the complete observation path.
                        WriteSettings(folder, name, initialVSync, handle.Valid ? handle.Capacity : -1);
                        for (int i = 0; i < Warmup; i++) yield return null;
                        if (raw != null) raw.ResetWindow(); else governor.ResetGcStats();
                        startGc = GC.CollectionCount(0);
                        for (int i = 0; i < Window; i++)
                        {
                            var source = Source();
                            yield return null;
                            samples[count] = Read(source, raw, governor, handle); count++;
                        }
                        // samples[179] freezes the endpoint. All formatting/IO waits until controls end.
                        for (int control = 0; control < 6; control++)
                        {
                            probe.Arm(control, Time.frameCount + 1);
                            for (int f = 0; f < 4; f++)
                            {
                                probe.CoroutinePulse();
                                var source = Source();
                                yield return null;
                                samples[count] = Read(source, raw, governor, handle); count++;
                            }
                        }
                        complete = true;
                    }
                    finally
                    {
                        // Cold exports retain partial/invalid evidence too. No profiler preference is changed.
                        try
                        {
                            WriteSamples(Path.Combine(folder, "window.csv"), samples, Math.Min(count, Window));
                            if (count >= Window) WriteEndpoint(folder, samples[Window - 1], startGc);
                            WriteSamples(Path.Combine(folder, "all-observations.csv"), samples, count);
                            bool samplesValid = ValidateSamples(samples, count, initialVSync, initialScale, initialRenderInterval);
                            bool controlsValid = WriteControls(folder, samples, count, probe);
                            bool valid = complete && samplesValid && controlsValid && QuietProfiler();
                            WriteSummary(folder, name, valid, complete, samplesValid, controlsValid, samples, count, startGc);
                            allValid &= valid;
                        }
                        finally
                        {
                            root.SetActive(false); cameraRoot.SetActive(false);
                            Object.Destroy(root); Object.Destroy(cameraRoot);
                        }
                    }
                    yield return null; // Complete destruction before inspecting the next condition.
                }
            }
            finally { Application.targetFrameRate = oldRate; }
            Assert.IsTrue(allValid, "Platform control sampling/calibration invalid; inspect raw evidence. This is not a product gate.");
        }

        static bool QuietProfiler() => !Profiler.enabled && !ProfilerDriver.enabled && !ProfilerDriver.profileEditor
            && !ProfilerDriver.deepProfiling && !Profiler.enableAllocationCallstacks && !Profiler.enableBinaryLog;

        static PlatformSample Source() => new PlatformSample { SourceFrame = Time.frameCount, SourceTime = Time.realtimeSinceStartupAsDouble };

        static PlatformSample Read(PlatformSample sample, ShooterPlatformRawReader raw, FrameGovernor governor, ProfilerRecorder handle)
        {
            sample.ReadFrame = Time.frameCount; sample.ReadTime = Time.realtimeSinceStartupAsDouble;
            sample.Valid = handle.Valid;
            sample.Running = handle.Valid && handle.IsRunning; sample.Wrapped = handle.Valid && handle.WrappedAround;
            sample.Count = handle.Valid ? handle.Count : -1; sample.Capacity = handle.Valid ? handle.Capacity : -1;
            sample.DirectBytes = handle.Valid ? handle.LastValue : -1;
            sample.ReaderFrame = raw != null ? raw.ReadFrame : -1; // B has no public Update-frame stamp; never manufacture one.
            sample.Bytes = raw != null ? raw.LastBytes : governor.GcBytesLastFrame;
            sample.Total = raw != null ? raw.Bytes : governor.GcBytesSinceReset;
            sample.Frames = raw != null ? raw.Frames : governor.FramesSinceReset;
            sample.Allocating = raw != null ? raw.AllocatingFrames : governor.GcFramesSinceReset;
            sample.Gc0 = GC.CollectionCount(0); sample.Rate = Application.targetFrameRate;
            sample.VSync = QualitySettings.vSyncCount;
            sample.Quality = governor != null ? governor.Level : -1;
            sample.Idle = governor != null && governor.IsIdle;
            sample.DeltaTime = Time.unscaledDeltaTime; sample.TimeScale = Time.timeScale;
            sample.RenderInterval = OnDemandRendering.renderFrameInterval;
            sample.EffectiveRate = governor != null ? governor.EffectiveFrameRate : Application.targetFrameRate;
            sample.Thermal = governor != null ? (int)governor.Thermal.State.Thermal : -1;
            sample.Battery = governor != null ? governor.Thermal.State.Battery : -1f;
            sample.Charging = governor != null && governor.Thermal.State.Charging;
            sample.LowPower = governor != null && governor.Thermal.State.LowPowerMode;
            return sample;
        }

        internal static bool ValidateSamples(PlatformSample[] samples, int count, int vSync, float timeScale, int renderInterval)
        {
            if (count != Capacity) return false;
            long sum = 0; int allocating = 0;
            for (int i = 0; i < count; i++)
            {
                var s = samples[i]; sum += s.Bytes; if (s.Bytes > 0) allocating++;
                // Count is a ring index when wrapped; zero alone does not mean unready at capacity 1.
                if (!s.Valid || !s.Running || (!s.Wrapped && s.Count == 0) || s.Capacity != 1 || s.Bytes < 0
                    || s.DirectBytes != s.Bytes || s.Total != sum || s.Allocating != allocating || s.Frames != i + 1
                    || s.ReadFrame != s.SourceFrame + 1 || (s.ReaderFrame >= 0 && s.ReaderFrame != s.ReadFrame)
                    || s.ReadTime <= s.SourceTime || s.Rate != 60 || s.VSync != vSync || s.Idle
                    || s.TimeScale != timeScale || s.RenderInterval != renderInterval || s.EffectiveRate != 60) return false;
                if (i > 0 && (s.SourceFrame != samples[i - 1].SourceFrame + 1 || s.Gc0 < samples[i - 1].Gc0)) return false;
            }
            return true;
        }

        internal static bool ValidateControl(PlatformSample[] samples, int count, int source, int requested, int actualPhase, int control, int emissions, int payload,
            bool retained, out int candidates, out int matchedFrame, out long matchedBytes)
        {
            candidates = 0; matchedFrame = -1; matchedBytes = -1;
            if (source <= 0 || source != requested || actualPhase != control / 2 || emissions != 1 || !retained) return false;
            bool sourcePresent = false;
            for (int i = Window; i < count; i++)
            {
                var s = samples[i]; int lag = s.ReadFrame - source;
                if (s.SourceFrame == source) sourcePresent = true;
                // Empty whole frames may include background allocation: preserve their lag-1 bytes.
                if (payload == 0 ? lag != 1 : lag < -1 || lag > 2 || s.Bytes < payload) continue;
                candidates++; matchedFrame = s.ReadFrame; matchedBytes = s.Bytes;
            }
            return sourcePresent && candidates == 1 && matchedFrame - source == 1;
        }

        static bool WriteControls(string folder, PlatformSample[] samples, int count, ShooterPlatformControlProbe probe)
        {
            bool valid = probe != null;
            using (var writer = new StreamWriter(Path.Combine(folder, "controls.csv")))
            {
                writer.WriteLine("control,phase,requestedFrame,actualPhase,sourceUnityFrame,sourceRealtime,payload,emissions,retained,candidateCount,matchedReadFrame,wholeFrameBytes,lag,valid");
                if (probe == null) return false;
                for (int c = 0; c < 6; c++)
                {
                    bool retained = probe.Retained(c);
                    bool ok = ValidateControl(samples, count, probe.SourceFrames[c], probe.RequestedFrames[c], probe.ActualPhases[c], c, probe.Emissions[c], ShooterPlatformControlProbe.Payload(c),
                        retained, out int candidates, out int frame, out long bytes);
                    valid &= ok;
                    writer.WriteLine(string.Join(",", c, ShooterPlatformControlProbe.Names[c], probe.RequestedFrames[c], probe.ActualPhases[c], probe.SourceFrames[c],
                        probe.SourceTimes[c].ToString("R", Invariant), ShooterPlatformControlProbe.Payload(c), probe.Emissions[c],
                        retained, candidates, frame, bytes, frame < 0 ? -1 : frame - probe.SourceFrames[c], ok));
                }
            }
            return valid;
        }

        static void WriteSamples(string path, PlatformSample[] samples, int count)
        {
            using (var writer = new StreamWriter(path))
            {
                writer.WriteLine("ordinal,phase,sourceUnityFrame,readUnityFrame,rawReaderUpdateFrame,sourceRealtime,readRealtime,frames,previousFrameBytes,cumulativeBytes,allocatingFrames,counterValid,counterRunning,counterWrapped,counterCount,counterCapacity,directCounterBytes,gc0,requestedFps,vSync,quality,idle,unscaledDeltaTime,timeScale,renderInterval,effectiveFps,thermal,battery,charging,lowPower");
                for (int i = 0; i < count; i++)
                {
                    var s = samples[i];
                    writer.WriteLine(string.Join(",", i, i < Window ? "window" : "calibration", s.SourceFrame, s.ReadFrame, s.ReaderFrame,
                        s.SourceTime.ToString("R", Invariant), s.ReadTime.ToString("R", Invariant), s.Frames, s.Bytes, s.Total, s.Allocating,
                        s.Valid, s.Running, s.Wrapped, s.Count, s.Capacity, s.DirectBytes, s.Gc0, s.Rate, s.VSync, s.Quality, s.Idle,
                        s.DeltaTime.ToString("R", Invariant), s.TimeScale.ToString("R", Invariant), s.RenderInterval, s.EffectiveRate,
                        s.Thermal, s.Battery.ToString("R", Invariant), s.Charging, s.LowPower));
                }
            }
        }

        static string Json(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r") + "\"";
        static string Bool(bool value) => value ? "true" : "false";

        static void WriteSettings(string folder, string condition, int vSync, int capacity)
        {
            File.WriteAllText(Path.Combine(folder, "settings.txt"), "condition=" + condition + "\nunity=" + Application.unityVersion
                + "\nplatform=" + Application.platform + "\ngraphics=" + SystemInfo.graphicsDeviceType
                + "\nresolution=" + Screen.width + "x" + Screen.height + "\nrequestedFps=60\nvSync=" + vSync
                + "\nrecorderCapacity=" + capacity + "\nrecorderOptions=StartNew defaults\nburst=" + BurstCompiler.IsEnabled
                + "\nprofilerStateChanged=false\neditorPrefsChanged=false\nallocationCallstacks=false\ndeepProfiling=false\n"
                + "scope=No SPF/Shooter gameplay components in A; minimal test observers and project-loaded Editor environment remain.\n"
                + "B adds the actual shared FrameGovernor, including its platform services and quality policy.\n");
        }

        static void WriteEndpoint(string folder, PlatformSample end, int gc0)
        {
            File.WriteAllText(Path.Combine(folder, "window-endpoint.json"), "{\"frames\":" + end.Frames
                + ",\"allocatingFrames\":" + end.Allocating + ",\"bytes\":" + end.Total
                + ",\"generation0Collections\":" + (end.Gc0 - gc0) + ",\"establishesProductGate\":false}\n");
        }

        static void WriteSummary(string folder, string name, bool valid, bool complete, bool samplesValid, bool controlsValid,
            PlatformSample[] samples, int count, int gc0)
        {
            var end = count > 0 ? samples[Math.Min(count, Window) - 1] : default;
            File.WriteAllText(Path.Combine(folder, "summary.json"), "{\"condition\":" + Json(name)
                + ",\"diagnosticOnly\":true,\"measurementValid\":" + Bool(valid) + ",\"complete\":" + Bool(complete)
                + ",\"observationsValid\":" + Bool(samplesValid) + ",\"calibrationValid\":" + Bool(controlsValid)
                + ",\"observationCount\":" + count + ",\"windowFrames\":" + (count >= Window ? Window : count)
                + ",\"windowBytes\":" + end.Total + ",\"windowAllocatingFrames\":" + end.Allocating
                + ",\"generation0Collections\":" + (count > 0 ? end.Gc0 - gc0 : -1)
                + ",\"establishesProductGate\":false,\"allocationOwnership\":\"unattributed\""
                + ",\"counterLimit\":\"Capacity-one readiness and phase controls establish bounded responsiveness, not independent freshness of every zero sample. Source-to-allocation mapping is unvalidated if calibrationValid=false.\""
                + ",\"interpretation\":\"A reproduction establishes occurrence without SPF/Shooter gameplay components, not a pure engine source. B-only occurrence narrows to governor or induced state, not a managed callsite. Non-reproduction excludes no source. No background subtraction or Shooter threshold change.\"}\n");
        }

        static void CheckComponentInventory(string folder)
        {
            var output = new StringBuilder(); bool clean = true;
            foreach (var component in Object.FindObjectsOfType<Component>(true))
            {
                if (component == null) { output.AppendLine("<missing component>"); clean = false; continue; }
                var type = component.GetType(); string assembly = type.Assembly.GetName().Name;
                output.Append(type.FullName).Append(" | ").AppendLine(assembly);
                if (type != typeof(Transform) && !AllowedRunnerComponent(type.FullName, assembly)) clean = false;
            }
            File.WriteAllText(Path.Combine(folder, "initial-components.txt"), output.ToString());
            Assert.IsTrue(clean, "Unexpected scene component invalidates the empty-scene control; preserve inventory without deleting unrelated objects.");
        }

        internal static bool AllowedRunnerComponent(string fullName, string assembly)
        {
            if (assembly != "UnityEngine.TestRunner") return false;
            return fullName == "UnityEngine.TestTools.TestRunner.PlaymodeTestsController"
                || fullName == "UnityEngine.TestTools.TestRunner.Callbacks.PlayModeRunnerCallback"
                || fullName == "UnityEngine.TestTools.TestRunner.Callbacks.PlayerQuitHandler"
                || fullName == "UnityEngine.TestTools.TestRunner.Callbacks.RemoteTestResultSender"
                || fullName == "UnityEngine.TestTools.TestRunner.Callbacks.TestResultRendererCallback";
        }
    }
}
#endif
