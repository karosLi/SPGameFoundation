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
    /// <summary>Family-specific final-FK red controls for the rejected e023387 native footage.
    /// These geometry checks are necessary, not a substitute for normal-speed native review.</summary>
    public class BladeSwordChoreographyTests
    {
        static GameplayCharacterInput Input(int weapon, int face, int role, bool moving, int tickRate)
        {
            var p = WeaponProfiles.CreateDefaults(tickRate)[weapon - 1001];
            return new GameplayCharacterInput {
                Handle = new EntityHandle(7, 1), Facing = face, Scale = .66f, MotionProfileId = role,
                Velocity = moving ? new float2(.8f * face, .2f) : float2.zero,
                Weapon = new WeaponViewState { ContentId = p.ContentId, VisualId = p.VisualId, Family = p.Family,
                    Stage = WeaponStage.Idle, AimDirection = new float2(face, 0), GripOffset = p.GripOffset,
                    SecondaryGripOffset = p.SecondaryGripOffset, MuzzleOffset = p.MuzzleOffset,
                    ContactPhase = p.Active.From / (float)p.DurationTicks, ReleasePhase = p.ReleaseTick / (float)p.DurationTicks,
                    ActiveEndPhase = p.Active.Until / (float)p.DurationTicks }
            };
        }
        static void Sample(in SkeletonView rig, NativeArray<BoneLocal> local, NativeArray<BoneWorld> world,
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
        static float2 Model(in BoneWorld bone, in GameplayCharacterInput input) =>
            NaturalMotion.ModelPoint(bone.Position, input.Root, input.Facing, input.Scale);

        [TestCase(1001)] [TestCase(1002)]
        public void IdleMeleeWeaponDoesNotTakeOverKickBody(int weapon)
        {
            using (var rig = NaturalCharacterRig.Create())
            using (var local = new NativeArray<BoneLocal>(NaturalCharacterRig.Bones, Allocator.Temp))
            using (var world = new NativeArray<BoneWorld>(NaturalCharacterRig.Bones, Allocator.Temp))
            {
                var input = Input(weapon, 1, 0, false, 60);
                input.State = GameplayCharacterState.Attack; input.Action = GameplayCharacterAction.Kick; input.Phase = .42f;
                var motion = default(GameplayCharacterMotion); motion.Step(input, 1f / 60);
                GameplayCharacterMotion.Pose(rig.View, local, world, input, motion, 0);
                float activeBody = local[NaturalCharacterRig.Torso].Rotation;
                var neutral = motion; neutral.Attack = 0;
                GameplayCharacterMotion.Pose(rig.View, local, world, input, neutral, 0);
                Assert.That(activeBody - local[NaturalCharacterRig.Torso].Rotation, Is.EqualTo(-.12f).Within(.00001f),
                    "an idle held weapon must preserve the existing generic kick body contribution");
            }
        }

        [TestCase(30)] [TestCase(60)] [TestCase(120)]
        public void SwordChambersRearwardAndExtendsAlongThrustLine(int hz)
        {
            using (var rig = NaturalCharacterRig.Create())
            using (var local = new NativeArray<BoneLocal>(NaturalCharacterRig.Bones, Allocator.Temp))
            using (var world = new NativeArray<BoneWorld>(NaturalCharacterRig.Bones, Allocator.Temp))
            for (int tickRate = 30; tickRate <= 60; tickRate += 30)
            for (int role = 0; role < 3; role++) for (int face = -1; face <= 1; face += 2) for (int move = 0; move < 2; move++)
            {
                var input = Input(1002, face, role, move != 0, tickRate); var motion = default(GameplayCharacterMotion);
                for (int frame = 0; frame < hz; frame++) Sample(rig.View, local, world, ref input, ref motion, -1, 1f / hz);
                float2 idle = Model(world[NaturalCharacterRig.Hand], input);
                float chamberPhase = input.Weapon.ContactPhase * .55f;
                float duration = WeaponProfiles.CreateDefaults(tickRate)[1].DurationTicks / (float)tickRate;
                int chamberFrames = (int)math.ceil(chamberPhase * duration * hz);
                for (int frame = 0; frame <= chamberFrames; frame++)
                    Sample(rig.View, local, world, ref input, ref motion, chamberPhase * frame / chamberFrames, 1f / hz);
                float2 chamber = Model(world[NaturalCharacterRig.Hand], input), shoulder = Model(world[NaturalCharacterRig.NearArm], input);
                float chamberFlex = local[NaturalCharacterRig.NearForearm].Rotation;
                int extendFrames = (int)math.ceil((input.Weapon.ContactPhase - chamberPhase) * duration * hz);
                for (int frame = 1; frame <= extendFrames; frame++)
                    Sample(rig.View, local, world, ref input, ref motion, math.lerp(chamberPhase, input.Weapon.ContactPhase, frame / (float)extendFrames), 1f / hz);
                float2 contact = Model(world[NaturalCharacterRig.Hand], input), contactShoulder = Model(world[NaturalCharacterRig.NearArm], input);
                float advance = contact.x - chamber.x, rise = math.abs(contact.y - chamber.y), opening = chamberFlex - local[NaturalCharacterRig.NearForearm].Rotation;
                string context = "Hz=" + hz + " tick=" + tickRate + " role=" + role + " face=" + face + " move=" + move
                    + " rear=" + (idle.x - chamber.x) + " forward=" + advance + " vertical=" + rise + " elbowOpening=" + opening;
                TestContext.WriteLine(context);
                Assert.Greater(idle.x - chamber.x, .12f, "chamber draws back from guard " + context);
                Assert.Greater(advance, .32f, "hand advances visibly along canonical thrust " + context);
                Assert.Less(rise, .16f, "thrust must not become the rejected vertical pump " + context);
                Assert.Greater(advance, rise * 2.5f, context);
                Assert.Greater(opening, .28f, "elbow opens rather than shoulder catching the hand " + context);
                Assert.Less(contactShoulder.x - shoulder.x, advance * .65f, context);
            }
        }

        [TestCase(30)] [TestCase(60)] [TestCase(120)]
        public void BladeDiagonalFollowThroughClearsGroundAndRecovers(int hz)
        {
            using (var rig = NaturalCharacterRig.Create())
            using (var local = new NativeArray<BoneLocal>(NaturalCharacterRig.Bones, Allocator.Temp))
            using (var world = new NativeArray<BoneWorld>(NaturalCharacterRig.Bones, Allocator.Temp))
            for (int role = 0; role < 3; role++) for (int face = -1; face <= 1; face += 2) for (int move = 0; move < 2; move++)
            {
                var input = Input(1001, face, role, move != 0, 60); var motion = default(GameplayCharacterMotion);
                for (int frame = 0; frame < hz; frame++) Sample(rig.View, local, world, ref input, ref motion, -1, 1f / hz);
                float2 idle = Model(world[NaturalCharacterRig.Hand], input);
                float low = 100, highHand = 0, follow = 0, top = 0, wristError = 0;
                int frames = (int)math.ceil(WeaponProfiles.CreateDefaults(60)[0].DurationTicks / 60f * hz);
                for (int frame = 0; frame <= frames; frame++) {
                    float phase = frame / (float)frames;
                    Sample(rig.View, local, world, ref input, ref motion, phase, 1f / hz);
                    var attachment = WeaponMotion.Attach(input, motion, world, 0);
                    low = math.min(low, (attachment.Tip.y - input.Root.y) / input.Scale);
                    wristError = math.max(wristError, math.abs(WeaponMotion.AngleDelta(world[NaturalCharacterRig.Hand].Rotation, attachment.Rotation)));
                    highHand = math.max(highHand, Model(world[NaturalCharacterRig.Hand], input).y);
                    float angle = math.atan2(attachment.Direction.y, attachment.Direction.x * face);
                    top = math.max(top, angle); if (phase >= input.Weapon.ContactPhase) follow = math.min(follow, angle);
                }
                string context = "Hz=" + hz + " role=" + role + " face=" + face + " move=" + move + " clearance=" + low + " follow=" + follow;
                TestContext.WriteLine(context);
                Assert.Less(wristError, .01f, "blade wrist and weapon align through the diagonal cut " + context);
                Assert.Greater(low, .30f, "full blade tip must clear boots/ground through follow-through " + context);
                Assert.Greater(highHand - idle.y, .20f, "diagonal cut retains visible anticipation " + context);
                Assert.Greater(top - follow, .95f, "follow-through is retained " + context);
                // The user superseded the old below-contact finish: preserve the complete
                // overhead cut, but recover from contact without another downward key.
                Assert.GreaterOrEqual(follow, -.00001f, "no post-contact downward finish " + context);
                Assert.Less(math.distance(Model(world[NaturalCharacterRig.Hand], input), idle), .05f, "returns to guard " + context);
            }
        }

        [TestCase(30)] [TestCase(60)] [TestCase(120)]
        public void SwordHandsStayAlignedWithVisibleHandle(int hz)
        {
            using (var rig = NaturalCharacterRig.Create())
            using (var local = new NativeArray<BoneLocal>(NaturalCharacterRig.Bones, Allocator.Temp))
            using (var world = new NativeArray<BoneWorld>(NaturalCharacterRig.Bones, Allocator.Temp))
            for (int face = -1; face <= 1; face += 2) for (int move = 0; move < 2; move++)
            {
                var input = Input(1002, face, 0, move != 0, 60); var motion = default(GameplayCharacterMotion);
                for (int frame = 0; frame < hz; frame++) Sample(rig.View, local, world, ref input, ref motion, -1, 1f / hz);
                float maxHandError = 0, minX = 100, maxX = -100, minY = 100, maxY = -100;
                int frames = (int)math.ceil(WeaponProfiles.CreateDefaults(60)[1].DurationTicks / 60f * hz);
                for (int frame = 0; frame <= frames; frame++) {
                    Sample(rig.View, local, world, ref input, ref motion, frame / (float)frames, 1f / hz);
                    var attachment = WeaponMotion.Attach(input, motion, world, 0);
                    float handError = math.abs(WeaponMotion.AngleDelta(world[NaturalCharacterRig.Hand].Rotation, attachment.Rotation));
                    float2 offset = (attachment.SupportGrip - attachment.PrimaryGrip) / input.Scale;
                    float pixelsPerUnit = 198 / WeaponMotion.Sample(input, motion).Length;
                    float x = 46 + math.dot(offset, attachment.Direction) * pixelsPerUnit;
                    float y = 48 + (attachment.Direction.x * offset.y - attachment.Direction.y * offset.x) * face * pixelsPerUnit;
                    maxHandError = math.max(maxHandError, handError); minX = math.min(minX, x); maxX = math.max(maxX, x); minY = math.min(minY, y); maxY = math.max(maxY, y);
                    string context = "Hz=" + hz + " face=" + face + " move=" + move + " frame=" + frame + " handError=" + handError + " art=" + x + "," + y;
                    Assert.Less(handError, .01f, "wrist and blade share grip orientation " + context);
                    Assert.That(x, Is.InRange(20f, 34f), "support palm lies on rear handle, separate from primary pivot (46,48) " + context);
                    Assert.That(y, Is.InRange(43f, 53f), "support palm lies across handle centerline " + context);
                }
                TestContext.WriteLine("Hz=" + hz + " face=" + face + " move=" + move + " maxHandError=" + maxHandError
                    + " artX=" + minX + ".." + maxX + " artY=" + minY + ".." + maxY);
            }
        }
    }
}
