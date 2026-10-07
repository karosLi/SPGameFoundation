#if !SPF_DOTNET_HARNESS
using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Contracts.Weapons;
using SPF.Presentation;
using SPF.Presentation.Particles;
using SPF.Testing;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace SPF.Particles.Tests.PlayMode
{
    public class WeaponParticleGraphicsTests
    {
        static readonly float4 View=new float4(-4,-4,4,4);
        static readonly Bounds Bounds=new Bounds(Vector3.zero,new Vector3(8,8,10));
        static void RequireGraphics(){if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null)Assert.Ignore("Actual particle graphics unverified: no graphics device.");}
        static void RequireCompute(ParticleRenderer renderer)
        {
            Assert.That(Resources.Load<ComputeShader>("SPF/Particles/WeaponParticles"),Is.Not.Null,"Production compute resource must ship.");
            if(renderer.Backend!=ParticleBackend.GpuCompute)Assert.Ignore("Actual compute particles unverified: capability gate selected "+renderer.Backend+" on "+SystemInfo.graphicsDeviceName);
        }
        static ParticleState Particle(int index,int frame)
        {
            uint seed=ParticleMath.Hash((uint)(index+frame*64));
            var p=WeaponParticlePresenter.Make(new float2((index%8-3.5f)*.65f,(index/8-3.5f)*.65f),new float2(ParticleMath.Random01(ref seed)-.5f,ParticleMath.Random01(ref seed)),.3f+ParticleMath.Random01(ref seed)*.4f,
                new float2(.16f,.075f),index*.2f,new float4(.3f+.5f*ParticleMath.Random01(ref seed),.6f,1,.8f),0,(ParticleShape)(index%4),ParticlePriority.Hero);
            p.VelocityDrag.z=index%4;p.VelocityDrag.w=-.2f*index;p.Shape.w=.3f;
            if(index%5==0){p.Attachment.x=1;p.Attachment.y=7;p.PositionAge.xy*=.2f;}
            return p;
        }
        static void Fill(ParticleRenderer renderer,int frame,int count=64)
        {
            renderer.Pool.BeginFrame(.02f,View);
            renderer.Pool.SetSocket(0,new ParticleSocket{Pose=new float4(math.sin(frame*.1f),.1f*frame,1,0),Identity=new uint4(7,1,0,0)});
            for(int i=0;i<count;i++)renderer.Pool.Spawn(Particle(i,frame));
            renderer.Simulate();
        }
        [Test]
        public void ActualComputeStateMatchesCpuForSpawnMotionDragGravityLifetimeAndSockets()
        {
            RequireGraphics();using var gpu=new ParticleRenderer(RenderTier.GpuDriven);RequireCompute(gpu);
            using var cpu=new ParticleRenderer(RenderTier.GpuDriven,forceCpu:true);var read=new ParticleState[gpu.Pool.Capacity];float maximum=0;
            for(int frame=0;frame<80;frame++)
            {
                gpu.Pool.BeginFrame(.02f,View);cpu.Pool.BeginFrame(.02f,View);
                var socket=new ParticleSocket{Pose=new float4(math.sin(frame*.1f),.02f*frame,math.cos(frame*.05f),math.sin(frame*.05f)),Identity=new uint4(frame<40?7u:8u,1,0,0)};
                gpu.Pool.SetSocket(0,socket);cpu.Pool.SetSocket(0,socket);
                if(frame<8)for(int i=0;i<64;i++){var p=Particle(i,frame);Assert.That(gpu.Pool.Spawn(p),Is.EqualTo(cpu.Pool.Spawn(p)));}
                gpu.Simulate();cpu.Simulate();gpu.ReadbackForValidation(read);
                for(int i=0;i<gpu.Pool.Capacity;i++)
                {
                    var a=read[i];var b=cpu.Pool.CpuStates[i];Assert.That(a.Alive,Is.EqualTo(b.Alive));
                    maximum=math.max(maximum,math.cmax(math.abs(a.PositionAge-b.PositionAge)));maximum=math.max(maximum,math.cmax(math.abs(a.VelocityDrag-b.VelocityDrag)));
                    maximum=math.max(maximum,math.cmax(math.abs(a.Shape-b.Shape)));maximum=math.max(maximum,math.cmax(math.abs(a.Color-b.Color)));
                    maximum=math.max(maximum,math.cmax(math.abs(a.Visual-b.Visual)));Assert.That(a.Attachment,Is.EqualTo(b.Attachment));
                    Assert.That(maximum,Is.LessThan(1e-4f));
                }
                Assert.That(gpu.BytesUploaded,Is.LessThanOrEqualTo(64*112+32*32));Assert.That(gpu.DispatchCalls,Is.InRange(1,2));
            }
            Assert.That(gpu.Pool.ReservedCount,Is.Zero);Debug.Log("Actual GPU particle state parity maximum coefficient error="+maximum+"; device="+SystemInfo.graphicsDeviceName);
        }
        [Test]
        public void MissingComputeAndWrongKernelsFallbackAndSeparateBatchesKeepTheirResources()
        {
            RequireGraphics();using var missing=new ParticleRenderer(RenderTier.GpuDriven,disableComputeAsset:true);
            using var forced=new ParticleRenderer(RenderTier.GpuDriven,forceCpu:true);
            using var lower=new ParticleRenderer(RenderTier.DataTexture);
            Assert.That(missing.Backend,Is.EqualTo(ParticleBackend.CpuBurst));Assert.That(forced.Backend,Is.EqualTo(ParticleBackend.CpuBurst));Assert.That(lower.Backend,Is.EqualTo(ParticleBackend.CpuBurst));
            var capabilities=ParticleRenderer.Capabilities(Resources.Load<ComputeShader>("SPF/PointCloud"),Resources.Load<Shader>("SPF/SpriteGPU"));
            Assert.That(capabilities.Kernels,Is.False);Assert.That(capabilities.Select(false,256),Is.EqualTo(ParticleBackend.CpuBurst));
            using var first=new ParticleRenderer(RenderTier.GpuDriven);RequireCompute(first);using var second=new ParticleRenderer(RenderTier.GpuDriven,true);RequireCompute(second);
            var a=new ParticleState[first.Pool.Capacity];var b=new ParticleState[second.Pool.Capacity];
            for(int f=0;f<4;f++){Fill(first,f,17);Fill(second,f+20,11);first.ReadbackForValidation(a);second.ReadbackForValidation(b);Assert.That(a[0].PositionAge.w,Is.Not.EqualTo(b[0].PositionAge.w));}
            first.Clear();first.ReadbackForValidation(a);foreach(var p in a)Assert.That(p.Alive,Is.False);
            first.Dispose();first.Dispose();Assert.That(first.SimulationBufferBytes,Is.Zero);Assert.Throws<ObjectDisposedException>(()=>first.Simulate());
            Fill(second,32,7);second.ReadbackForValidation(b);Assert.That(b[0].Alive,Is.True);
            using var recreate=new ParticleRenderer(RenderTier.GpuDriven);RequireCompute(recreate);Fill(recreate,2);Assert.That(recreate.Pool.ReservedCount,Is.GreaterThan(0));
        }
        [UnityTest]
        public IEnumerator ProductionDrawMatchesCpuPackedSpritesAndDataTextureFallback([Values(false,true)]bool dataTexture)
            => ProductionDrawParity(dataTexture,false);
        [UnityTest]
        public IEnumerator MissingGpuShaderStillDrawsThroughDataTextureFallback()
            => ProductionDrawParity(false,true);
        IEnumerator ProductionDrawParity(bool dataTexture,bool missingGpuShader)
        {
            RequireGraphics();using var gpu=new ParticleRenderer(RenderTier.GpuDriven);RequireCompute(gpu);
            using var cpu=new ParticleRenderer(dataTexture?RenderTier.DataTexture:RenderTier.GpuDriven,forceCpu:true,disableGpuShader:missingGpuShader);
            Assert.That(cpu.Backend,Is.EqualTo(ParticleBackend.CpuBurst));
            Assert.That(cpu.Tier,Is.EqualTo(dataTexture||missingGpuShader?RenderTier.DataTexture:RenderTier.GpuDriven));
            var go=new GameObject("Particle production parity camera");var camera=go.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=4;
            camera.transform.position=new Vector3(0,0,-10);camera.cullingMask=1<<30;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;
            var target=new RenderTexture(512,512,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear){antiAliasing=1};target.Create();camera.targetTexture=target;
            var read=new Texture2D(512,512,TextureFormat.RGBA32,false,true);
            try
            {
                for(int frame=0;frame<4;frame++){Fill(gpu,frame);Fill(cpu,frame);}
                yield return null;gpu.Draw(Bounds,30);camera.Render();Read(target,read);var pixels=read.GetPixels32();Save(read,"particles-production-gpu.png");
                yield return null;cpu.Draw(Bounds,30);camera.Render();Read(target,read);var expected=read.GetPixels32();Save(read,missingGpuShader?"particles-production-shader-fallback.png":dataTexture?"particles-production-cpu-texture.png":"particles-production-cpu-buffer.png");
                int occupied=0,union=0,mismatch=0,magenta=0;
                for(int i=0;i<pixels.Length;i++)
                {
                    var a=pixels[i];var b=expected[i];if(a.a>2)occupied++;if(a.a>2||b.a>2)union++;
                    if(Math.Abs(a.r-b.r)>8 || Math.Abs(a.g-b.g)>8 || Math.Abs(a.b-b.b)>8 || Math.Abs(a.a-b.a)>8)mismatch++;
                    if(a.r>240 && a.b>240 && a.g<15)magenta++;
                }
                Assert.That(occupied,Is.GreaterThan(150));Assert.That(magenta,Is.Zero);Assert.That(mismatch,Is.LessThanOrEqualTo(math.max(12,union*.025f)));
                Assert.That(gpu.DrawCalls,Is.EqualTo(1));Assert.That(cpu.DrawCalls,Is.EqualTo(1));
                yield return null;gpu.Clear();gpu.Draw(Bounds,30);camera.Render();Read(target,read);foreach(var p in read.GetPixels32())Assert.That(p.a,Is.Zero);
                Debug.Log("Actual particle production draw parity: "+occupied+" occupied pixels, "+mismatch+" mismatches (>8/255 tolerance); CPU render tier="+cpu.Tier);
            }
            finally{camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(read);target.Release();UnityEngine.Object.DestroyImmediate(target);}
        }
        [TestCase(false)][TestCase(true)]
        public void WarmedSimulationAndDrawHaveCalibratedZeroCurrentThreadManagedAllocation(bool forceCpu)
        {
            RequireGraphics();using var renderer=new ParticleRenderer(RenderTier.GpuDriven,forceCpu:forceCpu);if(!forceCpu)RequireCompute(renderer);
            for(int frame=0;frame<64;frame++){Fill(renderer,frame,8);renderer.Draw(Bounds);}
            Action work=()=>{for(int frame=0;frame<64;frame++){Fill(renderer,frame,8);renderer.Draw(Bounds);}};
            using var probe=new ManagedAllocationProbe();var before=probe.Calibrate();var sample=probe.Measure(work);var after=probe.Calibrate();
            TestContext.WriteLine($"Particle {renderer.Backend}: 64 warmed simulation/draw submissions, {sample.Value} current-thread {sample.Metric}; process gen0={sample.Collections}; retained/empty controls={before.RetainedArrays.Value}/{before.Empty.Value}, {after.RetainedArrays.Value}/{after.Empty.Value}. Excludes driver/native/other threads/frame rendering. No mobile timing claim.");
            Assert.That(sample.Value,Is.Zero);
        }
        static void Read(RenderTexture target,Texture2D read){var previous=RenderTexture.active;RenderTexture.active=target;read.ReadPixels(new Rect(0,0,target.width,target.height),0,0);read.Apply();RenderTexture.active=previous;}
        static void Save(Texture2D texture,string filename){string dir=Path.GetFullPath(Path.Combine(Application.dataPath,"../Artifacts"));Directory.CreateDirectory(dir);File.WriteAllBytes(Path.Combine(dir,filename),texture.EncodeToPNG());}
    }
}
#endif
