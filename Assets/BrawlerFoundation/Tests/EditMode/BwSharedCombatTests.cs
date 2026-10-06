using System;
using System.IO;
using NUnit.Framework;
using SPF.Testing;
using SPF.Contracts;
using SPF.L2.Combat;
using Unity.Collections;
using Unity.Mathematics;

namespace BrawlerFoundation.Tests
{
    public class BwSharedCombatTests
    {
        static BwTestWorld World(int fighters = 64, int targets = 64) =>
            new BwTestWorld(sharedCombat: new BwSharedCombatConfig { Fighters = fighters, TargetsPerAttack = targets });

        static EntityHandle Target(BwTestWorld t, float x = .9f)
        {
            BwSpawner.Spawn(t.World, 1, new float2(x, 0), -1, 0);
            int row = t.Count - 1;
            var f = t.Info(row); f.Hp = f.MaxHp = 100; t.World.Column(BwKeys.Info).Set(row, f);
            return t.World.Table(BwKeys.Fighter).Handles[row];
        }

        static float Hp(BwTestWorld t, EntityHandle target)
        {
            Assert.IsTrue(t.World.Registry.TryResolve(target, out _, out int row));
            return t.Info(row).Hp;
        }

        static EntityHandle CorpseScenario(BwTestWorld t, bool corpseFirst = false)
        {
            t.World.ClearLevel();
            if (!corpseFirst) BwSpawner.Spawn(t.World, 0, float2.zero, 1, 0);
            int corpseRow = t.Count;
            BwSpawner.Spawn(t.World, 1, new float2(-6, 0), -1, 0);
            var corpse = t.Info(corpseRow); corpse.Hp = 0; corpse.State = FighterState.KO; corpse.StateTime = 1.5f;
            t.World.Column(BwKeys.Info).Set(corpseRow, corpse);
            var target = Target(t);
            if (corpseFirst) BwSpawner.Spawn(t.World, 0, float2.zero, 1, 0);
            t.Game.Flow = BwFlow.Fighting;
            return target;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NormalCorpseExpiryCannotRepeatHitWhenTargetOrSourceMoves(bool sourceMoves)
        {
            using var t = World();
            var target = CorpseScenario(t, sourceMoves);
            t.Press(BwButton.Punch); t.Step(4);
            Assert.AreEqual(92, Hp(t, target), .0001f);
            t.Step(8);
            Assert.AreEqual(2, t.Count, "old corpse naturally faded while the jab was active");
            Assert.AreEqual(92, Hp(t, target), .0001f);
        }

        [Test]
        public void SortRowsDuringActiveSwingCannotRepeatHit()
        {
            using var t = World(); t.Duel(.9f);
            var target = t.World.Table(BwKeys.Fighter).Handles[1];
            t.Press(BwButton.Punch); t.Step(4);
            using var keys = new NativeArray<uint>(new uint[] { 2, 1 }, Allocator.Temp);
            t.World.SortRows(BwKeys.Fighter, keys);
            t.Step(7);
            Assert.AreEqual(22, Hp(t, target), .0001f, "training dummy has 30 HP; only one 8-point jab");
        }

        [TestCase(1, false)]
        [TestCase(2, true)]
        public void RecycledTargetGenerationIsNewUnlessBoundedHistoryIsFull(int capacity, bool canHit)
        {
            using var t = World(targets: capacity);
            t.World.ClearLevel(); BwSpawner.Spawn(t.World, 0, float2.zero, 1, 0);
            var old = Target(t); t.Game.Flow = BwFlow.Fighting;
            t.Press(BwButton.Punch); t.Step(4); Assert.AreEqual(92, Hp(t, old), .0001f);
            t.World.DestroyEntity(old);
            var replacement = Target(t);
            Assert.AreEqual(old.Index, replacement.Index); Assert.AreNotEqual(old.Generation, replacement.Generation);
            t.Step(5);
            Assert.AreEqual(canHit ? 92 : 100, Hp(t, replacement), .0001f);
            var state = t.World.Resource(BwKeys.SharedCombat);
            Assert.AreEqual(canHit, state.RejectedHits == 0);
        }

        [Test]
        public void OptInCombatCanHitRow65WithoutAliasingRowOne()
        {
            using var t = World(fighters: 96, targets: 96);
            t.World.ClearLevel(); BwSpawner.Spawn(t.World, 0, float2.zero, 1, 0);
            for (int i = 1; i < 65; i++)
            {
                BwSpawner.Spawn(t.World, 1, new float2(-8, 0), -1, 0);
                var f = t.Info(i); f.Hp = 0; f.State = FighterState.KO; t.World.Column(BwKeys.Info).Set(i, f);
            }
            var target = Target(t); t.Game.Flow = BwFlow.Fighting;
            Assert.AreEqual(66, t.Count);
            t.Press(BwButton.Punch); t.Step(10);
            Assert.AreEqual(92, Hp(t, target), .0001f);
        }

        [Test]
        public void MidHitSnapshotRestoresHistoryBeforeCorpseSwapback()
        {
            using var t = World(); var target = CorpseScenario(t);
            t.Press(BwButton.Punch); t.Step(4); Assert.AreEqual(92, Hp(t, target));
            byte[] mid = t.Session.CaptureSnapshot();
            t.Step(10); byte[] expected = t.Session.CaptureSnapshot();
            using var restored = new BwTestWorld(start: false, sharedCombat: BwSharedCombatConfig.Default);
            restored.Session.RestoreSnapshot(mid);
            CollectionAssert.AreEqual(mid, restored.Session.CaptureSnapshot());
            restored.Step(10);
            CollectionAssert.AreEqual(expected, restored.Session.CaptureSnapshot());
            Assert.AreEqual(92, Hp(restored, target));
        }

        [Test]
        public void NextSwingAndLevelResetReleasePreviousScope()
        {
            using var t = World();
            t.World.ClearLevel(); BwSpawner.Spawn(t.World, 0, float2.zero, 1, 0); var target = Target(t);
            t.Game.Flow = BwFlow.Fighting;
            t.Press(BwButton.Punch); t.Step(4);
            var resource = t.World.Resource(BwKeys.SharedCombat);
            Assert.AreEqual(1, resource.Attacks[0].History.Count);
            t.Step(30);
            Assert.AreEqual(0, resource.Attacks[0].History.Pulse);
            t.World.Registry.TryResolve(target, out _, out int row);
            t.World.Column(BwKeys.Position).Set(row, new float2(.9f, 0));
            var f = t.Info(row); f.VelocityX = 0; t.World.Column(BwKeys.Info).Set(row, f);
            t.Press(BwButton.Punch); t.Step(6);
            Assert.AreEqual(84, Hp(t, target));
            t.World.ClearLevel();
            Assert.AreEqual(0, resource.Attacks[0].History.Count);
            Assert.IsTrue(resource.Targets[0].IsNull);
        }

        [Test]
        public void ComboStartsANewPulseWithoutLosingTheSourceHandle()
        {
            using var t = World(); t.World.ClearLevel();
            BwSpawner.Spawn(t.World, 0, float2.zero, 1, 0); var target = Target(t); t.Game.Flow = BwFlow.Fighting;
            t.Press(BwButton.Punch); t.Step(4);
            var state = t.World.Resource(BwKeys.SharedCombat);
            var owner = state.Attacks[0].History.Owner; uint pulse = state.Attacks[0].History.Pulse;
            t.Press(BwButton.Punch);
            for (int i = 0; i < 30 && t.Info(t.Player).Attack != AttackKind.Cross; i++) t.Step();
            Assert.AreEqual(AttackKind.Cross, t.Info(t.Player).Attack);
            Assert.AreEqual(owner, state.Attacks[0].History.Owner);
            Assert.AreNotEqual(pulse, state.Attacks[0].History.Pulse);
            Assert.AreEqual(0, state.Attacks[0].History.Count);
            t.Step(10); Assert.Less(Hp(t, target), 92);
        }

        [Test]
        public void ClassicAndSharedSnapshotsHaveExplicitlyDifferentLayouts()
        {
            using var classic = new BwTestWorld(); using var shared = World();
            byte[] classicBytes = classic.Session.CaptureSnapshot(), sharedBytes = shared.Session.CaptureSnapshot();
            Assert.Throws<InvalidDataException>(() => classic.Session.RestoreSnapshot(sharedBytes));
            Assert.Throws<InvalidDataException>(() => shared.Session.RestoreSnapshot(classicBytes));
        }

        [Test]
        public void RecycledAttackerCannotInheritPreviousOwnersScope()
        {
            using var t = World(); t.World.ClearLevel();
            BwSpawner.Spawn(t.World, 0, float2.zero, 1, 0);
            var oldOwner = t.World.Table(BwKeys.Fighter).Handles[0];
            var target = Target(t); t.Game.Flow = BwFlow.Fighting;
            t.Press(BwButton.Punch); t.Step(4); Assert.AreEqual(92, Hp(t, target));
            t.World.DestroyEntity(oldOwner);
            BwSpawner.Spawn(t.World, 0, float2.zero, 1, 0);
            var newOwner = t.World.Table(BwKeys.Fighter).Handles[t.Player];
            Assert.AreEqual(oldOwner.Index, newOwner.Index); Assert.AreNotEqual(oldOwner.Generation, newOwner.Generation);
            t.World.Registry.TryResolve(target, out _, out int row);
            t.World.Column(BwKeys.Position).Set(row, new float2(.9f, 0));
            var f = t.Info(row); f.VelocityX = 0; t.World.Column(BwKeys.Info).Set(row, f);
            t.Press(BwButton.Punch); t.Step(6);
            Assert.AreEqual(84, Hp(t, target));
            Assert.AreEqual(newOwner, t.World.Resource(BwKeys.SharedCombat).Attacks[0].History.Owner);
        }

        [Test]
        public void SharedCombatFixedTicksAllocateNoManagedMemoryAfterWarmup()
        {
            using var t = World(); t.Duel(.9f);
            var f = t.Info(1); f.Hp = f.MaxHp = 100000; t.World.Column(BwKeys.Info).Set(1, f);
            void Run()
            {
                for (int tick = 0; tick < 90; tick++)
                {
                    if (tick % 16 == 0) t.Game.Input = new InputFrame { Pressed = 1u << BwButton.Punch };
                    t.Step();
                }
            }
            Run();
            Action measured = Run;
            using var probe = new ManagedAllocationProbe();
            var calibrationBefore = probe.Calibrate();
            var sample = probe.Measure(measured);
            var calibrationAfter = probe.Calibrate();
            TestContext.WriteLine($"Brawler shared combat, 90 ticks after 90 warm-up ticks: {sample.Value} current-thread {sample.Metric}; independent process-wide gen0 collections={sample.Collections}; retained-array/empty calibration before={calibrationBefore.RetainedArrays.Value}/{calibrationBefore.Empty.Value}, after={calibrationAfter.RetainedArrays.Value}/{calibrationAfter.Empty.Value}.");
            Assert.AreEqual(0, sample.Value, $"Warmed simulation must allocate zero current-thread {sample.Metric}; excludes rendering/native allocations.");
        }

        [Test]
        public void SnapshotRejectsDifferentCapacityAndCorruptCount()
        {
            using var state = new BwSharedCombatState(BwSharedCombatConfig.Default);
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
            state.WriteSnapshot(writer); var bytes = stream.ToArray();
            using var other = new BwSharedCombatState(new BwSharedCombatConfig { Fighters = 64, TargetsPerAttack = 32 });
            Assert.Throws<InvalidDataException>(() => other.ReadSnapshot(new BinaryReader(new MemoryStream(bytes))));
            // Header 24 bytes; first scope: owner 8, pulse 4, then count.
            bytes[36] = 65;
            Assert.Throws<InvalidDataException>(() => state.ReadSnapshot(new BinaryReader(new MemoryStream(bytes))));
        }
    }
}
