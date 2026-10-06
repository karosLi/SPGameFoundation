#if !SPF_DOTNET_HARNESS
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Presentation;
using SPF.Presentation.Animation;
using SPF.Testing;
using SurvivorFoundation.Game;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;

namespace SurvivorFoundation.Tests.PlayMode
{
    public class SvNaturalGameplayTests
    {
        [UnityTest]
        public IEnumerator NaturalHordeMovesHitsAndKeepsEveryFallback([Values(RenderTier.GpuDriven,RenderTier.DataTexture)] RenderTier tier)
        {
            if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null)Assert.Ignore("Real graphics required.");
            if(tier==RenderTier.GpuDriven&&!SystemInfo.supportsComputeShaders)Assert.Ignore("Indirect tier unsupported.");
            RenderCapabilities.Override=tier;var config=SvConfig.CreateMobileCombatExample();
            config.Settings.SpawnPerSecond=config.Settings.SpawnGrowth=config.Settings.EliteEvery=0;config.Settings.XpBase=100000;
            config.Settings.BeaconHp=100000;config.Settings.HeroHp=100000;
            foreach(var e in config.Enemies){e.Hp=1000;e.Damage=0;e.Speed=.2f;}
            var game=SvGameBootstrap.CreateMobileCombatExample(config);game.Renderer.NaturalCharacters=true;CanvasCapture capture=null;
            string suffix=tier==RenderTier.GpuDriven?"gpu":"datatex";
            try
            {
                yield return null;game.StartRun();yield return UIDriver.WaitUntil(()=>game.State.Flow==SvFlow.Playing,5);
                game.Session.Sync();game.Session.ManualClock=true;game.InputRouter.enabled=false;game.Governor.AdaptiveQuality=false;
                var world=game.Session.World;world.ClearLevel();var runtime=world.Resource(SvKeys.Config);
                for(int k=0;k<game.State.Upgrades.Length;k++)game.State.Upgrades[k]=0;
                // More than the high-detail budget: cheap fallback must remain visible, including at low quality.
                for(int i=0;i<224;i++)SvSpawner.SpawnEnemy(world,runtime,1+i%runtime.EnemyKinds,new float2(-4.4f+i%16*.58f,1.1f+i/16*.48f));
                game.Session.Step();capture=new CanvasCapture(game.gameObject,game.CameraRig.Camera,720,1280);var safe=new Rect(0,0,720,1280);
                game.Hud.MobileHud.SetPreviewViewport(720,1280,safe);game.CameraRig.Snap();
                yield return capture.Save("natural-horde-ready-"+suffix,safe);
                Assert.Greater(game.Renderer.ArticulatedEnemies,100);Assert.Greater(game.Renderer.EnemiesDrawn,game.Renderer.ArticulatedEnemies);
                var first=capture.Pixels;float2 start=game.State.Hero;
                for(int tick=0;tick<8;tick++){game.State.Input=new InputFrame {Move=new float2(.6f,.4f)};game.Session.Step();}
                yield return capture.Save("natural-horde-run-"+suffix,safe);
                Assert.Greater(math.distance(start,game.State.Hero),.1f);Assert.Greater(Difference(first,capture.Pixels),500);
                var enemyInfo=world.Column(SvKeys.Info);var doomed=enemyInfo[7];doomed.Hp=1;enemyInfo[7]=doomed;
                game.State.Input=new InputFrame {Pressed=1u<<SvMobileSkills.Pulse};game.Session.Step();game.State.Input=default;
                yield return capture.Save("natural-horde-hit-"+suffix,safe);
                Assert.That(game.Renderer.Characters.VisibleStates&(1u<<(int)GameplayCharacterState.Hit),Is.Not.Zero,"actual pulse damage drives hit presentation");
                Assert.That(game.Renderer.Characters.VisibleStates&(1u<<(int)GameplayCharacterState.Death),Is.Not.Zero,"verified death feedback has a bounded fall/fade pose");
                bool damaged=false;for(int i=0;i<world.Table(SvKeys.Enemy).Count;i++)damaged|=world.Column(SvKeys.Info)[i].Hp<1000;Assert.IsTrue(damaged);
                for(int tick=0;tick<20;tick++)game.Session.Step();yield return null;
                byte[] before=game.Session.CaptureSnapshot();int full=game.Renderer.EnemiesDrawn;
                game.Renderer.SetQualityLevel(3);game.Renderer.RenderFrame();
                Assert.LessOrEqual(game.Renderer.ArticulatedEnemies,48);Assert.AreEqual(full,game.Renderer.EnemiesDrawn,"quality never hides remaining enemies");
                CollectionAssert.AreEqual(before,game.Session.CaptureSnapshot(),"presentation quality/LOD must preserve snapshot bytes");
                yield return capture.Save("natural-horde-low-"+suffix,safe);
                game.Renderer.SetQualityLevel(0);game.Renderer.RenderFrame();
                Assert.AreEqual(192,game.Renderer.ArticulatedEnemies,"allocation window exercises the full articulated horde budget");
                Action warmed=()=>{for(int i=0;i<60;i++)game.Renderer.RenderFrame();};warmed();
                using(var probe=new ManagedAllocationProbe()){probe.Calibrate();var watch=System.Diagnostics.Stopwatch.StartNew();var sample=probe.Measure(warmed);watch.Stop();probe.Calibrate();TestContext.WriteLine("Natural horde 192-actor full renderer: "+(watch.Elapsed.TotalMilliseconds/60).ToString("F3")+" ms/call, "+sample.Value+" "+sample.Metric+"; desktop test, not device performance.");Assert.AreEqual(0,sample.Value,"calibrated synchronous full-render allocation samples");}
                if(Environment.GetEnvironmentVariable("SPF_GAMEPLAY_CHARACTER_SEQUENCE")=="1")
                    yield return CaptureRunningSequence(game,capture,safe,suffix);
                LogAssert.NoUnexpectedReceived();
            }
            finally{capture?.Dispose();RenderCapabilities.Override=null;if(Camera.main!=null)Object.Destroy(Camera.main.gameObject);Object.Destroy(game.gameObject);Object.Destroy(config);}
        }
        static IEnumerator CaptureRunningSequence(SvGameBootstrap game,CanvasCapture capture,Rect safe,string suffix)
        {
            bool manual=game.Session.ManualClock;var previous=game.State.Input;
            var times=new double[30];var names=new string[30];double start=Time.realtimeSinceStartupAsDouble;
            game.Session.ManualClock=false;
            try
            {
                for(int frame=0;frame<30;frame++)
                {
                    while(Time.realtimeSinceStartupAsDouble<start+frame*.1)yield return null;
                    game.State.Input=new InputFrame {Move=new float2(frame<15?-.5f:.5f,.2f),Pressed=frame==8?1u<<SvMobileSkills.Pulse:0};
                    names[frame]="natural-horde-sequence-"+suffix+"-"+frame.ToString("D3");
                    yield return capture.Save(names[frame],safe);
                    times[frame]=capture.CaptureRealtime-start;
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
            string name="natural-horde-sequence-"+suffix;
            File.WriteAllText(Path.Combine(dir,name+".csv"),csv.ToString());File.WriteAllText(Path.Combine(dir,name+".ffconcat"),concat.ToString());
        }
        static int Difference(Color32[] a,Color32[] b){int n=0;for(int i=0;i<a.Length;i++)if(math.abs(a[i].r-b[i].r)+math.abs(a[i].g-b[i].g)+math.abs(a[i].b-b[i].b)>40)n++;return n;}
    }
}
#endif
