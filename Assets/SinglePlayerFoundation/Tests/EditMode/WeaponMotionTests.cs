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
    public class WeaponMotionTests
    {
        static GameplayCharacterInput Input(int weapon=1001,float facing=1)
        {
            int kind=weapon-1001;float contact=kind==0?.31f:kind==1?.38f:kind==2?.41f:.60f;
            return new GameplayCharacterInput {Handle=new EntityHandle(7,1),Facing=facing,Scale=1,Tint=new float4(1),
                Weapon=new WeaponViewState {ContentId=weapon,VisualId=weapon,Family=(WeaponActionFamily)(kind+1),Stage=WeaponStage.Idle,
                ContactPhase=contact,ReleasePhase=contact,ActiveEndPhase=contact+.14f,AimDirection=new float2(facing,0),
                GripOffset=new float2(kind==3?.65f:kind==2?.45f:.58f,kind==3?1.42f:kind==2?1.30f:1.25f),
                SecondaryGripOffset=new float2(kind==3?.1f:.23f,kind==3?1.43f:1.18f),
                MuzzleOffset=new float2(kind==0?1.65f:kind==1?2.12f:kind==2?1.25f:.94f,kind==3?1.42f:kind==2?1.65f:1.25f)}};
        }
        [Test] public void WeaponOptInPreservesClassicAtlasAndStreamBudget()
        {
            using(var classic=new GameplayCharacterPresenter(RenderTier.DataTexture,1))
            using(var equipped=new GameplayCharacterPresenter(RenderTier.DataTexture,1,includeWeapons:true))
            {
                Assert.That(classic.ColorAtlasBytes,Is.EqualTo(1024*1024));Assert.That(equipped.ColorAtlasBytes,Is.EqualTo(2*1024*1024));
                var input=Input();classic.Begin(.016f,0);Assert.IsFalse(classic.Submit(input),"weapon contract requires explicit allocated capability");
                input.Weapon=default;Assert.IsTrue(classic.Submit(input));classic.Evaluate();Assert.AreEqual(14,classic.PartsDrawn);
            }
        }
        [Test] public void CurvedBladeSpriteTipMatchesTheAttachmentAtBothFacings()
        {
            for(int face=-1;face<=1;face+=2)
            {float rotation=face<0?math.PI:0;float2 tip=new float2(201,20*face)/WeaponArt.TipPixels(1001);
                float2 rendered=WeaponMotion.Rotate(tip,rotation+WeaponArt.AngleOffset(1001)*face);
                Assert.That(math.distance(rendered,new float2(face,0)),Is.LessThan(.00001f));}
        }
        [Test] public void UnknownVisualUsesMatchingFamilyArtAndPivot()
        {
            for(int family=1;family<=4;family++)
            {int resolved=WeaponArt.Resolve(2001,(WeaponActionFamily)family);Assert.AreEqual(1000+family,resolved);Assert.AreEqual(family-1,WeaponArt.Index(resolved));Assert.IsTrue(WeaponArt.Supported(resolved));}
            Assert.AreEqual(1002,WeaponArt.Resolve(1002,WeaponActionFamily.Slash));
        }
        [Test] public void AuthoredEarlyAndLateMarkersAreNotRemappedToDefaultTimings()
        {
            var input=Input();input.Weapon.ContactPhase=.95f;input.Weapon.ActiveEndPhase=.98f;input.Weapon.Stage=WeaponStage.Active;input.Weapon.Phase=.95f;
            var motion=default(GameplayCharacterMotion);motion.Step(input,1f/60);var pose=WeaponMotion.Sample(input,motion);
            Assert.That(math.distance(pose.Grip,WeaponMotion.WorldOffset(input,input.Weapon.AimDirection,input.Weapon.GripOffset)),Is.LessThan(.0001f));
            Assert.That(WeaponMotion.Curve(.95f,.95f,.98f,0,-.4f,1,.6f),Is.EqualTo(1).Within(1e-6f));
            input=Input(1004);input.Weapon.ContactPhase=.01f;input.Weapon.ReleasePhase=.01f;input.Weapon.ActiveEndPhase=.02f;input.Weapon.Phase=.01f;input.Weapon.Stage=WeaponStage.Active;
            motion=default;motion.Step(input,1f/60);Assert.That(WeaponMotion.Sample(input,motion).Draw,Is.EqualTo(1).Within(1e-6f));
            input=Input(1003);input.Weapon.ContactPhase=.333333f;input.Weapon.ReleasePhase=.5f;input.Weapon.ActiveEndPhase=.666667f;input.Weapon.Phase=.5f;input.Weapon.Stage=WeaponStage.Active;
            motion=default;motion.Step(input,1f/60);pose=WeaponMotion.Sample(input,motion);
            Assert.That(math.distance(pose.Grip,WeaponMotion.WorldOffset(input,input.Weapon.AimDirection,input.Weapon.GripOffset)),Is.LessThan(.0001f));
            Assert.That(math.abs(pose.Angle-math.atan2(.35f,.8f)),Is.LessThan(.0001f),"staff aligns to its release marker even when release is later than active start");
        }
        [Test] public void WeaponCurvesPassThroughContactWithContinuousNonzeroVelocity()
        {
            float c=.37f,e=.53f,h=.0001f;
            Assert.That(WeaponMotion.Curve(c,c,e,0,-.4f,1,.6f),Is.EqualTo(1).Within(1e-6f));
            foreach(float t in new[]{c*.55f,c,e})
            {
                float before=(WeaponMotion.Curve(t,c,e,0,-.4f,1,.6f)-WeaponMotion.Curve(t-h,c,e,0,-.4f,1,.6f))/h;
                float after=(WeaponMotion.Curve(t+h,c,e,0,-.4f,1,.6f)-WeaponMotion.Curve(t,c,e,0,-.4f,1,.6f))/h;
                Assert.That(math.abs(before-after),Is.LessThan(.08f),"C1 velocity at authored key");
                if(t==c)Assert.That(math.abs(before),Is.GreaterThan(1),"contact must sweep through, not stop at each key");
            }
        }
        [TestCase(30)] [TestCase(60)] [TestCase(120)]
        public void AllWeaponContactsAndTwoHandGripsStayAttachedAtBothFacings(int hz)
        {
            using(var rig=NaturalCharacterRig.Create())
            using(var local=new NativeArray<BoneLocal>(NaturalCharacterRig.Bones,Allocator.Temp))
            using(var world=new NativeArray<BoneWorld>(NaturalCharacterRig.Bones,Allocator.Temp))
            {
                for(int id=1001;id<=1004;id++)for(int face=-1;face<=1;face+=2)
                {
                    var input=Input(id,face);var motion=default(GameplayCharacterMotion);
                    for(int frame=0;frame<hz;frame++){motion.Step(input,1f/hz);GameplayCharacterMotion.Pose(rig.View,local,world,input,motion,0);}
                    input.Weapon.Stage=WeaponStage.Active;input.Weapon.Phase=input.Weapon.ContactPhase;
                    motion.Step(input,1f/hz);GameplayCharacterMotion.Pose(rig.View,local,world,input,motion,0);
                    var sample=WeaponMotion.Attach(input,motion,world,0);var expected=WeaponMotion.WorldOffset(input,input.Weapon.AimDirection,input.Weapon.MuzzleOffset);
                    Assert.That(math.distance(sample.Muzzle,expected),Is.LessThan(.003f),"canonical contact/release socket "+id+" facing "+face);
                    Assert.That(math.distance(sample.PrimaryGrip,world[NaturalCharacterRig.Hand].Position),Is.LessThan(.00001f));
                    if(id!=1001)
                    {
                        var pose=WeaponMotion.Sample(input,motion);
                        Assert.That(math.distance(sample.SupportGrip,pose.Support+sample.PrimaryGrip-pose.Grip),Is.LessThan(.003f),"support hand must follow weapon/string "+id);
                    }
                }
            }
        }
        [Test] public void DirectionalReleaseSocketsStayAlignedWithCanonicalMuzzle()
        {
            using(var rig=NaturalCharacterRig.Create())
            using(var local=new NativeArray<BoneLocal>(NaturalCharacterRig.Bones,Allocator.Temp))
            using(var world=new NativeArray<BoneWorld>(NaturalCharacterRig.Bones,Allocator.Temp))
            {
                for(int id=1001;id<=1004;id++)for(int direction=0;direction<16;direction++)
                {
                    float a=direction*math.PI/8;var input=Input(id,math.cos(a)<0?-1:1);
                    input.Weapon.AimDirection=new float2(math.cos(a),math.sin(a));
                    input.Weapon.Stage=WeaponStage.Active;input.Weapon.Phase=input.Weapon.ContactPhase;
                    var motion=default(GameplayCharacterMotion);motion.Step(input,1f/60);
                    GameplayCharacterMotion.Pose(rig.View,local,world,input,motion,0);
                    var socket=WeaponMotion.Attach(input,motion,world,0);
                    Assert.That(math.distance(socket.Muzzle,WeaponMotion.WorldOffset(input,input.Weapon.AimDirection,input.Weapon.MuzzleOffset)),Is.LessThan(.003f),"weapon="+id+" direction="+direction);
                }
            }
        }
        [Test] public void MovingDepthSupportKeepsRangedReleaseSocketsReachable()
        {
            using(var rig=NaturalCharacterRig.Create())
            using(var local=new NativeArray<BoneLocal>(NaturalCharacterRig.Bones,Allocator.Temp))
            using(var world=new NativeArray<BoneWorld>(NaturalCharacterRig.Bones,Allocator.Temp))
            {
                for(int id=1003;id<=1004;id++)for(int travel=0;travel<4;travel++)for(int aim=0;aim<8;aim++)
                {
                    float a=aim*math.PI/4;var input=Input(id,math.cos(a)<0?-1:1);input.Scale=.66f;
                    input.Weapon.AimDirection=new float2(math.cos(a),math.sin(a));input.Weapon.Stage=WeaponStage.Active;input.Weapon.Phase=input.Weapon.ContactPhase;
                    input.Velocity=new float2(math.cos(travel*math.PI/2),math.sin(travel*math.PI/2))*5;input.State=GameplayCharacterState.Run;
                    var motion=default(GameplayCharacterMotion);
                    for(int frame=0;frame<120;frame++)
                    {
                        input.Root=input.Ground+=input.Velocity/120;motion.Step(input,1f/120);GameplayCharacterMotion.Pose(rig.View,local,world,input,motion,0);
                        var sample=WeaponMotion.Attach(input,motion,world,0);
                        Assert.That(math.distance(sample.Muzzle,WeaponMotion.WorldOffset(input,input.Weapon.AimDirection,input.Weapon.MuzzleOffset)),Is.LessThan(.003f),"id="+id+" travel="+travel+" aim="+aim+" frame="+frame);
                        var planned=WeaponMotion.Sample(input,motion);
                        Assert.That(math.distance(sample.SupportGrip,planned.Support+sample.PrimaryGrip-planned.Grip),Is.LessThan(.003f),"moving secondary grip");
                    }
                }
            }
        }
        [Test] public void NearMaximumReachKeepsElbowBendAndFiniteLengths()
        {
            using(var rig=NaturalCharacterRig.Create())
            using(var pose=new NativeArray<BoneLocal>(NaturalCharacterRig.Bones,Allocator.Temp))
            {
                var writable=pose;for(int i=0;i<pose.Length;i++)writable[i]=new BoneLocal{Position=rig.View.Bones[i].Position,Rotation=rig.View.Bones[i].Rotation};
                for(int d=0;d<64;d++)
                {
                    float a=d*math.PI/32;float2 shoulder=NaturalMotion.BonePoint(rig.View,pose,NaturalCharacterRig.NearArm,0);
                    NaturalMotion.BlendAim(rig.View,pose,NaturalCharacterRig.NearArm,NaturalCharacterRig.NearForearm,shoulder+new float2(math.cos(a),math.sin(a))*100,0,1,1,-1,1);
                    float2 hand=NaturalMotion.BonePoint(rig.View,pose,NaturalCharacterRig.Hand,0);
                    Assert.That(math.distance(hand,shoulder),Is.EqualTo(.836f).Within(.0001f));
                    Assert.That(pose[NaturalCharacterRig.NearForearm].Rotation,Is.GreaterThan(.3f),"elbow side must not invert at extension");
                }
            }
        }
        [Test] public void BowDrawReleasesAtAuthoritativeMarkerAndHasContinuousRecoil()
        {
            var input=Input(1004);var motion=default(GameplayCharacterMotion);motion.Step(input,.016f);
            input.Weapon.Stage=WeaponStage.Windup;input.Weapon.Phase=input.Weapon.ReleasePhase*.5f;
            float middle=WeaponMotion.Sample(input,motion).Draw;
            input.Weapon.Phase=input.Weapon.ReleasePhase;float release=WeaponMotion.Sample(input,motion).Draw;
            input.Weapon.Phase+=.00001f;float after=WeaponMotion.Sample(input,motion).Draw;
            input.Weapon.Phase+=.1f;float recovered=WeaponMotion.Sample(input,motion).Draw;
            Assert.That(middle,Is.InRange(.45f,.55f));Assert.That(release,Is.EqualTo(1).Within(1e-6f));
            Assert.That(after,Is.EqualTo(release).Within(.0001f));Assert.That(recovered,Is.LessThan(.001f));
        }
        [Test] public void UnaimedLocomotionMovesBothArmsChestAndHeadWithoutFixedGuard()
        {
            using(var rig=NaturalCharacterRig.Create())
            using(var local=new NativeArray<BoneLocal>(NaturalCharacterRig.Bones,Allocator.Temp))
            using(var world=new NativeArray<BoneWorld>(NaturalCharacterRig.Bones,Allocator.Temp))
            {
                var input=Input();input.Weapon=default;input.State=GameplayCharacterState.Run;input.Velocity=new float2(2,0);
                var motion=default(GameplayCharacterMotion);float minArm=100,maxArm=-100,minFore=100,maxFore=-100,minChest=100,maxChest=-100;
                for(int i=0;i<240;i++)
                {
                    input.Root=input.Ground+=input.Velocity/120;motion.Step(input,1f/120);GameplayCharacterMotion.Pose(rig.View,local,world,input,motion,0);
                    minArm=math.min(minArm,local[NaturalCharacterRig.NearArm].Rotation);maxArm=math.max(maxArm,local[NaturalCharacterRig.NearArm].Rotation);
                    minFore=math.min(minFore,local[NaturalCharacterRig.FarForearm].Rotation);maxFore=math.max(maxFore,local[NaturalCharacterRig.FarForearm].Rotation);
                    minChest=math.min(minChest,local[NaturalCharacterRig.Torso].Rotation);maxChest=math.max(maxChest,local[NaturalCharacterRig.Torso].Rotation);
                }
                Assert.That(maxArm-minArm,Is.GreaterThan(.35f));Assert.That(maxFore-minFore,Is.GreaterThan(.18f));Assert.That(maxChest-minChest,Is.GreaterThan(.1f));
            }
        }
        [Test] public void AimWrapTurnsAcrossPiAndEquipChangesKeepGripContinuous()
        {
            var input=Input();var motion=default(GameplayCharacterMotion);
            input.Weapon.AimDirection=new float2(math.cos(3.12f),math.sin(3.12f));motion.Step(input,.016f);
            input.Weapon.AimDirection=new float2(math.cos(-3.12f),math.sin(-3.12f));motion.Step(input,.016f);
            Assert.That(motion.WeaponAim.x,Is.LessThan(-.99f),"angle wrap must not pass through forward");
            var last=WeaponMotion.Sample(input,motion);
            input=Input(1004);motion.Step(input,1f/120);var changed=WeaponMotion.Sample(input,motion);
            Assert.That(math.distance(last.Grip,changed.Grip),Is.LessThan(.00001f));
            for(int i=0;i<30;i++)
            {motion.Step(input,1f/120);var next=WeaponMotion.Sample(input,motion);Assert.That(math.distance(changed.Grip,next.Grip),Is.LessThan(.08f));changed=next;}
            input.Teleported=true;input.Root=input.Ground=new float2(40,20);motion.Step(input,1f/120);
            Assert.That(math.distance(WeaponMotion.Sample(input,motion).Grip,input.Root),Is.LessThan(3),"teleport must reset old weapon interpolation");
        }
        [Test] public void InterruptedWindupPreservesGripAndVelocityBeforeReturningToHold()
        {
            for(int equip=0;equip<2;equip++)
            {
                var input=Input();var motion=default(GameplayCharacterMotion);motion.Step(input,1f/120);
                input.Weapon.Stage=WeaponStage.Windup;
                for(int i=0;i<20;i++){input.Weapon.Phase=input.Weapon.ContactPhase*.55f*i/19;motion.Step(input,1f/120);}
                var before=WeaponMotion.Sample(input,motion);var velocity=motion.HeldGripVelocity;
                input.Weapon.Stage=equip==0?WeaponStage.Idle:WeaponStage.Equipping;input.Weapon.Phase=0;input.Weapon.StagePhase=0;
                motion.Step(input,1f/120);var interrupted=WeaponMotion.Sample(input,motion);
                Assert.That(math.distance(before.Grip,interrupted.Grip),Is.LessThan(.00001f));
                Assert.That(math.abs(WeaponMotion.AngleDelta(before.Angle,interrupted.Angle)),Is.LessThan(.00001f));
                Assert.That(math.distance(velocity,motion.ChangeGripVelocity),Is.LessThan(.00001f));
                Assert.That(math.abs(before.Body-interrupted.Body),Is.LessThan(.00001f),"weapon torso anticipation must not snap off at interruption");
                for(int i=0;i<30;i++)
                {motion.Step(input,1f/120);var next=WeaponMotion.Sample(input,motion);Assert.That(math.distance(interrupted.Grip,next.Grip),Is.LessThan(.08f));interrupted=next;}
            }
        }
        [TestCase(30)] [TestCase(60)] [TestCase(120)]
        public void VelocityStepsTurnsAndExtremeAimKeepEveryJointFinite(int hz)
        {
            using(var p=new GameplayCharacterPresenter(RenderTier.DataTexture,1,includeWeapons:true))
            {
                var input=Input(1004);float2 last=0;int samples=0;
                for(int i=0;i<hz*4;i++)
                {
                    float t=i/(float)hz;input.Facing=t<2?1:-1;
                    input.Velocity=t<1?new float2(0,3):t<2?new float2(-3,0):new float2(0,-3);
                    input.State=GameplayCharacterState.Run;input.Root=input.Ground+=input.Velocity/hz;
                    float a=t*5;input.Weapon.AimDirection=new float2(math.cos(a),math.sin(a));
                    p.Begin(1f/hz,3);p.Submit(input);p.Evaluate();Assert.IsTrue(p.TryReadWeapon(input.Handle,out var sample));
                    for(int k=0;k<NaturalCharacterRig.Bones;k++){var b=p.ReadBone(input.Handle,k);Assert.IsTrue(math.all(math.isfinite(b.Position)));Assert.IsTrue(math.isfinite(b.Rotation));}
                    if(i>0)Assert.That(math.distance(last,sample.PrimaryGrip),Is.LessThan(.65f),"bounded grip displacement during reversal/extreme aim");
                    last=sample.PrimaryGrip;samples++;
                }
                Assert.AreEqual(hz*4,samples);
            }
        }
        [Test] public void ContinuousSocketsAndJointsAreIndependentOfPresentationQuality()
        {
            using(var high=new GameplayCharacterPresenter(RenderTier.DataTexture,1,includeWeapons:true))
            using(var low=new GameplayCharacterPresenter(RenderTier.DataTexture,1,includeWeapons:true))
            {
                var input=Input(1003);input.Kind=1;
                for(int i=0;i<240;i++)
                {
                    input.Weapon.Stage=WeaponStage.Windup;input.Weapon.Phase=math.frac(i/120f);input.Root=input.Ground=new float2(i*.003f,0);input.Velocity=new float2(.36f,0);input.State=GameplayCharacterState.Run;
                    high.Begin(1f/120,0);low.Begin(1f/120,3);high.Submit(input);low.Submit(input);high.Evaluate();low.Evaluate();
                    high.TryReadWeapon(input.Handle,out var a);low.TryReadWeapon(input.Handle,out var b);
                    Assert.That(math.distance(a.Tip,b.Tip),Is.LessThan(1e-6f));
                    for(int k=0;k<NaturalCharacterRig.Bones;k++)Assert.That(math.distance(high.ReadBone(input.Handle,k).Position,low.ReadBone(input.Handle,k).Position),Is.LessThan(1e-6f));
                }
            }
        }
        [Test] public void WarmedWeaponPoseAndFkAllocateZeroWithCalibratedProbe()
        {
            using(var rig=NaturalCharacterRig.Create())
            using(var local=new NativeArray<BoneLocal>(NaturalCharacterRig.Bones,Allocator.Temp))
            using(var world=new NativeArray<BoneWorld>(NaturalCharacterRig.Bones,Allocator.Temp))
            {
                var input=Input(1004);var motion=default(GameplayCharacterMotion);
                Action work=()=>{for(int i=0;i<120;i++){input.Weapon.Stage=WeaponStage.Windup;input.Weapon.Phase=i/120f;motion.Step(input,1f/120);GameplayCharacterMotion.Pose(rig.View,local,world,input,motion,0);WeaponMotion.Attach(input,motion,world,0);}};
                work();using(var probe=new ManagedAllocationProbe()){probe.Calibrate();var result=probe.Measure(work);probe.Calibrate();Assert.AreEqual(0,result.Value);}
            }
        }
    }
}
