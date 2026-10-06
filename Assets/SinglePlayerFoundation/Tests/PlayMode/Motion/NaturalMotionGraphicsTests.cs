#if !SPF_DOTNET_HARNESS
using System.Collections;
using System.IO;
using NUnit.Framework;
using SPF.MotionValidation;
using SPF.Presentation;
using SPF.Presentation.Animation;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace SPF.Motion.Tests.PlayMode
{
    public class NaturalMotionGraphicsTests
    {
        [UnityTest] public IEnumerator NaturalMotion_DataTexture_SequenceAndFallback()=>Capture(true);
        [UnityTest] public IEnumerator NaturalMotion_IndirectSprites_SequenceAndFallback()=>Capture(false);
        static IEnumerator Capture(bool data)
        {
            if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null)Assert.Ignore("Real graphics device required.");
            var camObject=new GameObject("Motion graphics camera");var cam=camObject.AddComponent<Camera>();cam.enabled=false;
            var target=new RenderTexture(960,540,24,RenderTextureFormat.ARGB32){antiAliasing=1};target.Create();cam.targetTexture=target;
            var read=new Texture2D(960,540,TextureFormat.RGBA32,false);
            var root=new GameObject("Actual natural motion showcase");root.SetActive(false);var demo=root.AddComponent<NaturalMotionDemo>();
            demo.OutputCamera=cam;demo.ForceDataTexture=data;demo.Playback=false;demo.InteractiveAimWhilePaused=false;
            string suffix=data?"datatex":"indirect";
            try
            {
                root.SetActive(true);yield return null;yield return null;
                if(!data&&demo.ActiveTier!=RenderTier.GpuDriven)Assert.Ignore("Indirect sprite tier unsupported; independent data-texture test remains.");
                Assert.That(demo.ShadowAtlasBytes,Is.EqualTo(1024*1024));
                Color32[] first=null;
                float[] times={0,.23f,.46f,.69f,.92f,1.18f};
                for(int i=0;i<times.Length;i++)
                {
                    demo.SetPreviewTime(times[i]);yield return null;cam.Render();Read(target,read);var pixels=read.GetPixels32();
                    AssertActors(pixels,cam,demo,960,540);Save(read,"motion-"+suffix+"-"+i+".png");
                    if(i==0)first=pixels;
                    if(i==3)Assert.That(Difference(first,pixels),Is.GreaterThan(1200),"The real cutout draw must visibly animate.");
                    Assert.That(demo.BlobShadows,Is.GreaterThanOrEqualTo(4),"Dynamic foot/arm IK always uses the blob fallback.");
                    Assert.That(demo.BakedShadows,Is.GreaterThanOrEqualTo(1),"Supported pose-library actors should select actual silhouette shadows.");
                }
                // Opt-in evidence capture only. Readbacks, PNG encoding and strings intentionally allocate
                // outside every performance window. Each image is a real render of the actual demo at
                // a deterministic presentation time; no generated/interpolated/in-between images.
                if(data&&System.Environment.GetEnvironmentVariable("SPF_MOTION_CAPTURE_SEQUENCE")=="1")
                {
                    for(int frame=0;frame<30;frame++)
                    {
                        demo.SetPreviewTime(frame*.1f);yield return null;cam.Render();Read(target,read);
                        AssertActors(read.GetPixels32(),cam,demo,960,540);
                        Save(read,"motion-datatex-sequence-"+frame.ToString("D3")+".png");
                    }
                    Debug.Log("Natural motion evidence: 30 actual camera-rendered frames, 10 fps, 3 seconds, CPU/Burst cutout data-texture sprites. Capture allocations are test-only.");
                }
                demo.UnreachableTarget=true;demo.SetPreviewTime(1.18f);yield return null;cam.Render();Read(target,read);Save(read,"motion-"+suffix+"-unreachable.png");
                var a=demo.ReadActor(2);var upper=demo.ReadBone(2,NaturalCharacterRig.NearArm);var hand=demo.ReadBone(2,NaturalCharacterRig.Hand);
                Assert.That(math.distance(upper.Position,hand.Position),Is.LessThanOrEqualTo(.851f*a.Scale));
                Assert.That(math.distance(a.AimTarget,hand.Position),Is.GreaterThan(1));
                demo.ShadowQuality=PoseShadowQuality.None;demo.Shading=false;yield return null;cam.Render();Read(target,read);Save(read,"motion-"+suffix+"-low.png");
                Assert.That(demo.BakedShadows+demo.BlobShadows,Is.Zero);AssertActors(read.GetPixels32(),cam,demo,960,540);
                demo.ShadowQuality=PoseShadowQuality.Blob;yield return null;Assert.That(demo.BakedShadows,Is.Zero);Assert.That(demo.BlobShadows,Is.EqualTo(8));
                root.SetActive(false);yield return null;root.SetActive(true);yield return null;yield return null;
                cam.Render();Read(target,read);AssertActors(read.GetPixels32(),cam,demo,960,540);Save(read,"motion-"+suffix+"-recreated.png");
                // Portrait is fit-only, never a global PlayerSettings orientation change.
                var portrait=new RenderTexture(540,960,24,RenderTextureFormat.ARGB32);portrait.Create();cam.targetTexture=portrait;
                var tallRead=new Texture2D(540,960,TextureFormat.RGBA32,false);
                try{yield return null;cam.Render();Read(portrait,tallRead);AssertActors(tallRead.GetPixels32(),cam,demo,540,960);Save(tallRead,"motion-"+suffix+"-portrait.png");}
                finally{cam.targetTexture=target;Object.DestroyImmediate(tallRead);portrait.Release();Object.DestroyImmediate(portrait);}
                Debug.Log("Natural motion actual showcase validated: "+suffix+", six motion frames, unreachable IK, baked/blob/none shadows, lighting off, recreate, portrait fit.");
            }
            finally
            {
                root.SetActive(false);Object.DestroyImmediate(root);cam.targetTexture=null;Object.DestroyImmediate(camObject);Object.DestroyImmediate(read);target.Release();Object.DestroyImmediate(target);
            }
        }
        static void AssertActors(Color32[] pixels,Camera cam,NaturalMotionDemo demo,int width,int height)
        {
            int magenta=0;foreach(var p in pixels)if(p.r>245&&p.g<10&&p.b>245)magenta++;
            Assert.That(magenta,Is.Zero,"Shader error magenta in actual scene.");
            for(int i=0;i<NaturalMotionDemo.ActorCount;i++)
            {
                var actor=demo.ReadActor(i);var v=cam.WorldToScreenPoint(new Vector3(actor.Root.x,actor.Root.y+1.5f*actor.Scale,0));
                int x=(int)v.x,y=(int)v.y;Assert.That(x,Is.InRange(6,width-7));Assert.That(y,Is.InRange(6,height-7));
                int colored=0;for(int yy=y-5;yy<=y+5;yy++)for(int xx=x-5;xx<=x+5;xx++){var p=pixels[yy*width+xx];if(p.r>65||p.g>85)colored++;}
                Assert.That(colored,Is.GreaterThan(20),"Actual actor chest pixels missing: "+i);
            }
        }
        static int Difference(Color32[] a,Color32[] b)
        {int count=0;for(int i=0;i<a.Length;i++)if(math.abs(a[i].r-b[i].r)+math.abs(a[i].g-b[i].g)+math.abs(a[i].b-b[i].b)>35)count++;return count;}
        static void Read(RenderTexture rt,Texture2D read)
        {var old=RenderTexture.active;RenderTexture.active=rt;read.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);read.Apply();RenderTexture.active=old;}
        static void Save(Texture2D image,string name)
        {string path=Path.Combine(Application.dataPath,"../Artifacts/NaturalMotion");Directory.CreateDirectory(path);File.WriteAllBytes(Path.Combine(path,name),image.EncodeToPNG());}
    }
}
#endif
