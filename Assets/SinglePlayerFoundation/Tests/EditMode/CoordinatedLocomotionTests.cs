using NUnit.Framework;
using SPF.Contracts;
using SPF.Contracts.Weapons;
using SPF.L1.Skeleton;
using SPF.Presentation.Animation;
using Unity.Collections;
using Unity.Mathematics;

namespace SPF.Tests.EditMode
{
    public class CoordinatedLocomotionTests
    {
        static GameplayCharacterInput Input(int role=0)=>new GameplayCharacterInput {Handle=new EntityHandle(0,12),Scale=.9f,Facing=1,Tint=new float4(1),MotionProfileId=role,
            Weapon=new WeaponViewState{ContentId=1001,VisualId=1001,Family=WeaponActionFamily.Slash,AimDirection=new float2(1,0),GripOffset=new float2(.58f,1.25f),MuzzleOffset=new float2(1.65f,1.25f)}};

        [TestCase(30,0f)] [TestCase(60,0f)] [TestCase(120,0f)]
        [TestCase(30,.3168f)] [TestCase(60,.3168f)] [TestCase(120,.3168f)]
        [TestCase(30,-.3168f)] [TestCase(60,-.3168f)] [TestCase(120,-.3168f)]
        public void CapturedSlowWalkUsesLowClearanceAndCoordinatedArmedBody(int hz,float depth)
        {
            using(var rig=NaturalCharacterRig.Create())
            using(var local=new NativeArray<BoneLocal>(NaturalCharacterRig.Bones,Allocator.Temp))
            using(var world=new NativeArray<BoneWorld>(NaturalCharacterRig.Bones,Allocator.Temp))
            {
                {
                    var input=Input();input.Velocity=new float2(.864f,depth);var m=default(GameplayCharacterMotion);
                    float clearance=0,pathClearance=0,minPelvis=10,maxPelvis=-10,minChest=10,maxChest=-10,minElbow=10,maxElbow=-10,minHead=10,maxHead=-10,minGrip=10,maxGrip=-10;
                    for(int f=0;f<hz*5;f++)
                    {
                        input.Root=input.Ground+=input.Velocity/hz;var before=m;m.Step(input,1f/hz);GameplayCharacterMotion.Pose(rig.View,local,world,input,m,0);
                        Assert.IsTrue(m.FarFoot.InStance||m.NearFoot.InStance,"Walk must retain support");
                        CheckPlant(before.FarFoot,m.FarFoot,world[NaturalCharacterRig.FarFoot].Position,input.Scale);
                        CheckPlant(before.NearFoot,m.NearFoot,world[NaturalCharacterRig.NearFoot].Position,input.Scale);
                        if(f<hz)continue;
                        float stature=world[NaturalCharacterRig.Head].Position.y-input.Root.y;
                        clearance=math.max(clearance,(math.max(world[NaturalCharacterRig.FarFoot].Position.y,world[NaturalCharacterRig.NearFoot].Position.y)-input.Root.y)/stature);
                        pathClearance=math.max(pathClearance,math.max(Clearance(m.FarFoot),Clearance(m.NearFoot))*input.Scale/stature);
                        float pelvis=local[NaturalCharacterRig.Pelvis].Rotation,chest=world[NaturalCharacterRig.Torso].Rotation,elbow=local[NaturalCharacterRig.FarForearm].Rotation;
                        float head=world[NaturalCharacterRig.Head].Position.x-input.Root.x,grip=WeaponMotion.Attach(input,m,world,0).Rotation;
                        minPelvis=math.min(minPelvis,pelvis);maxPelvis=math.max(maxPelvis,pelvis);minChest=math.min(minChest,chest);maxChest=math.max(maxChest,chest);
                        minElbow=math.min(minElbow,elbow);maxElbow=math.max(maxElbow,elbow);minHead=math.min(minHead,head);maxHead=math.max(maxHead,head);minGrip=math.min(minGrip,grip);maxGrip=math.max(maxGrip,grip);
                    }
                    TestContext.WriteLine($"COORDINATED hz={hz} depth={depth} ankleElevation/stature={clearance:F5} pathClearance/stature={pathClearance:F5} pelvisAngle={maxPelvis-minPelvis:F5} chestAngle={maxChest-minChest:F5} elbowAngle={maxElbow-minElbow:F5} headX={maxHead-minHead:F5} holdAngle={maxGrip-minGrip:F5}");
                    // These are deliberately bounded regressions for the captured path, not
                    // a universal naturalness gate. Projected ankle elevation includes depth;
                    // pathClearance excludes the contact-path depth and the ankle rest height.
                    Assert.Greater(pathClearance,.014f);Assert.Less(pathClearance,.03f);
                    Assert.Less(clearance,depth==0?.085f:.12f,"screen-space Walk foot elevation includes projected depth; avoid the old 20% high-knee silhouette");
                    Assert.Greater(maxPelvis-minPelvis,.018f,"weight transfer must involve the pelvis");
                    Assert.Greater(maxChest-minChest,.075f,"armed chest must participate");
                    Assert.Greater(maxElbow-minElbow,.14f,"the slash free arm must participate");
                    Assert.Greater(maxHead-minHead,.035f,"head follows restrained body translation");
                    Assert.Greater(maxGrip-minGrip,.025f,"relaxed guard follows the body");
                    Assert.Less(maxGrip-minGrip,.16f,"guard cannot become a full free-arm swing");
                }
            }
        }
        [TestCase(30)] [TestCase(60)] [TestCase(120)]
        public void LowSpeedStartsStopsReversalsAndDepthKeepLiftAndBodyContinuous(int hz)
        {
            using(var rig=NaturalCharacterRig.Create())
            using(var local=new NativeArray<BoneLocal>(NaturalCharacterRig.Bones,Allocator.Temp))
            using(var world=new NativeArray<BoneWorld>(NaturalCharacterRig.Bones,Allocator.Temp))
            {
                for(int role=0;role<3;role++)
                {
                    var input=Input(role);input.Scale=1;var m=default(GameplayCharacterMotion);
                    float previousPelvis=0,previousChest=0,previousHold=0,maxLiftDelta=0,maxBodyDelta=0,maxHoldDelta=0;
                    for(int f=0;f<hz*9;f++)
                    {
                        int stage=f/hz;
                        float2 velocity=stage==0||stage>=7?float2.zero:stage==1?new float2(.04f,0):stage==2?new float2(.15f,.04f):
                            stage==3?new float2(.96f,.352f):stage==4?new float2(1.5f,-.5f):stage==5?new float2(4,0):new float2(-1.4f,.3f);
                        input.Velocity=velocity;input.Root=input.Ground+=velocity/hz;
                        // Reversing travel without flipping aim exercises backpedal and lets this
                        // bound measure pose continuity separately from authored facing changes.
                        var before=m;m.Step(input,1f/hz);GameplayCharacterMotion.Pose(rig.View,local,world,input,m,0);
                        if(m.Locomotion!=GameplayLocomotionState.Run)Assert.IsTrue(m.FarFoot.InStance||m.NearFoot.InStance);
                        CheckPlant(before.FarFoot,m.FarFoot,world[NaturalCharacterRig.FarFoot].Position,1);
                        CheckPlant(before.NearFoot,m.NearFoot,world[NaturalCharacterRig.NearFoot].Position,1);
                        Assert.IsFalse(m.Airborne,"visual Run flight cannot invent a gameplay jump");
                        float pelvis=local[NaturalCharacterRig.Pelvis].Rotation,chest=world[NaturalCharacterRig.Torso].Rotation,hold=WeaponMotion.Attach(input,m,world,0).Rotation;
                        if(f>0)
                        {
                            maxLiftDelta=math.max(maxLiftDelta,math.abs(m.SwingHeight-before.SwingHeight));
                            maxBodyDelta=math.max(maxBodyDelta,math.max(math.abs(pelvis-previousPelvis),math.abs(chest-previousChest)));
                            maxHoldDelta=math.max(maxHoldDelta,math.abs(WeaponMotion.AngleDelta(previousHold,hold)));
                            Assert.Less(math.abs(m.SwingHeight-before.SwingHeight),.28f*(1-math.exp(-18f/hz))+.00001f,"lift has the same bounded response at speed discontinuities");
                        }
                        previousPelvis=pelvis;previousChest=chest;previousHold=hold;
                        if(stage==1)Assert.Less(m.SwingHeight,.003f,"creeping must not use full-height marching");
                        if(stage==8){Assert.Less(math.abs(m.Gait),.00001f);Assert.Less(math.abs(pelvis),.00001f);Assert.Less(m.SwingHeight,.00001f);}
                    }
                    TestContext.WriteLine($"TRANSITIONS hz={hz} role={role} maxLiftDelta={maxLiftDelta:F5} maxBodyDelta={maxBodyDelta:F5} maxHoldDelta={maxHoldDelta:F5}");
                    Assert.Less(maxBodyDelta,.12f);Assert.Less(maxHoldDelta,.06f);
                }
            }
        }

        [Test]
        public void ActualRolePosesHaveDistinctCadenceClearanceAndFreeArmArcs()
        {
            using(var rig=NaturalCharacterRig.Create())
            using(var local=new NativeArray<BoneLocal>(NaturalCharacterRig.Bones,Allocator.Temp))
            using(var world=new NativeArray<BoneWorld>(NaturalCharacterRig.Bones,Allocator.Temp))
            {
                var armArc=new float[3];var clearance=new float[3];var landings=new int[3];
                for(int role=0;role<3;role++)
                {
                    var input=Input(role);input.Scale=1;input.Velocity=new float2(1,0);var m=default(GameplayCharacterMotion);float min=10,max=-10;
                    for(int f=0;f<120*8;f++)
                    {
                        input.Root=input.Ground+=input.Velocity/120;var before=m;m.Step(input,1f/120);GameplayCharacterMotion.Pose(rig.View,local,world,input,m,0);
                        if(f<120)continue;
                        min=math.min(min,local[NaturalCharacterRig.FarArm].Rotation);max=math.max(max,local[NaturalCharacterRig.FarArm].Rotation);
                        clearance[role]=math.max(clearance[role],m.SwingHeight);
                        if(!before.FarFoot.InStance&&m.FarFoot.InStance)landings[role]++;
                        if(!before.NearFoot.InStance&&m.NearFoot.InStance)landings[role]++;
                    }
                    armArc[role]=max-min;
                    TestContext.WriteLine($"ROLE role={role} actualArmArc={armArc[role]:F5} actualClearance={clearance[role]:F5} landings={landings[role]}");
                }
                Assert.Greater(armArc[1],armArc[0]+.08f);Assert.Greater(armArc[0],armArc[2]+.15f);
                Assert.Greater(clearance[1],clearance[0]+.008f);Assert.Greater(clearance[0],clearance[2]+.008f);
                Assert.Greater(landings[1],landings[0]);Assert.Greater(landings[0],landings[2]);
            }
        }

        [TestCase(30)] [TestCase(60)] [TestCase(120)]
        public void WalkingAttackAndRecoveryKeepLowerBodyAndCanonicalWeaponPriority(int hz)
        {
            using(var rig=NaturalCharacterRig.Create())
            using(var local=new NativeArray<BoneLocal>(NaturalCharacterRig.Bones,Allocator.Temp))
            using(var world=new NativeArray<BoneWorld>(NaturalCharacterRig.Bones,Allocator.Temp))
            {
                foreach(var p in SPF.L2.Weapons.WeaponProfiles.CreateDefaults(60))for(int role=0;role<3;role++)foreach(float facing in new[]{-1f,1f})
                {
                    var input=Input(role);input.Scale=1;input.Facing=facing;input.Velocity=new float2(.96f*facing,.352f);
                    input.Weapon=new WeaponViewState {ContentId=p.ContentId,VisualId=p.VisualId,Family=p.Family,AimDirection=new float2(facing,0),
                        GripOffset=p.GripOffset,SecondaryGripOffset=p.SecondaryGripOffset,MuzzleOffset=p.MuzzleOffset,
                        ContactPhase=(float)p.Active.From/p.DurationTicks,ReleasePhase=(float)p.ReleaseTick/p.DurationTicks,ActiveEndPhase=(float)p.Active.Until/p.DurationTicks};
                    var moving=default(GameplayCharacterMotion);var armed=default(GameplayCharacterMotion);
                    float2 previousGrip=0;float previousAngle=0;
                    for(int f=0;f<hz*3;f++)
                    {
                        input.Root=input.Ground+=input.Velocity/hz;var baseInput=input;baseInput.Weapon=default;baseInput.State=GameplayCharacterState.Walk;
                        moving.Step(baseInput,1f/hz);
                        input.Weapon.Phase=(f%hz)/(float)hz;input.Phase=input.Weapon.Phase;
                        input.Weapon.Stage=f<hz||f>=hz*2?WeaponStage.Idle:input.Weapon.Phase<input.Weapon.ContactPhase?WeaponStage.Windup:
                            input.Weapon.Phase<input.Weapon.ActiveEndPhase?WeaponStage.Active:WeaponStage.Recovery;
                        input.State=f<hz||f>=hz*2?GameplayCharacterState.Walk:input.Weapon.Stage==WeaponStage.Recovery?GameplayCharacterState.Recovery:GameplayCharacterState.Attack;
                        armed.Step(input,1f/hz);GameplayCharacterMotion.Pose(rig.View,local,world,input,armed,0);
                        Assert.AreEqual(moving.FarFoot.Position,armed.FarFoot.Position);Assert.AreEqual(moving.NearFoot.Position,armed.NearFoot.Position);
                        Assert.AreEqual(moving.Locomotion,armed.Locomotion,"attack overlay cannot erase the Walk base");
                        var guard=WeaponMotion.Attach(input,armed,world,0);float2 relativeGrip=guard.PrimaryGrip-input.Root;
                        if(f==hz||f==hz*2)
                        {
                            Assert.Less(math.distance(relativeGrip,previousGrip),.05f,"gait/action/recovery guard position cannot snap");
                            Assert.Less(math.abs(WeaponMotion.AngleDelta(previousAngle,guard.Rotation)),.07f,"gait/action/recovery guard angle cannot snap");
                        }
                        previousGrip=relativeGrip;previousAngle=guard.Rotation;
                        // Pose at the exact authoritative marker without advancing time/feet.
                        if(f%hz==hz/2)
                        {
                            var contact=input;contact.Weapon.Stage=WeaponStage.Active;
                            contact.Weapon.Phase=p.Family==WeaponActionFamily.Cast?contact.Weapon.ReleasePhase:contact.Weapon.ContactPhase;
                            GameplayCharacterMotion.Pose(rig.View,local,world,contact,armed,0);
                            var actual=WeaponMotion.Attach(contact,armed,world,0);
                            Assert.Less(math.distance(actual.Muzzle,WeaponMotion.WorldOffset(contact,contact.Weapon.AimDirection,p.MuzzleOffset)),.003f);
                        }
                    }
                }
            }
        }

        static float Clearance(in FootPlantState foot)
        {
            if(foot.InStance)return 0;
            float u=(foot.Phase-NaturalMotion.Stance)/(1-NaturalMotion.Stance);
            return foot.Position.y-math.lerp(foot.SwingStart.y,foot.SwingEnd.y,NaturalMotion.Ease(u));
        }
        static void CheckPlant(FootPlantState before,FootPlantState foot,float2 actual,float scale)
        {
            if(!foot.InStance)return;
            if(before.Initialized&&before.InStance)Assert.AreEqual(before.Plant,foot.Plant);
            Assert.Less(math.distance(actual,foot.Plant*scale),.0002f,"planted FK stays exact");
        }
    }
}
