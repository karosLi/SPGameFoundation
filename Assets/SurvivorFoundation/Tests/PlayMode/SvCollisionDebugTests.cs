#if !SPF_DOTNET_HARNESS
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Contracts.Combat;
using SPF.L2.Weapons;
using SPF.Presentation;
using SPF.Presentation.Combat;
using SPF.Shell.UI;
using SPF.Testing;
using SurvivorFoundation.Game;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace SurvivorFoundation.Tests.PlayMode
{
    /// <summary>Native portrait gameplay evidence with automatic-clock acquisition timestamps.</summary>
    public class SvCollisionDebugTests
    {
        [UnityTest]
        public IEnumerator WeaponHordeOverlayShowsActualContactsWithoutChangingGameplay(
            [Values(RenderTier.GpuDriven, RenderTier.DataTexture)] RenderTier tier)
        {
            RequireGraphics(tier);
            RenderCapabilities.Override = tier;
            var config = SvConfig.CreateWeaponCombatExample();
            config.Settings.SpawnPerSecond = config.Settings.SpawnGrowth = config.Settings.EliteEvery = 0;
            config.Settings.XpBase = config.Settings.BeaconHp = config.Settings.HeroHp = 100000;
            foreach (var enemy in config.Enemies)
            {
                enemy.Hp = 10000;
                enemy.Speed = enemy.Damage = 0;
                enemy.Shooter = false;
            }
            var game = SvGameBootstrap.CreateWeaponCombatExample(config);
            CanvasCapture capture = null;
            string suffix = tier == RenderTier.GpuDriven ? "gpu" : "fallback";
            try
            {
                yield return null;
                game.StartRun();
                yield return UIDriver.WaitUntil(() => game.State.Flow == SvFlow.Playing, 5);
                game.Session.Sync();
                game.Session.ManualClock = true;
                game.InputRouter.enabled = false;
                game.Governor.AdaptiveQuality = false;
                game.Session.Clock.Restore(game.Session.Clock.NextTickIndex, game.Session.Clock.Elapsed);
                var weapons = game.Session.World.Resource(SvWeapons.Key);
                var overlay = game.CollisionOverlay;
                Assert.IsNotNull(overlay, "the weapon example must attach its diagnostic component");
                Assert.IsFalse(overlay.ShowCollisionDebug, "diagnostics are opt-in");
                overlay.RenderFrame();
                Assert.IsFalse(weapons.CollisionDebug.Enabled);
                Assert.AreEqual(0, overlay.PrimitivesDrawn);
                Assert.AreEqual(0, weapons.CollisionDebug.Count);

                PrepareTargets(game, false);
                capture = new CanvasCapture(game.gameObject, game.CameraRig.Camera, 720, 1280);
                var safe = new Rect(0, 0, 720, 1280);
                game.Hud.MobileHud.SetPreviewViewport(720, 1280, safe);
                game.Renderer.RenderFrame();
                game.CameraRig.Snap();
                overlay.ShowCollisionDebug = true;
                overlay.RenderFrame();
                Assert.IsTrue(weapons.CollisionDebug.Enabled);
                int hitsBefore = weapons.AcceptedHits;
                float hpBefore = game.Session.World.Column(SvKeys.Info)[0].Hp;
                for (int i = 0; i < weapons.Current.DurationTicks * 2; i++)
                {
                    game.Session.Step();
                    if (HasReason(weapons.CollisionDebug, CombatContactReason.Accepted)) break;
                }
                Assert.Greater(weapons.AcceptedHits, hitsBefore, "the actual horde resolver must accept the auto-targeted strike");
                Assert.Less(game.Session.World.Column(SvKeys.Info)[0].Hp, hpBefore, "the current target must take real queued damage");
                Assert.IsTrue(HasReason(weapons.CollisionDebug, CombatContactReason.Accepted));
                Assert.IsTrue(HasReason(weapons.CollisionDebug, CombatContactReason.GroundMiss), "the off-axis target must be a real broadphase rejection");
                Assert.AreEqual(weapons.Tick, weapons.CollisionDebug.Tick);
                AssertTraceBounds(weapons.CollisionDebug);

                byte[] snapshot = game.Session.CaptureSnapshot();
                for (int i = 0; i < 8; i++) overlay.RenderFrame();
                CollectionAssert.AreEqual(snapshot, game.Session.CaptureSnapshot(), "overlay-only rendering must not alter any saved gameplay state");
                AssertOverlayBounds(overlay.PrimitivesDrawn, overlay.SegmentsDrawn, overlay.DroppedShapes);
                string name = "collision-horde-debug-" + suffix;
                WriteTrace(name, weapons.CollisionDebug);
                yield return capture.Save(name, safe, game.Hud.MobileHud.Buttons[3].gameObject);
                var enabledPixels = (Color32[])capture.Pixels.Clone();

                overlay.ShowCollisionDebug = false;
                overlay.RenderFrame();
                AssertStopped(weapons.CollisionDebug, overlay.PrimitivesDrawn, overlay.SegmentsDrawn);
                yield return capture.Save(name + "-disabled", safe);
                AssertRolePixels(name, enabledPixels, capture.Pixels, capture.Width, capture.Height);
                CollectionAssert.AreEqual(snapshot, game.Session.CaptureSnapshot(), "capturing and toggling the debug view must not modify gameplay");
                overlay.ShowCollisionDebug = true;
                overlay.RenderFrame();
                Assert.IsTrue(weapons.CollisionDebug.Enabled);
                Assert.AreEqual(0, weapons.CollisionDebug.Count, "re-enabling cannot replay the old contact list");
                Assert.IsFalse(weapons.CollisionDebug.HasAccepted);
                overlay.enabled = false;
                AssertStopped(weapons.CollisionDebug, overlay.PrimitivesDrawn, overlay.SegmentsDrawn);
                overlay.enabled = true;
                overlay.RenderFrame();
                Assert.IsTrue(weapons.CollisionDebug.Enabled);

                PrepareTargets(game, true);
                Equip(game, weapons, WeaponProfiles.Staff);
                overlay.RenderFrame();
                hitsBefore = weapons.AcceptedHits;
                hpBefore = game.Session.World.Column(SvKeys.Info)[0].Hp;
                for (int i = 0; i < weapons.TickRate * 3; i++)
                {
                    game.Session.Step();
                    if (HasReason(weapons.CollisionDebug, CombatContactReason.Accepted)) break;
                }
                Assert.Greater(weapons.AcceptedHits, hitsBefore);
                Assert.Less(game.Session.World.Column(SvKeys.Info)[0].Hp, hpBefore);
                Assert.IsTrue(HasReason(weapons.CollisionDebug, CombatContactReason.Accepted));
                var contact = weapons.CollisionDebug.LastAccepted;
                Assert.Greater(contact.Scope, 0, "this witness must come from a projectile scope");
                Assert.Greater(math.distance(contact.From, contact.To), 0, "the captured projectile must actually travel");
                Assert.That(contact.Fraction, Is.InRange(0f, 1f));
                Assert.Less(math.distance(contact.Contact, math.lerp(contact.From, contact.To, contact.Fraction)), .0001f);
                Assert.AreEqual(game.Session.World.Table(SvKeys.Enemy).Count, weapons.CollisionDebug.MovementRowsExamined,
                    "the projectile pass records its one actual target-movement-bound scan");
                overlay.RenderFrame();
                AssertOverlayBounds(overlay.PrimitivesDrawn, overlay.SegmentsDrawn, overlay.DroppedShapes);
                WriteTrace("collision-horde-debug-projectile-" + suffix, weapons.CollisionDebug);
                yield return capture.Save("collision-horde-debug-projectile-" + suffix, safe);

                byte[] projectileSnapshot = game.Session.CaptureSnapshot();
                game.Session.RestoreSnapshot(projectileSnapshot);
                overlay.RenderFrame();
                Assert.AreEqual(0, weapons.CollisionDebug.Count);
                Assert.IsFalse(weapons.CollisionDebug.HasAccepted);
                game.Session.World.ClearLevel();
                game.State.Flow = SvFlow.Playing;
                overlay.RenderFrame();
                Assert.AreEqual(0, weapons.CollisionDebug.Count);
                Assert.IsFalse(weapons.CollisionDebug.HasAccepted);
                Assert.AreEqual(2, overlay.PrimitivesDrawn, "only the current hero body/hurt shapes remain after the horde is cleared");

                PrepareTargets(game, true);
                Equip(game, weapons, WeaponProfiles.Staff);
                capture.Dispose();
                capture = new CanvasCapture(game.gameObject, game.CameraRig.Camera, 360, 640);
                game.Hud.MobileHud.SetPreviewViewport(360, 640, new Rect(0, 0, 360, 640));
                game.Renderer.RenderFrame();
                game.CameraRig.Snap();
                Canvas.ForceUpdateCanvases();
                yield return null;
                yield return null;
                using (var frames = new BufferedFrameCapture(capture.Target, 45))
                {
                    long firstTick = weapons.Tick;
                    hitsBefore = weapons.AcceptedHits;
                    int releasesBefore = weapons.Releases;
                    game.Session.ManualClock = false;
                    double next = Time.realtimeSinceStartupAsDouble;
                    for (int i = 0; i < frames.Capacity; i++)
                    {
                        do { yield return null; } while (Time.realtimeSinceStartupAsDouble < next);
                        double acquiredAt = Time.realtimeSinceStartupAsDouble;
                        Assert.IsTrue(frames.Capture(weapons.Tick / (double)weapons.TickRate));
                        next = acquiredAt + 1d / 30;
                    }
                    game.Session.ManualClock = true;
                    Assert.AreEqual(45, frames.Count);
                    Assert.Greater(weapons.Tick, firstTick, "the recording must advance the actual automatic simulation clock");
                    Assert.Greater(weapons.Releases, releasesBefore);
                    Assert.Greater(weapons.AcceptedHits, hitsBefore);
                    frames.Write("collision-horde-debug-live-" + suffix,
                        "Actual automatic-clock portrait weapon horde with auto-targeted staff flights and collision overlay. 45 unmodified GPU readbacks, target 30 Hz; measured acquisition times and simulation annotations are retained. This is visual/contact evidence, not proof of game feel or device frame pacing.");
                }
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                if (game != null && game.Session != null) game.Session.ManualClock = true;
                capture?.Dispose();
                RenderCapabilities.Override = null;
                if (Camera.main != null) Object.Destroy(Camera.main.gameObject);
                Object.Destroy(game.gameObject);
                Object.Destroy(config);
            }
        }

        static void PrepareTargets(SvGameBootstrap game, bool ranged)
        {
            var world = game.Session.World;
            world.ClearLevel();
            game.State.Flow = SvFlow.Playing;
            game.State.Hero = game.State.HeroPrev = 0;
            game.State.Facing = new float2(1, 0);
            game.State.Input = default;
            var runtime = world.Resource(SvKeys.Config);
            SvSpawner.SpawnEnemy(world, runtime, 1, new float2(ranged ? 4.13f : 1f, 0));
            SvSpawner.SpawnEnemy(world, runtime, 1, new float2(ranged ? 4.4f : 1.4f, .9f));
        }

        static void Equip(SvGameBootstrap game, WeaponRuntime weapons, int id)
        {
            Assert.IsTrue(weapons.RequestEquip(id));
            int limit = weapons.Current.DurationTicks + weapons.Profile(id).EquipTicks + 2;
            for (int i = 0; i < limit && (weapons.Equipment.EquippedId != id || weapons.Equipment.PendingId != 0 || weapons.Equipment.EquipRemaining != 0); i++) game.Session.Step();
            Assert.AreEqual(id, weapons.Equipment.EquippedId);
            Assert.AreEqual(0, weapons.Equipment.PendingId);
            Assert.AreEqual(0, weapons.Equipment.EquipRemaining);
        }

        static void RequireGraphics(RenderTier tier)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Native graphics are required for actual GPU evidence.");
            if (tier == RenderTier.GpuDriven && (!SystemInfo.supportsComputeShaders || SystemInfo.maxComputeBufferInputsVertex < 4)) Assert.Ignore("The actual device does not support this GPU render tier.");
        }

        static bool HasReason(CombatTraceBuffer trace, CombatContactReason reason)
        {
            for (int i = 0; i < trace.Count; i++) if (trace.Entries[i].Reason == reason) return true;
            return false;
        }

        static void AssertTraceBounds(CombatTraceBuffer trace)
        {
            Assert.That(trace.Count, Is.InRange(1, trace.Entries.Length));
            Assert.AreEqual(0, trace.Dropped, "this deliberately small scene must fit the preallocated trace buffer");
            for (int i = 0; i < trace.Count; i++)
            {
                var entry = trace.Entries[i];
                Assert.AreEqual(trace.Tick, entry.Tick);
                Assert.That(entry.Fraction, Is.InRange(0f, 1f));
                Assert.IsTrue(math.all(math.isfinite(entry.Contact)));
            }
        }

        static void AssertOverlayBounds(int primitives, int segments, int dropped)
        {
            Assert.That(primitives, Is.InRange(1, CombatDebugOverlay.PrimitiveCapacity));
            Assert.That(segments, Is.InRange(1, CombatDebugOverlay.SegmentCapacity));
            Assert.AreEqual(0, dropped, "the small contact scene must fit the diagnostic shape budget");
        }

        static void AssertStopped(CombatTraceBuffer trace, int primitives, int segments)
        {
            Assert.IsFalse(trace.Enabled);
            Assert.AreEqual(0, trace.Count);
            Assert.IsFalse(trace.HasAccepted);
            Assert.AreEqual(0, primitives);
            Assert.AreEqual(0, segments);
        }

        static void WriteTrace(string name, CombatTraceBuffer trace)
        {
            var csv = new StringBuilder("tick,scope,owner_index,owner_generation,target_index,target_generation,reason,damage_outcome,from_x,from_y,to_x,to_y,contact_x,contact_y,height,radius,toi_fraction\n");
            for (int i = 0; i < trace.Count; i++)
            {
                var t = trace.Entries[i];
                csv.Append(t.Tick).Append(',').Append(t.Scope).Append(',').Append(t.Owner.Index).Append(',').Append(t.Owner.Generation).Append(',').Append(t.Target.Index).Append(',').Append(t.Target.Generation).Append(',').Append(t.Reason).Append(',').Append(t.DamageOutcome);
                foreach (float v in new[] { t.From.x, t.From.y, t.To.x, t.To.y, t.Contact.x, t.Contact.y, t.Height, t.Radius, t.Fraction }) csv.Append(',').Append(v.ToString("R", CultureInfo.InvariantCulture));
                csv.Append('\n');
            }
            string dir = Path.Combine(Application.dataPath, "..", "Artifacts", "Screenshots", "MobileHud");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, name + "-contacts.csv"), csv.ToString());
            TestContext.WriteLine(name + ": actual tick " + trace.Tick + ", contacts " + trace.Count + ", dropped " + trace.Dropped + ", maximum target movement " + trace.MaxTargetMovement.ToString("R", CultureInfo.InvariantCulture));
        }

        static void AssertRolePixels(string name, Color32[] enabled, Color32[] disabled, int width, int height)
        {
            Assert.AreEqual(enabled.Length, disabled.Length);
            var counts = new int[5];
            // Restrict evidence to the central gameplay viewport, away from the edge HUD controls.
            for (int y = height / 4; y < height * 4 / 5; y++)
                for (int x = width * 3 / 20; x < width * 17 / 20; x++)
                {
                    int at = y * width + x;
                    var p = enabled[at]; var b = disabled[at];
                    if (Math.Abs(p.r - b.r) + Math.Abs(p.g - b.g) + Math.Abs(p.b - b.b) < 80) continue;
                    if (p.r < 110 && p.g > 180 && p.b > 180) counts[0]++;
                    if (p.r > 180 && p.g < 145 && p.b > 145) counts[1]++;
                    if (p.r > 180 && p.g > 110 && p.b < 110) counts[2]++;
                    if (p.r < 155 && p.g > 180 && p.b < 140) counts[3]++;
                    if (p.r > 180 && p.g < 145 && p.b < 145) counts[4]++;
                }
            string[] roles = { "cyan body", "magenta hurt", "amber attack", "green accepted", "red rejected" };
            var report = new StringBuilder("Actual enabled-versus-disabled GPU pixels in the central gameplay viewport.\n");
            for (int i = 0; i < counts.Length; i++) report.Append(roles[i]).Append(": ").Append(counts[i]).Append('\n');
            File.WriteAllText(Path.Combine(Application.dataPath, "..", "Artifacts", "Screenshots", "MobileHud", name + "-pixels.txt"), report.ToString());
            TestContext.WriteLine(report.ToString());
            for (int i = 0; i < counts.Length; i++) Assert.Greater(counts[i], 2, "actual GPU readback is missing visible " + roles[i] + " diagnostics");
        }
    }
}
#endif
