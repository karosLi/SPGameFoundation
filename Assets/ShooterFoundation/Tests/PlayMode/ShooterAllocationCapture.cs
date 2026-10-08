#if UNITY_EDITOR && !SPF_DOTNET_HARNESS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using ShooterFoundation.Game;
using SPF.Presentation;
using SPF.Presentation.Audio;
using Unity.Burst;
using Unity.Collections;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Profiling;

namespace ShooterFoundation.Tests.PlayMode
{
    /// <summary>Intrusive, default-off native diagnostic; never substitutes for the ordinary gate.</summary>
    sealed class ShooterAllocationCapture : IDisposable
    {
        const string ObserverMarker = "ShooterGC.CaptureObserver";
        const int WindowFrames = 180, ControlFrames = 24, Capacity = WindowFrames + ControlFrames;
        const int Tag = 1;
        static readonly Guid StampId = new Guid("ec238609-5091-4b45-9eb2-ad9150ca7f37");
        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        struct Stamp
        {
            public int Ordinal, UnityFrame, Flow, Version, Kills, Wave, Coins, Feedback, AudioPlayed, AudioLast;
            public int Effects, Accepted, Sprites, Quality, RenderQuality, HudRebuilds;
            public int GovernorFrames, GovernorAllocatingFrames, CounterValid, Gc0, Gc1, Gc2;
            public long Tick, NextTick, UploadBytes, GovernorPreviousFrameBytes, GovernorWindowBytes;
            public float Hp, ShotTimer, WingTimer;
            public double Realtime;
        }

        sealed class Aggregate
        {
            public long Bytes;
            public int Samples, MissingStacks, UnknownAddresses;
            public string Thread, Ancestry, Stack;
        }

        readonly bool m_Enabled = Profiler.enabled, m_DriverEnabled = ProfilerDriver.enabled;
        readonly bool m_Editor = ProfilerDriver.profileEditor, m_Deep = ProfilerDriver.deepProfiling;
        readonly bool m_Stacks = Profiler.enableAllocationCallstacks, m_Binary = Profiler.enableBinaryLog;
        readonly string m_Log = Profiler.logFile;
        readonly int m_Memory = Profiler.maxUsedMemory;
        readonly ProfilerMemoryRecordMode m_RecordMode = ProfilerDriver.memoryRecordMode;
        readonly bool[] m_Areas = new bool[Profiler.areaCount];
        readonly PropertyInfo m_History;
        readonly int m_HistoryCount, m_HistoryPreference;
        readonly bool m_HadHistoryPreference;
        readonly Stamp[] m_Snapshots = new Stamp[Capacity];
        readonly Stamp[] m_Reads = new Stamp[Capacity];
        readonly ShooterGameBootstrap m_Game;
        readonly SoundPlayer m_Sound;
        readonly ShooterAllocationProbe m_Probe;
        Stamp m_End;
        bool m_GateValid;
        int m_GateFrames, m_GateAllocating, m_GateCollections;
        long m_GateBytes;
        NativeArray<Stamp> m_Metadata;
        readonly string m_Prefix;
        int m_Observed;
        bool m_Disposed;

        public static ShooterAllocationCapture CreateIfRequested(ShooterGameBootstrap game, RenderTier tier)
        {
            return Environment.GetEnvironmentVariable("SPF_SHOOTER_GC_CAPTURE") == "1"
                ? new ShooterAllocationCapture(game, tier) : null;
        }

        ShooterAllocationCapture(ShooterGameBootstrap game, RenderTier tier)
        {
            m_Game = game;
            m_Sound = game.GetComponentInChildren<SoundPlayer>();
            if (!BurstCompiler.IsEnabled || !BurstCompiler.Options.EnableBurstCompilation)
                throw new InvalidOperationException("Shooter diagnostic requires the existing enabled Burst configuration.");
            // Setup and all storage allocation occur before the original 150 warmup yields.
            for (int i = 0; i < m_Areas.Length; i++) m_Areas[i] = ProfilerDriver.IsAreaEnabled((ProfilerArea)i);
            var settings = typeof(ProfilerDriver).Assembly.GetType("UnityEditor.Profiling.ProfilerUserSettings", true);
            m_History = settings.GetProperty("frameCount", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (m_History == null) throw new NotSupportedException("No Unity 2022.3 Profiler frame-count property.");
            m_HistoryCount = (int)m_History.GetValue(null, null);
            m_HadHistoryPreference = EditorPrefs.HasKey("Profiler.FrameCount");
            m_HistoryPreference = EditorPrefs.GetInt("Profiler.FrameCount", 300);
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Artifacts", "GC",
                "shooter-" + tier + "-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ", Invariant)));
            Directory.CreateDirectory(root);
            m_Prefix = Path.Combine(root, "steady-window");
            try
            {
                m_Metadata = new NativeArray<Stamp>(1, Allocator.Persistent);
                m_Probe = game.gameObject.AddComponent<ShooterAllocationProbe>();
                m_Probe.enabled = false;
                Stop();
                Profiler.enableBinaryLog = false;
                ProfilerDriver.profileEditor = Environment.GetEnvironmentVariable("SPF_SHOOTER_GC_INCLUDE_EDITOR") == "1";
                ProfilerDriver.deepProfiling = false;
                m_History.SetValue(null, 300, null);
                if ((int)m_History.GetValue(null, null) != 300)
                    throw new InvalidOperationException("Profiler history length did not apply.");
                for (int i = 0; i < m_Areas.Length; i++)
                    ProfilerDriver.SetAreaEnabled((ProfilerArea)i, i == (int)ProfilerArea.CPU || i == (int)ProfilerArea.Memory);
                Profiler.maxUsedMemory = 64 * 1024 * 1024;
                Profiler.enableAllocationCallstacks = true;
                File.WriteAllText(m_Prefix + "-settings.txt", "unity=" + Application.unityVersion
                    + "\nplatform=" + Application.platform + "\ngraphics=" + SystemInfo.graphicsDeviceType
                    + "\ntier=" + tier + "\nburstEnabled=" + BurstCompiler.IsEnabled
                    + "\nburstCompilationEnabled=" + BurstCompiler.Options.EnableBurstCompilation
                    + "\nincludeEditor=" + ProfilerDriver.profileEditor + "\ndeepProfiling=false\nallocationCallstacks=true"
                    + "\nmaxUsedMemory=67108864\nframeHistory=300\noriginalWarmupYields=150\noriginalObservations=180"
                    + "\noriginalAllocatingFrameBudget=2\ncalibrationFrames=24\ndrainFrames=3"
                    + "\nIntrusive diagnostic only; not a full-suite or ordinary allocation acceptance result."
                    + "\nScene, input, audio, adaptive quality and the original 150/180 schedule remain active."
                    + "\nFull recorded profiler frames include work before and after the coroutine stamp."
                    + "\nAll recorded threads are exported; EditorLoop may be absent unless explicitly enabled."
                    + "\nNo observer, test runner, control or unknown allocation is subtracted."
                    + "\nCurrent gameplay state is context, not allocation attribution."
                    + "\nAllocation-to-governor lag is checked with retained Update/LateUpdate/coroutine pulses after the gate."
                    + "\nEmpty controls require zero allocations only inside their named marker, never the entire frame."
                    + "\nMissing stacks/bytes, unresolved addresses, ambiguous mapping and truncated history remain failures/unknowns."
                    + "\nCollection counts are process-wide, independent from allocation bytes and sample counts.\n");
                ProfilerDriver.ClearAllFrames();
                ProfilerDriver.enabled = true;
                Profiler.enabled = true;
            }
            catch
            {
                try { Dispose(); }
                catch (Exception restoreError) { Debug.LogError("Shooter profiler restoration also failed: " + restoreError); }
                throw;
            }
        }

        static void Stop() { Profiler.enabled = false; ProfilerDriver.enabled = false; }

        Stamp Read(int ordinal)
        {
            var governor = m_Game.Governor;
            var run = m_Game.State.Run;
            return new Stamp {
                Ordinal = ordinal, UnityFrame = Time.frameCount, Realtime = Time.realtimeSinceStartupAsDouble,
                Tick = m_Game.Session.Pipeline.Stats.TickCount, NextTick = m_Game.Session.Clock.NextTickIndex,
                Flow = (int)run.Flow, Version = run.Version, Kills = run.Kills, Wave = run.Wave,
                Hp = run.Hp, Coins = run.Coins, ShotTimer = run.ShotTimer, WingTimer = run.WingTimer,
                Feedback = m_Game.Session.World.Resource(ShooterKeys.Feedback).Count,
                AudioPlayed = m_Sound != null ? m_Sound.Played : -1, AudioLast = m_Sound != null ? m_Sound.LastPlayed : -1,
                Effects = m_Game.Renderer.ActiveEffects, Accepted = m_Game.Renderer.VfxStats.Accepted,
                Sprites = m_Game.Renderer.SpritesDrawn, UploadBytes = m_Game.Renderer.BytesUploaded,
                Quality = governor.Level, RenderQuality = m_Game.Renderer.QualityLevel, HudRebuilds = m_Game.Hud.Stats.Rebuilds,
                CounterValid = governor.GcCounterValid ? 1 : 0,
                GovernorFrames = governor.FramesSinceReset, GovernorAllocatingFrames = governor.GcFramesSinceReset,
                GovernorPreviousFrameBytes = governor.GcBytesLastFrame, GovernorWindowBytes = governor.GcBytesSinceReset,
                Gc0 = GC.CollectionCount(0), Gc1 = GC.MaxGeneration >= 1 ? GC.CollectionCount(1) : -1,
                Gc2 = GC.MaxGeneration >= 2 ? GC.CollectionCount(2) : -1
            };
        }

        public void Warm()
        {
            m_Probe.Warm();
            m_Observed = -1;
            Frame();
            Observe();
            m_Observed = 0;
        }

        void Emit(int ordinal)
        {
            Profiler.BeginSample(ObserverMarker);
            try
            {
                var stamp = Read(ordinal);
                m_Metadata[0] = stamp;
                Profiler.EmitFrameMetaData(StampId, Tag, m_Metadata);
                if (ordinal >= 0) m_Snapshots[ordinal] = stamp;
            }
            finally { Profiler.EndSample(); }
        }

        // Called before the existing yield. Metadata supplies the actual Unity/profiler association.
        public void Frame()
        {
            if (m_Observed >= Capacity) throw new InvalidOperationException("Shooter capture capacity exceeded.");
            m_Probe.CoroutinePulse();
            Emit(m_Observed++);
        }

        public void Observe() => Observe(m_Observed - 1);
        void Observe(int ordinal)
        {
            Profiler.BeginSample(ObserverMarker);
            try
            {
                var stamp = Read(ordinal);
                if (ordinal >= 0) m_Reads[ordinal] = stamp;
            }
            finally { Profiler.EndSample(); }
        }

        // Original endpoint, before any calibration, diagnostic drains or file IO.
        public void End(bool valid, int frames, int allocating, long bytes, int collections)
        {
            m_End = Read(WindowFrames);
            m_GateValid = valid; m_GateFrames = frames; m_GateAllocating = allocating;
            m_GateBytes = bytes; m_GateCollections = collections;
        }

        public void ArmControl(int control) => m_Probe.Arm(control, Time.frameCount + 1);

        public bool Export()
        {
            m_Probe.enabled = false;
            Stop();
            ProfilerDriver.SaveProfile(m_Prefix + ".raw");
            bool valid = Extract();
            Debug.Log("Shooter allocation diagnostic: " + m_Prefix + " (mapping/calibration valid=" + valid + ")");
            return valid;
        }

        bool Extract()
        {
            var frames = new int[m_Observed];
            var mappings = new int[m_Observed];
            var collisions = new bool[m_Observed];
            var bytes = new long[m_Observed];
            var samples = new int[m_Observed];
            var missing = new int[m_Observed];
            var unknown = new int[m_Observed];
            var observerBytes = new long[m_Observed];
            var frameMs = new float[m_Observed];
            for (int i = 0; i < frames.Length; i++) frames[i] = -1;
            var addresses = new List<ulong>(128);
            var parents = new List<int>(128);
            var ends = new List<int>(128);
            var methods = new Dictionary<ulong, string>();
            var aggregates = new Dictionary<string, Aggregate>();
            var stack = new StringBuilder(8192);
            var path = new StringBuilder(2048);
            int first = ProfilerDriver.firstFrameIndex, last = ProfilerDriver.lastFrameIndex, invalid = 0;
            var controlMarkers = new int[6];
            var controlSamples = new int[6];
            var controlMissingStacks = new int[6];
            var controlBytes = new long[6];
            int missingByteMetadata = 0;
            using (var output = new StreamWriter(m_Prefix + "-allocations.csv", false, new UTF8Encoding(false)))
            {
                Csv(output, "ordinal", "unityFrame", "profilerFrame0", "profilerUIFrame1", "threadIndex", "threadId",
                    "threadGroup", "threadName", "sample", "bytes", "startNs", "observerScope", "missingCallstack", "unresolvedAddresses", "ancestry", "callstack");
                for (int frame = first; frame >= 0 && frame <= last; )
                {
                    int ordinal = -1;
                    bool collision = false;
                    using (var main = ProfilerDriver.GetRawFrameDataView(frame, 0))
                    if (main.valid)
                    {
                        int count = main.GetFrameMetaDataCount(StampId, Tag);
                        for (int c = 0; c < count; c++)
                        using (var data = main.GetFrameMetaData<Stamp>(StampId, Tag, c))
                        for (int d = 0; d < data.Length; d++)
                        {
                            var stamp = data[d];
                            if (stamp.Ordinal < 0) continue;
                            if (stamp.Ordinal >= m_Observed || stamp.UnityFrame != m_Snapshots[stamp.Ordinal].UnityFrame)
                            { invalid++; continue; }
                            if (ordinal >= 0 && ordinal != stamp.Ordinal)
                            { collisions[ordinal] = true; collisions[stamp.Ordinal] = true; collision = true; }
                            ordinal = stamp.Ordinal;
                            mappings[ordinal]++;
                            frames[ordinal] = frame;
                            frameMs[ordinal] = main.frameTimeMs;
                        }
                    }
                    if (ordinal >= 0 && !collision)
                    for (int thread = 0; ; thread++)
                    using (var view = ProfilerDriver.GetRawFrameDataView(frame, thread))
                    {
                        if (!view.valid) break;
                        int gc = view.GetMarkerId("GC.Alloc");
                        parents.Clear(); ends.Clear();
                        for (int s = 0; s < view.sampleCount; s++)
                        {
                            while (ends.Count > 0 && s > ends[ends.Count - 1])
                            { parents.RemoveAt(parents.Count - 1); ends.RemoveAt(ends.Count - 1); }
                            string sampleName = view.GetSampleName(s);
                            for (int c = 0; c < 6; c++)
                                if (sampleName == ShooterAllocationProbe.Markers[c] && m_Probe.Frames[c] == m_Snapshots[ordinal].UnityFrame)
                                    controlMarkers[c]++;
                            if (gc != FrameDataView.invalidMarkerId && view.GetSampleMarkerId(s) == gc)
                            {
                                long amount = view.GetSampleMetadataCount(s) > 0 ? view.GetSampleMetadataAsLong(s, 0) : -1;
                                path.Length = 0;
                                bool observer = false;
                                int control = -1;
                                if (amount < 0) missingByteMetadata++;
                                foreach (int p in parents)
                                {
                                    string name = view.GetSampleName(p);
                                    if (name == ObserverMarker) observer = true;
                                    for (int c = 0; c < 6; c++)
                                        if (name == ShooterAllocationProbe.Markers[c] && m_Probe.Frames[c] == m_Snapshots[ordinal].UnityFrame)
                                            control = c;
                                    if (path.Length > 0) path.Append(" > ");
                                    path.Append(name);
                                }
                                addresses.Clear();
                                view.GetSampleCallstack(s, addresses);
                                stack.Length = 0;
                                int unresolved = 0;
                                foreach (ulong address in addresses)
                                {
                                    if (!methods.TryGetValue(address, out string method))
                                    {
                                        var info = view.ResolveMethodInfo(address);
                                        method = string.IsNullOrEmpty(info.methodName) ? "<unresolved>" : info.methodName;
                                        if (!string.IsNullOrEmpty(info.sourceFileName)) method += " (" + info.sourceFileName + ":" + info.sourceFileLine + ")";
                                        methods.Add(address, method);
                                    }
                                    if (method.StartsWith("<unresolved>", StringComparison.Ordinal)) unresolved++;
                                    if (stack.Length > 0) stack.Append(" | ");
                                    stack.Append("0x").Append(address.ToString("X", Invariant)).Append(' ').Append(method);
                                }
                                bool noStack = addresses.Count == 0;
                                if (noStack) stack.Append("<no recorded callstack>");
                                if (control >= 0)
                                {
                                    controlSamples[control]++;
                                    controlBytes[control] += Math.Max(0, amount);
                                    if (noStack) controlMissingStacks[control]++;
                                }
                                string ancestry = path.ToString(), resolved = stack.ToString();
                                Csv(output, ordinal, m_Snapshots[ordinal].UnityFrame, frame, frame + 1, thread, view.threadId,
                                    view.threadGroupName, view.threadName, s, amount, view.GetSampleStartTimeNs(s), observer, noStack, unresolved, ancestry, resolved);
                                bytes[ordinal] += Math.Max(0, amount); samples[ordinal]++;
                                if (noStack) missing[ordinal]++;
                                unknown[ordinal] += unresolved;
                                if (observer) observerBytes[ordinal] += Math.Max(0, amount);
                                string threadName = view.threadGroupName + "/" + view.threadName;
                                string key = threadName + "\n" + ancestry + "\n" + resolved;
                                if (!aggregates.TryGetValue(key, out var aggregate))
                                {
                                    aggregate = new Aggregate { Thread = threadName, Ancestry = ancestry, Stack = resolved };
                                    aggregates.Add(key, aggregate);
                                }
                                aggregate.Bytes += Math.Max(0, amount); aggregate.Samples++;
                                if (noStack) aggregate.MissingStacks++;
                                aggregate.UnknownAddresses += unresolved;
                            }
                            int descendants = view.GetSampleChildrenCountRecursive(s);
                            if (descendants > 0) { parents.Add(s); ends.Add(s + descendants); }
                        }
                    }
                    int next = ProfilerDriver.GetNextFrameIndex(frame);
                    if (next <= frame) break;
                    frame = next;
                }
            }
            int matched = 0;
            long totalBytes = 0, totalObserver = 0, windowBytes = 0;
            int totalSamples = 0, totalMissing = 0, totalUnknown = 0;
            using (var output = new StreamWriter(m_Prefix + "-frames.csv", false, new UTF8Encoding(false)))
            {
                Csv(output, "ordinal", "phase", "sourceUnityFrame", "profilerFrame0", "profilerUIFrame1", "mappingCount", "collision", "profilerFrameMs",
                    "coroutineStampRealtime", "nextReadRealtime", "contextTick", "contextNextTick", "contextFlow", "contextVersion",
                    "contextKills", "contextWave", "contextHp", "contextCoins", "contextShotTimer", "contextWingTimer", "contextPendingFeedback",
                    "contextAudioPlayed", "contextAudioLast", "contextEffects", "contextAcceptedEffects", "contextSprites", "contextUploadBytes",
                    "contextGovernorQuality", "contextRendererQuality", "contextHudRebuilds", "allRecordedThreadAllocBytes", "allocSamples",
                    "missingCallstacks", "unresolvedAddresses", "observerScopeBytes");
                for (int i = 0; i < m_Observed; i++)
                {
                    if (mappings[i] == 1 && !collisions[i]) matched++;
                    var a = m_Snapshots[i]; var b = m_Reads[i];
                    Csv(output, i, i < WindowFrames ? "gate-source" : "calibration-source", a.UnityFrame, frames[i], frames[i] < 0 ? -1 : frames[i] + 1,
                        mappings[i], collisions[i], frameMs[i], a.Realtime, b.Realtime, a.Tick, a.NextTick, a.Flow, a.Version,
                        a.Kills, a.Wave, a.Hp, a.Coins, a.ShotTimer, a.WingTimer, a.Feedback, a.AudioPlayed, a.AudioLast,
                        a.Effects, a.Accepted, a.Sprites, a.UploadBytes, a.Quality, a.RenderQuality, a.HudRebuilds,
                        bytes[i], samples[i], missing[i], unknown[i], observerBytes[i]);
                    totalBytes += bytes[i]; totalObserver += observerBytes[i]; totalSamples += samples[i];
                    totalMissing += missing[i]; totalUnknown += unknown[i];
                    if (i < WindowFrames) windowBytes += bytes[i];
                }
            }
            bool controlsValid = true;
            int calibratedLag = -1;
            using (var output = new StreamWriter(m_Prefix + "-controls.csv", false, new UTF8Encoding(false)))
            {
                Csv(output, "control", "marker", "sourceUnityFrame", "retainedPayloadBytes", "markerOccurrences", "markerAllocSamples", "markerAllocBytes",
                    "missingCallstacks", "candidateReadCount", "candidateReadUnityFrame", "candidateGovernorBytes", "observedLag", "valid");
                for (int c = 0; c < 6; c++)
                {
                    int payload = ShooterAllocationProbe.Payload(c);
                    int candidates = 0, readFrame = -1, lag = -1;
                    long governorBytes = -1;
                    if (payload > 0)
                        for (int i = WindowFrames; i < m_Observed; i++)
                        {
                            var read = m_Reads[i];
                            // Search both sides of the pulse; do not manufacture a frame offset.
                            int delta = read.UnityFrame - m_Probe.Frames[c];
                            if (delta < -1 || delta > 2 || read.GovernorPreviousFrameBytes < controlBytes[c] || controlBytes[c] < payload) continue;
                            candidates++; readFrame = read.UnityFrame; lag = delta; governorBytes = read.GovernorPreviousFrameBytes;
                        }
                    bool valid = m_Probe.Emissions[c] == 1 && controlMarkers[c] == 1;
                    if (payload == 0) valid &= controlSamples[c] == 0 && controlBytes[c] == 0;
                    else
                    {
                        valid &= controlSamples[c] > 0 && controlBytes[c] >= payload && controlMissingStacks[c] == 0 && candidates == 1 && lag == 1;
                        if (calibratedLag < 0) calibratedLag = lag;
                        else valid &= calibratedLag == lag;
                    }
                    controlsValid &= valid;
                    Csv(output, c, ShooterAllocationProbe.Markers[c], m_Probe.Frames[c], payload, controlMarkers[c], controlSamples[c], controlBytes[c],
                        controlMissingStacks[c], candidates, readFrame, governorBytes, lag, valid);
                }
            }
            bool observationsValid = true;
            long readByteSum = 0;
            int allocatingReadCount = 0;
            using (var output = new StreamWriter(m_Prefix + "-observations.csv", false, new UTF8Encoding(false)))
            {
                Csv(output, "ordinal", "phase", "readUnityFrame", "counterValid", "governorFramesSinceReset", "previousCompletedFrameBytes", "cumulativeBytes",
                    "calibratedSourceUnityFrame", "matchedSourceOrdinal", "matchedProfilerFrame0", "mappingValidated", "gc0", "gc1", "gc2");
                for (int i = 0; i < m_Observed; i++)
                {
                    var read = m_Reads[i];
                    int source = controlsValid ? read.UnityFrame - calibratedLag : -1;
                    int sourceOrdinal = -1;
                    for (int j = 0; j < m_Observed; j++) if (m_Snapshots[j].UnityFrame == source) { sourceOrdinal = j; break; }
                    readByteSum += read.GovernorPreviousFrameBytes;
                    if (read.GovernorPreviousFrameBytes > 0) allocatingReadCount++;
                    bool valid = controlsValid && sourceOrdinal == i && mappings[i] == 1 && !collisions[i]
                        && read.CounterValid == 1 && read.GovernorPreviousFrameBytes >= 0
                        && read.GovernorWindowBytes == readByteSum && read.GovernorAllocatingFrames == allocatingReadCount
                        && read.GovernorFrames == i + 1 && (i == 0 || read.UnityFrame == m_Reads[i - 1].UnityFrame + 1);
                    if (i == WindowFrames - 1)
                        valid &= read.GovernorFrames == m_GateFrames && read.GovernorAllocatingFrames == m_GateAllocating
                            && read.GovernorWindowBytes == m_GateBytes && (read.CounterValid == 1) == m_GateValid;
                    observationsValid &= valid;
                    Csv(output, i, i < WindowFrames ? "gate-read" : "calibration-read", read.UnityFrame, read.CounterValid, read.GovernorFrames,
                        read.GovernorPreviousFrameBytes, read.GovernorWindowBytes, source, sourceOrdinal,
                        sourceOrdinal < 0 ? -1 : frames[sourceOrdinal], valid, read.Gc0, read.Gc1, read.Gc2);
                }
            }
            var sorted = new List<Aggregate>(aggregates.Values);
            sorted.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));
            using (var output = new StreamWriter(m_Prefix + "-stacks.csv", false, new UTF8Encoding(false)))
            {
                Csv(output, "bytes", "samples", "missingCallstacks", "unresolvedAddresses", "thread", "ancestry", "callstack");
                foreach (var a in sorted) Csv(output, a.Bytes, a.Samples, a.MissingStacks, a.UnknownAddresses, a.Thread, a.Ancestry, a.Stack);
            }
            var begin = m_Snapshots[0];
            bool complete = m_Observed == Capacity && matched == Capacity && invalid == 0 && missingByteMetadata == 0
                && m_GateValid && m_GateFrames == WindowFrames && controlsValid && observationsValid;
            File.WriteAllText(m_Prefix + "-summary.txt", "diagnosticOnly=true\nmappingAndCalibrationComplete=" + complete
                + "\nattributionStatus=requires-stack-review"
                + "\nobservedSourceFrames=" + m_Observed + "\nuniquelyMatchedFrames=" + matched
                + "\ninvalidMetadata=" + invalid + "\nmissingAllocationByteMetadata=" + missingByteMetadata
                + "\nretainedProfilerFrameRange=" + first + ".." + last
                + "\ngateCounterValid=" + m_GateValid + "\ngateFrames=" + m_GateFrames
                + "\ngateAllocatingFrames=" + m_GateAllocating + "\ngateBytes=" + m_GateBytes
                + "\ngateProcessGeneration0Collections=" + m_GateCollections
                + "\ncalibrationValid=" + controlsValid + "\nobservationsValid=" + observationsValid + "\ncalibratedGovernorLag=" + calibratedLag
                + "\nallocationBytesAllRecordedThreads=" + totalBytes + "\ngateSourceAllocationBytesAllRecordedThreads=" + windowBytes
                + "\nallocationSamples=" + totalSamples + "\nobserverScopeBytes=" + totalObserver
                + "\nmissingCallstackSamples=" + totalMissing + "\nunresolvedAddresses=" + totalUnknown
                + "\ngcMaxGeneration=" + GC.MaxGeneration + "\ngateGc0Delta=" + (m_End.Gc0 - begin.Gc0)
                + "\ngateGc1Delta=" + Delta(begin.Gc1, m_End.Gc1) + "\ngateGc2Delta=" + Delta(begin.Gc2, m_End.Gc2) + "\n");
            return complete;
        }

        static int Delta(int a, int b) => a < 0 || b < 0 ? -1 : b - a;

        static void Csv(TextWriter writer, params object[] cells)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                if (i > 0) writer.Write(',');
                writer.Write('"'); writer.Write((Convert.ToString(cells[i], Invariant) ?? "").Replace("\"", "\"\"")); writer.Write('"');
            }
            writer.WriteLine();
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            Exception failure = null;
            // All steps are cold and bounded. A failed setter must not strand unrelated profiler state.
            void Restore(Action action)
            {
                try { action(); }
                catch (Exception error) { if (failure == null) failure = error; }
            }
            Restore(() => Profiler.enabled = false);
            Restore(() => ProfilerDriver.enabled = false);
            Restore(() => { if (m_Probe != null) m_Probe.enabled = false; });
            Restore(() => { if (m_Probe != null) UnityEngine.Object.Destroy(m_Probe); });
            Restore(() => { if (m_Metadata.IsCreated) m_Metadata.Dispose(); });
            Restore(() => Profiler.enableAllocationCallstacks = m_Stacks);
            Restore(() => ProfilerDriver.memoryRecordMode = m_RecordMode);
            Restore(() => ProfilerDriver.profileEditor = m_Editor);
            Restore(() => ProfilerDriver.deepProfiling = m_Deep);
            Restore(() => Profiler.logFile = m_Log);
            Restore(() => Profiler.enableBinaryLog = m_Binary);
            Restore(() => Profiler.maxUsedMemory = m_Memory);
            for (int i = 0; i < m_Areas.Length; i++)
            {
                int area = i;
                Restore(() => ProfilerDriver.SetAreaEnabled((ProfilerArea)area, m_Areas[area]));
            }
            Restore(() => m_History.SetValue(null, m_HistoryCount, null));
            Restore(() => {
                if (m_HadHistoryPreference) EditorPrefs.SetInt("Profiler.FrameCount", m_HistoryPreference);
                else EditorPrefs.DeleteKey("Profiler.FrameCount");
            });
            Restore(() => ProfilerDriver.enabled = m_DriverEnabled);
            Restore(() => Profiler.enabled = m_Enabled);
            if (failure != null) throw new InvalidOperationException("Shooter profiler state restoration was incomplete.", failure);
        }
    }
}
#endif
