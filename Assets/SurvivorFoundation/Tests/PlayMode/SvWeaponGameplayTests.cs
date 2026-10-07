#if !SPF_DOTNET_HARNESS
using System;
using System.Collections;
using NUnit.Framework;
using SPF.Contracts;
using SPF.L2.Weapons;
using SPF.Presentation;
using SPF.Testing;
using SurvivorFoundation.Game;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;

namespace SurvivorFoundation.Tests.PlayMode
{
    public class SvWeaponGameplayTests
    {
        [UnityTest]
        public IEnumerator FourWeaponsDrivePortraitHordeAndKeepSkillHud([Values(RenderTier.GpuDriven,RenderTier.DataTexture)] RenderTier tier)
        {
            if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null)Assert.Ignore("Actual graphics required.");
            if(tier==RenderTier.GpuDriven&&!SystemInfo.supportsComputeShaders)Assert.Ignore("Compute tier unsupported.");
            RenderCapabilities.Override=tier;var config=SvConfig.CreateWeaponCombatExample();config.Settings.SpawnPerSecond=config.Settings.SpawnGrowth=config.Settings.EliteEvery=0;config.Settings.XpBase=100000;config.Settings.BeaconHp=100000;config.Settings.HeroHp=100000;
            foreach(var e in config.Enemies){e.Hp=10000;e.Speed=0;e.Damage=0;e.Shooter=false;}
            var game=SvGameBootstrap.CreateWeaponCombatExample(config);CanvasCapture capture=null;string suffix=tier==RenderTier.GpuDriven?"gpu":"fallback";
            try
            {
                yield return null;game.StartRun();yield return UIDriver.WaitUntil(()=>game.State.Flow==SvFlow.Playing,5);game.Session.Sync();game.Session.ManualClock=true;game.InputRouter.enabled=false;game.Governor.AdaptiveQuality=false;
                // Manual Step advances tick index, not the automatic accumulator. Exact marker captures require alpha zero.
                game.Session.Clock.Restore(game.Session.Clock.NextTickIndex,game.Session.Clock.Elapsed);
                var world=game.Session.World;var weapons=world.Resource(SvWeapons.Key);var runtime=world.Resource(SvKeys.Config);
                capture=new CanvasCapture(game.gameObject,game.CameraRig.Camera,720,1280);var safe=new Rect(0,0,720,1280);game.Hud.MobileHud.SetPreviewViewport(720,1280,safe);game.CameraRig.Snap();Assert.AreEqual(4,game.Hud.MobileHud.Buttons.Length);
                foreach(int id in new[]{WeaponProfiles.Blade,WeaponProfiles.Sword,WeaponProfiles.Staff,WeaponProfiles.Bow})for(int side=-1;side<=1;side+=2)
                {
                    world.ClearLevel();game.State.Flow=SvFlow.Playing;game.State.Hero=game.State.HeroPrev=0;game.State.Facing=new float2(side,0);game.State.Input=default;weapons.RequestEquip(id);for(int i=0;i<weapons.Profile(id).EquipTicks;i++)game.Session.Step();
                    SvSpawner.SpawnEnemy(world,runtime,1,new float2(side*(id>=WeaponProfiles.Staff?5:1),0));
                    int marker=weapons.Current.Ranged?weapons.Current.ReleaseTick:weapons.Current.Active.From;
                    // Auto-target acquisition uses the existing grid; wait for the real action and its exact sampled marker.
                    for(int i=0;i<weapons.Current.DurationTicks*2&&(weapons.Equipment.Timeline.PulseId==0||weapons.Equipment.Timeline.Tick<marker+1);i++)game.Session.Step();
                    Assert.IsTrue(weapons.Equipment.Timeline.Running);
                    var sampled=weapons.View(game.Session.InterpolationAlpha);
                    Assert.AreEqual((float)marker/weapons.Current.DurationTicks,sampled.Phase,.00001f,"fixture must sample the authored contact/release marker");
                    TestContext.WriteLine($"Horde weapon {id}, facing {side}: ticks {weapons.Equipment.Timeline.PreviousTick}/{weapons.Equipment.Timeline.Tick}, alpha {game.Session.InterpolationAlpha:R}, sampled phase {sampled.Phase:R}, marker {(float)marker/weapons.Current.DurationTicks:R}");
                    yield return capture.Save("weapon-horde-"+id+"-"+(side<0?"left-":"right-")+suffix,safe,game.Hud.MobileHud.Buttons[3].gameObject);
                    Assert.IsTrue(game.Renderer.Characters.TryReadWeapon(new EntityHandle(-1,1),out var socket));Assert.AreEqual(id,socket.VisualId);
                    float2 canonical=game.State.Hero+new float2(side*weapons.Current.MuzzleOffset.x,weapons.Current.MuzzleOffset.y)*SvWeapons.ActorScale;
                    float socketError=math.distance(canonical,socket.Muzzle);
                    TestContext.WriteLine($"Horde weapon {id}, facing {side}: native socket error {socketError:R}, muzzle ({socket.Muzzle.x:R},{socket.Muzzle.y:R}), canonical ({canonical.x:R},{canonical.y:R})");
                    Assert.Less(socketError,.17f);
                    for(int i=0;i<35;i++)game.Session.Step();Assert.Greater(weapons.AcceptedHits,0);Assert.AreEqual(0,world.Table(SvKeys.Bullet).Count,"classic hero bolt is disabled only for this explicit variant");
                }
                yield return null;game.Session.Pause();uint consumed=game.Renderer.LastWeaponCueSequence;Assert.Greater(consumed,0);
                game.Renderer.NaturalCharacters=false;yield return null;yield return null;game.Session.Resume();
                for(int i=0;i<weapons.Current.DurationTicks+weapons.Current.ReleaseTick+2;i++)game.Session.Step();
                Assert.Greater(weapons.Equipment.CueSequence,consumed);game.Session.Pause();game.Renderer.NaturalCharacters=true;yield return null;yield return null;
                Assert.AreEqual(weapons.Equipment.CueSequence,game.Renderer.LastWeaponCueSequence);Assert.AreEqual(0,game.Renderer.WeaponParticles.Renderer.Pool.ReservedCount,"rebuilding the view must not replay retained bursts from the hidden interval");game.Session.Resume();
                uint baseline=game.Renderer.LastWeaponCueSequence;bool observedNewBurst=false;uint releaseSequence=0;
                game.State.Input=default;
                for(int tick=0;tick<weapons.Current.DurationTicks+weapons.Current.ReleaseTick+3;tick++)
                {
                    game.Session.Step();
                    for(int c=0;c<weapons.CueCount;c++){var cue=weapons.Cues[c];if(cue.Kind==SPF.Contracts.Weapons.WeaponCueKind.Release&&cue.Sequence>baseline)releaseSequence=cue.Sequence;}
                    yield return null;
                    observedNewBurst|=releaseSequence!=0&&game.Renderer.LastWeaponCueSequence>=releaseSequence&&game.Renderer.WeaponParticles.Renderer.Pool.SpawnCount>0;
                }
                game.State.Input=default;Assert.IsTrue(observedNewBurst,"new releases after binding still produce their sequenced burst");
                game.State.Input=new InputFrame{Pressed=1};game.Session.Step();game.State.Input=default;game.Session.Step();
                Assert.AreEqual(SvWeapons.PulsePose,world.Resource(SvWeapons.PoseKey).ContentId);yield return capture.Save("weapon-horde-skill-pulse-"+suffix,safe);
                game.State.Input=new InputFrame{Pressed=2,Aim=new float2(1,0)};game.Session.Step();game.State.Input=default;game.Session.Step();
                Assert.AreEqual(SvWeapons.BlinkPose,world.Resource(SvWeapons.PoseKey).ContentId);yield return capture.Save("weapon-horde-skill-blink-"+suffix,safe);
                Assert.AreEqual(1,world.Resource(SvMobileSkills.Key).GetSnapshot(0).Charges);Assert.AreEqual(1,world.Resource(SvMobileSkills.Key).GetSnapshot(1).Charges);
                byte[] before=game.Session.CaptureSnapshot();for(int i=0;i<6;i++)game.Renderer.RenderFrame();CollectionAssert.AreEqual(before,game.Session.CaptureSnapshot());Assert.IsNotNull(game.Renderer.WeaponParticles);
                game.Session.Pause();game.Renderer.RenderFrame();Assert.IsTrue(game.Renderer.Characters.TryReadWeapon(new EntityHandle(-1,1),out var pausedSocket));
                yield return null;yield return null;game.Renderer.RenderFrame();Assert.IsTrue(game.Renderer.Characters.TryReadWeapon(new EntityHandle(-1,1),out var heldSocket));
                Assert.Less(math.distance(pausedSocket.Muzzle,heldSocket.Muzzle),.0001f,"paused interpolation must not oscillate a skill or held weapon");game.Session.Resume();
                if(Environment.GetEnvironmentVariable("SPF_WEAPON_GAMEPLAY_SEQUENCE")=="1")
                {
                    capture.Dispose();capture=new CanvasCapture(game.gameObject,game.CameraRig.Camera,360,640);game.Hud.MobileHud.SetPreviewViewport(360,640,new Rect(0,0,360,640));world.ClearLevel();game.State.Flow=SvFlow.Playing;game.State.Hero=game.State.HeroPrev=0;weapons.RequestEquip(WeaponProfiles.Staff);
                    for(int i=0;i<weapons.Profile(WeaponProfiles.Staff).EquipTicks;i++)game.Session.Step();for(int i=0;i<24;i++){float a=i*2.399963f;SvSpawner.SpawnEnemy(world,runtime,1,new float2(math.cos(a),math.sin(a))*(4+i*.08f));}
                    Canvas.ForceUpdateCanvases();yield return null;yield return null;
                    using(var frames=new BufferedFrameCapture(capture.Target,90))
                    {
                        game.Session.ManualClock=false;double next=Time.realtimeSinceStartupAsDouble;
                        for(int i=0;i<90;i++)
                        {
                            while(Time.realtimeSinceStartupAsDouble<next)yield return null;
                            game.State.Input=InputFrame.Latch(game.State.Input,new InputFrame{Move=i<40?new float2(.8f,.6f):new float2(-.3f,-.2f),Pressed=i==40?1u<<SvWeapons.SwitchButton:0});
                            double acquiredAt=Time.realtimeSinceStartupAsDouble;frames.Capture(weapons.Tick/30d);next=acquiredAt+1d/30;
                        }
                        game.Session.ManualClock=true;frames.Write("weapon-horde-live-"+suffix,"Actual automatic-clock portrait horde: run/walk, pulse/blink in contact captures, staff charge/projectile, switch to bow draw/release while independent pulse/blink HUD remains. Target30Hz, measured timestamps retained.");
                    }
                }
                LogAssert.NoUnexpectedReceived();
            }
            finally{capture?.Dispose();RenderCapabilities.Override=null;if(Camera.main!=null)Object.Destroy(Camera.main.gameObject);Object.Destroy(game.gameObject);Object.Destroy(config);}
        }
    }
}
#endif
