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
    public class MobileActionCoordinationTests
    {
        [TestCase(30)] [TestCase(60)] [TestCase(120)]
        public void EveryRoleSkillWeaponContactKeepsBothHandsAndAuthoritativeSocket(int hz)
        {
            var profiles = WeaponProfiles.CreateDefaults(60);
            using var rig = NaturalCharacterRig.Create();
            using var local = new NativeArray<BoneLocal>(NaturalCharacterRig.Bones, Allocator.Temp);
            using var world = new NativeArray<BoneWorld>(NaturalCharacterRig.Bones, Allocator.Temp);
            float maxMuzzle = 0, maxSupport = 0;
            for (int role = 0; role < 3; role++) for (int skill = 100; skill <= 105; skill++)
            for (int weapon = 0; weapon < profiles.Length; weapon++) for (int direction = 0; direction < 8; direction++)
            {
                var profile = profiles[weapon]; float angle = direction * math.PI / 4;
                var input = new GameplayCharacterInput { Handle = new EntityHandle(1, 1), MotionProfileId = role,
                    Scale = .66f, Facing = math.cos(angle) < 0 ? -1 : 1, State = GameplayCharacterState.Attack,
                    Velocity = new float2(0, 1.2f), Tint = new float4(1), SkillPoseId = skill,
                    SkillPhase = GameplaySkillProfiles.Get(skill).Contact, SkillWeight = 1, SkillPulse = 1,
                    HitWeight = role == 0 ? 1 : 0,
                    Weapon = new WeaponViewState { ContentId = profile.ContentId, VisualId = profile.VisualId,
                        Family = profile.Family, Stage = WeaponStage.Active, ActionPulse = 1,
                        Phase = (float)profile.ReleaseTick / profile.DurationTicks,
                        ContactPhase = (float)profile.Active.From / profile.DurationTicks,
                        ReleasePhase = (float)profile.ReleaseTick / profile.DurationTicks,
                        ActiveEndPhase = (float)profile.Active.Until / profile.DurationTicks,
                        AimDirection = new float2(math.cos(angle), math.sin(angle)), GripOffset = profile.GripOffset,
                        SecondaryGripOffset = profile.SecondaryGripOffset, MuzzleOffset = profile.MuzzleOffset } };
                var motion = default(GameplayCharacterMotion);
                for (int frame = 0; frame < hz / 2; frame++)
                {
                    input.Root = input.Ground += input.Velocity / hz;
                    motion.Step(input, 1f / hz); GameplayCharacterMotion.Pose(rig.View, local, world, input, motion, 0);
                    var sample = WeaponMotion.Attach(input, motion, world, 0);
                    maxMuzzle = math.max(maxMuzzle, math.distance(sample.Muzzle, WeaponMotion.WorldOffset(input, input.Weapon.AimDirection, profile.MuzzleOffset)));
                    if (weapon != 0)
                    {
                        var pose = WeaponMotion.Sample(input, motion);
                        maxSupport = math.max(maxSupport, math.distance(sample.SupportGrip, pose.Support + sample.PrimaryGrip - pose.Grip));
                    }
                    for (int bone = 0; bone < world.Length; bone++)
                        Assert.That(math.all(math.isfinite(world[bone].Position)) && math.isfinite(world[bone].Rotation), Is.True);
                }
            }
            Assert.That(maxMuzzle, Is.LessThan(.003f)); Assert.That(maxSupport, Is.LessThan(.003f));
            TestContext.WriteLine("role/skill/weapon/aim contact matrix at " + hz + " Hz: muzzle=" + maxMuzzle + ", support=" + maxSupport);
        }
    }
}
