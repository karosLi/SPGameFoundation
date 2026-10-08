using System;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Contracts.Weapons;
using SPF.L1.Skeleton;
using SPF.L2.Weapons;
using SPF.Presentation.Animation;
using SPF.Testing;
using Unity.Collections;
using Unity.Mathematics;

namespace SPF.Tests.EditMode
{
    /// <summary>Final-FK regressions for the 14-bone procedural cutout, not the independent weighted BAT rig.</summary>
    public class HumanoidActionMotionTests
    {
        static GameplayCharacterInput Input(int weapon, float facing = 1)
        {
            int kind = weapon - 1001;
            float contact = kind == 0 ? .31f : kind == 1 ? .38f : kind == 2 ? .41f : .60f;
            return new GameplayCharacterInput
            {
                Handle = new EntityHandle(7, 1), Facing = facing, Scale = 1, Tint = new float4(1),
                Weapon = new WeaponViewState
                {
                    ContentId = weapon, VisualId = weapon, Family = (WeaponActionFamily)(kind + 1), Stage = WeaponStage.Idle,
                    ContactPhase = contact, ReleasePhase = contact, ActiveEndPhase = contact + .14f,
                    AimDirection = new float2(facing, 0),
                    GripOffset = new float2(kind == 3 ? .65f : kind == 2 ? .45f : .58f, kind == 3 ? 1.42f : kind == 2 ? 1.30f : 1.25f),
                    SecondaryGripOffset = new float2(kind == 3 ? .1f : .23f, kind == 3 ? 1.43f : 1.18f),
                    MuzzleOffset = new float2(kind == 0 ? 1.65f : kind == 1 ? 2.12f : kind == 2 ? 1.25f : .94f, kind == 3 ? 1.42f : kind == 2 ? 1.65f : 1.25f)
                }
            };
        }

        static float Marker(in WeaponViewState weapon) => weapon.Family == WeaponActionFamily.Cast || weapon.Family == WeaponActionFamily.Draw
            ? weapon.ReleasePhase : weapon.ContactPhase;

        static void ActionPhase(ref GameplayCharacterInput input, float phase)
        {
            input.State = GameplayCharacterState.Attack;
            input.Phase = input.Weapon.Phase = phase;
            input.Weapon.Stage = phase < Marker(input.Weapon) ? WeaponStage.Windup
                : phase < input.Weapon.ActiveEndPhase ? WeaponStage.Active : WeaponStage.Recovery;
        }

        [TestCase(1003, .35f)] [TestCase(1004, .22f)]
        public void CastAndBowAnticipationLiftRenderedHandAfterFinalIk(int weapon, float minimumLift)
        {
            using (var rig = NaturalCharacterRig.Create())
            using (var local = new NativeArray<BoneLocal>(NaturalCharacterRig.Bones, Allocator.Temp))
            using (var world = new NativeArray<BoneWorld>(NaturalCharacterRig.Bones, Allocator.Temp))
            {
                Assert.AreEqual(14, rig.View.BoneCount);
                for (int role = 0; role < 3; role++) for (int face = -1; face <= 1; face += 2)
                {
                    var input = Input(weapon, face); input.MotionProfileId = role; input.Scale = .66f;
                    var motion = default(GameplayCharacterMotion);
                    for (int frame = 0; frame < 120; frame++) motion.Step(input, 1f / 120);
                    GameplayCharacterMotion.Pose(rig.View, local, world, input, motion, 0);
                    float2 idleHand = world[NaturalCharacterRig.Hand].Position;
                    float2 idleElbow = world[NaturalCharacterRig.NearForearm].Position;
                    for (int frame = 0; frame <= 120; frame++)
                    {
                        ActionPhase(ref input, Marker(input.Weapon) * (weapon == 1001 ? .5f : .55f) * frame / 120);
                        motion.Step(input, 1f / 120);
                        GameplayCharacterMotion.Pose(rig.View, local, world, input, motion, 0);
                    }
                    float2 hand = world[NaturalCharacterRig.Hand].Position;
                    float2 shoulder = world[NaturalCharacterRig.NearArm].Position;
                    string context = "weapon=" + weapon + " role=" + role + " face=" + face;
                    // Casting and bow-raising require elevation. Blade and sword have separate
                    // trajectory/extension/clearance controls; a universal lift criterion rewarded
                    // the rejected sword pump. Always inspect the final result after weapon IK.
                    Assert.Greater((hand.y - idleHand.y) / input.Scale, minimumLift, context);
                    Assert.Greater((hand.y - shoulder.y) / input.Scale, -.015f, "readable shoulder-height anticipation " + context);
                    Assert.Greater((world[NaturalCharacterRig.NearForearm].Position.y - idleElbow.y) / input.Scale,
                        weapon == 1001 ? .2f : weapon == 1004 ? .1f : .12f, "upper arm must visibly lift the elbow " + context);
                    Assert.Greater(math.distance(idleElbow, world[NaturalCharacterRig.NearForearm].Position) / input.Scale, .18f,
                        "the elbow must participate in the hand lift " + context);
                    Assert.Less(math.distance(hand, WeaponMotion.Sample(input, motion).Grip) / input.Scale, .003f,
                        "final hand must follow the authored anticipation socket " + context);
                }
            }
        }

        [TestCase(30)] [TestCase(60)] [TestCase(120)]
        public void WristTracksGripWithinAnatomicalLimitAtBothFacings(int hz)
        {
            using (var rig = NaturalCharacterRig.Create())
            using (var local = new NativeArray<BoneLocal>(NaturalCharacterRig.Bones, Allocator.Temp))
            using (var world = new NativeArray<BoneWorld>(NaturalCharacterRig.Bones, Allocator.Temp))
            {
                int aligned = 0, corrected = 0;
                for (int weapon = 1001; weapon <= 1004; weapon++) for (int face = -1; face <= 1; face += 2)
                {
                    var input = Input(weapon, face); input.Velocity = new float2(.8f * face, .2f);
                    var motion = default(GameplayCharacterMotion);
                    for (int frame = 0; frame <= hz; frame++)
                    {
                        input.Root = input.Ground += input.Velocity / hz;
                        ActionPhase(ref input, frame / (float)hz); motion.Step(input, 1f / hz);
                        GameplayCharacterMotion.Pose(rig.View, local, world, input, motion, 0);
                        var socket = WeaponMotion.Attach(input, motion, world, 0);
                        float forearmError = math.abs(WeaponMotion.AngleDelta(world[NaturalCharacterRig.NearForearm].Rotation, socket.Rotation));
                        float handError = math.abs(WeaponMotion.AngleDelta(world[NaturalCharacterRig.Hand].Rotation, socket.Rotation));
                        string context = "weapon=" + weapon + " face=" + face + " frame=" + frame;
                        Assert.LessOrEqual(math.abs(local[NaturalCharacterRig.Hand].Rotation), .8501f, context);
                        Assert.LessOrEqual(handError, math.max(0, forearmError - .84f) + .001f,
                            "rendered wrist must close the reachable angular gap " + context);
                        if (forearmError < .8f) { Assert.Less(handError, .001f, context); aligned++; }
                        if (forearmError > .2f) corrected++;
                    }
                    input.Weapon = default;
                    motion.Step(input, 1f / hz);
                    GameplayCharacterMotion.CorrectContacts(rig.View, local, input, motion, 0);
                    Assert.AreEqual(0, local[NaturalCharacterRig.Hand].Rotation, "cached pose must clear the equipped wrist after unequip");
                }
                Assert.Greater(aligned, hz, "exercise actual alignment rather than only the angular clamp");
                Assert.Greater(corrected, hz, "exercise wrists whose bind pose would visibly miss the grip");
            }
        }

        [Test]
        public void DistinctReleaseMarkersKeepCanonicalSocketsDuringSkillOverlap()
        {
            using (var rig = NaturalCharacterRig.Create())
            using (var local = new NativeArray<BoneLocal>(NaturalCharacterRig.Bones, Allocator.Temp))
            using (var world = new NativeArray<BoneWorld>(NaturalCharacterRig.Bones, Allocator.Temp))
            {
                for (int weapon = 1001; weapon <= 1004; weapon++) for (int direction = 0; direction < 8; direction++)
                for (int markerCase = 0; markerCase < 3; markerCase++)
                {
                    float angle = direction * math.PI / 4;
                    var input = Input(weapon, math.cos(angle) < 0 ? -1 : 1); input.Scale = .66f;
                    input.Weapon.AimDirection = new float2(math.cos(angle), math.sin(angle));
                    // Ranged profiles allow release anywhere inside the active window. Keep
                    // these distinct-marker cases valid authored content, not malformed views.
                    input.Weapon.ContactPhase = weapon >= 1003 ? .12f : .45f;
                    input.Weapon.ReleasePhase = markerCase == 0 ? .20f : markerCase == 1 ? .60f : .88f;
                    input.Weapon.ActiveEndPhase = math.max(.72f, input.Weapon.ReleasePhase + .07f);
                    input.SkillPoseId = 101; input.SkillWeight = 1; input.SkillPhase = .28f;
                    ActionPhase(ref input, Marker(input.Weapon));
                    var motion = default(GameplayCharacterMotion); motion.Step(input, 1f / 60);
                    GameplayCharacterMotion.Pose(rig.View, local, world, input, motion, 0);
                    var attachment = WeaponMotion.Attach(input, motion, world, 0);
                    string context = "weapon=" + weapon + " direction=" + direction + " release=" + input.Weapon.ReleasePhase;
                    Assert.Less(math.distance(attachment.PrimaryGrip, WeaponMotion.WorldOffset(input, input.Weapon.AimDirection, input.Weapon.GripOffset)), .003f, context);
                    Assert.Less(math.distance(attachment.Muzzle, WeaponMotion.WorldOffset(input, input.Weapon.AimDirection, input.Weapon.MuzzleOffset)), .003f, context);
                    if (weapon != 1001)
                    {
                        var planned = WeaponMotion.Sample(input, motion);
                        Assert.Less(math.distance(attachment.SupportGrip, planned.Support + attachment.PrimaryGrip - planned.Grip), .003f, context);
                    }
                    if (weapon == 1004) Assert.That(attachment.Draw, Is.EqualTo(1).Within(.00001f), context);
                }
            }
        }

        [Test]
        public void ReleasedBowStringRelaxesBeforeDrawingHandRecovers()
        {
            using (var rig = NaturalCharacterRig.Create())
            using (var local = new NativeArray<BoneLocal>(NaturalCharacterRig.Bones, Allocator.Temp))
            using (var world = new NativeArray<BoneWorld>(NaturalCharacterRig.Bones, Allocator.Temp))
            {
                for (int face = -1; face <= 1; face += 2) for (int aim = -2; aim <= 2; aim += 2)
                {
                    var input = Input(1004, face); input.Scale = .66f;
                    input.Weapon.AimDirection = new float2(math.cos(aim * math.PI / 4) * face, math.sin(aim * math.PI / 4));
                    var motion = default(GameplayCharacterMotion);
                    ActionPhase(ref input, input.Weapon.ReleasePhase); motion.Step(input, 1f / 120);
                    GameplayCharacterMotion.Pose(rig.View, local, world, input, motion, 0);
                    var release = WeaponMotion.Attach(input, motion, world, 0);
                    Assert.That(release.Draw, Is.EqualTo(1).Within(.00001f));
                    Assert.That(release.SupportWeight, Is.EqualTo(1).Within(.00001f), "drawing hand owns the string at release");
                    float2 drawnOffset = (release.SupportGrip - release.PrimaryGrip) / input.Scale;
                    float2 relaxedOffset = -input.Weapon.AimDirection * .10f;
                    for (int frame = 1; frame <= 12; frame++)
                    {
                        ActionPhase(ref input, input.Weapon.ReleasePhase + .10f * frame / 12);
                        motion.Step(input, 1f / 120);
                        GameplayCharacterMotion.Pose(rig.View, local, world, input, motion, 0);
                    }
                    var after = WeaponMotion.Attach(input, motion, world, 0);
                    Assert.Less(after.Draw, .0001f, "string has returned after the existing .09-phase recoil");
                    Assert.Less(after.SupportWeight, .0001f, "released string must detach from the following-through hand");
                    float2 heldOffset = (after.SupportGrip - after.PrimaryGrip) / input.Scale;
                    Assert.Greater(math.dot(heldOffset - relaxedOffset, drawnOffset - relaxedOffset),
                        math.lengthsq(drawnOffset - relaxedOffset) * .65f, "drawing hand remains back while string recoils");
                    float2 stringPull = math.lerp(after.PrimaryGrip - after.Direction * .10f * input.Scale, after.SupportGrip, after.SupportWeight);
                    Assert.Greater(math.distance(stringPull, after.SupportGrip) / input.Scale, .25f, "rendered string must visibly separate from the released fingers");
                    ActionPhase(ref input, 1); motion.Step(input, 1f / 120);
                    GameplayCharacterMotion.Pose(rig.View, local, world, input, motion, 0);
                    var recovered = WeaponMotion.Attach(input, motion, world, 0);
                    Assert.Less(math.distance((recovered.SupportGrip - recovered.PrimaryGrip) / input.Scale, relaxedOffset), .003f,
                        "drawing hand returns to its relaxed hold by the end of recovery");
                }
            }
        }

        [TestCase(30)] [TestCase(60)] [TestCase(120)]
        public void MovingActionsKeepFinalJointsContinuousAndFeetPlanted(int hz)
        {
            using (var rig = NaturalCharacterRig.Create())
            using (var local = new NativeArray<BoneLocal>(NaturalCharacterRig.Bones, Allocator.Temp))
            using (var world = new NativeArray<BoneWorld>(NaturalCharacterRig.Bones, Allocator.Temp))
            using (var previous = new NativeArray<BoneWorld>(NaturalCharacterRig.Bones, Allocator.Temp))
            {
                var previousWritable = previous;
                var defaults = WeaponProfiles.CreateDefaults(60);
                for (int weapon = 1001; weapon <= 1004; weapon++) for (int face = -1; face <= 1; face += 2)
                for (int aim = -2; aim <= 2; aim++)
                {
                    var profile = defaults[weapon - 1001];
                    float duration = profile.DurationTicks / 60f;
                    var input = Input(weapon, face); input.Scale = .66f; input.Velocity = new float2(.55f * face, .12f);
                    input.Weapon.ContactPhase = profile.Active.From / (float)profile.DurationTicks;
                    input.Weapon.ReleasePhase = profile.ReleaseTick / (float)profile.DurationTicks;
                    input.Weapon.ActiveEndPhase = profile.Active.Until / (float)profile.DurationTicks;
                    input.Weapon.AimDirection = new float2(math.cos(aim * math.PI / 4) * face, math.sin(aim * math.PI / 4));
                    var motion = default(GameplayCharacterMotion);
                    for (int frame = -hz; frame <= (int)math.ceil(hz * duration); frame++)
                    {
                        input.Root = input.Ground += input.Velocity / hz;
                        if (frame >= 0) ActionPhase(ref input, math.min(1, frame / (hz * duration)));
                        var before = motion; motion.Step(input, 1f / hz);
                        GameplayCharacterMotion.Pose(rig.View, local, world, input, motion, 0);
                        if (frame >= 0)
                        {
                            string context = "weapon=" + weapon + " face=" + face + " aim=" + aim + " Hz=" + hz + " frame=" + frame;
                            Assert.IsTrue(motion.FarFoot.InStance || motion.NearFoot.InStance, "moving attack cannot become a hop " + context);
                            AssertContact(before.FarFoot, motion.FarFoot, world[NaturalCharacterRig.FarFoot].Position, input.Scale, context);
                            AssertContact(before.NearFoot, motion.NearFoot, world[NaturalCharacterRig.NearFoot].Position, input.Scale, context);
                            for (int bone = 0; bone < NaturalCharacterRig.Bones; bone++)
                            {
                                Assert.IsTrue(math.all(math.isfinite(world[bone].Position)) && math.isfinite(world[bone].Rotation), context);
                                Assert.Less(math.distance(previous[bone].Position, world[bone].Position) / input.Scale,
                                    12f / hz + .002f, "bounded rendered displacement bone=" + bone + " " + context);
                                Assert.Less(math.abs(WeaponMotion.AngleDelta(previous[bone].Rotation, world[bone].Rotation)),
                                    28f / hz + .025f, "bounded rendered rotation bone=" + bone + " " + context);
                            }
                        }
                        for (int bone = 0; bone < NaturalCharacterRig.Bones; bone++) previousWritable[bone] = world[bone];
                    }
                }
            }
        }

        static void AssertContact(in FootPlantState before, in FootPlantState after, float2 actual, float scale, string context)
        {
            if (!after.InStance) return;
            if (before.Initialized && before.InStance) Assert.AreEqual(before.Plant, after.Plant, "world-space plant slid " + context);
            Assert.Less(math.distance(actual, after.Position * scale), .0002f, "reported support must match final FK " + context);
        }

        struct GaitMeasure { public float Cycles; public int Landings; public float2 Root; }

        static GaitMeasure MeasureWalk(in SkeletonView rig, NativeArray<BoneLocal> local, NativeArray<BoneWorld> world,
            int role, float speed, int hz, bool baseline)
        {
            var input = Input(1001); input.Weapon = default; input.MotionProfileId = role;
            input.State = GameplayCharacterState.Walk; input.Velocity = new float2(speed, 0);
            if (baseline)
            {
                // Pinned authored walk inputs from c0c2cc6, evaluated by the SAME gait solver and
                // root trajectory. This isolates cadence from movement-speed or solver changes.
                var profile = GameplayMotionProfiles.Get(role);
                profile.WalkStride = role == 0 ? 1.4f : role == 1 ? 1.25f : 1.65f;
                profile.WalkPeriod = role == 0 ? 1.08f : role == 1 ? .95f : 1.18f;
                input.MotionProfile = profile;
            }
            var motion = default(GameplayCharacterMotion); var measure = default(GaitMeasure); int doubleSupport = 0;
            for (int frame = 0; frame < hz * 14; frame++)
            {
                input.Root = input.Ground += input.Velocity / hz;
                var before = motion; motion.Step(input, 1f / hz);
                GameplayCharacterMotion.Pose(rig, local, world, input, motion, 0);
                if (frame < hz * 2) continue;
                Assert.AreEqual(GameplayLocomotionState.Walk, motion.Locomotion);
                Assert.IsTrue(motion.FarFoot.InStance || motion.NearFoot.InStance, "faster walking must retain support");
                AssertContact(before.FarFoot, motion.FarFoot, world[NaturalCharacterRig.FarFoot].Position, 1, "walk far");
                AssertContact(before.NearFoot, motion.NearFoot, world[NaturalCharacterRig.NearFoot].Position, 1, "walk near");
                if (motion.FarFoot.InStance && motion.NearFoot.InStance) doubleSupport++;
                if (!before.FarFoot.InStance && motion.FarFoot.InStance) measure.Landings++;
                if (!before.NearFoot.InStance && motion.NearFoot.InStance) measure.Landings++;
                measure.Cycles += math.frac(motion.Phase - before.Phase + 1);
            }
            Assert.Greater(doubleSupport, 0, "walk retains a double-support interval");
            measure.Root = input.Root; return measure;
        }

        [TestCase(30)] [TestCase(60)] [TestCase(120)]
        public void EveryBodyTypeWalksFasterAtTheSameAuthoritativeRootSpeed(int hz)
        {
            using (var rig = NaturalCharacterRig.Create())
            using (var local = new NativeArray<BoneLocal>(NaturalCharacterRig.Bones, Allocator.Temp))
            using (var world = new NativeArray<BoneWorld>(NaturalCharacterRig.Bones, Allocator.Temp))
            {
                for (int role = 0; role < 3; role++) foreach (float speed in new[] { .5f, 1.5f })
                {
                    var before = MeasureWalk(rig.View, local, world, role, speed, hz, true);
                    var after = MeasureWalk(rig.View, local, world, role, speed, hz, false);
                    string context = "role=" + role + " speed=" + speed + " Hz=" + hz;
                    Assert.AreEqual(before.Root, after.Root, "cadence must not change authoritative travel " + context);
                    Assert.Greater(after.Cycles / before.Cycles, role == 2 && speed > 1 ? 1.07f : 1.12f, "visible cadence gain " + context);
                    Assert.Greater(after.Landings, before.Landings, "actual foot contacts must speed up too " + context);
                }
            }
        }

        [Test]
        public void BladeBalancingHandJoinsGuardDuringActualDurationAction()
        {
            using (var rig = NaturalCharacterRig.Create())
            using (var local = new NativeArray<BoneLocal>(NaturalCharacterRig.Bones, Allocator.Temp))
            using (var world = new NativeArray<BoneWorld>(NaturalCharacterRig.Bones, Allocator.Temp))
            {
                var profile = WeaponProfiles.CreateDefaults(60)[0];
                for (int face = -1; face <= 1; face += 2)
                {
                    var input = Input(1001, face);
                    input.Weapon.ContactPhase = profile.Active.From / (float)profile.DurationTicks;
                    input.Weapon.ActiveEndPhase = profile.Active.Until / (float)profile.DurationTicks;
                    var motion = default(GameplayCharacterMotion);
                    for (int frame = 0; frame < 120; frame++) motion.Step(input, 1f / 120);
                    GameplayCharacterMotion.Pose(rig.View, local, world, input, motion, 0);
                    float idleHeight = world[NaturalCharacterRig.FarForearm].Transform(new float2(.42f, 0), face).y;
                    float peakHeight = idleHeight;
                    // The balancing arm has its own envelope; it need not peak at the weapon's
                    // earlier hand key, but must visibly participate during the actual action.
                    for (int frame = 0; frame <= profile.DurationTicks * 2; frame++)
                    {
                        ActionPhase(ref input, frame / (profile.DurationTicks * 2f));
                        motion.Step(input, 1f / 120);
                        GameplayCharacterMotion.Pose(rig.View, local, world, input, motion, 0);
                        peakHeight = math.max(peakHeight, world[NaturalCharacterRig.FarForearm].Transform(new float2(.42f, 0), face).y);
                    }
                    Assert.Greater(peakHeight - idleHeight, .15f, "free hand must visibly join the action guard");
                }
            }
        }

        [TestCase(30)] [TestCase(60)] [TestCase(120)]
        public void CancellingBladeWindupKeepsBalancingArmContinuous(int hz)
        {
            using (var rig = NaturalCharacterRig.Create())
            using (var local = new NativeArray<BoneLocal>(NaturalCharacterRig.Bones, Allocator.Temp))
            using (var world = new NativeArray<BoneWorld>(NaturalCharacterRig.Bones, Allocator.Temp))
            {
                for (int face = -1; face <= 1; face += 2)
                {
                    var input = Input(1001, face); input.Velocity = new float2(.65f * face, .1f);
                    var motion = default(GameplayCharacterMotion);
                    for (int frame = 0; frame < hz; frame++)
                    {
                        input.Root = input.Ground += input.Velocity / hz;
                        motion.Step(input, 1f / hz);
                    }
                    for (int frame = 0; frame <= hz / 5; frame++)
                    {
                        input.Root = input.Ground += input.Velocity / hz;
                        ActionPhase(ref input, input.Weapon.ContactPhase * .65f * frame / (hz / 5));
                        motion.Step(input, 1f / hz);
                    }
                    GameplayCharacterMotion.Pose(rig.View, local, world, input, motion, 0);
                    var previousArm = world[NaturalCharacterRig.FarArm];
                    var previousForearm = world[NaturalCharacterRig.FarForearm];
                    float2 previousHand = previousForearm.Transform(new float2(.42f, 0), face);
                    input.State = GameplayCharacterState.Walk; input.Phase = 0;
                    input.Weapon.Stage = WeaponStage.Idle; input.Weapon.Phase = 0;
                    for (int frame = 0; frame < hz; frame++)
                    {
                        input.Root = input.Ground += input.Velocity / hz; motion.Step(input, 1f / hz);
                        GameplayCharacterMotion.Pose(rig.View, local, world, input, motion, 0);
                        var arm = world[NaturalCharacterRig.FarArm]; var forearm = world[NaturalCharacterRig.FarForearm];
                        float2 hand = forearm.Transform(new float2(.42f, 0), face);
                        string context = "face=" + face + " Hz=" + hz + " cancellation frame=" + frame;
                        Assert.Less(math.distance(previousForearm.Position, forearm.Position), 12f / hz + .002f, "balancing elbow " + context);
                        Assert.Less(math.distance(previousHand, hand), 12f / hz + .002f, "balancing hand " + context);
                        Assert.Less(math.abs(WeaponMotion.AngleDelta(previousArm.Rotation, arm.Rotation)), 28f / hz + .025f, "balancing upper arm " + context);
                        Assert.Less(math.abs(WeaponMotion.AngleDelta(previousForearm.Rotation, forearm.Rotation)), 28f / hz + .025f, "balancing forearm " + context);
                        previousArm = arm; previousForearm = forearm; previousHand = hand;
                    }
                }
            }
        }

        [TestCase(30)] [TestCase(60)] [TestCase(120)]
        public void UnarmedPunchAndCastStayContinuousThroughHitRecoveryAndDeath(int hz)
        {
            using (var rig = NaturalCharacterRig.Create())
            using (var local = new NativeArray<BoneLocal>(NaturalCharacterRig.Bones, Allocator.Temp))
            using (var world = new NativeArray<BoneWorld>(NaturalCharacterRig.Bones, Allocator.Temp))
            using (var previous = new NativeArray<BoneWorld>(NaturalCharacterRig.Bones, Allocator.Temp))
            {
                var previousWritable = previous;
                foreach (var action in new[] { GameplayCharacterAction.Punch, GameplayCharacterAction.Cast })
                for (int face = -1; face <= 1; face += 2)
                {
                    var input = Input(1001, face); input.Weapon = default; input.Action = action;
                    var motion = default(GameplayCharacterMotion);
                    float2 idleHand = 0; float attackExcursion = 0;
                    for (int frame = -hz; frame <= hz * 3; frame++)
                    {
                        float time = frame / (float)hz;
                        input.State = time < 0 ? GameplayCharacterState.Walk : time < .8f ? GameplayCharacterState.Attack
                            : time < 1.2f ? GameplayCharacterState.Hit : time < 1.8f ? GameplayCharacterState.Recovery : GameplayCharacterState.Death;
                        input.Phase = time < 0 ? 0 : time < .8f ? time / .8f : 1;
                        input.Velocity = time < 1.8f ? new float2(.65f * face, .1f) : float2.zero;
                        input.Root = input.Ground += input.Velocity / hz;
                        var before = motion; motion.Step(input, 1f / hz);
                        GameplayCharacterMotion.Pose(rig.View, local, world, input, motion, 0);
                        if (frame == -1) idleHand = world[NaturalCharacterRig.Hand].Position - input.Root;
                        if (frame >= 0 && time < .8f)
                            attackExcursion = math.max(attackExcursion, math.distance(idleHand, world[NaturalCharacterRig.Hand].Position - input.Root));
                        if (frame >= 0)
                        {
                            string context = "action=" + action + " face=" + face + " Hz=" + hz + " frame=" + frame + " state=" + input.State;
                            Assert.IsTrue(motion.FarFoot.InStance || motion.NearFoot.InStance, "grounded reaction needs support " + context);
                            AssertContact(before.FarFoot, motion.FarFoot, world[NaturalCharacterRig.FarFoot].Position, 1, context);
                            AssertContact(before.NearFoot, motion.NearFoot, world[NaturalCharacterRig.NearFoot].Position, 1, context);
                            for (int bone = 0; bone < NaturalCharacterRig.Bones; bone++)
                            {
                                Assert.Less(math.distance(previous[bone].Position, world[bone].Position), 12f / hz + .002f,
                                    "unarmed/reaction position must not pop bone=" + bone + " " + context);
                                Assert.Less(math.abs(WeaponMotion.AngleDelta(previous[bone].Rotation, world[bone].Rotation)), 28f / hz + .025f,
                                    "unarmed/reaction angle must not flip bone=" + bone + " " + context);
                            }
                        }
                        for (int bone = 0; bone < NaturalCharacterRig.Bones; bone++) previousWritable[bone] = world[bone];
                    }
                    Assert.Greater(attackExcursion, .25f, "continuous action must still produce visible final hand motion");
                    Assert.Greater(motion.Death, .99f, "exercise the settled death pose, not only its first frame");
                }
            }
        }

        [Test]
        public void WarmedMovingActionLayersAllocateZeroWithPositiveAndEmptyControls()
        {
            using (var rig = NaturalCharacterRig.Create())
            using (var local = new NativeArray<BoneLocal>(NaturalCharacterRig.Bones, Allocator.Temp))
            using (var world = new NativeArray<BoneWorld>(NaturalCharacterRig.Bones, Allocator.Temp))
            {
                var input = Input(1001); var motion = default(GameplayCharacterMotion);
                Action work = () =>
                {
                    for (int weapon = 1001; weapon <= 1004; weapon++) for (int role = 0; role < 3; role++)
                    {
                        input = Input(weapon); input.MotionProfileId = role; input.Velocity = new float2(.8f, .2f);
                        for (int frame = 0; frame < 120; frame++)
                        {
                            input.Root = input.Ground += input.Velocity / 120;
                            ActionPhase(ref input, frame / 120f); motion.Step(input, 1f / 120);
                            GameplayCharacterMotion.Pose(rig.View, local, world, input, motion, 0);
                            WeaponMotion.Attach(input, motion, world, 0);
                        }
                    }
                };
                work();
                using (var probe = new ManagedAllocationProbe())
                {
                    probe.Calibrate(); var measured = probe.Measure(work); probe.Calibrate();
                    Assert.AreEqual(0, measured.Value, "warmed movement, phase-authored arms, final FK and sockets");
                }
            }
        }
    }
}
