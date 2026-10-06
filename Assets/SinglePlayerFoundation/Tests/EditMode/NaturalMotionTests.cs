using System;
using NUnit.Framework;
using SPF.Testing;
using SPF.L1.Skeleton;
using SPF.Presentation.Animation;
using SPF.Presentation.Sprites;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace SPF.Tests.EditMode
{
    public class NaturalMotionTests
    {
        [TestCase(30)] [TestCase(60)] [TestCase(120)]
        public void StanceAnchorNeverSlides(int hz)
        {
            FootPlantState foot=default;float dt=1f/hz;
            NaturalMotion.InitializeFoot(ref foot,0,new float2(.1f,0));
            for(int i=1;i<=hz*8;i++)
            {
                float2 plant=foot.Plant;bool wasStance=foot.InStance;
                NaturalMotion.StepFoot(ref foot,new float2(i*dt*.3f,0),new float2(.3f,0),0,dt,0);
                if(wasStance&&foot.InStance)Assert.That(math.distance(foot.Position,plant),Is.LessThan(1e-7f));
                Assert.That(math.all(math.isfinite(foot.Position)),Is.True);
                Assert.That(foot.Position.y,Is.GreaterThanOrEqualTo(-1e-6f));
            }
        }
        static FootPlantState Run(int hz)
        {
            FootPlantState f=default;NaturalMotion.InitializeFoot(ref f,0,0);
            for(int i=1;i<=hz*5;i++)NaturalMotion.StepFoot(ref f,new float2(i/(float)hz*.4f,0),new float2(.4f,0),0,1f/hz,0);
            return f;
        }
        [Test] public void LandingAndPhaseAreStableAcrossFrameRates()
        {
            var a=Run(30);var b=Run(60);var c=Run(120);
            Assert.That(math.distance(a.Plant,c.Plant),Is.LessThan(.0001f));
            Assert.That(math.distance(a.Position,c.Position),Is.LessThan(.0001f));
            Assert.That(math.distance(b.Position,c.Position),Is.LessThan(.0001f));
            Assert.That(math.abs(a.Phase-c.Phase),Is.LessThan(.0001f));
        }
        [TestCase(0f)] [TestCase(.62f)] [TestCase(1f)]
        public void CanonicalCycleAndRootTravelArePositionAndVelocityContinuous(float phase)
        {
            const float epsilon=.0001f;float2 left=NaturalMotion.CanonicalFoot(phase-epsilon,0),mid=NaturalMotion.CanonicalFoot(phase,0),right=NaturalMotion.CanonicalFoot(phase+epsilon,0);
            Assert.That(math.distance(left,right),Is.LessThan(.0003f));
            Assert.That(math.distance((mid-left)/epsilon,(right-mid)/epsilon),Is.LessThan(.008f));
            if(phase==0||phase==1)Assert.That(math.distance(NaturalMotion.CanonicalFoot(0,0),NaturalMotion.CanonicalFoot(1,0)),Is.LessThan(.000001f));
        }
        [Test] public void SwingClearsGroundAndLandsWithoutVerticalSnap()
        {
            float max=0;for(int i=0;i<=100;i++){float p=NaturalMotion.Stance+(1-NaturalMotion.Stance)*i/100f;max=math.max(max,NaturalMotion.CanonicalFoot(p,0).y);}
            Assert.That(max,Is.EqualTo(.075f+NaturalMotion.SwingHeight).Within(.00001f));
            Assert.That(NaturalMotion.CanonicalFoot(.62f,0).y,Is.EqualTo(.075f).Within(.00001f));
            Assert.That(NaturalMotion.CanonicalFoot(1,0).y,Is.EqualTo(.075f).Within(.00001f));
        }
        [Test] public void SmoothedAimIsFrameRateIndependentForHeldTarget()
        {
            float2 RunAim(int hz){SmoothedAimState s=default;NaturalMotion.SmoothAim(ref s,0,0);for(int i=0;i<hz;i++)NaturalMotion.SmoothAim(ref s,new float2(1,2),1f/hz,3);return s.Target;}
            Assert.That(math.distance(RunAim(30),RunAim(120)),Is.LessThan(.00001f));
            Assert.That(math.distance(RunAim(60),new float2(1,2)*(1-math.exp(-3))),Is.LessThan(.00001f));
        }
        [TestCase(1f,1f)] [TestCase(1f,-1f)] [TestCase(-1f,1f)] [TestCase(-1f,-1f)]
        public void MirroringAndBendAreConsistent(float facing,float bend)
        {
            using var rig=NaturalCharacterRig.Create();using var local=new NativeArray<BoneLocal>(14,Allocator.Temp);using var world=new NativeArray<BoneWorld>(14,Allocator.Temp);
            Skeletal.Sample(rig.View,0,0,local);
            float2 target=new float2(.6f,1.65f);
            NaturalMotion.Aim(rig.View,local,11,12,target*new float2(facing,1),0,facing,1,bend);
            Skeletal.ToWorld(rig.View,local,0,facing,1,world);
            float2 tip=world[12].Transform(new float2(.42f,0),facing);
            Assert.That(math.distance(tip,target*new float2(facing,1)),Is.LessThan(.0003f));
            float2 upper=world[11].Position,elbow=world[12].Position,d=target*new float2(facing,1)-upper,e=elbow-upper;
            float side=d.x*e.y-d.y*e.x;Assert.That(math.sign(side),Is.EqualTo(facing*bend));
        }
        [TestCase(100f,50f)] [TestCase(-100f,-20f)] [TestCase(.05f,1.67f)]
        public void UnreachableAndCollapsedTargetsRemainFiniteAndBounded(float x,float y)
        {
            using var rig=NaturalCharacterRig.Create();using var local=new NativeArray<BoneLocal>(14,Allocator.Temp);using var world=new NativeArray<BoneWorld>(14,Allocator.Temp);
            Skeletal.Sample(rig.View,0,0,local);NaturalMotion.Aim(rig.View,local,11,12,new float2(x,y),0,1,1);Skeletal.ToWorld(rig.View,local,0,1,1,world);
            float2 tip=world[12].Transform(new float2(.42f,0),1);Assert.That(math.all(math.isfinite(tip)),Is.True);
            Assert.That(math.distance(world[11].Position,tip),Is.LessThanOrEqualTo(.8501f));
        }
        [Test] public void PoseLegTipsStayOnTheirPlantedTargets()
        {
            using var rig=NaturalCharacterRig.Create();using var local=new NativeArray<BoneLocal>(14,Allocator.Temp);using var world=new NativeArray<BoneWorld>(14,Allocator.Temp);
            for(int i=0;i<100;i++)
            {
                float2 far=new float2(-.21f,.075f),near=new float2(.21f,.075f);
                NaturalCharacterRig.Pose(rig.View,local,world,0,1,1,i*.013f,far,near,false,0,.2f);
                Assert.That(math.distance(world[7].Position,far),Is.LessThan(.0003f));
                Assert.That(math.distance(world[10].Position,near),Is.LessThan(.0003f));
                Assert.That(math.abs(world[7].Rotation),Is.LessThan(.00001f));
            }
        }
        [Test] public void WarmedPoseAndFootAndAimCallsAllocateNoManagedMemory()
        {
            using var rig=NaturalCharacterRig.Create();using var local=new NativeArray<BoneLocal>(14,Allocator.Temp);using var world=new NativeArray<BoneWorld>(14,Allocator.Temp);
            FootPlantState foot=default;SmoothedAimState aim=default;
            void Run(){NaturalMotion.StepFoot(ref foot,0,0,0,.016f,0);NaturalMotion.SmoothAim(ref aim,new float2(.7f,1.7f),.016f);NaturalCharacterRig.Pose(rig.View,local,world,0,1,1,.2f,new float2(-.2f,.075f),new float2(.2f,.075f),true,aim.Target,0);}
            for(int i=0;i<100;i++)Run();
            Action measured = () => { for(int i=0;i<1000;i++)Run(); };
            using var probe = new ManagedAllocationProbe();
            var calibrationBefore = probe.Calibrate();
            var sample = probe.Measure(measured);
            var calibrationAfter = probe.Calibrate();
            TestContext.WriteLine($"Natural motion pose/foot/aim, 1000 iterations after 100 warm-up iterations: {sample.Value} current-thread {sample.Metric}; independent process-wide gen0 collections={sample.Collections}; retained-array/empty calibration before={calibrationBefore.RetainedArrays.Value}/{calibrationBefore.Empty.Value}, after={calibrationAfter.RetainedArrays.Value}/{calibrationAfter.Empty.Value}.");
            Assert.That(sample.Value, Is.Zero, $"Warmed natural motion calls must allocate zero {sample.Metric}.");
        }
        [TestCase(1.77778f)] [TestCase(.5625f)] [TestCase(2.3333f)]
        public void AspectFitContainsLandscapeEnvelope(float aspect)
        {
            float half=NaturalMotion.FitOrthographic(18,10.2f,aspect);Assert.That(half*2,Is.GreaterThanOrEqualTo(10.1999f));Assert.That(half*2*aspect,Is.GreaterThanOrEqualTo(17.9999f));
        }
        [Test] public void OriginalArtHasAntialiasedEdgesAndDistinctSilhouettes()
        {
            using var art=new NaturalCharacterArt();int partial=0,different=0;
            var hero=art.Canvases[0][2];var monster=art.Canvases[1][2];
            for(int i=0;i<hero.Pixels.Length;i++){if(hero.Pixels[i].a>0&&hero.Pixels[i].a<255)partial++;if(hero.Pixels[i].a!=monster.Pixels[i].a)different++;}
            Assert.That(partial,Is.GreaterThan(80));Assert.That(different,Is.GreaterThan(1000));
            Assert.That(art.Attachments[0].Length,Is.EqualTo(14));
        }
    }
    public class PoseSilhouetteShadowTests
    {
        static PoseShadowSample Sample(float facing=1)
        {
            var canvas=new PixelCanvas(32,48);canvas.Ellipse(16,24,12,22,new Color32(255,255,255,255));
            return new PoseShadowSample{Kind=0,Facing=facing,Bones=new[]{new BoneWorld{Position=new float2(0,1),Rotation=facing<0?math.PI:0}},
                Attachments=new[]{new BoneAttachment{Bone=0,Size=new float2(.8f,2),Tint=1}},Parts=new[]{canvas}};
        }
        [Test] public void BoundsCoverageAndAtlasBudgetAreExplicit()
        {
            using var atlas=new PoseSilhouetteShadow(new[]{Sample()});var frame=PoseSilhouetteShadow.BakeFrame(Sample(),out var mask);var pixels=mask.Pixels;int nonzero=0;
            for(int y=0;y<PoseSilhouetteShadow.TileHeight;y++)for(int x=0;x<PoseSilhouetteShadow.TileWidth;x++)
            {byte a=pixels[y*mask.Width+x].a;if(a>0)nonzero++;if(x==0||y==0||x==95||y==63)Assert.That(a,Is.Zero);}
            Assert.That(nonzero,Is.InRange(800,5700));Assert.That(frame.Size.x,Is.GreaterThan(1.64f));Assert.That(frame.Size.y,Is.GreaterThan(.48f));
            Assert.That(atlas.TextureBytes,Is.LessThanOrEqualTo(PoseSilhouetteShadow.MaxAtlasBytes));
            for(int i=0;i<50;i++){float2 p=new float2(i*.03f,i*.04f);Assert.That(math.distance(p,PoseSilhouetteShadow.Unproject(PoseSilhouetteShadow.Project(p))),Is.LessThan(.000001f));}
        }
        [Test] public void MaximumAtlasStaysAtOneMebibyte()
        {
            var s=new PoseShadowSample[32];for(int i=0;i<s.Length;i++)s[i]=Sample();using var atlas=new PoseSilhouetteShadow(s);
            Assert.That(atlas.TextureBytes,Is.EqualTo(1024*1024));Assert.Throws<ArgumentOutOfRangeException>(()=>new PoseSilhouetteShadow(new PoseShadowSample[33]));
        }
        [TestCase(1f)] [TestCase(-1f)]
        public void QualityAndPoseEnvelopeForceHonestFallback(float facing)
        {
            var sample=Sample(facing);using var atlas=new PoseSilhouetteShadow(new[]{sample});using var world=new NativeArray<BoneWorld>(sample.Bones,Allocator.Temp);
            Assert.That(atlas.TrySelect(PoseShadowQuality.BakedPose,0,world,0,0,facing,1,false,out _),Is.True);
            Assert.That(atlas.TrySelect(PoseShadowQuality.BakedPose,0,world,0,0,facing,1,true,out _),Is.False);
            Assert.That(atlas.TrySelect(PoseShadowQuality.Blob,0,world,0,0,facing,1,false,out _),Is.False);
            Assert.That(atlas.TrySelect(PoseShadowQuality.None,0,world,0,0,facing,1,false,out _),Is.False);
            Assert.That(atlas.TrySelect(PoseShadowQuality.BakedPose,0,world,0,0,-facing,1,false,out _),Is.False);
            var b=world[0];b.Position.x+=.13f;var writable=world;writable[0]=b;
            Assert.That(atlas.TrySelect(PoseShadowQuality.BakedPose,0,world,0,0,facing,1,false,out _),Is.False);
        }
        [Test] public void InvalidBakeGeometryAndNonfiniteSelectionAreRejected()
        {
            var bad=Sample();bad.Attachments[0]=new BoneAttachment{Bone=0,Size=new float2(0,1)};
            Assert.Throws<ArgumentException>(()=>PoseSilhouetteShadow.BakeFrame(bad,out _));
            var sample=Sample();using var atlas=new PoseSilhouetteShadow(new[]{sample});using var world=new NativeArray<BoneWorld>(sample.Bones,Allocator.Temp);
            Assert.That(atlas.TrySelect(PoseShadowQuality.BakedPose,0,world,0,0,1,float.NaN,false,out _),Is.False);
            var writable=world;writable[0]=new BoneWorld{Position=new float2(float.NaN,1)};
            Assert.That(atlas.TrySelect(PoseShadowQuality.BakedPose,0,world,0,0,1,1,false,out _),Is.False);
        }
        [Test] public void SelectionFreezesSourceGeometryAndAllocatesNothing()
        {
            var s=Sample();using var atlas=new PoseSilhouetteShadow(new[]{s});using var world=new NativeArray<BoneWorld>(s.Bones,Allocator.Temp);
            s.Bones[0]=default;s.Attachments[0]=default;
            for(int i=0;i<10;i++)atlas.TrySelect(PoseShadowQuality.BakedPose,0,world,0,0,1,1,false,out _);
            bool found=false;
            Action measured = () =>
            {
                for(int i=0;i<1000;i++)found=atlas.TrySelect(PoseShadowQuality.BakedPose,0,world,0,0,1,1,false,out _);
            };
            using var probe = new ManagedAllocationProbe();
            var calibrationBefore = probe.Calibrate();
            var sample = probe.Measure(measured);
            var calibrationAfter = probe.Calibrate();
            TestContext.WriteLine($"Pose shadow selection, 1000 calls after 10 warm-up calls: {sample.Value} current-thread {sample.Metric}; independent process-wide gen0 collections={sample.Collections}; retained-array/empty calibration before={calibrationBefore.RetainedArrays.Value}/{calibrationBefore.Empty.Value}, after={calibrationAfter.RetainedArrays.Value}/{calibrationAfter.Empty.Value}.");
            Assert.That(found, Is.True);
            Assert.That(sample.Value, Is.Zero, $"Warmed pose shadow selection must allocate zero {sample.Metric}.");
        }
    }
}
