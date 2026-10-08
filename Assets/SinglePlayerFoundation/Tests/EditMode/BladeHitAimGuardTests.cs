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
    public class BladeHitAimGuardTests
    {
        // GPU f113..126, native 3c45520 grounded-belt-live-gpu/gait.csv.
        // Acquisition time, root X/Y, velocity X/Y, body facing, source state.
        // This replays the recorded public input window, not the unrecorded intermediate
        // render steps, and does not claim pixel-exact reconstruction of native FK.
        static readonly float[,] ObservedHitWindow={
            {5.126034584f,2.772915f,1.13161421f,-.66377157f,-.3904402f,-1,3},
            {5.175962084f,2.794234f,1.11887372f,2.22470284f,0,-1,3},
            {5.225950125f,2.89322329f,1.11887372f,1.567726f,0,-1,3},
            {5.275942667f,2.96298027f,1.11887372f,1.1047554f,0,1,3},
            {5.326004750f,2.90056443f,1.11887372f,-1.97972775f,0,1,3},
            {5.359340959f,2.838521f,1.11887372f,-1.567726f,0,1,3},
            {5.409259459f,2.76877666f,1.118866f,-1.073513f,-.0119984141f,1,3},
            {5.442737542f,2.74037838f,1.11640835f,-.5330086f,-.137812614f,1,3},
            {5.492734084f,2.716851f,1.11011243f,-.397725075f,-.08361911f,1,3},
            {5.542638042f,2.698752f,1.10648286f,-.292196244f,-.05699443f,1,3},
            {5.576017875f,2.68955374f,1.10468924f,-.2333021f,-.04542875f,1,3},
            {5.625933375f,2.66625667f,1.09638667f,-.8951425f,-.407324523f,-1,1},
            {5.675908625f,2.621891f,1.07653451f,-.8462619f,-.397572368f,-1,1},
            {5.725941125f,2.57969952f,1.05711687f,-.81027025f,-.390935868f,-1,1}};

        static float FrameStep(int schedule,int frame)
        {
            if(schedule>0)return 1f/schedule;
            switch(frame%5){case 0:return 1f/120;case 1:return 1f/30;case 2:return 1f/90;case 3:return 1f/60;default:return 1f/48;}
        }

        [Test]
        public void RecordedGroundedHitInputsDoNotLowerTheBlade()
        {
            using(var high=new GameplayCharacterPresenter(RenderTier.DataTexture,1,includeWeapons:true))
            using(var low=new GameplayCharacterPresenter(RenderTier.DataTexture,1,includeWeapons:true))
            using(var runtime=new WeaponRuntime(WeaponProfiles.CreateDefaults(60),60,4,4,8)) {
                var input=new GameplayCharacterInput {Handle=new EntityHandle(0,12),Scale=.9f,Facing=-1,Kind=1,Tint=new float4(1)};
                runtime.Owner=input.Handle;runtime.Step(true,true,false,false,false,new float2(-1,0),0,0,input.Scale);
                input.Weapon=runtime.View(1);input.Root=input.Ground=new float2(ObservedHitWindow[0,1],ObservedHitWindow[0,2]);
                for(int frame=0;frame<60;frame++) {
                    high.Begin(1f/60,0);low.Begin(1f/60,3);high.Submit(input);low.Submit(input);high.Evaluate();low.Evaluate();
                }
                float minTip=10,minDirection=1;int cachedFrames=0;
                for(int row=0;row<ObservedHitWindow.GetLength(0);row++) {
                    float dt=row==0?0:ObservedHitWindow[row,0]-ObservedHitWindow[row-1,0];
                    input.Root=input.Ground=new float2(ObservedHitWindow[row,1],ObservedHitWindow[row,2]);
                    input.Velocity=new float2(ObservedHitWindow[row,3],ObservedHitWindow[row,4]);input.Facing=ObservedHitWindow[row,5];
                    input.State=ObservedHitWindow[row,6]==3?GameplayCharacterState.Hit:GameplayCharacterState.Run;
                    high.Begin(dt,0);low.Begin(dt,3);high.Submit(input);low.Submit(input);high.Evaluate();low.Evaluate();
                    if(low.BasePoseRefreshes==0)cachedFrames++;
                    Assert.IsTrue(high.TryReadWeapon(input.Handle,out var at));Assert.IsTrue(low.TryReadWeapon(input.Handle,out var cached));
                    minTip=math.min(minTip,(at.Tip.y-input.Root.y)/input.Scale);minDirection=math.min(minDirection,at.Direction.y);
                    Assert.Less(math.distance(at.Tip,cached.Tip),.000001f,"cached base pose must consume current Hit/body ownership");
                    Assert.Less(math.distance(at.PrimaryGrip,high.ReadBone(input.Handle,NaturalCharacterRig.Hand).Position),.000001f);
                    Assert.Less(math.abs(WeaponMotion.AngleDelta(at.Rotation,high.ReadBone(input.Handle,NaturalCharacterRig.Hand).Rotation)),.000001f);
                }
                TestContext.WriteLine("recorded public input window minDirectionY="+minDirection+" minTip="+minTip);
                Assert.Greater(cachedFrames,0);Assert.GreaterOrEqual(minDirection,-.003f);Assert.GreaterOrEqual(minTip,1.25f-.003f);
            }
        }

        [TestCase(30)] [TestCase(60)] [TestCase(120)] [TestCase(0)]
        public void HitDuringWeaponTurnKeepsTheWholeCancellationAndReentryTailRaised(int schedule)
        {
            using(var rig=NaturalCharacterRig.Create())
            using(var local=new NativeArray<BoneLocal>(NaturalCharacterRig.Bones,Allocator.Temp))
            using(var world=new NativeArray<BoneWorld>(NaturalCharacterRig.Bones,Allocator.Temp))
            for(int face=-1;face<=1;face+=2)foreach(int cancelTick in new[]{1,2,4,7}) {
                using(var runtime=new WeaponRuntime(WeaponProfiles.CreateDefaults(60),60,4,4,8)) {
                    var input=new GameplayCharacterInput{Handle=new EntityHandle(7,1),Scale=.9f,Facing=face};
                    runtime.Owner=input.Handle;runtime.Step(true,true,false,false,false,new float2(face,0),0,0,input.Scale);
                    var motion=default(GameplayCharacterMotion);float elapsed=0,simulated=0,maxHand=0,maxElbow=0,maxAngle=0,minTip=10,maxTime=0;
                    int frame=0,reverseTick=0,cancelledTick=0,opposedFrames=0;float2 oldHand=0,oldElbow=0;float oldAngle=0;
                    while(elapsed<3.6f) {
                        float dt=FrameStep(schedule,frame++);elapsed+=dt;
                        while(simulated+1f/60<=elapsed) {
                            int tick=(int)runtime.Tick+1,side=simulated<1.4f?face:-face;
                            bool cancel=reverseTick>0&&tick==reverseTick+cancelTick;
                            if(cancel)cancelledTick=tick;
                            bool held=simulated>.15f&&(cancelledTick==0||tick>cancelledTick+24);
                            runtime.Step(true,true,cancel,false,held,new float2(side,0),0,0,input.Scale);simulated+=1f/60;
                            if(reverseTick==0&&runtime.Equipment.Aim.x==-face&&runtime.Equipment.Timeline.Running)reverseTick=tick;
                        }
                        input.Weapon=runtime.View(math.saturate((elapsed-simulated)*60));
                        bool hit=cancelledTick>0&&runtime.Tick<cancelledTick+24;
                        input.Facing=hit?face:(input.Weapon.AimDirection.x<0?-1:1);
                        input.State=hit?GameplayCharacterState.Hit:WeaponMotion.Acting(input.Weapon)?GameplayCharacterState.Attack:GameplayCharacterState.Idle;
                        input.Phase=input.Weapon.Phase;motion.Step(input,dt);GameplayCharacterMotion.Pose(rig.View,local,world,input,motion,0);
                        var at=WeaponMotion.Attach(input,motion,world,0);var elbow=world[NaturalCharacterRig.NearForearm];
                        if(hit&&input.Facing*input.Weapon.AimDirection.x<0)opposedFrames++;
                        if(elapsed>1.4f) {
                            minTip=math.min(minTip,at.Tip.y/input.Scale);
                            float handSpeed=math.distance(at.PrimaryGrip,oldHand)/input.Scale/dt;
                            if(handSpeed>maxHand){maxHand=handSpeed;maxTime=elapsed;}
                            maxElbow=math.max(maxElbow,math.distance(elbow.Position,oldElbow)/input.Scale/dt);
                            maxAngle=math.max(maxAngle,math.abs(WeaponMotion.AngleDelta(oldAngle,at.Rotation))/dt);
                            Assert.Less(math.distance(at.PrimaryGrip,world[NaturalCharacterRig.Hand].Position),.000001f);
                            Assert.Less(math.abs(WeaponMotion.AngleDelta(at.Rotation,world[NaturalCharacterRig.Hand].Rotation)),.000001f);
                            Assert.LessOrEqual(math.abs(local[NaturalCharacterRig.Hand].Rotation),.850001f);
                        }
                        oldHand=at.PrimaryGrip;oldElbow=elbow.Position;oldAngle=at.Rotation;
                    }
                    string context="schedule="+schedule+" face="+face+" cancelTick="+cancelTick+" minTip="+minTip+" handSpeed="+maxHand+" elbowSpeed="+maxElbow+" angularSpeed="+maxAngle+" maxTime="+maxTime;
                    TestContext.WriteLine(context);Assert.Greater(opposedFrames,0);Assert.GreaterOrEqual(minTip,1.25f-.003f,context);
                    Assert.Less(maxHand,15f,context);Assert.Less(maxElbow,20f,context);Assert.Less(maxAngle,35f,context);
                }
            }
        }

        // Native 3c45520 grounded-belt GPU f116..123 / fallback f115..121:
        // Hit turns the body right while the equipped blade's runtime aim stays left.
        // BwRenderer makes its weapon view idle during Hit, without replacing that aim.
        // Reproduce that public input ownership, then also cover its mirrored direction.
        [TestCase(30)] [TestCase(60)] [TestCase(120)] [TestCase(0)]
        public void HitBodyReversalRetainsTheLockedBladeGuard(int schedule)
        {
            using(var rig=NaturalCharacterRig.Create())
            using(var local=new NativeArray<BoneLocal>(NaturalCharacterRig.Bones,Allocator.Temp))
            using(var world=new NativeArray<BoneWorld>(NaturalCharacterRig.Bones,Allocator.Temp))
            for(int face=-1;face<=1;face+=2)for(int role=0;role<3;role++) {
                using(var runtime=new WeaponRuntime(WeaponProfiles.CreateDefaults(60),60,4,4,8)) {
                    var input=new GameplayCharacterInput {Handle=new EntityHandle(7,1),Scale=.9f,Facing=face,MotionProfileId=role};
                    runtime.Owner=input.Handle;
                    runtime.Step(true,true,false,false,false,new float2(face,0),0,0,input.Scale);
                    input.Weapon=runtime.View(1);
                    var motion=default(GameplayCharacterMotion);
                    for(int frame=0;frame<120;frame++)motion.Step(input,1f/60);
                    float elapsed=0,minDirection=1,minTip=10,maxHand=0,maxElbow=0,maxAngle=0;
                    GameplayCharacterMotion.Pose(rig.View,local,world,input,motion,0);
                    var previous=WeaponMotion.Attach(input,motion,world,0);
                    float2 previousElbow=world[NaturalCharacterRig.NearForearm].Position;
                    for(int frame=0;elapsed<1.1f;frame++) {
                        float dt=FrameStep(schedule,frame);elapsed+=dt;
                        input.State=elapsed<.65f?GameplayCharacterState.Hit:GameplayCharacterState.Idle;
                        input.Facing=elapsed<.65f?-face:face;
                        // The simulation's locked aim and all contact offsets remain unchanged.
                        motion.Step(input,dt);GameplayCharacterMotion.Pose(rig.View,local,world,input,motion,0);
                        var at=WeaponMotion.Attach(input,motion,world,0);
                        minDirection=math.min(minDirection,at.Direction.y);minTip=math.min(minTip,(at.Tip.y-input.Root.y)/input.Scale);
                        maxHand=math.max(maxHand,math.distance(at.PrimaryGrip,previous.PrimaryGrip)/input.Scale/dt);
                        maxElbow=math.max(maxElbow,math.distance(world[NaturalCharacterRig.NearForearm].Position,previousElbow)/input.Scale/dt);
                        maxAngle=math.max(maxAngle,math.abs(WeaponMotion.AngleDelta(previous.Rotation,at.Rotation))/dt);
                        Assert.Less(math.distance(at.PrimaryGrip,world[NaturalCharacterRig.Hand].Position),.000001f);
                        Assert.Less(math.abs(WeaponMotion.AngleDelta(at.Rotation,world[NaturalCharacterRig.Hand].Rotation)),.000001f);
                        Assert.LessOrEqual(math.abs(local[NaturalCharacterRig.Hand].Rotation),.850001f);
                        Assert.That(math.distance(world[NaturalCharacterRig.NearArm].Position,world[NaturalCharacterRig.NearForearm].Position)/input.Scale,Is.EqualTo(rig.View.Bones[NaturalCharacterRig.NearArm].Length).Within(.00001f));
                        Assert.That(math.distance(world[NaturalCharacterRig.NearForearm].Position,world[NaturalCharacterRig.Hand].Position)/input.Scale,Is.EqualTo(rig.View.Bones[NaturalCharacterRig.NearForearm].Length).Within(.00001f));
                        previous=at;previousElbow=world[NaturalCharacterRig.NearForearm].Position;
                    }
                    string context="schedule="+schedule+" face="+face+" role="+role+" minDirectionY="+minDirection+" minTip="+minTip+" handSpeed="+maxHand+" elbowSpeed="+maxElbow+" angularSpeed="+maxAngle;
                    TestContext.WriteLine(context);
                    Assert.GreaterOrEqual(minDirection,-.003f,"held horizontal guard must not point down during body-only Hit reversal: "+context);
                    Assert.GreaterOrEqual(minTip,1.25f-.003f,context);
                    Assert.Less(maxHand,15f,context);Assert.Less(maxElbow,20f,context);Assert.Less(maxAngle,35f,context);
                }
            }
        }
    }
}
