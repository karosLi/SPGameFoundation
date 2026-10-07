using NUnit.Framework;
using SPF.Contracts;
using SPF.Contracts.Weapons;
using SPF.L1.Skeleton;
using SPF.Presentation.Animation;
using Unity.Collections;
using Unity.Mathematics;

namespace SPF.Tests.EditMode
{
    public class GroundedGaitInterruptionTests
    {
        static GameplayCharacterInput Input()=>new GameplayCharacterInput{Handle=new EntityHandle(1,1),Scale=.9f,Facing=1,Tint=new float4(1)};
        [TestCase(30)] [TestCase(60)] [TestCase(120)]
        public void RestartDuringFarFootSettlementKeepsTheOnlyExistingSupport(int hz)
        {
            for(int role=0;role<3;role++)foreach(int stoppedFrames in new[]{1,2,3})foreach(int axis in new[]{0,1})
            {
                var input=Input();input.MotionProfileId=role;var m=default(GameplayCharacterMotion);float dt=1f/hz;
                input.Velocity=axis==0?new float2(1.5f,0):new float2(0,1.2f);
                bool found=false;
                for(int f=0;f<hz*3;f++)
                {
                    input.Root=input.Ground+=input.Velocity*dt;m.Step(input,dt);
                    if(!m.FarFoot.InStance&&m.NearFoot.InStance&&m.FarFoot.Phase<.8f){found=true;break;}
                }
                Assert.IsTrue(found);input.Velocity=0;input.State=GameplayCharacterState.Hit;
                for(int f=0;f<stoppedFrames;f++)m.Step(input,dt);
                input.Velocity=axis==0?new float2(-2.2247f,0):new float2(0,-1.2f);input.State=GameplayCharacterState.Recovery;
                for(int f=0;f<hz/2;f++)
                {
                    input.Root=input.Ground+=input.Velocity*dt;m.Step(input,dt);
                    Assert.IsTrue(m.FarFoot.InStance||m.NearFoot.InStance,$"role={role} stop={stoppedFrames} axis={axis} frame={f}");
                }
            }
        }
        [TestCase(30)] [TestCase(60)] [TestCase(120)]
        public void CapturedDepthTurnDoesNotLeaveTheTrailingLandingAsTheOnlySupport(int hz)
        {
            // Boundary reconstructed from actual grounded-horde-live-gpu frame 135 and its
            // next captured landing. No hit/weapon layer is needed to reproduce the support dip.
            var input=Input();input.Scale=.66f;input.Facing=-1;input.Root=input.Ground=new float2(8.056909f,6.0141573f);
            var m=default(GameplayCharacterMotion);m.Step(input,0);m.Moving=true;m.Move=1;m.Walk=1;m.NextNearStep=false;
            NaturalMotion.InitializeFoot(ref m.FarFoot,input.Ground/input.Scale,new float2(8.258318f,6.268847f)/input.Scale,.490654469f);
            NaturalMotion.InitializeFoot(ref m.NearFoot,input.Ground/input.Scale,new float2(7.72599268f,6.053631f)/input.Scale,.98508656f);
            m.FarFoot.SupportSeconds=.3f;m.NearFoot.AirSeconds=.3f;m.NearFoot.SwingEnd=new float2(7.72558165f,6.05216837f)/input.Scale;m.NearSwingSeconds=.28f;
            input.Facing=1;input.Velocity=new float2(0,1);input.State=GameplayCharacterState.Walk;
            using(var rig=NaturalCharacterRig.Create())
            using(var local=new NativeArray<BoneLocal>(NaturalCharacterRig.Bones,Allocator.Temp))
            using(var world=new NativeArray<BoneWorld>(NaturalCharacterRig.Bones,Allocator.Temp))
            {
                float lowest=10,highest=-10;
                for(int i=0;i<hz;i++)
                {
                    input.Root=input.Ground+=input.Velocity/hz;m.Step(input,1f/hz);GameplayCharacterMotion.Pose(rig.View,local,world,input,m,0);
                    Assert.IsTrue(m.FarFoot.InStance||m.NearFoot.InStance);
                    float y=(world[NaturalCharacterRig.Pelvis].Position.y-input.Root.y)/input.Scale;lowest=math.min(lowest,y);highest=math.max(highest,y);
                    if(m.FarFoot.InStance)Assert.Less(math.distance(world[NaturalCharacterRig.FarFoot].Position,m.FarFoot.Position*input.Scale),.0002f);
                    if(m.NearFoot.InStance)Assert.Less(math.distance(world[NaturalCharacterRig.NearFoot].Position,m.NearFoot.Position*input.Scale),.0002f);
                }
                TestContext.WriteLine($"Captured depth-turn pelvis {lowest}..{highest}");
                Assert.Less(highest-lowest,.10f,"turning on the ground must not create a repeated squat/recovery bounce");
            }
        }
        [TestCase(30)] [TestCase(60)] [TestCase(120)]
        public void HorizontalWeaponFacingReversalCannotInventADownwardAimCrouch(int hz)
        {
            using(var rig=NaturalCharacterRig.Create())
            using(var local=new NativeArray<BoneLocal>(NaturalCharacterRig.Bones,Allocator.Temp))
            using(var world=new NativeArray<BoneWorld>(NaturalCharacterRig.Bones,Allocator.Temp))
            {
                var input=Input();input.Scale=1;input.Weapon=new WeaponViewState{ContentId=1001,VisualId=1001,Family=WeaponActionFamily.Slash,AimDirection=new float2(1,0),GripOffset=new float2(.58f,1.25f),MuzzleOffset=new float2(1.65f,1.25f)};
                var m=default(GameplayCharacterMotion);for(int i=0;i<hz;i++)m.Step(input,1f/hz);
                float lowest=10,highest=-10;
                for(int i=0;i<hz;i++)
                {
                    input.Facing=-1;input.Weapon.AimDirection=new float2(-1,0);input.State=i<hz/4?GameplayCharacterState.Hit:GameplayCharacterState.Idle;
                    m.Step(input,1f/hz);GameplayCharacterMotion.Pose(rig.View,local,world,input,m,0);
                    float y=world[NaturalCharacterRig.Pelvis].Position.y;lowest=math.min(lowest,y);highest=math.max(highest,y);
                }
                TestContext.WriteLine($"Horizontal reversal pelvis {lowest}..{highest}");
                Assert.Less(highest-lowest,.04f,"a horizontal facing turn must not produce the downward weapon-aim crouch");
            }
        }
        [TestCase(30)] [TestCase(60)] [TestCase(120)]
        public void RealVerticalAimRetainsAccommodationAndReleaseSocketsAcrossActionStates(int hz)
        {
            using(var rig=NaturalCharacterRig.Create())
            using(var local=new NativeArray<BoneLocal>(NaturalCharacterRig.Bones,Allocator.Temp))
            using(var world=new NativeArray<BoneWorld>(NaturalCharacterRig.Bones,Allocator.Temp))
            {
                foreach(var p in SPF.L2.Weapons.WeaponProfiles.CreateDefaults(60))foreach(bool moving in new[]{false,true})
                foreach(var stage in new[]{WeaponStage.Idle,WeaponStage.Recovery,WeaponStage.Active})foreach(int vertical in new[]{-1,1})
                {
                    var input=Input();input.Scale=1;input.Velocity=moving?new float2(.5f,.25f):float2.zero;
                    input.Weapon=new WeaponViewState{ContentId=p.ContentId,VisualId=p.VisualId,Family=p.Family,Stage=stage,
                        Phase=stage==WeaponStage.Recovery?.8f:(float)p.ReleaseTick/p.DurationTicks,ContactPhase=(float)p.Active.From/p.DurationTicks,
                        ReleasePhase=(float)p.ReleaseTick/p.DurationTicks,ActiveEndPhase=(float)p.Active.Until/p.DurationTicks,
                        AimDirection=new float2(1,0),GripOffset=p.GripOffset,SecondaryGripOffset=p.SecondaryGripOffset,MuzzleOffset=p.MuzzleOffset};
                    input.State=stage==WeaponStage.Recovery?GameplayCharacterState.Recovery:stage==WeaponStage.Active?GameplayCharacterState.Attack:GameplayCharacterState.Idle;
                    input.Phase=input.Weapon.Phase;var m=default(GameplayCharacterMotion);
                    for(int i=0;i<hz/2;i++){input.Root=input.Ground+=input.Velocity/hz;m.Step(input,1f/hz);}
                    input.Weapon.AimDirection=new float2(0,vertical);
                    for(int i=0;i<hz/2;i++)
                    {
                        input.Root=input.Ground+=input.Velocity/hz;m.Step(input,1f/hz);GameplayCharacterMotion.Pose(rig.View,local,world,input,m,0);
                        if(stage==WeaponStage.Active)
                        {var attachment=WeaponMotion.Attach(input,m,world,0);Assert.Less(math.distance(attachment.Muzzle,WeaponMotion.WorldOffset(input,input.Weapon.AimDirection,p.MuzzleOffset)),.003f);}
                        for(int bone=0;bone<world.Length;bone++)Assert.IsTrue(math.all(math.isfinite(world[bone].Position)));
                    }
                    Assert.That(m.WeaponAimDrop,Is.EqualTo(vertical<0?.16f:0).Within(.0001f),"true downward aim still has geometric accommodation while up aim does not");
                }
            }
        }
        [Test]
        public void CurrentFrameDiagnosticExcludesRetainedOffscreenActors()
        {
            using(var presenter=new GameplayCharacterPresenter(SPF.Presentation.RenderTier.DataTexture,1))
            {
                var input=Input();presenter.Begin(1f/60,0);presenter.Submit(input);presenter.Evaluate();Assert.IsTrue(presenter.TryReadCurrent(input.Handle,out _));
                presenter.Begin(1f/60,0);presenter.Evaluate();Assert.IsTrue(presenter.TryRead(input.Handle,out _));Assert.IsFalse(presenter.TryReadCurrent(input.Handle,out _));
            }
        }
    }
}
