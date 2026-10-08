using NUnit.Framework;
using SPF.Contracts;
using SPF.Contracts.Weapons;
using SPF.L1.Skeleton;
using SPF.L2.Weapons;
using SPF.Presentation.Animation;
using Unity.Collections;
using Unity.Mathematics;

namespace SPF.Tests.EditMode
{
    /// <summary>Final-chain regression for the user-rejected late wrist drop. Native 1x review remains required.</summary>
    public class BladeControlledRecoveryTests
    {
        static GameplayCharacterInput Input(int face, int role, bool moving, int tickRate=60)
        {
            var p = WeaponProfiles.CreateDefaults(tickRate)[0];
            return new GameplayCharacterInput { Handle = new EntityHandle(7, 1), Facing = face, Scale = .66f,
                Kind = role % 2, MotionProfileId = role, Velocity = moving ? new float2(.8f * face, .2f) : float2.zero,
                Weapon = new WeaponViewState { ContentId = p.ContentId, VisualId = p.VisualId, Family = p.Family,
                    Stage = WeaponStage.Idle, AimDirection = new float2(face, 0), GripOffset = p.GripOffset,
                    SecondaryGripOffset = p.SecondaryGripOffset, MuzzleOffset = p.MuzzleOffset,
                    ContactPhase = p.Active.From / (float)p.DurationTicks, ReleasePhase = p.ReleaseTick / (float)p.DurationTicks,
                    ActiveEndPhase = p.Active.Until / (float)p.DurationTicks } };
        }
        static void Pose(SkeletonView rig, NativeArray<BoneLocal> local, NativeArray<BoneWorld> world,
            ref GameplayCharacterInput input, ref GameplayCharacterMotion motion, float phase, float dt)
        {
            input.Root = input.Ground += input.Velocity * dt;
            if (phase >= 0) {
                input.State = GameplayCharacterState.Attack; input.Phase = input.Weapon.Phase = phase;
                input.Weapon.Stage = phase < input.Weapon.ContactPhase ? WeaponStage.Windup
                    : phase < input.Weapon.ActiveEndPhase ? WeaponStage.Active : WeaponStage.Recovery;
            }
            motion.Step(input, dt); GameplayCharacterMotion.Pose(rig, local, world, input, motion, 0);
        }
        [TestCase(30)] [TestCase(60)] [TestCase(120)]
        public void BladeBrakesIntoCompactFinishWithoutLateWristDominance(int hz)
        {
            using (var rig = NaturalCharacterRig.Create())
            using (var local = new NativeArray<BoneLocal>(NaturalCharacterRig.Bones, Allocator.Temp))
            using (var world = new NativeArray<BoneWorld>(NaturalCharacterRig.Bones, Allocator.Temp))
            for (int tickRate=30;tickRate<=60;tickRate+=30)
            for (int role = 0; role < 3; role++) for (int face = -1; face <= 1; face += 2) for (int move = 0; move < 2; move++) {
                var input = Input(face, role, move != 0,tickRate); var motion = default(GameplayCharacterMotion);
                for (int frame = 0; frame < hz; frame++) Pose(rig.View, local, world, ref input, ref motion, -1, 1f / hz);
                float c = input.Weapon.ContactPhase, end = input.Weapon.ActiveEndPhase;
                float duration=WeaponProfiles.CreateDefaults(tickRate)[0].DurationTicks/(float)tickRate;
                int before = (int)math.ceil(c * duration * hz);
                for (int i = 0; i <= before; i++) Pose(rig.View, local, world, ref input, ref motion, c * i / before, 1f / hz);
                float contactWrist = local[NaturalCharacterRig.Hand].Rotation;
                float contactFore = world[NaturalCharacterRig.NearForearm].Rotation;
                float contactBlade = WeaponMotion.Attach(input, motion, world, 0).Rotation;
                float maxWristDrop = 0, maxBladeDrop = 0, wristAtFinish = 0, foreAtFinish = 0;
                int count = (int)math.ceil((end - c) * duration * hz);
                for (int i = 1; i <= count; i++) {
                    Pose(rig.View, local, world, ref input, ref motion, math.lerp(c, end, i / (float)count), 1f / hz);
                    float bladeDrop = -WeaponMotion.AngleDelta(contactBlade, WeaponMotion.Attach(input, motion, world, 0).Rotation) * face;
                    float wristDrop = contactWrist - local[NaturalCharacterRig.Hand].Rotation;
                    maxWristDrop = math.max(maxWristDrop, wristDrop);
                    if (bladeDrop > maxBladeDrop) {
                        maxBladeDrop = bladeDrop; wristAtFinish = wristDrop;
                        foreAtFinish = -WeaponMotion.AngleDelta(contactFore, world[NaturalCharacterRig.NearForearm].Rotation) * face;
                    }
                }
                string context = "Hz=" + hz + " tick=" + tickRate + " role=" + role + " face=" + face + " move=" + move
                    + " sweep=" + maxBladeDrop + " wrist=" + maxWristDrop + " fore=" + foreAtFinish;
                TestContext.WriteLine(context);
                Assert.Less(maxBladeDrop, .36f, "compact finish preserves the cut without the late hanging blade " + context);
                Assert.Greater(maxBladeDrop, .18f, "retain purposeful follow-through " + context);
                Assert.Less(maxWristDrop, .16f, "palm articulates modestly after contact " + context);
                Assert.Greater(foreAtFinish, wristAtFinish, "forearm carries most remaining cut " + context);
            }
        }
        [Test]
        public void BladeRetainsExactContactAndIncomingTangent()
        {
            var input = Input(1, 0, false); var motion = default(GameplayCharacterMotion);
            motion.Step(input, 1f / 60); input.Weapon.Stage = WeaponStage.Active;
            float c = input.Weapon.ContactPhase, end = input.Weapon.ActiveEndPhase, h = .00001f;
            input.Weapon.Phase = c - h; var before = WeaponMotion.Sample(input, motion);
            input.Weapon.Phase = c; var contact = WeaponMotion.Sample(input, motion);
            input.Weapon.Phase = c + h; var after = WeaponMotion.Sample(input, motion);
            float2 expected = WeaponMotion.WorldOffset(input, input.Weapon.AimDirection, input.Weapon.GripOffset);
            Assert.That(math.distance(contact.Grip, expected), Is.LessThan(.000001f));
            Assert.That(math.abs(contact.Angle), Is.LessThan(.000001f));
            float angleTangent = (-.58f - 1.30f) / (end - c * .5f);
            Assert.That((contact.Angle - before.Angle) / h, Is.EqualTo(angleTangent).Within(.02f));
            Assert.That((after.Angle - contact.Angle) / h, Is.EqualTo(angleTangent).Within(.02f));
            Assert.That(math.distance((contact.Grip-before.Grip)/h, (after.Grip-contact.Grip)/h), Is.LessThan(.02f));
        }
        [Test]
        public void BladeGripAngleAndBodySettleTogetherWithoutARecoveryKick()
        {
            var input = Input(1, 0, false); var motion = default(GameplayCharacterMotion);
            motion.Step(input, 1f / 60); input.Weapon.Stage = WeaponStage.Active;
            float c = input.Weapon.ContactPhase, settle = c + (input.Weapon.ActiveEndPhase - c) * .45f, h = .0001f;
            input.Weapon.Phase = settle - h; var before = WeaponMotion.Sample(input, motion);
            input.Weapon.Phase = settle; var at = WeaponMotion.Sample(input, motion);
            input.Weapon.Phase = settle + h; var after = WeaponMotion.Sample(input, motion);
            Assert.Less(math.distance(before.Grip, after.Grip) / (2 * h), .01f, "hand brakes at the same key as blade/chest");
            Assert.Less(math.abs(after.Angle-before.Angle) / (2*h), .01f, "no late wrist-driven blade drop");
            Assert.Less(math.abs(after.Body-before.Body) / (2*h), .01f, "chest does not recover ahead of hand");
            Assert.Less(math.distance(at.Grip-before.Grip, after.Grip-at.Grip) / h, .02f, "continuous hand velocity");
            input.Weapon.Phase = 1; var guard = WeaponMotion.Sample(input, motion);
            input.Weapon.Stage = WeaponStage.Idle; var idle = WeaponMotion.Sample(input, motion);
            Assert.That(guard.Grip, Is.EqualTo(idle.Grip)); Assert.That(guard.Angle, Is.EqualTo(idle.Angle));
        }
        [TestCase(30)] [TestCase(60)] [TestCase(120)]
        public void BladeContactSettleAndRecoveryInterruptWithoutSnapping(int hz)
        {
            for(int face=-1;face<=1;face+=2)for(int role=0;role<3;role++)for(int equip=0;equip<2;equip++)
            foreach(float phase in new[]{.34f,.43f,.72f}) {
                var input=Input(face,role,true);var motion=default(GameplayCharacterMotion);
                for(int i=0;i<hz;i++)motion.Step(input,1f/hz);
                input.State=GameplayCharacterState.Attack;input.Weapon.Stage=WeaponStage.Active;
                for(int i=0;i<=hz;i++){input.Phase=input.Weapon.Phase=phase*i/hz;motion.Step(input,1f/hz);}
                var previous=WeaponMotion.Sample(input,motion);float2 velocity=motion.HeldGripVelocity;
                input.Weapon.Stage=equip==0?WeaponStage.Idle:WeaponStage.Equipping;input.Weapon.Phase=0;
                input.Weapon.StagePhase=0;motion.Step(input,1f/hz);var next=WeaponMotion.Sample(input,motion);
                Assert.Less(math.distance(previous.Grip,next.Grip),.000001f);
                Assert.Less(math.abs(WeaponMotion.AngleDelta(previous.Angle,next.Angle)),.000001f);
                Assert.Less(math.distance(velocity,motion.ChangeGripVelocity),.000001f);
                for(int i=0;i<hz;i++){
                    motion.Step(input,1f/hz);next=WeaponMotion.Sample(input,motion);
                    Assert.IsTrue(math.all(math.isfinite(next.Grip)));Assert.IsTrue(math.isfinite(next.Angle));
                    Assert.Less(math.distance(previous.Grip,next.Grip),.18f,"bounded recovery after cancellation/equip");previous=next;
                }
            }
        }
    }
}
