using System;
using System.IO;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Contracts.Weapons;
using SPF.L2.Combat;
using SPF.L2.Weapons;
using SPF.Runtime.Composition;
using SPF.Runtime.Session;
using SPF.Testing;
using Unity.Mathematics;

namespace BrawlerFoundation.Tests
{
    public class BwBeltAttackMobilityTests
    {
        static BwTestWorld Create(bool smooth = true)
        {
            var t = new BwTestWorld(belt: smooth ? BwBeltConfig.SmoothAttack : BwBeltConfig.Default, weapons: true);
            t.Duel(8); var target = t.Info(1); target.Hp = target.MaxHp = 10000;
            t.World.Column(BwKeys.Info).Set(1, target); return t;
        }
        static WeaponRuntime Weapon(BwTestWorld t) => t.World.Resource(BwWeapons.Key);
        static float2 Ground(BwTestWorld t) => t.World.Column(BwBeltKeys.Ground)[0];
        static void Equip(BwTestWorld t, int id)
        { Weapon(t).RequestEquip(id); t.Step(Weapon(t).Profile(id).EquipTicks + 1); }

        [Test]
        public void HeldAttackContinuouslyReadsStickInsteadOfLockingAfterAdmission()
        {
            using var t = Create();
            t.Game.Input = new InputFrame { Held = 1, Move = new float2(.3f, 0) };
            t.Step();
            uint firstPulse = Weapon(t).Equipment.Timeline.PulseId;
            for (int tick = 0; tick < Weapon(t).Current.DurationTicks * 3; tick++)
            {
                var before = Ground(t); t.Step();
                Assert.Greater(Ground(t).x - before.x, .01f, "A held primary attack must keep translating on every unobstructed tick.");
            }
            Assert.Greater(Weapon(t).Equipment.Timeline.PulseId, firstPulse);
        }

        [TestCase(WeaponProfiles.Blade)]
        [TestCase(WeaponProfiles.Sword)]
        [TestCase(WeaponProfiles.Staff)]
        [TestCase(WeaponProfiles.Bow)]
        public void EachWeaponMovesAtItsActualStartTickPhaseWithoutChangingTheTimeline(int id)
        {
            using var moving = Create(); using var stationary = Create();
            Equip(moving, id); Equip(stationary, id);
            moving.Game.Input = new InputFrame { Held = 1, Move = new float2(.2f, 0), Aim = new float2(1, 0) };
            stationary.Game.Input = new InputFrame { Held = 1, Aim = new float2(1, 0) };
            moving.Step(); stationary.Step();
            int phases = 0;
            for (int i = 0; i < Weapon(moving).Current.DurationTicks * 2; i++)
            {
                var w = Weapon(moving); int tick = w.Equipment.Timeline.Tick;
                float scale = tick < w.Current.Active.From ? .75f : tick < w.Current.Active.Until ? .70f : .80f;
                phases |= tick < w.Current.Active.From ? 1 : tick < w.Current.Active.Until ? 2 : 4;
                var before = Ground(moving); moving.Step(); stationary.Step();
                Assert.AreEqual(BwRules.PlayerSpeed * .2f * scale / 60, Ground(moving).x - before.x, .000002f);
                Assert.AreEqual(Weapon(stationary).Equipment.Timeline.Tick, w.Equipment.Timeline.Tick);
                Assert.AreEqual(Weapon(stationary).Equipment.Timeline.PulseId, w.Equipment.Timeline.PulseId);
                Assert.AreEqual(Weapon(stationary).Releases, w.Releases);
                Assert.AreEqual(stationary.World.Resource(BwMobileSkills.Key).GetSnapshot(0), moving.World.Resource(BwMobileSkills.Key).GetSnapshot(0));
            }
            Assert.AreEqual(7, phases, "Windup, active and recovery all receive real movement.");
        }

        [Test]
        public void StickChangesAndReleaseApplyDuringCommittedAttackWithoutFlippingAim()
        {
            using var t = Create(); Equip(t, WeaponProfiles.Bow);
            t.Game.Input = new InputFrame { Held = 1, Move = new float2(.4f, .3f), Aim = new float2(1, 0) }; t.Step();
            var before = Ground(t); t.Step(); var step = Ground(t) - before;
            Assert.AreEqual(.4f * BwRules.PlayerSpeed * .75f / 60, step.x, .000001f);
            Assert.AreEqual(.3f * BwRules.PlayerSpeed * .75f / 60, step.y, .000001f);
            t.Game.Input = new InputFrame { Held = 1, Move = new float2(-1, 0), Aim = new float2(-1, 0) };
            before = Ground(t); t.Step(); Assert.Less(Ground(t).x, before.x);
            Assert.AreEqual(1, t.Info(0).Facing); Assert.AreEqual(new float2(1, 0), Weapon(t).Equipment.Aim);
            t.Game.Input = new InputFrame { Held = 1 }; before = Ground(t); t.Step(8);
            Assert.AreEqual(before, Ground(t), "Held attack cannot retain an old stick vector.");
            t.Game.Input = default; t.Step(Weapon(t).Current.DurationTicks);
            t.Game.Input = new InputFrame { Move = new float2(-1, 0) }; t.Step();
            Assert.AreEqual(-1, t.Info(0).Facing, "Facing follows movement again when the attack finishes.");
        }

        [TestCase(FighterState.Hit)]
        [TestCase(FighterState.KO)]
        public void InterruptionsIgnoreTheStickButPreservePhysicalKnockback(FighterState state)
        {
            using var t = Create(); t.Game.Input = new InputFrame { Held = 1, Move = new float2(1, 0) }; t.Step(3);
            var f = t.Info(0); f.State = state; f.StateTime = 0; f.VelocityX = -3;
            if (state == FighterState.KO) f.Hp = 0;
            t.World.Column(BwKeys.Info).Set(0, f); var before = Ground(t); t.Step();
            Assert.AreEqual(-3f / 60, Ground(t).x - before.x, .000001f);
            Assert.AreEqual(-3 * math.exp(-7f / 60), t.Info(0).VelocityX, .000001f);
            Assert.IsFalse(Weapon(t).Busy); Assert.IsFalse(Weapon(t).Equipment.BufferedAttack.Pending);
            Assert.AreEqual(0, Weapon(t).ActiveProjectiles); Assert.AreEqual(0, t.World.Column(BwBeltKeys.Motion)[0].ComboGraceTicks);
        }

        [Test]
        public void DiagonalInputIsBoundedAndWallSlidesWithoutAStoredMovementVector()
        {
            using var t = Create();
            t.World.Column(BwBeltKeys.Ground).Set(0, new float2(BwRules.ArenaHalf - .005f, -1));
            t.Game.Input = new InputFrame { Held = 1, Move = new float2(1, 1) }; t.Step();
            var before = Ground(t); t.Step(); var after = Ground(t);
            Assert.AreEqual(BwRules.ArenaHalf, after.x);
            Assert.AreEqual(BwRules.PlayerSpeed * .75f / math.sqrt(2) / 60, after.y - before.y, .000001f);
            t.Game.Input = new InputFrame { Held = 1 }; t.Step(); Assert.AreEqual(after, Ground(t));
            t.Game.Input = new InputFrame { Held = 1, Move = new float2(-1, 0) }; t.Step(); Assert.Less(Ground(t).x, after.x);
        }

        [TestCase(WeaponProfiles.Staff)]
        [TestCase(WeaponProfiles.Bow)]
        public void MovingRangedReleaseUsesTheFinalRootAndTheUnchangedReleaseMarker(int id)
        {
            using var t = Create(); Equip(t, id);
            t.Game.Input = new InputFrame { Held = 1, Move = new float2(.3f, .2f), Aim = new float2(1, 0) }; t.Step();
            var w = Weapon(t);
            for (int i = 0; i < w.Current.ReleaseTick; i++)
            {
                Assert.AreEqual(0, w.Releases); t.Step();
            }
            Assert.AreEqual(w.Current.ReleaseTick, w.Equipment.Timeline.Tick); Assert.AreEqual(1, w.Releases);
            var shot = w.Projectiles[0];
            Assert.AreEqual(w.Tick, shot.SpawnTick); Assert.AreEqual(shot.Position, shot.Previous);
            Assert.Less(math.distance(Ground(t) + w.Equipment.Aim * w.Current.MuzzleOffset.x * BwWeapons.ActorScale, shot.Position), .000001f);
            Assert.AreEqual(w.Current.MuzzleOffset.y * BwWeapons.ActorScale, shot.Height, .000001f);
        }

        [Test]
        public void CancelAndEquipmentRecoveryKeepMovementAndDoNotReleaseTheCanceledShot()
        {
            using var t = Create(); Equip(t, WeaponProfiles.Bow);
            t.Game.Input = new InputFrame { Held = 1, Move = new float2(.3f, 0) }; t.Step(4);
            t.Game.Input = new InputFrame { Pressed = 1u << BwWeapons.SwitchButton, Move = new float2(.3f, 0) }; t.Step();
            t.Game.Input = new InputFrame { Move = new float2(.3f, 0) };
            for (int i = 0; i < 35; i++) { var before = Ground(t); t.Step(); Assert.Greater(Ground(t).x, before.x); }
            Assert.AreEqual(WeaponProfiles.Blade, Weapon(t).Current.ContentId);
            Assert.AreEqual(0, Weapon(t).Releases); Assert.IsFalse(Weapon(t).Equipment.BufferedAttack.Pending);
        }

        [Test]
        public void MovingMeleeStillSettlesOnlyTheAuthoredContactOncePerPulse()
        {
            using var t = Create(); t.World.Column(BwBeltKeys.Ground).Set(1, new float2(1.2f, 0));
            float hp = t.Info(1).Hp; var w = Weapon(t);
            t.Game.Input = new InputFrame { Pressed = 1, Move = new float2(.2f, 0) }; t.Step();
            for (int i = 1; i <= w.Current.Active.From; i++)
            { Assert.AreEqual(hp, t.Info(1).Hp); t.Step(); }
            Assert.AreEqual(hp - w.Current.Damage, t.Info(1).Hp); Assert.AreEqual(1, w.AcceptedHits);
            t.Game.Input = new InputFrame { Move = new float2(.2f, 0) }; t.Step(w.Current.DurationTicks);
            Assert.AreEqual(hp - w.Current.Damage, t.Info(1).Hp); Assert.AreEqual(1, w.AcceptedHits);
        }

        [Test]
        public void KickKeepsItsClockFacingAndCooldownWhileTranslatingThroughEveryPhase()
        {
            using var t = Create(); var rig = t.World.Resource(BwKeys.Rig); var kick = rig.Attack(AttackKind.Kick);
            t.Game.Input = new InputFrame { Pressed = 1u << BwButton.Kick, Move = new float2(.2f, 0) }; t.Step();
            Assert.AreEqual(AttackKind.Kick, t.Info(0).Attack);
            int phases = 0;
            for (int i = 0; i < 25; i++)
            {
                t.Game.Input = new InputFrame { Move = new float2(-.2f, 0) };
                var before = Ground(t); t.Step(); var f = t.Info(0);
                float scale = f.StateTime < kick.ActiveFrom ? .75f : f.StateTime < kick.ActiveTo ? .70f : .80f;
                phases |= f.StateTime < kick.ActiveFrom ? 1 : f.StateTime < kick.ActiveTo ? 2 : 4;
                Assert.AreEqual(-.2f * BwRules.PlayerSpeed * scale / 60, Ground(t).x - before.x, .000001f);
                Assert.AreEqual(1, f.Facing); Assert.AreEqual(AttackKind.Kick, f.Attack);
            }
            Assert.AreEqual(7, phases); Assert.AreEqual(1, t.World.Resource(BwMobileSkills.Key).GetSnapshot(1).Charges);
        }

        [Test]
        public void PauseSamePolicyRestoreAndRestartDoNotRetainStaleInputOrChangeReplay()
        {
            using var a = Create(); using var b = Create();
            a.Game.Input = new InputFrame { Held = 1, Move = new float2(.2f, 0) }; a.Step(15);
            byte[] raw = a.Session.CaptureSnapshot(); var before = Ground(a);
            a.Session.Pause(); a.Session.Update(.3f); a.Session.Sync(); Assert.AreEqual(before, Ground(a)); a.Session.Resume();
            b.Session.RestoreSnapshot(raw);
            for (int i = 0; i < 90; i++)
            {
                a.Game.Input = b.Game.Input = new InputFrame { Held = 1, Move = new float2(i % 30 < 15 ? .3f : -.3f, .1f) };
                a.Step(); b.Step();
            }
            CollectionAssert.AreEqual(a.Session.CaptureSnapshot(), b.Session.CaptureSnapshot());
            a.Game.Send(BwCommandKind.Menu); a.Step(); Assert.AreEqual(0, a.Count); Assert.IsFalse(Weapon(a).Busy);
            a.Game.Send(BwCommandKind.Start); a.Step(); before = Ground(a); a.Step();
            Assert.AreEqual(before, Ground(a)); Assert.IsFalse(Weapon(a).Busy); Assert.AreEqual(0, a.Game.Input.Held);
        }

        [Test]
        public void LegacyDefaultRemainsLockedAndUnknownPolicyIsRejected()
        {
            Assert.AreEqual(BwBeltPlayerMobility.Legacy, BwBeltConfig.Default.PlayerMobility);
            using var t = Create(false); t.Game.Input = new InputFrame { Held = 1, Move = new float2(.3f, 0) }; t.Step();
            var before = Ground(t); t.Step(70); Assert.AreEqual(before, Ground(t));
            var config = BwBeltConfig.Default; config.PlayerMobility = (BwBeltPlayerMobility)255;
            Assert.Throws<ArgumentOutOfRangeException>(() => config.Validate());
        }

        [Test]
        public void EnemyPursuitAttacksAndStationaryPlayerKeepTheLegacySimulation()
        {
            using var legacy = new BwTestWorld(belt: BwBeltConfig.Default, weapons: true);
            using var smooth = new BwTestWorld(belt: BwBeltConfig.SmoothAttack, weapons: true);
            for (int i = 0; i < 240; i++) { legacy.Step(); smooth.Step(); }
            CollectionAssert.AreEqual(legacy.Session.CaptureSnapshot(), smooth.Session.CaptureSnapshot());
            Assert.Less(smooth.Info(smooth.Player).Hp, smooth.Info(smooth.Player).MaxHp, "Enemy approach and combat actually occurred.");
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void EnvelopeRejectsCrossPolicyBeforeMutationWithIdenticalRawLayouts(int recipe)
        {
            using var legacy = new PolicySession(false, recipe); using var smooth = new PolicySession(true, recipe);
            var oldDescriptor = legacy.Describe(); var descriptor = smooth.Describe();
            Assert.AreEqual(oldDescriptor.SchemaFingerprint, descriptor.SchemaFingerprint);
            Assert.AreEqual(oldDescriptor.RawCompatibilityFingerprint, descriptor.RawCompatibilityFingerprint);
            Assert.AreNotEqual(oldDescriptor.ContentFingerprint, descriptor.ContentFingerprint);
            CollectionAssert.AreEqual(legacy.Session.CaptureSnapshot(), smooth.Session.CaptureSnapshot(), "Rule choice adds no raw state or bytes.");
            var raw = smooth.Session.CaptureSnapshot(); var revision = smooth.Session.TimelineRevision;
            smooth.Session.RequestTicks(2); var pending = smooth.Session.PendingTicks;
            using var stream = new MemoryStream(SaveEnvelope.Capture(legacy.Session, oldDescriptor));
            Assert.Throws<InvalidDataException>(() => SaveEnvelope.Restore(stream, smooth.Session, descriptor));
            Assert.AreEqual(revision, smooth.Session.TimelineRevision); Assert.AreEqual(pending, smooth.Session.PendingTicks);
            CollectionAssert.AreEqual(raw, smooth.Session.CaptureSnapshot());
        }

        [TestCase(1)]
        [TestCase(2)]
        public void ComposedSkillsKeepAdmissionHealingAndInterruptionWithMovingKick(int recipe)
        {
            using var t = new PolicySession(true, recipe); var world = t.Session.World;
            BwSpawner.Spawn(world, 0, 0, 1, 0); BwSpawner.Spawn(world, 1, new float2(8, 0), -1, 0);
            var game = world.Resource(BwKeys.Game); game.Flow = BwFlow.Fighting;
            var rule = world.Resource(BwComposedAbilityState.Key); var f = world.Column(BwKeys.Info)[0]; f.Hp = 40; world.Column(BwKeys.Info).Set(0, f);
            game.Input = new InputFrame { Pressed = 1u << BwButton.Kick, Move = new float2(.3f, 0) }; t.Session.Step();
            for (int i = 0; i < 28; i++) { game.Input = new InputFrame { Move = new float2(.3f, 0) }; var before = world.Column(BwBeltKeys.Ground)[0]; t.Session.Step(); Assert.Greater(world.Column(BwBeltKeys.Ground)[0].x, before.x); }
            Assert.AreEqual(1, rule.AcceptedKicks);
            game.Input = new InputFrame { Pressed = 1u << BwBeltRules.HealButton }; t.Session.Step();
            Assert.AreEqual(1, rule.AcceptedHeals); Assert.AreEqual(40, world.Column(BwKeys.Info)[0].Hp);
            for (int i = 0; i < BwComposedAbilityState.HealReleaseTick; i++)
            { game.Input = new InputFrame { Held = 1, Pressed = 1u << BwButton.Kick, Move = new float2(1, 0) }; var before = world.Column(BwBeltKeys.Ground)[0]; t.Session.Step(); Assert.AreEqual(before, world.Column(BwBeltKeys.Ground)[0]); }
            Assert.AreEqual(40 + rule.Config.HealAmount, world.Column(BwKeys.Info)[0].Hp); Assert.AreEqual(1, rule.AppliedHeals);
            Assert.AreEqual(1, rule.AcceptedKicks); Assert.IsFalse(world.Resource(BwWeapons.Key).Busy);
            f = world.Column(BwKeys.Info)[0]; f.State = FighterState.Hit; f.StateTime = 0; world.Column(BwKeys.Info).Set(0, f); t.Session.Step();
            Assert.IsFalse(world.Resource(BwWeapons.PoseKey).Running); Assert.IsFalse(world.Resource(BwWeapons.Key).Busy);
        }

        [Test]
        public void SustainedMovingAttackTicksRemainAllocationFreeWithCalibratedControls()
        {
            using var t = Create(); Equip(t, WeaponProfiles.Bow);
            Action work = () => { for (int i = 0; i < 360; i++) { t.Game.Input = new InputFrame { Held = 1, Move = new float2(i % 60 < 30 ? .3f : -.3f, 0), Aim = new float2(1, 0) }; t.Step(); } };
            work(); using var probe = new ManagedAllocationProbe();
            var pre = probe.Calibrate(); var measured = probe.Measure(work); var post = probe.Calibrate();
            TestContext.WriteLine($"Smooth attack movement: {measured.Value} {measured.Metric}; controls {pre.RetainedArrays.Value}/{pre.Empty.Value}, {post.RetainedArrays.Value}/{post.Empty.Value}; current-thread logic only.");
            Assert.AreEqual(0, measured.Value); Assert.Greater(Weapon(t).Releases, 1);
        }

        sealed class PolicySession : IDisposable
        {
            readonly ModeDefinition mode; readonly GameplayModuleAsset module; readonly int recipe;
            public readonly SimSession Session;
            public PolicySession(bool smooth, int recipe)
            {
                this.recipe = recipe; var config = smooth ? BwBeltConfig.SmoothAttack : BwBeltConfig.Default;
                mode = recipe == 0 ? BwMode.CreateWeaponBelt(config, out module) : recipe == 1 ?
                    BwMode.CreateComposedAbilityBelt(config, BwComposedAbilityConfig.Default, out module) :
                    BwMode.CreateDamageNumbersBelt(config, BwComposedAbilityConfig.Default, CriticalDamageRule.Default, out module);
                Session = SimSession.Create(mode, 7); Session.Start();
            }
            public SaveCompatibilityDescriptor Describe() => recipe == 0 ? BwWeaponSave.Describe(Session, "mobility-tests") : recipe == 1 ?
                BwComposedAbilitySave.Describe(Session, "mobility-tests") : BwDamageNumbersSave.Describe(Session, "mobility-tests");
            public void Dispose() { Session.Dispose(); UnityEngine.Object.DestroyImmediate(mode); UnityEngine.Object.DestroyImmediate(module); }
        }
    }
}
