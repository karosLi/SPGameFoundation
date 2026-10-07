#if !SPF_DOTNET_HARNESS
using System;
using System.Collections;
using BrawlerFoundation.Game;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Contracts.Weapons;
using SPF.L2.Weapons;
using SPF.Presentation;
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
                capture=new CanvasCapture(game.gameObject,game.CameraRig.Camera,1280,720);var safe=new Rect(0,0,1280,720);game.MobileHud.SetPreviewViewport(1280,720,safe);game.CameraRig.Snap();
                var world=game.Session.World;var weapons=world.Resource(BwWeapons.Key);
                foreach(int id in new[]{WeaponProfiles.Blade,WeaponProfiles.Sword,WeaponProfiles.Staff,WeaponProfiles.Bow})for(int side=-1;side<=1;side+=2)
                {
                    world.ClearLevel();game.State.Flow=BwFlow.Fighting;game.State.Input=default;
                    BwSpawner.Spawn(world,0,0,side,0);BwSpawner.Spawn(world,1,new float2(side*(id>=WeaponProfiles.Staff?5:1.2f),0),-side,0);var info=world.Column(BwKeys.Info);var target=info[1];target.Hp=target.MaxHp=1000;info[1]=target;
                    weapons.RequestEquip(id);for(int i=0;i<weapons.Profile(id).EquipTicks;i++)game.Session.Step();
                    game.State.Input=new InputFrame{Pressed=1};game.Session.Step();game.State.Input=default;for(int i=0;i<weapons.Current.Active.From+1;i++)game.Session.Step();
                    yield return capture.Save("weapon-belt-"+id+"-"+(side<0?"left-":"right-")+suffix,safe,game.SwitchWeaponButton.gameObject);
                    Assert.IsTrue(game.Renderer.Characters.TryReadWeapon(weapons.Owner,out var socket));Assert.AreEqual(id,socket.VisualId);
                    float2 root=world.Column(BwKeys.Position)[0];float2 canonical=root+new float2(side*weapons.Current.MuzzleOffset.x,weapons.Current.MuzzleOffset.y)*BwWeapons.ActorScale;
                    Assert.Less(math.distance(canonical,socket.Muzzle),.17f,"contact/release socket follows the same authored muzzle used by simulation");
                    for(int i=0;i<60;i++)game.Session.Step();Assert.Less(info[1].Hp,1000,"equipped weapon must hit the actual enemy");
                }
                // A third-party Button click never releases the joystick's independent pointer.
                game.MobileHud.Refresh();var pointer=new PointerEventData(EventSystem.current){pointerId=77,position=new Vector2(100,130)};game.Joystick.OnPointerDown(pointer);
                Assert.IsTrue(game.Joystick.Pressed);UIDriver.Click(game.SwitchWeaponButton.gameObject);Assert.IsTrue(game.Joystick.Pressed);game.Joystick.OnPointerUp(pointer);game.Session.Step();
                Assert.AreNotEqual(0,weapons.Equipment.PendingId);Assert.IsNotNull(game.Renderer.WeaponParticles);
                byte[] before=game.Session.CaptureSnapshot();for(int i=0;i<6;i++)game.Renderer.RenderFrame();CollectionAssert.AreEqual(before,game.Session.CaptureSnapshot(),"repeated rendering cannot change weapon or hit state");
                if(Environment.GetEnvironmentVariable("SPF_WEAPON_GAMEPLAY_SEQUENCE")=="1")
                {
                    capture.Dispose();capture=new CanvasCapture(game.gameObject,game.CameraRig.Camera,640,360);game.MobileHud.SetPreviewViewport(640,360,new Rect(0,0,640,360));
                    world.ClearLevel();game.State.Flow=BwFlow.Fighting;BwSpawner.Spawn(world,0,new float2(-2,0),1,0);BwSpawner.Spawn(world,1,new float2(4,0),-1,1);var target=world.Column(BwKeys.Info)[1];target.Hp=target.MaxHp=10000;world.Column(BwKeys.Info).Set(1,target);
                    weapons.RequestEquip(WeaponProfiles.Bow);for(int i=0;i<weapons.Profile(WeaponProfiles.Bow).EquipTicks;i++)game.Session.Step();
                    using(var frames=new BufferedFrameCapture(capture.Target,90))
                    {
                        game.Session.ManualClock=false;double next=Time.realtimeSinceStartupAsDouble;
                        for(int i=0;i<90;i++)
                        {
                            while(Time.realtimeSinceStartupAsDouble<next)yield return null;
                            game.State.Input=InputFrame.Latch(game.State.Input,new InputFrame{Move=i<30?new float2(.3f,.25f):i<60?new float2(-.3f,-.25f):float2.zero,Held=1,Pressed=i==44?1u<<BwWeapons.SwitchButton:0});
                            frames.Capture(weapons.Tick/60d);next=Time.realtimeSinceStartupAsDouble+1d/30;
                        }
                        game.Session.ManualClock=true;frames.Write("weapon-belt-live-"+suffix,"Actual automatic-clock belt gameplay: move, bow draw/release, switch into blade, continuing attacks. Target30Hz, measured acquisition timestamps retained.");
                    }
                }
                LogAssert.NoUnexpectedReceived();
            }
            finally{capture?.Dispose();RenderCapabilities.Override=null;if(Camera.main!=null)Object.Destroy(Camera.main.gameObject);Object.Destroy(game.gameObject);}
        }
    }
}
#endif
