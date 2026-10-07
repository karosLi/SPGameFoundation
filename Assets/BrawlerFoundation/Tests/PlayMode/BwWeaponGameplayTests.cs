#if !SPF_DOTNET_HARNESS
using System;
using System.Collections;
using BrawlerFoundation.Game;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Contracts.Weapons;
using SPF.L2.Weapons;
using SPF.Presentation;
using SPF.Shell.UI;
using SPF.Testing;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;

namespace BrawlerFoundation.Tests.PlayMode
{
    public class BwWeaponGameplayTests
    {
        [UnityTest]
        public IEnumerator FourWeaponsUseActualBeltLoopAndSockets([Values(RenderTier.GpuDriven,RenderTier.DataTexture)] RenderTier tier)
        {
            if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null)Assert.Ignore("Actual graphics required.");
            if(tier==RenderTier.GpuDriven&&!SystemInfo.supportsComputeShaders)Assert.Ignore("Compute tier unsupported.");
            RenderCapabilities.Override=tier;var game=BwGameBootstrap.CreateWeaponBelt();CanvasCapture capture=null;string suffix=tier==RenderTier.GpuDriven?"gpu":"fallback";
            try
            {
                yield return null;UIDriver.Click(game.StartButton.gameObject);yield return UIDriver.WaitUntil(()=>game.State.Flow==BwFlow.Fighting,5);
                game.Session.Sync();game.Session.ManualClock=true;game.InputRouter.enabled=false;game.Governor.AdaptiveQuality=false;
                // Manual Step advances tick index, not the automatic accumulator. Exact marker captures require alpha zero.
                game.Session.Clock.Restore(game.Session.Clock.NextTickIndex,game.Session.Clock.Elapsed);
                capture=new CanvasCapture(game.gameObject,game.CameraRig.Camera,1280,720);var safe=new Rect(0,0,1280,720);game.MobileHud.SetPreviewViewport(1280,720,safe);game.CameraRig.Snap();
                var world=game.Session.World;var weapons=world.Resource(BwWeapons.Key);
                foreach(int id in new[]{WeaponProfiles.Blade,WeaponProfiles.Sword,WeaponProfiles.Staff,WeaponProfiles.Bow})for(int side=-1;side<=1;side+=2)
                {
                    world.ClearLevel();game.State.Flow=BwFlow.Fighting;game.State.Input=default;
                    BwSpawner.Spawn(world,0,0,side,0);BwSpawner.Spawn(world,1,new float2(side*(id>=WeaponProfiles.Staff?5:1.2f),0),-side,0);var info=world.Column(BwKeys.Info);var target=info[1];target.Hp=target.MaxHp=1000;info[1]=target;
                    weapons.RequestEquip(id);for(int i=0;i<weapons.Profile(id).EquipTicks;i++)game.Session.Step();
                    byte[] beforeHud=game.Session.CaptureSnapshot();game.MobileHud.Refresh();
                    int attackGlyph=id==WeaponProfiles.Bow?CombatControlGraphic.BowGlyph:id==WeaponProfiles.Staff?CombatControlGraphic.StaffGlyph:CombatControlGraphic.BladeGlyph;
                    Assert.AreEqual(attackGlyph,game.MobileHud.Buttons[0].transform.Find("Icon").GetComponent<CombatControlGraphic>().Glyph);
                    Assert.AreEqual(CombatControlGraphic.KickGlyph,game.MobileHud.Buttons[1].transform.Find("Icon").GetComponent<CombatControlGraphic>().Glyph);
                    Assert.AreEqual(CombatControlGraphic.JumpGlyph,game.MobileHud.Buttons[2].transform.Find("Icon").GetComponent<CombatControlGraphic>().Glyph);
                    Assert.AreEqual(CombatControlGraphic.HealGlyph,game.MobileHud.Buttons[3].transform.Find("Icon").GetComponent<CombatControlGraphic>().Glyph);
                    for(int slot=0;slot<4;slot++)Assert.AreEqual(world.Resource(BwMobileSkills.Key).GetSnapshot(slot).Definition,game.MobileHud.Buttons[slot].Snapshot.Definition,"fallback art must preserve authored asset keys and skill rules");
                    CollectionAssert.AreEqual(beforeHud,game.Session.CaptureSnapshot(),"HUD refresh must not rewrite persisted skill definitions or equipment state");
                    game.State.Input=new InputFrame{Pressed=1};game.Session.Step();game.State.Input=default;
                    int marker=weapons.Current.Ranged?weapons.Current.ReleaseTick:weapons.Current.Active.From;
                    for(int i=0;i<marker+1;i++)game.Session.Step();
                    var sampled=weapons.View(game.Session.InterpolationAlpha);
                    Assert.AreEqual((float)marker/weapons.Current.DurationTicks,sampled.Phase,.00001f,"fixture must sample the authored contact/release marker");
                    TestContext.WriteLine($"Belt weapon {id}, facing {side}: ticks {weapons.Equipment.Timeline.PreviousTick}/{weapons.Equipment.Timeline.Tick}, alpha {game.Session.InterpolationAlpha:R}, sampled phase {sampled.Phase:R}, marker {(float)marker/weapons.Current.DurationTicks:R}");
                    yield return capture.Save("weapon-belt-"+id+"-"+(side<0?"left-":"right-")+suffix,safe,game.SwitchWeaponButton.gameObject);
                    Assert.IsTrue(game.Renderer.Characters.TryReadWeapon(weapons.Owner,out var socket));Assert.AreEqual(id,socket.VisualId);
                    float2 root=world.Column(BwKeys.Position)[0];float2 canonical=root+new float2(side*weapons.Current.MuzzleOffset.x,weapons.Current.MuzzleOffset.y)*BwWeapons.ActorScale;
                    float socketError=math.distance(canonical,socket.Muzzle);
                    TestContext.WriteLine($"Belt weapon {id}, facing {side}: native socket error {socketError:R}, muzzle ({socket.Muzzle.x:R},{socket.Muzzle.y:R}), canonical ({canonical.x:R},{canonical.y:R})");
                    Assert.Less(socketError,.17f,"contact/release socket follows the same authored muzzle used by simulation");
                    for(int i=0;i<60;i++)game.Session.Step();Assert.Less(info[1].Hp,1000,"equipped weapon must hit the actual enemy");
                }
                yield return null;game.Session.Pause();uint consumed=game.Renderer.LastWeaponCueSequence;Assert.Greater(consumed,0);
                game.Renderer.NaturalCharacters=false;yield return null;yield return null;game.Session.Resume();game.State.Input=new InputFrame{Held=1};
                for(int i=0;i<weapons.Current.DurationTicks+weapons.Current.ReleaseTick+2;i++)game.Session.Step();game.State.Input=default;
                Assert.Greater(weapons.Equipment.CueSequence,consumed);game.Session.Pause();game.Renderer.NaturalCharacters=true;yield return null;yield return null;
                Assert.AreEqual(weapons.Equipment.CueSequence,game.Renderer.LastWeaponCueSequence);Assert.AreEqual(0,game.Renderer.WeaponParticles.Renderer.Pool.ReservedCount,"rebuilding the view must not replay retained bursts from the hidden interval");game.Session.Resume();
                uint baseline=game.Renderer.LastWeaponCueSequence;bool observedNewBurst=false;uint releaseSequence=0;
                game.State.Input=new InputFrame{Held=1};
                for(int tick=0;tick<weapons.Current.DurationTicks+weapons.Current.ReleaseTick+3;tick++)
                {
                    game.Session.Step();
                    for(int c=0;c<weapons.CueCount;c++){var cue=weapons.Cues[c];if(cue.Kind==SPF.Contracts.Weapons.WeaponCueKind.Release&&cue.Sequence>baseline)releaseSequence=cue.Sequence;}
                    yield return null;
                    observedNewBurst|=releaseSequence!=0&&game.Renderer.LastWeaponCueSequence>=releaseSequence&&game.Renderer.WeaponParticles.Renderer.Pool.SpawnCount>0;
                }
                game.State.Input=default;Assert.IsTrue(observedNewBurst,"new releases after binding still produce their sequenced burst");
                world.ClearLevel();game.State.Flow=BwFlow.Fighting;game.State.Input=default;BwSpawner.Spawn(world,0,0,1,0);BwSpawner.Spawn(world,1,new float2(8,0),-1,0);
                foreach(int button in new[]{BwButton.Kick,BwBeltRules.JumpButton,BwBeltRules.HealButton})
                {
                    if(button==BwBeltRules.HealButton){var injured=world.Column(BwKeys.Info)[0];injured.Hp=60;world.Column(BwKeys.Info).Set(0,injured);}
                    game.State.Input=new InputFrame{Pressed=1u<<button};game.Session.Step();game.State.Input=default;for(int i=0;i<5;i++)game.Session.Step();
                    int expected=button==BwButton.Kick?BwWeapons.KickPose:button==BwBeltRules.JumpButton?BwWeapons.JumpPose:BwWeapons.HealPose;
                    Assert.AreEqual(expected,world.Resource(BwWeapons.PoseKey).ContentId);Assert.IsTrue(world.Resource(BwWeapons.PoseKey).Running);
                    yield return capture.Save("weapon-belt-skill-"+expected+"-"+suffix,safe);
                    for(int i=0;i<65;i++)game.Session.Step();
                }
                // A third-party Button click never releases the joystick's independent pointer.
                game.MobileHud.Refresh();var pointer=new PointerEventData(EventSystem.current){pointerId=77,position=new Vector2(100,130)};game.Joystick.OnPointerDown(pointer);
                Assert.IsTrue(game.Joystick.Pressed);UIDriver.Click(game.SwitchWeaponButton.gameObject);Assert.IsTrue(game.Joystick.Pressed);game.Joystick.OnPointerUp(pointer);game.Session.Step();
                Assert.AreNotEqual(0,weapons.Equipment.PendingId);Assert.IsNotNull(game.Renderer.WeaponParticles);
                byte[] before=game.Session.CaptureSnapshot();for(int i=0;i<6;i++)game.Renderer.RenderFrame();CollectionAssert.AreEqual(before,game.Session.CaptureSnapshot(),"repeated rendering cannot change weapon or hit state");
                game.Session.Pause();game.Renderer.RenderFrame();Assert.IsTrue(game.Renderer.Characters.TryReadWeapon(weapons.Owner,out var pausedSocket));
                yield return null;yield return null;game.Renderer.RenderFrame();Assert.IsTrue(game.Renderer.Characters.TryReadWeapon(weapons.Owner,out var heldSocket));
                Assert.Less(math.distance(pausedSocket.Muzzle,heldSocket.Muzzle),.0001f,"paused interpolation must not oscillate the held weapon");game.Session.Resume();
                if(Environment.GetEnvironmentVariable("SPF_WEAPON_GAMEPLAY_SEQUENCE")=="1")
                {
                    capture.Dispose();capture=new CanvasCapture(game.gameObject,game.CameraRig.Camera,640,360);game.MobileHud.SetPreviewViewport(640,360,new Rect(0,0,640,360));
                    world.ClearLevel();game.State.Flow=BwFlow.Fighting;BwSpawner.Spawn(world,0,new float2(-2,0),1,0);BwSpawner.Spawn(world,1,new float2(4,0),-1,1);var target=world.Column(BwKeys.Info)[1];target.Hp=target.MaxHp=10000;world.Column(BwKeys.Info).Set(1,target);
                    weapons.RequestEquip(WeaponProfiles.Bow);for(int i=0;i<weapons.Profile(WeaponProfiles.Bow).EquipTicks;i++)game.Session.Step();
                    Canvas.ForceUpdateCanvases();yield return null;yield return null;
                    using(var frames=new BufferedFrameCapture(capture.Target,90))
                    {
                        game.Session.ManualClock=false;double next=Time.realtimeSinceStartupAsDouble;
                        for(int i=0;i<90;i++)
                        {
                            while(Time.realtimeSinceStartupAsDouble<next)yield return null;
                            game.State.Input=InputFrame.Latch(game.State.Input,new InputFrame{Move=i<30?new float2(1f,.5f):i<60?new float2(-.3f,-.25f):float2.zero,Held=1,Pressed=i==44?1u<<BwWeapons.SwitchButton:0});
                            double acquiredAt=Time.realtimeSinceStartupAsDouble;frames.Capture(weapons.Tick/60d);next=acquiredAt+1d/30;
                        }
                        game.Session.ManualClock=true;frames.Write("weapon-belt-live-"+suffix,"Actual automatic-clock belt gameplay: run, walk, idle, kick/jump/heal in contact captures, bow draw/release, switch into blade, continuing attacks. Target30Hz, measured acquisition timestamps retained.");
                    }
                }
                LogAssert.NoUnexpectedReceived();
            }
            finally{capture?.Dispose();RenderCapabilities.Override=null;if(Camera.main!=null)Object.Destroy(Camera.main.gameObject);Object.Destroy(game.gameObject);}
        }
    }
}
#endif
