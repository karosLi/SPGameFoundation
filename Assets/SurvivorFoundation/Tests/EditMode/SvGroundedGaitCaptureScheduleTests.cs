using System;
using System.Reflection;
using NUnit.Framework;
using SPF.Contracts;
using SPF.L2.Weapons;
using SPF.Presentation;
using SPF.Presentation.Animation;
using SurvivorFoundation.Presentation;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SurvivorFoundation.Tests
{
    /// <summary>
    /// Preflights the native 160-frame capture fixture with real fixed-tick simulation and the
    /// production character adapter. This is not a graphics or automatic-clock capture replacement.
    /// Each sample advances its accumulated real30Hz simulation ticks. The cadence matrix includes
    /// steady20/21/22/23/30Hz and an alternating20–23Hz interval sequence.
    /// </summary>
    public class SvGroundedGaitCaptureScheduleTests
    {
        const int Frames = 160;
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly EntityHandle Hero = new EntityHandle(-1, 1);

        sealed class View : IDisposable
        {
            readonly GameObject root;
            readonly RenderTier? previousTier;
            readonly MethodInfo prepare = typeof(SvRenderer).GetMethod("PrepareCharacters", Private);
            readonly FieldInfo inputs = typeof(GameplayCharacterPresenter).GetField("m_Inputs", Private);
            public readonly SvRenderer Renderer;

            public View(SvTestWorld t)
            {
                previousTier = RenderCapabilities.Override;
                RenderCapabilities.Override = RenderTier.DataTexture;
                root = new GameObject("Grounded horde capture schedule preflight");
                Renderer = root.AddComponent<SvRenderer>();
                Renderer.NaturalCharacters = true;
                typeof(SvRenderer).GetMethod("Bind", Private).Invoke(Renderer, new object[] { t.Session });
            }

            public void Draw(SvTestWorld t, float dt, float2 camera)
            {
                const float halfHeight = 10f;
                const float halfWidth = halfHeight * 320f / 568f;
                prepare.Invoke(Renderer, new object[] { t.World, t.Game, t.Game.Hero, 1f,
                    new float4(camera.x - halfWidth, camera.y - halfHeight, camera.x + halfWidth, camera.y + halfHeight), dt });
                Renderer.Characters.Evaluate();
            }

            public bool Submitted(EntityHandle handle)
            {
                var current = (NativeArray<GameplayCharacterInput>)inputs.GetValue(Renderer.Characters);
                for (int i = 0; i < Renderer.Characters.Count; i++)
                    if (current[i].Handle == handle) return true;
                return false;
            }

            public void Dispose()
            {
                typeof(SvRenderer).GetMethod("Release", Private).Invoke(Renderer, null);
                Object.DestroyImmediate(root);
                RenderCapabilities.Override = previousTier;
            }
        }

        static float2 HeroInput(int frame) => frame < 32 ? new float2(.18f, .10f) :
            frame < 48 ? new float2(.6f, 0) : frame < 64 ? new float2(-.6f, 0) :
            frame < 84 ? float2.zero : frame < 116 ? new float2(-.18f, -.10f) :
            frame < 138 ? new float2(0, .2f) : new float2(0, -.2f);

        [TestCase(30)]
        [TestCase(20)]
        [TestCase(21)]
        [TestCase(22)]
        [TestCase(23)]
        [TestCase(0)] // Alternating real acquisition intervals across the observed 20–23 Hz band.
        public void ScriptedSpeedFixtureKeepsFourMovingRolesVisibleWithWalkAndRun(int captureHz)
        {
            using var t = new SvTestWorld(tweak: c =>
            {
                var example = SvConfig.CreateWeaponCombatExample();
                c.Settings = example.Settings;
                Object.DestroyImmediate(example);
                c.WeaponCombat = c.MobileSkills = true;
                c.WeaponProfiles = WeaponProfiles.CreateDefaults(30);
                c.Settings.SpawnPerSecond = c.Settings.SpawnGrowth = c.Settings.EliteEvery = 0;
                c.Settings.XpBase = c.Settings.BeaconHp = c.Settings.HeroHp = 100000;
                c.Settings.GuardAggroRadius = 30;
                foreach (var enemy in c.Enemies)
                { enemy.Hp = 10000; enemy.Speed = 0; enemy.Damage = 0; enemy.Shooter = false; }
            });
            t.World.ClearLevel();
            t.Game.Flow = SvFlow.Playing;
            t.Game.Hero = t.Game.HeroPrev = float2.zero;
            t.Game.Input = default;
            var starts = new[] { float2.zero, new float2(3.5f, 4.2f), new float2(0, 6.5f), new float2(-3.5f, 7) };
            var handles = new[] { Hero, t.Spawn(1, starts[1]), t.Spawn(2, starts[2]), t.Spawn(3, starts[3]) };
            var previous = (float2[])starts.Clone();
            var visible = new int[4]; var moving = new int[4];
            var walk = new int[4]; var run = new int[4]; var unsupportedWalk = new int[4];
            var travel = new float[4]; var walkTravel = new float[4]; var runTravel = new float[4];
            var minimumHeroDistance = new[] { float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity };
            float minimumBeaconClearance = float.PositiveInfinity, maximumHeroDistance = 0;
            float2 camera = float2.zero;
            float pendingTicks = 0;
            using var view = new View(t);
            for (int frame = 0; frame < Frames; frame++)
            {
                // Deliberately scripted fixture speeds. Mutating EnemyDef after spawning would not
                // change existing actors: the authoritative mover consumes each row's EnemyInfo.Speed.
                var info = t.World.Column(SvKeys.Info);
                bool fast = frame >= 32 && frame < 56;
                for (int row = 0; row < 3; row++)
                {
                    var enemy = info[row];
                    Assert.AreEqual(row + 1, enemy.Kind, "content IDs are one-based");
                    enemy.Speed = fast ? (row == 0 ? 1.4f : row == 1 ? 1.9f : 3.7f) :
                        (row == 0 ? .4f : row == 1 ? .45f : .3f);
                    info[row] = enemy;
                }
                t.Input(HeroInput(frame));
                float dt = 1f / (captureHz == 0 ? 20 + frame % 4 : captureHz);
                pendingTicks += dt * 30;
                int ticks = (int)math.floor(pendingTicks + .00001f);pendingTicks -= ticks;
                t.Step(ticks);
                t.Session.Sync();
                Assert.AreEqual(3, t.Enemies);
                // Match the existing follow coefficient and portrait half-height, without zooming.
                camera = math.lerp(camera, t.Game.Hero, 1f - math.exp(-10f * dt));
                maximumHeroDistance = math.max(maximumHeroDistance, math.length(t.Game.Hero));
                view.Draw(t, dt, camera);
                for (int actor = 0; actor < 4; actor++)
                {
                    float2 position = actor == 0 ? t.Game.Hero : t.World.Column(SvKeys.Position)[actor - 1];
                    float distance = math.distance(position, previous[actor]);
                    previous[actor] = position;
                    if (actor > 0)
                    {
                        minimumHeroDistance[actor] = math.min(minimumHeroDistance[actor], math.distance(position, t.Game.Hero));
                        minimumBeaconClearance = math.min(minimumBeaconClearance,
                            math.distance(position, t.Runtime.Settings.BeaconPosition) - t.Runtime.Settings.BeaconRadius - info[actor - 1].Radius);
                    }
                    // Conservative silhouette margin inside the unmodified 320x568 view. Current
                    // submission is required because TryRead alone can return a retained old slot.
                    bool inside = math.abs(position.x - camera.x) < 10f * 320f / 568f - .7f &&
                        math.abs(position.y - camera.y) < 8.2f;
                    if (!inside || !view.Submitted(handles[actor]) ||
                        !view.Renderer.Characters.TryRead(handles[actor], out var motion)) continue;
                    visible[actor]++;
                    travel[actor] += distance;
                    if (distance > .003f) moving[actor]++;
                    if (motion.Locomotion == GameplayLocomotionState.Walk)
                    {
                        walk[actor]++; walkTravel[actor] += distance;
                        if (!motion.Airborne && !motion.FarFoot.InStance && !motion.NearFoot.InStance)
                            unsupportedWalk[actor]++;
                    }
                    if (motion.Locomotion == GameplayLocomotionState.Run)
                    { run[actor]++; runTravel[actor] += distance; }
                }
            }
            TestContext.WriteLine($"Capture preflight {(captureHz==0?"varying 20–23 Hz":captureHz+" Hz")}: max hero radius {maximumHeroDistance:F3}, minimum beacon clearance {minimumBeaconClearance:F3}");
            for (int actor = 0; actor < 4; actor++)
            {
                TestContext.WriteLine($"role {actor}: visible {visible[actor]}, moving {moving[actor]}, Walk {walk[actor]}, Run {run[actor]}, unsupported Walk {unsupportedWalk[actor]}, travel {travel[actor]:F3}, Walk travel {walkTravel[actor]:F3}, Run travel {runTravel[actor]:F3}, minimum hero distance {minimumHeroDistance[actor]:F3}");
                Assert.GreaterOrEqual(visible[actor], 120, $"role {actor}: visible samples");
                Assert.GreaterOrEqual(moving[actor], 100, $"role {actor}: real root movement");
                Assert.GreaterOrEqual(walk[actor], 60, $"role {actor}: Walk coverage");
                Assert.GreaterOrEqual(run[actor], 16, $"role {actor}: Run coverage");
                Assert.GreaterOrEqual(travel[actor], 2f, $"role {actor}: world travel");
                Assert.AreEqual(0, unsupportedWalk[actor], $"role {actor}: grounded Walk needs a supporting foot");
                if (actor > 0) Assert.Greater(minimumHeroDistance[actor], 1f, $"role {actor}: avoid stacking on hero");
            }
            Assert.Less(maximumHeroDistance, 4.1f, "bounded excursion at both acquisition cadences");
            Assert.Greater(minimumBeaconClearance, 1f, "no beacon-pinned locomotion samples");
        }
    }
}
