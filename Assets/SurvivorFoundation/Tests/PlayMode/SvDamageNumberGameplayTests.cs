#if !SPF_DOTNET_HARNESS
using System;
using System.Collections;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Contracts.Combat;
using SPF.Presentation;
using SPF.Runtime.World;
using SPF.Testing;
using SurvivorFoundation.Game;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace SurvivorFoundation.Tests.PlayMode
{
    public class SvDamageNumberGameplayTests
    {
        [UnityTest]
        public IEnumerator DamageNumbersUseSettledFactsAndRenderRealGlyphs([Values(RenderTier.GpuDriven, RenderTier.DataTexture)] RenderTier tier)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Actual graphics required.");
            if (tier == RenderTier.GpuDriven && !SystemInfo.supportsComputeShaders) Assert.Ignore("Compute tier unsupported.");
            RenderCapabilities.Override = tier;
            var config = SvConfig.CreateDamageNumbersExample(false);
            config.Settings.SpawnPerSecond = config.Settings.SpawnGrowth = config.Settings.EliteEvery = 0;
            config.Settings.XpBase = 100000; config.Settings.HeroHp = config.Settings.BeaconHp = 100000;
            foreach (var enemy in config.Enemies) { enemy.Hp = 1000; enemy.Speed = 0; enemy.Damage = 0; enemy.Shooter = false; }
            var game = SvGameBootstrap.CreateDamageNumbersExample(config); CanvasCapture capture = null;
            string suffix = tier == RenderTier.GpuDriven ? "gpu" : "datatex";
            try
            {
                yield return null; game.StartRun(); yield return UIDriver.WaitUntil(() => game.State.Flow == SvFlow.Playing, 5);
                game.Session.Sync(); game.Session.ManualClock = true; game.InputRouter.enabled = false; game.Governor.AdaptiveQuality = false;
                capture = new CanvasCapture(game.gameObject, game.CameraRig.Camera, 720, 1280);
                var safe = new Rect(0, 0, 720, 1280); game.Hud.MobileHud.SetPreviewViewport(720, 1280, safe);
                Setup(game, 1); game.CameraRig.Snap(); yield return null; game.Renderer.RenderFrame(0);
                FirePulse(game); game.Renderer.RenderFrame(0); game.Session.Pause();
                Assert.AreEqual(1, game.Renderer.DamageNumbers.Active); Assert.IsFalse(game.Renderer.DamageNumbers.Read(0).Critical);
                Assert.AreEqual(20d, game.Renderer.DamageNumbers.Read(0).Amount);
                yield return capture.Save("damage-horde-single-" + suffix, safe);
                TestContext.WriteLine("Horde single label: " + game.Renderer.DamageNumberGlyphsDrawn + " glyphs, " + game.Renderer.DamageNumberBytesUploaded + " uploaded API bytes");
                game.Session.Resume(); Setup(game, 8); game.CameraRig.Snap(); yield return null;
                game.Renderer.RenderFrame(0); Assert.Zero(game.Renderer.DamageNumbers.Active);
                var world = game.Session.World; var journal = world.Resource(AppliedDamageJournal.Key);
                var independent = journal.CreateCursor(); FirePulse(game); game.Renderer.RenderFrame(0);
                Assert.AreEqual(tier, game.Renderer.Tier); AssertLabels(game.Renderer.DamageNumbers);
                int normal = 0, critical = 0; double accepted = 0;
                while (journal.TryRead(ref independent, out var fact)) { accepted += fact.Amount; if (fact.Critical) critical++; else normal++; }
                Assert.AreEqual(6, normal); Assert.AreEqual(2, critical); Assert.AreEqual(200d, accepted);
                Assert.AreEqual(accepted, game.Renderer.DamageNumbers.Stats.AcceptedAmount, .0001);
                float hpLoss = 0; for (int row = 0; row < world.Table(SvKeys.Enemy).Count; row++) hpLoss += 1000 - world.Column(SvKeys.Info)[row].Hp;
                Assert.AreEqual(accepted, hpLoss, .0001, "displayed amounts are accepted HP deltas");
                game.Session.Pause(); var snapshot = game.Session.CaptureSnapshot();
                float age = game.Renderer.DamageNumbers.Read(0).Age;
                game.Renderer.RenderFrame(.5f); game.Renderer.RenderFrame(.5f);
                Assert.AreEqual(age, game.Renderer.DamageNumbers.Read(0).Age, "pause freezes label age");
                CollectionAssert.AreEqual(snapshot, game.Session.CaptureSnapshot(), "views never change accepted damage or critical cadence");
                yield return capture.Save("damage-horde-normal-critical-" + suffix, safe);
                AssertGlyphPixels(game.Renderer, game.CameraRig.Camera, "damage-horde-" + suffix);

                Action warmed = () => { for (int i = 0; i < 64; i++) game.Renderer.RenderFrame(0); };
                warmed(); using (var probe = new ManagedAllocationProbe())
                {
                    probe.Calibrate(); var allocation = probe.Measure(warmed); probe.Calibrate();
                    TestContext.WriteLine("Damage horde paused warmed full renderer / 64 calls: " + allocation.Value + " " + allocation.Metric);
                    Assert.Zero(allocation.Value, "calibrated current-thread allocation in warmed glyph rendering");
                }
                game.Renderer.SetQualityLevel(3); game.Renderer.RenderFrame(0);
                Assert.LessOrEqual(game.Renderer.DamageNumberGlyphsDrawn, 64); Assert.LessOrEqual(game.Renderer.DamageNumbers.Active, 20);
                CollectionAssert.AreEqual(snapshot, game.Session.CaptureSnapshot()); game.Renderer.SetQualityLevel(0);
                uint tick = game.Session.Clock.NextTickIndex; game.Session.RestoreSnapshot(snapshot); game.Renderer.RenderFrame(0);
                Assert.AreEqual(tick, game.Session.Clock.NextTickIndex); Assert.Zero(game.Renderer.DamageNumbers.Active); Assert.Zero(journal.Count);
                game.Session.Resume(); Setup(game, 8); game.Renderer.RenderFrame(0); game.Renderer.enabled = false;
                FirePulse(game); Assert.Greater(journal.Count, 0); game.Renderer.enabled = true; game.Renderer.RenderFrame(0);
                Assert.Zero(game.Renderer.DamageNumbers.Active, "hidden damage is skipped at reenable");
                Setup(game, 8); game.Renderer.RenderFrame(0); FirePulse(game); game.Renderer.RenderFrame(0); AssertLabels(game.Renderer.DamageNumbers);
                game.Renderer.NaturalCharacters = false; game.Renderer.RenderFrame(0); Assert.Zero(game.Renderer.DamageNumbers.Active, "art rebind starts at journal head");
                game.Renderer.NaturalCharacters = true; game.Renderer.RenderFrame(0);
                game.Session.Restart(); game.Renderer.RenderFrame(0); Assert.Zero(game.Renderer.DamageNumbers.Active);

                game.Session.Resume(); Setup(game, 128); game.CameraRig.Snap(); game.Renderer.RenderFrame(0);
                FirePulse(game); game.Renderer.RenderFrame(0); game.Session.Pause();
                Assert.GreaterOrEqual(journal.Count, 64); Assert.Greater(game.Renderer.DamageNumbers.Stats.Dropped, 0);
                Assert.LessOrEqual(game.Renderer.DamageNumbers.Active, 64); Assert.LessOrEqual(game.Renderer.DamageNumberGlyphsDrawn, 192);
                yield return capture.Save("damage-horde-dense-" + suffix, safe);
                game.Renderer.SetQualityLevel(3); game.Renderer.RenderFrame(0);
                yield return capture.Save("damage-horde-dense-low-" + suffix, safe); game.Renderer.SetQualityLevel(0);

                capture.Dispose(); capture = new CanvasCapture(game.gameObject, game.CameraRig.Camera, 360, 640);
                game.Hud.MobileHud.SetPreviewViewport(360, 640, new Rect(0, 0, 360, 640));
                game.Session.Resume(); Setup(game, 24); game.CameraRig.Snap(); game.Renderer.RenderFrame(0);
                yield return CaptureLive(game, capture, suffix); LogAssert.NoUnexpectedReceived();
            }
            finally { capture?.Dispose(); RenderCapabilities.Override = null; Object.Destroy(game.gameObject); Object.Destroy(config); if (Camera.main != null) Object.Destroy(Camera.main.gameObject); }
        }
        static void Setup(SvGameBootstrap game, int count)
        {
            // The real Start command also restores hero/beacon HP after Session.Restart.
            game.State.Send(SvCommandKind.Start); game.Session.Step();
            var world = game.Session.World; world.ClearLevel(); game.State.Flow = SvFlow.Playing;
            game.State.Hero = game.State.HeroPrev = 0; game.State.Input = default; game.State.Facing = new float2(1, 0);
            for (int i = 0; i < game.State.Upgrades.Length; i++) game.State.Upgrades[i] = 0;
            for (int i = 0; i < count; i++)
            {
                float angle = i * 2.399963f; float radius = count <= 8 ? 3.8f : 2.6f + 1.4f * (i % 7) / 6;
                SvSpawner.SpawnEnemy(world, world.Resource(SvKeys.Config), 1, new float2(math.cos(angle), math.sin(angle)) * radius);
            }
        }
        static void FirePulse(SvGameBootstrap game)
        {
            game.State.Input = new InputFrame { Pressed = 1u << SvMobileSkills.Pulse }; game.Session.Step(); game.State.Input = default;
            for (int i = 0; i < 3; i++) game.Session.Step();
        }
        struct LiveSample { public double At; public uint Tick; public ulong Normal, Critical; public int Active, Glyphs, Dropped, Overlap; }
        static IEnumerator CaptureLive(SvGameBootstrap game, CanvasCapture capture, string suffix)
        {
            yield return null; yield return null;
            using (var frames = new BufferedFrameCapture(capture.Target, 90))
            {
                var trace = new LiveSample[90]; game.Session.ManualClock = false; double next = Time.realtimeSinceStartupAsDouble;
                var journal = game.Session.World.Resource(AppliedDamageJournal.Key); int visibleFrames = 0;
                for (int i = 0; i < trace.Length; i++)
                {
                    while (Time.realtimeSinceStartupAsDouble < next) yield return null;
                    game.State.Input = new InputFrame { Move = new float2(i < 45 ? .12f : -.12f, 0), Pressed = i == 4 || i == 48 ? 1u << SvMobileSkills.Pulse : 0 };
                    game.Session.Sync();
                    double at = Time.realtimeSinceStartupAsDouble; frames.Capture(game.Session.Clock.Elapsed);
                    var stats = game.Renderer.DamageNumbers.Stats; if (stats.Glyphs > 0) visibleFrames++;
                    trace[i] = new LiveSample { At = at, Tick = game.Session.Clock.NextTickIndex, Normal = journal.AcceptedNormal, Critical = journal.AcceptedCritical,
                        Active = game.Renderer.DamageNumbers.Active, Glyphs = stats.Glyphs, Dropped = stats.Dropped, Overlap = stats.OverlapDrops };
                    next = at + 1d / 30;
                }
                game.State.Input = default; game.Session.ManualClock = true; game.Session.Sync();
                string directory = frames.Write("damage-horde-live-" + suffix,
                    "Actual automatic-clock horde gameplay. Authored pulse input and ordinary movement; normal/critical labels consume settled HP deltas with fixed budgets. No injected facts or intermediate frames. Acquisition timestamps retained; target30Hz is not a device performance claim.", BufferedFrameFormat.Jpeg95Review);
                var csv = new System.Text.StringBuilder("frame,acquisition_seconds,tick,normal_facts,critical_facts,active_labels,glyphs,dropped,overlap_drops\n");
                for (int i = 0; i < trace.Length; i++)
                {
                    var s = trace[i]; csv.Append(i).Append(',').Append(s.At.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',').Append(s.Tick).Append(',')
                        .Append(s.Normal).Append(',').Append(s.Critical).Append(',').Append(s.Active).Append(',').Append(s.Glyphs).Append(',').Append(s.Dropped).Append(',').Append(s.Overlap).Append('\n');
                }
                System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "damage.csv"), csv.ToString());
                Assert.Greater(journal.AcceptedNormal, 0ul); Assert.Greater(journal.AcceptedCritical, 0ul); Assert.Greater(visibleFrames, 4);
            }
        }
        // Inspect the actual batch prepared by the game renderer. Its normal scene draw remains
        // covered by the main-camera screenshots; this second camera excludes every actor/HUD/effect,
        // so passing pixels must be font glyphs rather than an unrelated non-background scene.
        static void AssertGlyphPixels(object renderer, Camera source, string name)
        {
            var field = renderer.GetType().GetField("m_DamageGlyphs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var batch = (SPF.Presentation.Sprites.SpriteBatch)field.GetValue(renderer);
            Assert.Greater(batch.Count, 0);
            var go = new GameObject("DamageGlyphPixelProbe"); var camera = go.AddComponent<Camera>();
            var target = new RenderTexture(640, 640, 24, RenderTextureFormat.ARGB32);
            var read = new Texture2D(640, 640, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            try
            {
                camera.CopyFrom(source); camera.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                camera.enabled = false; camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black; camera.targetTexture = target; camera.aspect = source.aspect; target.Create();
                camera.Render(); RenderTexture.active = target; read.ReadPixels(new Rect(0, 0, 640, 640), 0, 0); read.Apply(false);
                CountGlyphFill(read.GetPixels32(), out int emptyNormal, out int emptyCritical);
                Assert.Zero(emptyNormal + emptyCritical, "negative control excludes all game art and HUD pixels");
                batch.Draw(new Bounds(Vector3.zero, new Vector3(100000, 100000, 100)), layer: 31);
                camera.Render(); read.ReadPixels(new Rect(0, 0, 640, 640), 0, 0); read.Apply(false);
                CountGlyphFill(read.GetPixels32(), out int normal, out int critical);
                Assert.Greater(normal, 20, "actual normal font fill pixels are present");
                Assert.Greater(critical, 20, "actual critical font fill pixels are present");
                string directory = System.IO.Path.Combine(Application.dataPath, "..", "Artifacts", "Screenshots", "DamageNumbers");
                System.IO.Directory.CreateDirectory(directory);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory, name + "-glyphs.png"), read.EncodeToPNG());
                TestContext.WriteLine($"{name}: glyph sprites={batch.Count}, normal fill pixels={normal}, critical fill pixels={critical}, empty control={emptyNormal + emptyCritical}, API upload={batch.BytesUploaded} bytes");
            }
            finally
            {
                RenderTexture.active = previous; camera.targetTexture = null; target.Release();
                Object.Destroy(target); Object.Destroy(read); Object.Destroy(go);
            }
        }
        static void CountGlyphFill(Color32[] pixels, out int normal, out int critical)
        {
            normal = critical = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                var c = pixels[i];
                if (c.r > 170 && c.g > 160 && c.b > 140 && math.abs(c.r - c.g) < 35) normal++;
                if (c.r > 160 && c.g > 130 && c.g > c.b * 1.3f && c.b < 170) critical++;
            }
        }
        static void AssertLabels(SPF.Presentation.Combat.DamageNumberPool pool)
        {
            Assert.IsNotNull(pool); int normal = 0, critical = 0;
            for (int i = 0; i < pool.Active; i++) { if (pool.Read(i).Critical) critical++; else normal++; }
            Assert.Greater(normal, 0, "normal labels come from settled damage facts");
            Assert.Greater(critical, 0, "critical labels come from the authored outgoing-hit rule");
            Assert.LessOrEqual(pool.Active, pool.Budget.Active); Assert.LessOrEqual(pool.Stats.Glyphs, pool.Budget.Glyphs);
        }
    }
}
#endif
