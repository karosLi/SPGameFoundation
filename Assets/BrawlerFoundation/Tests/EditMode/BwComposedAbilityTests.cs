using System;
using System.IO;
using NUnit.Framework;
using SPF.Contracts;
using SPF.L2.Skills;
using SPF.L2.Weapons;
using SPF.Runtime.Composition;
using SPF.Runtime.Session;
using SPF.Runtime.World;
using SPF.Testing;
using Unity.Collections;
using Unity.Mathematics;

namespace BrawlerFoundation.Tests
{
    public class BwComposedAbilityTests
    {
        sealed class Duel : IDisposable
        {
            readonly GameplayModuleAsset module; readonly ModeDefinition mode;
            public readonly SimSession Session;
            public SimWorld World => Session.World;
            public BwGameState Game => World.Resource(BwKeys.Game);
            public BwComposedAbilityState Rule => World.Resource(BwComposedAbilityState.Key);
            public ActionPoseClock Pose => World.Resource(BwWeapons.PoseKey);
            public SkillSlots Slots => World.Resource(BwMobileSkills.Key);
            public WeaponRuntime Weapon => World.Resource(BwWeapons.Key);
            public FighterInfo Read(int row = 0) => World.Column(BwKeys.Info)[row];
            public void Write(FighterInfo f, int row = 0) => World.Column(BwKeys.Info).Set(row, f);
            public Duel(BwComposedAbilityConfig? config = null, int history = 64, float depth = 0, float distance = .9f)
            {
                var belt = BwBeltConfig.Default; belt.TargetsPerAttack = history;
                mode = BwMode.CreateComposedAbilityBelt(belt, config ?? BwComposedAbilityConfig.Default, out module);
                Session = SimSession.Create(mode, 7); Session.Start();
                BwSpawner.Spawn(World, 0, 0, 1, 0); BwSpawner.Spawn(World, 1, new float2(distance, depth), -1, 0);
                var target = Read(1); target.Hp = target.MaxHp = 1000; Write(target, 1); Game.Flow = BwFlow.Fighting;
            }
            public void Step(int count = 1) { for (int i = 0; i < count; i++) Session.Step(); }
            public void Press(int button) { Game.Input = new InputFrame { Pressed = 1u << button }; Step(); Game.Input = default; }
            public void Injure(float hp = 40) { var f = Read(); f.Hp = hp; Write(f); }
            public void Earn() { Press(BwButton.Kick); Step(30); Assert.AreEqual(6, Rule.HealCredit); }
            public void Dispose() { Session.Dispose(); UnityEngine.Object.DestroyImmediate(mode); UnityEngine.Object.DestroyImmediate(module); }
        }

        [Test]
        public void DataVariantTunesOnlyNewFactoryAndIndependentRuleEarnsCredit()
        {
            using var data = new Duel(BwComposedAbilityConfig.DataVariant); using var rules = new Duel();
            foreach (var t in new[] { data, rules })
            {
                t.Press(BwButton.Kick); t.Step(30);
                Assert.AreEqual(978, t.Read(1).Hp); Assert.AreEqual(1, t.Rule.AcceptedKicks); Assert.AreEqual(1, t.Rule.SettledKickHits);
                Assert.AreEqual(1, t.Slots.GetSnapshot(1).Charges); Assert.AreEqual(84, t.Slots.GetSnapshot(1).Definition.CooldownTicks);
            }
            Assert.Zero(data.Rule.HealCredit); Assert.AreEqual(6, rules.Rule.HealCredit);
            data.Injure(); data.Press(3); Assert.AreEqual(40, data.Read().Hp); data.Step(9); Assert.AreEqual(58, data.Read().Hp);
        }

        [TestCase(0f, .9f, true)]
        [TestCase(.9f, .9f, false)]
        [TestCase(0f, 8f, false)]
        public void ActualKickContactHasIndependentChargeAndHitAdmission(float depth, float distance, bool hits)
        {
            using var t = new Duel(depth: depth, distance: distance); t.Press(1); t.Step(30);
            Assert.AreEqual(hits ? 978 : 1000, t.Read(1).Hp); Assert.AreEqual(hits ? 6 : 0, t.Rule.HealCredit);
            Assert.AreEqual(1, t.Slots.GetSnapshot(1).Charges, "accepted whiff still spends one action charge");
            Assert.AreEqual(1, t.Rule.AcceptedKicks); Assert.AreEqual(hits ? 1 : 0, t.Rule.SettledKickHits);
        }

        [Test]
        public void FirstKickContactMarkerRetainsTheActualShinTipOverlap()
        {
            using var t = new Duel(); t.Press(1); t.Step(8); Assert.AreEqual(978, t.Read(1).Hp);
            var rig = t.World.Resource(BwKeys.Rig); var ground = t.World.Column(BwBeltKeys.Ground); var motion = t.World.Column(BwBeltKeys.Motion);
            using var local = new NativeArray<SPF.L1.Skeleton.BoneLocal>(rig.Asset.BoneCount * 2, Allocator.Temp);
            using var bones = new NativeArray<SPF.L1.Skeleton.BoneWorld>(rig.Asset.BoneCount, Allocator.Temp);
            var tip = BrawlerFoundation.Systems.BwProbe.Tip(rig, t.Read(), t.World.Column(BwKeys.Anim)[0], new float2(ground[0].x, motion[0].Height), local, bones);
            var hurt = new SPF.L2.Combat.GroundHurtBox { Ground = ground[1], HalfWidth = BwRules.BodyHalfWidth, HalfDepth = BwBeltRules.BodyDepth,
                Bottom = motion[1].Height + BwRules.HurtBottom, Top = motion[1].Height + BwRules.HurtTop };
            Assert.IsTrue(SPF.L2.Combat.GroundCombatQueries.ProbeOverlaps(tip.x, ground[0].y, tip.y, BwRules.ProbeRadius, .5f, hurt));
            Assert.AreEqual(8, t.Pose.Timeline.Tick); Assert.AreEqual(1, t.Rule.SettledKickHits);
        }

        [Test]
        public void HealReleaseMatchesExistingPoseAndSpendsBankOnce()
        {
            using var t = new Duel(); t.Earn(); t.Injure(); t.Press(3);
            Assert.AreEqual(BwWeapons.HealPose, t.Pose.ContentId); Assert.IsTrue(t.Rule.PendingHeal);
            Assert.AreEqual(0, t.Slots.GetSnapshot(3).Charges); Assert.AreEqual(40, t.Read().Hp); Assert.AreEqual(6, t.Rule.HealCredit);
            t.Step(BwComposedAbilityState.HealReleaseTick - 1); Assert.AreEqual(40, t.Read().Hp);
            Assert.AreEqual(6, t.Rule.HealCredit); t.Step(); Assert.AreEqual(64, t.Read().Hp); Assert.Zero(t.Rule.HealCredit);
            Assert.AreEqual(1, t.Rule.AppliedHeals); Assert.IsFalse(t.Rule.PendingHeal); t.Step(30); Assert.AreEqual(64, t.Read().Hp);
        }

        [Test]
        public void BusyKickHealAndWeaponRequestsSpendNothing()
        {
            using var t = new Duel(); t.Injure(); t.Press(1); t.Press(1); t.Press(3);
            Assert.AreEqual(1, t.Slots.GetSnapshot(1).Charges); Assert.AreEqual(1, t.Slots.GetSnapshot(3).Charges);
            Assert.AreEqual(1, t.Rule.AcceptedKicks); Assert.Zero(t.Rule.AcceptedHeals); Assert.AreEqual(2, t.Rule.RejectedRequests);
            Assert.AreEqual(AbilityRejection.Busy, t.Rule.LastRejection);
            t.Step(30); t.Press(3); t.Press(1); t.Press(0);
            Assert.AreEqual(1, t.Slots.GetSnapshot(1).Charges); Assert.Zero(t.Weapon.Releases); Assert.AreEqual(1, t.Rule.AcceptedHeals);
            Assert.IsFalse(BwBeltRules.CanUseSlot(t.World, 0)); Assert.IsFalse(BwBeltRules.CanUseSlot(t.World, 1));
        }

        [Test]
        public void FullHealthAirborneAndNoChargeRejectWithoutExtraSpend()
        {
            using var t = new Duel(distance: 8); t.Press(3); Assert.AreEqual(1, t.Slots.GetSnapshot(3).Charges);
            t.Injure(); t.Press(2); t.Press(3); Assert.AreEqual(1, t.Slots.GetSnapshot(3).Charges);
            t.Step(60); t.Press(1); t.Step(28); t.Press(1); t.Step(28); t.Press(1);
            Assert.Zero(t.Slots.GetSnapshot(1).Charges); Assert.AreEqual(2, t.Rule.AcceptedKicks); Assert.AreEqual(AbilityRejection.NoCharge, t.Rule.LastRejection);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void CanceledKickSpendsChargeWithoutDamageOrCredit(int cancellation)
        {
            using var t = new Duel(); t.Press(1); t.Step(2);
            if (cancellation == 0) { var f = t.Read(); f.State = FighterState.Hit; f.StateTime = 0; t.Write(f); }
            else if (cancellation == 1) t.Press(BwWeapons.SwitchButton);
            else t.Weapon.RequestEquip(WeaponProfiles.Sword);
            t.Step(35); Assert.AreEqual(1000, t.Read(1).Hp); Assert.Zero(t.Rule.HealCredit); Assert.Zero(t.Rule.SettledKickHits);
            Assert.AreEqual(1, t.Slots.GetSnapshot(1).Charges); Assert.AreEqual(1, t.Rule.AcceptedKicks);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void InterruptedHealKeepsCreditButNeverRefundsOrPaysLater(int cancellation)
        {
            using var t = new Duel(); t.Earn(); t.Injure(); t.Press(3); t.Step(3);
            if (cancellation == 0) { var f = t.Read(); f.State = FighterState.Hit; f.StateTime = 0; t.Write(f); }
            else if (cancellation == 1) t.Press(BwWeapons.SwitchButton);
            else t.Pose.Cancel();
            t.Step(30); Assert.AreEqual(40, t.Read().Hp); Assert.AreEqual(6, t.Rule.HealCredit);
            Assert.Zero(t.Slots.GetSnapshot(3).Charges); Assert.Zero(t.Rule.AppliedHeals); Assert.AreEqual(1, t.Rule.CanceledHeals);
            t.Step(360); t.Press(3); t.Step(9); Assert.AreEqual(64, t.Read().Hp); Assert.Zero(t.Rule.HealCredit); Assert.AreEqual(1, t.Rule.AppliedHeals);
        }

        [Test]
        public void ActualEnemyHitAtHealMarkerCancelsBeforePayment()
        {
            using var t = new Duel(distance: 8); t.Injure(); t.Press(3);
            // Existing jab active starts at ceil(.06 * 60) = four ticks. Start at heal tick five.
            t.Step(5); var enemy = t.Read(1); enemy.State = FighterState.Attack; enemy.Attack = AttackKind.Jab; enemy.StateTime = 0;
            t.Write(enemy, 1); var rig = t.World.Resource(BwKeys.Rig);
            t.World.Column(BwKeys.Anim).Set(1, SPF.L1.Skeleton.Animator2D.Start(rig.Jab));
            t.World.Column(BwBeltKeys.Ground).Set(1, new float2(.9f, 0));
            t.Step(3); Assert.AreEqual(40, t.Read().Hp); Assert.IsTrue(t.Rule.PendingHeal);
            t.Step(); Assert.Less(t.Read().Hp, 40); Assert.Zero(t.Rule.AppliedHeals); Assert.IsFalse(t.Rule.PendingHeal);
            Assert.AreEqual(1, t.Rule.CanceledHeals); Assert.Zero(t.Slots.GetSnapshot(3).Charges);
        }

        [Test]
        public void DeathClearsBankAndRestartDoesNotRetainOldAction()
        {
            using var t = new Duel(); t.Earn(); t.Injure(); t.Press(3); var f = t.Read(); f.Hp = 0; f.State = FighterState.KO; t.Write(f); t.Step(2);
            Assert.Zero(t.Rule.HealCredit); Assert.Zero(t.Rule.AppliedHeals); Assert.IsFalse(t.Rule.PendingHeal); Assert.IsFalse(t.Pose.Running);
            t.Game.Send(BwCommandKind.Start); t.Step(); Assert.Zero(t.Rule.AcceptedKicks); Assert.Zero(t.Rule.AcceptedHeals); Assert.AreEqual(2, t.Slots.GetSnapshot(1).Charges);
        }

        [Test]
        public void HistoryCapacityRejectsHitsWithoutCreditAndCreditCapDoesNotAllocate()
        {
            using var t = new Duel(history: 1); BwSpawner.Spawn(t.World, 1, new float2(.9f, .35f), -1, 0);
            var f = t.Read(2); f.Hp = f.MaxHp = 1000; t.Write(f, 2); t.Press(1); t.Step(20);
            Assert.AreEqual(1978, t.Read(1).Hp + t.Read(2).Hp); Assert.AreEqual(1, t.Rule.SettledKickHits); Assert.AreEqual(6, t.Rule.HealCredit);
            Assert.Greater(t.World.Resource(BwKeys.SharedCombat).RejectedHits, 0);
            using var cap = new Duel();
            for (int i = 0; i < 4; i++)
            {
                var target = cap.Read(1); target.State = FighterState.Idle; target.VelocityX = target.StateTime = 0; target.Attack = AttackKind.None; cap.Write(target, 1);
                cap.World.Column(BwBeltKeys.Ground).Set(1, new float2(.9f, 0)); cap.World.Column(BwBeltKeys.Motion).Set(1, default);
                cap.Press(1); cap.Step(90);
            }
            Assert.AreEqual(4, cap.Rule.SettledKickHits); Assert.AreEqual(18, cap.Rule.HealCredit);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SnapshotBeforeAndAfterHealMarkerCannotDoubleSpendOrDoubleHeal(bool afterMarker)
        {
            using var a = new Duel(); a.Earn(); a.Injure(); a.Press(3); a.Step(afterMarker ? 9 : 4);
            var saved = a.Session.CaptureSnapshot(); using var b = new Duel(); b.Session.RestoreSnapshot(saved);
            CollectionAssert.AreEqual(saved, b.Session.CaptureSnapshot()); a.Step(30); b.Step(30);
            CollectionAssert.AreEqual(a.Session.CaptureSnapshot(), b.Session.CaptureSnapshot()); Assert.AreEqual(64, b.Read().Hp);
            Assert.AreEqual(1, b.Rule.AcceptedHeals); Assert.AreEqual(1, b.Rule.AppliedHeals); Assert.Zero(b.Rule.HealCredit);
        }

        [Test]
        public void MidKickRestoreAndStableRowReorderKeepOneHitAndOneCredit()
        {
            using var a = new Duel(); a.Press(1); a.Step(10); Assert.AreEqual(6, a.Rule.HealCredit);
            var target = a.World.Table(BwKeys.Fighter).Handles[1];
            using (var order = new NativeArray<uint>(new uint[] { 2, 1 }, Allocator.Temp)) a.World.SortRows(BwKeys.Fighter, order);
            var saved = a.Session.CaptureSnapshot(); using var b = new Duel(); b.Session.RestoreSnapshot(saved); a.Step(30); b.Step(30);
            CollectionAssert.AreEqual(a.Session.CaptureSnapshot(), b.Session.CaptureSnapshot()); Assert.AreEqual(1, b.Rule.SettledKickHits);
            Assert.IsTrue(b.World.Registry.TryResolve(target, out _, out int row)); Assert.AreEqual(978, b.Read(row).Hp); Assert.AreEqual(6, b.Rule.HealCredit);
        }

        [Test]
        public void PauseIsAnExactNoOpForPendingHealAndChargeRecharge()
        {
            using var t = new Duel(); t.Earn(); t.Injure(); t.Press(3); t.Step(4); var saved = t.Session.CaptureSnapshot();
            t.Session.Pause(); t.Session.Update(3); t.Session.Sync(); CollectionAssert.AreEqual(saved, t.Session.CaptureSnapshot());
            t.Session.Resume(); t.Step(5); Assert.AreEqual(64, t.Read().Hp); Assert.AreEqual(1, t.Rule.AppliedHeals);
        }

        [Test]
        public void HeightMissStillSpendsOneChargeAndNeverEarnsCredit()
        {
            using var t = new Duel(); var motion = t.World.Column(BwBeltKeys.Motion)[1]; motion.Height = motion.PreviousHeight = 3;
            t.World.Column(BwBeltKeys.Motion).Set(1, motion); t.Press(1); t.Step(18);
            Assert.AreEqual(1000, t.Read(1).Hp); Assert.AreEqual(1, t.Slots.GetSnapshot(1).Charges); Assert.Zero(t.Rule.HealCredit);
        }
        [Test]
        public void CollectionPhaseAndEquipBusyDoNotConsumeAbilityCharges()
        {
            using var t = new Duel(); t.Injure(); t.Game.Flow = BwFlow.WaveClear; t.Game.FlowTimer = 100;
            t.Press(1); t.Press(3); Assert.AreEqual(2, t.Slots.GetSnapshot(1).Charges); Assert.AreEqual(1, t.Slots.GetSnapshot(3).Charges);
            Assert.AreEqual(AbilityRejection.NotPlaying, t.Rule.LastRejection); Assert.Zero(t.Rule.AcceptedKicks); Assert.Zero(t.Rule.AcceptedHeals);
            t.Game.Flow = BwFlow.Fighting; t.Weapon.RequestEquip(WeaponProfiles.Sword); t.Press(1); t.Press(3);
            Assert.AreEqual(2, t.Slots.GetSnapshot(1).Charges); Assert.AreEqual(1, t.Slots.GetSnapshot(3).Charges);
            Assert.AreEqual(AbilityRejection.Busy, t.Rule.LastRejection); Assert.IsFalse(BwBeltRules.CanUseSlot(t.World, 1));
        }
        [Test]
        public void ReplacementOwnerCannotInheritEarnedCredit()
        {
            using var t = new Duel(); t.Earn(); var former = t.World.Table(BwKeys.Fighter).Handles[0];
            t.World.DestroyEntity(former); BwSpawner.Spawn(t.World, 0, 0, 1, 0); t.Step();
            Assert.Zero(t.Rule.HealCredit); Assert.AreNotEqual(former, t.Rule.Owner); Assert.Zero(t.Rule.KickPulse); Assert.IsFalse(t.Rule.PendingHeal);
        }
        [Test]
        public void SimultaneousKickAndHealAdmitsOnlyTheHigherPriorityKick()
        {
            using var t = new Duel(); t.Injure(); t.Game.Input = new InputFrame { Pressed = (1u << 1) | (1u << 3) }; t.Step(); t.Game.Input = default;
            Assert.AreEqual(1, t.Rule.AcceptedKicks); Assert.Zero(t.Rule.AcceptedHeals); Assert.AreEqual(1, t.Rule.RejectedRequests);
            Assert.AreEqual(1, t.Slots.GetSnapshot(1).Charges); Assert.AreEqual(1, t.Slots.GetSnapshot(3).Charges);
        }
        [Test]
        public void ConfigurationRejectsNonfiniteUnboundedOrIncoherentRules()
        {
            var c = BwComposedAbilityConfig.Default; c.KickDamage = float.NaN; Assert.Throws<ArgumentOutOfRangeException>(() => c.Validate());
            c = BwComposedAbilityConfig.Default; c.MaxHealCredit = int.MaxValue; Assert.Throws<ArgumentOutOfRangeException>(() => c.Validate());
            c = BwComposedAbilityConfig.Default; c.KickHitHealCredit = 0; Assert.Throws<ArgumentOutOfRangeException>(() => c.Validate());
            c = BwComposedAbilityConfig.Default; c.HealCooldownTicks = 0; Assert.Throws<ArgumentOutOfRangeException>(() => c.Validate());
        }

        [Test]
        public void WarmComposedKickCreditAndTimedHealRemainAllocationFree()
        {
            using var t = new Duel();
            void Run()
            {
                // Two full 420-tick charge cycles, including actual kick contact and tick9 heal.
                // Reposition the passive fixture target between cycles; never refill skill charges.
                for (int cycle = 0; cycle < 2; cycle++)
                {
                    var player = t.Read(); player.Hp = 40; t.Write(player);
                    var target = t.Read(1); target.Hp = target.MaxHp = 1000; target.VelocityX = 0; t.Write(target, 1);
                    t.World.Column(BwBeltKeys.Ground).Set(1, new float2(.9f, 0));
                    t.World.Column(BwBeltKeys.Motion).Set(1, default);
                    t.Press(BwButton.Kick); t.Step(30); t.Press(BwBeltRules.HealButton); t.Step(388);
                }
            }
            Action measured = Run; measured(); // Warm the entire active path, not only idle ticks.
            int acceptedKicks = t.Rule.AcceptedKicks, hits = t.Rule.SettledKickHits, heals = t.Rule.AppliedHeals;
            using var probe = new ManagedAllocationProbe();
            var before = probe.Calibrate(); var sample = probe.Measure(measured); var after = probe.Calibrate();
            TestContext.WriteLine($"Composed Brawler: 840 measured ticks after 840 warm-up ticks; {sample.Value} current-thread {sample.Metric}; independent process-wide gen0 collections={sample.Collections}; retained-array/empty controls before={before.RetainedArrays.Value}/{before.Empty.Value}, after={after.RetainedArrays.Value}/{after.Empty.Value}; includes two accepted kicks, two unique contacts, earned credit and two timed heal settlements. Excludes rendering/native allocation.");
            Assert.AreEqual(0, sample.Value, "The warmed composed ability path retains the zero current-thread allocation threshold.");
            Assert.AreEqual(acceptedKicks + 2, t.Rule.AcceptedKicks); Assert.AreEqual(hits + 2, t.Rule.SettledKickHits);
            Assert.AreEqual(heals + 2, t.Rule.AppliedHeals); Assert.Zero(t.Rule.HealCredit); Assert.AreEqual(64, t.Read().Hp);
        }

        [Test]
        public void NewResourceRejectsUnknownSchemaChangedContentInvalidBoundAndTruncation()
        {
            var config = BwComposedAbilityConfig.Default; var state = new BwComposedAbilityState(config); byte[] saved = Save(state);
            byte[] version = (byte[])saved.Clone(); BitConverter.GetBytes(2).CopyTo(version, 4); Assert.Throws<InvalidDataException>(() => Read(state, version));
            var changed = config; changed.KickDamage++; Assert.Throws<InvalidDataException>(() => Read(new BwComposedAbilityState(changed), saved));
            byte[] bound = (byte[])saved.Clone(); BitConverter.GetBytes(19).CopyTo(bound, 44); Assert.Throws<InvalidDataException>(() => Read(state, bound));
            Assert.Throws<EndOfStreamException>(() => state.ReadSnapshot(new BinaryReader(new MemoryStream(saved, 0, saved.Length - 1))));
            CollectionAssert.AreEqual(saved, Save(state), "bad bounded payloads do not partially mutate the resource");
        }

        [Test]
        public void ClassicAndWeaponFactoriesDoNotInstallOrUseTheExtension()
        {
            using var classic = new BwTestWorld(); using var weapon = new BwTestWorld(belt: BwBeltConfig.Default, weapons: true);
            Assert.IsFalse(classic.World.HasResource(BwComposedAbilityState.Key)); Assert.IsFalse(weapon.World.HasResource(BwComposedAbilityState.Key));
            weapon.Duel(.9f); weapon.Game.Flow = BwFlow.Fighting; var enemy = weapon.Info(1); enemy.Hp = enemy.MaxHp = 1000; weapon.World.Column(BwKeys.Info).Set(1, enemy);
            weapon.Press(1); weapon.Step(30); Assert.AreEqual(984, weapon.Info(1).Hp);
            var player = weapon.Info(0); player.Hp = 40; weapon.World.Column(BwKeys.Info).Set(0, player); weapon.Press(3); Assert.AreEqual(64, weapon.Info(0).Hp, "legacy heal is still immediate 24 HP");
            Assert.AreEqual(100, weapon.World.Resource(BwMobileSkills.Key).GetSnapshot(1).Definition.CooldownTicks);
            Assert.AreEqual(480, weapon.World.Resource(BwMobileSkills.Key).GetSnapshot(3).Definition.CooldownTicks);
            var saved = weapon.Session.CaptureSnapshot(); using var restored = new BwTestWorld(start: false, belt: BwBeltConfig.Default, weapons: true);
            restored.Session.RestoreSnapshot(saved); CollectionAssert.AreEqual(saved, restored.Session.CaptureSnapshot());
        }
        static byte[] Save(BwComposedAbilityState state) { using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream); state.WriteSnapshot(writer); return stream.ToArray(); }
        static void Read(BwComposedAbilityState state, byte[] bytes) { using var reader = new BinaryReader(new MemoryStream(bytes)); state.ReadSnapshot(reader); }
    }
}
