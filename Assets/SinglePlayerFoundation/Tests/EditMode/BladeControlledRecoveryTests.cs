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
        public void BladeRecoversFromContactWithoutDownwardOvershoot(int hz)
        {
            using (var rig = NaturalCharacterRig.Create())
            using (var local = new NativeArray<BoneLocal>(NaturalCharacterRig.Bones, Allocator.Temp))
            using (var world = new NativeArray<BoneWorld>(NaturalCharacterRig.Bones, Allocator.Temp))
            for (int tickRate=30;tickRate<=60;tickRate+=30)
            for (int role=0;role<3;role++) for (int face=-1;face<=1;face+=2) for (int move=0;move<2;move++) {
                var input=Input(face,role,move!=0,tickRate);var motion=default(GameplayCharacterMotion);
                for(int frame=0;frame<hz;frame++)Pose(rig.View,local,world,ref input,ref motion,-1,1f/hz);
                float c=input.Weapon.ContactPhase;
                float duration=WeaponProfiles.CreateDefaults(tickRate)[0].DurationTicks/(float)tickRate;
                int before=(int)math.ceil(c*duration*hz);
                for(int i=0;i<=before;i++)Pose(rig.View,local,world,ref input,ref motion,c*i/before,1f/hz);
                var contact=WeaponMotion.Attach(input,motion,world,0);
                float handY=(contact.PrimaryGrip.y-input.Root.y)/input.Scale,tipY=(contact.Tip.y-input.Root.y)/input.Scale;
                float minAngle=0,minHand=0,minTip=0,maxPivot=0,maxPalmAngle=0;
                int count=(int)math.ceil((1-c)*duration*hz);
                for(int i=1;i<=count;i++) {
                    Pose(rig.View,local,world,ref input,ref motion,math.lerp(c,1,i/(float)count),1f/hz);
                    var at=WeaponMotion.Attach(input,motion,world,0);
                    minAngle=math.min(minAngle,WeaponMotion.AngleDelta(contact.Rotation,at.Rotation)*face);
                    minHand=math.min(minHand,(at.PrimaryGrip.y-input.Root.y)/input.Scale-handY);
                    minTip=math.min(minTip,(at.Tip.y-input.Root.y)/input.Scale-tipY);
                    maxPivot=math.max(maxPivot,math.distance(at.PrimaryGrip,world[NaturalCharacterRig.Hand].Position));
                    maxPalmAngle=math.max(maxPalmAngle,math.abs(WeaponMotion.AngleDelta(at.Rotation,world[NaturalCharacterRig.Hand].Rotation)));
                }
                string context="Hz="+hz+" tick="+tickRate+" role="+role+" face="+face+" move="+move
                    +" minAngle="+minAngle+" minHand="+minHand+" minTip="+minTip+" palmError="+maxPalmAngle;
                TestContext.WriteLine(context);
                Assert.GreaterOrEqual(minAngle,-.00001f,"no authored blade drop below contact "+context);
                Assert.GreaterOrEqual(minHand,-.001f,"final hand cannot sag below root-relative contact "+context);
                Assert.GreaterOrEqual(minTip,-.001f,"full blade tip cannot sag below contact "+context);
                Assert.Less(maxPivot,.000001f,"final hand is the actual weapon pivot "+context);
                Assert.Less(maxPalmAngle,.01f,"palm and blade stay rigidly aligned "+context);
            }
        }
        [Test]
        public void BladeRetainsExactContactWithCoherentZeroVelocity()
        {
            for(int rate=30;rate<=60;rate+=30)for(int face=-1;face<=1;face+=2) {
                var input=Input(face,0,false,rate);var motion=default(GameplayCharacterMotion);
                motion.Step(input,1f/60);input.Weapon.Stage=WeaponStage.Active;
                float c=input.Weapon.ContactPhase,h=.0001f;
                input.Weapon.Phase=c-h;var before=WeaponMotion.Sample(input,motion);
                input.Weapon.Phase=c;var contact=WeaponMotion.Sample(input,motion);
                input.Weapon.Phase=c+h;var after=WeaponMotion.Sample(input,motion);
                float2 expected=WeaponMotion.WorldOffset(input,input.Weapon.AimDirection,input.Weapon.GripOffset);
                Assert.Less(math.distance(contact.Grip,expected),.000001f);
                Assert.Less(math.abs(WeaponMotion.AngleDelta(face==1?0:math.PI,contact.Angle)),.000001f);
                Assert.Less(math.abs(contact.Body+.10f),.000001f);
                Assert.Less(math.abs(WeaponMotion.AngleDelta(before.Angle,contact.Angle))/h,.025f,"blade brakes before contact");
                Assert.Less(math.abs(WeaponMotion.AngleDelta(contact.Angle,after.Angle))/h,.025f);
                Assert.Less(math.length((contact.Grip-before.Grip)/h),.025f,"grip brakes with blade");
                Assert.Less(math.abs((contact.Body-before.Body)/h),.025f,"chest brakes with grip");
                Assert.Less(math.distance((contact.Grip-before.Grip)/h,(after.Grip-contact.Grip)/h),.025f);
            }
        }
        [Test]
        public void BladeStartsRecoveryImmediatelyAndRemainsC1AtEveryJoin()
        {
            for(int rate=30;rate<=60;rate+=30) {
                var input=Input(1,0,false,rate);var motion=default(GameplayCharacterMotion);motion.Step(input,1f/60);
                input.Weapon.Stage=WeaponStage.Active;
                float c=input.Weapon.ContactPhase,end=input.Weapon.ActiveEndPhase,h=.0001f;
                foreach(float key in new[]{c*.35f,c*.5f,c-(end-c)*.45f,c,end,1f-h}) {
                    input.Weapon.Phase=key-h;var before=WeaponMotion.Sample(input,motion);
                    input.Weapon.Phase=key;var at=WeaponMotion.Sample(input,motion);
                    input.Weapon.Phase=key+h;var after=WeaponMotion.Sample(input,motion);
                    Assert.Less(math.distance(at.Grip-before.Grip,after.Grip-at.Grip)/h,.04f,"C1 grip at "+key);
                    Assert.Less(math.abs((at.Angle-before.Angle)-(after.Angle-at.Angle))/h,.06f,"C1 blade at "+key);
                    Assert.Less(math.abs((at.Body-before.Body)-(after.Body-at.Body))/h,.04f,"C1 body at "+key);
                }
                input.Weapon.Phase=c;var contact=WeaponMotion.Sample(input,motion);
                input.Weapon.Phase=c+.01f;var recovering=WeaponMotion.Sample(input,motion);
                Assert.Greater(recovering.Angle,contact.Angle+.0001f,"no hold after contact");
                Assert.Less(recovering.Grip.x,contact.Grip.x-.00001f,"hand begins returning with blade");
                Assert.Greater(recovering.Grip.y,contact.Grip.y+.00001f);
                Assert.Greater(recovering.Body,contact.Body+.00001f,"body returns with grip");
                input.Weapon.Phase=1;var guard=WeaponMotion.Sample(input,motion);
                input.Weapon.Stage=WeaponStage.Idle;var idle=WeaponMotion.Sample(input,motion);
                Assert.That(guard.Grip,Is.EqualTo(idle.Grip));Assert.That(guard.Angle,Is.EqualTo(idle.Angle));
            }
        }
        [Test]
        public void BladeDenseRecoveryIsMonotonicInAimedContactFrame()
        {
            for(int face=-1;face<=1;face+=2)foreach(float pitch in new[]{-.25f,0,.25f}) {
                var input=Input(face,0,false);input.Root=input.Ground=new float2(8,-3);
                input.Weapon.AimDirection=new float2(face*math.cos(pitch),math.sin(pitch));
                var motion=default(GameplayCharacterMotion);motion.Step(input,1f/60);input.Weapon.Stage=WeaponStage.Active;
                float c=input.Weapon.ContactPhase;input.Weapon.Phase=c;var contact=WeaponMotion.Sample(input,motion);
                float previousAngle=0,previousHeight=0,previousTipHeight=0;
                for(int i=1;i<=1000;i++) {
                    input.Weapon.Phase=math.lerp(c,1,i/1000f);var at=WeaponMotion.Sample(input,motion);
                    float2 axis=new float2(math.cos(contact.Angle),math.sin(contact.Angle));
                    float2 normal=new float2(-axis.y*face,axis.x*face);
                    float angle=WeaponMotion.AngleDelta(contact.Angle,at.Angle)*face;
                    float height=math.dot((at.Grip-contact.Grip)/input.Scale,normal);
                    float2 tip=at.Grip+new float2(math.cos(at.Angle),math.sin(at.Angle))*at.Length*input.Scale;
                    float2 contactTip=contact.Grip+axis*contact.Length*input.Scale;
                    float tipHeight=math.dot((tip-contactTip)/input.Scale,normal);
                    Assert.GreaterOrEqual(angle,previousAngle-.000002f,"no oscillating angular recovery");
                    Assert.GreaterOrEqual(height,previousHeight-.000002f,"no sag or hand reversal in contact frame");
                    Assert.GreaterOrEqual(tipHeight,previousTipHeight-.000002f,"no tip reversal in contact frame");
                    previousAngle=angle;previousHeight=height;previousTipHeight=tipHeight;
                }
            }
        }
        [Test]
        public void BladeFinalChainIsC1AtBrakingContactAndGuard()
        {
            using(var rig=NaturalCharacterRig.Create())
            using(var local=new NativeArray<BoneLocal>(NaturalCharacterRig.Bones,Allocator.Temp))
            using(var world=new NativeArray<BoneWorld>(NaturalCharacterRig.Bones,Allocator.Temp))
            for(int rate=30;rate<=60;rate+=30)for(int face=-1;face<=1;face+=2)for(int role=0;role<3;role++) {
                var input=Input(face,role,false,rate);var motion=default(GameplayCharacterMotion);motion.Step(input,1f/60);
                input.Weapon.Stage=WeaponStage.Active;float c=input.Weapon.ContactPhase,h=.0001f;
                foreach(float key in new[]{c-(input.Weapon.ActiveEndPhase-c)*.45f,c,1f-h}) {
                    var before=new BoneWorld[NaturalCharacterRig.Bones];var at=new BoneWorld[NaturalCharacterRig.Bones];
                    input.Weapon.Phase=key-h;GameplayCharacterMotion.Pose(rig.View,local,world,input,motion,0);
                    var bladeBefore=WeaponMotion.Attach(input,motion,world,0);for(int j=0;j<before.Length;j++)before[j]=world[j];
                    input.Weapon.Phase=key;GameplayCharacterMotion.Pose(rig.View,local,world,input,motion,0);
                    var bladeAt=WeaponMotion.Attach(input,motion,world,0);for(int j=0;j<at.Length;j++)at[j]=world[j];
                    input.Weapon.Phase=key+h;GameplayCharacterMotion.Pose(rig.View,local,world,input,motion,0);
                    var bladeAfter=WeaponMotion.Attach(input,motion,world,0);
                    Assert.Less(math.distance(bladeAt.Tip-bladeBefore.Tip,bladeAfter.Tip-bladeAt.Tip)/h,.07f,"final tip C1");
                    foreach(int bone in new[]{NaturalCharacterRig.Torso,NaturalCharacterRig.NearArm,NaturalCharacterRig.NearForearm,NaturalCharacterRig.Hand}) {
                        string context="rate="+rate+" face="+face+" role="+role+" key="+key+" bone="+bone;
                        Assert.Less(math.distance(at[bone].Position-before[bone].Position,world[bone].Position-at[bone].Position)/h,.06f,"final-chain C1 position "+context);
                        Assert.Less(math.abs(WeaponMotion.AngleDelta(before[bone].Rotation,at[bone].Rotation)-WeaponMotion.AngleDelta(at[bone].Rotation,world[bone].Rotation))/h,.12f,"final-chain C1 angle "+context);
                    }
                    if(key==c) {
                        Assert.Less(math.distance(bladeAt.PrimaryGrip,WeaponMotion.WorldOffset(input,input.Weapon.AimDirection,input.Weapon.GripOffset)),.00001f);
                        Assert.Less(math.distance(bladeAt.Tip,WeaponMotion.WorldOffset(input,input.Weapon.AimDirection,input.Weapon.MuzzleOffset)),.00001f);
                    }
                }
            }
        }
        [TestCase(30)] [TestCase(60)] [TestCase(120)]
        public void BladeUpwardContactKeepsWristLimitWithBoundedShoulderRoom(int hz)
        {
            using(var rig=NaturalCharacterRig.Create())
            using(var local=new NativeArray<BoneLocal>(NaturalCharacterRig.Bones,Allocator.Temp))
            using(var world=new NativeArray<BoneWorld>(NaturalCharacterRig.Bones,Allocator.Temp))
            for(int role=0;role<3;role++)for(int move=0;move<2;move++)for(int aim=0;aim<16;aim++) {
                float angle=aim*math.PI/8;int face=math.cos(angle)<0?-1:1;
                var input=Input(face,role,false);input.Velocity=move!=0?new float2(0,3.5f):float2.zero;
                input.Weapon.AimDirection=new float2(math.cos(angle),math.sin(angle));input.SkillPoseId=101;input.SkillWeight=1;input.SkillPhase=.28f;
                var motion=default(GameplayCharacterMotion);
                for(int frame=0;frame<hz;frame++) {
                    Pose(rig.View,local,world,ref input,ref motion,input.Weapon.ContactPhase,1f/hz);
                    var at=WeaponMotion.Attach(input,motion,world,0);
                    string context="hz="+hz+" role="+role+" move="+move+" aim="+aim+" frame="+frame;
                    Assert.Less(math.distance(at.Tip,WeaponMotion.WorldOffset(input,input.Weapon.AimDirection,input.Weapon.MuzzleOffset)),.003f,context);
                    Assert.LessOrEqual(math.abs(local[NaturalCharacterRig.Hand].Rotation),.850001f,"original wrist bound "+context);
                    float2 anchor=rig.View.Bones[NaturalCharacterRig.NearArm].Position;anchor.x*=motion.Turn*motion.Facing;
                    Assert.LessOrEqual(math.distance(local[NaturalCharacterRig.NearArm].Position,anchor),.320001f,"original .10 fold plus at most .22 blade shoulder room "+context);
                }
            }
        }
        [TestCase(false)] [TestCase(true)]
        public void BladeElevatedGripKeepsShoulderRoomWhenCancelledOrEquipped(bool equip)
        {
            using(var rig=NaturalCharacterRig.Create())
            using(var local=new NativeArray<BoneLocal>(NaturalCharacterRig.Bones,Allocator.Temp))
            using(var world=new NativeArray<BoneWorld>(NaturalCharacterRig.Bones,Allocator.Temp))
            foreach(int schedule in new[]{30,60,120,0})for(int face=-1;face<=1;face+=2)for(int role=0;role<3;role++) {
                var input=Input(face,role,false);input.Weapon.AimDirection=new float2(0,1);input.Velocity=new float2(0,3.5f);
                input.SkillPoseId=101;input.SkillWeight=1;input.SkillPhase=.28f;var motion=default(GameplayCharacterMotion);
                for(int frame=0;frame<90;frame++)Pose(rig.View,local,world,ref input,ref motion,input.Weapon.ContactPhase,FrameStep(schedule,frame));
                var before=WeaponMotion.Attach(input,motion,world,0);float2 beforeShoulder=local[NaturalCharacterRig.NearArm].Position;
                input.Weapon.Stage=equip?WeaponStage.Equipping:WeaponStage.Idle;input.Weapon.Phase=0;input.Weapon.StagePhase=0;
                Pose(rig.View,local,world,ref input,ref motion,-1,0);var after=WeaponMotion.Attach(input,motion,world,0);
                string context="equip="+equip+" schedule="+schedule+" face="+face+" role="+role;
                Assert.Less(math.abs(WeaponMotion.AngleDelta(before.Rotation,after.Rotation)),.00001f,"elevated cancellation preserves displayed angle "+context);
                Assert.Less(math.distance(before.Tip,after.Tip),.00001f,"elevated cancellation preserves displayed tip "+context);
                // Existing punch/skill layers re-enter on cancellation and may move the
                // shoulder while the IK retains the displayed hand. Record that separately;
                // rigid weapon continuity must not be reported as whole-body C0.
                TestContext.WriteLine(context+" shoulderLocalDelta="+math.distance(beforeShoulder,local[NaturalCharacterRig.NearArm].Position));
                Pose(rig.View,local,world,ref input,ref motion,0,0);var restarted=WeaponMotion.Attach(input,motion,world,0);
                Assert.Less(math.abs(WeaponMotion.AngleDelta(after.Rotation,restarted.Rotation)),.00001f,"elevated immediate reattack preserves displayed angle "+context);
                Assert.Less(math.distance(after.Tip,restarted.Tip),.00001f,"elevated immediate reattack preserves displayed tip "+context);
                input.Weapon.Stage=equip?WeaponStage.Equipping:WeaponStage.Idle;input.Weapon.Phase=0;
                Pose(rig.View,local,world,ref input,ref motion,-1,0);after=WeaponMotion.Attach(input,motion,world,0);
                for(int frame=0;frame<24;frame++) {
                    float dt=FrameStep(schedule,frame);var previous=after;Pose(rig.View,local,world,ref input,ref motion,-1,dt);after=WeaponMotion.Attach(input,motion,world,0);
                    Assert.Less(math.distance(previous.Tip,after.Tip),.18f,"bounded visible return from elevated grip "+context);
                    Assert.LessOrEqual(math.abs(local[NaturalCharacterRig.Hand].Rotation),.850001f);
                }
            }
        }
        static float FrameStep(int schedule,int frame)
        {
            if(schedule>0)return 1f/schedule;
            switch(frame%5){case 0:return 1f/120;case 1:return 1f/30;case 2:return 1f/90;case 3:return 1f/60;default:return 1f/48;}
        }
        [TestCase(30)] [TestCase(60)] [TestCase(120)] [TestCase(0)]
        public void BladePresenterKeepsCurrentGripOnCachedFramesThroughTurnEquipAndCancel(int schedule)
        {
            using(var high=new GameplayCharacterPresenter(RenderTier.DataTexture,1,includeWeapons:true))
            using(var low=new GameplayCharacterPresenter(RenderTier.DataTexture,1,includeWeapons:true))
            using(var rig=NaturalCharacterRig.Create())
            using(var local=new NativeArray<BoneLocal>(NaturalCharacterRig.Bones,Allocator.Temp))
            using(var world=new NativeArray<BoneWorld>(NaturalCharacterRig.Bones,Allocator.Temp))
            for(int face=-1;face<=1;face+=2)for(int role=0;role<3;role++)for(int move=0;move<2;move++) {
                high.Clear();low.Clear();var input=Input(face,role,move!=0);input.Tint=new float4(1);
                var motion=default(GameplayCharacterMotion);float time=0,maxPalmError=0;int frame=0,cached=0;
                while(time<4) {
                    float dt=FrameStep(schedule,frame++);time+=dt;
                    int direction=time<1.9f?face:-face;input.Facing=direction;
                    input.Weapon.AimDirection=new float2(direction,0);input.Velocity=move!=0?new float2(direction*.8f,.2f):float2.zero;
                    input.Root=input.Ground+=input.Velocity*dt;
                    input.State=move!=0?GameplayCharacterState.Run:GameplayCharacterState.Idle;
                    input.Weapon.Stage=WeaponStage.Idle;input.Weapon.Phase=0;input.Weapon.StagePhase=0;
                    float phase=time>=1&&time<1.64f?(time-1)/.64f:time>=2.8f&&time<3.05f?(time-2.8f)/.64f:-1;
                    if(phase>=0){input.State=GameplayCharacterState.Attack;input.Phase=input.Weapon.Phase=phase;
                        input.Weapon.Stage=phase<input.Weapon.ContactPhase?WeaponStage.Windup:phase<input.Weapon.ActiveEndPhase?WeaponStage.Active:WeaponStage.Recovery;}
                    if(time>=2.05f&&time<2.27f){input.Weapon.Stage=WeaponStage.Equipping;input.Weapon.StagePhase=(time-2.05f)/.22f;}
                    motion.Step(input,dt);GameplayCharacterMotion.Pose(rig.View,local,world,input,motion,0);
                    high.Begin(dt,0);low.Begin(dt,3);Assert.IsTrue(high.Submit(input));Assert.IsTrue(low.Submit(input));high.Evaluate();low.Evaluate();
                    if(low.BasePoseRefreshes==0)cached++;
                    Assert.IsTrue(high.TryReadWeapon(input.Handle,out var a));Assert.IsTrue(low.TryReadWeapon(input.Handle,out var b));
                    var fresh=WeaponMotion.Attach(input,motion,world,0);var hand=low.ReadBone(input.Handle,NaturalCharacterRig.Hand);
                    string context="schedule="+schedule+" face="+face+" role="+role+" move="+move+" time="+time;
                    Assert.Less(math.distance(fresh.PrimaryGrip,b.PrimaryGrip),.000001f,"cached hand has no phase lag "+context);
                    Assert.Less(math.abs(WeaponMotion.AngleDelta(fresh.Rotation,b.Rotation)),.000001f,"cached angle uses current phase "+context);
                    Assert.Less(math.distance(hand.Position,b.PrimaryGrip),.000001f,"final hand is the weapon pivot "+context);
                    Assert.Less(math.distance(a.Tip,b.Tip),.000001f,"quality does not change blade pose "+context);
                    for(int bone=0;bone<NaturalCharacterRig.Bones;bone++) {
                        Assert.Less(math.distance(high.ReadBone(input.Handle,bone).Position,low.ReadBone(input.Handle,bone).Position),.000001f,context);
                        Assert.IsTrue(math.all(math.isfinite(low.ReadBone(input.Handle,bone).Position)),context);
                    }
                    float palmError=math.abs(WeaponMotion.AngleDelta(hand.Rotation,b.Rotation));maxPalmError=math.max(maxPalmError,palmError);
                    Assert.Less(palmError,.01f,"palm direction remains rigid at supported transitions "+context);
                    var fist=low.ReadPart(17);Assert.Less(math.distance(fist.Center,b.PrimaryGrip),.000001f,"closed-grip sprite uses same final pivot");
                    var weapon=low.ReadPart(11);float length=math.distance(b.PrimaryGrip,b.Tip)/input.Scale;
                    float2 renderedPivot=weapon.Center-WeaponMotion.Rotate(WeaponArt.Centre(1001,length)*new float2(input.Scale,input.Scale*direction),weapon.Rotation);
                    Assert.Less(math.distance(renderedPivot,fist.Center),.001f,"packed weapon pivot stays in fist within half-angle packing precision");
                    if(time>=1+input.Weapon.ContactPhase*.64f&&time<1.64f)
                        Assert.GreaterOrEqual((b.Tip.y-input.Root.y)/input.Scale,1.25f-.001f,"irregular schedule cannot introduce a below-contact tip");
                }
                if(role%2==1||schedule==0||schedule>60)
                    Assert.Greater(cached,0,"exercise the actual cached-pose correction path");
                TestContext.WriteLine("schedule="+schedule+" face="+face+" role="+role+" move="+move+" cached="+cached+" maxPalmError="+maxPalmError);
            }
        }
        [TestCase(30)] [TestCase(60)] [TestCase(120)] [TestCase(0)]
        public void BladeAttackAndCancelDuringTurnKeepDisplayedOrientationContinuous(int schedule)
        {
            using(var rig=NaturalCharacterRig.Create())
            using(var local=new NativeArray<BoneLocal>(NaturalCharacterRig.Bones,Allocator.Temp))
            using(var world=new NativeArray<BoneWorld>(NaturalCharacterRig.Bones,Allocator.Temp))
            foreach(float delay in new[]{0f,.008f,.016f,.033f,.05f,.083f,.125f,.2f})
            for(int face=-1;face<=1;face+=2)for(int move=0;move<2;move++) {
                var input=Input(face,1,move!=0);var motion=default(GameplayCharacterMotion);
                for(int frame=0;frame<120;frame++)Pose(rig.View,local,world,ref input,ref motion,-1,1f/120);
                input.Facing=-face;input.Weapon.AimDirection=new float2(-face,0);input.Velocity=-input.Velocity;
                float elapsed=0;int count=0;
                do {float dt=FrameStep(schedule,count++);elapsed+=dt;Pose(rig.View,local,world,ref input,ref motion,-1,dt);}while(elapsed<delay);
                var idle=WeaponMotion.Attach(input,motion,world,0);
                Pose(rig.View,local,world,ref input,ref motion,0,0);var start=WeaponMotion.Attach(input,motion,world,0);
                string context="schedule="+schedule+" delay="+delay+" face="+face+" move="+move;
                Assert.Less(math.abs(WeaponMotion.AngleDelta(idle.Rotation,start.Rotation)),.00001f,"displayed Idle to Windup continuity during turn "+context);
                Assert.Less(math.distance(idle.Tip,start.Tip),.00001f,context);
                float actionElapsed=0;
                for(int i=0;i<3;i++) {
                    float dt=FrameStep(schedule,count++);actionElapsed+=dt;Pose(rig.View,local,world,ref input,ref motion,actionElapsed/.64f,dt);
                    var at=WeaponMotion.Attach(input,motion,world,0);
                    Assert.Less(math.abs(WeaponMotion.AngleDelta(at.Rotation,world[NaturalCharacterRig.Hand].Rotation)),.01f,"no transient palm/blade separation "+context);
                    Assert.That(math.distance(at.PrimaryGrip,at.Tip)/input.Scale,Is.EqualTo(1.07f).Within(.00001f),"rigid blade cannot shrink during turn/attack "+context);
                }
                var before=WeaponMotion.Attach(input,motion,world,0);
                input.Weapon.Stage=WeaponStage.Idle;input.Weapon.Phase=0;
                Pose(rig.View,local,world,ref input,ref motion,-1,0);var cancelled=WeaponMotion.Attach(input,motion,world,0);
                Assert.Less(math.abs(WeaponMotion.AngleDelta(before.Rotation,cancelled.Rotation)),.00001f,"displayed cancellation continuity during turn "+context);
                Assert.Less(math.distance(before.Tip,cancelled.Tip),.00001f,"displayed cancellation tip continuity "+context);
                Pose(rig.View,local,world,ref input,ref motion,0,0);var restarted=WeaponMotion.Attach(input,motion,world,0);
                Assert.Less(math.abs(WeaponMotion.AngleDelta(cancelled.Rotation,restarted.Rotation)),.00001f,"immediate reattack keeps displayed orientation "+context);
                Assert.Less(math.distance(cancelled.Tip,restarted.Tip),.00001f,"immediate reattack keeps displayed tip "+context);
            }
        }
        [TestCase(30)] [TestCase(60)]
        public void BladeAuthoritativeImmediateReentryPreservesContactAndRigidGrip(int tickRate)
        {
            using(var presenter=new GameplayCharacterPresenter(RenderTier.DataTexture,1,includeWeapons:true))
            using(var rig=NaturalCharacterRig.Create())
            using(var local=new NativeArray<BoneLocal>(NaturalCharacterRig.Bones,Allocator.Temp))
            using(var world=new NativeArray<BoneWorld>(NaturalCharacterRig.Bones,Allocator.Temp))
            foreach(int schedule in new[]{30,60,120,0})for(int scenario=0;scenario<4;scenario++)
            for(int face=-1;face<=1;face+=2)for(int move=0;move<2;move++)for(int elevated=0;elevated<2;elevated++) {
                presenter.Clear();using(var runtime=new WeaponRuntime(WeaponProfiles.CreateDefaults(tickRate),tickRate,4,4,8)) {
                    var input=Input(face,1,move!=0,tickRate);input.Tint=new float4(1);runtime.Owner=input.Handle;
                    if(elevated!=0){input.SkillPoseId=101;input.SkillWeight=1;input.SkillPhase=.28f;}
                    var simulationInput=input;var simulationMotion=default(GameplayCharacterMotion);
                    if(scenario==3)Assert.IsTrue(runtime.RequestEquip(1002));
                    float elapsed=0,simulated=0;int frame=0,contacts=0,equipping=0;uint lastPulse=0;int begins=0;
                    while(elapsed<3.4f) {
                        float dt=FrameStep(schedule,frame++);elapsed+=dt;
                        while(simulated+1f/tickRate<=elapsed) {
                            int tick=(int)runtime.Tick+1;int direction=tick<tickRate?face:-face;
                            bool pressed=tick==tickRate;
                            bool held=scenario==1&&tick>=tickRate;
                            bool interrupted=scenario==2&&tick==tickRate+2;
                            if(scenario==2&&tick==tickRate+3)pressed=true;
                            if(scenario==1&&runtime.Equipment.Timeline.Running&&runtime.Equipment.Timeline.Tick>=runtime.Current.Active.Until)pressed=true;
                            if(scenario==3&&tick==tickRate)Assert.IsTrue(runtime.RequestEquip(1001));
                            if(scenario==3&&runtime.Equipment.EquipRemaining>0&&tick>=tickRate)pressed=true;
                            simulationInput.Facing=direction;simulationInput.Velocity=move!=0?(elevated!=0?new float2(0,3.5f):new float2(direction*.8f,.2f)):float2.zero;
                            simulationInput.Root=simulationInput.Ground+=simulationInput.Velocity/tickRate;
                            runtime.Step(true,true,interrupted,pressed,held,elevated!=0?new float2(0,1):new float2(direction,0),simulationInput.Ground,0,input.Scale);
                            simulated+=1f/tickRate;simulationInput.Weapon=runtime.View(1);
                            simulationInput.State=WeaponMotion.Acting(simulationInput.Weapon)?GameplayCharacterState.Attack:GameplayCharacterState.Run;
                            simulationInput.Phase=simulationInput.Weapon.Phase;
                            simulationMotion.Step(simulationInput,1f/tickRate);GameplayCharacterMotion.Pose(rig.View,local,world,simulationInput,simulationMotion,0);
                            if(simulationInput.Weapon.Stage==WeaponStage.Equipping&&tick>=tickRate)equipping++;
                            if(simulationInput.Weapon.ActionPulse!=lastPulse){begins++;lastPulse=simulationInput.Weapon.ActionPulse;}
                            if(simulationInput.Weapon.VisualId==1001&&runtime.Equipment.Timeline.Running&&runtime.Equipment.Timeline.Tick==runtime.Current.Active.From) {
                                contacts++;var contact=WeaponMotion.Attach(simulationInput,simulationMotion,world,0);
                                string context="rate="+tickRate+" schedule="+schedule+" scenario="+scenario+" face="+face+" move="+move+" elevated="+elevated;
                                Assert.That(simulationInput.Weapon.Phase,Is.EqualTo(simulationInput.Weapon.ContactPhase),"unchanged authoritative hit phase "+context);
                                Assert.Less(math.distance(contact.PrimaryGrip,WeaponMotion.WorldOffset(simulationInput,simulationInput.Weapon.AimDirection,simulationInput.Weapon.GripOffset)),.003f,context);
                                Assert.Less(math.distance(contact.Tip,WeaponMotion.WorldOffset(simulationInput,simulationInput.Weapon.AimDirection,simulationInput.Weapon.MuzzleOffset)),.003f,"true-runtime contact direction/position "+context);
                            }
                        }
                        int facing=elapsed<1?face:-face;input.Facing=facing;input.Velocity=move!=0?(elevated!=0?new float2(0,3.5f):new float2(facing*.8f,.2f)):float2.zero;
                        input.Root=input.Ground+=input.Velocity*dt;input.Weapon=runtime.View(math.saturate((elapsed-simulated)*tickRate));
                        input.State=WeaponMotion.Acting(input.Weapon)?GameplayCharacterState.Attack:GameplayCharacterState.Run;input.Phase=input.Weapon.Phase;
                        presenter.Begin(dt,3);presenter.Submit(input);presenter.Evaluate();presenter.TryReadWeapon(input.Handle,out var at);
                        if(input.Weapon.VisualId==1001) {
                            var hand=presenter.ReadBone(input.Handle,NaturalCharacterRig.Hand);
                            Assert.Less(math.distance(hand.Position,at.PrimaryGrip),.000001f);
                            Assert.Less(math.abs(WeaponMotion.AngleDelta(hand.Rotation,at.Rotation)),.000001f,"rigid displayed grip during immediate reentry");
                            Assert.IsTrue(math.all(math.isfinite(at.Tip)));
                        }
                    }
                    Assert.Greater(contacts,0,"every scenario must actually reach blade contact");
                    if(scenario==1||scenario==2)Assert.Greater(begins,1,"exercise repeat/restart");
                    if(scenario==3)Assert.Greater(equipping,0,"press attack while equip is still pending");
                }
            }
        }
        [TestCase(30)] [TestCase(60)]
        public void BladeActualEquipmentAndInterruptLifecycleKeepsFinalAttachment(int tickRate)
        {
            using(var presenter=new GameplayCharacterPresenter(RenderTier.DataTexture,1,includeWeapons:true))
            foreach(int schedule in new[]{30,60,120,0})for(int face=-1;face<=1;face+=2) {
                presenter.Clear();using(var runtime=new WeaponRuntime(WeaponProfiles.CreateDefaults(tickRate),tickRate,4,4,8)) {
                    var input=Input(face,1,true,tickRate);input.Tint=new float4(1);runtime.Owner=input.Handle;
                    float elapsed=0,simulated=0;int frame=0,swordFrames=0,bladeReturnFrames=0,cancelledFrames=0;
                    while(elapsed<4.6f) {
                        float dt=FrameStep(schedule,frame++);elapsed+=dt;
                        while(simulated+1f/tickRate<=elapsed) {
                            int tick=(int)runtime.Tick+1;
                            if(tick==2*tickRate)Assert.IsTrue(runtime.RequestEquip(1002));
                            if(tick==(int)(2.6f*tickRate))Assert.IsTrue(runtime.RequestEquip(1001));
                            bool pressed=tick==(int)(3.2f*tickRate)||tick==(int)(3.75f*tickRate);
                            bool held=tick>=tickRate&&tick<(int)(1.6f*tickRate);
                            runtime.Step(true,true,tick==(int)(3.35f*tickRate),pressed,held,new float2(face,0),input.Ground,0,input.Scale);
                            simulated+=1f/tickRate;
                        }
                        input.Root=input.Ground+=input.Velocity*dt;
                        input.Weapon=runtime.View(math.saturate((elapsed-simulated)*tickRate));
                        input.State=WeaponMotion.Acting(input.Weapon)?GameplayCharacterState.Attack:GameplayCharacterState.Run;
                        input.Phase=input.Weapon.Phase;
                        presenter.Begin(dt,3);presenter.Submit(input);presenter.Evaluate();
                        Assert.IsTrue(presenter.TryReadWeapon(input.Handle,out var at));
                        var hand=presenter.ReadBone(input.Handle,NaturalCharacterRig.Hand);
                        Assert.Less(math.distance(hand.Position,at.PrimaryGrip),.000001f,"actual equipment uses final hand pivot");
                        if(input.Weapon.VisualId==1002)swordFrames++;
                        else {
                            if(elapsed>2.8f)bladeReturnFrames++;
                            if(elapsed>3.35f&&elapsed<3.75f)cancelledFrames++;
                            Assert.Less(math.abs(WeaponMotion.AngleDelta(hand.Rotation,at.Rotation)),.01f,"blade palm angle through real equip/cancel schedule="+schedule+" time="+elapsed);
                            if(WeaponMotion.Acting(input.Weapon)&&input.Weapon.Phase>=input.Weapon.ContactPhase)
                                Assert.GreaterOrEqual((at.Tip.y-input.Root.y)/input.Scale,1.25f-.001f,"authoritative phase retains no-droop recovery");
                        }
                    }
                    Assert.Greater(swordFrames,0);Assert.Greater(bladeReturnFrames,0);Assert.Greater(cancelledFrames,0);
                }
            }
        }
        [TestCase(30)] [TestCase(60)] [TestCase(120)]
        public void BladeContactAndRecoveryInterruptWithoutSnapping(int hz)
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
