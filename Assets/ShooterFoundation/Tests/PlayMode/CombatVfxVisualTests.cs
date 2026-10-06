using System.Collections;
using System.IO;
using NUnit.Framework;
using SPF.Presentation;
using SPF.Presentation.Combat;
using SPF.Presentation.Sprites;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace ShooterFoundation.Tests.PlayMode
{
    public class CombatVfxVisualTests
    {
        [UnityTest]
        public IEnumerator LayeredBurstPixelsAndPayloadMatchBothTiers()
        {
            if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null)Assert.Ignore("No graphics device");
            if(!SystemInfo.supportsComputeShaders || SystemInfo.maxComputeBufferInputsVertex<4)Assert.Ignore("GPU tier unavailable");
            var atlas=new SpriteAtlasBuilder();var art=CombatVfxArt.AddTo(atlas);
            using var sheet=atlas.Build(256,FilterMode.Bilinear,2,true);
            var go=new GameObject("CombatVfxProof",typeof(Camera));var camera=go.GetComponent<Camera>();
            camera.orthographic=true;camera.orthographicSize=3.2f;camera.aspect=1f;camera.allowHDR=false;camera.allowMSAA=false;
            camera.transform.position=new Vector3(0,0,-20);camera.nearClipPlane=1;camera.farClipPlane=50;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(0.035f,0.07f,0.11f);camera.cullingMask=1<<30;
            var target=new RenderTexture(640,640,24,RenderTextureFormat.ARGB32);target.Create();camera.targetTexture=target;
            var read=new Texture2D(640,640,TextureFormat.RGBA32,false);var frames=new Color32[2][];
            try
            {
                for(int pass=0;pass<2;pass++)
                {
                    var tier=pass==0?RenderTier.GpuDriven:RenderTier.DataTexture;
                    using var batch=new SpriteBatch(tier,sheet.Texture,BlendKind.Translucent,384);batch.Warmup(384);
                    var pool=new CombatVfxPool();
                    for(int f=0;f<4;f++)
                    {
                        pool.Clear();pool.BeginFrame(0,0);batch.Clear();
                        pool.Emit(VfxProfile.Muzzle,new float2(-1.6f,1.3f),100,2f);
                        pool.Emit(VfxProfile.Impact,new float2(0,1.3f),200,2f);
                        pool.Emit(VfxProfile.Electric,new float2(1.6f,1.3f),300,2f);
                        pool.Emit(VfxProfile.Destruction,new float2(-1f,-1.1f),400,1.5f);
                        pool.Emit(VfxProfile.HeroHurt,new float2(1.3f,-1.1f),500,1.5f);
                        pool.BeginFrame(0.045f,0);pool.Draw(batch,art.Resolve(sheet),new float4(-3.2f,-3.2f,3.2f,3.2f));
                        batch.Draw(new Bounds(Vector3.zero,new Vector3(20,20,50)),30);
                        yield return null;
                    }
                    var previous=RenderTexture.active;RenderTexture.active=target;
                    read.ReadPixels(new Rect(0,0,640,640),0,0);read.Apply(false);RenderTexture.active=previous;
                    frames[pass]=read.GetPixels32();
                    int colored=0,white=0;
                    for(int i=0;i<frames[pass].Length;i++) {var c=frames[pass][i];if(c.r>90 || c.g>130 || c.b>150)colored++;if(c.r>245&&c.g>245&&c.b>245)white++;}
                    Assert.Greater(colored,500,"actual layered flash/ring/spark pixels must be visible");
                    Assert.Less(white,colored/2,"effects retain colored material instead of solid white blobs");
                    Assert.LessOrEqual(batch.BytesUploaded,tier==RenderTier.GpuDriven?12308L:16384L);
                    TestContext.WriteLine($"combat VFX {tier}: sprites={batch.Count}, API bytes={batch.BytesUploaded}, transparent quad/view area={pool.Stats.ScreenArea:F5}, visiblePixels={colored}");
                    string dir=Path.Combine(Application.dataPath,"..","Artifacts","Screenshots");Directory.CreateDirectory(dir);
                    File.WriteAllBytes(Path.Combine(dir,"combat-vfx-"+(pass==0?"gpu":"datatex")+".png"),read.EncodeToPNG());
                    batch.Count=384;batch.Draw(new Bounds(Vector3.zero,new Vector3(20,20,50)),30);
                    Assert.AreEqual(tier==RenderTier.GpuDriven?12308L:16384L,batch.BytesUploaded,"384-slot capacity upload probe, including padding/indirect args; not 384 meaningful effects");
                    TestContext.WriteLine($"combat VFX {tier}: capacity384 API bytes={batch.BytesUploaded}");
                    yield return null; // consume the submitted draw before disposing its GPU resources
                }
                int differing=0;
                for(int i=0;i<frames[0].Length;i++) {var a=frames[0][i];var b=frames[1][i];if(math.abs(a.r-b.r)+math.abs(a.g-b.g)+math.abs(a.b-b.b)>30)differing++;}
                Assert.Less(differing,640*640/200,"both render paths consume the same packed visual stream");
            }
            finally {camera.targetTexture=null;target.Release();Object.Destroy(target);Object.Destroy(read);Object.Destroy(go);}
        }
    }
}
