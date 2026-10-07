using System;
using System.Reflection;
using System.Collections;
using System.Security.Cryptography;
using SPF.Runtime.Session;
using SPF.Runtime.Composition;
using NUnit.Framework;
using Unity.Mathematics;

namespace SnakeFoundation.Tests
{
    public class SnakeConfigIsolationTests
    {
        [Test]
        public void SourceEditsAfterSessionCreationDoNotChangeRuntimeOrReplay()
        {
            using var edited = new SnakeTestWorld(aiPerRegion: 4, foodPerChunk: 2);
            using var control = new SnakeTestWorld(aiPerRegion: 4, foodPerChunk: 2);
            var source = edited.Config;
            source.Capacity.ChunkSize = 1f;
            source.Capacity.Snakes = 1;
            source.Movement.BaseSpeed = 99f;
            source.Body.MaxLength = 1f;
            source.Buffs[0].Value = 100f;
            source.Props[0].Radius = 100f;
            source.Regions[0].Min = new UnityEngine.Vector2(0, 0);
            source.Portals[0].ToRegion = 0;
            source.Skins[0].Alpha = 0.1f;
            source.AINames[0] = "edited";
            Assert.AreEqual(control.Runtime.Capacity.ChunkSize, edited.Runtime.Capacity.ChunkSize);
            Assert.AreEqual(control.Runtime.Capacity.Snakes, edited.Runtime.Capacity.Snakes);
            Assert.AreEqual(control.Runtime.Settings.BaseSpeed, edited.Runtime.Settings.BaseSpeed);
            Assert.AreEqual(control.Runtime.Buffs[0].Value, edited.Runtime.Buffs[0].Value);
            Assert.AreEqual(control.Runtime.Props[0].Radius, edited.Runtime.Props[0].Radius);
            Assert.AreEqual(control.Runtime.Regions[0].Min, edited.Runtime.Regions[0].Min);
            Assert.AreEqual(control.Runtime.Portals[0].ToRegion, edited.Runtime.Portals[0].ToRegion);
            Assert.AreEqual(control.Runtime.Skins[0].Alpha, edited.Runtime.Skins[0].Alpha);
            Assert.AreEqual(control.Runtime.Names[0], edited.Runtime.Names[0]);
            CollectionAssert.AreEqual(SaveCompatibilityDescriptor.Encode(control.Runtime.WriteContent),
                SaveCompatibilityDescriptor.Encode(edited.Runtime.WriteContent));
            edited.StartPlayer(); control.StartPlayer();
            edited.Step(15); control.Step(15);
            CollectionAssert.AreEqual(control.Session.CaptureSnapshot(), edited.Session.CaptureSnapshot());
        }

        static SnakeRuntimeConfig BakeWithCheckpoint(SnakeConfig source, Action<SnakeRuntimeConfig> allocated)
        {
            return (SnakeRuntimeConfig)typeof(SnakeRuntimeConfig).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(SnakeConfig), typeof(Action<SnakeRuntimeConfig>) }, null).Invoke(new object[] { source, allocated });
        }

        [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)]
        public void PartialNativeConstructionReleasesExactlyTheAllocatedPrefix(int stopAfter)
        {
            var source = SnakeConfig.CreateDefault();
            SnakeRuntimeConfig partial = null;
            int checkpoints = 0;
            try
            {
                var error = Assert.Throws<TargetInvocationException>(() => BakeWithCheckpoint(source, value =>
                {
                    partial = value;
                    if (++checkpoints == stopAfter) throw new InvalidOperationException("injected after native allocation");
                }));
                Assert.IsInstanceOf<InvalidOperationException>(error.InnerException);
                Assert.AreEqual(stopAfter, checkpoints);
                Assert.IsFalse(partial.Buffs.IsCreated, "Buffs");
                Assert.IsFalse(partial.Props.IsCreated, "Props");
                Assert.IsFalse(partial.Regions.IsCreated, "Regions");
                Assert.IsFalse(partial.Portals.IsCreated, "Portals");
                partial.Dispose(); // Repeated cleanup must be safe.
                using var retry = source.Bake();
                Assert.IsTrue(retry.Buffs.IsCreated);
            }
            finally { partial?.Dispose(); UnityEngine.Object.DestroyImmediate(source); }
        }

        [Test]
        public void EveryAuthoredFloatRejectsNaNBeforeNativeAllocation()
        {
            var source = SnakeConfig.CreateDefault();
            try
            {
                foreach (var field in typeof(SnakeConfig).GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    object section = field.GetValue(source);
                    if (section is IList entries)
                    {
                        foreach (var entry in entries) if (entry != null && !(entry is string)) CheckFloats(source, entry);
                    }
                    else if (section != null) CheckFloats(source, section);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }

        static void CheckFloats(SnakeConfig source, object section)
        {
            foreach (var field in section.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.FieldType != typeof(float)) continue;
                object previous = field.GetValue(section);
                field.SetValue(section, float.NaN);
                int allocations = 0;
                try
                {
                    var error = Assert.Throws<TargetInvocationException>(() => BakeWithCheckpoint(source, value => allocations++), field.Name);
                    Assert.IsInstanceOf<ArgumentException>(error.InnerException, field.Name);
                    Assert.AreEqual(0, allocations, field.Name);
                }
                finally { field.SetValue(section, previous); }
            }
        }

        [Test]
        public void SnapshotHasIndependentMutableRuntimeObjectsAndStableContentInput()
        {
            var source = SnakeConfig.CreateDefault();
            try
            {
                using var first = source.Bake();
                using var second = source.Bake();
                var encoded = SaveCompatibilityDescriptor.Encode(first.WriteContent);
                var identity = Identity(encoded);
                CollectionAssert.AreEqual(encoded, SaveCompatibilityDescriptor.Encode(second.WriteContent));
                Assert.AreEqual(identity, Identity(SaveCompatibilityDescriptor.Encode(second.WriteContent)));
                first.Capacity.Snakes++;
                Assert.AreEqual(source.Capacity.Snakes, second.Capacity.Snakes);
                Assert.AreNotEqual(first.Capacity.Snakes, second.Capacity.Snakes);
                Assert.AreNotEqual(identity, Identity(SaveCompatibilityDescriptor.Encode(first.WriteContent)));
                Assert.AreEqual(identity, Identity(SaveCompatibilityDescriptor.Encode(second.WriteContent)));
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }

        static string Identity(byte[] content) => new SaveCompatibilityDescriptor("snake.test", "source-snapshot.v1", "test-runtime",
            Array.Empty<byte>(), content, Array.Empty<byte>(), Array.Empty<byte>()).ContentFingerprint;

        [Test]
        public void CopiedAuthoringSectionsContainOnlyValueFields()
        {
            foreach (var field in typeof(SnakeConfig.CapacitySection).GetFields(BindingFlags.Public | BindingFlags.Instance))
                Assert.IsTrue(field.FieldType.IsPrimitive || field.FieldType.IsEnum, "Add explicit deep copy for " + field.Name);
        }

#if SPF_DOTNET_HARNESS
        [Test, Explicit("Config-only .NET diagnostic; not a Unity or mobile allocation/peak-memory measurement.")]
        public void RecordConfigColdAllocationAndRetainedPayload()
        {
            var source = SnakeConfig.CreateDefault();
            const int count = 128;
            var retained = new SnakeRuntimeConfig[count];
            try
            {
                using (var warmup = source.Bake()) { }
                using var probe = new SPF.Testing.ManagedAllocationProbe();
                Action build = () => { for (int i = 0; i < count; i++) retained[i] = source.Bake(); };
                var before = probe.Calibrate();
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                long heapBefore = GC.GetTotalMemory(true);
                var sample = probe.Measure(build);
                long liveHeap = GC.GetTotalMemory(false) - heapBefore;
                long retainedHeap = GC.GetTotalMemory(true) - heapBefore;
                var after = probe.Calibrate();
                var value = retained[0];
                long nativePayload = 0;
                nativePayload += Payload(value.Buffs);
                nativePayload += Payload(value.Props);
                nativePayload += Payload(value.Regions);
                nativePayload += Payload(value.Portals);
                TestContext.WriteLine($"A3_CONFIG_MEASURE count={count} managedAllocatedBytes={sample.Value} managedLiveDeltaBeforeCollectionBytes={liveHeap} managedRetainedDeltaBytes={retainedHeap} nativePayloadBytesPerConfig={nativePayload}");
                TestContext.WriteLine($"A3_CONTROLS metric={sample.Metric} gen0={sample.Collections} positiveBefore={before.RetainedArrays.Value} emptyBefore={before.Empty.Value} positiveAfter={after.RetainedArrays.Value} emptyAfter={after.Empty.Value}");
                GC.KeepAlive(retained);
            }
            finally
            {
                foreach (var value in retained) value?.Dispose();
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        static long Payload<T>(Unity.Collections.NativeArray<T> values) where T : unmanaged =>
            (long)values.Length * Unity.Collections.LowLevel.Unsafe.UnsafeUtility.SizeOf<T>();

        [Test, Explicit("Exact same-runtime raw snapshot comparison for the A3 baseline and candidate.")]
        public void RecordDefaultRawSnapshotDigest()
        {
            using var session = new SnakeTestWorld(aiPerRegion: 4, foodPerChunk: 2);
            session.StartPlayer(); session.Step(15);
            var raw = session.Session.CaptureSnapshot();
            using var hash = SHA256.Create();
            TestContext.WriteLine($"A3_RAW length={raw.Length} sha256={BitConverter.ToString(hash.ComputeHash(raw)).Replace("-", "").ToLowerInvariant()}");
        }
#endif

        [TestCase("missing section")]
        [TestCase("missing entry")]
        [TestCase("portal reference")]
        [TestCase("buff reference")]
        [TestCase("trail overflow")]
        [TestCase("event overflow")]
        [TestCase("grid overflow")]
        [TestCase("region overflow")]
        [TestCase("combined head-grid overflow")]
        [TestCase("empty regions")]
        [TestCase("invalid slab")]
        [TestCase("infinity")]
        public void InvalidSourceFailsBeforeNativeAllocation(string scenario)
        {
            var source = SnakeConfig.CreateDefault();
            switch (scenario)
            {
                case "missing section": source.Capacity = null; break;
                case "missing entry": source.Skins[0] = null; break;
                case "portal reference": source.Portals[0].ToRegion = source.Regions.Count; break;
                case "buff reference": source.Props[0].BuffKind = 256; break;
                case "trail overflow": source.Capacity.AverageTrailPoints = int.MaxValue; break;
                case "event overflow": source.Capacity.EventQueue = int.MaxValue; break;
                case "grid overflow": source.Capacity.GridCells = int.MaxValue; break;
                case "region overflow": source.Regions[0].Max = new UnityEngine.Vector2(float.MaxValue, float.MaxValue); break;
                case "combined head-grid overflow":
                    source.Capacity.ChunkSize = 1f;
                    source.Regions[0].Min = source.Regions[1].Min = new UnityEngine.Vector2(0, 0);
                    source.Regions[0].Max = new UnityEngine.Vector2(10000000, 1);
                    source.Regions[1].Max = new UnityEngine.Vector2(1, 10000000);
                    break;
                case "empty regions": source.Regions.Clear(); break;
                case "invalid slab": source.Capacity.MaxTrailPoints = 17; break;
                case "infinity": source.Capacity.ChunkSize = float.PositiveInfinity; break;
            }
            try
            {
                int allocations = 0;
                var error = Assert.Throws<TargetInvocationException>(() => BakeWithCheckpoint(source, value => allocations++));
                Assert.IsInstanceOf<ArgumentException>(error.InnerException);
                Assert.AreEqual(0, allocations);
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }

        [Test]
        public void ExistingClampsAndEmptyListFallbacksArePreserved()
        {
            var source = SnakeConfig.CreateDefault();
            source.Buffs.Clear(); source.Props.Clear(); source.Portals.Clear(); source.Skins.Clear(); source.AINames.Clear();
            source.Skill.HitBuffKind = 0;
            source.Movement.BoostDropMass = 0;
            source.Body.TrailSpacing = 0; source.Body.TrailSpacingPerRadius = -1;
            source.AI.DecisionIntervalTicks = 0; source.AI.SpawnRingMax = 0;
            source.Capacity.BodyGridCellSize = -1; // Existing fallback to GridCellSize.
            try
            {
                using var config = source.Bake();
                Assert.AreEqual(1, config.Buffs.Length); Assert.AreEqual(1, config.Props.Length);
                Assert.AreEqual(0, config.PropCount); Assert.AreEqual(0, config.PortalCount);
                Assert.AreEqual(1, config.Skins.Length); Assert.AreEqual("Snake", config.Names[0]);
                Assert.AreEqual(0.1f, config.Settings.BoostDropMass);
                Assert.AreEqual(0.05f, config.Settings.TrailSpacing);
                Assert.AreEqual(0, config.Settings.TrailSpacingPerRadius);
                Assert.AreEqual(1, config.Settings.AIDecisionIntervalTicks);
                Assert.AreEqual(source.AI.SpawnRingMin, config.Settings.SpawnRingMax);
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }

        [Test]
        public void TwoSessionsFromOneSourceKeepIndependentUnchangedSnapshots()
        {
            var source = SnakeConfig.CreateDefault();
            var module = SnakeGameModule.Create(source);
            try
            {
                using var first = new SimSession(new IGameplayModule[] { module }, SessionSettings.Default, 42);
                using var second = new SimSession(new IGameplayModule[] { module }, SessionSettings.Default, 42);
                first.Start(); second.Start();
                var a = first.World.Resource(SnakeKeys.Config); var b = second.World.Resource(SnakeKeys.Config);
                var original = SaveCompatibilityDescriptor.Encode(a.WriteContent);
                CollectionAssert.AreEqual(original, SaveCompatibilityDescriptor.Encode(b.WriteContent));
                source.Capacity.ChunkSize = 1; source.Capacity.Snakes = 1;
                source.Regions[0].Name = "edited"; source.Buffs[0].Duration = 99;
                Assert.AreNotSame(a.Capacity, b.Capacity); Assert.AreNotSame(source.Capacity, a.Capacity);
                CollectionAssert.AreEqual(original, SaveCompatibilityDescriptor.Encode(a.WriteContent));
                CollectionAssert.AreEqual(original, SaveCompatibilityDescriptor.Encode(b.WriteContent));
            }
            finally { UnityEngine.Object.DestroyImmediate(module); UnityEngine.Object.DestroyImmediate(source); }
        }
    }
}
