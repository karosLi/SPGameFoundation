#if !SPF_DOTNET_HARNESS
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using BrawlerFoundation.Game;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Contracts.Weapons;
using SPF.L2.Weapons;
using SPF.Presentation;
using SPF.Presentation.Animation;
using SPF.Presentation.Sprites;
using SPF.Testing;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BrawlerFoundation.Tests.PlayMode
{
    /// <summary>Bounded isolated-camera evidence. Copies the live prepared actor stream after
    /// production LateUpdate; never evaluates a pose, rewrites a sprite or advances a tick.</summary>
    public class BwBladeGripCloseupCaptureTests
    {
        const int FrameBudget = 128, ImageSize = 256, ActorParts = 19, CaptureLayer = 31;
        const double Interval = 1d / 30;
        const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        [UnityTest]
        public IEnumerator PreparedBladeGripSurvivesRepeatTurnEquipCancelAndReentry(
            [Values(RenderTier.GpuDriven, RenderTier.DataTexture)] RenderTier tier)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Actual graphics required.");
            if (tier == RenderTier.GpuDriven && !SystemInfo.supportsComputeShaders) Assert.Ignore("Compute tier unsupported.");
            // The explicit disabled-camera render used here targets the project's Built-in pipeline.
            Assert.IsNull(GraphicsSettings.currentRenderPipeline, "This diagnostic requires the project's Built-in render pipeline.");
            RenderCapabilities.Override = tier;
            BwGameBootstrap game = null;
            CloseupObserver observer = null;
            try
            {
                game = BwGameBootstrap.CreateWeaponBelt(playerMobility: BwBeltPlayerMobility.SmoothAttackV1);
                yield return null;
                UIDriver.Click(game.StartButton.gameObject);
                yield return UIDriver.WaitUntil(() => game.State.Flow == BwFlow.Fighting, 5);
                game.Session.Sync(); game.Session.ManualClock = true;
                game.Governor.AdaptiveQuality = false;
                game.InputRouter.Scripted.Active = true;
                game.InputRouter.Scripted.Frame = default;
                yield return null; yield return null;
                // Ordinary public scene setup only; variant-zero sentinel prevents wave transitions
                // without approaching, hitting or hiding the hero. No state is authored during capture.
                var world = game.Session.World;
                world.ClearLevel(); game.State.Flow = BwFlow.Fighting;
                BwSpawner.Spawn(world, 0, new float2(0, 0), 1, 0);
                BwSpawner.Spawn(world, 1, new float2(-8, 2), 1, 0);
                game.Session.Step(); game.Session.Step();
                game.Session.Clock.Restore(game.Session.Clock.NextTickIndex, game.Session.Clock.Elapsed);
                game.MobileHud.Refresh(); game.CameraRig.Snap();
                yield return null; yield return null;
                var weapons = world.Resource(BwWeapons.Key);
                Assert.AreEqual(WeaponProfiles.Blade, weapons.Current.ContentId);
                Assert.AreEqual(tier, game.Renderer.Tier);
                Assert.AreEqual(1f, Time.timeScale, "retain normal 1x gameplay time");
                observer = new CloseupObserver(game, tier);
                observer.CheckEmptyCamera();
                observer.Start();
                game.Session.ManualClock = false;
                int phase = 0, switchFrame = -1;
                uint reentryPulse = 0;
                double deadline = Time.realtimeSinceStartupAsDouble + 30;
                while (observer.Count < FrameBudget && observer.Error == null)
                {
                    if (Time.realtimeSinceStartupAsDouble >= deadline)
                        Assert.Fail("LateUpdate capture must acquire 128 frames within 30 seconds; acquired=" +
                            observer.Count + ", late_callbacks=" + observer.LateCallbacks +
                            ", last_captured_unity_frame=" + observer.LastUnityFrame);
                    game.Session.Sync();
                    int frame = observer.Count;
                    if (phase == 0 && frame >= 4) phase = 1;
                    if (phase == 1 && frame >= 64 && weapons.Equipment.Timeline.Running)
                    {
                        Assert.IsTrue(weapons.RequestEquip(WeaponProfiles.Sword));
                        switchFrame = frame; phase = 2;
                    }
                    else if (phase == 2 && frame >= switchFrame + 4 &&
                        weapons.Current.ContentId == WeaponProfiles.Sword && !weapons.Busy)
                    {
                        Assert.IsTrue(weapons.RequestEquip(WeaponProfiles.Blade));
                        phase = 3;
                    }
                    else if (phase == 3 && weapons.Current.ContentId == WeaponProfiles.Blade && !weapons.Busy)
                    {
                        reentryPulse = weapons.Equipment.Timeline.PulseId; phase = 4;
                    }
                    int side = frame < 38 ? 1 : -1;
                    bool held = phase == 1 || phase == 4;
                    game.InputRouter.Scripted.Frame = new InputFrame
                    {
                        Move = held ? new float2(side * .15f, 0) : float2.zero,
                        Aim = new float2(side, 0), Held = held ? 1u << BwButton.Punch : 0u
                    };
                    observer.ScenarioPhase = phase;
                    yield return null;
                }
                observer.Stop();
                game.Session.ManualClock = true;
                game.InputRouter.Scripted.Frame = default;
                if (observer.Error != null) ExceptionDispatchInfo.Capture(observer.Error).Throw();
                Assert.AreEqual(FrameBudget, observer.Count);
                Assert.AreEqual(4, phase, "actual equipment cancellation must return to blade attacks");
                string directory = observer.Write();
                observer.Validate(reentryPulse);
                TestContext.WriteLine("Isolated actor diagnostic (not the normal gameplay camera): " + directory);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                observer?.Dispose();
                if (game != null)
                {
                    game.InputRouter.Scripted.Frame = default; game.InputRouter.Scripted.Active = false;
                    if (game.CameraRig != null && game.CameraRig.Camera != null)
                        Object.Destroy(game.CameraRig.Camera.gameObject);
                    Object.Destroy(game.gameObject);
                }
                RenderCapabilities.Override = null;
            }
        }

        struct Sample
        {
            public int Frame, UnityFrame, PreparedFrame, Scenario, State, Action, Stage, TimelineTick, PreviousTick;
            public int WeaponId, PendingId, EquipRemaining, ActorOffset, Parts, SkillId, NormalMask, OriginalNormalMask;
            public uint Tick, Pulse, CueSequence, InputHeld;
            public long DroppedTicks;
            public double ObservedAt, SimulationSeconds;
            public float Phase, Alpha, Hp, Facing, Turn, Scale, CameraSize, NormalCameraSize;
            public float2 Root, Ground, Velocity, InputMove, InputAim, Hand, Grip, Tip, Camera, NormalCamera;
            public bool Running, SkillRunning, SawCancel;
        }

        sealed class CloseupObserver : IDisposable
        {
            readonly BwGameBootstrap m_Game;
            readonly RenderTier m_Tier;
            readonly Camera m_Normal, m_Close;
            readonly GameObject m_CameraObject;
            readonly RenderTexture m_Target;
            readonly SpriteBatch m_Batch;
            readonly BufferedFrameCapture m_Frames;
            readonly BwBladeGripCloseupCaptureDriver m_Driver;
            readonly int m_OriginalNormalMask;
            readonly FieldInfo m_PreparedFrame;
            int m_LastPreparedFrame;
            // Borrowed read-only views. The production presenter remains their sole owner.
            readonly NativeArray<GameplayCharacterInput> m_Inputs;
            readonly NativeArray<int> m_Offsets;
            readonly PackedSprite[] m_Copied = new PackedSprite[ActorParts];
            readonly Sample[] m_Samples = new Sample[FrameBudget];
            double m_Next;
            int m_LastUnityFrame = -1;
            bool m_Disposed;
            public int ScenarioPhase;
            public Exception Error { get; private set; }
            public int Count => m_Frames.Count;
            public int LateCallbacks => m_Driver.LateCallbacks;
            public int LastUnityFrame => m_LastUnityFrame;

            public CloseupObserver(BwGameBootstrap game, RenderTier tier)
            {
                m_Game = game; m_Tier = tier; m_Normal = game.CameraRig.Camera;
                m_OriginalNormalMask = m_Normal.cullingMask;
                var presenter = game.Renderer.Characters;
                var art = ReadPrivate<NaturalCharacterArt>(presenter, "m_Art");
                var source = ReadPrivate<SpriteBatch>(presenter, "m_Batch");
                m_Inputs = ReadPrivate<NativeArray<GameplayCharacterInput>>(presenter, "m_Inputs");
                m_Offsets = ReadPrivate<NativeArray<int>>(presenter, "m_SpriteOffsets");
                m_PreparedFrame = presenter.GetType().GetField("m_Frame", Private);
                Assert.IsNotNull(m_PreparedFrame);
                m_LastPreparedFrame = (int)m_PreparedFrame.GetValue(presenter);
                try
                {
                    m_CameraObject = new GameObject("Blade grip isolated diagnostic camera");
                    m_Close = m_CameraObject.AddComponent<Camera>();
                    m_Close.CopyFrom(m_Normal); m_Close.enabled = false;
                    m_Close.cullingMask = 1 << CaptureLayer;
                    m_Close.clearFlags = CameraClearFlags.SolidColor; m_Close.backgroundColor = Color.black;
                    m_Close.orthographic = true; m_Close.orthographicSize = 2.2f; m_Close.aspect = 1;
                    m_Close.allowHDR = false; m_Close.allowMSAA = false;
                    m_Target = new RenderTexture(ImageSize, ImageSize, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                    m_Target.Create(); m_Close.targetTexture = m_Target;
                    m_Batch = new SpriteBatch(tier, art.Sheet.Texture, BlendKind.Translucent, ActorParts, queueOffset: -20);
                    // Match the live cutout batch's depth/alpha settings, while retaining our own GPU buffers.
                    m_Batch.Material.SetFloat(RenderAssets.Ids.ZWrite, source.Material.GetFloat(RenderAssets.Ids.ZWrite));
                    int cutoff = Shader.PropertyToID("_Cutoff");
                    m_Batch.Material.SetFloat(cutoff, source.Material.GetFloat(cutoff));
                    Assert.AreSame(source.Material.shader, m_Batch.Material.shader);
                    Assert.AreSame(art.Sheet.Texture, m_Batch.Material.GetTexture(Shader.PropertyToID("_MainTex")));
                    m_Batch.Warmup(ActorParts);

                    m_Frames = new BufferedFrameCapture(m_Target, FrameBudget);
                    m_Driver = m_CameraObject.AddComponent<BwBladeGripCloseupCaptureDriver>();
                    m_Driver.enabled = false;
                }
                catch { Dispose(); throw; }
            }

            static T ReadPrivate<T>(object owner, string name)
            {
                var field = owner.GetType().GetField(name, Private);
                Assert.IsNotNull(field, "diagnostic private stream contract changed: " + name);
                return (T)field.GetValue(owner);
            }

            public void CheckEmptyCamera()
            {
                var previous = RenderTexture.active;
                Texture2D read = null;
                try
                {
                    read = new Texture2D(ImageSize, ImageSize, TextureFormat.RGBA32, false);
                    m_Close.Render(); RenderTexture.active = m_Target;
                    read.ReadPixels(new Rect(0, 0, ImageSize, ImageSize), 0, 0, false);
                    foreach (var pixel in read.GetPixels32())
                        Assert.IsTrue(pixel.r <= 1 && pixel.g <= 1 && pixel.b <= 1,
                            "isolated layer must contain no scene, trail, particle or HUD pixels before our actor draw");
                }
                finally { RenderTexture.active = previous; if (read != null) Object.Destroy(read); }
            }

            public void Start()
            {
                // SpriteBatch.Draw targets all cameras. Exclude only this already-proven-empty
                // diagnostic layer from the normal camera for this fixture; restore it on disposal.
                m_Normal.cullingMask = m_OriginalNormalMask & ~(1 << CaptureLayer);
                m_Next = Time.realtimeSinceStartupAsDouble;
                m_Driver.Observe = Observe; m_Driver.enabled = true;
            }
            public void Stop()
            {
                if (m_Driver == null) return;
                m_Driver.Observe = null; m_Driver.enabled = false;
            }

            void Observe()
            {
                if (Error != null || Count >= FrameBudget ||
                    Time.frameCount == m_LastUnityFrame || Time.realtimeSinceStartupAsDouble < m_Next) return;
                try { CapturePrepared(); }
                catch (Exception error) { Error = error; Stop(); }
            }

            void CapturePrepared()
            {
                // Test-owned LateUpdate(1000) is after BwRenderer(500), before the
                // SessionTickLauncher(32000). Completing a new tick here would compare
                // a future equipment ID with the already prepared actor stream.
                // No GameView/camera callback is needed. The diagnostic layer is temporarily
                // excluded from the normal camera, which prevents a second actor draw there.
                Assert.IsFalse(m_Game.Session.Pipeline.HasPendingTick,
                    "capture must stay inside the completed-tick presentation window; never Sync a future tick");
                uint sourceTick = m_Game.Session.Clock.NextTickIndex;
                var world = m_Game.Session.World;
                var weapons = world.Resource(BwWeapons.Key);
                var presenter = m_Game.Renderer.Characters;
                int preparedFrame = (int)m_PreparedFrame.GetValue(presenter);
                Assert.Greater(preparedFrame, m_LastPreparedFrame, "production must prepare a new actor stream before each sample");
                Assert.AreEqual(m_Tier, m_Game.Renderer.Tier);
                Assert.AreEqual(1f, Time.timeScale);
                Assert.IsTrue(m_Normal.enabled);
                Assert.AreEqual(m_OriginalNormalMask & ~(1 << CaptureLayer), m_Normal.cullingMask);
                Assert.IsTrue(presenter.TryReadCurrent(weapons.Owner, out var motion));
                Assert.IsTrue(presenter.TryReadWeapon(weapons.Owner, out var attachment));
                int actor = -1;
                for (int i = 0; i < presenter.Count; i++) if (m_Inputs[i].Handle.Equals(weapons.Owner)) actor = i;
                Assert.GreaterOrEqual(actor, 0, "copy the live sorted stream for the exact stable actor handle");
                int offset = m_Offsets[actor];
                int end = actor + 1 < presenter.Count ? m_Offsets[actor + 1] : presenter.PartsDrawn;
                Assert.AreEqual(ActorParts, end - offset, "armed actor keeps the original 19-record budget");
                var instances = m_Batch.Instances;
                for (int i = 0; i < ActorParts; i++) instances[i] = m_Copied[i] = presenter.ReadPart(offset + i);
                m_Batch.Count = ActorParts;
                AssertVisible(m_Copied[9], .5f); AssertVisible(m_Copied[11], .03f); AssertVisible(m_Copied[17], .5f);
                Assert.AreEqual(weapons.Current.VisualId, attachment.VisualId);
                var normalPosition = m_Normal.transform.position;
                var normalRotation = m_Normal.transform.rotation;
                var normalTarget = m_Normal.targetTexture;
                float normalSize = m_Normal.orthographicSize, normalAspect = m_Normal.aspect;
                int normalMask = m_Normal.cullingMask;
                m_Close.transform.SetPositionAndRotation(new Vector3(motion.PreviousRoot.x,
                    motion.PreviousRoot.y + 1.1f, normalPosition.z), normalRotation);
                AssertWholeActorFits();
                var sourceSnapshot = m_Game.Session.CaptureSnapshot();
                var view = weapons.View(m_Game.Session.InterpolationAlpha);
                var fighter = world.Column(BwKeys.Info)[0];
                var skill = world.Resource(BwWeapons.PoseKey);
                bool cancel = false;
                for (int i = 0; i < weapons.CueCount; i++) cancel |= weapons.Cues[i].Kind == WeaponCueKind.Cancel;
                m_Samples[Count] = new Sample
                {
                    Frame = Count, UnityFrame = Time.frameCount, PreparedFrame = preparedFrame, Scenario = ScenarioPhase,
                    ObservedAt = Time.realtimeSinceStartupAsDouble, SimulationSeconds = m_Game.Session.Clock.Elapsed,
                    Tick = m_Game.Session.Clock.NextTickIndex, DroppedTicks = m_Game.Session.Clock.DroppedTicks,
                    State = (int)fighter.State, Action = (int)fighter.Attack, Hp = fighter.Hp,
                    Stage = (int)view.Stage, Phase = view.Phase, Alpha = m_Game.Session.InterpolationAlpha,
                    WeaponId = weapons.Current.ContentId, PendingId = weapons.Equipment.PendingId,
                    EquipRemaining = weapons.Equipment.EquipRemaining, TimelineTick = weapons.Equipment.Timeline.Tick,
                    PreviousTick = weapons.Equipment.Timeline.PreviousTick, Pulse = weapons.Equipment.Timeline.PulseId,
                    Running = weapons.Equipment.Timeline.Running, CueSequence = weapons.Equipment.CueSequence, SawCancel = cancel,
                    Root = motion.PreviousRoot, Ground = world.Column(BwBeltKeys.Ground)[0],
                    Velocity = world.Column(BwBeltKeys.Motion)[0].GroundVelocity,
                    Facing = motion.Facing, Turn = motion.Turn, Scale = motion.Scale,
                    Hand = presenter.ReadBone(weapons.Owner, NaturalCharacterRig.Hand).Position,
                    Grip = attachment.PrimaryGrip, Tip = attachment.Tip,
                    InputMove = m_Game.State.Input.Move, InputAim = m_Game.State.Input.Aim, InputHeld = m_Game.State.Input.Held,
                    ActorOffset = offset, Parts = end - offset, SkillId = skill.ContentId, SkillRunning = skill.Running,
                    Camera = new float2(m_Close.transform.position.x, m_Close.transform.position.y), CameraSize = m_Close.orthographicSize,
                    NormalCamera = new float2(normalPosition.x, normalPosition.y), NormalCameraSize = normalSize,
                    NormalMask = normalMask, OriginalNormalMask = m_OriginalNormalMask
                };
                var previous = RenderTexture.active;
                try
                {
                    m_Batch.Draw(new Bounds(Vector3.zero, new Vector3(100000, 100000, 100)), layer: CaptureLayer);
                    m_Close.Render();
                    Assert.IsTrue(m_Frames.Capture(m_Game.Session.Clock.Elapsed));
                }
                finally { RenderTexture.active = previous; }
                CollectionAssert.AreEqual(sourceSnapshot, m_Game.Session.CaptureSnapshot(), "isolated draw/readback must not mutate simulation");
                Assert.AreEqual(sourceTick, m_Game.Session.Clock.NextTickIndex, "capture must not advance the source tick");
                Assert.IsFalse(m_Game.Session.Pipeline.HasPendingTick, "capture must not schedule a new tick");
                for (int i = 0; i < ActorParts; i++)
                {
                    var source = presenter.ReadPart(offset + i);
                    Assert.IsTrue(math.all(source.A == m_Copied[i].A) && math.all(source.B == m_Copied[i].B),
                        "diagnostic must leave every prepared source record bit-identical");
                }
                Assert.AreEqual(normalPosition, m_Normal.transform.position); Assert.AreEqual(normalRotation, m_Normal.transform.rotation);
                Assert.AreSame(normalTarget, m_Normal.targetTexture); Assert.AreEqual(normalMask, m_Normal.cullingMask);
                Assert.AreEqual(normalSize, m_Normal.orthographicSize); Assert.AreEqual(normalAspect, m_Normal.aspect);
                m_LastUnityFrame = Time.frameCount;
                m_LastPreparedFrame = preparedFrame;
                m_Next = m_Samples[Count - 1].ObservedAt + Interval;
            }

            static void AssertVisible(PackedSprite sprite, float minimumAlpha)
            {
                Assert.Greater(sprite.Color.w, minimumAlpha, "prepared body, blade and primary hand must be present (equip fades are retained)");
                Assert.Greater(math.cmin(math.abs(sprite.Size)), .02f);
                Assert.Greater(math.cmin(sprite.Uv.zw), 0f);
            }

            void AssertWholeActorFits()
            {
                foreach (var sprite in m_Copied)
                {
                    if (sprite.Color.w <= .01f) continue;
                    float c = math.cos(sprite.Rotation), s = math.sin(sprite.Rotation);
                    for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2)
                    {
                        float2 corner = math.abs(sprite.Size) * new float2(x, y) * .5f;
                        float2 p = sprite.Center + new float2(c * corner.x - s * corner.y, s * corner.x + c * corner.y);
                        var viewport = m_Close.WorldToViewportPoint(new Vector3(p.x, p.y, sprite.Depth));
                        Assert.Greater(viewport.x, .005f); Assert.Less(viewport.x, .995f);
                        Assert.Greater(viewport.y, .005f); Assert.Less(viewport.y, .995f);
                    }
                }
            }

            public void Validate(uint reentryPulse)
            {
                int phases = 0; bool left = false, right = false, cancel = false, sword = false;
                uint lastPulse = 0;
                for (int i = 0; i < Count; i++)
                {
                    var s = m_Samples[i];
                    Assert.AreEqual(ActorParts, s.Parts);
                    Assert.AreEqual(120f, s.Hp, "no damage may explain a grip/pose interruption");
                    Assert.IsFalse(s.SkillRunning, "this fixture makes no claim of simultaneous skill overlap");
                    if (i > 0)
                    {
                        Assert.Greater(s.UnityFrame, m_Samples[i - 1].UnityFrame);
                        Assert.Greater(s.PreparedFrame, m_Samples[i - 1].PreparedFrame);
                        Assert.Greater(s.ObservedAt, m_Samples[i - 1].ObservedAt);
                        Assert.GreaterOrEqual(s.Tick, m_Samples[i - 1].Tick);
                    }
                    if (s.WeaponId == WeaponProfiles.Blade)
                    {
                        phases |= 1 << s.Stage; left |= s.Facing < -.9f; right |= s.Facing > .9f;
                        if (s.Scenario == 4) lastPulse = math.max(lastPulse, s.Pulse);
                    }
                    cancel |= s.SawCancel; sword |= s.WeaponId == WeaponProfiles.Sword;
                    int colored = 0;
                    for (int y = 0; y < ImageSize; y++) for (int x = 0; x < ImageSize; x++)
                    { var p = m_Frames.ReadPixel(i, x, y); if (p.r > 20 || p.g > 20 || p.b > 20) colored++; }
                    Assert.Greater(colored, 100, "actual isolated GPU pixels must contain the prepared actor");
                }
                int required = (1 << (int)WeaponStage.Idle) | (1 << (int)WeaponStage.Windup) |
                    (1 << (int)WeaponStage.Active) | (1 << (int)WeaponStage.Recovery);
                Assert.AreEqual(required, phases & required);
                Assert.IsTrue(left && right && cancel && sword, "record both facings and actual equip-triggered cancellation");
                Assert.Greater(lastPulse, reentryPulse + 1, "record repeated real blade attacks after equipment reentry");
                Assert.Greater(m_Samples[Count - 1].Tick, m_Samples[0].Tick);
            }

            public string Write()
            {
                string directory = m_Frames.Write("blade-grip-isolated-" + (m_Tier == RenderTier.GpuDriven ? "gpu" : "fallback"),
                    "Separate 256x256 diagnostic camera follows the live hero root at fixed size 2.2. " +
                    "All 19 prepared actor records copied unchanged after production LateUpdate; actual shared atlas and requested tier. " +
                    "Trails, particles, terrain, other actors and HUD excluded only in this isolated view. " +
                    "Automatic gameplay clock at timeScale=1; target at most 30 samples/second, actual source cadence in acquisition.csv. " +
                    "Repeated blade attacks, aim/movement reversal, public RequestEquip sword cancellation, blade reentry. " +
                    "No skill-overlap claim; this is not same-camera full-gameplay or a frame-pacing benchmark.");
                var csv = new StringBuilder("frame,unity_frame,prepared_frame,scenario_phase,observation_realtime,simulation_seconds,next_tick,dropped_ticks,actor_state,actor_action,hp,weapon_id,weapon_stage,weapon_phase,interpolation_alpha,timeline_previous,timeline_tick,pulse,running,pending_id,equip_remaining,cue_sequence,cancel_cue_retained,root_x,root_y,ground_x,ground_depth,velocity_x,velocity_depth,facing,turn,scale,hand_x,hand_y,grip_x,grip_y,tip_x,tip_y,input_move_x,input_move_depth,input_aim_x,input_aim_y,input_held,actor_record_offset,actor_record_count,skill_id,skill_running,isolated_camera_x,isolated_camera_y,isolated_camera_size,normal_camera_x,normal_camera_y,normal_camera_size,normal_camera_mask_during_fixture,original_normal_camera_mask\n");
                for (int i = 0; i < Count; i++)
                {
                    var s = m_Samples[i];
                    foreach (double value in new double[] { s.Frame, s.UnityFrame, s.PreparedFrame, s.Scenario, s.ObservedAt, s.SimulationSeconds,
                        s.Tick, s.DroppedTicks, s.State, s.Action, s.Hp, s.WeaponId, s.Stage, s.Phase, s.Alpha,
                        s.PreviousTick, s.TimelineTick, s.Pulse, s.Running ? 1 : 0, s.PendingId, s.EquipRemaining,
                        s.CueSequence, s.SawCancel ? 1 : 0, s.Root.x, s.Root.y, s.Ground.x, s.Ground.y,
                        s.Velocity.x, s.Velocity.y, s.Facing, s.Turn, s.Scale, s.Hand.x, s.Hand.y, s.Grip.x, s.Grip.y,
                        s.Tip.x, s.Tip.y, s.InputMove.x, s.InputMove.y, s.InputAim.x, s.InputAim.y, s.InputHeld,
                        s.ActorOffset, s.Parts, s.SkillId, s.SkillRunning ? 1 : 0, s.Camera.x, s.Camera.y, s.CameraSize,
                        s.NormalCamera.x, s.NormalCamera.y, s.NormalCameraSize, s.NormalMask, s.OriginalNormalMask })
                        csv.Append(value.ToString("R", CultureInfo.InvariantCulture)).Append(',');
                    csv.Length--; csv.Append('\n');
                }
                File.WriteAllText(Path.Combine(directory, "authority-and-camera.csv"), csv.ToString());
                File.WriteAllText(Path.Combine(directory, "scenario.txt"),
                    "Scenario phases: 0 idle; 1 held blade (right, then left from sample 38); 2 requested sword while running; " +
                    "3 requested blade after observed sword equip; 4 held blade reentry. Input goes through the existing scripted router.\n" +
                    "Observation timestamps are after-production-LateUpdate source reads; acquisition.csv timestamps the subsequent explicit isolated camera readback. " +
                    "No pose update, simulation Step, source-record change, timescale change, frame interpolation or image encoding occurs inside acquisition.\n" +
                    "The normal camera remains enabled with its original target, transform and projection. Its mask excludes only diagnostic layer31 during this fixture and is restored on disposal. " +
                    "The existing SpriteBatch.Draw draws the copied records on layer31; no custom backend submission is used. The normal camera is never manually rerendered. Original normal-camera capture fixtures are unchanged.\n");
                return directory;
            }

            public void Dispose()
            {
                if (m_Disposed) return; m_Disposed = true; Stop();
                if (m_Normal != null) m_Normal.cullingMask = m_OriginalNormalMask;
                m_Frames?.Dispose(); m_Batch?.Dispose();
                if (m_Close != null) m_Close.targetTexture = null;
                if (m_Target != null) { m_Target.Release(); Object.Destroy(m_Target); }
                if (m_CameraObject != null) Object.Destroy(m_CameraObject);
                // The borrowed presenter, its atlas, NativeArrays and source batch are never disposed here.
            }
        }
    }
}
#endif
