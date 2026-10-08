#if !SPF_DOTNET_HARNESS
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using BrawlerFoundation.Game;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Contracts.Weapons;
using SPF.L2.Weapons;
using SPF.Presentation;
using SPF.Testing;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BrawlerFoundation.Tests.PlayMode
{
    /// <summary>Readback evidence of movement admitted by the existing belt rules. Never supplies poses,
    /// velocity or simulation time. Keep separate from the existing before/after gait recordings.</summary>
    public class BwMovementSpeedCaptureTests
    {
        const int FrameCount = 50;
        const double TargetInterval = 1d / 30;

        [UnityTest]
        public IEnumerator FullHorizontalInputAndHeldAttackUseActualMovement(
            [Values(RenderTier.GpuDriven, RenderTier.DataTexture)] RenderTier tier)
        {
            if (Environment.GetEnvironmentVariable("SPF_WEAPON_GAMEPLAY_SEQUENCE") != "1")
                Assert.Ignore("Opt-in bounded movement capture: SPF_WEAPON_GAMEPLAY_SEQUENCE=1.");
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Actual graphics required.");
            if (tier == RenderTier.GpuDriven && !SystemInfo.supportsComputeShaders) Assert.Ignore("Compute tier unsupported.");
            RenderCapabilities.Override = tier;
            var game = BwGameBootstrap.CreateWeaponBelt(playerMobility: BwBeltPlayerMobility.SmoothAttackV1);
            CanvasCapture capture = null;
            try
            {
                yield return null;
                UIDriver.Click(game.StartButton.gameObject);
                yield return UIDriver.WaitUntil(() => game.State.Flow == BwFlow.Fighting, 5);
                game.Session.Sync(); game.Session.ManualClock = true;
                game.Governor.AdaptiveQuality = false;
                // Use the existing public input source and normal router execution order. Settle
                // the HUD ownership boundary before either measurement; never write state per frame.
                game.InputRouter.Scripted.Active = true;
                game.InputRouter.Scripted.Frame = default;
                yield return null; yield return null;
                capture = new CanvasCapture(game.gameObject, game.CameraRig.Camera, 640, 360);
                game.MobileHud.SetPreviewViewport(640, 360, new Rect(0, 0, 640, 360));
                float normalRate = 0;
                for (int mode = 0; mode < 2; mode++)
                {
                    bool heldAttack = mode == 1;
                    Assert.AreEqual(1f, UnityEngine.Time.timeScale, "comparison must use normal 1x gameplay time");
                    game.InputRouter.Scripted.Frame = default;
                    game.Session.Sync();
                    var world = game.Session.World;
                    Assert.AreEqual(BwBeltPlayerMobility.SmoothAttackV1,
                        world.Resource(BwBeltKeys.State).Config.PlayerMobility,
                        "the playable weapon bootstrap must select the approved smooth movement policy");
                    world.ClearLevel(); game.State.Flow = BwFlow.Fighting;
                    // Public scene setup only. The stationary target stays alive behind the hero,
                    // in a different depth lane. It prevents wave transitions without obstructing
                    // the forward route; no actor is repositioned during acquisition.
                    BwSpawner.Spawn(world, 0, new float2(-5, 0), 1, 0);
                    BwSpawner.Spawn(world, 1, new float2(-8, 2), 1, 0);
                    game.Session.Step(); game.Session.Step();
                    game.Session.Clock.Restore(game.Session.Clock.NextTickIndex, game.Session.Clock.Elapsed);
                    game.MobileHud.Refresh(); game.CameraRig.Snap();
                    Canvas.ForceUpdateCanvases();
                    yield return null; yield return null;
                    Assert.AreEqual(tier, game.Renderer.Tier, "requested backend must actually be bound");
                    var weapons = world.Resource(BwWeapons.Key);
                    Assert.AreEqual(WeaponProfiles.Blade, weapons.Current.ContentId);
                    Assert.IsFalse(weapons.Busy);
                    game.InputRouter.Scripted.Frame = new InputFrame
                    { Move = new float2(1, 0), Held = heldAttack ? 1u << BwButton.Punch : 0u };
                    yield return null; yield return null;
                    Assert.AreEqual(1f, game.InputRouter.Last.Move.x);
                    var samples = new Sample[FrameCount];
                    using (var frames = new BufferedFrameCapture(capture.Target, FrameCount))
                    {
                        game.Session.ManualClock = false;
                        double next = Time.realtimeSinceStartupAsDouble;
                        for (int frame = 0; frame < FrameCount; frame++)
                        {
                            while (Time.realtimeSinceStartupAsDouble < next) yield return null;
                            game.Session.Sync();
                            samples[frame] = Read(game, frame);
                            Assert.IsTrue(frames.Capture(game.Session.Clock.Elapsed));
                            next = samples[frame].Realtime + TargetInterval;
                        }
                        game.Session.ManualClock = true;
                        game.InputRouter.Scripted.Frame = default;
                        string name = "belt-movement-" + (heldAttack ? "held-attack-" : "full-input-") +
                            (tier == RenderTier.GpuDriven ? "gpu" : "fallback");
                        // Preserve every raw pixel losslessly; all encoding/serialization is deferred.
                        string directory = frames.Write(name,
                            "Automatic session clock and public scripted full horizontal input; " +
                            (heldAttack ? "blade attack held" : "no attack") +
                            ". 640x360, target 30 Hz, measured cadence, normal 1x playback. " +
                            "movement.csv annotates source state at readback, not exact camera-render time.");
                        WriteTrace(directory, samples, game.Session.Clock.StepSeconds, tier, heldAttack);
                    }
                    float rate = Validate(samples, game.Session.Clock.StepSeconds, heldAttack);
                    if (!heldAttack) normalRate = rate;
                    else
                    {
                        Assert.Less(rate, normalRate, "attack commitment must remain slower than free movement");
                        Assert.GreaterOrEqual(rate, normalRate * BwBeltRules.AttackActiveMoveScale - .002f,
                            "continuous held attacks must sustain the minimum approved movement rate");
                    }
                }
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                if (game != null) { game.InputRouter.Scripted.Frame = default; game.InputRouter.Scripted.Active = false; }
                capture?.Dispose(); RenderCapabilities.Override = null;
                if (Camera.main != null) Object.Destroy(Camera.main.gameObject);
                if (game != null) Object.Destroy(game.gameObject);
            }
        }

        struct Sample
        {
            public int Frame, State, Action, Stage, TimelineTick, PreMoveTimelineTick, PreMoveStage, Mobility;
            public uint NextTick, Pulse, InputHeld, RouterHeld;
            public long DroppedTicks;
            public double Realtime, SimulationSeconds;
            public float2 Ground, PreviousGround, Velocity, Input, RouterInput;
            public float StateTime, Hp, Height, TargetDistance, CameraX, CameraY, CameraSize, ExpectedMoveScale;
            public bool Busy, HasPreMovePhase;
        }

        static Sample Read(BwGameBootstrap game, int frame)
        {
            var world = game.Session.World;
            var actor = world.Column(BwKeys.Info)[0];
            var ground = world.Column(BwBeltKeys.Ground)[0];
            var motion = world.Column(BwBeltKeys.Motion)[0];
            var weapons = world.Resource(BwWeapons.Key);
            var timeline = weapons.Equipment.Timeline;
            // Advance retains the authoritative timeline tick used earlier by movement. Begin
            // replaces it with -1, so initial admission/restart boundaries stay unattributed.
            bool hasPreMovePhase = timeline.Running && timeline.PreviousTick >= 0 &&
                timeline.Tick == timeline.PreviousTick + 1;
            int preMoveStage = hasPreMovePhase ? (int)(timeline.PreviousTick < weapons.Current.Active.From
                ? WeaponStage.Windup : timeline.PreviousTick < weapons.Current.Active.Until
                ? WeaponStage.Active : WeaponStage.Recovery) : -1;
            var camera = game.CameraRig.Camera;
            return new Sample
            {
                Frame = frame, NextTick = game.Session.Clock.NextTickIndex,
                Realtime = Time.realtimeSinceStartupAsDouble, SimulationSeconds = game.Session.Clock.Elapsed,
                DroppedTicks = game.Session.Clock.DroppedTicks,
                Ground = ground, PreviousGround = world.Column(BwBeltKeys.PreviousGround)[0],
                Velocity = motion.GroundVelocity, Input = game.State.Input.Move, InputHeld = game.State.Input.Held,
                RouterInput = game.InputRouter.Last.Move, RouterHeld = game.InputRouter.Last.Held,
                State = (int)actor.State, Action = (int)actor.Attack, StateTime = actor.StateTime,
                Hp = actor.Hp, Height = motion.Height,
                TargetDistance = math.distance(ground, world.Column(BwBeltKeys.Ground)[1]),
                Busy = weapons.Busy, Stage = (int)weapons.View(1).Stage,
                TimelineTick = weapons.Equipment.Timeline.Tick, Pulse = weapons.Equipment.Timeline.PulseId,
                PreMoveTimelineTick = timeline.PreviousTick, PreMoveStage = preMoveStage,
                HasPreMovePhase = hasPreMovePhase, ExpectedMoveScale = MoveScale(preMoveStage),
                Mobility = (int)world.Resource(BwBeltKeys.State).Config.PlayerMobility,
                CameraX = camera.transform.position.x, CameraY = camera.transform.position.y,
                CameraSize = camera.orthographicSize
            };
        }

        static float Validate(Sample[] samples, double step, bool heldAttack)
        {
            int walk = 0, attack = 0, windup = 0, active = 0, recovery = 0;
            var first = samples[0]; var last = samples[samples.Length - 1];
            for (int i = 0; i < samples.Length; i++)
            {
                var s = samples[i];
                Assert.AreEqual((int)BwBeltPlayerMobility.SmoothAttackV1, s.Mobility);
                Assert.AreEqual(1f, s.Input.x); Assert.AreEqual(0f, s.Input.y);
                Assert.AreEqual(1f, s.RouterInput.x); Assert.AreEqual(0f, s.RouterInput.y);
                uint expectedHeld = heldAttack ? 1u << BwButton.Punch : 0u;
                Assert.AreEqual(expectedHeld, s.InputHeld); Assert.AreEqual(expectedHeld, s.RouterHeld);
                Assert.AreEqual(120f, s.Hp, "incoming damage must not explain a slowdown");
                Assert.AreEqual(0f, s.Height); Assert.AreEqual(0f, s.Ground.y);
                Assert.Less(math.abs(s.Ground.x), BwRules.ArenaHalf - .5f, "wall must not explain a slowdown");
                Assert.Greater(s.TargetDistance, 2f, "target must remain outside the comparison route");
                Assert.AreEqual(first.CameraX, s.CameraX, .0001f); Assert.AreEqual(first.CameraY, s.CameraY, .0001f);
                Assert.AreEqual(first.CameraSize, s.CameraSize, .0001f);
                Assert.AreEqual(s.Velocity.x * step, s.Ground.x - s.PreviousGround.x, .0001,
                    "last actual tick displacement must agree with velocity without separation or clamping");
                if (i > 0)
                {
                    Assert.GreaterOrEqual(s.NextTick, samples[i - 1].NextTick);
                    Assert.Greater(s.Realtime, samples[i - 1].Realtime);
                    Assert.GreaterOrEqual(s.Ground.x, samples[i - 1].Ground.x - .0001f);
                    if (s.NextTick > samples[i - 1].NextTick)
                        Assert.Greater(s.Ground.x, samples[i - 1].Ground.x,
                            "held input must keep moving between every pair of advancing samples");
                }
                if (s.State == (int)FighterState.Walk) walk++;
                if (s.State == (int)FighterState.Attack) attack++;
                if (s.NextTick > first.NextTick)
                {
                    Assert.Greater(s.Ground.x - s.PreviousGround.x, 0f,
                        "every sampled completed movement tick must have positive displacement");
                    Assert.Greater(s.Velocity.x, 0f);
                    double observedRatio = (s.Ground.x - s.PreviousGround.x) / step / BwRules.PlayerSpeed;
                    Assert.GreaterOrEqual(observedRatio, BwBeltRules.AttackActiveMoveScale - .002,
                        "even an unattributed Begin boundary must sustain the approved minimum speed");
                    Assert.LessOrEqual(observedRatio, 1.002, "no movement sample may exceed normal full input");
                    if (s.HasPreMovePhase)
                    {
                        Assert.IsTrue(heldAttack); Assert.AreEqual((int)FighterState.Attack, s.State);
                        Assert.AreEqual(s.ExpectedMoveScale, observedRatio, .002,
                            "measured last-tick movement must match its retained pre-advance phase");
                        if (s.PreMoveStage == (int)WeaponStage.Windup) windup++;
                        if (s.PreMoveStage == (int)WeaponStage.Active) active++;
                        if (s.PreMoveStage == (int)WeaponStage.Recovery) recovery++;
                    }
                }
            }
            Assert.Greater(last.NextTick, first.NextTick, "automatic clock must actually advance");
            Assert.Greater(last.Ground.x - first.Ground.x, .001f, "positive input must cause actual displacement");
            float rate = (float)((last.Ground.x - first.Ground.x) / (last.SimulationSeconds - first.SimulationSeconds));
            if (heldAttack)
            {
                Assert.Greater(attack, 0); Assert.Greater(windup, 0); Assert.Greater(active, 0); Assert.Greater(recovery, 0);
                Assert.Greater(last.Pulse, first.Pulse, "the input must start an actual weapon action");
            }
            else
            {
                Assert.Greater(walk, 0); Assert.Zero(attack); Assert.Zero(windup); Assert.Zero(active); Assert.Zero(recovery);
                Assert.AreEqual(BwRules.PlayerSpeed, rate, .002f, "full input must reach the authoritative normal rate");
            }
            return rate;
        }

        // -1 means an unavailable source phase, not an inferred normal-speed tick.
        static float MoveScale(int stage) => stage == (int)WeaponStage.Windup ? BwBeltRules.AttackWindupMoveScale :
            stage == (int)WeaponStage.Active ? BwBeltRules.AttackActiveMoveScale :
            stage == (int)WeaponStage.Recovery ? BwBeltRules.AttackRecoveryMoveScale : -1f;

        static void WriteTrace(string directory, Sample[] samples, double step, RenderTier tier, bool heldAttack)
        {
            var csv = new StringBuilder("frame,observation_seconds,simulation_seconds,next_tick,ticks_since_sample,dropped_ticks_total,ground_x,ground_depth,last_tick_previous_x,last_tick_previous_depth,last_tick_dx,last_tick_ddepth,sample_dx,sample_ddepth,sample_simulation_seconds,last_tick_velocity_x,last_tick_velocity_depth,last_tick_effective_speed_ratio,input_x,input_depth,input_held,router_x,router_depth,router_held,state_after_tick,action_after_tick,state_seconds,weapon_stage_after_tick,timeline_tick_after_tick,pulse,busy,pre_move_phase_available,pre_move_timeline_tick,pre_move_weapon_stage,expected_pre_move_scale,hp,height,target_distance,camera_x,camera_y,camera_size,profile_player_speed,mobility_policy,windup_scale,active_scale,recovery_scale\n");
            var first = samples[0]; int windup = 0, active = 0, recovery = 0, boundaries = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                var s = samples[i]; var previous = i == 0 ? s : samples[i - 1];
                if (s.HasPreMovePhase && s.PreMoveStage == (int)WeaponStage.Windup) windup++;
                if (s.HasPreMovePhase && s.PreMoveStage == (int)WeaponStage.Active) active++;
                if (s.HasPreMovePhase && s.PreMoveStage == (int)WeaponStage.Recovery) recovery++;
                if (s.Busy && !s.HasPreMovePhase) boundaries++;
                // Allocation/formatting deliberately happens only after all image acquisition.
                foreach (double value in new double[] { s.Frame, s.Realtime - first.Realtime, s.SimulationSeconds,
                    s.NextTick, s.NextTick - previous.NextTick, s.DroppedTicks, s.Ground.x, s.Ground.y,
                    s.PreviousGround.x, s.PreviousGround.y, s.Ground.x - s.PreviousGround.x, s.Ground.y - s.PreviousGround.y,
                    s.Ground.x - previous.Ground.x, s.Ground.y - previous.Ground.y, s.SimulationSeconds - previous.SimulationSeconds,
                    s.Velocity.x, s.Velocity.y, (s.Ground.x - s.PreviousGround.x) / step / BwRules.PlayerSpeed,
                    s.Input.x, s.Input.y, s.InputHeld, s.RouterInput.x, s.RouterInput.y, s.RouterHeld,
                    s.State, s.Action, s.StateTime, s.Stage, s.TimelineTick, s.Pulse, s.Busy ? 1 : 0,
                    s.HasPreMovePhase ? 1 : 0, s.PreMoveTimelineTick, s.PreMoveStage, s.ExpectedMoveScale,
                    s.Hp, s.Height, s.TargetDistance, s.CameraX, s.CameraY, s.CameraSize, BwRules.PlayerSpeed,
                    s.Mobility, BwBeltRules.AttackWindupMoveScale, BwBeltRules.AttackActiveMoveScale, BwBeltRules.AttackRecoveryMoveScale })
                    csv.Append(value.ToString("R", CultureInfo.InvariantCulture)).Append(',');
                csv.Length--; csv.Append('\n');
            }
            File.WriteAllText(Path.Combine(directory, "movement.csv"), csv.ToString());
            var last = samples[samples.Length - 1];
            string summary = string.Format(CultureInfo.InvariantCulture,
                "Movement readback: backend={0}, held_attack={1}, samples={2}, pre_move_windup_samples={3}, pre_move_active_samples={4}, pre_move_recovery_samples={5}, displacement_x={6:R}, simulation_seconds={7:R}, effective_whole_clip_speed={8:R}, profile_PlayerSpeed={9:R}, phase_unavailable_begin_boundary_samples={10}, mobility_policy={11}.\n",
                tier, heldAttack, samples.Length, windup, active, recovery, last.Ground.x - first.Ground.x,
                last.SimulationSeconds - first.SimulationSeconds,
                (last.Ground.x - first.Ground.x) / (last.SimulationSeconds - first.SimulationSeconds),
                BwRules.PlayerSpeed, boundaries, (BwBeltPlayerMobility)last.Mobility);
            File.WriteAllText(Path.Combine(directory, "movement.txt"), summary +
                "State is sampled after the last completed tick. Movement precedes weapon Advance; pre-move phase comes from authoritative ActionTimeline.PreviousTick, never the newer post-step stage. Begin/restart replaces PreviousTick with -1: those boundaries remain recorded and must move, but receive no fabricated pre-phase or phase-ratio assertion. The initial admission tick uses normal speed.\n" +
                "Last-tick deltas are actual Ground-PreviousGround. Acquisition may span multiple simulation ticks; counts describe observed samples, not exhaustive tick occupancy. SmoothAttackV1 windup/active/recovery ratios are 0.75/0.70/0.80; native assertions require positive sustained sampled movement and measured per-phase ratios. This fixture does not modify production rules or poses.\n");
            TestContext.WriteLine(summary);
        }
    }
}
#endif
