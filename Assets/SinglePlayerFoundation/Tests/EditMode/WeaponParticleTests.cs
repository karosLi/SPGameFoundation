using System;
using System.Runtime.InteropServices;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Contracts.Weapons;
using SPF.Presentation;
using SPF.Presentation.Particles;
using Unity.Mathematics;
using UnityEngine;

namespace SPF.Tests.EditMode
{
    public class WeaponParticleTests
    {
        static readonly float4 View=new float4(-8,-5,8,5);
        static ParticleState P(ParticlePriority priority=ParticlePriority.Decorative,float life=1.5f) => WeaponParticlePresenter.Make(float2.zero,new float2(.8f,1.2f),life,new float2(.08f,.025f),.1f,new float4(.6f,.8f,1,1),0,ParticleShape.Streak,priority);
        [Test]
        public void CpuGpuAbiAndIndependentPhysicsReference()
        {
            Assert.That(Marshal.SizeOf<ParticleState>(),Is.EqualTo(96));Assert.That(Marshal.SizeOf<ParticleSpawn>(),Is.EqualTo(112));Assert.That(Marshal.SizeOf<ParticleSocket>(),Is.EqualTo(32));
            Assert.That(Marshal.OffsetOf<ParticleState>(nameof(ParticleState.Attachment)).ToInt32(),Is.EqualTo(80));
            var p=P();p.VelocityDrag.z=2;p.VelocityDrag.w=-3;p.Shape.w=.7f;
            var actual=ParticleMath.Step(p,default,.02f);
            Assert.That(actual.VelocityDrag.x,Is.EqualTo(.8f/1.04f).Within(1e-6f));
            Assert.That(actual.PositionAge.y,Is.EqualTo((1.2f-.06f)/1.04f*.02f).Within(1e-6f));
            Assert.That(actual.Shape.z,Is.EqualTo(.114f).Within(1e-6f));
            actual.PositionAge.z=1.49f;actual=ParticleMath.Step(actual,default,.02f);Assert.That(actual.Alive,Is.False);
        }
        [Test]
        public void SeededVisualRandomAndCurvesAreRepeatableWithoutGameplayState()
        {
            uint a=19,b=19;for(int i=0;i<4096;i++){float x=ParticleMath.Random01(ref a);Assert.That(x,Is.EqualTo(ParticleMath.Random01(ref b)));Assert.That(x,Is.InRange(0,1f));}
            var p=P();p.PositionAge.z=.2f;var packed=ParticleMath.Sprite(p,default);
            Assert.That(packed.Color.w,Is.GreaterThan(0));Assert.That(packed.Color.w,Is.LessThan(1));
            p.PositionAge.z=p.PositionAge.w;Assert.That(ParticleMath.Sprite(p,default).Size,Is.EqualTo(float2.zero));
        }
        [TestCase(false)][TestCase(true)]
        public void AdmissionReservesHeroCapacityAndNeverDuplicatesScatterTargets(bool low)
        {
            using var pool=new ParticlePool(low);
            for(int frame=0;frame<40;frame++)
            {
                pool.BeginFrame(0,View);for(int i=0;i<80;i++)pool.Spawn(P());pool.SimulateCpu();
                Assert.That(pool.SpawnCount,Is.LessThanOrEqualTo(48));AssertUnique(pool);
            }
            Assert.That(pool.ReservedCount,Is.EqualTo(pool.Capacity-64));
            pool.BeginFrame(.01f,View);
            for(int i=0;i<64;i++)Assert.That(pool.Spawn(P(ParticlePriority.Hero)),Is.True);
            Assert.That(pool.ReservedCount,Is.EqualTo(pool.Capacity));Assert.That(pool.SpawnCount,Is.EqualTo(64));AssertUnique(pool);
            Assert.That(pool.Spawn(P(ParticlePriority.Decorative)),Is.False);
            pool.BeginFrame(.01f,View);Assert.That(pool.Spawn(P(ParticlePriority.Hero)),Is.True);Assert.That(pool.Replaced,Is.GreaterThan(0));
            Assert.That(pool.ReservedCount,Is.EqualTo(pool.Capacity));Assert.That(pool.CoverageFraction,Is.LessThanOrEqualTo(pool.CoverageLimit+.000001f));
        }
        [Test]
        public void OverflowReplacesLowPriorityStagedCommandsWithoutScatterRace()
        {
            using var pool=new ParticlePool();pool.BeginFrame(.02f,View);
            for(int i=0;i<48;i++)pool.Spawn(P());for(int i=0;i<16;i++)pool.Spawn(P(ParticlePriority.Release));
            Assert.That(pool.Spawn(P(ParticlePriority.Hero)),Is.True);Assert.That(pool.SpawnCount,Is.EqualTo(64));AssertUnique(pool);
            pool.SimulateCpu();int live=0;for(int i=0;i<pool.Capacity;i++)if(pool.CpuStates[i].Alive)live++;
            Assert.That(live,Is.EqualTo(pool.ReservedCount));
        }
        [Test]
        public void CoverageAndZoomBoundAreConservativeAndInvalidInputCannotPoisonPool()
        {
            using var pool=new ParticlePool();pool.BeginFrame(.02f,new float4(-1,-1,1,1));
            var p=P();p.Shape.xy=new float2(.1f);
            for(int frame=0;frame<10;frame++){pool.BeginFrame(0,new float4(-1,-1,1,1));for(int i=0;i<64;i++)pool.Spawn(p);pool.SimulateCpu();}
            Assert.That(pool.CoverageFraction,Is.LessThanOrEqualTo(pool.CoverageLimit));
            pool.BeginFrame(.02f,new float4(-.2f,-.2f,.2f,.2f));Assert.That(pool.DrawScale,Is.LessThan(1));Assert.That(pool.CoverageFraction,Is.LessThanOrEqualTo(pool.CoverageLimit+.000001f));
            p.PositionAge.x=float.NaN;Assert.That(pool.Spawn(p),Is.False);p=P();p.Shape.x=100;Assert.That(pool.Spawn(p),Is.False);
            pool.BeginFrame(float.NaN,View);Assert.That(pool.DeltaTime,Is.Zero);
            pool.BeginFrame(10,View);Assert.That(pool.DeltaTime,Is.EqualTo(ParticleLimits.MaxDeltaTime));
        }
        [Test]
        public void AttachedParticlesFollowSocketAndDieOnGenerationChangeWhileWorldBurstDetaches()
        {
            using var pool=new ParticlePool();pool.BeginFrame(.02f,View);
            pool.SetSocket(0,new ParticleSocket{Pose=new float4(1,2,0,1),Identity=new uint4(4,1,0,0)});
            var p=P();p.PositionAge.xy=new float2(.2f,0);p.VelocityDrag=default;p.Attachment.x=1;p.Attachment.y=4;
            Assert.That(pool.Spawn(p),Is.True);Assert.That(pool.Spawn(P(ParticlePriority.Impact)),Is.True);pool.SimulateCpu();
            Assert.That(ParticleMath.WorldPosition(pool.CpuStates[0],pool.Sockets[0]),Is.EqualTo(new float2(1,2.2f)));
            pool.BeginFrame(.02f,View);pool.SetSocket(0,new ParticleSocket{Pose=new float4(4,1,1,0),Identity=new uint4(5,1,0,0)});pool.SimulateCpu();
            Assert.That(pool.CpuStates[0].Alive,Is.False);Assert.That(pool.CpuStates[1].Alive,Is.True);Assert.That(pool.ReservedCount,Is.EqualTo(1));
        }
        [TestCase("compute")][TestCase("kernel")][TestCase("format")][TestCase("api")][TestCase("buffer")][TestCase("shader")][TestCase("threads")]
        public void EveryMissingCapabilitySelectsHonestCpuFallback(string missing)
        {
            var c=Supported();Assert.That(c.Select(false,1024),Is.EqualTo(ParticleBackend.GpuCompute));
            switch(missing){case "compute":c.Compute=false;break;case "kernel":c.Kernels=false;break;case "format":c.AtlasFormat=false;break;case "api":c.SupportedApi=false;break;case "buffer":c.MaxBufferBytes=1024*96-1;break;case "shader":c.Shader=false;break;case "threads":c.GroupSize=32;break;}
            Assert.That(c.Select(false,1024),Is.EqualTo(ParticleBackend.CpuBurst));Assert.That(Supported().Select(true,1024),Is.EqualTo(ParticleBackend.CpuBurst));
        }
        [TestCase("shader")][TestCase("instancing")][TestCase("vertexBuffers")][TestCase("shaderLevel")][TestCase("buffer")][TestCase("api")]
        public void UnusableGpuDrawSelectsDataTextureWhileComputeFailurePreservesHealthyDraw(string missing)
        {
            var c=Supported();
            Assert.That(c.SelectRenderTier(RenderTier.GpuDriven,1024),Is.EqualTo(RenderTier.GpuDriven));
            Assert.That(c.SelectRenderTier(RenderTier.DataTexture,1024),Is.EqualTo(RenderTier.DataTexture));
            c.Compute=false;c.Kernels=false;c.ComputeBuffers=0;c.GroupSize=0;
            Assert.That(c.Select(false,1024),Is.EqualTo(ParticleBackend.CpuBurst));
            Assert.That(c.SelectRenderTier(RenderTier.GpuDriven,1024),Is.EqualTo(RenderTier.GpuDriven));
            switch(missing)
            {
                case "shader":c.Shader=false;break;case "instancing":c.Instancing=false;break;
                case "vertexBuffers":c.VertexBuffers=0;break;case "shaderLevel":c.ShaderLevel=30;break;
                case "buffer":c.MaxBufferBytes=1024*32-1;break;case "api":c.SupportedApi=false;break;
            }
            Assert.That(c.SelectRenderTier(RenderTier.GpuDriven,1024),Is.EqualTo(RenderTier.DataTexture));
        }
        [Test]
        public void PresenterDeduplicatesConfirmedCuesAndResetsMissingRecycledAndSwappedOwners()
        {
            using var view=new WeaponParticlePresenter(RenderTier.DataTexture,true,true);
            var owner=new EntityHandle(3,1);var state=new WeaponViewState{ContentId=1,VisualId=1,Family=WeaponActionFamily.Cast,Stage=WeaponStage.Windup,ReleasePhase=.6f,ActionPulse=1};
            for(int f=0;f<6;f++){view.BeginFrame(.05f,View);view.UpdateEmitter(owner,state,new float2(.3f,0),float2.zero,new float2(1,0));view.EndFrame(new Bounds(Vector3.zero,Vector3.one*10));}
            Assert.That(view.Renderer.Pool.ReservedCount,Is.GreaterThan(0));
            view.BeginFrame(.02f,View);view.EndFrame(new Bounds());Assert.That(view.Renderer.Pool.ReservedCount,Is.Zero,"Missing owner retires attached charge.");
            view.BeginFrame(.02f,View);view.UpdateEmitter(new EntityHandle(3,2),state,float2.zero,float2.zero,new float2(1,0));
            var cue=new WeaponCue{Owner=new EntityHandle(3,2),Sequence=7,ActionPulse=1,Kind=WeaponCueKind.Impact,Position=new float2(.5f,0),Direction=new float2(1,0)};
            view.SubmitCue(cue);int count=view.Renderer.Pool.SpawnCount;Assert.That(count,Is.GreaterThan(0));view.SubmitCue(cue);Assert.That(view.Renderer.Pool.SpawnCount,Is.EqualTo(count));
            view.Clear();Assert.That(view.Renderer.Pool.ReservedCount,Is.Zero);view.SubmitCue(cue);Assert.That(view.Renderer.Pool.SpawnCount,Is.GreaterThan(0));
        }
        static WeaponCue Impact(EntityHandle owner,uint sequence) => new WeaponCue{Owner=owner,Sequence=sequence,Kind=WeaponCueKind.Impact,Direction=new float2(1,0)};
        [TestCase(WeaponActionFamily.Cast)][TestCase(WeaponActionFamily.Draw)]
        public void ChargePersistsPastContactUntilAuthoritativeReleaseAndResetsOnCancelOrEquip(WeaponActionFamily family)
        {
            // The valid authored profile has Duration=60, Active=[20,40), Release=30.
            // Exercise its public view contract so presentation tests do not depend on gameplay.
            const int duration=60,contact=20,activeEnd=40,release=30;
            using var view=new WeaponParticlePresenter(RenderTier.DataTexture,true,true);
            var owner=new EntityHandle(3,1);
            var state=new WeaponViewState{ContentId=1,VisualId=1,Family=family,ActionPulse=1,ContactPhase=(float)contact/duration,ActiveEndPhase=(float)activeEnd/duration,ReleasePhase=(float)release/duration};
            foreach(var stop in new[]{WeaponCueKind.Release,WeaponCueKind.Cancel,WeaponCueKind.Equip})
            {
                view.Clear();
                for(int tick=16;tick<release;tick++)
                {
                    state.Stage=tick<contact?WeaponStage.Windup:WeaponStage.Active;state.Phase=(float)tick/duration;
                    view.BeginFrame(1f/duration,View);view.UpdateEmitter(owner,state,float2.zero,float2.zero,new float2(1,0));view.EndFrame(new Bounds());
                    if(tick==contact-1 || tick==release-1)Assert.That(AttachedCount(view),Is.GreaterThan(0),"Charge must remain visible through the pre-release Active subphase.");
                }
                view.BeginFrame(1f/duration,View);
                if(stop==WeaponCueKind.Release)state.Phase=(float)release/duration;
                view.UpdateEmitter(owner,state,float2.zero,float2.zero,new float2(1,0));
                if(stop!=WeaponCueKind.Release){var cue=Impact(owner,1);cue.Kind=stop;cue.ActionPulse=state.ActionPulse;cue.ContentId=state.ContentId;view.SubmitCue(cue);}
                view.EndFrame(new Bounds());Assert.That(AttachedCount(view),Is.Zero,"Authoritative release, cancel and equip must retire attached charge.");
            }
        }
        static int AttachedCount(WeaponParticlePresenter view)
        {
            int count=0;var states=view.Renderer.Pool.CpuStates;
            for(int i=0;i<states.Length;i++)if(states[i].Alive && states[i].Attachment.x!=0)count++;
            return count;
        }
        [Test]
        public void CueSequencesAreIndependentPerOwnerAndSurviveEmitterResets()
        {
            using var view=new WeaponParticlePresenter(RenderTier.DataTexture,true,true);
            var a=new EntityHandle(3,1);var b=new EntityHandle(4,1);
            view.BeginFrame(.02f,View);view.SubmitCue(Impact(a,1));int burst=view.Renderer.Pool.SpawnCount;
            Assert.That(burst,Is.GreaterThan(0));view.SubmitCue(Impact(b,1));Assert.That(view.Renderer.Pool.SpawnCount,Is.EqualTo(burst*2));
            view.SubmitCue(Impact(a,1));view.SubmitCue(Impact(b,1));Assert.That(view.Renderer.Pool.SpawnCount,Is.EqualTo(burst*2));
            foreach(var reset in new[]{WeaponCueKind.Cancel,WeaponCueKind.Equip})
            {
                view.BeginFrame(.02f,View);
                uint sequence=reset==WeaponCueKind.Cancel?2u:4u;
                var cue=Impact(a,sequence);cue.Kind=reset;view.SubmitCue(cue);view.ResetOwner(a);
                view.SubmitCue(Impact(a,sequence));view.SubmitCue(Impact(a,1));Assert.That(view.Renderer.Pool.SpawnCount,Is.Zero);
                view.SubmitCue(Impact(a,sequence+1));Assert.That(view.Renderer.Pool.SpawnCount,Is.EqualTo(burst));
            }
        }
        [Test]
        public void CueDedupHandlesSequenceWrapRecycledOwnersAndExplicitClear()
        {
            using var view=new WeaponParticlePresenter(RenderTier.DataTexture,true,true);
            var owner=new EntityHandle(3,1);view.BeginFrame(.02f,View);
            view.SubmitCue(Impact(owner,uint.MaxValue));int burst=view.Renderer.Pool.SpawnCount;
            view.BeginFrame(.02f,View);view.SubmitCue(Impact(owner,1));Assert.That(view.Renderer.Pool.SpawnCount,Is.EqualTo(burst));
            view.SubmitCue(Impact(owner,uint.MaxValue));view.SubmitCue(Impact(owner,1));Assert.That(view.Renderer.Pool.SpawnCount,Is.EqualTo(burst));
            view.BeginFrame(.02f,View);var recycled=new EntityHandle(3,2);view.SubmitCue(Impact(recycled,1));Assert.That(view.Renderer.Pool.SpawnCount,Is.EqualTo(burst));
            view.SubmitCue(Impact(owner,2));view.SubmitCue(Impact(recycled,1));Assert.That(view.Renderer.Pool.SpawnCount,Is.EqualTo(burst));
            view.Clear();view.BeginFrame(.02f,View);view.SubmitCue(Impact(recycled,1));Assert.That(view.Renderer.Pool.SpawnCount,Is.EqualTo(burst));
            view.BeginFrame(.02f,View);view.SubmitCue(Impact(recycled,0));view.SubmitCue(Impact(EntityHandle.Null,1));Assert.That(view.Renderer.Pool.SpawnCount,Is.Zero);
            view.Clear();view.BeginFrame(.02f,View);view.SubmitCue(Impact(new EntityHandle(3,int.MaxValue),1));
            view.BeginFrame(.02f,View);view.SubmitCue(Impact(new EntityHandle(3,int.MinValue),1));Assert.That(view.Renderer.Pool.SpawnCount,Is.EqualTo(burst));
            view.SubmitCue(Impact(new EntityHandle(3,int.MaxValue),2));Assert.That(view.Renderer.Pool.SpawnCount,Is.EqualTo(burst),"Signed generation wrap must still reject the previous owner.");
        }
        [Test]
        public void CueCursorCapacityRejectsUntrackedOwnersWithoutEvictingDedupHistory()
        {
            using var view=new WeaponParticlePresenter(RenderTier.DataTexture,true,true);view.BeginFrame(.02f,View);
            for(int i=0;i<ParticleLimits.CueOwners;i++){var cue=Impact(new EntityHandle(i,1),1);cue.Kind=WeaponCueKind.Begin;view.SubmitCue(cue);}
            view.SubmitCue(Impact(new EntityHandle(ParticleLimits.CueOwners,1),1));Assert.That(view.DroppedCueOwners,Is.EqualTo(1));
            view.SubmitCue(Impact(new EntityHandle(0,1),1));Assert.That(view.Renderer.Pool.SpawnCount,Is.Zero,"Capacity pressure must not permit duplicate replay.");
            view.SubmitCue(Impact(new EntityHandle(0,2),1));Assert.That(view.Renderer.Pool.SpawnCount,Is.GreaterThan(0),"A recycled index reuses its bounded cursor.");
            view.Clear();view.BeginFrame(.02f,View);view.SubmitCue(Impact(new EntityHandle(ParticleLimits.CueOwners,1),1));
            Assert.That(view.DroppedCueOwners,Is.Zero);Assert.That(view.Renderer.Pool.SpawnCount,Is.GreaterThan(0));
        }
        [TestCase(false)][TestCase(true)]
        public void MixedSpawnRetirementAndCameraChangesNeverUnderestimateLiveQuads(bool low)
        {
            using var pool=new ParticlePool(low);uint seed=8145;
            for(int frame=0;frame<350;frame++)
            {
                float extent=frame%40<20?4:1.3f;var view=new float4(-extent,-extent,extent,extent);
                pool.BeginFrame(.005f+.045f*ParticleMath.Random01(ref seed),view);
                uint generation=(uint)(1+frame/17);
                pool.SetSocket(0,new ParticleSocket{Pose=new float4(.1f,0,1,0),Identity=new uint4(generation,1,0,0)});
                for(int spawn=0;spawn<80;spawn++)
                {
                    var p=P((ParticlePriority)(spawn%5),.02f+1.4f*ParticleMath.Random01(ref seed));
                    p.PositionAge.xy=new float2((ParticleMath.Random01(ref seed)-.5f)*extent,(ParticleMath.Random01(ref seed)-.5f)*extent);
                    p.Shape.xy=new float2(.03f+.10f*ParticleMath.Random01(ref seed),.02f+.10f*ParticleMath.Random01(ref seed));
                    if(spawn%3==0){p.Attachment.x=1;p.Attachment.y=generation;}
                    pool.Spawn(p);
                }
                AssertUnique(pool);pool.SimulateCpu();int alive=0;float area=0;
                for(int i=0;i<pool.Capacity;i++)
                {
                    var p=pool.CpuStates[i];if(!p.Alive)continue;alive++;
                    float scale=math.max(1,p.Visual.z)*pool.DrawScale;area+=p.Shape.x*p.Shape.y*scale*scale;
                }
                Assert.That(alive,Is.LessThanOrEqualTo(pool.ReservedCount));
                Assert.That(area/(4*extent*extent),Is.LessThanOrEqualTo(pool.CoverageLimit+.00002f));
                Assert.That(pool.SpawnCount,Is.LessThanOrEqualTo(64));
            }
        }
        static ParticleCapabilities Supported()=>new ParticleCapabilities{Compute=true,Kernels=true,Graphics=true,SupportedApi=true,Shader=true,Instancing=true,AtlasFormat=true,ShaderLevel=45,ComputeBuffers=4,VertexBuffers=1,GroupSize=64,MaxBufferBytes=1024*96};
        static void AssertUnique(ParticlePool pool){for(int i=0;i<pool.SpawnCount;i++)for(int j=i+1;j<pool.SpawnCount;j++)Assert.That(pool.Spawns[i].Target.x,Is.Not.EqualTo(pool.Spawns[j].Target.x));}
    }
}
