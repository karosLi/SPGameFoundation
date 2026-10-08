using NUnit.Framework;
using SPF.Contracts;
using SPF.Contracts.Weapons;
using SPF.L1.Skeleton;
using SPF.L2.Weapons;
using SPF.Presentation;
using SPF.Presentation.Animation;
using Unity.Collections;
using Unity.Mathematics;

namespace SPF.Tests.EditMode
{
    public class BladeTurnTrajectoryTests
    {
        static float FrameStep(int schedule,int frame)
        {
            if(schedule>0)return 1f/schedule;
            switch(frame%5){case 0:return 1f/120;case 1:return 1f/30;case 2:return 1f/90;case 3:return 1f/60;default:return 1f/48;}
        }

        // Same public input ownership as the native closeup: idle samples 0..3,
        // held right until sample 38, then held left through the NEXT action pulse.
        // The running action owns facing/aim until WeaponRuntime admits the next one.
        [TestCase(30)] [TestCase(60)] [TestCase(120)] [TestCase(0)]
        public void HeldBladeReversalNeverDetoursBelowContact(int schedule)
        {
            using(var high=new GameplayCharacterPresenter(RenderTier.DataTexture,1,includeWeapons:true))
            using(var low=new GameplayCharacterPresenter(RenderTier.DataTexture,1,includeWeapons:true))
            for(int role=0;role<3;role++)for(int face=-1;face<=1;face+=2)for(int moving=0;moving<2;moving++) {
                high.Clear();low.Clear();
                using(var runtime=new WeaponRuntime(WeaponProfiles.CreateDefaults(60),60,4,4,8)) {
                    var input=new GameplayCharacterInput{Handle=new EntityHandle(7,1),Scale=.9f,Facing=face,Tint=new float4(1),MotionProfileId=role,Kind=role%2};
                    runtime.Owner=input.Handle;float elapsed=0,simulated=0,minTip=10,minHand=10,maxWristSpeed=0,maxElbowSpeed=0,maxTurnSpeed=0;
                    float2 previousHand=0,previousElbow=0;float previousRotation=0;int frame=0,reversed=0,cachedFrames=0;uint reversalPulse=0;
                    while(elapsed<3.3f) {
                        float dt=FrameStep(schedule,frame++);elapsed+=dt;
                        while(simulated+1f/60<=elapsed) {
                            int sample=(int)((simulated+1f/60)/.037f);bool held=sample>=4;int side=sample<38?face:-face;
                            runtime.Step(true,true,false,false,held,new float2(side,0),input.Ground,0,input.Scale);simulated+=1f/60;
                        }
                        input.Weapon=runtime.View(math.saturate((elapsed-simulated)*60));
                        input.Facing=input.Weapon.AimDirection.x<0?-1:1;
                        int moveSide=elapsed<38*.037f?face:-face;
                        input.Velocity=moving!=0&&elapsed>=4*.037f?new float2(moveSide*.36f,0):float2.zero;
                        input.Root=input.Ground+=input.Velocity*dt;
                        input.State=WeaponMotion.Acting(input.Weapon)?GameplayCharacterState.Attack:GameplayCharacterState.Idle;input.Phase=input.Weapon.Phase;
                        high.Begin(dt,0);low.Begin(dt,3);Assert.IsTrue(high.Submit(input));Assert.IsTrue(low.Submit(input));high.Evaluate();low.Evaluate();
                        if(low.BasePoseRefreshes==0)cachedFrames++;
                        Assert.IsTrue(high.TryReadWeapon(input.Handle,out var at));Assert.IsTrue(low.TryReadWeapon(input.Handle,out var cached));
                        var hand=high.ReadBone(input.Handle,NaturalCharacterRig.Hand);var elbow=high.ReadBone(input.Handle,NaturalCharacterRig.NearForearm);
                        Assert.Less(math.distance(hand.Position,at.PrimaryGrip),.000001f,"final palm owns grip");
                        Assert.Less(math.abs(WeaponMotion.AngleDelta(hand.Rotation,at.Rotation)),.000001f,"rigid blade/palm angle");
                        Assert.Less(math.distance(at.Tip,cached.Tip),.000001f,"quality caching cannot change current turn");
                        Assert.That(math.distance(at.PrimaryGrip,at.Tip)/input.Scale,Is.EqualTo(1.07f).Within(.00001f));
                        if(elapsed>38*.037f&&input.Facing==-face) {
                            if(reversalPulse==0)reversalPulse=input.Weapon.ActionPulse;
                            if(input.Weapon.ActionPulse==reversalPulse) {
                                reversed++;minTip=math.min(minTip,(at.Tip.y-input.Root.y)/input.Scale);minHand=math.min(minHand,(at.PrimaryGrip.y-input.Root.y)/input.Scale);
                                maxWristSpeed=math.max(maxWristSpeed,math.distance((hand.Position-input.Root)/input.Scale,previousHand)/dt);
                                maxElbowSpeed=math.max(maxElbowSpeed,math.distance((elbow.Position-input.Root)/input.Scale,previousElbow)/dt);
                                maxTurnSpeed=math.max(maxTurnSpeed,math.abs(WeaponMotion.AngleDelta(previousRotation,at.Rotation))/dt);
                            }
                        }
                        previousHand=(hand.Position-input.Root)/input.Scale;previousElbow=(elbow.Position-input.Root)/input.Scale;previousRotation=at.Rotation;
                    }
                    string context="schedule="+schedule+" role="+role+" face="+face+" move="+moving+" minTip="+minTip+" minHand="+minHand+" wristSpeed="+maxWristSpeed+" elbowSpeed="+maxElbowSpeed+" angularSpeed="+maxTurnSpeed;
                    TestContext.WriteLine(context);Assert.Greater(reversed,0,"must sample entire new reverse-facing pulse");
                    if(role==1||schedule==120||schedule==0)Assert.Greater(cachedFrames,0,"exercise quality-limited cached correction");
                    Assert.GreaterOrEqual(minTip,1.25f-.003f,"blade does not point down during horizontal held reversal "+context);
                    Assert.GreaterOrEqual(minHand,1.25f-.003f,"hand does not dip under canonical contact "+context);
                    Assert.Less(maxWristSpeed,15f,"no wrist pop "+context);
                    Assert.Less(maxElbowSpeed,20f,"no elbow branch pop "+context);
                    Assert.Less(maxTurnSpeed,35f,"no blade angular pop "+context);
                }
            }
        }

        [TestCase(30)] [TestCase(60)] [TestCase(120)] [TestCase(0)]
        public void BladeTurnCancellationAndReentryStayContinuousForWholeTail(int schedule)
        {
            using(var presenter=new GameplayCharacterPresenter(RenderTier.DataTexture,1,includeWeapons:true))
            for(int face=-1;face<=1;face+=2)foreach(int cancelTick in new[]{1,2,4,7}) {
                presenter.Clear();using(var runtime=new WeaponRuntime(WeaponProfiles.CreateDefaults(60),60,4,4,8)) {
                    var input=new GameplayCharacterInput{Handle=new EntityHandle(7,1),Scale=.9f,Facing=face,Tint=new float4(1)};
                    runtime.Owner=input.Handle;runtime.Step(true,true,false,false,false,new float2(face,0),0,0,input.Scale);
                    float elapsed=0,simulated=0,maxHand=0,maxElbow=0,maxAngle=0;int frame=0,reverseTick=0,cancelledTick=0,tail=0;
                    float2 oldHand=0,oldElbow=0;float oldAngle=0;
                    while(elapsed<3.6f) {
                        float dt=FrameStep(schedule,frame++);elapsed+=dt;
                        while(simulated+1f/60<=elapsed) {
                            int tick=(int)runtime.Tick+1;int side=simulated<1.4f?face:-face;
                            bool cancel=reverseTick>0&&tick==reverseTick+cancelTick;
                            if(cancel)cancelledTick=tick;
                            bool held=simulated>.15f&&(cancelledTick==0||tick>cancelledTick+12);
                            runtime.Step(true,true,cancel,false,held,new float2(side,0),0,0,input.Scale);simulated+=1f/60;
                            if(reverseTick==0&&runtime.Equipment.Aim.x==-face&&runtime.Equipment.Timeline.Running)reverseTick=tick;
                        }
                        input.Weapon=runtime.View(math.saturate((elapsed-simulated)*60));input.Facing=input.Weapon.AimDirection.x<0?-1:1;
                        input.State=WeaponMotion.Acting(input.Weapon)?GameplayCharacterState.Attack:GameplayCharacterState.Idle;input.Phase=input.Weapon.Phase;
                        presenter.Begin(dt,3);Assert.IsTrue(presenter.Submit(input));presenter.Evaluate();Assert.IsTrue(presenter.TryReadWeapon(input.Handle,out var at));
                        var hand=presenter.ReadBone(input.Handle,NaturalCharacterRig.Hand);var elbow=presenter.ReadBone(input.Handle,NaturalCharacterRig.NearForearm);
                        if(elapsed>1.4f) {
                            tail++;maxHand=math.max(maxHand,math.distance(at.PrimaryGrip,oldHand)/input.Scale/dt);
                            maxElbow=math.max(maxElbow,math.distance(elbow.Position,oldElbow)/input.Scale/dt);
                            maxAngle=math.max(maxAngle,math.abs(WeaponMotion.AngleDelta(oldAngle,at.Rotation))/dt);
                            Assert.GreaterOrEqual(at.Tip.y/input.Scale,1.25f-.003f,"no low detour in cancelled turn/reentry tail");
                            Assert.Less(math.distance(at.PrimaryGrip,hand.Position),.000001f);
                            Assert.Less(math.abs(WeaponMotion.AngleDelta(at.Rotation,hand.Rotation)),.000001f);
                        }
                        oldHand=at.PrimaryGrip;oldElbow=elbow.Position;oldAngle=at.Rotation;
                    }
                    string context="schedule="+schedule+" face="+face+" cancelTick="+cancelTick+" hand="+maxHand+" elbow="+maxElbow+" angle="+maxAngle;
                    TestContext.WriteLine(context);Assert.Greater(cancelledTick,0);Assert.Greater(tail,0);
                    Assert.Less(maxHand,15f,context);Assert.Less(maxElbow,20f,context);Assert.Less(maxAngle,35f,context);
                }
            }
        }

        [TestCase(30)] [TestCase(60)] [TestCase(120)]
        public void EquippingFromUnarmedDoesNotIntroduceAnUnweightedFold(int hz)
        {
            using(var rig=NaturalCharacterRig.Create())
            using(var local=new NativeArray<BoneLocal>(NaturalCharacterRig.Bones,Allocator.Temp))
            using(var world=new NativeArray<BoneWorld>(NaturalCharacterRig.Bones,Allocator.Temp))
            for(int face=-1;face<=1;face+=2)for(int turn=0;turn<2;turn++) {
                var input=new GameplayCharacterInput{Handle=new EntityHandle(7,1),Scale=.9f,Facing=face};
                var motion=default(GameplayCharacterMotion);
                for(int frame=0;frame<hz;frame++)motion.Step(input,1f/hz);
                using(var runtime=new WeaponRuntime(WeaponProfiles.CreateDefaults(60),60,4,4,8)) {
                    input.Facing=turn==0?face:-face;runtime.Step(true,true,false,false,false,new float2(input.Facing,0),0,0,input.Scale);
                    input.Weapon=runtime.View(1);motion.Step(input,0);
                    Assert.That(motion.ChangeArmBendVelocity,Is.EqualTo(0),"an absent arm has no turn velocity to inherit");
                    // Hold the same armed body/target corrections in both samples; only
                    // turn ownership differs. This isolates the new arm fold from the
                    // pre-existing equipped torso entry correction.
                    var neutral=motion;neutral.ChangeArmBend=1;neutral.ChangeArmBendVelocity=0;
                    GameplayCharacterMotion.Pose(rig.View,local,world,input,neutral,0);float2 before=world[NaturalCharacterRig.Hand].Position;
                    GameplayCharacterMotion.Pose(rig.View,local,world,input,motion,0);
                    Assert.That(motion.WeaponWeight,Is.EqualTo(0));
                    Assert.Less(math.distance(before,world[NaturalCharacterRig.Hand].Position),.000001f,"zero weapon weight cannot add the turn fold");
                    for(int frame=0;frame<hz;frame++) {
                        motion.Step(input,1f/hz);GameplayCharacterMotion.Pose(rig.View,local,world,input,motion,0);
                        if(turn==0)Assert.That(motion.HeldArmBend,Is.EqualTo(1).Within(.000001f),"equipping alone does not invent a turn");
                        var at=WeaponMotion.Attach(input,motion,world,0);
                        Assert.IsTrue(math.all(math.isfinite(at.Tip)));Assert.Less(math.distance(at.PrimaryGrip,world[NaturalCharacterRig.Hand].Position),.000001f);
                        Assert.LessOrEqual(math.abs(local[NaturalCharacterRig.Hand].Rotation),.850001f);
                    }
                    input.Facing=-input.Facing;input.Weapon.AimDirection=new float2(input.Facing,0);motion.Step(input,1f/hz);
                    Assert.Greater(math.abs(motion.HeldArmBendVelocity),0,"exercise an in-progress turn before paused removal");
                    var equipped=input.Weapon;input.Weapon=default;motion.Step(input,0);
                    Assert.That(motion.HeldArmBend,Is.EqualTo(1));Assert.That(motion.HeldArmBendVelocity,Is.EqualTo(0));
                    input.Weapon=equipped;motion.Step(input,0);
                    Assert.That(motion.ChangeArmBendVelocity,Is.EqualTo(0),"zero-dt reentry cannot revive a removed turn");
                }
            }
        }
    }
}
