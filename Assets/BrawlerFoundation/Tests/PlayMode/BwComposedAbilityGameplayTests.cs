#if !SPF_DOTNET_HARNESS
using System;
using System.Collections;
using BrawlerFoundation.Game;
using BrawlerFoundation.Systems;
using NUnit.Framework;
using SPF.Contracts;
using SPF.L1.Skeleton;
using SPF.L2.Combat;
using SPF.L2.Weapons;
using SPF.Presentation;
using SPF.Presentation.Animation;
using SPF.Presentation.Particles;
using SPF.Testing;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BrawlerFoundation.Tests.PlayMode
{
    /// <summary>Native graphics only. Reuses the actual belt, original art, fallback, shared HUD,
    /// fixed-tick contacts and read-only character presenter. Acquisition is never encoded inline.</summary>
    public class BwComposedAbilityGameplayTests
    {
        [UnityTest]
        public IEnumerator HudKickEarnsCreditAndTimedHealUsesActualPose([Values(RenderTier.GpuDriven, RenderTier.DataTexture)] RenderTier tier, [Values(false, true)] bool kickCredit)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Actual graphics required.");
            if (tier == RenderTier.GpuDriven && !SystemInfo.supportsComputeShaders) Assert.Ignore("Compute tier unsupported.");
            RenderCapabilities.Override = tier; var game = BwGameBootstrap.CreateComposedAbilityBelt(abilities: kickCredit ? BwComposedAbilityConfig.Default : BwComposedAbilityConfig.DataVariant); CanvasCapture capture = null;
            int expectedCredit = kickCredit ? 6 : 0; float healedHp = 58 + expectedCredit;
            string suffix = (kickCredit ? "credit-" : "data-") + (tier == RenderTier.GpuDriven ? "gpu" : "fallback");
            try
            {
                yield return null; UIDriver.Click(game.StartButton.gameObject); yield return UIDriver.WaitUntil(() => game.State.Flow == BwFlow.Fighting, 5);
                game.Session.Sync(); game.Session.ManualClock = true; game.InputRouter.enabled = false; game.Governor.AdaptiveQuality = false;
                game.Session.Clock.Restore(game.Session.Clock.NextTickIndex, game.Session.Clock.Elapsed);
                capture = new CanvasCapture(game.gameObject, game.CameraRig.Camera, 1280, 720); var safe = new Rect(0, 0, 1280, 720);
                game.MobileHud.SetPreviewViewport(1280, 720, safe); Setup(game); game.CameraRig.Snap();
                yield return null; yield return null; Canvas.ForceUpdateCanvases();
                var world = game.Session.World; var abilities = world.Resource(BwComposedAbilityState.Key); var pose = world.Resource(BwWeapons.PoseKey);
                Assert.AreEqual(tier, game.Renderer.Tier); Assert.AreEqual(tier, game.Renderer.WeaponParticles.Renderer.Tier);
                if (tier == RenderTier.DataTexture) Assert.AreEqual(ParticleBackend.CpuBurst, game.Renderer.WeaponParticles.Renderer.Backend);
                var weapon = world.Resource(BwWeapons.Key); var handles = world.Table(BwKeys.Fighter).Handles;
                Assert.AreSame(game.MobileHud.Buttons[1].gameObject, capture.FirstHit(game.MobileHud.Buttons[1].gameObject));
                SendHudRequest(game, capture, 1); game.Session.Step(); game.State.Input = default;
                Assert.AreEqual(1, abilities.AcceptedKicks); Assert.AreEqual(BwWeapons.KickPose, pose.ContentId);
                Step(game, 8); Assert.AreEqual(978, world.Column(BwKeys.Info)[1].Hp); Assert.AreEqual(expectedCredit, abilities.HealCredit);
                Assert.AreEqual(1, abilities.SettledKickHits); Assert.AreEqual(1, world.Resource(BwMobileSkills.Key).GetSnapshot(1).Charges);
                var rig = world.Resource(BwKeys.Rig); var source = world.Column(BwKeys.Info)[0]; var ground = world.Column(BwBeltKeys.Ground); var motion = world.Column(BwBeltKeys.Motion);
                using (var locals = new NativeArray<BoneLocal>(rig.Asset.BoneCount * 2, Allocator.Temp))
                using (var bones = new NativeArray<BoneWorld>(rig.Asset.BoneCount, Allocator.Temp))
                {
                    var tip = BwProbe.Tip(rig, source, world.Column(BwKeys.Anim)[0], new float2(ground[0].x, motion[0].Height), locals, bones);
                    var hurt = new GroundHurtBox { Ground = ground[1], HalfWidth = BwRules.BodyHalfWidth, HalfDepth = BwBeltRules.BodyDepth, Bottom = motion[1].Height + BwRules.HurtBottom, Top = motion[1].Height + BwRules.HurtTop };
                    Assert.IsTrue(GroundCombatQueries.ProbeOverlaps(tip.x, ground[0].y, tip.y, BwRules.ProbeRadius, .5f, hurt), "damage comes from the actual authored shin-tip socket in ground/height space");
                    TestContext.WriteLine($"Composed belt kick contact: tip=({tip.x:R},{tip.y:R}), depth={ground[0].y:R}, phase={source.StateTime / rig.Attack(AttackKind.Kick).Duration:R}, HP={world.Column(BwKeys.Info)[1].Hp:R}, bank={abilities.HealCredit}");
                }
                yield return capture.Save("ability-belt-kick-contact-" + suffix, safe, game.MobileHud.Buttons[1].gameObject);
                Assert.IsTrue(game.Renderer.Characters.TryRead(handles[0], out var kickMotion)); Assert.AreEqual(BwWeapons.KickPose, kickMotion.Skill.ProfileId);
                Step(game, 24); SendHudRequest(game, capture, 3); game.Session.Step(); game.State.Input = default;
                Assert.AreEqual(40, world.Column(BwKeys.Info)[0].Hp); Assert.AreEqual(expectedCredit, abilities.HealCredit); Assert.IsTrue(abilities.PendingHeal);
                Assert.AreEqual(BwWeapons.HealPose, pose.ContentId); Step(game, 8);
                Assert.AreEqual(40, world.Column(BwKeys.Info)[0].Hp, "admitted heal still waits for its existing pose contact marker");
                var beforeMarker = game.Session.CaptureSnapshot(); game.Renderer.RenderFrame(); game.MobileHud.Refresh();
                CollectionAssert.AreEqual(beforeMarker, game.Session.CaptureSnapshot());
                game.Session.Step(); Assert.AreEqual(BwComposedAbilityState.HealReleaseTick, pose.Timeline.Tick);
                Assert.AreEqual(healedHp, world.Column(BwKeys.Info)[0].Hp); Assert.AreEqual(1, abilities.AppliedHeals); Assert.Zero(abilities.HealCredit);
                Assert.AreEqual((int)math.ceil(GameplaySkillProfiles.Get(BwWeapons.HealPose).Contact * BwComposedAbilityState.HealDurationTicks), BwComposedAbilityState.HealReleaseTick);
                // Freeze interpolation at the settled marker for pose inspection, without advancing simulation.
                game.Session.Pause(); yield return capture.Save("ability-belt-heal-marker-" + suffix, safe, game.MobileHud.Buttons[3].gameObject);
                Assert.IsTrue(game.Renderer.Characters.TryRead(handles[0], out var healMotion)); Assert.AreEqual(BwWeapons.HealPose, healMotion.Skill.ProfileId);
                var afterMarker = game.Session.CaptureSnapshot(); game.Renderer.RenderFrame(); game.Renderer.RenderFrame();
                CollectionAssert.AreEqual(afterMarker, game.Session.CaptureSnapshot(), "HUD, pause and repeated renderer reads never settle abilities");
                game.Session.RestoreSnapshot(beforeMarker); game.Renderer.RenderFrame(); Assert.AreEqual(40, world.Column(BwKeys.Info)[0].Hp);
                game.Session.Resume(); game.Session.Step(); Assert.AreEqual(healedHp, world.Column(BwKeys.Info)[0].Hp); Assert.AreEqual(1, abilities.AppliedHeals);
                Step(game, 30); Assert.AreEqual(healedHp, world.Column(BwKeys.Info)[0].Hp); Assert.AreEqual(1, abilities.AcceptedHeals);

                // Existing weapon equipment still uses its real canonical socket after ability recovery.
                weapon.RequestEquip(WeaponProfiles.Sword); Step(game, weapon.Profile(WeaponProfiles.Sword).EquipTicks);
                SendHudRequest(game, capture, 0); game.Session.Step(); game.State.Input = default; Step(game, weapon.Current.Active.From + 1);
                yield return capture.Save("ability-belt-weapon-contact-" + suffix, safe);
                Assert.IsTrue(game.Renderer.Characters.TryReadWeapon(handles[0], out var socket));
                float2 root = world.Column(BwKeys.Position)[0]; float2 canonical = root + weapon.Current.MuzzleOffset * BwWeapons.ActorScale;
                Assert.Less(math.distance(canonical, socket.Muzzle), .17f, "composed abilities preserve the existing equipment socket adapter");

                if (kickCredit && Environment.GetEnvironmentVariable("SPF_ABILITY_GAMEPLAY_SEQUENCE") == "1")
                {
                    capture.Dispose(); capture = new CanvasCapture(game.gameObject, game.CameraRig.Camera, 640, 360);
                    game.MobileHud.SetPreviewViewport(640, 360, new Rect(0, 0, 640, 360)); Setup(game); game.CameraRig.Snap();
                    Canvas.ForceUpdateCanvases(); yield return null; yield return null;
                    using (var frames = new BufferedFrameCapture(capture.Target, 120))
                    {
                        var samples = new TraceSample[120]; game.Session.ManualClock = false; double next = Time.realtimeSinceStartupAsDouble;
                        for (int frame = 0; frame < samples.Length; frame++)
                        {
                            while (Time.realtimeSinceStartupAsDouble < next) yield return null;
                            if (frame == 10) SendHudRequest(game, capture, 1);
                            if (frame == 55) SendHudRequest(game, capture, 3);
                            double acquired = Time.realtimeSinceStartupAsDouble; frames.Capture(game.Session.Clock.NextTickIndex / 60d);
                            samples[frame] = new TraceSample { Acquired = acquired, Tick = game.Session.Clock.NextTickIndex, Pose = pose.ContentId, PoseTick = pose.Timeline.Tick,
                                Hp = world.Column(BwKeys.Info)[0].Hp, EnemyHp = world.Column(BwKeys.Info)[1].Hp, Credit = abilities.HealCredit,
                                KickCharges = world.Resource(BwMobileSkills.Key).GetSnapshot(1).Charges, HealCharges = world.Resource(BwMobileSkills.Key).GetSnapshot(3).Charges,
                                Hits = abilities.SettledKickHits, Heals = abilities.AppliedHeals };
                            next = acquired + 1d / 30;
                        }
                        game.Session.ManualClock = true;
                        string directory = frames.Write("ability-belt-live-" + suffix,
                            "Actual automatic-clock composed belt gameplay, shared HUD kick and heal presses. Unique bone-tip kick damage; credit rule enabled=" + kickCredit + ". Heal spends its charge on admission and applies its data amount plus banked credit at existing pose tick9/18. Original art and selected render tier. Target30Hz; measured acquisition timestamps retained, no generated intermediate frames.", BufferedFrameFormat.Jpeg95Review);
                        WriteTrace(directory, samples); Assert.AreEqual(1, abilities.SettledKickHits); Assert.AreEqual(1, abilities.AppliedHeals);
                        Assert.AreEqual(healedHp, world.Column(BwKeys.Info)[0].Hp); Assert.Zero(abilities.HealCredit);
                    }
                }
                LogAssert.NoUnexpectedReceived();
            }
            finally { capture?.Dispose(); RenderCapabilities.Override = null; if (Camera.main != null) Object.Destroy(Camera.main.gameObject); Object.Destroy(game.gameObject); }
        }
        static void Setup(BwGameBootstrap game)
        {
            var world = game.Session.World; world.ClearLevel(); game.State.Flow = BwFlow.Fighting; game.State.Input = default;
            BwSpawner.Spawn(world, 0, 0, 1, 0); BwSpawner.Spawn(world, 1, new float2(.9f, 0), -1, 0);
            var player = world.Column(BwKeys.Info)[0]; player.Hp = 40; world.Column(BwKeys.Info).Set(0, player);
            var target = world.Column(BwKeys.Info)[1]; target.Hp = target.MaxHp = 1000; world.Column(BwKeys.Info).Set(1, target); game.MobileHud.Refresh();
        }
        static void SendHudRequest(BwGameBootstrap game, CanvasCapture capture, int slot)
        {
            game.MobileHud.Refresh(); Canvas.ForceUpdateCanvases(); var button = game.MobileHud.Buttons[slot];
            Assert.IsTrue(button.Snapshot.Enabled); var pointer = capture.Pointer(button.gameObject, 17 + slot);
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerUpHandler);
            var frame = default(InputFrame); Assert.IsTrue(game.MobileHud.Input.TryRead(ref frame));
            Assert.IsTrue(frame.WasPressed(slot)); game.InputRouter.Sink(frame);
        }
        static void Step(BwGameBootstrap game, int count) { for (int i = 0; i < count; i++) game.Session.Step(); }
        struct TraceSample { public double Acquired; public long Tick; public int Pose, PoseTick, Credit, KickCharges, HealCharges, Hits, Heals; public float Hp, EnemyHp; }
        static void WriteTrace(string directory, TraceSample[] samples)
        {
            var csv = new System.Text.StringBuilder("frame,acquisition_seconds,tick,pose,pose_tick,player_hp,enemy_hp,heal_credit,kick_charges,heal_charges,settled_kick_hits,applied_heals\n");
            var invariant = System.Globalization.CultureInfo.InvariantCulture;
            for (int i = 0; i < samples.Length; i++)
            {
                var s = samples[i]; csv.Append(i).Append(',').Append((s.Acquired - samples[0].Acquired).ToString("F9", invariant)).Append(',').Append(s.Tick).Append(',').Append(s.Pose).Append(',').Append(s.PoseTick)
                    .Append(',').Append(s.Hp.ToString("R", invariant)).Append(',').Append(s.EnemyHp.ToString("R", invariant)).Append(',').Append(s.Credit).Append(',').Append(s.KickCharges).Append(',').Append(s.HealCharges).Append(',').Append(s.Hits).Append(',').Append(s.Heals).Append('\n');
            }
            System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "ability.csv"), csv.ToString());
        }
    }
}
#endif
