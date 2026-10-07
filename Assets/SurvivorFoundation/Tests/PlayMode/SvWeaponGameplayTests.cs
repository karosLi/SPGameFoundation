#if !SPF_DOTNET_HARNESS
using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using SPF.Contracts;
using SPF.L2.Weapons;
using SPF.Presentation;
using SPF.Presentation.Animation;
using SPF.Presentation.Particles;
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
                // Native lifecycle regression on this exact renderer/backend. Disable retains its
                // private warm buffers, drops hidden cue backlog and releases its camera callback.
                weapons.RequestEquip(WeaponProfiles.Staff);
                for(int i=0;i<weapons.Profile(WeaponProfiles.Staff).EquipTicks;i++)game.Session.Step();
                Assert.AreEqual(WeaponProfiles.Staff,weapons.Current.ContentId);
                // Three bounded point images of this lifecycle fixture, before later live clips reset it.
                yield return CaptureLifecyclePoint(game,capture,safe,tier,"before-disable");
                int hiddenReleases=weapons.Releases;
                var warmParticles=game.Renderer.WeaponParticles;
                uint hiddenHead=weapons.Equipment.CueSequence;
                game.Renderer.enabled=false;
                Assert.IsNull(game.CameraRig.UpdateTarget);
                for(int i=0;i<weapons.Current.DurationTicks*2+weapons.Current.EquipTicks+4;i++)
                {game.State.Input=new InputFrame{Held=1u<<SvWeapons.AttackButton};game.Session.Step();}
                game.State.Input=default;game.Session.Pause();
                Assert.Greater(weapons.Equipment.CueSequence,hiddenHead,"the disabled interval must contain real authoritative weapon cues");
                Assert.Greater(weapons.Releases,hiddenReleases,"the hidden interval must contain an actual release burst");
                var lifecycleSnapshot=game.Session.CaptureSnapshot();
                game.Renderer.enabled=true;game.Renderer.RenderFrame();
                Assert.AreSame(warmParticles,game.Renderer.WeaponParticles);
                Assert.AreEqual(weapons.Equipment.CueSequence,game.Renderer.LastWeaponCueSequence);
                Assert.Zero(warmParticles.Renderer.Pool.ReservedCount,"reenable must skip hidden releases and old attachments");
                Assert.IsNotNull(game.CameraRig.UpdateTarget);
                yield return CaptureLifecyclePoint(game,capture,safe,tier,"after-reenable");
                Assert.Zero(warmParticles.Renderer.Pool.ReservedCount,"hidden releases must remain suppressed through the captured frame");
                CollectionAssert.AreEqual(lifecycleSnapshot,game.Session.CaptureSnapshot());
                uint restoreTick=game.Session.Clock.NextTickIndex;
                game.Session.RestoreSnapshot(lifecycleSnapshot);game.Renderer.RenderFrame();
                Assert.AreEqual(restoreTick,game.Session.Clock.NextTickIndex);
                Assert.Zero(warmParticles.Renderer.Pool.ReservedCount);
                CollectionAssert.AreEqual(lifecycleSnapshot,game.Session.CaptureSnapshot());
                yield return CaptureLifecyclePoint(game,capture,safe,tier,"after-same-tick-restore");
                Assert.AreEqual(restoreTick,game.Session.Clock.NextTickIndex);
                Assert.Zero(warmParticles.Renderer.Pool.ReservedCount,"restored cues must remain suppressed through the captured frame");
                CollectionAssert.AreEqual(lifecycleSnapshot,game.Session.CaptureSnapshot());
                game.Session.Resume();
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
                        game.Session.ManualClock=true;frames.Write("weapon-horde-live-"+suffix,"Actual automatic-clock portrait horde: run/walk, pulse/blink in contact captures, staff charge/projectile, switch to bow draw/release while independent pulse/blink HUD remains. Target30Hz, measured timestamps retained.",BufferedFrameFormat.Jpeg95Review);
                    }
                    // Move actual agile/heavy enemies for locomotion evidence; the weapon fixture
                    // above intentionally freezes enemies and cannot demonstrate their walking.
                    capture.Dispose();capture=new CanvasCapture(game.gameObject,game.CameraRig.Camera,320,568);game.Hud.MobileHud.SetPreviewViewport(320,568,new Rect(0,0,320,568));
                    world.ClearLevel();game.State.Flow=SvFlow.Playing;game.State.Hero=game.State.HeroPrev=0;game.State.Input=default;
                    for(int kind=0;kind<runtime.Enemies.Length;kind++){var enemy=runtime.Enemies[kind];enemy.Speed=kind==2?.3f:kind==0?.4f:.45f;runtime.Enemies[kind]=enemy;}
                    runtime.Settings.GuardAggroRadius=30;
                    Assert.GreaterOrEqual(runtime.EnemyKinds,3,"the locomotion fixture requires three authored enemy families");
                    SvSpawner.SpawnEnemy(world,runtime,1,new float2(3.5f,4.2f));SvSpawner.SpawnEnemy(world,runtime,2,new float2(0,6.5f));SvSpawner.SpawnEnemy(world,runtime,3,new float2(-3.5f,7));
                    Assert.AreEqual(3,world.Table(SvKeys.Enemy).Count);
                    for(int row=0;row<3;row++)Assert.AreEqual(row+1,world.Column(SvKeys.Info)[row].Kind,"enemy content IDs are one-based");
                    Assert.Less(world.Column(SvKeys.Info)[0].Radius,.65f,"first captured enemy uses the agile profile");
                    Assert.GreaterOrEqual(world.Column(SvKeys.Info)[2].Radius,.65f,"third captured enemy uses the heavy profile");
                    Canvas.ForceUpdateCanvases();yield return null;yield return null;
                    using(var frames=new BufferedFrameCapture(capture.Target,160))
                    {
                        var trace=new GaitTrace(game.CameraRig.Camera);game.Session.ManualClock=false;double next=Time.realtimeSinceStartupAsDouble;
                        for(int i=0;i<160;i++)
                        {
                            while(Time.realtimeSinceStartupAsDouble<next)yield return null;
                            int scenarioPhase=i<32?0:i<56?1:2;
                            // Explicit scripted content-speed transitions, through the real simulation.
                            // The renderer still receives only actual fixed-tick travel and combat state.
                            if(i==32||i==56)for(int row=0;row<world.Table(SvKeys.Enemy).Count;row++)
                            {
                                var enemy=world.Column(SvKeys.Info)[row];
                                enemy.Speed=i==32?(enemy.Kind==3?3.7f:enemy.Kind==1?1.4f:1.9f):(enemy.Kind==3?.3f:enemy.Kind==1?.4f:.45f);
                                world.Column(SvKeys.Info).Set(row,enemy);
                            }
                            float2 move=i<32?new float2(.18f,.10f):i<48?new float2(.6f,0):i<64?new float2(-.6f,0):i<84?float2.zero:i<116?new float2(-.18f,-.10f):i<138?new float2(0,.2f):new float2(0,-.2f);
                            game.State.Input=InputFrame.Latch(game.State.Input,new InputFrame{Move=move});
                            double acquiredAt=Time.realtimeSinceStartupAsDouble;frames.Capture(weapons.Tick/30d);trace.Capture(i,0,acquiredAt,game.Renderer.Characters,new EntityHandle(-1,1),(int)game.State.Flow,0,game.State.Time,game.State.Hp,scenarioPhase);
                            var handles=world.Table(SvKeys.Enemy).Handles;for(int row=0;row<math.min(3,world.Table(SvKeys.Enemy).Count);row++){var actor=world.Column(SvKeys.Info)[row];trace.Capture(i,row+1,acquiredAt,game.Renderer.Characters,handles[row],(int)actor.Flags,actor.Kind,actor.Timer,actor.Hp,scenarioPhase);}
                            next=acquiredAt+1d/30;
                        }
                        game.Session.ManualClock=true;string directory=frames.Write("grounded-horde-live-"+suffix,"Actual automatic-clock hero plus agile, standard and heavy enemies: bounded walking/running/reversal/depth input. Scripted NPC speeds in world units/s: agile/standard/heavy .4/.45/.3 for frames0-31 and56-159, 1.4/1.9/3.7 for frames32-55; hero aggro30, through the real simulation. Spawns clear of the beacon; same camera. Per-actor gait.csv is a readback annotation, not a manufactured render clock.",BufferedFrameFormat.Jpeg95Review);trace.Write(directory);trace.AssertRoleCoverage();
                    }
                }
                LogAssert.NoUnexpectedReceived();
            }
            finally{capture?.Dispose();RenderCapabilities.Override=null;if(Camera.main!=null)Object.Destroy(Camera.main.gameObject);Object.Destroy(game.gameObject);Object.Destroy(config);}
        }

        static IEnumerator CaptureLifecyclePoint(SvGameBootstrap game,CanvasCapture capture,Rect safe,RenderTier requestedTier,string stage)
        {
            Assert.IsTrue(game.Session.ManualClock,"point capture must not advance the simulation");
            byte[] snapshot=game.Session.CaptureSnapshot();uint tick=game.Session.Clock.NextTickIndex;
            game.Renderer.RenderFrame();
            Assert.AreEqual(requestedTier,game.Renderer.Tier,"requested world rendering tier must actually be bound");
            Assert.IsNotNull(game.Renderer.WeaponParticles);
            var particles=game.Renderer.WeaponParticles.Renderer;
            if(requestedTier==RenderTier.DataTexture)
            {
                Assert.AreEqual(RenderTier.DataTexture,particles.Tier,"fallback point image must use data-texture particle rendering");
                Assert.AreEqual(ParticleBackend.CpuBurst,particles.Backend,"data-texture fallback must use CPU/Burst particle simulation");
            }
            // Particle capability selection is independent of the requested world tier. Record its
            // actual result: a GPU-labelled world capture is not proof of compute particle execution.
            string name="weapon-horde-lifecycle-point-"+stage+"-"+(requestedTier==RenderTier.GpuDriven?"gpu":"fallback");
            yield return capture.Save(name,safe);
            Assert.AreEqual(tick,game.Session.Clock.NextTickIndex,"point capture cannot add a simulation tick");
            CollectionAssert.AreEqual(snapshot,game.Session.CaptureSnapshot(),"point capture must leave the authoritative snapshot unchanged");
            string evidence=$"Point image only; not continuous-motion proof. Transition: {stage}.\n"+
                $"Requested world tier: {requestedTier}; active world tier: {game.Renderer.Tier}; active particle render tier: {particles.Tier}; active particle simulation backend: {particles.Backend}.\n"+
                $"Graphics API: {SystemInfo.graphicsDeviceType}; next simulation tick: {tick}; acquisition realtime: {capture.CaptureRealtime:R}; reserved particles: {particles.Pool.ReservedCount}.\n";
            File.AppendAllText(Path.Combine(Application.dataPath,"..","Artifacts","Screenshots","MobileHud",name+".txt"),evidence);
            TestContext.WriteLine(evidence);
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
            public int Frame,Actor,SourceState,SourceAction,ScenarioPhase;public double Time;public GameplayCharacterMotion Motion;public float Pelvis,Head,SourcePhase,Hp;public EntityHandle Handle;public bool Visible;
        }
        sealed class GaitTrace
        {
            readonly GaitSample[] samples=new GaitSample[160*4];readonly Camera camera;int count;
            public GaitTrace(Camera camera){this.camera=camera;}
            public void Capture(int frame,int actor,double time,GameplayCharacterPresenter presenter,EntityHandle handle,int sourceState=0,int sourceAction=0,float sourcePhase=0,float hp=0,int scenarioPhase=0)
            {
                if(count==samples.Length||!presenter.TryReadCurrent(handle,out var motion))return;
                float head=presenter.ReadBone(handle,NaturalCharacterRig.Head).Position.y;
                var feetViewport=camera.WorldToViewportPoint(new Vector3(motion.PreviousRoot.x,motion.PreviousRoot.y,0));
                var headViewport=camera.WorldToViewportPoint(new Vector3(motion.PreviousRoot.x,head,0));
                bool visible=feetViewport.z>0&&feetViewport.x>.02f&&feetViewport.x<.98f&&feetViewport.y>.02f&&headViewport.y<.98f;
                samples[count++]=new GaitSample {Frame=frame,Actor=actor,Time=time,Motion=motion,Handle=handle,SourceState=sourceState,SourceAction=sourceAction,SourcePhase=sourcePhase,Hp=hp,ScenarioPhase=scenarioPhase,Visible=visible,
                    Pelvis=presenter.ReadBone(handle,NaturalCharacterRig.Pelvis).Position.y,
                    Head=head};
            }
            public void AssertRoleCoverage()
            {
                for(int actor=0;actor<4;actor++)
                {
                    int visible=0,moving=0,walk=0,run=0,unsupportedWalk=0;float distance=0;bool hasPrevious=false;float2 previous=0;
                    for(int i=0;i<count;i++)
                    {
                        var sample=samples[i];if(sample.Actor!=actor||!sample.Visible)continue;
                        visible++;var m=sample.Motion;float speed=math.length(m.PreviousVelocity*m.Scale);
                        if(m.Locomotion==GameplayLocomotionState.Walk&&!m.Airborne&&!m.FarFoot.InStance&&!m.NearFoot.InStance)unsupportedWalk++;
                        if(speed>.05f){moving++;if(m.Locomotion==GameplayLocomotionState.Walk)walk++;if(m.Locomotion==GameplayLocomotionState.Run)run++;}
                        if(hasPrevious)distance+=math.distance(previous,m.PreviousRoot);previous=m.PreviousRoot;hasPrevious=true;
                    }
                    TestContext.WriteLine($"Captured role {actor}: visible={visible}, moving={moving}, Walk={walk}, Run={run}, unsupportedWalk={unsupportedWalk}, travel={distance}");
                    Assert.GreaterOrEqual(visible,120,"the role must remain on camera");Assert.GreaterOrEqual(moving,100,"the role must actually move");
                    Assert.GreaterOrEqual(walk,60,"the role must demonstrate sustained walking");Assert.GreaterOrEqual(run,16,"the role must demonstrate running");
                    Assert.GreaterOrEqual(distance,2f,"static or beacon-pinned actors do not validate locomotion");
                    Assert.AreEqual(0,unsupportedWalk,"a visible grounded walk must retain support");
                }
            }
            public void Write(string directory)
            {
                var csv=new System.Text.StringBuilder("frame,actor,acquisition_seconds,root_x,root_y,ground_y,velocity_x,velocity_depth,scale,locomotion,far_phase,near_phase,far_stance,near_stance,far_x,far_y,near_x,near_y,pelvis_above_ground,head_above_ground,authoritative_jump,entity_index,entity_generation,current_frame_visible,source_state,source_action,source_phase,hp,scenario_phase,hit_weight,attack_weight,facing,turn,far_air_seconds,near_air_seconds,far_support_seconds,near_support_seconds,far_toeoff_support,near_toeoff_support,support_ceiling,transfer_delay,weapon_aim_x,weapon_aim_y,weapon_aim_drop,skill_pelvis_drop,moving\n");
                for(int i=0;i<count;i++)
                {
                    var s=samples[i];var m=s.Motion;float ground=m.FarFoot.PreviousRoot.y*m.Scale;
                    csv.Append(s.Frame).Append(',').Append(s.Actor).Append(',').Append((s.Time-samples[0].Time).ToString("F9",System.Globalization.CultureInfo.InvariantCulture));
                    foreach(float value in new[]{m.PreviousRoot.x,m.PreviousRoot.y,ground,m.PreviousVelocity.x*m.Scale,m.PreviousVelocity.y*m.Scale,m.Scale,(float)m.Locomotion,m.FarFoot.Phase,m.NearFoot.Phase,m.FarFoot.InStance?1:0,m.NearFoot.InStance?1:0,m.FarFoot.Position.x*m.Scale,m.FarFoot.Position.y*m.Scale,m.NearFoot.Position.x*m.Scale,m.NearFoot.Position.y*m.Scale,s.Pelvis-ground,s.Head-ground,m.PreviousRoot.y-ground,s.Handle.Index,s.Handle.Generation,s.Visible?1:0,s.SourceState,s.SourceAction,s.SourcePhase,s.Hp,s.ScenarioPhase,m.Hit,m.Attack,m.Facing,m.Turn,m.FarFoot.AirSeconds,m.NearFoot.AirSeconds,m.FarFoot.SupportSeconds,m.NearFoot.SupportSeconds,m.FarFoot.ToeOffSupported?1:0,m.NearFoot.ToeOffSupported?1:0,m.SupportCeiling,m.TransferDelay,m.WeaponAim.x,m.WeaponAim.y,m.WeaponAimDrop,m.Skill.Pose.PelvisDrop,m.Moving?1:0})
                        csv.Append(',').Append(value.ToString("R",System.Globalization.CultureInfo.InvariantCulture));
                    csv.Append('\n');
                }
                System.IO.File.WriteAllText(System.IO.Path.Combine(directory,"gait.csv"),csv.ToString());
            }
        }
    }
}
#endif
