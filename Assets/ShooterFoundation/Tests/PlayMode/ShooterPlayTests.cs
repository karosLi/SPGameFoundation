using System.Collections;
using System.IO;
using System.Text;
using NUnit.Framework;
using ShooterFoundation.Game;
using SPF.Presentation;
using SPF.Testing;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace ShooterFoundation.Tests.PlayMode
{
    public class ShooterPlayTests
    {
        struct SteadyFrameSample
        {
            public int ReadFrame, GovernorFrames, Flow, Version, Kills, Wave;
            public long Tick, PreviousFrameBytes, CumulativeBytes;
        }

        static void WriteSteadySamples(RenderTier tier, SteadyFrameSample[] samples)
        {
            var output = new StringBuilder("ordinal,readUnityFrame,governorFramesSinceReset,previousFrameBytes,cumulativeBytes,currentTick,currentFlow,currentVersion,currentKills,currentWave\n");
            for (int i = 0; i < samples.Length; i++)
            {
                var sample = samples[i];
                output.Append(i).Append(',').Append(sample.ReadFrame).Append(',').Append(sample.GovernorFrames)
                    .Append(',').Append(sample.PreviousFrameBytes).Append(',').Append(sample.CumulativeBytes)
                    .Append(',').Append(sample.Tick).Append(',').Append(sample.Flow).Append(',').Append(sample.Version)
                    .Append(',').Append(sample.Kills).Append(',').Append(sample.Wave).Append('\n');
            }
            string dir = Path.Combine(Application.dataPath, "..", "Artifacts");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, $"shooter-steady-frame-samples-{tier}.csv"), output.ToString());
        }
        [UnityTest]
        public IEnumerator PortraitFlowAndCancelOnBothTiers([Values(RenderTier.GpuDriven,RenderTier.DataTexture)] RenderTier tier)
        {
            CheckGraphics(tier);
            var config=ShooterConfig.CreateDefault();config.Settings.Waves=2;config.Settings.EnemiesPerWave=2;config.Settings.EnemyHp=4f;config.Settings.HeroHp=10000;
            var game=ShooterGameBootstrap.Create(tier,config);
            try
            {
                yield return null;yield return null;
                Assert.AreEqual(tier,game.Renderer.Tier);
                UIDriver.Click(game.Hud.StartButton.gameObject);
                yield return UIDriver.WaitUntil(()=>game.State.Run.Flow==ShooterFlow.Upgrade,5f);
                Assert.AreEqual(3,game.Hud.ChoiceButtons.Length);
                yield return Capture(game,"upgrade",tier);
                UIDriver.Click(game.Hud.ChoiceButtons[0].gameObject);
                yield return UIDriver.WaitUntil(()=>game.State.Run.Flow==ShooterFlow.Playing,5f);
                Assert.AreEqual(1,game.State.Run.Beam);
                // Controlled visual fixture: all enemies are still killed through normal simulation/bot play.
                // Keeps ray, wing partner and both pickup types visible instead of capturing between waves.
                game.Session.Sync();
                ShooterSpawner.Enemy(game.Session.World,new float2(0f,-1.2f),2,hp:180f,speed:0.4f);
                ShooterSpawner.Enemy(game.Session.World,new float2(-2.8f,2.5f),1,hp:35f,speed:0.6f);
                ShooterSpawner.Enemy(game.Session.World,new float2(2.8f,4f),0,hp:35f,speed:0.6f);
                ShooterSpawner.Pickup(game.Session.World,new float2(-2.8f,-2.3f),true);
                ShooterSpawner.Pickup(game.Session.World,new float2(2.8f,-2.3f),false);
                // A second pointer cannot steal the flight control. Explicit cancel discards pending movement.
                var pad=game.Hud.DragPad;
                var primary=new PointerEventData(EventSystem.current){pointerId=11,position=new Vector2(200,300),delta=new Vector2(40,20)};
                var other=new PointerEventData(EventSystem.current){pointerId=12,position=new Vector2(250,300),delta=new Vector2(-500,0)};
                pad.OnPointerDown(primary);pad.OnPointerDown(other);pad.OnDrag(other);Assert.AreEqual(float2.zero,game.State.Run.Drag);
                pad.OnDrag(primary);Assert.Greater(math.lengthsq(game.State.Run.Drag),0f);
                pad.OnCancel(new BaseEventData(EventSystem.current));Assert.IsFalse(pad.Captured);Assert.AreEqual(float2.zero,game.State.Run.Drag);
                pad.OnDrag(primary);Assert.AreEqual(float2.zero,game.State.Run.Drag,"old pointer cannot resume after cancel");
                pad.OnPointerDown(primary);pad.OnDrag(primary);
                pad.ReleasePointer(other.pointerId,true);Assert.IsTrue(pad.Captured);Assert.Greater(math.lengthsq(game.State.Run.Drag),0f,"foreign cancellation does not steal the captured finger");
                pad.ReleasePointer(primary.pointerId,false);Assert.IsFalse(pad.Captured);Assert.Greater(math.lengthsq(game.State.Run.Drag),0f,"normal release preserves the last valid drag for the next simulation tick");
                pad.CancelPointer();
                pad.OnPointerDown(primary);pad.OnDrag(primary);pad.ReleasePointer(primary.pointerId,true);
                Assert.IsFalse(pad.Captured);Assert.AreEqual(float2.zero,game.State.Run.Drag,"native canceled release discards pending displacement");
                pad.OnPointerDown(primary);pad.OnDrag(primary);pad.enabled=false;Assert.IsFalse(pad.Captured);Assert.AreEqual(float2.zero,game.State.Run.Drag);pad.enabled=true;
                game.AutoPlay=true;
                yield return UIDriver.WaitUntil(()=>game.State.Run.BeamTargetId>0,5f);
                Assert.Greater(game.Renderer.SpritesDrawn,20);Assert.Greater(game.Renderer.BytesUploaded,0);
                yield return Capture(game,"playing",tier);
                yield return UIDriver.WaitUntil(()=>game.State.Run.Kills>0,20f);
                yield return UIDriver.WaitUntil(()=>game.State.Run.Flow==ShooterFlow.Won,35f);
                yield return null;Assert.IsTrue(game.Hud.ResultPanel.gameObject.activeInHierarchy);
                game.AutoPlay=false;
                UIDriver.Click(game.Hud.RestartButton.gameObject);
                yield return UIDriver.WaitUntil(()=>game.State.Run.Flow==ShooterFlow.Upgrade,5f);
                UIDriver.Click(game.Hud.ChoiceButtons[1].gameObject);
                yield return UIDriver.WaitUntil(()=>game.State.Run.Flow==ShooterFlow.Playing,5f);
                game.Session.Sync();game.State.Run.Hp=0f;
                yield return UIDriver.WaitUntil(()=>game.State.Run.Flow==ShooterFlow.Dead,5f);
                yield return null;Assert.IsTrue(game.Hud.RestartButton.gameObject.activeInHierarchy);
                UIDriver.Click(game.Hud.MenuButton.gameObject);
                yield return UIDriver.WaitUntil(()=>game.State.Run.Flow==ShooterFlow.Menu,5f);
                yield return null;Assert.IsTrue(game.Hud.StartButton.gameObject.activeInHierarchy);
            }
            finally { if(Camera.main!=null)Object.Destroy(Camera.main.gameObject);Object.Destroy(game.gameObject);Object.Destroy(config); }
        }
        [UnityTest]
        public IEnumerator WarmSteadyFrameAndPresentationOnlyQuality([Values(RenderTier.GpuDriven,RenderTier.DataTexture)] RenderTier tier)
        {
            CheckGraphics(tier);var config=ShooterConfig.CreateDefault();config.Settings.SpawnWaves=false;config.Settings.HeroHp=100000;config.Settings.EnemyFireInterval=100000;
            var game=ShooterGameBootstrap.Create(tier,config);
            var samples = new SteadyFrameSample[180]; // Before warmup; no allocation in the measured loop.
            try
            {
                yield return null;game.StartRun();yield return UIDriver.WaitUntil(()=>game.State.Run.Flow==ShooterFlow.Upgrade,5f);game.Choose(0);
                yield return UIDriver.WaitUntil(()=>game.State.Run.Flow==ShooterFlow.Playing,5f);
                game.Session.Sync();
                for(int i=0;i<80;i++)ShooterSpawner.Enemy(game.Session.World,new float2((i%10)*0.8f-3.6f,1+(i/10)*0.8f),hp:100000,speed:0);
                byte[] before=game.Session.CaptureSnapshot();game.Renderer.SetQuality(3);byte[] after=game.Session.CaptureSnapshot();CollectionAssert.AreEqual(before,after,"quality is presentation only");Assert.AreEqual(0,game.Renderer.ShadowBudget);game.Renderer.SetQuality(0);
                for(int i=0;i<150;i++)yield return null;
                game.Governor.ResetGcStats();
                for(int i=0;i<180;i++)
                {
                    yield return null;
                    // LastValue describes the governor's previous-frame sample. Current Tick/flow
                    // provide observation context, not allocation callstack attribution.
                    samples[i] = new SteadyFrameSample {
                        ReadFrame = Time.frameCount, GovernorFrames = game.Governor.FramesSinceReset,
                        PreviousFrameBytes = game.Governor.GcBytesLastFrame,
                        CumulativeBytes = game.Governor.GcBytesSinceReset,
                        Tick = game.Session.Pipeline.Stats.TickCount,
                        Flow = (int)game.State.Run.Flow, Version = game.State.Run.Version,
                        Kills = game.State.Run.Kills, Wave = game.State.Run.Wave
                    };
                }
                WriteSteadySamples(tier, samples); // All formatting/IO occurs after the unchanged window.
                if(game.Governor.GcCounterValid)
                {
                    GcReport.Write($"shooter steady ({tier})",game.Governor.FramesSinceReset,game.Governor.GcFramesSinceReset,game.Governor.GcBytesSinceReset);
                    Assert.LessOrEqual(game.Governor.GcFramesSinceReset,2,"same strict steady-frame budget as existing play validation");
                }
                else TestContext.WriteLine("GC profiler counter unavailable: no engine-frame allocation claim.");
            }
            finally { if(Camera.main!=null)Object.Destroy(Camera.main.gameObject);Object.Destroy(game.gameObject);Object.Destroy(config); }
        }
        static void CheckGraphics(RenderTier tier)
        {
            if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null)Assert.Ignore("No graphics device");
            if(tier==RenderTier.GpuDriven && (!SystemInfo.supportsComputeShaders || SystemInfo.maxComputeBufferInputsVertex<4))Assert.Ignore("GPU tier unavailable");
        }
        static int TelemetryWhite(Color32[] pixels)
        {
            int count=0;
            for(int y=860;y<925;y++)for(int x=35;x<505;x++)
            {
                var c=pixels[y*540+x];if(c.r>185 && c.g>185 && c.b>185)count++;
            }
            return count;
        }
        static IEnumerator Capture(ShooterGameBootstrap game,string phase,RenderTier tier)
        {
            var camera=game.CameraRig.Camera;var canvas=game.Hud.Canvas;var oldMode=canvas.renderMode;var oldCamera=canvas.worldCamera;
            var target=new RenderTexture(540,960,24,RenderTextureFormat.ARGB32);var read=new Texture2D(540,960,TextureFormat.RGBA32,false);
            try
            {
                camera.targetTexture=target;canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=2f;
                yield return null;yield return null;
                var previous=RenderTexture.active;RenderTexture.active=target;read.ReadPixels(new Rect(0,0,540,960),0,0);read.Apply(false);RenderTexture.active=previous;
                int bright=0;var pixels=read.GetPixels32();for(int i=0;i<pixels.Length;i+=7)if(pixels[i].g>110 || pixels[i].r>180)bright++;
                Assert.Greater(bright,100,"portrait contains visible aircraft/HUD instead of an empty render");
                if(phase=="playing")
                {
                    Assert.Greater(TelemetryWhite(pixels),30,"wave/hull/salvage text must actually render, not just exist in a text buffer");
                    int beamPixels=0;
                    for(int y=230;y<360;y++)for(int x=260;x<281;x++)
                    {
                        var c=pixels[y*540+x];if(c.r>180 && c.g>100 && c.g<230 && c.b<140)beamPixels++;
                    }
                    Assert.Greater(beamPixels,80,"the active nearest-target ray must be visible below the fixture target");
                }
                string dir=Path.Combine(Application.dataPath,"..","Artifacts","Screenshots");Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir,$"shooter-{phase}-{(tier==RenderTier.GpuDriven?"gpu":"datatex")}.png"),read.EncodeToPNG());
            }
            finally { camera.targetTexture=null;canvas.renderMode=oldMode;canvas.worldCamera=oldCamera;target.Release();Object.Destroy(target);Object.Destroy(read); }
        }
    }
}
