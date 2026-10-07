#if !SPF_DOTNET_HARNESS
using System;
using System.Collections;
using NUnit.Framework;
using SPF.Contracts;
using SPF.L2.Weapons;
using SPF.Presentation;
using SPF.Presentation.Animation;
using SPF.Shell.UI;
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
                    world.ClearLevel();game.State.Flow=SvFlow.Playing;game.State.Hero=game.State.HeroPrev=0;game.State.Facing=new float2(side,0);game.State.Input=default;Equip(game,weapons,id);
                    byte[] beforeHud=game.Session.CaptureSnapshot();game.Hud.MobileHud.Refresh();
                    int attackGlyph=id==WeaponProfiles.Bow?CombatControlGraphic.BowGlyph:id==WeaponProfiles.Staff?CombatControlGraphic.StaffGlyph:CombatControlGraphic.BladeGlyph;
                    Assert.AreEqual(CombatControlGraphic.PulseGlyph,game.Hud.MobileHud.Buttons[0].transform.Find("Icon").GetComponent<CombatControlGraphic>().Glyph);
                    Assert.AreEqual(CombatControlGraphic.BlinkGlyph,game.Hud.MobileHud.Buttons[1].transform.Find("Icon").GetComponent<CombatControlGraphic>().Glyph);
                    Assert.AreEqual(attackGlyph,game.Hud.MobileHud.Buttons[2].transform.Find("Icon").GetComponent<CombatControlGraphic>().Glyph);
                    Assert.AreEqual(CombatControlGraphic.SwitchGlyph,game.Hud.MobileHud.Buttons[3].transform.Find("Icon").GetComponent<CombatControlGraphic>().Glyph);
                    for(int slot=0;slot<4;slot++)Assert.AreEqual(world.Resource(SvMobileSkills.Key).GetSnapshot(slot).Definition,game.Hud.MobileHud.Buttons[slot].Snapshot.Definition,"fallback art must preserve authored asset keys and skill rules");
                    CollectionAssert.AreEqual(beforeHud,game.Session.CaptureSnapshot(),"HUD refresh must not rewrite persisted skill definitions or equipment state");
                    SvSpawner.SpawnEnemy(world,runtime,1,new float2(side*(id>=WeaponProfiles.Staff?5:1),0));
                    int hitsBefore=weapons.AcceptedHits;float targetHp=world.Column(SvKeys.Info)[0].Hp;
                    int marker=weapons.Current.Ranged?weapons.Current.ReleaseTick:weapons.Current.Active.From;
                    // Stop retains the previous tick/pulse. Require a fresh auto-targeted action instead
                    // of mistaking an already completed action for this target's contact marker.
                    uint previousPulse=weapons.Equipment.Timeline.PulseId;
                    for(int i=0;i<weapons.Current.DurationTicks*2+2;i++)
                    {
                        game.Session.Step();var timeline=weapons.Equipment.Timeline;
                        if(timeline.Running&&timeline.PulseId!=previousPulse&&timeline.Tick==marker+1)break;
                    }
                    Assert.IsTrue(weapons.Equipment.Timeline.Running);
                    Assert.AreNotEqual(previousPulse,weapons.Equipment.Timeline.PulseId,"capture must observe a new attack against the current target");
                    var sampled=weapons.View(game.Session.InterpolationAlpha);
                    Assert.AreEqual((float)marker/weapons.Current.DurationTicks,sampled.Phase,.00001f,"fixture must sample the authored contact/release marker");
                    TestContext.WriteLine($"Horde weapon {id}, facing {side}: ticks {weapons.Equipment.Timeline.PreviousTick}/{weapons.Equipment.Timeline.Tick}, alpha {game.Session.InterpolationAlpha:R}, sampled phase {sampled.Phase:R}, marker {(float)marker/weapons.Current.DurationTicks:R}");
                    yield return capture.Save("weapon-horde-"+id+"-"+(side<0?"left-":"right-")+suffix,safe,game.Hud.MobileHud.Buttons[3].gameObject);
                    Assert.IsTrue(game.Renderer.Characters.TryReadWeapon(new EntityHandle(-1,1),out var socket));Assert.AreEqual(id,socket.VisualId);
                    float2 canonical=game.State.Hero+new float2(side*weapons.Current.MuzzleOffset.x,weapons.Current.MuzzleOffset.y)*SvWeapons.ActorScale;
                    float socketError=math.distance(canonical,socket.Muzzle);
                    TestContext.WriteLine($"Horde weapon {id}, facing {side}: native socket error {socketError:R}, muzzle ({socket.Muzzle.x:R},{socket.Muzzle.y:R}), canonical ({canonical.x:R},{canonical.y:R})");
                    Assert.Less(socketError,.17f);
                    for(int i=0;i<35;i++)game.Session.Step();Assert.Greater(weapons.AcceptedHits,hitsBefore,"this setup must resolve a new hit");Assert.Less(world.Column(SvKeys.Info)[0].Hp,targetHp,"the current target must actually take damage");Assert.AreEqual(0,world.Table(SvKeys.Bullet).Count,"classic hero bolt is disabled only for this explicit variant");
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
                    capture.Dispose();capture=new CanvasCapture(game.gameObject,game.CameraRig.Camera,360,640);game.Hud.MobileHud.SetPreviewViewport(360,640,new Rect(0,0,360,640));world.ClearLevel();game.State.Flow=SvFlow.Playing;game.State.Hero=game.State.HeroPrev=0;Equip(game,weapons,WeaponProfiles.Staff);
                    for(int i=0;i<24;i++){float a=i*2.399963f;SvSpawner.SpawnEnemy(world,runtime,1,new float2(math.cos(a),math.sin(a))*(4+i*.08f));}
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
                    // Move actual agile/heavy enemies for locomotion evidence; the weapon fixture
                    // above intentionally freezes enemies and cannot demonstrate their walking.
                    capture.Dispose();capture=new CanvasCapture(game.gameObject,game.CameraRig.Camera,320,568);game.Hud.MobileHud.SetPreviewViewport(320,568,new Rect(0,0,320,568));
                    world.ClearLevel();game.State.Flow=SvFlow.Playing;game.State.Hero=game.State.HeroPrev=0;game.State.Input=default;
                    for(int kind=0;kind<runtime.Enemies.Length;kind++){var enemy=runtime.Enemies[kind];enemy.Speed=kind==2?1.3f:kind==0?2.1f:1.7f;runtime.Enemies[kind]=enemy;}
                    SvSpawner.SpawnEnemy(world,runtime,0,new float2(3.5f,4));SvSpawner.SpawnEnemy(world,runtime,1,new float2(-3.5f,3.5f));SvSpawner.SpawnEnemy(world,runtime,2,new float2(0,-4));
                    Canvas.ForceUpdateCanvases();yield return null;yield return null;
                    using(var frames=new BufferedFrameCapture(capture.Target,160))
                    {
                        var trace=new GaitTrace();game.Session.ManualClock=false;double next=Time.realtimeSinceStartupAsDouble;
                        for(int i=0;i<160;i++)
                        {
                            while(Time.realtimeSinceStartupAsDouble<next)yield return null;
                            float2 move=i<40?new float2(.20f,.12f):i<80?new float2(.8f,.6f):i<100?float2.zero:i<135?new float2(-.2f,-.12f):new float2(0,.20f);
                            game.State.Input=InputFrame.Latch(game.State.Input,new InputFrame{Move=move});
                            double acquiredAt=Time.realtimeSinceStartupAsDouble;frames.Capture(weapons.Tick/30d);trace.Capture(i,0,acquiredAt,game.Renderer.Characters,new EntityHandle(-1,1));
                            var handles=world.Table(SvKeys.Enemy).Handles;for(int row=0;row<math.min(3,world.Table(SvKeys.Enemy).Count);row++)trace.Capture(i,row+1,acquiredAt,game.Renderer.Characters,handles[row]);
                            next=acquiredAt+1d/30;
                        }
                        game.Session.ManualClock=true;string directory=frames.Write("grounded-horde-live-"+suffix,"Actual automatic-clock hero plus moving agile, standard and heavy enemies: walk, run, stop, reverse and depth walk. Same camera/input schedule for before/after. Per-actor gait.csv is a readback annotation, not a manufactured render clock.");trace.Write(directory);
                    }
                }
                LogAssert.NoUnexpectedReceived();
            }
            finally{capture?.Dispose();RenderCapabilities.Override=null;if(Camera.main!=null)Object.Destroy(Camera.main.gameObject);Object.Destroy(game.gameObject);Object.Destroy(config);}
        }

        static void Equip(SvGameBootstrap game,WeaponRuntime weapons,int id)
        {
            Assert.IsTrue(weapons.RequestEquip(id));
            // A committed attack can delay the start of equip; EquipTicks alone is not a completion bound.
            int limit=weapons.Current.DurationTicks+weapons.Profile(id).EquipTicks+2;
            for(int i=0;i<limit&&(weapons.Equipment.EquippedId!=id||weapons.Equipment.PendingId!=0||weapons.Equipment.EquipRemaining!=0);i++)game.Session.Step();
            Assert.AreEqual(id,weapons.Equipment.EquippedId);
            Assert.AreEqual(0,weapons.Equipment.PendingId);
            Assert.AreEqual(0,weapons.Equipment.EquipRemaining);
        }

        // Test-only, fixed-capacity samples. Serialization happens after image acquisition.
        struct GaitSample
        {
            public int Frame,Actor;public double Time;public GameplayCharacterMotion Motion;public float Pelvis,Head;
        }
        sealed class GaitTrace
        {
            readonly GaitSample[] samples=new GaitSample[160*4];int count;
            public void Capture(int frame,int actor,double time,GameplayCharacterPresenter presenter,EntityHandle handle)
            {
                if(count==samples.Length||!presenter.TryRead(handle,out var motion))return;
                samples[count++]=new GaitSample {Frame=frame,Actor=actor,Time=time,Motion=motion,
                    Pelvis=presenter.ReadBone(handle,NaturalCharacterRig.Pelvis).Position.y,
                    Head=presenter.ReadBone(handle,NaturalCharacterRig.Head).Position.y};
            }
            public void Write(string directory)
            {
                var csv=new System.Text.StringBuilder("frame,actor,acquisition_seconds,root_x,root_y,ground_y,velocity_x,velocity_depth,scale,locomotion,far_phase,near_phase,far_stance,near_stance,far_x,far_y,near_x,near_y,pelvis_above_ground,head_above_ground,authoritative_jump\n");
                for(int i=0;i<count;i++)
                {
                    var s=samples[i];var m=s.Motion;float ground=m.FarFoot.PreviousRoot.y*m.Scale;
                    csv.Append(s.Frame).Append(',').Append(s.Actor).Append(',').Append((s.Time-samples[0].Time).ToString("F9",System.Globalization.CultureInfo.InvariantCulture));
                    foreach(float value in new[]{m.PreviousRoot.x,m.PreviousRoot.y,ground,m.PreviousVelocity.x*m.Scale,m.PreviousVelocity.y*m.Scale,m.Scale,(float)m.Locomotion,m.FarFoot.Phase,m.NearFoot.Phase,m.FarFoot.InStance?1:0,m.NearFoot.InStance?1:0,m.FarFoot.Position.x*m.Scale,m.FarFoot.Position.y*m.Scale,m.NearFoot.Position.x*m.Scale,m.NearFoot.Position.y*m.Scale,s.Pelvis-ground,s.Head-ground,m.PreviousRoot.y-ground})
                        csv.Append(',').Append(value.ToString("R",System.Globalization.CultureInfo.InvariantCulture));
                    csv.Append('\n');
                }
                System.IO.File.WriteAllText(System.IO.Path.Combine(directory,"gait.csv"),csv.ToString());
            }
        }
    }
}
#endif
