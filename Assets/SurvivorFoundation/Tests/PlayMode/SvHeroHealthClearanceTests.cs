#if !SPF_DOTNET_HARNESS
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Contracts.Weapons;
using SPF.L2.Weapons;
using SPF.Presentation;
using SPF.Presentation.Animation;
using SPF.Presentation.Combat;
using SPF.Presentation.Sprites;
using SPF.Testing;
using SurvivorFoundation.Game;
using SurvivorFoundation.Presentation;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace SurvivorFoundation.Tests.PlayMode
{
    public class SvHeroHealthClearanceTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly EntityHandle Hero = new EntityHandle(-1, 1);
        static SpriteBatch Health(SvRenderer renderer) => (SpriteBatch)typeof(SvRenderer).GetField("m_Health", Private).GetValue(renderer);

        [UnityTest]
        public IEnumerator ArmedHeroHealthRemainsVisibleOutsideWeaponAndFootMotion(
            [Values(RenderTier.GpuDriven, RenderTier.DataTexture)] RenderTier tier)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Actual graphics required.");
            if (tier == RenderTier.GpuDriven && !SystemInfo.supportsComputeShaders) Assert.Ignore("Compute tier unsupported.");
            RenderCapabilities.Override = tier;
            var config = SvConfig.CreateWeaponCombatExample();
            config.Settings.SpawnPerSecond = config.Settings.SpawnGrowth = config.Settings.EliteEvery = 0;
            config.Settings.BeaconHp = config.Settings.HeroHp = 100000;
            var game = SvGameBootstrap.CreateWeaponCombatExample(config);
            CanvasCapture capture = null;
            string suffix = tier == RenderTier.GpuDriven ? "gpu" : "fallback";
            int oldStaffOverlap = 0, oldBowOverlap = 0, captures = 0;
            try
            {
                yield return null; game.StartRun();
                yield return UIDriver.WaitUntil(() => game.State.Flow == SvFlow.Playing, 5);
                game.Session.Sync(); game.Session.ManualClock = true;
                game.InputRouter.enabled = false; game.Governor.AdaptiveQuality = false;
                game.Session.Clock.Restore(game.Session.Clock.NextTickIndex, game.Session.Clock.Elapsed);
                capture = new CanvasCapture(game.gameObject, game.CameraRig.Camera, 360, 640);
                var safe = new Rect(0, 0, 360, 640);
                game.Hud.MobileHud.SetPreviewViewport(360, 640, safe);
                var weapons = game.Session.World.Resource(SvWeapons.Key);
                game.State.Hero = game.State.HeroPrev = new float2(4, 4);
                game.CameraRig.Snap();
                game.Renderer.RenderFrame(0);
                float2 anchor = Health(game.Renderer).Instances[0].Center - game.State.Hero;
                foreach (int id in new[] { WeaponProfiles.Blade, WeaponProfiles.Sword, WeaponProfiles.Staff, WeaponProfiles.Bow })
                for (int side = -1; side <= 1; side += 2)
                {
                    game.State.Input = new InputFrame { Aim = new float2(side, 0) };
                    for (int i = 0; i < weapons.Current.DurationTicks + 1; i++)
                    { game.Session.Step(); game.Renderer.RenderFrame(1f / 30); }
                    Assert.IsTrue(weapons.RequestEquip(id));
                    for (int i = 0; i < weapons.Profile(id).EquipTicks + 1; i++)
                    { game.Session.Step(); game.Renderer.RenderFrame(1f / 30); }
                    Assert.AreEqual(id, weapons.Equipment.EquippedId);
                    game.State.Facing = new float2(side, 0);
                    game.State.Hp = game.State.MaxHp * (side < 0 ? 1f : .4f);
                    game.CameraRig.Snap();
                    int sampled = 0;
                    for (int tick = 0; tick < weapons.Current.DurationTicks + 3; tick++)
                    {
                        if (tick > 0)
                        {
                            game.State.Input = new InputFrame { Aim = new float2(side, 0), Held = 1u << SvWeapons.AttackButton,
                                Move = new float2(side * .2f, .1f) };
                            game.Session.Step();
                        }
                        game.Renderer.SetQualityLevel(tick % 4); game.Renderer.RenderFrame(1f / 30);
                        Assert.AreEqual(tier, game.Renderer.Tier);
                        Assert.IsTrue(game.Renderer.Characters.TryReadCurrent(Hero, out var motion));
                        var bars = Health(game.Renderer); var bar = bars.Instances[0];
                        Assert.AreEqual(6, bars.Count, "hero and beacon keep their original three-sprite budgets");
                        Assert.Less(math.distance(anchor, bar.Center - motion.PreviousRoot), .00001f);
                        var barRect = BoundsOf(bar);
                        // Inspect final packed render geometry, including whole weapon art and both
                        // feet, rather than merely asserting a chosen anchor constant.
                        Assert.AreEqual(19, game.Renderer.Characters.PartsDrawn);
                        for (int part = 0; part < 19; part++)
                        {
                            var sprite = game.Renderer.Characters.ReadPart(part);
                            if (sprite.Color.w > .01f)
                                Assert.IsFalse(IntersectsSprite(barRect, sprite),
                                    $"HP bar overlaps rendered part {part}, weapon {id}, side {side}, tick {tick}");
                        }
                        var stage = weapons.View(game.Session.InterpolationAlpha).Stage;
                        int bit = 1 << (int)stage;
                        if ((sampled & bit) != 0 || stage == WeaponStage.Equipping) continue;
                        sampled |= bit;
                        var before = game.Session.CaptureSnapshot();
                        string name = $"health-horde-{id}-{side}-{stage}-{suffix}";
                        yield return capture.Save(name, safe);
                        CollectionAssert.AreEqual(before, game.Session.CaptureSnapshot());
                        bars = Health(game.Renderer); barRect = BoundsOf(bars.Instances[0]);
                        var projected = Project(game.CameraRig.Camera, barRect);
                        Assert.Greater(projected.x, 0); Assert.Greater(projected.y, 0);
                        Assert.Less(projected.z, 1); Assert.Less(projected.w, 1);
                        var layout = game.Renderer.DamageLayout;
                        Assert.IsFalse(DamageNumberLayout.Overlaps(projected, layout.HeaderViewport));
                        Assert.IsFalse(DamageNumberLayout.Overlaps(projected, layout.MenuViewport));
                        Assert.IsFalse(DamageNumberLayout.Overlaps(projected, layout.StatusViewport));
                        foreach (var button in game.Hud.MobileHud.Buttons)
                        {
                            var r = capture.RectOf(button.gameObject);
                            Assert.IsFalse(DamageNumberLayout.Overlaps(projected,
                                new float4(r.xMin / 360, r.yMin / 640, r.xMax / 360, r.yMax / 640)));
                        }
                        int green = CountPixels(capture.Pixels, 360, 640,
                            Project(game.CameraRig.Camera, BoundsOf(bars.Instances[2])), true);
                        Assert.Greater(green, 3, "positive actual scene pixels must retain the full or damaged green HP fill");
                        Assert.AreEqual(side < 0 ? 1f : .4f, bars.Instances[2].Size.x / bars.Instances[1].Size.x, .002f);
                        int oldOverlap = OldAnchorArmPixels(game, name);
                        if (id == WeaponProfiles.Staff) oldStaffOverlap += oldOverlap;
                        if (id == WeaponProfiles.Bow) oldBowOverlap += oldOverlap;
                        captures++;
                    }
                    int phases = (1 << (int)WeaponStage.Idle) | (1 << (int)WeaponStage.Windup) |
                        (1 << (int)WeaponStage.Active) | (1 << (int)WeaponStage.Recovery);
                    Assert.AreEqual(phases, sampled & phases, "idle, preparation, release/contact and recovery must all be checked");
                }
                Assert.Greater(oldStaffOverlap, 0, "negative control must reproduce staff/arm pixels under the original overhead bar");
                TestContext.WriteLine($"{suffix}: {captures} actual scene HP captures; old-anchor arm/weapon pixels staff={oldStaffOverlap}, bow={oldBowOverlap}.");
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                capture?.Dispose(); RenderCapabilities.Override = null;
                if (Camera.main != null) Object.Destroy(Camera.main.gameObject);
                Object.Destroy(game.gameObject); Object.Destroy(config);
            }
        }

        static bool IntersectsSprite(float4 rect, PackedSprite sprite)
        {
            float2 half = (rect.zw - rect.xy) * .5f, extent = math.abs(sprite.Size) * .5f;
            float2 delta = (rect.xy + rect.zw) * .5f - sprite.Center;
            float2 right = new float2(math.cos(sprite.Rotation), math.sin(sprite.Rotation));
            float2 up = new float2(-right.y, right.x);
            return math.abs(delta.x) < half.x + extent.x * math.abs(right.x) + extent.y * math.abs(up.x) &&
                math.abs(delta.y) < half.y + extent.x * math.abs(right.y) + extent.y * math.abs(up.y) &&
                math.abs(math.dot(delta, right)) < extent.x + math.dot(half, math.abs(right)) &&
                math.abs(math.dot(delta, up)) < extent.y + math.dot(half, math.abs(up));
        }
        static float4 BoundsOf(PackedSprite sprite)
        {
            float2 size = math.abs(sprite.Size);
            float c = math.abs(math.cos(sprite.Rotation)), s = math.abs(math.sin(sprite.Rotation));
            float2 half = new float2(size.x * c + size.y * s, size.x * s + size.y * c) * .5f;
            return new float4(sprite.Center - half, sprite.Center + half);
        }
        static float4 Project(Camera camera, float4 world)
        {
            var a = camera.WorldToViewportPoint(new Vector3(world.x, world.y, 0));
            var b = camera.WorldToViewportPoint(new Vector3(world.z, world.w, 0));
            return new float4(a.x, a.y, b.x, b.y);
        }
        static int CountPixels(Color32[] pixels, int width, int height, float4 area, bool green)
        {
            int count = 0;
            for (int y = Mathf.Max(0, Mathf.CeilToInt(area.y * height)); y < Mathf.Min(height, Mathf.FloorToInt(area.w * height)); y++)
            for (int x = Mathf.Max(0, Mathf.CeilToInt(area.x * width)); x < Mathf.Min(width, Mathf.FloorToInt(area.z * width)); x++)
            {
                var p = pixels[y * width + x];
                if (green ? p.g > 100 && p.g > p.r * 1.18f && p.g > p.b * 1.18f : p.r > 20 || p.g > 20 || p.b > 20) count++;
            }
            return count;
        }

        // Copy only the actual prepared arm/hand/weapon instances into a test batch. A black
        // isolated camera excludes the hero's head, terrain, health bars, VFX and HUD. The old
        // anchor is a counterfactual regression control, never applied to the live game renderer.
        static int OldAnchorArmPixels(SvGameBootstrap game, string name)
        {
            var presenter = game.Renderer.Characters;
            var art = (NaturalCharacterArt)typeof(GameplayCharacterPresenter).GetField("m_Art", Private).GetValue(presenter);
            var go = new GameObject("Hero arm clearance pixel control"); var camera = go.AddComponent<Camera>();
            var target = new RenderTexture(360, 640, 24, RenderTextureFormat.ARGB32);
            var read = new Texture2D(360, 640, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            using var batch = new SpriteBatch(game.Renderer.Tier, art.Sheet.Texture, BlendKind.Translucent, 10);
            try
            {
                camera.CopyFrom(game.CameraRig.Camera);
                camera.transform.SetPositionAndRotation(game.CameraRig.Camera.transform.position, game.CameraRig.Camera.transform.rotation);
                camera.enabled = false; camera.cullingMask = 1 << 31; camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black; camera.targetTexture = target; target.Create();
                camera.Render(); RenderTexture.active = target;
                read.ReadPixels(new Rect(0, 0, 360, 640), 0, 0); read.Apply(false);
                Assert.Zero(CountPixels(read.GetPixels32(), 360, 640, new float4(0, 0, 1, 1), false), "empty camera is the pixel negative control");
                var instances = batch.Instances;
                foreach (int part in new[] { 0, 1, 11, 12, 13, 14, 15, 16, 17, 18 }) instances[batch.Count++] = presenter.ReadPart(part);
                batch.Draw(new Bounds(Vector3.zero, new Vector3(100000, 100000, 100)), layer: 31);
                camera.Render(); read.ReadPixels(new Rect(0, 0, 360, 640), 0, 0); read.Apply(false);
                var pixels = read.GetPixels32();
                Assert.Greater(CountPixels(pixels, 360, 640, new float4(0, 0, 1, 1), false), 10, "actual arm/weapon art is present");
                Assert.IsTrue(presenter.TryReadCurrent(Hero, out var motion));
                var root = motion.PreviousRoot;
                int old = CountPixels(pixels, 360, 640, Project(camera,
                    new float4(root + new float2(-.6f, 1.45f), root + new float2(.6f, 1.65f))), false);
                Assert.Zero(CountPixels(pixels, 360, 640, Project(camera, BoundsOf(Health(game.Renderer).Instances[0])), false),
                    "actual arm/weapon pixels cannot occupy the new HP bar footprint");
                string directory = Path.Combine(Application.dataPath, "..", "Artifacts", "Screenshots", "HealthClearance");
                Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory, name + "-arms.png"), read.EncodeToPNG());
                return old;
            }
            finally
            {
                RenderTexture.active = previous; camera.targetTexture = null; target.Release();
                Object.Destroy(target); Object.Destroy(read); Object.Destroy(go);
            }
        }
    }
}
#endif
