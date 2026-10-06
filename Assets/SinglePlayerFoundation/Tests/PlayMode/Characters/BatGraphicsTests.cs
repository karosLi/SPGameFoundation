#if !SPF_DOTNET_HARNESS
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using SPF.Testing;
using SPF.Presentation.Characters;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace SPF.Characters.Tests.PlayMode
{
    public partial class BatGraphicsTests
    {
        static void RequireGraphics()
        {
            if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null)Assert.Ignore("No graphics device: this is NOT a GPU validation pass.");
        }
        static void RequireGpu(BatCharacterBatch batch)
        {
            if(batch.Backend!=BatBackend.GpuVertex)Assert.Ignore("Verified BAT capability gate selected CPU. GPU skinning is NOT validated on this backend.");
        }
        [TestCase(false)][TestCase(true)]
        public void VertexStageReadback_MatchesWeightedCpuAndBoundedIk(bool compactHalfAsset)
        {
            RequireGraphics();
            if(!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat))Assert.Ignore("RGBAFloat render target unavailable; numeric GPU parity is unverified.");
            using var asset=CreateAsset(compactHalfAsset);using var batch=new BatCharacterBatch(asset,1);RequireGpu(batch);
            if(compactHalfAsset){Assert.That(asset.HalfAccepted,Is.True);if(batch.Precision!=BatPrecision.Half)Assert.Ignore("Sampleable RGBAHalf absent: half GPU path is unverified.");}
            var shader=Resources.Load<Shader>("SPF/Characters/BatVertexProbe");Assert.That(shader,Is.Not.Null);Assert.That(shader.isSupported,Is.True);
            var material=new Material(shader){enableInstancing=true};var mesh=ProbeMesh(asset);
            var target=new RenderTexture(asset.VertexCount,1,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear){antiAliasing=1};target.Create();
            var read=new Texture2D(asset.VertexCount,1,TextureFormat.RGBAFloat,false,true);
            var targets=new[]{asset.IkShape.xy,new float2(.201f,1.4f),new float2(.7f,1.8f),new float2(3,3),new float2(-3,-2)};
            float maximum=0;
            try
            {
                for(int clip=0;clip<2;clip++)for(int sign=0;sign<2;sign++)for(int side=0;side<2;side++)for(int mode=0;mode<=targets.Length;mode++)
                {
                    float facing=sign==0?1:-1,bend=side==0?-1:1;
                    var instance=asset.Instance(clip,mode==0?-.003f:.367f,new float2(.2f,-.1f),.85f,facing,new float4(1),.125f,mode==0?default:targets[mode-1],mode!=0,bend);
                    batch.Clear();batch.Add(instance);
                    using(var cb=new CommandBuffer()){cb.SetRenderTarget(target);cb.ClearRenderTarget(false,true,Color.clear);batch.RecordProbe(cb,mesh,material);Graphics.ExecuteCommandBuffer(cb);}
                    Read(target,read);var pixels=read.GetPixels();
                    for(int v=0;v<asset.VertexCount;v++)
                    {
                        var expected=asset.Skin(v,instance,batch.Precision);var actual=pixels[v];
                        Assert.That(actual.a,Is.GreaterThan(.99f),"Probe vertex did not render.");
                        float error=math.distance(expected,new float3(actual.r,actual.g,actual.b));maximum=math.max(maximum,error);
                        Assert.That(error,Is.LessThan(1e-4f),"Vertex "+v+", clip "+clip+", IK mode "+mode+", facing "+facing);
                    }
                }
                Debug.Log("BAT actual vertex-stage parity max error="+maximum+"; backend="+SystemInfo.graphicsDeviceType+"; device="+SystemInfo.graphicsDeviceName+"; precision="+batch.Precision);
            }
            finally{UnityEngine.Object.DestroyImmediate(read);target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(material);}
        }
        [Test]
        public void ProductionShader_CpuGpuPixelParity_AndEmptyReuse()
        {
            RequireGraphics();using var asset=BatCharacterAsset.Bake();using var gpu=new BatCharacterBatch(asset,4);RequireGpu(gpu);using var cpu=new BatCharacterBatch(asset,4,true);
            for(int i=0;i<4;i++)
            {
                var data=asset.Instance(i%2,.173f+i*.193f,new float2(i%2*3,i/2*2.7f),1,i%2==0?1:-1,new float4(1,.8f+i*.05f,.7f,1),0,new float2(.6f,2),i>=2,i==2?-1:1);
                gpu.Add(data);cpu.Add(data);
            }
            var target=new RenderTexture(512,512,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear){antiAliasing=1};target.Create();
            var read=new Texture2D(512,512,TextureFormat.RGBA32,false,true);
            try
            {
                var a=Render(gpu,target,read);Save(read,"bat-gpu.png");var b=Render(cpu,target,read);Save(read,"bat-cpu.png");
                int visible=0,mismatch=0,magenta=0;var quadrants=new int[4];
                for(int y=0;y<512;y++)for(int x=0;x<512;x++)
                {
                    int at=y*512+x;var p=a[at];if(p.a>0){visible++;quadrants[(x>=256?1:0)+(y>=256?2:0)]++;}
                    if(p.r>240&&p.g<15&&p.b>240)magenta++;
                    if(!Near(p,b[at])&&!NearNeighbour(p,b,x,y))mismatch++;
                }
                Assert.That(visible,Is.GreaterThan(5000));Assert.That(magenta,Is.Zero);Assert.That(mismatch,Is.LessThan(10),"GPU/CPU interior pixel mismatch; one-pixel edge band allowed.");
                foreach(int occupied in quadrants)Assert.That(occupied,Is.GreaterThan(300),"One phase-separated actor region is empty.");
                gpu.Clear();var empty=Render(gpu,target,read);foreach(var p in empty)Assert.That(p.a,Is.Zero);Assert.That(gpu.BytesUploaded,Is.Zero);Assert.That(gpu.DrawCalls,Is.Zero);
                gpu.Add(asset.Instance(1,.1f,new float2(0),1,1,new float4(1)));var single=Render(gpu,target,read);int singlePixels=0;foreach(var p in single)if(p.a>0)singlePixels++;
                Assert.That(singlePixels,Is.GreaterThan(300));Assert.That(singlePixels,Is.LessThan(visible/2));
                Debug.Log("BAT production pixel parity: "+visible+" pixels, "+mismatch+" mismatches beyond 1-pixel edge tolerance. PNG pair saved under Artifacts/Bat.");
            }
            finally{UnityEngine.Object.DestroyImmediate(read);target.Release();UnityEngine.Object.DestroyImmediate(target);}
        }
        [TestCase(false)][TestCase(true)]
        public void CapacityAndDirtyUploads_256ToZeroToOne(bool forceCpu)
        {
            RequireGraphics();using var asset=BatCharacterAsset.Bake();using var batch=new BatCharacterBatch(asset,256,forceCpu);
            var instance=asset.Instance(0,.3f,0,1,1,new float4(1));
            for(int i=0;i<256;i++)Assert.That(batch.Add(instance),Is.True);
            Assert.That(batch.Add(instance),Is.False);batch.Prepare();
            Assert.That(batch.BytesUploaded,Is.EqualTo(256L*(batch.Backend==BatBackend.GpuVertex?64:asset.VertexCount*BatCpuVertex.Stride)));
            batch.Prepare();Assert.That(batch.BytesUploaded,Is.Zero);batch.Clear();batch.Prepare();Assert.That(batch.Count,Is.Zero);Assert.That(batch.BytesUploaded,Is.Zero);
            batch.Add(instance);batch.Prepare();Assert.That(batch.Count,Is.EqualTo(1));batch.Dispose();batch.Dispose();Assert.Throws<ObjectDisposedException>(()=>batch.Clear());
        }
        [TestCase(false)][TestCase(true)]
        public void WarmedPrepareDoesNotAllocateManagedMemory(bool forceCpu)
        {
            RequireGraphics();using var asset=BatCharacterAsset.Bake();using var batch=new BatCharacterBatch(asset,64,forceCpu);
            var instance=asset.Instance(0,.3f,0,1,1,new float4(1));for(int i=0;i<64;i++)batch.Add(instance);
            for(int f=0;f<64;f++){instance.Frames.z=(f%10)*.1f;batch.Set(0,instance);batch.Prepare();}
            Action measured = () =>
            {
                for(int f=0;f<64;f++){instance.Frames.z=(f%10)*.1f;batch.Set(0,instance);batch.Prepare();}
            };
            using var probe = new ManagedAllocationProbe();
            var calibrationBefore = probe.Calibrate();
            var sample = probe.Measure(measured);
            var calibrationAfter = probe.Calibrate();
            TestContext.WriteLine($"BAT warmed Prepare backend={batch.Backend}, 64 updates after 64 warm-up updates: {sample.Value} current-thread {sample.Metric}; independent process-wide gen0 collections={sample.Collections}; retained-array/empty calibration before={calibrationBefore.RetainedArrays.Value}/{calibrationBefore.Empty.Value}, after={calibrationAfter.RetainedArrays.Value}/{calibrationAfter.Empty.Value}.");
            Assert.That(sample.Value, Is.Zero, $"Warmed BAT Prepare must allocate zero current-thread {sample.Metric}; excludes driver/native GPU memory.");
        }
        static BatClipSet CreateAsset(bool compact)
        {
            if(!compact)return BatCharacterAsset.Bake();
            using var rig=BatCharacterAsset.CreateRig();BatCharacterAsset.CreateMesh(out var vertices,out var indices);
            for(int b=0;b<rig.BoneCount;b++){var bone=rig.Bones[b];bone.Position*=.1f;bone.Length*=.1f;rig.Bones[b]=bone;}
            for(int v=0;v<vertices.Length;v++){var vertex=vertices[v];vertex.Position*=.1f;vertices[v]=vertex;}
            return BatBaker.Bake(rig.View,vertices,indices);
        }
        static Mesh ProbeMesh(BatClipSet asset)
        {
            var p=new Vector3[asset.VertexCount*4];var skin=new List<Vector4>(p.Length);var clip=new List<Vector2>(p.Length);var indices=new int[asset.VertexCount*6];
            for(int v=0;v<asset.VertexCount;v++)
            {
                var vertex=asset.Vertex(v);float left=2f*v/asset.VertexCount-1,right=2f*(v+1)/asset.VertexCount-1;
                for(int j=0;j<4;j++){p[v*4+j]=new Vector3(vertex.Position.x,vertex.Position.y,0);skin.Add(new Vector4(vertex.Skin.x,vertex.Skin.y,vertex.Skin.z,vertex.Skin.w));clip.Add(new Vector2(j==0||j==3?left:right,j<2?-1:1));}
                int at=v*6,b=v*4;indices[at]=b;indices[at+1]=b+1;indices[at+2]=b+2;indices[at+3]=b;indices[at+4]=b+2;indices[at+5]=b+3;
            }
            var mesh=new Mesh {vertices=p,triangles=indices};mesh.SetUVs(1,skin);mesh.SetUVs(2,clip);return mesh;
        }
        static Color32[] Render(BatCharacterBatch batch,RenderTexture target,Texture2D read)
        {
            using(var cb=new CommandBuffer())
            {
                cb.SetRenderTarget(target);cb.ClearRenderTarget(true,true,Color.clear);
                cb.SetViewProjectionMatrices(Matrix4x4.identity,GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(-1.8f,4.8f,-.5f,6.1f,-5,5),true));
                batch.Record(cb);Graphics.ExecuteCommandBuffer(cb);
            }
            Read(target,read);return read.GetPixels32();
        }
        static void Read(RenderTexture target,Texture2D read)
        {var prior=RenderTexture.active;try{RenderTexture.active=target;read.ReadPixels(new Rect(0,0,target.width,target.height),0,0);read.Apply(false,false);}finally{RenderTexture.active=prior;}}
        static bool Near(Color32 a,Color32 b)=>math.abs(a.r-b.r)<=3&&math.abs(a.g-b.g)<=3&&math.abs(a.b-b.b)<=3&&math.abs(a.a-b.a)<=3;
        static bool NearNeighbour(Color32 a,Color32[] b,int x,int y)
        {for(int yy=math.max(0,y-1);yy<=math.min(511,y+1);yy++)for(int xx=math.max(0,x-1);xx<=math.min(511,x+1);xx++)if(Near(a,b[yy*512+xx]))return true;return false;}
        static void Save(Texture2D texture,string name)
        {string dir=Path.GetFullPath(Path.Combine(Application.dataPath,"../Artifacts/Bat"));Directory.CreateDirectory(dir);File.WriteAllBytes(Path.Combine(dir,name),texture.EncodeToPNG());}
    }
}
#endif
