#if !SPF_DOTNET_HARNESS
using System;
using System.Collections;
using NUnit.Framework;
using SPF.Contracts;
using SPF.L2.Weapons;
using SPF.Presentation;
using SPF.Presentation.Animation;
using SPF.Presentation.Particles;
using SPF.Testing;
using SurvivorFoundation.Game;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace SurvivorFoundation.Tests.PlayMode
{
    public class SvComposedPulseGameplayTests
    {
        [UnityTest]
        public IEnumerator ComposedPulseUsesRealHudReleasePoseAndResolver(
            [Values(RenderTier.GpuDriven, RenderTier.DataTexture)] RenderTier tier, [Values(false, true)] bool repulse)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Actual graphics required.");
            if (tier == RenderTier.GpuDriven && !SystemInfo.supportsComputeShaders) Assert.Ignore("Compute tier unsupported.");
            RenderCapabilities.Override = tier;
            var config = SvConfig.CreateComposedPulseExample(repulse);
            config.Settings.SpawnPerSecond = config.Settings.SpawnGrowth = config.Settings.EliteEvery = 0;
            config.Settings.XpBase = 100000; config.Settings.BeaconHp = config.Settings.HeroHp = 100000;
            foreach (var e in config.Enemies) { e.Hp = 100; e.Speed = 0; e.Damage = 0; e.Shooter = false; }
            var game = SvGameBootstrap.CreateComposedPulseExample(config);
            CanvasCapture capture = null;
            string suffix = (repulse ? "repulse-" : "wide-") + (tier == RenderTier.GpuDriven ? "gpu" : "fallback");
            try
            {
                yield return null; UIDriver.Click(game.Hud.StartButton.gameObject);
                yield return UIDriver.WaitUntil(() => game.State.Flow == SvFlow.Playing, 5);
                game.Session.Sync(); game.Session.ManualClock = true; game.Governor.AdaptiveQuality = false;
                game.Session.Clock.Restore(game.Session.Clock.NextTickIndex, game.Session.Clock.Elapsed);
                var world = game.Session.World; world.ClearLevel(); game.State.Flow = SvFlow.Playing;
                game.State.Hero = game.State.HeroPrev = 0; game.State.Input = default; game.State.Facing = new float2(1, 0);
                for (int i = 0; i < game.State.Upgrades.Length; i++) game.State.Upgrades[i] = 0;
                var pulse = world.Resource(SvComposedPulseState.Key); var weapons = world.Resource(SvWeapons.Key);
                var target = SvSpawner.SpawnEnemy(world, world.Resource(SvKeys.Config), 1, new float2(4.8f, 0));
                capture = new CanvasCapture(game.gameObject, game.CameraRig.Camera, 720, 1280);
                var safe = new Rect(0, 0, 720, 1280); var hud = game.Hud.MobileHud;
                hud.SetPreviewViewport(720, 1280, safe); game.CameraRig.Snap(); hud.Refresh(); Canvas.ForceUpdateCanvases(); yield return null;
                Assert.AreSame(hud.Buttons[0].gameObject, capture.FirstHit(hud.Buttons[0].gameObject));
                var tap = capture.Pointer(hud.Buttons[0].gameObject, 41);
                ExecuteEvents.Execute(hud.Buttons[0].gameObject, tap, ExecuteEvents.pointerDownHandler);
                ExecuteEvents.Execute(hud.Buttons[0].gameObject, tap, ExecuteEvents.pointerUpHandler);
                yield return null; yield return null; game.Session.Step(); game.InputRouter.enabled = false;
                Assert.AreEqual(1, pulse.Starts); Assert.AreEqual(0, pulse.Releases);
                Assert.AreEqual(100, world.Column(SvKeys.Info)[0].Hp, "actual HUD action first winds up");
                Assert.AreEqual(1, world.Resource(SvMobileSkills.Key).GetSnapshot(0).Charges);
                hud.Refresh(); Assert.IsFalse(hud.Buttons[0].Snapshot.Ready, "HUD projects busy admission; it owns no cooldown clock");
                game.Session.Step(); game.Session.Step(); Assert.AreEqual(100, world.Column(SvKeys.Info)[0].Hp);
                game.Session.Step(); Assert.AreEqual(80, world.Column(SvKeys.Info)[0].Hp); Assert.AreEqual(1, pulse.Releases);
                Assert.AreEqual(1, pulse.LastRelease.AcceptedRequests); Assert.AreEqual(target, pulse.History[0]);
                Assert.AreEqual(4.8f + (repulse ? 1.5f : 0f), world.Column(SvKeys.Position)[0].x, .0001f);
                game.Session.Step(); // alpha zero now samples the actual tick-three release pose.
                var pose = world.Resource(SvWeapons.PoseKey);
                Assert.AreEqual(SvWeapons.PulsePose, pose.ContentId); Assert.AreEqual(3f / 9f, pose.Phase(game.Session.InterpolationAlpha), .00001f);
                var snapshot = game.Session.CaptureSnapshot();
                yield return capture.Save("ability-horde-" + suffix, safe, hud.Buttons[0].gameObject, hud.Buttons[1].gameObject);
                Assert.AreEqual(tier, game.Renderer.Tier);
                Assert.IsTrue(game.Renderer.Characters.TryReadWeapon(new EntityHandle(-1, 1), out var socket));
                // Pulse is radial at the hero, not a weapon strike. The held weapon must remain attached
                // with its authored rigid grip-to-muzzle length while the pulse upper body pose plays.
                float authoredLength = math.distance(weapons.Current.GripOffset, weapons.Current.MuzzleOffset) * SvWeapons.ActorScale;
                Assert.AreEqual(authoredLength, math.distance(socket.PrimaryGrip, socket.Muzzle), .0001f);
                Assert.IsTrue(math.all(math.isfinite(socket.PrimaryGrip)) && math.all(math.isfinite(socket.Muzzle)));
                if (tier == RenderTier.DataTexture)
                { Assert.AreEqual(RenderTier.DataTexture, game.Renderer.WeaponParticles.Renderer.Tier); Assert.AreEqual(ParticleBackend.CpuBurst, game.Renderer.WeaponParticles.Renderer.Backend); }
                for (int i = 0; i < 6; i++) game.Renderer.RenderFrame();
                CollectionAssert.AreEqual(snapshot, game.Session.CaptureSnapshot(), "view/HUD/particle work cannot settle extra damage or consume a charge");
                game.Session.RestoreSnapshot(snapshot); game.Renderer.RenderFrame(); game.Session.Step();
                Assert.AreEqual(1, pulse.Releases); Assert.AreEqual(80, world.Column(SvKeys.Info)[0].Hp);

                if (repulse && Environment.GetEnvironmentVariable("SPF_ABILITY_GAMEPLAY_SEQUENCE") == "1")
                {
                    capture.Dispose(); capture = new CanvasCapture(game.gameObject, game.CameraRig.Camera, 360, 640);
                    hud.SetPreviewViewport(360, 640, new Rect(0, 0, 360, 640)); world.ClearLevel();
                    game.State.Flow = SvFlow.Playing; game.State.Hero = game.State.HeroPrev = 0; game.State.Input = default;
                    for (int row = 0; row < 12; row++) { float a = row * 2.399963f; SvSpawner.SpawnEnemy(world, world.Resource(SvKeys.Config), 1, new float2(math.cos(a), math.sin(a)) * 3.5f); }
                    game.InputRouter.enabled = true; hud.Refresh(); Canvas.ForceUpdateCanvases(); yield return null; yield return null;
                    using (var frames = new BufferedFrameCapture(capture.Target, 90))
                    {
                        var samples = new TraceSample[90];
                        var move = capture.Pointer(hud.Joystick.gameObject, 42); hud.Joystick.OnPointerDown(move);
                        Vector2 origin = move.position; game.Session.ManualClock = false; double next = Time.realtimeSinceStartupAsDouble;
                        for (int i = 0; i < 90; i++)
                        {
                            while (Time.realtimeSinceStartupAsDouble < next) yield return null;
                            move.position = origin + (i < 45 ? new Vector2(45, 15) : new Vector2(-30, -10)); hud.Joystick.OnDrag(move);
                            if (i == 4 || i == 50) { hud.Refresh(); var p = capture.Pointer(hud.Buttons[0].gameObject, 43); hud.Buttons[0].OnPointerDown(p); hud.Buttons[0].OnPointerUp(p); }
                            if (i == 76) { hud.Refresh(); var p = capture.Pointer(hud.Buttons[3].gameObject, 44); hud.Buttons[3].OnPointerDown(p); hud.Buttons[3].OnPointerUp(p); }
                            double acquiredAt = Time.realtimeSinceStartupAsDouble;
                            frames.Capture(game.Session.Clock.NextTickIndex / 30d);
                            samples[i] = new TraceSample { At = acquiredAt, Tick = game.Session.Clock.NextTickIndex, Starts = pulse.Starts, Releases = pulse.Releases,
                                Accepted = pulse.LastRelease.AcceptedRequests, Rejected = pulse.LastRelease.RejectedRequests, Charges = world.Resource(SvMobileSkills.Key).GetSnapshot(0).Charges,
                                PoseTick = pulse.Timeline.Tick, Hero = game.State.Hero };
                            next = acquiredAt + 1d / 30;
                        }
                        hud.Joystick.OnPointerUp(move); game.Session.ManualClock = true; game.Session.Sync();
                        string directory = frames.Write("ability-horde-live-" + suffix,
                            "Actual automatic-clock composed repulse horde, shared mobile joystick and pulse taps, two accepted pulses with bounded outward knock and ordinary weapon re-equip. Existing pulse/weapon art. Target30Hz, measured timestamps retained; encode at normal1x after acquisition.", BufferedFrameFormat.Jpeg95Review);
                        WriteTrace(directory, samples);
                    }
                    Assert.GreaterOrEqual(pulse.Starts, 2); Assert.GreaterOrEqual(pulse.Releases, 2);
                }
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                capture?.Dispose(); RenderCapabilities.Override = null;
                Object.Destroy(game.gameObject); Object.Destroy(config);
                if (Camera.main != null) Object.Destroy(Camera.main.gameObject);
            }
        }
        struct TraceSample { public double At; public uint Tick; public int Starts, Releases, Accepted, Rejected, Charges, PoseTick; public float2 Hero; }
        static void WriteTrace(string directory, TraceSample[] samples)
        {
            var csv = new System.Text.StringBuilder("frame,acquisition_seconds,tick,starts,releases,accepted_requests,rejected_requests,charges,pose_tick,hero_x,hero_y\n");
            var invariant = System.Globalization.CultureInfo.InvariantCulture;
            for (int i = 0; i < samples.Length; i++)
            {
                var t = samples[i]; csv.Append(i).Append(',').Append((t.At - samples[0].At).ToString("F9", invariant)).Append(',').Append(t.Tick).Append(',')
                    .Append(t.Starts).Append(',').Append(t.Releases).Append(',').Append(t.Accepted).Append(',').Append(t.Rejected).Append(',').Append(t.Charges).Append(',')
                    .Append(t.PoseTick).Append(',').Append(t.Hero.x.ToString("R", invariant)).Append(',').Append(t.Hero.y.ToString("R", invariant)).Append('\n');
            }
            System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "ability.csv"), csv.ToString());
        }
    }
}
#endif
