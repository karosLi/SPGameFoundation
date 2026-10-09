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
    public class BladeAuthoredArcTests
    {
        [TestCase(30)] [TestCase(60)] [TestCase(120)]
        public void LoadedBladeRaisesTheUpperArmAndElbowAwayFromRibs(int hz)
        {
            using(var rig=NaturalCharacterRig.Create())
            using(var local=new NativeArray<BoneLocal>(NaturalCharacterRig.Bones,Allocator.Temp))
            using(var world=new NativeArray<BoneWorld>(NaturalCharacterRig.Bones,Allocator.Temp))
            for(int face=-1;face<=1;face+=2)for(int role=0;role<3;role++)for(int moving=0;moving<2;moving++)
            {
                var p=WeaponProfiles.CreateDefaults(60)[0];
                var input=new GameplayCharacterInput { Handle=new EntityHandle(7,1), Facing=face, Scale=.66f, MotionProfileId=role,
                    Velocity=moving!=0?new float2(.8f*face,.2f):float2.zero,
                    Weapon=new WeaponViewState { ContentId=p.ContentId,VisualId=p.VisualId,Family=p.Family,Stage=WeaponStage.Idle,
                        AimDirection=new float2(face,0),GripOffset=p.GripOffset,MuzzleOffset=p.MuzzleOffset,SecondaryGripOffset=p.SecondaryGripOffset,
                        ContactPhase=p.Active.From/(float)p.DurationTicks,ActiveEndPhase=p.Active.Until/(float)p.DurationTicks } };
                var motion=default(GameplayCharacterMotion);
                for(int i=0;i<hz;i++){input.Root=input.Ground+=input.Velocity/hz;motion.Step(input,1f/hz);}
                float phase=input.Weapon.ContactPhase*.5f;
                int count=(int)math.ceil(phase*p.DurationTicks/60f*hz);
                for(int i=0;i<=count;i++)
                {
                    input.Root=input.Ground+=input.Velocity/hz;
                    input.Phase=input.Weapon.Phase=phase*i/count;input.State=GameplayCharacterState.Attack;input.Weapon.Stage=WeaponStage.Windup;
                    motion.Step(input,1f/hz);GameplayCharacterMotion.Pose(rig.View,local,world,input,motion,0);
                }
                float2 shoulder=world[NaturalCharacterRig.NearArm].Position;
                float2 elbow=world[NaturalCharacterRig.NearForearm].Position;
                float2 hand=world[NaturalCharacterRig.Hand].Position;
                string context="hz="+hz+" face="+face+" role="+role+" moving="+moving;
                Assert.Greater((elbow.y-shoulder.y)/input.Scale,.17f,"upper arm visibly rises, not merely wrist "+context);
                Assert.Greater((elbow.x-shoulder.x)*face/input.Scale,.20f,"elbow opens away from ribs "+context);
                Assert.Greater((hand.y-shoulder.y)/input.Scale,.40f,"whole arm loads overhead "+context);
                Assert.That(math.distance(shoulder,elbow)/input.Scale,Is.EqualTo(.43f).Within(.00001f));
                Assert.That(math.distance(elbow,hand)/input.Scale,Is.EqualTo(.42f).Within(.00001f));
                var blade=WeaponMotion.Attach(input,motion,world,0);
                Assert.Less(math.distance(blade.PrimaryGrip,hand),.000001f);
                Assert.Less(math.abs(WeaponMotion.AngleDelta(blade.Rotation,world[NaturalCharacterRig.Hand].Rotation)),.000001f);
            }
        }
    }
}
