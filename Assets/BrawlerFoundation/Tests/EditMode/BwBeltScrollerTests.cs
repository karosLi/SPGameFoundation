using System;
using System.IO;
using NUnit.Framework;
using SPF.Contracts;
using SPF.L2.Combat;
using SPF.Testing;
using Unity.Collections;
using Unity.Mathematics;

namespace BrawlerFoundation.Tests
{
    public class BwBeltScrollerTests
    {
        static BwTestWorld World(BwBeltConfig? config = null, bool start = true) => new BwTestWorld(start: start, belt: config ?? BwBeltConfig.Default);
        static void Duel(BwTestWorld t, float2 enemy, byte variant = 0)
        {
            t.World.ClearLevel(); BwSpawner.Spawn(t.World, 0, float2.zero, 1, 0); BwSpawner.Spawn(t.World, 1, enemy, -1, variant);
            var f = t.Info(1); f.Hp = f.MaxHp = 100; t.World.Column(BwKeys.Info).Set(1, f); t.Game.Flow = BwFlow.Fighting;
        }
        static BwBeltMotion Motion(BwTestWorld t, int row = 0) => t.World.Column(BwBeltKeys.Motion)[row];
        static float2 Ground(BwTestWorld t, int row = 0) => t.World.Column(BwBeltKeys.Ground)[row];
        static void Frame(BwTestWorld t, uint pressed = 0, uint held = 0, float2 move = default)
        { t.Game.Input = new InputFrame { Pressed = pressed, Held = held, Move = move }; t.Step(); }

        [Test]
        public void StartsBoundedDepthWaveAndFourRealSkillSlots()
        {
            using var t = World();
            Assert.AreEqual(5, t.Count); Assert.AreEqual(BwFlow.Fighting, t.Game.Flow);
            Assert.AreNotEqual(Ground(t, 1).y, Ground(t, 3).y);
            Assert.AreEqual(2, t.World.Resource(BwMobileSkills.Key).GetSnapshot(1).Charges);
            Assert.AreEqual(14, t.World.Resource(BwMobileSkills.Key).GetSnapshot(3).Definition.Id);
            Assert.IsFalse(BwBeltRules.CanUseSlot(t.World, 3), "full-health heal is disabled without consuming cooldown");
        }
        [Test]
        public void GroundHasTwoAxesAndDiagonalSpeedIsNormalized()
        {
            using var t = World(); Duel(t, new float2(8, -2));
            Frame(t, move: new float2(1, 1)); t.Step(29);
            var p = Ground(t);
            Assert.Greater(p.x, 1); Assert.Greater(p.y, 1); Assert.AreEqual(1.6f, math.length(p), .001f);
            Assert.AreEqual(0, Motion(t).Height); Assert.AreEqual(p.y * BwBeltRules.DepthProjection, t.Position(0).y, .0001f);
        }
        [Test]
        public void JumpHeightIsIndependentFromGroundDepthAndLands()
        {
            using var t = World(); Duel(t, new float2(8, -2));
            Frame(t, 1u << 2, move: new float2(0, 1)); t.Step(9);
            Assert.Greater(Motion(t).Height, .5f); Assert.Greater(Ground(t).y, .4f);
            Assert.AreEqual(Ground(t).y * .55f + Motion(t).Height, t.Position(0).y, .0001f);
            Assert.AreEqual(0, t.World.Resource(BwMobileSkills.Key).GetSnapshot(2).Charges);
            t.Game.Input = default; float depth = Ground(t).y; t.Step(65);
            Assert.AreEqual(0, Motion(t).Height); Assert.AreEqual(depth, Ground(t).y, .0001f);
            Assert.AreEqual(1, t.World.Resource(BwMobileSkills.Key).GetSnapshot(2).Charges);
        }
        [TestCase(0f, true)]
        [TestCase(.9f, false)]
        [TestCase(-1.4f, false)]
        public void BoneStrikeRequiresGroundDepthOverlap(float depth, bool hits)
        {
            using var t = World(); Duel(t, new float2(.9f, depth));
            Frame(t, 1); t.Game.Input = default; t.Step(10);
            Assert.AreEqual(hits ? 92 : 100, t.Info(1).Hp, .0001f);
        }
        [Test]
        public void HeightSeparatedFighterDoesNotGetHitAtMatchingGround()
        {
            using var t = World(); Duel(t, new float2(.9f, 0));
            var motion = Motion(t, 1); motion.Height = motion.PreviousHeight = 3; t.World.Column(BwBeltKeys.Motion).Set(1, motion);
            Frame(t, 1); t.Game.Input = default; t.Step(9);
            Assert.AreEqual(100, t.Info(1).Hp, .0001f);
        }
        [Test]
        public void ClosedDepthAndHeightTangenciesHaveExplicitMathContract()
        {
            var hurt = new GroundHurtBox { Ground = new float2(2, 3), HalfWidth = .5f, HalfDepth = .5f, Bottom = 1, Top = 2 };
            Assert.IsTrue(GroundCombatQueries.ProbeOverlaps(3, 4, 2.5f, .5f, .5f, hurt));
            Assert.IsFalse(GroundCombatQueries.ProbeOverlaps(3.001f, 4, 2.5f, .5f, .5f, hurt));
            Assert.IsFalse(GroundCombatQueries.ProbeOverlaps(3, 4.001f, 2.5f, .5f, .5f, hurt));
            Assert.IsFalse(GroundCombatQueries.ProbeOverlaps(3, 4, 2.501f, .5f, .5f, hurt));
        }
        [Test]
        public void HoldChainsThreeAttacksUsingNewStablePulses()
        {
            using var t = World(); Duel(t, new float2(8, 0));
            Frame(t, held: 1); Assert.AreEqual(AttackKind.Jab, t.Info(0).Attack);
            bool cross = false, kick = false; uint first = t.World.Resource(BwKeys.SharedCombat).Attacks[0].History.Pulse;
            for (int i = 0; i < 48; i++) { t.Step(); cross |= t.Info(0).Attack == AttackKind.Cross; kick |= t.Info(0).Attack == AttackKind.Kick; }
            Assert.IsTrue(cross); Assert.IsTrue(kick);
            Assert.Greater(t.World.Resource(BwKeys.SharedCombat).Attacks[0].History.Pulse, first);
            Assert.AreEqual(2, t.World.Resource(BwMobileSkills.Key).GetSnapshot(1).Charges, "basic finisher does not spend charged kick");
        }
        [Test]
        public void TapDuringRecoveryBuffersWithoutPrematureSkillCharge()
        {
            using var t = World(); Duel(t, new float2(8, 0)); Frame(t, 1); t.Game.Input = default; t.Step(4);
            Frame(t, 1); t.Game.Input = default;
            Assert.IsTrue(Motion(t).BufferedAttack.Pending); Assert.AreEqual(AttackKind.Jab, t.Info(0).Attack);
            t.Step(15); Assert.AreEqual(AttackKind.Cross, t.Info(0).Attack); Assert.IsFalse(Motion(t).BufferedAttack.Pending);
        }
        [Test]
        public void EnemyClosesDepthAndStaggerRecovers()
        {
            using var t = World(); Duel(t, new float2(4, 2), 1); float before = math.length(Ground(t, 1)); t.Step(45);
            Assert.Less(math.length(Ground(t, 1)), before); Assert.Less(Ground(t, 1).y, 2);
            Duel(t, new float2(.9f, 0)); Frame(t, 1); t.Game.Input = default; t.Step(5);
            Assert.AreEqual(FighterState.Hit, t.Info(1).State); t.Step(25); Assert.AreEqual(FighterState.Idle, t.Info(1).State);
        }
        [Test]
        public void BodiesSeparateInGroundPlaneButNotBetweenDistantDepths()
        {
            using var t = World(); Duel(t, new float2(0, 1.3f)); t.Step(4);
            Assert.AreEqual(0, Ground(t).x); Assert.AreEqual(0, Ground(t, 1).x);
            Duel(t, new float2(.1f, .1f)); t.Step(8);
            Assert.Greater(math.distance(Ground(t), Ground(t, 1)), .6f);
            Assert.AreEqual(0, t.World.Resource(BwBeltKeys.State).LastGridDropped);
        }
        [Test]
        public void SharedIdentityPreventsRepeatAfterRowSort()
        {
            using var t = World(); Duel(t, new float2(.9f, 0)); var target = t.World.Table(BwKeys.Fighter).Handles[1];
            Frame(t, 1); t.Game.Input = default; t.Step(4); Assert.AreEqual(92, t.Info(1).Hp);
            using var keys = new NativeArray<uint>(new uint[] { 2, 1 }, Allocator.Temp); t.World.SortRows(BwKeys.Fighter, keys); t.Step(6);
            Assert.IsTrue(t.World.Registry.TryResolve(target, out _, out int row)); Assert.AreEqual(92, t.Info(row).Hp);
        }
        [Test]
        public void FixedHitCapacityRejectsNewGenerationWithoutAliasing()
        {
            var config = BwBeltConfig.Default; config.TargetsPerAttack = 1;
            using var t = World(config); Duel(t, new float2(.9f, 0)); Frame(t, 1); t.Game.Input = default; t.Step(4);
            var old = t.World.Table(BwKeys.Fighter).Handles[1]; t.World.DestroyEntity(old);
            BwSpawner.Spawn(t.World, 1, new float2(.9f, 0), -1, 0); var replacement = t.World.Table(BwKeys.Fighter).Handles[1];
            Assert.AreEqual(old.Index, replacement.Index); Assert.AreNotEqual(old.Generation, replacement.Generation);
            float hp = t.Info(1).Hp; t.Step(5); Assert.AreEqual(hp, t.Info(1).Hp);
            Assert.Greater(t.World.Resource(BwKeys.SharedCombat).RejectedHits, 0);
        }
        [Test]
        public void KillCreatesCollectibleCoinsAndHealingAndHealSkillRestoresHp()
        {
            using var t = World(); Duel(t, new float2(.9f, 0));
            var f = t.Info(0); f.Hp = 50; t.World.Column(BwKeys.Info).Set(0, f);
            var target = t.Info(1); target.Hp = 1; t.World.Column(BwKeys.Info).Set(1, target); t.Game.Kos = 1;
            Frame(t, 1); t.Game.Input = default; t.Step(5);
            var belt = t.World.Resource(BwBeltKeys.State); Assert.AreEqual(2, t.Game.Kos); Assert.AreEqual(2, belt.ActiveDrops);
            t.Step(40); Assert.AreEqual(10, belt.Coins); Assert.AreEqual(1, belt.HealsCollected); Assert.AreEqual(68, t.Info(0).Hp);
            t.Game.Flow = BwFlow.Fighting; BwSpawner.Spawn(t.World, 1, new float2(8, -2), -1, 0);
            Frame(t, 1u << 3); Assert.AreEqual(92, t.Info(0).Hp); Assert.AreEqual(0, t.World.Resource(BwMobileSkills.Key).GetSnapshot(3).Charges);
        }
        [Test]
        public void DropOverflowRejectsNewestAndNeverAllocatesOrOverwrites()
        {
            var config = BwBeltConfig.Default; config.Drops = 1;
            using var belt = new BwBeltState(config);
            Assert.IsTrue(belt.TryDrop(new float2(1, 2), BwBeltDropKind.Coin, 10));
            Assert.IsFalse(belt.TryDrop(float2.zero, BwBeltDropKind.Heal, 18)); Assert.AreEqual(1, belt.RejectedDrops);
            Assert.AreEqual(BwBeltDropKind.Coin, belt.Drops[0].Kind); Assert.AreEqual(new float2(1, 2), belt.Drops[0].Ground);
        }
        [Test]
        public void CapacityAndSpatialWorkStayBoundedWithoutDenseAllPairs()
        {
            var config = BwBeltConfig.Default; config.Fighters = config.TargetsPerAttack = 128;
            using var t = World(config); t.World.ClearLevel();
            BwSpawner.Spawn(t.World, 0, new float2(-8.5f, -2), 1, 0);
            for (int i = 1; i < 128; i++) BwSpawner.Spawn(t.World, 1, new float2(-8.5f + (i % 32) * .54f, -2 + (i / 32) * 1.3f), -1, 0);
            BwSpawner.Spawn(t.World, 1, float2.zero, -1, 0); t.Game.Flow = BwFlow.Fighting; t.Step();
            var state = t.World.Resource(BwBeltKeys.State); Assert.AreEqual(128, t.Count); Assert.AreEqual(1, state.RejectedSpawns);
            Assert.AreEqual(0, state.LastGridDropped); Assert.AreEqual(128, state.Grid.EntryCount);
            Assert.Less(state.SeparationCandidates, 128 * 8, "sparse fixture must only query local neighbors, not 128 squared pairs");
        }
        [Test]
        public void MidActionAirborneAndLootSnapshotResumesExactly()
        {
            using var t = World(); Duel(t, new float2(.9f, 0)); Frame(t, 1); t.Game.Input = default; t.Step(4);
            t.World.Resource(BwBeltKeys.State).TryDrop(new float2(4, 1), BwBeltDropKind.Heal, 18);
            var m = Motion(t); m.Height = .8f; m.HeightVelocity = 1; m.BufferedAttack.Push(0, t.Session.Pipeline.LastTickTime.Tick, 24); t.World.Column(BwBeltKeys.Motion).Set(0, m);
            byte[] mid = t.Session.CaptureSnapshot(); t.Step(60); byte[] expected = t.Session.CaptureSnapshot();
            using var restored = World(start: false); restored.Session.RestoreSnapshot(mid);
            CollectionAssert.AreEqual(mid, restored.Session.CaptureSnapshot()); restored.Step(60); CollectionAssert.AreEqual(expected, restored.Session.CaptureSnapshot());
        }
        [Test]
        public void SameSeedAndInputsStayDeterministicThroughWaves()
        {
            using var a = World(); using var b = World();
            for (int tick = 0; tick < 480; tick++)
            {
                var frame = new InputFrame { Move = new float2(tick % 180 < 90 ? 1 : -1, tick % 120 < 60 ? .6f : -.6f), Held = 1, Pressed = tick % 100 == 0 ? 2u : 0u };
                a.Game.Input = b.Game.Input = frame; a.Step(); b.Step();
            }
            CollectionAssert.AreEqual(a.Session.CaptureSnapshot(), b.Session.CaptureSnapshot());
        }
        [Test]
        public void TerminalFreezeAndRestartClearHandlesLootAndSkills()
        {
            var config = BwBeltConfig.Default; config.Waves = 1;
            using var t = World(config); var old = t.World.Table(BwKeys.Fighter).Handles[0];
            for (int i = 1; i < t.Count; i++) { var f = t.Info(i); f.Hp = 0; f.State = FighterState.KO; t.World.Column(BwKeys.Info).Set(i, f); }
            t.Step(150); Assert.AreEqual(BwFlow.Won, t.Game.Flow);
            float2 position = Ground(t); Frame(t, 15, 15, new float2(1, 1)); t.Step(10); Assert.AreEqual(position, Ground(t));
            t.World.Resource(BwBeltKeys.State).TryDrop(float2.zero, BwBeltDropKind.Coin, 5);
            t.Game.Send(BwCommandKind.Start); t.Step(); Assert.AreEqual(BwFlow.Fighting, t.Game.Flow); Assert.AreEqual(5, t.Count);
            Assert.IsFalse(t.World.Registry.TryResolve(old, out _, out _)); Assert.AreEqual(0, t.World.Resource(BwBeltKeys.State).ActiveDrops);
            Assert.AreEqual(2, t.World.Resource(BwMobileSkills.Key).GetSnapshot(1).Charges); Assert.AreEqual(0, Motion(t).Height);
            var f0 = t.Info(0); f0.State = FighterState.KO; f0.Hp = 0; f0.StateTime = 1; t.World.Column(BwKeys.Info).Set(0, f0); t.Step(); Assert.AreEqual(BwFlow.Lost, t.Game.Flow);
        }
        [Test]
        public void SchemaRejectsClassicAndChangedConfigWithoutChangingClassicLayout()
        {
            using var belt = World(); using var classic = new BwTestWorld();
            Assert.Throws<InvalidDataException>(() => classic.Session.RestoreSnapshot(belt.Session.CaptureSnapshot()));
            Assert.Throws<InvalidDataException>(() => belt.Session.RestoreSnapshot(classic.Session.CaptureSnapshot()));
            var config = BwBeltConfig.Default; config.Waves = 2; using var other = World(config);
            Assert.Throws<InvalidDataException>(() => other.Session.RestoreSnapshot(belt.Session.CaptureSnapshot()));
        }
        [Test]
        public void WarmedBeltLogicAllocatesNoCurrentThreadManagedBytes()
        {
            using var t = World(); Duel(t, new float2(8, 0));
            void Run() { for (int i = 0; i < 90; i++) { t.Game.Input = new InputFrame { Held = 1 }; t.Step(); } }
            Run(); using var probe = new ManagedAllocationProbe(); var calibration = probe.Calibrate(); var sample = probe.Measure((Action)Run);
            TestContext.WriteLine($"Belt logic 90 warmed ticks: {sample.Value} {sample.Metric}; calibration {calibration.RetainedArrays.Value}/{calibration.Empty.Value}. Excludes Unity/native/render allocations.");
            Assert.AreEqual(0, sample.Value);
        }
    }
}
