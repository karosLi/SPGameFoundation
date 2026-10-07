using System;
using System.Reflection;
using System.Collections;
using System.Security.Cryptography;
using SPF.Runtime.Session;
using SPF.Runtime.Composition;
using NUnit.Framework;

namespace RpgFoundation.Tests
{
    public class RpgConfigIsolationTests
    {
        [Test]
        public void SourceEditsAfterSessionCreationDoNotChangeRuntimeOrReplay()
        {
            using var edited = new RpgTestWorld(start: false);
            using var control = new RpgTestWorld(start: false);
            var source = edited.Config;
            source.Dungeon.MonsterDensity = 0f;
            source.Dungeon.EliteXp = 100f;
            source.Loot.GearTiers = 1;
            source.Loot.DropChance = 0f;
            source.Capacity.Actors = 1;
            source.Hero.SkillSlots[0] = 5;
            source.Hero.Health = 1f;
            source.Monsters[0].Health = 1f;
            source.Weapons[0].DamageMul = 100f;
            source.Skills[0].Power = 100f;
            Assert.AreEqual(control.Runtime.Dungeon.MonsterDensity, edited.Runtime.Dungeon.MonsterDensity);
            Assert.AreEqual(control.Runtime.Loot.GearTiers, edited.Runtime.Loot.GearTiers);
            Assert.AreEqual(control.Runtime.Capacity.Actors, edited.Runtime.Capacity.Actors);
            CollectionAssert.AreEqual(control.Runtime.HeroSkillSlots, edited.Runtime.HeroSkillSlots);
            Assert.AreEqual(control.Runtime.Settings.HeroHealth, edited.Runtime.Settings.HeroHealth);
            Assert.AreEqual(control.Runtime.Monsters[0].Health, edited.Runtime.Monsters[0].Health);
            Assert.AreEqual(control.Runtime.Weapons[0].DamageMul, edited.Runtime.Weapons[0].DamageMul);
            Assert.AreEqual(control.Runtime.Skills[0].Power, edited.Runtime.Skills[0].Power);
            CollectionAssert.AreEqual(SaveCompatibilityDescriptor.Encode(control.Runtime.WriteContent),
                SaveCompatibilityDescriptor.Encode(edited.Runtime.WriteContent));
            edited.Game.Profile.Reset(99, edited.Runtime.StartPotions);
            control.Game.Profile.Reset(99, control.Runtime.StartPotions);
            edited.Game.Send(RpgCommandKind.NewGame); control.Game.Send(RpgCommandKind.NewGame);
            edited.Step(20); control.Step(20);
            CollectionAssert.AreEqual(control.Session.CaptureSnapshot(), edited.Session.CaptureSnapshot());
        }

        static RpgRuntimeConfig BakeWithCheckpoint(RpgConfig source, Action<RpgRuntimeConfig> allocated)
        {
            return (RpgRuntimeConfig)typeof(RpgRuntimeConfig).GetMethod("Bake", BindingFlags.Static | BindingFlags.NonPublic,
                null, new[] { typeof(RpgConfig), typeof(Action<RpgRuntimeConfig>) }, null).Invoke(null, new object[] { source, allocated });
        }

        [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)]
        public void PartialNativeConstructionReleasesExactlyTheAllocatedPrefix(int stopAfter)
        {
            var source = RpgConfig.CreateDefault();
            RpgRuntimeConfig partial = null;
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
                Assert.IsFalse(partial.Monsters.IsCreated, "Monsters");
                Assert.IsFalse(partial.Weapons.IsCreated, "Weapons");
                Assert.IsFalse(partial.Skills.IsCreated, "Skills");
                Assert.IsFalse(partial.CombatDecisionProgram.IsCreated, "CombatDecisionProgram");
                partial.Dispose(); // Repeated cleanup must be safe.
                using var retry = RpgRuntimeConfig.Bake(source);
                Assert.IsTrue(retry.Monsters.IsCreated);
            }
            finally { partial?.Dispose(); UnityEngine.Object.DestroyImmediate(source); }
        }

        [Test]
        public void EveryAuthoredFloatRejectsNaNBeforeNativeAllocation()
        {
            var source = RpgConfig.CreateDefault();
            try
            {
                foreach (var field in typeof(RpgConfig).GetFields(BindingFlags.Public | BindingFlags.Instance))
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

        static void CheckFloats(RpgConfig source, object section)
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
            var source = RpgConfig.CreateDefault();
            try
            {
                using var first = RpgRuntimeConfig.Bake(source);
                using var second = RpgRuntimeConfig.Bake(source);
                var encoded = SaveCompatibilityDescriptor.Encode(first.WriteContent);
                var identity = Identity(encoded);
                CollectionAssert.AreEqual(encoded, SaveCompatibilityDescriptor.Encode(second.WriteContent));
                Assert.AreEqual(identity, Identity(SaveCompatibilityDescriptor.Encode(second.WriteContent)));
                first.Capacity.Actors++;
                first.Dungeon.FinalFloor++;
                first.Loot.GearTiers++;
                first.HeroSkillSlots[0] = 0;
                Assert.AreEqual(source.Capacity.Actors, second.Capacity.Actors);
                Assert.AreEqual(source.Dungeon.FinalFloor, second.Dungeon.FinalFloor);
                Assert.AreEqual(source.Loot.GearTiers, second.Loot.GearTiers);
                CollectionAssert.AreEqual(source.Hero.SkillSlots, second.HeroSkillSlots);
                Assert.AreNotEqual(identity, Identity(SaveCompatibilityDescriptor.Encode(first.WriteContent)));
                Assert.AreEqual(identity, Identity(SaveCompatibilityDescriptor.Encode(second.WriteContent)));
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }

        static string Identity(byte[] content) => new SaveCompatibilityDescriptor("rpg.test", "source-snapshot.v1", "test-runtime",
            Array.Empty<byte>(), content, Array.Empty<byte>(), Array.Empty<byte>()).ContentFingerprint;

        [Test]
        public void CopiedAuthoringSectionsContainOnlyValueFields()
        {
            foreach (var field in typeof(RpgConfig.DungeonSection).GetFields(BindingFlags.Public | BindingFlags.Instance))
                Assert.IsTrue(field.FieldType.IsPrimitive || field.FieldType.IsEnum, "Add explicit deep copy for " + field.Name);
            foreach (var field in typeof(RpgConfig.LootSection).GetFields(BindingFlags.Public | BindingFlags.Instance))
                Assert.IsTrue(field.FieldType.IsPrimitive || field.FieldType.IsEnum, "Add explicit deep copy for " + field.Name);
            foreach (var field in typeof(RpgConfig.CapacitySection).GetFields(BindingFlags.Public | BindingFlags.Instance))
                Assert.IsTrue(field.FieldType.IsPrimitive || field.FieldType.IsEnum, "Add explicit deep copy for " + field.Name);
        }

#if SPF_DOTNET_HARNESS
        [Test, Explicit("Config-only .NET diagnostic; not a Unity or mobile allocation/peak-memory measurement.")]
        public void RecordConfigColdAllocationAndRetainedPayload()
        {
            var source = RpgConfig.CreateDefault();
            const int count = 128;
            var retained = new RpgRuntimeConfig[count];
            try
            {
                using (var warmup = RpgRuntimeConfig.Bake(source)) { }
                using var probe = new SPF.Testing.ManagedAllocationProbe();
                Action build = () => { for (int i = 0; i < count; i++) retained[i] = RpgRuntimeConfig.Bake(source); };
                var before = probe.Calibrate();
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                long heapBefore = GC.GetTotalMemory(true);
                var sample = probe.Measure(build);
                long liveHeap = GC.GetTotalMemory(false) - heapBefore;
                long retainedHeap = GC.GetTotalMemory(true) - heapBefore;
                var after = probe.Calibrate();
                var value = retained[0];
                long nativePayload = 0;
                nativePayload += Payload(value.Monsters);
                nativePayload += Payload(value.Weapons);
                nativePayload += Payload(value.Skills);
                nativePayload += Payload(value.CombatDecisionProgram);
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
            using var session = new RpgTestWorld();
            session.Step(20);
            var raw = session.Session.CaptureSnapshot();
            using var hash = SHA256.Create();
            TestContext.WriteLine($"A3_RAW length={raw.Length} sha256={BitConverter.ToString(hash.ComputeHash(raw)).Replace("-", "").ToLowerInvariant()}");
        }
#endif

        [TestCase("missing section")]
        [TestCase("missing entry")]
        [TestCase("weapon reference")]
        [TestCase("skill reference")]
        [TestCase("monster skill reference")]
        [TestCase("gear overflow")]
        [TestCase("map overflow")]
        [TestCase("destroy overflow")]
        [TestCase("grid overflow")]
        [TestCase("invalid rooms")]
        [TestCase("infinity")]
        public void InvalidSourceFailsBeforeNativeAllocation(string scenario)
        {
            var source = RpgConfig.CreateDefault();
            switch (scenario)
            {
                case "missing section": source.Dungeon = null; break;
                case "missing entry": source.Weapons[0] = null; break;
                case "weapon reference": source.Weapons[0].Kind = (WeaponKind)255; break;
                case "skill reference": source.Hero.SkillSlots[0] = 256; break;
                case "monster skill reference": source.Monsters[0].Skill = 256; break;
                case "gear overflow": source.Loot.GearTiers = int.MaxValue; break;
                case "map overflow": source.Dungeon.Width = int.MaxValue; break;
                case "destroy overflow": source.Capacity.Projectiles = int.MaxValue; break;
                case "grid overflow": source.Capacity.GridCellSize = float.Epsilon; break;
                case "invalid rooms": source.Dungeon.RoomSizeMax = source.Dungeon.Width; break;
                case "infinity": source.Hero.Speed = float.PositiveInfinity; break;
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
        public void ExistingNullSlotsAndEmptyListFallbacksArePreserved()
        {
            var source = RpgConfig.CreateDefault();
            source.Monsters.Clear(); source.Weapons.Clear(); source.Skills.Clear(); source.Hero.SkillSlots = null;
            try
            {
                using var config = RpgRuntimeConfig.Bake(source);
                Assert.AreEqual(1, config.Monsters.Length); Assert.AreEqual(1, config.Skills.Length);
                Assert.AreEqual(0, config.HeroSkillSlots.Length); Assert.AreEqual(0, config.MonsterNames.Length);
                Assert.AreEqual(0, config.SkillNames.Length); Assert.AreEqual(0, config.LootWeapons.Length);
                Assert.AreEqual(source.Loot.GearTiers + 1, config.Gear.Length);
                Assert.AreEqual(0, config.HeroLoadout(30, 0).S0);
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }

        [Test]
        public void TwoSessionsFromOneSourceKeepIndependentUnchangedSnapshots()
        {
            var source = RpgConfig.CreateDefault();
            var mode = RpgMode.Create(source, out var modules);
            try
            {
                using var first = SimSession.Create(mode, 7);
                using var second = SimSession.Create(mode, 7);
                first.Start(); second.Start();
                var a = first.World.Resource(RpgKeys.Config); var b = second.World.Resource(RpgKeys.Config);
                var original = SaveCompatibilityDescriptor.Encode(a.WriteContent);
                CollectionAssert.AreEqual(original, SaveCompatibilityDescriptor.Encode(b.WriteContent));
                source.Capacity.Actors = 1; source.Dungeon.MonsterDensity = 0;
                source.Loot.GearTiers = 1; source.Hero.SkillSlots[0] = 0;
                Assert.AreNotSame(a.Capacity, b.Capacity); Assert.AreNotSame(a.Dungeon, b.Dungeon);
                Assert.AreNotSame(a.Loot, b.Loot); Assert.AreNotSame(a.HeroSkillSlots, b.HeroSkillSlots);
                CollectionAssert.AreEqual(original, SaveCompatibilityDescriptor.Encode(a.WriteContent));
                CollectionAssert.AreEqual(original, SaveCompatibilityDescriptor.Encode(b.WriteContent));
            }
            finally
            {
                foreach (var module in modules) UnityEngine.Object.DestroyImmediate(module);
                UnityEngine.Object.DestroyImmediate(mode); UnityEngine.Object.DestroyImmediate(source);
            }
        }
    }
}
