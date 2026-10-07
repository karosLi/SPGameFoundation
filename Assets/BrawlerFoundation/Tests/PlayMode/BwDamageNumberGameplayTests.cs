#if !SPF_DOTNET_HARNESS
using System;
using System.Collections;
using BrawlerFoundation.Game;
using NUnit.Framework;
using SPF.Contracts;
using SPF.L2.Combat;
using SPF.Presentation;
using SPF.Runtime.World;
using SPF.Testing;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BrawlerFoundation.Tests.PlayMode
{
    public class BwDamageNumberGameplayTests
    {
        [UnityTest]
        public IEnumerator DamageNumbersUseSettledFactsAndRenderRealGlyphs([Values(RenderTier.GpuDriven, RenderTier.DataTexture)] RenderTier tier)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Actual graphics required.");
            if (tier == RenderTier.GpuDriven && !SystemInfo.supportsComputeShaders) Assert.Ignore("Compute tier unsupported.");
            RenderCapabilities.Override = tier;
            // Small authored cadence variant makes both types visible in one real two-target kick.
            var game = BwGameBootstrap.CreateDamageNumbersBelt(criticalRule: new CriticalDamageRule { EveryNthHit = 2, Multiplier = 2 });
            CanvasCapture capture = null; string suffix = tier == RenderTier.GpuDriven ? "gpu" : "datatex";
            try
            {
                yield return null; UIDriver.Click(game.StartButton.gameObject); yield return UIDriver.WaitUntil(() => game.State.Flow == BwFlow.Fighting, 5);
                game.Session.Sync(); game.Session.ManualClock = true; game.InputRouter.enabled = false; game.Governor.AdaptiveQuality = false;
                capture = new CanvasCapture(game.gameObject, game.CameraRig.Camera, 1280, 720);
                var safe = new Rect(0, 0, 1280, 720); game.MobileHud.SetPreviewViewport(1280, 720, safe);
                Setup(game, 1); game.CameraRig.Snap(); yield return null; game.Renderer.RenderFrame(0);
                FireKick(game); game.Renderer.RenderFrame(0); game.Session.Pause();
                Assert.AreEqual(1, game.Renderer.DamageNumbers.Active); Assert.IsFalse(game.Renderer.DamageNumbers.Read(0).Critical);
                Assert.AreEqual(22d, game.Renderer.DamageNumbers.Read(0).Amount);
                yield return capture.Save("damage-belt-single-" + suffix, safe);
                TestContext.WriteLine("Belt single label: " + game.Renderer.DamageNumberGlyphsDrawn + " glyphs, " + game.Renderer.DamageNumberBytesUploaded + " uploaded API bytes");
                game.Session.Resume(); Setup(game, 2); game.CameraRig.Snap(); yield return null; game.Renderer.RenderFrame(0);
                var world = game.Session.World; var journal = world.Resource(AppliedDamageJournal.Key); var independent = journal.CreateCursor();
                FireKick(game); game.Renderer.RenderFrame(0); Assert.AreEqual(tier, game.Renderer.Tier); AssertLabels(game.Renderer.DamageNumbers);
                int normal = 0, critical = 0; double accepted = 0;
                while (journal.TryRead(ref independent, out var fact)) { accepted += fact.Amount; if (fact.Critical) critical++; else normal++; }
                Assert.AreEqual(1, normal); Assert.AreEqual(1, critical); Assert.AreEqual(66d, accepted);
                Assert.AreEqual(accepted, game.Renderer.DamageNumbers.Stats.AcceptedAmount, .0001);
                float hpLoss = 0; for (int row = 1; row < world.Table(BwKeys.Fighter).Count; row++) hpLoss += 1000 - world.Column(BwKeys.Info)[row].Hp;
                Assert.AreEqual(accepted, hpLoss, .0001, "font values use actual accepted HP deltas");
                game.Session.Pause(); var snapshot = game.Session.CaptureSnapshot(); float age = game.Renderer.DamageNumbers.Read(0).Age;
                game.Renderer.RenderFrame(.5f); game.Renderer.RenderFrame(.5f); Assert.AreEqual(age, game.Renderer.DamageNumbers.Read(0).Age);
                CollectionAssert.AreEqual(snapshot, game.Session.CaptureSnapshot(), "paused repeated views cannot change combat or critical cadence");
                yield return capture.Save("damage-belt-normal-critical-" + suffix, safe);
                AssertGlyphPixels(game.Renderer, game.CameraRig.Camera, "damage-belt-" + suffix);
                Action warmed = () => { for (int i = 0; i < 64; i++) game.Renderer.RenderFrame(0); };
                warmed(); using (var probe = new ManagedAllocationProbe())
                {
                    probe.Calibrate(); var allocation = probe.Measure(warmed); probe.Calibrate();
                    TestContext.WriteLine("Damage belt paused warmed full renderer / 64 calls: " + allocation.Value + " " + allocation.Metric);
                    Assert.Zero(allocation.Value, "calibrated current-thread allocation in warmed glyph rendering");
                }
                game.Renderer.SetQualityLevel(3); game.Renderer.RenderFrame(0);
                Assert.LessOrEqual(game.Renderer.DamageNumberGlyphsDrawn, 64); Assert.LessOrEqual(game.Renderer.DamageNumbers.Active, 20);
                CollectionAssert.AreEqual(snapshot, game.Session.CaptureSnapshot()); game.Renderer.SetQualityLevel(0);
                uint tick = game.Session.Clock.NextTickIndex; game.Session.RestoreSnapshot(snapshot); game.Renderer.RenderFrame(0);
                Assert.AreEqual(tick, game.Session.Clock.NextTickIndex); Assert.Zero(game.Renderer.DamageNumbers.Active); Assert.Zero(journal.Count);
                game.Session.Resume(); Setup(game, 2); game.Renderer.RenderFrame(0); game.Renderer.enabled = false;
                FireKick(game); Assert.Greater(journal.Count, 0); game.Renderer.enabled = true; game.Renderer.RenderFrame(0);
                Assert.Zero(game.Renderer.DamageNumbers.Active, "hidden accepted hits must not replay");
                Setup(game, 2); game.Renderer.RenderFrame(0); FireKick(game); game.Renderer.RenderFrame(0); AssertLabels(game.Renderer.DamageNumbers);
                game.Renderer.NaturalCharacters = false; game.Renderer.RenderFrame(0); Assert.Zero(game.Renderer.DamageNumbers.Active);
                game.Renderer.NaturalCharacters = true; game.Renderer.RenderFrame(0);
                game.Session.Restart(); game.Renderer.RenderFrame(0); Assert.Zero(game.Renderer.DamageNumbers.Active);

                game.Session.Resume(); Setup(game, 24); game.CameraRig.Snap(); game.Renderer.RenderFrame(0);
                // Pin the controlled contact fixture only before each real simulation step. Collision,
                // accepted HP loss, hit deduplication and critical classification remain authoritative.
                FireKick(game, pinDenseContact: true); game.Renderer.RenderFrame(0); game.Session.Pause();
                Assert.GreaterOrEqual(journal.Count, 16); Assert.Greater(game.Renderer.DamageNumbers.Stats.OverlapDrops, 0);
                Assert.LessOrEqual(game.Renderer.DamageNumbers.Active, 64); Assert.LessOrEqual(game.Renderer.DamageNumberGlyphsDrawn, 192);
                yield return capture.Save("damage-belt-dense-" + suffix, safe);
                game.Renderer.SetQualityLevel(3); game.Renderer.RenderFrame(0);
                yield return capture.Save("damage-belt-dense-low-" + suffix, safe); game.Renderer.SetQualityLevel(0);

                // Additional fixed frame only: preserve every existing live frame/window and capture.
                var beforeLayout = game.Session.CaptureSnapshot();
                capture.Dispose(); capture = new CanvasCapture(game.gameObject, game.CameraRig.Camera, 1280, 720);
                var notched = new Rect(30, 35, 1220, 615);
                game.MobileHud.SetPreviewViewport(1280, 720, notched); yield return null;
                game.CameraRig.Snap(); game.Renderer.RenderFrame(0); AssertReservedGlyphs(game.Renderer);
                var actualHeader = capture.RectOf(game.MobileHud.SafeRoot.Find("CombatStatsBackdrop").gameObject); var reservedHeader = game.Renderer.DamageLayout.HeaderViewport;
                Assert.That(reservedHeader.x * capture.Width, Is.EqualTo(actualHeader.xMin).Within(1.1f));
                Assert.That(reservedHeader.y * capture.Height, Is.EqualTo(actualHeader.yMin).Within(1.1f));
                Assert.That(reservedHeader.z * capture.Width, Is.EqualTo(actualHeader.xMax).Within(1.1f));
                Assert.That(reservedHeader.w * capture.Height, Is.EqualTo(actualHeader.yMax).Within(1.1f));
                CollectionAssert.AreEqual(beforeLayout, game.Session.CaptureSnapshot());
                yield return capture.Save("damage-belt-safe-landscape-" + suffix, notched);
                TestContext.WriteLine("Damage reservation suppressions=" + game.Renderer.DamageNumbers.Stats.ReservedDrops + "; bounded actor reservation overflow=" + game.Renderer.DamageLayout.ReservationOverflows);

                capture.Dispose(); capture = new CanvasCapture(game.gameObject, game.CameraRig.Camera, 640, 360);
                game.MobileHud.SetPreviewViewport(640, 360, new Rect(0, 0, 640, 360));
                game.Session.Resume(); Setup(game, 12, live: true); game.CameraRig.Snap(); game.Renderer.RenderFrame(0);
                yield return CaptureLive(game, capture, suffix); LogAssert.NoUnexpectedReceived();
            }
            finally { capture?.Dispose(); RenderCapabilities.Override = null; Object.Destroy(game.gameObject); if (Camera.main != null) Object.Destroy(Camera.main.gameObject); }
        }
        static void Setup(BwGameBootstrap game, int count, bool live = false)
        {
            var world = game.Session.World; world.ClearLevel(); game.State.Flow = BwFlow.Fighting; game.State.Input = default;
            BwSpawner.Spawn(world, 0, 0, 1, 0);
            for (int i = 0; i < count; i++)
            {
                float2 ground = live ? new float2(.9f + i / 3 * .68f, (i % 3 - 1) * .68f) : new float2(.9f, (i & 1) == 0 ? -.34f : .34f);
                BwSpawner.Spawn(world, 1, ground, -1, 0);
                var info = world.Column(BwKeys.Info)[i + 1]; info.Hp = info.MaxHp = 1000; world.Column(BwKeys.Info).Set(i + 1, info);
            }
        }
        static void FireKick(BwGameBootstrap game, bool pinDenseContact = false)
        {
            game.State.Input = new InputFrame { Pressed = 1u << BwButton.Kick };
            for (int i = 0; i < 9; i++)
            {
                if (pinDenseContact)
                {
                    var world = game.Session.World;
                    for (int row = 1; row < world.Table(BwKeys.Fighter).Count; row++)
                    {
                        var p = new float2(.9f, (row & 1) == 0 ? -.34f : .34f);
                        world.Column(BwBeltKeys.Ground).Set(row, p); world.Column(BwBeltKeys.PreviousGround).Set(row, p);
                        world.Column(BwKeys.Position).Set(row, BwBeltRules.Project(p, 0)); world.Column(BwKeys.Prev).Set(row, BwBeltRules.Project(p, 0));
                    }
                }
                game.Session.Step(); game.State.Input = default;
            }
        }
        struct LiveSample { public double At; public uint Tick; public ulong Normal, Critical; public int Active, Glyphs, Dropped, Overlap, Reserved, ReservationOverflow; }
        static IEnumerator CaptureLive(BwGameBootstrap game, CanvasCapture capture, string suffix)
        {
            yield return null; yield return null;
            using (var frames = new BufferedFrameCapture(capture.Target, 90))
            {
                var trace = new LiveSample[90]; game.Session.ManualClock = false; double next = Time.realtimeSinceStartupAsDouble;
                var journal = game.Session.World.Resource(AppliedDamageJournal.Key); int visibleFrames = 0;
                for (int i = 0; i < trace.Length; i++)
                {
                    while (Time.realtimeSinceStartupAsDouble < next) yield return null;
                    game.State.Input = new InputFrame { Move = new float2(i > 30 && i < 60 ? .15f : 0, 0), Pressed = i == 4 ? 1u << BwButton.Kick : 0, Held = i >= 25 ? 1u << BwButton.Punch : 0 };
                    game.Session.Sync();
                    double at = Time.realtimeSinceStartupAsDouble; frames.Capture(game.Session.Clock.Elapsed);
                    AssertReservedGlyphs(game.Renderer);
                    var stats = game.Renderer.DamageNumbers.Stats; if (stats.Glyphs > 0) visibleFrames++;
                    trace[i] = new LiveSample { At = at, Tick = game.Session.Clock.NextTickIndex, Normal = journal.AcceptedNormal, Critical = journal.AcceptedCritical,
                        Active = game.Renderer.DamageNumbers.Active, Glyphs = stats.Glyphs, Dropped = stats.Dropped, Overlap = stats.OverlapDrops, Reserved = stats.ReservedDrops, ReservationOverflow = game.Renderer.DamageLayout.ReservationOverflows };
                    next = at + 1d / 30;
                }
                game.State.Input = default; game.Session.ManualClock = true; game.Session.Sync();
                string directory = frames.Write("damage-belt-live-" + suffix,
                    "Actual automatic-clock belt gameplay with real kick, held weapon attacks and movement. Authored every-second-hit x2 cadence; labels use accepted HP deltas. No pinning during this live sequence, no injected facts or intermediate frames. Measured acquisition timestamps; target30Hz is not a device performance claim.", BufferedFrameFormat.Jpeg95Review);
                var csv = new System.Text.StringBuilder("frame,acquisition_seconds,tick,normal_facts,critical_facts,active_labels,glyphs,dropped,overlap_drops,reserved_drops,reservation_overflows\n");
                for (int i = 0; i < trace.Length; i++)
                {
                    var s = trace[i]; csv.Append(i).Append(',').Append(s.At.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',').Append(s.Tick).Append(',')
                        .Append(s.Normal).Append(',').Append(s.Critical).Append(',').Append(s.Active).Append(',').Append(s.Glyphs).Append(',').Append(s.Dropped).Append(',').Append(s.Overlap).Append(',').Append(s.Reserved).Append(',').Append(s.ReservationOverflow).Append('\n');
                }
                System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "damage.csv"), csv.ToString());
                Assert.Greater(journal.AcceptedNormal, 0ul); Assert.Greater(journal.AcceptedCritical, 0ul); Assert.Greater(visibleFrames, 4);
            }
        }
        // Inspect the actual batch prepared by the game renderer. Its normal scene draw remains
        // covered by the main-camera screenshots; this second camera excludes every actor/HUD/effect,
        // so passing pixels must be font glyphs rather than an unrelated non-background scene.
        static void AssertReservedGlyphs(object renderer)
        {
            var type = renderer.GetType();
            var field = type.GetField("m_DamageGlyphs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var batch = (SPF.Presentation.Sprites.SpriteBatch)field.GetValue(renderer);
            var layout = (SPF.Presentation.Combat.DamageNumberLayout)type.GetField("DamageLayout").GetValue(renderer);
            var camera = ((SPF.Shell.CameraRig.FollowCamera2D)type.GetField("Camera").GetValue(renderer)).Camera;
            for (int i = 0; i < batch.Count; i++)
            {
                var glyph = batch.Instances[i]; var rect = new float4(glyph.Center - glyph.Size * .5f, glyph.Center + glyph.Size * .5f);
                Assert.IsTrue(layout.Allows(rect), "actual game batch glyph must clear safe-area/header/menu reservations");
                Assert.Zero(layout.ActorLift(rect), "actual glyph must clear reserved character/head/bar geometry");
                // Independent Unity camera projection includes impact shake, unlike follow/culling bounds.
                var a = camera.WorldToViewportPoint(new Vector3(rect.x, rect.y, 0));
                var b = camera.WorldToViewportPoint(new Vector3(rect.z, rect.w, 0));
                var projected = new float4(a.x, a.y, b.x, b.y); var safe = layout.SafeViewport;
                Assert.GreaterOrEqual(projected.x, safe.x); Assert.GreaterOrEqual(projected.y, safe.y);
                Assert.LessOrEqual(projected.z, safe.z); Assert.LessOrEqual(projected.w, safe.w);
                Assert.IsFalse(SPF.Presentation.Combat.DamageNumberLayout.Overlaps(projected, layout.HeaderViewport));
                Assert.IsFalse(SPF.Presentation.Combat.DamageNumberLayout.Overlaps(projected, layout.MenuViewport));
                Assert.IsFalse(SPF.Presentation.Combat.DamageNumberLayout.Overlaps(projected, layout.StatusViewport));
            }
        }
        static void AssertGlyphPixels(object renderer, Camera source, string name)
        {
            AssertReservedGlyphs(renderer);
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
