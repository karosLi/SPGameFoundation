#if !SPF_DOTNET_HARNESS
using System.Collections;
using System.IO;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Contracts.Weapons;
using SPF.Presentation;
using SPF.Presentation.Animation;
using SPF.Testing;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

namespace SPF.Characters.Tests.PlayMode
{
    /// <summary>Actual shared presenter, both production batch tiers. These are rig/silhouette fixtures;
    /// Bw/Sv gameplay tests separately prove simulation attacks, equipment and projectiles.</summary>
    public class WeaponCharacterGraphicsTests
    {
        [UnityTest] public IEnumerator FourWeaponSilhouettesAndGripsDataTexture()=>RenderWeapons(RenderTier.DataTexture);
        [UnityTest] public IEnumerator FourWeaponSilhouettesAndGripsIndirect()=>RenderWeapons(RenderTier.GpuDriven);
        static GameplayCharacterInput Input(int row,int column)
        {
            bool bow=column==3,staff=column==2;float facing=row==0?1:-1;
            var root=new float2(column*4.1f,row*3.4f);
            return new GameplayCharacterInput {Handle=new EntityHandle(row*4+column,1),Root=root,Ground=root,Facing=facing,Scale=1,Depth=.4f-row*.01f,Tint=new float4(1),
                Weapon=new WeaponViewState {ContentId=1001+column,VisualId=1001+column,Family=(WeaponActionFamily)(column+1),Stage=WeaponStage.Idle,
                ContactPhase=.4f,ReleasePhase=.4f,ActiveEndPhase=.57f,AimDirection=new float2(facing,0),
                GripOffset=new float2(bow?.65f:staff?.45f:.58f,bow?1.42f:staff?1.30f:1.25f),
                SecondaryGripOffset=new float2(bow?.1f:.23f,bow?1.43f:1.18f),
                MuzzleOffset=new float2(column==0?1.65f:column==1?2.12f:staff?1.25f:.94f,bow?1.42f:staff?1.65f:1.25f)}};
        }
        static IEnumerator RenderWeapons(RenderTier tier)
        {
            if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)Assert.Ignore("Needs an actual graphics device.");
            if(tier==RenderTier.GpuDriven&&(!SystemInfo.supportsInstancing||!SystemInfo.supportsComputeShaders))Assert.Ignore("Indirect instancing unavailable.");
            var go=new GameObject("Shared weapon graphics camera");var camera=go.AddComponent<Camera>();camera.enabled=false;
            camera.orthographic=true;camera.orthographicSize=3.75f;camera.transform.position=new Vector3(6.15f,2.6f,-10);
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.045f,.072f,.10f,1);
            try
            {
            using(var capture=new CanvasCapture(go,camera,1600,720))
            using(var presenter=new GameplayCharacterPresenter(tier,8,includeWeapons:true))
            {
                    var actors=new GameplayCharacterInput[8];for(int row=0;row<2;row++)for(int col=0;col<4;col++)actors[row*4+col]=Input(row,col);
                    var read=new Texture2D(1600,720,TextureFormat.RGBA32,false);
                    try
                    {
                        for(int pose=0;pose<4;pose++)
                        {
                            for(int frame=0;frame<24;frame++)
                            {
                                presenter.Begin(1f/120,pose==3?3:0);
                                for(int i=0;i<8;i++)
                                {var a=actors[i];a.Weapon.Stage=pose==0?WeaponStage.Idle:pose==1?WeaponStage.Windup:pose==2?WeaponStage.Active:WeaponStage.Recovery;
                                    a.Weapon.Phase=pose==0?0:pose==1?.22f:pose==2?.4f:.75f;actors[i]=a;presenter.Submit(a);}
                                presenter.Evaluate();
                            }
                            Assert.That(presenter.PartsDrawn,Is.EqualTo(8*19));
                            yield return null;presenter.Draw(new Bounds(new Vector3(6,3,0),new Vector3(22,12,4)));camera.Render();
                            var previous=RenderTexture.active;RenderTexture.active=capture.Target;read.ReadPixels(new Rect(0,0,1600,720),0,0);read.Apply(false);RenderTexture.active=previous;
                            var pixels=read.GetPixels32();int foreground=0,magenta=0;
                            foreach(var pixel in pixels){if(pixel.g>55&&pixel.b>55)foreground++;if(pixel.r>245&&pixel.g<10&&pixel.b>245)magenta++;}
                            Assert.That(foreground,Is.GreaterThan(7000));Assert.That(magenta,Is.Zero);
                            for(int i=0;i<8;i++)
                            {Assert.IsTrue(presenter.TryReadWeapon(actors[i].Handle,out var socket));Assert.That(math.distance(socket.PrimaryGrip,presenter.ReadBone(actors[i].Handle,NaturalCharacterRig.Hand).Position),Is.LessThan(.0001f));}
                            string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../Artifacts/Screenshots/Weapons"));Directory.CreateDirectory(directory);
                            File.WriteAllBytes(Path.Combine(directory,"shared-weapons-"+tier+"-pose"+pose+".png"),read.EncodeToPNG());
                        }
                        System.Action draw=()=>{presenter.Begin(1f/60,0);for(int i=0;i<8;i++)presenter.Submit(actors[i]);presenter.Evaluate();presenter.Draw(new Bounds(new Vector3(6,3,0),new Vector3(22,12,4)));};
                        for(int i=0;i<30;i++)draw();using(var probe=new ManagedAllocationProbe()){probe.Calibrate();System.Action sampleWork=()=>{for(int n=0;n<64;n++)draw();};var sample=probe.Measure(sampleWork);probe.Calibrate();Assert.That(sample.Value,Is.Zero);}
                    }
                    finally{Object.DestroyImmediate(read);}
            }
            }
            finally{Object.DestroyImmediate(go);}
        }
    }
}
#endif
