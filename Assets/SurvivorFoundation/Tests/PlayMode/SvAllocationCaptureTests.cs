#if UNITY_EDITOR && !SPF_DOTNET_HARNESS
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using SPF.Presentation;
using SurvivorFoundation.Game;
using Unity.Collections;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace SurvivorFoundation.Tests.PlayMode
{
    /// <summary>
    /// Opt-in diagnostic, not an allocation-budget regression. It leaves AutoPlay, HUD,
    /// feedback, HP, spawning and level-up handling intact. All extraction runs after capture.
    /// API/metadata layout verified against the installed Unity 2022.3.62f2 assemblies.
    /// </summary>
    public class SvAllocationCaptureTests
    {
        const int DefaultFrames = 600;
        const int Tag = 1;
        const string ObserverMarker = "SurvivorGC.CaptureObserver";
        static readonly Guid StampId = new Guid("62497309-8920-4997-8477-e5cfae6a11bb");
        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        // Blittable: preallocated once and copied into the profiler stream without per-frame strings.
        struct Stamp
        {
            public int Window, Ordinal, UnityFrame, Flow, Level, Version, Kills;
            public int MaxGeneration, Gc0, Gc1, Gc2;
            public long Tick, GovernorPreviousFrameBytes;
            public double Realtime;
            public float GameTime, Hp;
        }

        sealed class Aggregate
        {
            public int Samples, MissingStacks, UnresolvedAddresses;
            public long Bytes;
            public string Thread, Ancestry, Stack;
        }

        // ProfilerUserSettings is internal in this exact LTS release. Reflection is confined to
        // setup/restore; its setter updates both EditorPrefs and the native frame-history limit.
        sealed class SavedSettings : IDisposable
        {
            readonly bool enabled = Profiler.enabled;
            readonly bool driverEnabled = ProfilerDriver.enabled;
            readonly bool profileEditor = ProfilerDriver.profileEditor;
            readonly bool deep = ProfilerDriver.deepProfiling;
            readonly bool stacks = Profiler.enableAllocationCallstacks;
            readonly bool binary = Profiler.enableBinaryLog;
            readonly string logFile = Profiler.logFile;
            readonly int memory = Profiler.maxUsedMemory;
            readonly ProfilerMemoryRecordMode recordMode = ProfilerDriver.memoryRecordMode;
            readonly int targetRate = Application.targetFrameRate;
            readonly int vSync = QualitySettings.vSyncCount;
            readonly float timeScale = Time.timeScale;
            readonly bool[] areas = new bool[Profiler.areaCount];
            readonly PropertyInfo history;
            readonly int historyCount;
            readonly bool hadHistoryPreference;
            readonly int historyPreference;
            readonly RenderTier? tier = RenderCapabilities.Override;

            public SavedSettings()
            {
                for (int i = 0; i < areas.Length; ++i) areas[i] = ProfilerDriver.IsAreaEnabled((ProfilerArea)i);
                var type = typeof(ProfilerDriver).Assembly.GetType("UnityEditor.Profiling.ProfilerUserSettings", true);
                history = type.GetProperty("frameCount", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (history == null) throw new NotSupportedException("No Unity 2022.3 Profiler frame-count property.");
                historyCount = (int)history.GetValue(null, null);
                hadHistoryPreference = EditorPrefs.HasKey("Profiler.FrameCount");
                historyPreference = EditorPrefs.GetInt("Profiler.FrameCount", 300);
            }
            public void Configure(bool includeEditor, int frames, int memoryMiB)
            {
                StopRecording();
                Profiler.enableBinaryLog = false;
                ProfilerDriver.profileEditor = includeEditor;
                ProfilerDriver.deepProfiling = false;
                // Keep within the documented 300..2,000 range, with 100 frames of margin.
                int historyFrames = Math.Max(300, frames + 100);
                history.SetValue(null, historyFrames, null);
                if ((int)history.GetValue(null, null) != historyFrames)
                    throw new InvalidOperationException("Profiler history length did not apply.");
                // Use only CPU + Memory; callstack capture is still intrinsically intrusive.
                for (int i = 0; i < areas.Length; ++i)
                    ProfilerDriver.SetAreaEnabled((ProfilerArea)i, i == (int)ProfilerArea.CPU || i == (int)ProfilerArea.Memory);
                Profiler.maxUsedMemory = memoryMiB * 1024 * 1024;
                Profiler.enableAllocationCallstacks = true;
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = 60;
                Time.timeScale = 1f;
            }
            public void Dispose()
            {
                StopRecording();
                Profiler.enableAllocationCallstacks = stacks;
                ProfilerDriver.memoryRecordMode = recordMode;
                ProfilerDriver.profileEditor = profileEditor;
                ProfilerDriver.deepProfiling = deep;
                Profiler.logFile = logFile;
                Profiler.enableBinaryLog = binary;
                Profiler.maxUsedMemory = memory;
                for (int i = 0; i < areas.Length; ++i) ProfilerDriver.SetAreaEnabled((ProfilerArea)i, areas[i]);
                history.SetValue(null, historyCount, null);
                if (hadHistoryPreference) EditorPrefs.SetInt("Profiler.FrameCount", historyPreference);
                else EditorPrefs.DeleteKey("Profiler.FrameCount");
                Application.targetFrameRate = targetRate;
                QualitySettings.vSyncCount = vSync;
                Time.timeScale = timeScale;
                RenderCapabilities.Override = tier;
                ProfilerDriver.enabled = driverEnabled;
                Profiler.enabled = enabled;
            }
        }

        [UnityTest, Explicit("Allocation callstack investigation: six fresh normal-gameplay windows; writes local Artifacts/GC captures.")]
        public IEnumerator NormalGameplayAllocationCallstacks()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Requires a graphics device.");
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("Both render tiers require compute support for this diagnostic.");
            // Read diagnostic options once; they never enter the measured-frame path.
            bool includeEditor = Environment.GetEnvironmentVariable("SPF_GC_INCLUDE_EDITOR") == "1";
            int repeats = ReadOption("SPF_GC_REPEATS", 3, 1, 3);
            int requestedFrames = ReadOption("SPF_GC_FRAMES", DefaultFrames, 60, DefaultFrames);
            int memoryMiB = ReadOption("SPF_GC_MEMORY_MIB", 256, 64, 256);
            string label = Environment.GetEnvironmentVariable("SPF_GC_LABEL");
            if (string.IsNullOrEmpty(label)) label = "unlabelled";
            label = SafeName(label);
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Artifacts", "GC",
                label + "-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ", Invariant)));
            Directory.CreateDirectory(root);
            var saved = new SavedSettings();
            var metadata = new NativeArray<Stamp>(1, Allocator.Persistent);
            var snapshots = new Stamp[requestedFrames + 1];
            var tiers = new[] { RenderTier.GpuDriven, RenderTier.DataTexture };
            try
            {
                saved.Configure(includeEditor, requestedFrames, memoryMiB);
                File.WriteAllText(Path.Combine(root, "capture-settings.txt"), Settings(label, includeEditor, repeats, requestedFrames, memoryMiB));
                for (int t = 0; t < tiers.Length; ++t)
                for (int repeat = 0; repeat < repeats; ++repeat)
                {
                    int window = t * repeats + repeat;
                    string prefix = Path.Combine(root, tiers[t] + "-" + (repeat + 1));
                    SvConfig config = null;
                    SvGameBootstrap game = null;
                    GameObject camera = null;
                    int observed = 0;
                    bool survived = true;
                    try
                    {
                        RenderCapabilities.Override = tiers[t];
                        config = SvConfig.CreateDefault();
                        config.Settings.SpawnPerSecond = 8f;
                        game = SvGameBootstrap.Create(config, seed: 3, ui: true);
                        camera = game.CameraRig.gameObject;
                        yield return null;
                        game.StartRun();
                        double deadline = Time.realtimeSinceStartupAsDouble + 5;
                        while (game.State.Flow != SvFlow.Playing && Time.realtimeSinceStartupAsDouble < deadline)
                            yield return null;
                        Assert.AreEqual(SvFlow.Playing, game.State.Flow, "StartRun entered gameplay.");
                        game.AutoPlay = true;
                        double warmStart = Time.realtimeSinceStartupAsDouble;
                        while (Time.realtimeSinceStartupAsDouble - warmStart < 6)
                        {
                            if (game.State.Flow == SvFlow.Dead) { survived = false; break; }
                            yield return null;
                        }
                        Assert.IsTrue(survived, "Normal seed-3 AutoPlay died during warmup; conditions were not altered.");
                        Assert.IsTrue(game.Hud.enabled && game.Renderer.Feedback != null && game.AutoPlay);
                        // Enabled script state alone is not proof that a Graphic can draw.
                        var hudRenderer = game.Hud.StatsText.GetComponent<CanvasRenderer>();
                        Assert.IsNotNull(hudRenderer, "The normal HUD must have its rendering component.");
                        var hudMesh = hudRenderer.GetMesh();
                        int hudVertices = hudMesh != null ? hudMesh.vertexCount : 0;
                        Assert.Greater(hudVertices, 0, "Warm HUD must have generated glyph geometry before capture.");
                        ProfilerDriver.ClearAllFrames();
                        Profiler.enableAllocationCallstacks = true;
                        ProfilerDriver.enabled = true;
                        Profiler.enabled = true;
                        // Warm up metadata, marker and counter paths before the requested interval.
                        for (int f = 0; f < 8; ++f)
                        {
                            Emit(game, window, -1, metadata);
                            yield return null;
                        }
                        for (int f = 0; f < requestedFrames; ++f)
                        {
                            snapshots[f] = Emit(game, window, f, metadata);
                            observed++;
                            if (snapshots[f].Flow == (int)SvFlow.Dead) { survived = false; break; }
                            yield return null;
                        }
                        // End-boundary counts are sampled at the same coroutine phase as each start.
                        snapshots[observed] = ReadStamp(game, window, observed);
                        // Give the last stamped frame and the native profiler receiver time to finish.
                        for (int f = 0; f < 3; ++f) yield return null;
                        StopRecording();
                        for (int f = 0; f < 2; ++f) yield return null;
                        game.Session.Sync();
                        survived &= game.State.Flow != SvFlow.Dead;
                        ProfilerDriver.SaveProfile(prefix + ".raw");
                        int matched = Extract(prefix, window, snapshots, requestedFrames, observed, survived);
                        File.AppendAllText(Path.Combine(root, "capture-settings.txt"), "\n" + tiers[t] + " repeat=" + (repeat + 1)
                            + " requested=" + requestedFrames + " observed=" + observed + " matched=" + matched + " survived=" + survived
                            + " bufferTextRenderer=True hudGlyphVertices=" + hudVertices
                            + " rawBytes=" + (File.Exists(prefix + ".raw") ? new FileInfo(prefix + ".raw").Length : -1));
                        TestContext.WriteLine("GC capture: " + prefix + " (" + matched + "/" + observed + " mapped frames)");
                        Assert.IsTrue(survived, "Normal gameplay died; captured evidence retained and no HP/feedback/bot changes applied.");
                        Assert.AreEqual(requestedFrames, observed);
                        Assert.AreEqual(observed, matched, "Missing or ambiguous profiler metadata: do not claim a complete capture.");
                    }
                    finally
                    {
                        StopRecording();
                        if (game != null) Object.Destroy(game.gameObject);
                        if (camera != null) Object.Destroy(camera);
                        if (config != null) Object.Destroy(config);
                    }
                    yield return null;
                    yield return null;
                }
                TestContext.WriteLine("Survivor allocation capture complete: " + root);
            }
            finally
            {
                if (metadata.IsCreated) metadata.Dispose();
                saved.Dispose();
            }
        }

        /// <summary>Explicit batch cleanup after an externally killed diagnostic cannot run finally.
        /// Disables recording for normal regression validation; does not claim to restore lost settings.</summary>
        public static void DisableProfilerForBatchValidation()
        {
            StopRecording();
            Profiler.enableAllocationCallstacks = false;
            Profiler.enableBinaryLog = false;
            ProfilerDriver.profileEditor = false;
            ProfilerDriver.deepProfiling = false;
            Debug.Log("Profiler disabled for batch validation: enabled=" + Profiler.enabled
                + ", editor=" + ProfilerDriver.profileEditor + ", allocationCallstacks=" + Profiler.enableAllocationCallstacks);
        }

        static void StopRecording() { Profiler.enabled = false; ProfilerDriver.enabled = false; }

        static Stamp Emit(SvGameBootstrap game, int window, int ordinal, NativeArray<Stamp> metadata)
        {
            Profiler.BeginSample(ObserverMarker);
            var stamp = ReadStamp(game, window, ordinal);
            metadata[0] = stamp;
            Profiler.EmitFrameMetaData(StampId, Tag, metadata);
            Profiler.EndSample();
            return stamp;
        }

        static Stamp ReadStamp(SvGameBootstrap game, int window, int ordinal)
        {
            var state = game.State;
            int max = GC.MaxGeneration;
            return new Stamp {
                Window = window, Ordinal = ordinal, UnityFrame = Time.frameCount,
                Flow = (int)state.Flow, Level = state.Level, Version = state.Version, Kills = state.Kills,
                Tick = game.Session.Pipeline.Stats.TickCount, Realtime = Time.realtimeSinceStartupAsDouble,
                GameTime = state.Time, Hp = state.Hp, GovernorPreviousFrameBytes = game.Governor.GcBytesLastFrame,
                MaxGeneration = max, Gc0 = GC.CollectionCount(0),
                Gc1 = max >= 1 ? GC.CollectionCount(1) : -1, Gc2 = max >= 2 ? GC.CollectionCount(2) : -1
            };
        }

        static int Extract(string prefix, int window, Stamp[] snapshots, int requestedFrames, int observed, bool survived)
        {
            var frames = new int[observed];
            var mappingCount = new int[observed];
            var frameCollision = new bool[observed];
            var bytes = new long[observed];
            var sampleCounts = new int[observed];
            var noStacks = new int[observed];
            var unresolved = new int[observed];
            var observedBytes = new long[observed];
            var frameMs = new float[observed];
            for (int i = 0; i < observed; ++i) frames[i] = -1;
            var aggregates = new Dictionary<string, Aggregate>();
            var methods = new Dictionary<ulong, string>();
            var addresses = new List<ulong>(128);
            var parents = new List<int>(128);
            var ends = new List<int>(128);
            var stack = new StringBuilder(8192);
            var path = new StringBuilder(2048);
            int matched = 0, chunks = 0, invalidOrdinals = 0;
            int first = ProfilerDriver.firstFrameIndex, last = ProfilerDriver.lastFrameIndex;
            using (var allocations = new StreamWriter(prefix + "-allocations.csv", false, new UTF8Encoding(false)))
            {
                allocations.WriteLine("window,ordinal,unityFrame,profilerFrame0,profilerUIFrame1,threadIndex,threadId,threadGroup,threadName,sampleIndex,bytes,startNs,observerScope,noCallstack,unresolvedAddresses,ancestry,callstack");
                for (int frame = first; frame >= 0 && frame <= last; )
                {
                    int ordinal = -1;
                    bool collidedThisFrame = false;
                    using (var main = ProfilerDriver.GetRawFrameDataView(frame, 0))
                    {
                        if (main.valid)
                        {
                            int count = main.GetFrameMetaDataCount(StampId, Tag);
                            for (int c = 0; c < count; ++c)
                            {
                                using (var data = main.GetFrameMetaData<Stamp>(StampId, Tag, c))
                                for (int d = 0; d < data.Length; ++d)
                                {
                                    var stamp = data[d];
                                    if (stamp.Window != window || stamp.Ordinal < 0) continue;
                                    chunks++;
                                    if (stamp.Ordinal >= observed || stamp.UnityFrame != snapshots[stamp.Ordinal].UnityFrame)
                                    { invalidOrdinals++; continue; }
                                    // Multiple distinct Unity-frame stamps in one profiler frame cannot
                                    // be assigned the same complete allocation sample stream safely.
                                    if (ordinal >= 0 && ordinal != stamp.Ordinal)
                                    {
                                        frameCollision[ordinal] = true;
                                        frameCollision[stamp.Ordinal] = true;
                                        collidedThisFrame = true;
                                    }
                                    ordinal = stamp.Ordinal;
                                    mappingCount[ordinal]++;
                                    frames[ordinal] = frame;
                                    frameMs[ordinal] = main.frameTimeMs;
                                }
                            }
                        }
                    }
                    if (ordinal >= 0 && !collidedThisFrame)
                    {
                        for (int thread = 0; ; ++thread)
                        using (var view = ProfilerDriver.GetRawFrameDataView(frame, thread))
                        {
                            if (!view.valid) break;
                            int gc = view.GetMarkerId("GC.Alloc");
                            if (gc == FrameDataView.invalidMarkerId) continue;
                            parents.Clear(); ends.Clear();
                            string threadGroup = view.threadGroupName, threadName = view.threadName;
                            for (int s = 0; s < view.sampleCount; ++s)
                            {
                                while (ends.Count > 0 && s > ends[ends.Count - 1])
                                { parents.RemoveAt(parents.Count - 1); ends.RemoveAt(ends.Count - 1); }
                                if (view.GetSampleMarkerId(s) == gc)
                                {
                                    long amount = view.GetSampleMetadataCount(s) > 0 ? view.GetSampleMetadataAsLong(s, 0) : -1;
                                    path.Length = 0;
                                    bool observer = false;
                                    foreach (int p in parents)
                                    {
                                        string name = view.GetSampleName(p);
                                        if (name == ObserverMarker) observer = true;
                                        if (path.Length > 0) path.Append(" > ");
                                        path.Append(name);
                                    }
                                    addresses.Clear();
                                    view.GetSampleCallstack(s, addresses);
                                    stack.Length = 0;
                                    int unknown = 0;
                                    foreach (ulong address in addresses)
                                    {
                                        string method;
                                        if (!methods.TryGetValue(address, out method))
                                        {
                                            var info = view.ResolveMethodInfo(address);
                                            method = string.IsNullOrEmpty(info.methodName) ? "<unresolved>" : info.methodName;
                                            if (!string.IsNullOrEmpty(info.sourceFileName)) method += " (" + info.sourceFileName + ":" + info.sourceFileLine + ")";
                                            methods.Add(address, method);
                                        }
                                        if (method == "<unresolved>") unknown++;
                                        if (stack.Length > 0) stack.Append(" | ");
                                        stack.Append("0x").Append(address.ToString("X", Invariant)).Append(' ').Append(method);
                                    }
                                    bool empty = addresses.Count == 0;
                                    if (empty) stack.Append("<no recorded callstack>");
                                    string ancestry = path.ToString(), resolved = stack.ToString();
                                    WriteCsv(allocations, window, ordinal, snapshots[ordinal].UnityFrame, frame, frame + 1, thread, view.threadId,
                                        threadGroup, threadName, s, amount, view.GetSampleStartTimeNs(s), observer, empty, unknown, ancestry, resolved);
                                    bytes[ordinal] += Math.Max(0, amount); sampleCounts[ordinal]++;
                                    if (empty) noStacks[ordinal]++;
                                    unresolved[ordinal] += unknown;
                                    if (observer) observedBytes[ordinal] += Math.Max(0, amount);
                                    string key = threadGroup + "/" + threadName + "\n" + ancestry + "\n" + resolved;
                                    Aggregate aggregate;
                                    if (!aggregates.TryGetValue(key, out aggregate))
                                    {
                                        aggregate = new Aggregate { Thread = threadGroup + "/" + threadName, Ancestry = ancestry, Stack = resolved };
                                        aggregates.Add(key, aggregate);
                                    }
                                    aggregate.Samples++; aggregate.Bytes += Math.Max(0, amount);
                                    if (empty) aggregate.MissingStacks++;
                                    aggregate.UnresolvedAddresses += unknown;
                                }
                                int descendants = view.GetSampleChildrenCountRecursive(s);
                                if (descendants > 0) { parents.Add(s); ends.Add(s + descendants); }
                            }
                        }
                    }
                    int next = ProfilerDriver.GetNextFrameIndex(frame);
                    if (next <= frame) break;
                    frame = next;
                }
            }
            using (var output = new StreamWriter(prefix + "-frames.csv", false, new UTF8Encoding(false)))
            {
                output.WriteLine("window,ordinal,unityFrame,profilerFrame0,profilerUIFrame1,mappingCount,profilerFrameCollision,realtimeStart,realtimeEnd,intervalMs,profilerFrameMs,tickStart,tickEnd,gameTimeStart,gameTimeEnd,flowStart,flowEnd,levelStart,levelEnd,versionStart,versionEnd,killsStart,killsEnd,hpStart,hpEnd,gcMaxGeneration,gc0Start,gc0End,gc0Delta,gc1Start,gc1End,gc1Delta,gc2Start,gc2End,gc2Delta,allocBytes,allocSamples,missingCallstacks,unresolvedAddresses,observerScopeBytes,governorPreviousFrameBytes");
                for (int i = 0; i < observed; ++i)
                {
                    if (mappingCount[i] == 1 && !frameCollision[i]) matched++;
                    var a = snapshots[i]; var b = snapshots[i + 1];
                    WriteCsv(output, window, i, a.UnityFrame, frames[i], frames[i] < 0 ? -1 : frames[i] + 1, mappingCount[i], frameCollision[i],
                        a.Realtime, b.Realtime, (b.Realtime - a.Realtime) * 1000, frameMs[i], a.Tick, b.Tick, a.GameTime, b.GameTime,
                        (SvFlow)a.Flow, (SvFlow)b.Flow, a.Level, b.Level, a.Version, b.Version, a.Kills, b.Kills, a.Hp, b.Hp,
                        a.MaxGeneration, a.Gc0, b.Gc0, b.Gc0 - a.Gc0, a.Gc1, b.Gc1, Delta(a.Gc1, b.Gc1),
                        a.Gc2, b.Gc2, Delta(a.Gc2, b.Gc2), bytes[i], sampleCounts[i], noStacks[i], unresolved[i], observedBytes[i], a.GovernorPreviousFrameBytes);
                }
            }
            var sorted = new List<Aggregate>(aggregates.Values);
            sorted.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));
            using (var output = new StreamWriter(prefix + "-stacks.csv", false, new UTF8Encoding(false)))
            {
                output.WriteLine("bytes,samples,missingCallstacks,unresolvedAddresses,thread,ancestry,callstack");
                foreach (var group in sorted) WriteCsv(output, group.Bytes, group.Samples, group.MissingStacks, group.UnresolvedAddresses, group.Thread, group.Ancestry, group.Stack);
            }
            long total = 0, totalObserver = 0; int allocs = 0, missing = 0, unknowns = 0;
            for (int i = 0; i < observed; ++i) { total += bytes[i]; totalObserver += observedBytes[i]; allocs += sampleCounts[i]; missing += noStacks[i]; unknowns += unresolved[i]; }
            var begin = snapshots[0]; var end = snapshots[observed];
            File.WriteAllText(prefix + "-summary.txt", "window=" + window + "\nrequestedFrames=" + requestedFrames + "\nobservedFrames=" + observed
                + "\nuniquelyMatchedFrames=" + matched + "\nmissingOrAmbiguousFrames=" + (observed - matched) + "\nmetadataChunks=" + chunks
                + "\ninvalidMetadata=" + invalidOrdinals + "\nretainedProfilerFrameRange=" + first + ".." + last
                + "\nsurvived=" + survived + "\nelapsedSeconds=" + (end.Realtime - begin.Realtime).ToString("R", Invariant)
                + "\ngameSeconds=" + (end.GameTime - begin.GameTime).ToString("R", Invariant) + "\ntickRange=" + begin.Tick + ".." + end.Tick
                + "\nallocationBytesAllThreads=" + total + "\nallocationSamples=" + allocs + "\nobserverScopeBytes=" + totalObserver
                + "\nmissingCallstackSamples=" + missing + "\nunresolvedAddresses=" + unknowns
                + "\ngcMaxGeneration=" + begin.MaxGeneration + "\ngc0Delta=" + (end.Gc0 - begin.Gc0)
                + "\ngc1Delta=" + Delta(begin.Gc1, end.Gc1) + "\ngc2Delta=" + Delta(begin.Gc2, end.Gc2) + "\n");
            return matched;
        }

        static int ReadOption(string name, int defaultValue, int min, int max)
        {
            string text = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrEmpty(text)) return defaultValue;
            int value;
            if (!int.TryParse(text, NumberStyles.Integer, Invariant, out value) || value < min || value > max)
                throw new ArgumentException(name + " must be an integer between " + min + " and " + max + ".");
            return value;
        }
        static int Delta(int a, int b) { return a < 0 || b < 0 ? -1 : b - a; }
        static void WriteCsv(TextWriter writer, params object[] cells)
        {
            for (int i = 0; i < cells.Length; ++i)
            {
                if (i > 0) writer.Write(',');
                string value = Convert.ToString(cells[i], Invariant) ?? "";
                writer.Write('"'); writer.Write(value.Replace("\"", "\"\"")); writer.Write('"');
            }
            writer.WriteLine();
        }
        static string SafeName(string value)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
            return value.Replace('/', '_').Replace('\\', '_');
        }
        static string Settings(string label, bool includeEditor, int repeats, int requestedFrames, int memoryMiB)
        {
            return "label=" + label + "\nunity=" + Application.unityVersion + "\nplatform=" + Application.platform
                + "\ngraphics=" + SystemInfo.graphicsDeviceType + "\ndevice=" + SystemInfo.graphicsDeviceName
                + "\nframesPerWindow=" + requestedFrames + "\nrepeatsPerTier=" + repeats + "\nincludeEditor=" + includeEditor + "\nseed=3\nspawnPerSecond=8\nwarmupSeconds>=6"
                + "\nautoPlay=true\nHUD=true\nfeedback=preserved\nHP=unaltered\nlevelupHandling=normalAutoPlay"
                + "\ntargetFrameRate=" + Application.targetFrameRate + "\nvSync=" + QualitySettings.vSyncCount
                + "\nprofileEditor=" + ProfilerDriver.profileEditor + "\ndeepProfiling=" + ProfilerDriver.deepProfiling
                + "\nallocationCallstacks=" + Profiler.enableAllocationCallstacks + "\nmemoryRecordMode=" + ProfilerDriver.memoryRecordMode
                + "\nCPUArea=" + ProfilerDriver.IsAreaEnabled(ProfilerArea.CPU) + "\nMemoryArea=" + ProfilerDriver.IsAreaEnabled(ProfilerArea.Memory)
                + "\nmaxUsedMemory=" + Profiler.maxUsedMemory + "\nrequestedProfilerMemoryMiB=" + memoryMiB + "\nframeHistory=" + Math.Max(300, requestedFrames + 100)
                + "\nFrame matching: emitted metadata contains window, ordinal and Time.frameCount; matched without guessed offsets; distinct ordinals sharing a profiler frame are rejected."
                + "\nGC.CollectionCount deltas: process-wide Editor counters at consecutive coroutine-phase boundaries; unsupported generations are -1; never sum generations."
                + "\nAllocations: complete stamped profiler frames/all recorded threads, including Editor safety checks and test runner; includeEditor additionally records EditorLoop. GC intervals share coroutine phase, not exact profiler frame edges."
                + "\nProfiler frame indices are 0-based; UI displays index+1. GovernorPreviousFrameBytes intentionally remains labelled previous-frame and is not alignment evidence."
                + "\nFull callstacks retain raw addresses. Unresolved addresses and absent stacks are not classified as engine-owned. Source lines identify method definitions, not exact allocation lines."
                + "\nObserver: preallocated NativeArray/Stamp[]; fixed-name marker + counter reads + metadata emission. All CSV/grouping/symbol resolution occurs after capture."
                + "\nProfiler.maxUsedMemory limits the profiling stream buffer, not total Editor process memory or all retained frame data."
                + "\nCapture overhead still includes allocation callstack recording; do not use this run as a production frame-time benchmark.\n";
        }
    }
}
#endif
