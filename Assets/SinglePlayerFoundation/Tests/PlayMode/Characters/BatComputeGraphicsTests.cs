#if !SPF_DOTNET_HARNESS
using System;
using System.Collections;
using NUnit.Framework;
using SPF.Presentation.Characters;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace SPF.Characters.Tests.PlayMode
{
    public partial class BatGraphicsTests
    {
        static void RequireCompute(BatCharacterBatch batch)
        {
            Assert.That(Resources.Load<ComputeShader>("SPF/Characters/BatPalette"),Is.Not.Null,"Compute asset must ship even on fallback devices.");
            Assert.That(Resources.Load<Shader>("SPF/Characters/BatComputed"),Is.Not.Null);
            if(batch.Backend!=BatBackend.GpuComputePalette)
                Assert.Ignore("Compute palette capability gate selected "+batch.Backend+". Actual compute execution is NOT verified on this device.");
        }

        [TestCase(false)][TestCase(true)]
        public void ComputeBufferReadback_ThreeMatricesMatchCpuReferenceAcrossAllCases(bool compactHalfAsset)
        {
            RequireGraphics();using var asset=CreateAsset(compactHalfAsset);
            using var batch=new BatCharacterBatch(asset,65,preferCompute:true);RequireCompute(batch);
            if(compactHalfAsset&&batch.Precision!=BatPrecision.Half)Assert.Ignore("Accepted half format unavailable; half compute path remains unverified.");
            var output=new BatRows[65*BatLimits.Bones];float maximum=0;
            for(int pass=0;pass<3;pass++)
            {
                batch.Clear();
                for(int i=0;i<65;i++)batch.Add(ComputeInstance(asset,i+pass*65));
                Assert.Throws<InvalidOperationException>(()=>batch.ReadbackComputedPaletteForValidation(output));
                batch.Prepare();
                Assert.That(batch.DispatchCalls,Is.EqualTo(1));Assert.That(batch.DispatchGroups,Is.EqualTo(2));
                Assert.That(batch.ReadbackComputedPaletteForValidation(output),Is.EqualTo(output.Length));
                for(int i=0;i<65;i++)
                {
                    var expected=asset.SamplePalette(batch.Get(i),batch.Precision);
                    for(int bone=0;bone<3;bone++)
                    {
                        var a=output[i*3+bone];var b=expected.Bone(bone);
                        maximum=math.max(maximum,math.max(math.cmax(math.abs(a.Row0-b.Row0)),math.cmax(math.abs(a.Row1-b.Row1))));
                        Assert.That(math.all(math.isfinite(a.Row0))&&math.all(math.isfinite(a.Row1)),Is.True);
                        Assert.That(maximum,Is.LessThan(1e-4f),"Actual GPU palette differs from the CPU reference.");
                    }
                }
            }
            Debug.Log("BAT actual compute buffer parity maximum coefficient error="+maximum+", precision="+batch.Precision+", device="+SystemInfo.graphicsDeviceName);
        }

        [TestCase(1)][TestCase(63)][TestCase(64)][TestCase(65)][TestCase(255)][TestCase(256)]
        public void ComputeCapacityCounters_EmptyReuseAndDispose(int capacity)
        {
            RequireGraphics();using var asset=BatCharacterAsset.Bake();
            using var batch=new BatCharacterBatch(asset,capacity,preferCompute:true);RequireCompute(batch);
            var output=new BatRows[capacity*3];
            Assert.That(batch.InstanceBufferBytes,Is.EqualTo(capacity*64));
            Assert.That(batch.ComputedPaletteBytes,Is.EqualTo(capacity*96));
            Assert.That(batch.PaletteBytes,Is.EqualTo(asset.FrameCount*3*32));
            for(int i=0;i<capacity;i++)Assert.That(batch.Add(ComputeInstance(asset,i)),Is.True);
            Assert.That(batch.Add(ComputeInstance(asset,0)),Is.False);batch.Prepare();
            Assert.That(batch.BytesUploaded,Is.EqualTo(capacity*64));Assert.That(batch.PaletteBytesWritten,Is.EqualTo(capacity*96));
            Assert.That(batch.DispatchCalls,Is.EqualTo(1));Assert.That(batch.DispatchGroups,Is.EqualTo((capacity+63)/64));
            batch.ReadbackComputedPaletteForValidation(output);
            var last=asset.SamplePalette(batch.Get(capacity-1),batch.Precision);
            Assert.That(math.distance(last.Lower.Row0,output[(capacity-1)*3+2].Row0),Is.LessThan(1e-4f),"Tail actor must execute.");
            batch.Prepare();Assert.That(batch.DispatchCalls,Is.Zero);Assert.That(batch.BytesUploaded,Is.Zero);Assert.That(batch.PaletteBytesWritten,Is.Zero);
            batch.Clear();using(var cb=new CommandBuffer())batch.Record(cb);
            Assert.That(batch.DispatchCalls,Is.Zero);Assert.That(batch.DispatchGroups,Is.Zero);Assert.That(batch.DrawCalls,Is.Zero);Assert.That(batch.BytesUploaded,Is.Zero);
            Assert.That(batch.ReadbackComputedPaletteForValidation(output),Is.Zero);
            var next=ComputeInstance(asset,121);batch.Add(next);batch.Prepare();
            Assert.That(batch.DispatchCalls,Is.EqualTo(1));Assert.That(batch.DispatchGroups,Is.EqualTo(1));Assert.That(batch.BytesUploaded,Is.EqualTo(64));Assert.That(batch.PaletteBytesWritten,Is.EqualTo(96));
            Assert.That(batch.ReadbackComputedPaletteForValidation(output),Is.EqualTo(3));
            Assert.That(math.distance(asset.SamplePalette(next,batch.Precision).Lower.Row1,output[2].Row1),Is.LessThan(1e-4f));
            batch.Dispose();batch.Dispose();Assert.That(batch.InstanceBufferBytes,Is.Zero);Assert.That(batch.ComputedPaletteBytes,Is.Zero);
            Assert.Throws<ObjectDisposedException>(()=>batch.Prepare());Assert.Throws<ObjectDisposedException>(()=>batch.ReadbackComputedPaletteForValidation(output));
            Assert.That(asset.IsDisposed,Is.False);
        }

        [Test]
        public void ComputeBatchesOwnBindingsAndForceCpuStillOverridesPreference()
        {
            RequireGraphics();using var a=CreateAsset(false);using var b=CreateAsset(true);
            using var first=new BatCharacterBatch(a,1,preferCompute:true);RequireCompute(first);
            using var second=new BatCharacterBatch(b,1,preferCompute:true);RequireCompute(second);
            using var cpu=new BatCharacterBatch(a,1,forceCpu:true,preferCompute:true);
            Assert.That(cpu.Backend,Is.EqualTo(BatBackend.CpuWeighted));Assert.That(cpu.ComputedPaletteBytes,Is.Zero);
            var rows=new BatRows[3];
            for(int i=0;i<8;i++)
            {
                first.Clear();second.Clear();first.Add(ComputeInstance(a,i));second.Add(ComputeInstance(b,i+3));
                first.Prepare();second.Prepare();first.ReadbackComputedPaletteForValidation(rows);
                Assert.That(math.distance(rows[2].Row1,a.SamplePalette(first.Get(0),first.Precision).Lower.Row1),Is.LessThan(1e-4f));
                second.ReadbackComputedPaletteForValidation(rows);
                Assert.That(math.distance(rows[1].Row0,b.SamplePalette(second.Get(0),second.Precision).Upper.Row0),Is.LessThan(1e-4f));
            }
        }

        [UnityTest]
        public IEnumerator ProductionDraw_ComputePaletteMatchesCpuWithClipBlendMirrorScaleAndBothIkBends()
        {
            RequireGraphics();using var asset=BatCharacterAsset.Bake();
            using var gpu=new BatCharacterBatch(asset,4,preferCompute:true);RequireCompute(gpu);
            using var cpu=new BatCharacterBatch(asset,4,true);
            var cameraObject=new GameObject("BAT computed production draw parity camera");var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;
            camera.orthographic=true;camera.orthographicSize=3.3f;camera.transform.position=new Vector3(1.5f,2.8f,-10);
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;
            var target=new RenderTexture(512,512,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear){antiAliasing=1};target.Create();camera.targetTexture=target;
            var read=new Texture2D(512,512,TextureFormat.RGBA32,false,true);
            try
            {
                for(int i=0;i<4;i++)
                {
                    var instance=ComputeInstance(asset,i+4);instance.Placement.xy=new float2(i%2*3,i/2*2.7f);
                    instance.Placement.z=i%2==0?.85f:1.15f;
                    // Two clip blends without IK, then reachable/unreachable IK with opposite bends.
                    instance.Ik.z=i<2?0:1;instance.Ik.w=i==2?-1:1;
                    instance.Ik.xy=i==2?new float2(.6f,2):new float2(3,-2);
                    gpu.Add(instance);cpu.Add(instance);
                }
                yield return null;gpu.Draw(camera);camera.Render();Read(target,read);var computed=read.GetPixels32();Save(read,"bat-compute-production.png");
                Assert.That(gpu.DrawCalls,Is.EqualTo(1));Assert.That(gpu.DispatchCalls,Is.EqualTo(1));
                Assert.That(gpu.BytesUploaded,Is.EqualTo(4*64));Assert.That(gpu.PaletteBytesWritten,Is.EqualTo(4*96));
                yield return null;cpu.Draw(camera);camera.Render();Read(target,read);var fallback=read.GetPixels32();Save(read,"bat-compute-production-cpu.png");
                int occupied=0,mismatches=0,magenta=0;var regions=new int[4];
                for(int y=0;y<512;y++)for(int x=0;x<512;x++)
                {
                    int at=y*512+x;var p=computed[at];
                    if(p.a>0){occupied++;regions[(x>=256?1:0)+(y>=256?2:0)]++;}
                    if(p.r>240&&p.g<15&&p.b>240)magenta++;
                    if(!Near(p,fallback[at])&&!NearNeighbour(p,fallback,x,y))mismatches++;
                }
                Assert.That(occupied,Is.GreaterThan(5000));Assert.That(magenta,Is.Zero);Assert.That(mismatches,Is.LessThan(10));
                foreach(int n in regions)Assert.That(n,Is.GreaterThan(300));
                yield return null;gpu.Clear();gpu.Draw(camera);camera.Render();Read(target,read);
                foreach(var p in read.GetPixels32())Assert.That(p.a,Is.Zero,"Empty production draw must not retain stale actors.");
                Assert.That(gpu.DispatchCalls,Is.Zero);Assert.That(gpu.DrawCalls,Is.Zero);
                Debug.Log("BAT actual Graphics.RenderMeshPrimitives compute/CPU pixel parity: "+occupied+" occupied pixels; "+mismatches+" interior mismatches (one-pixel edge tolerance).");
            }
            finally{camera.targetTexture=null;UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(read);target.Release();UnityEngine.Object.DestroyImmediate(target);}
        }

        [Test]
        public void WarmedComputeDrawHasCalibratedZeroCurrentThreadManagedAllocation()
        {
            RequireGraphics();using var asset=BatCharacterAsset.Bake();
            using var batch=new BatCharacterBatch(asset,64,preferCompute:true);RequireCompute(batch);
            var go=new GameObject("BAT compute allocation camera");var camera=go.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;
            try
            {
                for(int i=0;i<64;i++)batch.Add(ComputeInstance(asset,i));
                var instance=batch.Get(0);
                for(int f=0;f<64;f++){instance.Frames.z=(f%10)*.1f;batch.Set(0,instance);batch.Draw(camera);}
                Action measured=()=>{for(int f=0;f<64;f++){instance.Frames.z=(f%10)*.1f;batch.Set(0,instance);batch.Draw(camera);}};
                using var probe=new SPF.Testing.ManagedAllocationProbe();var before=probe.Calibrate();var sample=probe.Measure(measured);var after=probe.Calibrate();
                TestContext.WriteLine($"Compute Draw: 64 dirty upload/dispatch/render submissions after 64 warm-ups; {sample.Value} current-thread {sample.Metric}; process gen0 collections={sample.Collections}; retained-array/empty controls before={before.RetainedArrays.Value}/{before.Empty.Value}, after={after.RetainedArrays.Value}/{after.Empty.Value}. Excludes native/driver work, other threads and frame rendering.");
                Assert.That(sample.Value,Is.Zero,"Calibrated current-thread managed allocation during warmed production Draw.");
            }
            finally{UnityEngine.Object.DestroyImmediate(go);}
        }

        static BatInstance ComputeInstance(BatClipSet asset,int i)
        {
            int mode=i%5;
            float2 target=mode==1?asset.IkShape.xy:mode==2?asset.IkShape.xy+new float2(.2f,.3f):mode==3?new float2(3,3):new float2(-3,-2);
            var instance=asset.Instance(i%2,-.003f+i*.113f,new float2(.2f,-.1f),i%2==0?.85f:1.7f,i%2==0?1:-1,new float4(1),.125f,target,mode!=0,(i/5)%2==0?-1:1);
            if(i%3!=0)instance.Frames=new float4(17,asset.Clip(1).FirstFrame+29,.37f,.125f);
            return instance;
        }
    }
}
#endif
