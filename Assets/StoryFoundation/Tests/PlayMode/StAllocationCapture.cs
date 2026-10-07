#if UNITY_EDITOR && !SPF_DOTNET_HARNESS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using StoryFoundation.Game;
using Unity.Collections;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Profiling;

namespace StoryFoundation.Tests.PlayMode
{
    /// <summary>
    /// Opt-in evidence from the actual second-line regression window. No timing, presentation,
    /// audio, gameplay or budget changes. All extraction and formatting happen after the window.
    /// </summary>
    sealed class StAllocationCapture : IDisposable
    {
        const string ObserverMarker = "StoryGC.CaptureObserver";
        const int Tag = 1;
        static readonly Guid StampId = new Guid("92ef7680-e5d3-4e78-abec-d8bd95b0bc98");
        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        struct Stamp
        {
            public int Ordinal, UnityFrame, Serial, Visible, Length, BodyMeshRebuilds, Typing, Choices, Sprites, Quality;
            public int GovernorFrames, GovernorAllocatingFrames, Gc0, Gc1, Gc2;
            public long GovernorPreviousFrameBytes, GovernorWindowBytes;
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
        readonly Stamp[] m_Snapshots = new Stamp[31];
        NativeArray<Stamp> m_Metadata;
        readonly string m_Prefix;
        int m_Observed;
        bool m_Disposed;

        public static StAllocationCapture CreateIfRequested()
        {
            return Environment.GetEnvironmentVariable("SPF_STORY_GC_CAPTURE") == "1"
                ? new StAllocationCapture() : null;
        }

        StAllocationCapture()
        {
            // Setup occurs before the game and original first-line warmup; never in its budget window.
            for (int i = 0; i < m_Areas.Length; i++) m_Areas[i] = ProfilerDriver.IsAreaEnabled((ProfilerArea)i);
            var settings = typeof(ProfilerDriver).Assembly.GetType("UnityEditor.Profiling.ProfilerUserSettings", true);
            m_History = settings.GetProperty("frameCount", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (m_History == null) throw new NotSupportedException("No Unity 2022.3 Profiler frame-count property.");
            m_HistoryCount = (int)m_History.GetValue(null, null);
            m_HadHistoryPreference = EditorPrefs.HasKey("Profiler.FrameCount");
            m_HistoryPreference = EditorPrefs.GetInt("Profiler.FrameCount", 300);
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Artifacts", "GC",
                "story-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ", Invariant)));
            Directory.CreateDirectory(root);
            m_Prefix = Path.Combine(root, "second-line");
            try
            {
                m_Metadata = new NativeArray<Stamp>(1, Allocator.Persistent);
                Stop();
                Profiler.enableBinaryLog = false;
                ProfilerDriver.profileEditor = Environment.GetEnvironmentVariable("SPF_STORY_GC_INCLUDE_EDITOR") == "1";
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
                    + "\nincludeEditor=" + ProfilerDriver.profileEditor + "\ndeepProfiling=false\nallocationCallstacks=true"
                    + "\nmaxUsedMemory=67108864\nframeHistory=300\noriginalFrameLimit=30\noriginalAllocatingFrameBudget=1"
                    + "\nNormal UI, scene, audio, frame-rate/quality policies and original first-line warmup are preserved."
                    + "\nEvery allocation in each uniquely stamped complete profiler frame is exported, on every recorded thread."
                    + "\nNo observer/test-runner/Editor/UI/transition sample is subtracted from the budget or exported totals."
                    + "\nGovernor fields are snapshots of its previous-frame counter, not claimed profiler-frame alignment."
                    + "\nGC generation counters are process-wide coroutine-boundary observations, not allocation counts or pause times."
                    + "\nUnknown addresses and absent stacks are retained, never classified as engine-owned."
                    + "\nCallstack recording is intrusive; compare with an uninstrumented regression before claiming improvement.\n");
                ProfilerDriver.ClearAllFrames();
                ProfilerDriver.enabled = true;
                Profiler.enabled = true;
            }
            catch { Dispose(); throw; }
        }

        static void Stop() { Profiler.enabled = false; ProfilerDriver.enabled = false; }

        static Stamp Read(StGameBootstrap game, int ordinal)
        {
            var governor = game.Governor;
            return new Stamp {
                Ordinal = ordinal, UnityFrame = Time.frameCount, Realtime = Time.realtimeSinceStartupAsDouble,
                Serial = game.State.Runner.Serial, Visible = game.Dialogue.BodyText.MaxVisible,
                Length = game.Dialogue.BodyText.Length, BodyMeshRebuilds = game.Dialogue.BodyText.Rebuilds,
                Typing = game.Dialogue.Typing ? 1 : 0, Choices = game.Dialogue.ChoicesShown, Sprites = game.Renderer.SpritesDrawn, Quality = governor.Level,
                GovernorFrames = governor.FramesSinceReset, GovernorAllocatingFrames = governor.GcFramesSinceReset,
                GovernorPreviousFrameBytes = governor.GcBytesLastFrame, GovernorWindowBytes = governor.GcBytesSinceReset,
                Gc0 = GC.CollectionCount(0), Gc1 = GC.MaxGeneration >= 1 ? GC.CollectionCount(1) : -1,
                Gc2 = GC.MaxGeneration >= 2 ? GC.CollectionCount(2) : -1
            };
        }

        // Called once before the first-line warmup so metadata, marker and counter paths are warmed too.
        public void Warm(StGameBootstrap game)
        {
            m_Observed = -1;
            Frame(game);
            m_Observed = 0;
        }

        void Emit(StGameBootstrap game, int ordinal)
        {
            Profiler.BeginSample(ObserverMarker);
            var stamp = Read(game, ordinal);
            m_Metadata[0] = stamp;
            Profiler.EmitFrameMetaData(StampId, Tag, m_Metadata);
            if (ordinal >= 0) m_Snapshots[ordinal] = stamp;
            Profiler.EndSample();
        }

        public void Frame(StGameBootstrap game)
        {
            Emit(game, m_Observed);
            m_Observed++;
        }

        public void End(StGameBootstrap game) => m_Snapshots[m_Observed] = Read(game, m_Observed);

        // The fixture yields only AFTER saving the original budget result, letting the final captured
        // frame reach the profiler receiver. These drain frames are not added to the original window.
        public void Export()
        {
            Stop();
            ProfilerDriver.SaveProfile(m_Prefix + ".raw");
            int matched = Extract();
            Debug.Log("Story allocation capture: " + m_Prefix + " (" + matched + "/" + m_Observed + " uniquely mapped frames)");
            if (m_Observed == 0 || matched != m_Observed)
                throw new InvalidOperationException("Incomplete Story profiler frame mapping; inspect retained raw/CSV evidence.");
        }

        int Extract()
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
                        if (gc == FrameDataView.invalidMarkerId) continue;
                        parents.Clear(); ends.Clear();
                        for (int s = 0; s < view.sampleCount; s++)
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
                                    if (method == "<unresolved>") unresolved++;
                                    if (stack.Length > 0) stack.Append(" | ");
                                    stack.Append("0x").Append(address.ToString("X", Invariant)).Append(' ').Append(method);
                                }
                                bool noStack = addresses.Count == 0;
                                if (noStack) stack.Append("<no recorded callstack>");
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
            long totalBytes = 0, totalObserver = 0;
            int totalSamples = 0, totalMissing = 0, totalUnknown = 0;
            using (var output = new StreamWriter(m_Prefix + "-frames.csv", false, new UTF8Encoding(false)))
            {
                Csv(output, "ordinal", "unityFrame", "profilerFrame0", "profilerUIFrame1", "mappingCount", "collision", "profilerFrameMs",
                    "realtimeStart", "realtimeEnd", "serial", "visibleStart", "visibleEnd", "length", "bodyRebuildsStart", "bodyRebuildsEnd",
                    "typingStart", "typingEnd", "choicesStart", "choicesEnd", "spritesStart", "spritesEnd", "qualityStart", "qualityEnd", "governorFramesStart", "governorFramesEnd",
                    "governorAllocatingFramesStart", "governorAllocatingFramesEnd", "governorPreviousFrameBytesAtStart", "governorPreviousFrameBytesAtEnd",
                    "governorWindowBytesStart", "governorWindowBytesEnd", "gc0Start", "gc0End", "gc1Start", "gc1End", "gc2Start", "gc2End",
                    "allThreadAllocBytes", "allocSamples", "missingCallstacks", "unresolvedAddresses", "observerScopeBytes");
                for (int i = 0; i < m_Observed; i++)
                {
                    if (mappings[i] == 1 && !collisions[i]) matched++;
                    var a = m_Snapshots[i]; var b = m_Snapshots[i + 1];
                    Csv(output, i, a.UnityFrame, frames[i], frames[i] < 0 ? -1 : frames[i] + 1, mappings[i], collisions[i], frameMs[i],
                        a.Realtime, b.Realtime, a.Serial, a.Visible, b.Visible, a.Length, a.BodyMeshRebuilds, b.BodyMeshRebuilds,
                        a.Typing, b.Typing, a.Choices, b.Choices, a.Sprites, b.Sprites, a.Quality, b.Quality, a.GovernorFrames, b.GovernorFrames,
                        a.GovernorAllocatingFrames, b.GovernorAllocatingFrames, a.GovernorPreviousFrameBytes, b.GovernorPreviousFrameBytes,
                        a.GovernorWindowBytes, b.GovernorWindowBytes, a.Gc0, b.Gc0, a.Gc1, b.Gc1, a.Gc2, b.Gc2,
                        bytes[i], samples[i], missing[i], unknown[i], observerBytes[i]);
                    totalBytes += bytes[i]; totalObserver += observerBytes[i]; totalSamples += samples[i];
                    totalMissing += missing[i]; totalUnknown += unknown[i];
                }
            }
            var sorted = new List<Aggregate>(aggregates.Values);
            sorted.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));
            using (var output = new StreamWriter(m_Prefix + "-stacks.csv", false, new UTF8Encoding(false)))
            {
                Csv(output, "bytes", "samples", "missingCallstacks", "unresolvedAddresses", "thread", "ancestry", "callstack");
                foreach (var a in sorted) Csv(output, a.Bytes, a.Samples, a.MissingStacks, a.UnknownAddresses, a.Thread, a.Ancestry, a.Stack);
            }
            var begin = m_Snapshots[0]; var end = m_Snapshots[m_Observed];
            File.WriteAllText(m_Prefix + "-summary.txt", "observedFrames=" + m_Observed + "\nuniquelyMatchedFrames=" + matched
                + "\ninvalidMetadata=" + invalid + "\nretainedProfilerFrameRange=" + first + ".." + last
                + "\ngovernorFrames=" + end.GovernorFrames + "\ngovernorAllocatingFrames=" + end.GovernorAllocatingFrames
                + "\ngovernorBytes=" + end.GovernorWindowBytes + "\nallocationBytesAllThreads=" + totalBytes
                + "\nallocationSamples=" + totalSamples + "\nobserverScopeBytes=" + totalObserver
                + "\nmissingCallstackSamples=" + totalMissing + "\nunresolvedAddresses=" + totalUnknown
                + "\ngcMaxGeneration=" + GC.MaxGeneration + "\ngc0Delta=" + (end.Gc0 - begin.Gc0)
                + "\ngc1Delta=" + Delta(begin.Gc1, end.Gc1) + "\ngc2Delta=" + Delta(begin.Gc2, end.Gc2) + "\n");
            return invalid == 0 ? matched : -1;
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
            Stop();
            if (m_Metadata.IsCreated) m_Metadata.Dispose();
            Profiler.enableAllocationCallstacks = m_Stacks;
            ProfilerDriver.memoryRecordMode = m_RecordMode;
            ProfilerDriver.profileEditor = m_Editor;
            ProfilerDriver.deepProfiling = m_Deep;
            Profiler.logFile = m_Log;
            Profiler.enableBinaryLog = m_Binary;
            Profiler.maxUsedMemory = m_Memory;
            for (int i = 0; i < m_Areas.Length; i++) ProfilerDriver.SetAreaEnabled((ProfilerArea)i, m_Areas[i]);
            m_History.SetValue(null, m_HistoryCount, null);
            if (m_HadHistoryPreference) EditorPrefs.SetInt("Profiler.FrameCount", m_HistoryPreference);
            else EditorPrefs.DeleteKey("Profiler.FrameCount");
            ProfilerDriver.enabled = m_DriverEnabled;
            Profiler.enabled = m_Enabled;
        }
    }
}
#endif
