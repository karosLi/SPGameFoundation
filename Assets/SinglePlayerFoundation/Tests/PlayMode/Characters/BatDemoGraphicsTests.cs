#if !SPF_DOTNET_HARNESS
using System.Collections;
using NUnit.Framework;
using SPF.Presentation.Characters;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

namespace SPF.Characters.Tests.PlayMode
{
    public partial class BatGraphicsTests
    {
        [UnityTest]
        public IEnumerator ActualDemo_GpuRenderMeshPrimitives_IKAndRecreate() => CaptureActualDemo(false);

        // Intentionally independent of RequireGpu: usable fallback-only devices must render-test the CPU path.
        [UnityTest]
        public IEnumerator ActualDemo_ForcedCpuDrawMesh_IKAndRecreate() => CaptureActualDemo(true);

        [UnityTest]
        public IEnumerator ActualDemo_ComputePaletteRenderMeshPrimitives_IKAndRecreate() => CaptureActualDemo(false,true);

        static IEnumerator CaptureActualDemo(bool forceCpu,bool preferCompute=false)
        {
            RequireGraphics();
            var cameraObject=new GameObject("BAT production capture camera");
            var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;
            var target=new RenderTexture(768,768,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear){antiAliasing=1};target.Create();camera.targetTexture=target;
            var read=new Texture2D(768,768,TextureFormat.RGBA32,false,true);
            var root=new GameObject("BAT actual production demo");root.SetActive(false);
            var demo=root.AddComponent<BatCharacterDemo>();demo.OutputCamera=camera;demo.ForceCpu=forceCpu;demo.PreferCompute=preferCompute;demo.ActorCount=64;demo.EnableIk=true;
            string suffix=forceCpu?"cpu":preferCompute?"compute":"gpu";
            try
            {
                root.SetActive(true);
                if(forceCpu)Assert.That(demo.ActiveBackend,Is.EqualTo(BatBackend.CpuWeighted));
                else if(demo.ActiveBackend!=(preferCompute?BatBackend.GpuComputePalette:BatBackend.GpuVertex))Assert.Ignore("Actual production GPU draw unsupported here; separate forced-CPU demo test still runs.");
                yield return null;yield return null;
                // Render the supplied camera after Update queued the real Draw() calls. No CommandBuffer test shim.
                camera.Render();Read(target,read);var initial=read.GetPixels32();Save(read,"bat-demo-"+suffix+".png");
                AssertDemoRegions(initial,camera);
                Assert.That(demo.ReadInstance(0).Placement.w,Is.EqualTo(1));Assert.That(demo.ReadInstance(1).Placement.w,Is.EqualTo(-1));
                Assert.That(demo.ReadInstance(0).Frames.x,Is.Not.EqualTo(demo.ReadInstance(2).Frames.x),"Actors must have independent animation phases.");
                Assert.That(demo.ReadInstance(0).Ik.z,Is.EqualTo(1));Assert.That(demo.ReadInstance(1).Ik.z,Is.EqualTo(0));
                demo.ModelTarget=new Vector2(-.65f,1.3f);demo.BendSign=-1;
                yield return null;yield return null;
                camera.Render();Read(target,read);var changed=read.GetPixels32();Save(read,"bat-demo-"+suffix+"-ik.png");
                AssertDemoRegions(changed,camera);
                int changedArmPixels=0;
                Vector3 low=camera.WorldToScreenPoint(new Vector3(-1.3f,.45f,0)),high=camera.WorldToScreenPoint(new Vector3(1.4f,2.5f,0));
                for(int y=math.max(0,(int)low.y);y<=math.min(767,(int)high.y);y++)for(int x=math.max(0,(int)low.x);x<=math.min(767,(int)high.x);x++)
                {
                    var a=initial[y*768+x];var b=changed[y*768+x];
                    // Skin-colored surface, not just the gold/green target/probe markers.
                    if((a.r>190&&a.g>120&&a.g<225&&a.b>65&&a.b<165)||(b.r>190&&b.g>120&&b.g<225&&b.b>65&&b.b<165))
                        if(math.abs(a.r-b.r)+math.abs(a.g-b.g)+math.abs(a.b-b.b)>45)changedArmPixels++;
                }
                Assert.That(changedArmPixels,Is.GreaterThan(20),"Changing the GPU/CPU IK target must visibly deform the selected weighted arm.");
                root.SetActive(false);yield return null;yield return null;
                camera.Render();Read(target,read);var empty=read.GetPixels32();
                Assert.That(NonBackgroundPixels(empty,camera.backgroundColor),Is.LessThan(10),"Disabled demo must not leave stale actors/shadows.");
                root.SetActive(true);yield return null;yield return null;
                camera.Render();Read(target,read);AssertDemoRegions(read.GetPixels32(),camera);Save(read,"bat-demo-"+suffix+"-recreated.png");
                Debug.Log("BAT actual demo draw validated: "+demo.ActiveBackend+", 64 actors, both facings, IK changed arm pixels="+changedArmPixels+", disable/enable resource recreation.");
            }
            finally
            {
                root.SetActive(false);UnityEngine.Object.DestroyImmediate(root);camera.targetTexture=null;
                UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(read);target.Release();UnityEngine.Object.DestroyImmediate(target);
            }
        }
        static void AssertDemoRegions(Color32[] pixels,Camera camera)
        {
            Assert.That(NonBackgroundPixels(pixels,camera.backgroundColor),Is.GreaterThan(10000));
            int magenta=0;foreach(var p in pixels)if(p.r>240&&p.g<15&&p.b>240)magenta++;
            Assert.That(magenta,Is.Zero,"Shader-error magenta in real production demo.");
            for(int i=0;i<64;i++)
            {
                Vector3 point=camera.WorldToScreenPoint(new Vector3(i%8*2.5f,i/8*2.4f+.98f,0));
                int x=Mathf.RoundToInt(point.x),y=Mathf.RoundToInt(point.y);
                Assert.That(x,Is.InRange(2,765));Assert.That(y,Is.InRange(2,765));
                int foreground=0;
                for(int yy=y-2;yy<=y+2;yy++)for(int xx=x-2;xx<=x+2;xx++)
                {var p=pixels[yy*768+xx];if(p.g>50&&p.b>60&&p.a>200)foreground++;}
                Assert.That(foreground,Is.GreaterThan(15),"Actor torso region "+i+" was not rendered by the actual draw API.");
            }
        }
        static int NonBackgroundPixels(Color32[] pixels,Color background)
        {
            Color32 bg=background;int count=0;
            foreach(var p in pixels)if(math.abs(p.r-bg.r)>8||math.abs(p.g-bg.g)>8||math.abs(p.b-bg.b)>8)count++;
            return count;
        }
    }
}
#endif
