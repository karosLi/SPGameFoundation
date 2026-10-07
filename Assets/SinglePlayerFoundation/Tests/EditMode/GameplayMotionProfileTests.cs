using System;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Contracts.Weapons;
using SPF.L1.Skeleton;
using SPF.Presentation;
using SPF.Presentation.Animation;
using SPF.Testing;
using Unity.Collections;
using Unity.Mathematics;

namespace SPF.Tests.EditMode
{
    public class GameplayMotionProfileTests
    {
        static GameplayCharacterInput Input()=>new GameplayCharacterInput {Handle=new EntityHandle(9,1),Facing=1,Scale=.66f,Tint=new float4(1)};
        static WeaponViewState Weapon(int kind)=>new WeaponViewState {ContentId=1001+kind,VisualId=1001+kind,Family=(WeaponActionFamily)(kind+1),Stage=WeaponStage.Active,
            Phase=.4f,ContactPhase=.4f,ReleasePhase=.4f,ActiveEndPhase=.57f,AimDirection=new float2(1,0),
            GripOffset=new float2(kind==3?.65f:kind==2?.45f:.58f,kind==3?1.42f:kind==2?1.30f:1.25f),
            SecondaryGripOffset=new float2(kind==3?.1f:.23f,kind==3?1.43f:1.18f),MuzzleOffset=new float2(kind==0?1.65f:kind==1?2.12f:kind==2?1.25f:.94f,kind==3?1.42f:kind==2?1.65f:1.25f)};
        [Test] public void ClassifierHasStableIdleWalkRunHysteresisInCallerUnits()
        {
            var c=default(GameplayLocomotionClassifier);
            Assert.AreEqual(GameplayLocomotionState.Idle,c.Step(0,3,2.5f));
            Assert.AreEqual(GameplayLocomotionState.Walk,c.Step(1,3,2.5f));
            Assert.AreEqual(GameplayLocomotionState.Run,c.Step(3.1f,3,2.5f));
            for(int i=0;i<30;i++)Assert.AreEqual(GameplayLocomotionState.Run,c.Step(i%2==0?2.7f:3.01f,3,2.5f));
            Assert.AreEqual(GameplayLocomotionState.Walk,c.Step(2.4f,3,2.5f));
            Assert.AreEqual(GameplayLocomotionState.Idle,c.Step(.02f,3,2.5f));
            Assert.AreEqual(GameplayLocomotionState.Air,c.Step(4,3,2.5f,false));
            Assert.AreEqual(GameplayLocomotionState.Run,c.Step(2.8f,3,2.5f));
        }
        [TestCase(30)] [TestCase(60)] [TestCase(120)]
        public void WalkRunAndMovingAttackKeepContactPhaseContinuous(int hz)
        {
            var input=Input();input.Scale=1;var m=default(GameplayCharacterMotion);m.Step(input,1f/hz);
            float walkLift=0;
            for(int frame=0;frame<hz*3;frame++)
            {
                input.Velocity=new float2(frame<hz?1.5f:4,0);input.Root=input.Ground+=input.Velocity/hz;
                input.State=frame<hz*2?GameplayCharacterState.Run:GameplayCharacterState.Attack;input.Phase=math.frac(frame/(float)hz);
                var before=m;m.Step(input,1f/hz);
                if(frame==hz-1){Assert.AreEqual(GameplayLocomotionState.Walk,m.Locomotion);Assert.Greater(m.Walk,.99f);walkLift=m.SwingHeight;}
                if(frame>=hz*2){Assert.AreEqual(GameplayLocomotionState.Run,m.Locomotion);Assert.Greater(m.Move,.99f);Assert.Greater(m.SwingHeight,walkLift+.04f);}
                if(before.FarFoot.InStance&&m.FarFoot.InStance)Assert.AreEqual(before.FarFoot.Plant,m.FarFoot.Plant);
                if(before.NearFoot.InStance&&m.NearFoot.InStance)Assert.AreEqual(before.NearFoot.Plant,m.NearFoot.Plant);
            }
        }
        [Test] public void RoleProfilesHaveDistinctSupportAndGaitWithoutDifferentRigObjects()
        {
            var agile=GameplayMotionProfiles.Get(1);var heavy=GameplayMotionProfiles.Get(2);
            Assert.IsTrue(GameplayMotionProfiles.Valid(agile));Assert.IsTrue(GameplayMotionProfiles.Valid(heavy));
            Assert.Greater(heavy.FootWidth,agile.FootWidth+.04f);Assert.Greater(agile.RunArm,heavy.RunArm+.2f);
            Assert.Greater(heavy.MinimumPeriod,agile.MinimumPeriod+.05f);Assert.Greater(agile.HitRecoil,heavy.HitRecoil);
            var input=Input();input.MotionProfileId=2;var m=default(GameplayCharacterMotion);m.Step(input,.016f);
            Assert.That(m.NearFoot.Plant.x-m.FarFoot.Plant.x,Is.EqualTo(heavy.FootWidth*2).Within(1e-6f));
        }
        [Test] public void SkillProfilesAreDistinctAndCanBeAuthoredWithoutAddingAnEnum()
        {
            var pulse=GameplaySkillProfiles.Get(101);var heal=GameplaySkillProfiles.Get(105);var blink=GameplaySkillProfiles.Get(102);
            var a=GameplaySkillProfiles.Sample(pulse,pulse.Contact,1);var b=GameplaySkillProfiles.Sample(heal,heal.Contact,1);var c=GameplaySkillProfiles.Sample(blink,blink.Contact,1);
            Assert.Greater(math.distance(a.FarHand,b.FarHand),.5f);Assert.Greater(b.WeaponAngle-a.WeaponAngle,1);Assert.Greater(c.PelvisDrop,a.PelvisDrop+.1f);
            var custom=pulse;custom.Id=9001;custom.FarHand=new float2(-.5f,1.7f);Assert.IsTrue(GameplaySkillProfiles.Valid(custom));
            var input=Input();input.SkillProfile=custom;input.SkillPoseId=9001;input.SkillWeight=1;input.SkillPhase=custom.Contact;
            var m=default(GameplayCharacterMotion);m.Step(input,.016f);Assert.That(math.distance(m.Skill.Pose.FarHand,custom.FarHand),Is.LessThan(1e-6f));
        }
        [Test] public void SkillInterruptBlendsOutAndTeleportStartsAtTheNewIdentityPose()
        {
            var input=Input();input.SkillPoseId=102;input.SkillPhase=.34f;input.SkillWeight=1;input.SkillPulse=7;
            var m=default(GameplayCharacterMotion);m.Step(input,.016f);var before=m.Skill.Pose;
            input.SkillWeight=0;m.Step(input,1f/120);Assert.That(m.Skill.Pose.Body,Is.EqualTo(before.Body).Within(1e-6f));
            for(int i=0;i<30;i++)m.Step(input,1f/120);Assert.That(math.abs(m.Skill.Pose.Body),Is.LessThan(.0001f));
            input.Teleported=true;input.Root=input.Ground=new float2(30,10);m.Step(input,.016f);Assert.That(m.Skill.Pose.NearWeight,Is.Zero);
        }
        [Test] public void ConcurrentSkillsCannotMoveAuthoritativeWeaponReleaseSockets()
        {
            using(var rig=NaturalCharacterRig.Create())
            using(var local=new NativeArray<BoneLocal>(NaturalCharacterRig.Bones,Allocator.Temp))
            using(var world=new NativeArray<BoneWorld>(NaturalCharacterRig.Bones,Allocator.Temp))
            {
                for(int skillId=100;skillId<=105;skillId++)for(int weapon=0;weapon<4;weapon++)for(int aim=0;aim<8;aim++)
                {
                    var input=Input();input.Weapon=Weapon(weapon);float a=aim*math.PI/4;input.Weapon.AimDirection=new float2(math.cos(a),math.sin(a));input.Facing=math.cos(a)<0?-1:1;
                    input.SkillPoseId=skillId;input.SkillPhase=GameplaySkillProfiles.Get(skillId).Contact;input.SkillWeight=1;input.SkillPulse=1;
                    input.State=GameplayCharacterState.Attack;input.Phase=.4f;input.Velocity=new float2(0,3.5f);
                    var m=default(GameplayCharacterMotion);
                    for(int i=0;i<45;i++)
                    {
                        input.Root=input.Ground+=input.Velocity/60;m.Step(input,1f/60);GameplayCharacterMotion.Pose(rig.View,local,world,input,m,0);
                        var socket=WeaponMotion.Attach(input,m,world,0);var expected=WeaponMotion.WorldOffset(input,input.Weapon.AimDirection,input.Weapon.MuzzleOffset);
                        Assert.That(math.distance(socket.Muzzle,expected),Is.LessThan(.003f),"skill="+skillId+" weapon="+weapon+" aim="+aim);
                        if(weapon>0){var pose=WeaponMotion.Sample(input,m);Assert.That(math.distance(socket.SupportGrip,pose.Support+socket.PrimaryGrip-pose.Grip),Is.LessThan(.003f),"secondary contact annulus");}
                    }
                }
            }
        }
        [Test] public void InvalidAuthoredProfilesAreRejectedBeforeBatchSubmission()
        {
            using(var p=new GameplayCharacterPresenter(RenderTier.DataTexture,1))
            {
                var input=Input();input.MotionProfile=new GameplayLocomotionProfile{Id=99};p.Begin(.016f,0);Assert.IsFalse(p.Submit(input));
                input.MotionProfile=GameplayMotionProfiles.Get(0);input.MotionProfile.Breath=float.NaN;Assert.IsFalse(p.Submit(input));
                input.MotionProfile=default;input.SkillProfile=GameplaySkillProfiles.Get(101);input.SkillProfile.NearHand.x=float.PositiveInfinity;Assert.IsFalse(p.Submit(input));
                input.SkillProfile=default;Assert.IsTrue(p.Submit(input));p.Evaluate();
            }
        }
        [Test] public void ProfileLayersRemainAllocationFreeWhenWarmed()
        {
            var input=Input();input.MotionProfile=GameplayMotionProfiles.Get(2);input.SkillProfile=GameplaySkillProfiles.Get(101);input.SkillWeight=1;input.Velocity=new float2(3,0);var m=default(GameplayCharacterMotion);
            Action work=()=>{for(int i=0;i<240;i++){input.Root=input.Ground+=input.Velocity/120;input.SkillPhase=math.frac(i/120f);m.Step(input,1f/120);}};
            work();using(var probe=new ManagedAllocationProbe()){probe.Calibrate();var sample=probe.Measure(work);probe.Calibrate();Assert.AreEqual(0,sample.Value);}
        }
    }
}
