using System;
using NUnit.Framework;
using SPF.Contracts;
using SPF.L1.Skeleton;
using SPF.Presentation;
using SPF.Presentation.Animation;
using SPF.Testing;
using Unity.Collections;
using Unity.Mathematics;

namespace SPF.Tests.EditMode
{
    public class GameplayCharacterTests
    {
        static GameplayCharacterInput Input(int index=1)=>new GameplayCharacterInput {Handle=new EntityHandle(index,1),Facing=1,Scale=1,Tint=new float4(1)};
        [Test] public void SelectionUsesStableHandlesAcrossReorderAndHasHardBudget()
        {
            var a=new GameplayCharacterSelection(8);var b=new GameplayCharacterSelection(8);a.Begin(0,8);b.Begin(0,8);
            for(int i=0;i<100;i++)a.Consider(new EntityHandle(i,1),new float2(i%13,i%7),i);
            for(int i=99;i>=0;i--)b.Consider(new EntityHandle(i,1),new float2(i%13,i%7),99-i);
            Assert.AreEqual(8,a.Count);Assert.AreEqual(8,b.Count);
            for(int i=0;i<a.Count;i++){bool found=false;for(int k=0;k<b.Count;k++)if(a.Handle(i)==b.Handle(k))found=true;Assert.IsTrue(found);}
            a.Begin(0,0);a.Consider(new EntityHandle(1,1),0,0);Assert.AreEqual(0,a.Count);
        }
        [Test] public void IdlePlantsAreExactAndTeleportResetsWorldAnchors()
        {
            var input=Input();var state=default(GameplayCharacterMotion);state.Step(input,1f/60);var foot=state.FarFoot.Position;
            for(int i=0;i<120;i++)state.Step(input,1f/60);
            Assert.AreEqual(foot,state.FarFoot.Position);
            input.Root=input.Ground=new float2(100,200);state.Step(input,1f/60);
            Assert.Less(math.distance(state.FarFoot.Position,input.Root),.3f);
        }
        [TestCase(30)] [TestCase(60)] [TestCase(120)]
        public void MovingMirroredStatesAlwaysHaveFiniteLimitedIk(int hz)
        {
            using(var rig=NaturalCharacterRig.Create())
            using(var local=new NativeArray<BoneLocal>(NaturalCharacterRig.Bones,Allocator.Temp))
            using(var world=new NativeArray<BoneWorld>(NaturalCharacterRig.Bones,Allocator.Temp))
            {
                var input=Input();var motion=default(GameplayCharacterMotion);float dt=1f/hz;
                for(int i=0;i<hz*5;i++)
                {
                    input.Root=input.Ground=new float2(i*dt*3,math.sin(i*dt));input.Velocity=new float2(3,math.cos(i*dt));
                    input.State=(GameplayCharacterState)((i/hz)%6);input.Facing=(i/hz&1)==0?1:-1;
                    input.Aim=true;input.AimTarget=new float2(1000,-200);input.Phase=math.frac(i*dt*2);motion.Step(input,dt);
                    GameplayCharacterMotion.Pose(rig.View,local,world,input,motion,0);
                    for(int k=0;k<world.Length;k++){Assert.IsTrue(math.all(math.isfinite(world[k].Position)));Assert.IsTrue(math.isfinite(world[k].Rotation));}
                    Assert.LessOrEqual(math.distance(world[NaturalCharacterRig.NearArm].Position,world[NaturalCharacterRig.Hand].Position),.851f);
                    Assert.LessOrEqual(math.distance(world[NaturalCharacterRig.FarThigh].Position,world[NaturalCharacterRig.FarFoot].Position),1.111f);
                }
            }
        }
        [Test] public void IdentitySurvivesReorderAndRecycledGenerationStartsFresh()
        {
            using(var presenter=new GameplayCharacterPresenter(RenderTier.DataTexture,3))
            {
                var a=Input(1);var b=Input(2);a.State=GameplayCharacterState.Hit;
                for(int i=0;i<10;i++){presenter.Begin(.02f,0);presenter.Submit(a);presenter.Submit(b);presenter.Evaluate();}
                Assert.IsTrue(presenter.TryRead(a.Handle,out var before));Assert.Greater(before.Hit,.8f);
                presenter.Begin(0,3);presenter.Submit(b);presenter.Submit(a);Assert.IsFalse(presenter.Submit(a));presenter.Evaluate();
                presenter.TryRead(a.Handle,out var after);Assert.AreEqual(before.Hit,after.Hit);Assert.AreEqual(before.FarFoot.Position,after.FarFoot.Position);
                a.Handle=new EntityHandle(1,2);a.State=GameplayCharacterState.Idle;
                presenter.Begin(.02f,0);presenter.Submit(a);presenter.Submit(b);presenter.Evaluate();
                presenter.TryRead(a.Handle,out var recycled);Assert.AreEqual(0,recycled.Hit);Assert.AreEqual(28,presenter.PartsDrawn);
            }
        }
        [Test] public void LowRateSecondaryPoseKeepsWorldPlantOnEveryRenderStep()
        {
            using(var presenter=new GameplayCharacterPresenter(RenderTier.DataTexture,1))
            {
                var input=Input();input.Kind=1;input.State=GameplayCharacterState.Run;input.Velocity=new float2(.35f,0);
                int reused=0;
                for(int i=0;i<120;i++)
                {
                    input.Root=input.Ground=new float2(i*.35f/120,0);
                    presenter.Begin(1f/120,3);presenter.Submit(input);presenter.Evaluate();
                    if(presenter.PosesEvaluated==0)reused++;
                    presenter.TryRead(input.Handle,out var motion);
                    var bone=presenter.ReadBone(input.Handle,NaturalCharacterRig.FarFoot);
                    Assert.Less(math.distance(bone.Position,motion.FarFoot.Position),.0001f,"FK must re-solve contact even on skipped body-pose frames");
                }
                Assert.Greater(reused,90);
            }
        }
        [Test] public void ProductionGroundDirectionsKeepBothFkContactsAtLowBodyRate()
        {
            float[] speeds={.35f,3.2f,5f,2.1f},scales={1f,.9f,.66f,.366f};
            using(var presenter=new GameplayCharacterPresenter(RenderTier.DataTexture,1))
            {
                for(int profile=0;profile<speeds.Length;profile++)for(int facing=-1;facing<=1;facing+=2)for(int direction=0;direction<8;direction++)
                {
                    presenter.Clear();var input=Input();input.Kind=1;input.Scale=scales[profile];input.Facing=facing;
                    float a=direction*math.PI/4;float2 velocity=new float2(math.cos(a),math.sin(a))*speeds[profile];
                    // Initial idle, steady travel, stopped stance, airborne travel and landing.
                    for(int frame=0;frame<300;frame++)
                    {
                        bool move=frame>=10&&frame<220||frame>=250;
                        input.Velocity=move?velocity:float2.zero;input.State=move?GameplayCharacterState.Run:GameplayCharacterState.Idle;
                        input.Ground+=input.Velocity/120f;input.Root=input.Ground;
                        if(frame>=250&&frame<280)input.Root.y+=.7f;
                        presenter.Begin(1f/120,3);presenter.Submit(input);presenter.Evaluate();presenter.TryRead(input.Handle,out var motion);
                        if(motion.Airborne)continue;
                        if(motion.FarFoot.InStance)
                            Assert.Less(math.distance(presenter.ReadBone(input.Handle,NaturalCharacterRig.FarFoot).Position,motion.FarFoot.Position*input.Scale),.0002f,
                                "far grounded FK contact profile="+profile+" facing="+facing+" direction="+direction+" frame="+frame);
                        if(motion.NearFoot.InStance)
                            Assert.Less(math.distance(presenter.ReadBone(input.Handle,NaturalCharacterRig.NearFoot).Position,motion.NearFoot.Position*input.Scale),.0002f,
                                "near grounded FK contact profile="+profile+" facing="+facing+" direction="+direction+" frame="+frame);
                    }
                }
            }
        }

        [Test] public void AirbornePoseReleasesGroundAndReplantsOnLanding()
        {
            var input=Input();var motion=default(GameplayCharacterMotion);motion.Step(input,.016f);
            input.Root=new float2(0,1);motion.Step(input,.016f);Assert.IsTrue(motion.Airborne);
            input.Root=input.Ground=new float2(2,0);motion.Step(input,.016f);Assert.IsFalse(motion.Airborne);
            Assert.Less(math.distance(motion.FarFoot.Position,input.Ground),.3f);
        }

        [Test] public void WarmedMotionAndSelectionAllocateNothingWithCalibratedProbe()
        {
            var input=Input();input.State=GameplayCharacterState.Run;input.Velocity=new float2(2,0);
            var motion=default(GameplayCharacterMotion);var selection=new GameplayCharacterSelection(64);
            Action work=()=>{for(int n=0;n<100;n++){input.Root.x+=.03f;input.Ground=input.Root;motion.Step(input,.016f);selection.Begin(input.Root,64);for(int k=0;k<512;k++)selection.Consider(new EntityHandle(k,1),new float2(k%20,k/20),k);}};
            work();using(var probe=new ManagedAllocationProbe()){probe.Calibrate();var sample=probe.Measure(work);probe.Calibrate();Assert.AreEqual(0,sample.Value);}
        }
        [Test] public void AttackRecoveryCurveIsContinuousAndReturnsToRest()
        {
            Assert.AreEqual(0,GameplayCharacterMotion.Strike(0));Assert.AreEqual(0,GameplayCharacterMotion.Strike(1));
            Assert.Greater(GameplayCharacterMotion.Strike(.42f),.999f);
            Assert.Less(math.abs(GameplayCharacterMotion.Strike(.2f-1e-5f)-GameplayCharacterMotion.Strike(.2f+1e-5f)),.0001f);
        }
    }
}
