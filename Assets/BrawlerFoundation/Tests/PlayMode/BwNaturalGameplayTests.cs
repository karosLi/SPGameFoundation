#if !SPF_DOTNET_HARNESS
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using BrawlerFoundation.Game;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Presentation;
using SPF.Presentation.Animation;
using SPF.Testing;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;

namespace BrawlerFoundation.Tests.PlayMode
{
    public class BwNaturalGameplayTests
    {
        [UnityTest]
        public IEnumerator NaturalCharactersMoveHitRecoverAndKeepSimulation([Values(RenderTier.GpuDriven,RenderTier.DataTexture)] RenderTier tier)
        {
            if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null)Assert.Ignore("Real graphics required.");
            if(tier==RenderTier.GpuDriven&&!SystemInfo.supportsComputeShaders)Assert.Ignore("Indirect tier unsupported.");
            RenderCapabilities.Override=tier;var game=BwGameBootstrap.CreateMobileCombat();CanvasCapture capture=null;
            game.Renderer.NaturalCharacters=true;string suffix=tier==RenderTier.GpuDriven?"gpu":"datatex";
            try
            {
                yield return null;UIDriver.Click(game.StartButton.gameObject);
                yield return UIDriver.WaitUntil(()=>game.State.Flow==BwFlow.Fighting,5);
                game.Session.Sync();game.Session.ManualClock=true;game.InputRouter.enabled=false;game.Governor.AdaptiveQuality=false;
                var world=game.Session.World;world.ClearLevel();BwSpawner.Spawn(world,0,new float2(-1.8f,0),1,0);
                BwSpawner.Spawn(world,1,new float2(.1f,0),-1,0);BwSpawner.Spawn(world,1,new float2(5,0),-1,2);
                var info=world.Column(BwKeys.Info);var f=info[1];f.Hp=f.MaxHp=1000;f.Cooldown=20;info[1]=f;
                var hero=info[0];hero.Hp=hero.MaxHp=1000;info[0]=hero;
                var identity=world.Table(BwKeys.Fighter).Handles[0];
                capture=new CanvasCapture(game.gameObject,game.CameraRig.Camera,1280,720);var safe=new Rect(0,0,1280,720);
                game.MobileHud.SetPreviewViewport(1280,720,safe);game.CameraRig.Snap();
                yield return capture.Save("natural-brawler-ready-"+suffix,safe);
                Assert.AreEqual(42,game.Renderer.PartsDrawn);Assert.AreEqual(1024*1024,game.Renderer.Characters.ColorAtlasBytes);
                var first=capture.Pixels;
                float start=world.Column(BwKeys.Position)[0].x;
                for(int tick=0;tick<12;tick++){game.State.Input=new InputFrame {Move=new float2(1,0)};game.Session.Step();}
                yield return capture.Save("natural-brawler-run-"+suffix,safe);
                Assert.Greater(world.Column(BwKeys.Position)[0].x,start+.2f);
                Assert.Greater(Difference(first,capture.Pixels),500,"actual moving gameplay must change rendered pixels");
                uint seen=game.Renderer.Characters.VisibleStates;bool hit=false;
                for(int frame=0;frame<8;frame++)
                {
                    for(int tick=0;tick<4;tick++){game.State.Input=new InputFrame {Held=1u<<BwButton.Punch,Pressed=tick==0?1u<<BwButton.Punch:0};game.Session.Step();}
                    yield return null;seen|=game.Renderer.Characters.VisibleStates;hit|=info[1].Hp<1000;
                    if(frame==1||frame==4)yield return capture.Save("natural-brawler-combat-"+frame+"-"+suffix,safe);
                }
                Assert.IsTrue(hit,"natural view follows real damage, not showcase sparks");
                Assert.That(seen&(1u<<(int)GameplayCharacterState.Attack),Is.Not.Zero);
                Assert.That(seen&((1u<<(int)GameplayCharacterState.Hit)|(1u<<(int)GameplayCharacterState.Recovery)),Is.Not.Zero);
                game.State.Input=default;for(int tick=0;tick<30;tick++)game.Session.Step();yield return null;
                byte[] before=game.Session.CaptureSnapshot();game.Renderer.SetQualityLevel(3);game.Renderer.RenderFrame();
                CollectionAssert.AreEqual(before,game.Session.CaptureSnapshot(),"quality and IK never alter simulation snapshots");
                yield return capture.Save("natural-brawler-low-"+suffix,safe);
                game.Renderer.Characters.TryRead(identity,out var motion);
                var shoulder=game.Renderer.Characters.ReadBone(identity,NaturalCharacterRig.NearArm);var hand=game.Renderer.Characters.ReadBone(identity,NaturalCharacterRig.Hand);
                Assert.LessOrEqual(math.distance(shoulder.Position,hand.Position),.851f*motion.Scale);
                game.Renderer.SetQualityLevel(0);
                Action warmed=()=>{for(int i=0;i<120;i++)game.Renderer.RenderFrame();};warmed();
                using(var probe=new ManagedAllocationProbe()){probe.Calibrate();var sample=probe.Measure(warmed);probe.Calibrate();Assert.AreEqual(0,sample.Value,"calibrated synchronous full-render allocation samples");}
                if(Environment.GetEnvironmentVariable("SPF_GAMEPLAY_CHARACTER_SEQUENCE")=="1")
                    yield return CaptureRunningSequence(game,capture,safe,suffix);
                LogAssert.NoUnexpectedReceived();
            }
            finally{capture?.Dispose();RenderCapabilities.Override=null;if(Camera.main!=null)Object.Destroy(Camera.main.gameObject);Object.Destroy(game.gameObject);}
        }
        static IEnumerator CaptureRunningSequence(BwGameBootstrap game,CanvasCapture capture,Rect safe,string suffix)
        {
            bool manual=game.Session.ManualClock;var previous=game.State.Input;
            var times=new double[30];var names=new string[30];double start=0,next=0;
            game.Session.ManualClock=false;
            try
            {
                for(int frame=0;frame<30;frame++)
                {
                    if(frame>0)while(Time.realtimeSinceStartupAsDouble<next)yield return null;
                    game.State.Input=new InputFrame {Move=frame<10?new float2(-1,0):frame<20?new float2(1,0):float2.zero,Held=frame>=20?1u:0u};
                    names[frame]="natural-brawler-sequence-"+suffix+"-"+frame.ToString("D3");
                    yield return capture.Save(names[frame],safe);
                    if(frame==0)start=capture.CaptureRealtime;
                    times[frame]=capture.CaptureRealtime-start;next=capture.CaptureRealtime+.1;
                }
            }
            finally{game.Session.Sync();game.Session.ManualClock=manual;game.State.Input=previous;}
            // Slow editor frames/readback may miss the target cadence. Preserve measured acquisition
            // intervals in ffconcat instead of pretending these were thirty evenly spaced 10-Hz frames.
            string dir=Path.Combine(Application.dataPath,"..","Artifacts","Screenshots","MobileHud");
            var csv=new StringBuilder("frame,acquisition_seconds\n");var concat=new StringBuilder("ffconcat version 1.0\n");
            for(int i=0;i<times.Length;i++)
            {
                csv.Append(i).Append(',').Append(times[i].ToString("F6",CultureInfo.InvariantCulture)).Append('\n');
                double duration=i+1<times.Length?math.max(.001,(times[i+1]-times[i])):(times.Length>1?times[i]-times[i-1]:.1);
                concat.Append("file '").Append(names[i]).Append(".png'\n").Append("duration ").Append(duration.ToString("F6",CultureInfo.InvariantCulture)).Append('\n');
            }
            concat.Append("file '").Append(names[names.Length-1]).Append(".png'\n");
            string name="natural-brawler-sequence-"+suffix;
            File.WriteAllText(Path.Combine(dir,name+".csv"),csv.ToString());File.WriteAllText(Path.Combine(dir,name+".ffconcat"),concat.ToString());
        }
        static int Difference(Color32[] a,Color32[] b){int n=0;for(int i=0;i<a.Length;i++)if(math.abs(a[i].r-b[i].r)+math.abs(a[i].g-b[i].g)+math.abs(a[i].b-b[i].b)>40)n++;return n;}
    }
}
#endif
