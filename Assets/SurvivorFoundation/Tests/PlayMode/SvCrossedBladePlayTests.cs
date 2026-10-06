using System.Collections;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Presentation;
using SPF.Testing;
using SurvivorFoundation.Game;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace SurvivorFoundation.Tests.PlayMode
{
    public class SvCrossedBladePlayTests
    {
        [UnityTest]
        public IEnumerator CrossedBladeFactoryPulsesAndRestartsWithExistingRenderer([Values(RenderTier.GpuDriven, RenderTier.DataTexture)] RenderTier tier)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("No graphics device");
            if (tier == RenderTier.GpuDriven && !SystemInfo.supportsComputeShaders) Assert.Ignore("No compute shader support");
            RenderCapabilities.Override = tier;
            var config = SvConfig.CreateCrossedBladeExample();
            config.Settings.SpawnPerSecond = config.Settings.SpawnGrowth = config.Settings.EliteEvery = 0;
            config.Settings.CrossedBlades.TickInterval = 3;
            config.Settings.CrossedBlades.DamagePerPulse = 10;
            config.Settings.XpBase = 100000;
            config.Enemies[0].Speed = 0; config.Enemies[0].Hp = 100; config.Enemies[0].Damage = 0;
            var game = SvGameBootstrap.CreateCrossedBladeExample(config);
            try
            {
                yield return null;
                Assert.IsTrue(game.Session.World.HasResource(SvKeys.CrossedBlades));
                UIDriver.Click(game.Hud.StartButton.gameObject);
                yield return UIDriver.WaitUntil(() => game.State.Flow == SvFlow.Playing, 5f);
                game.Session.Sync(); game.Host.enabled = false;
                var world = game.Session.World;
                world.ClearLevel();
                for (int i = 0; i < game.State.Upgrades.Length; i++) game.State.Upgrades[i] = 0;
                var target = SvSpawner.SpawnEnemy(world, world.Resource(SvKeys.Config), 1, new float2(.25f, .25f));
                for (int tick = 0; tick < 3; tick++) game.Session.Step();
                Assert.AreEqual(90, world.Column(SvKeys.Info)[0].Hp, .0001f, "two crossed paths share one pulse");
                var state = world.Resource(SvKeys.CrossedBlades);
                Assert.AreEqual(1, state.Pulses); Assert.AreEqual(1, state.Scope[0].Count);
                yield return null;
                Assert.Greater(game.Renderer.EnemiesDrawn, 0, "existing enemy presentation remains usable");
                game.Governor.AdaptiveQuality = false;
                byte[] before = game.Session.CaptureSnapshot(); game.Governor.SetLevel(3);
                CollectionAssert.AreEqual(before, game.Session.CaptureSnapshot(), "quality changes no skill simulation state");

                game.State.Flow = SvFlow.Dead; game.State.Version++;
                yield return null;
                Assert.IsTrue(game.Hud.DeadPanel.gameObject.activeInHierarchy);
                UIDriver.Click(game.Hud.RestartButton.gameObject); game.Session.Step();
                Assert.AreEqual(SvFlow.Playing, game.State.Flow);
                Assert.IsFalse(world.Registry.TryResolve(target, out _, out _));
                Assert.AreEqual(0, state.Scope[0].Count); Assert.AreEqual(0, state.Pulses);
                yield return null;
                Assert.IsFalse(game.Hud.DeadPanel.gameObject.activeInHierarchy);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                RenderCapabilities.Override = null;
                if (Camera.main != null) Object.Destroy(Camera.main.gameObject);
                Object.Destroy(game.gameObject); Object.Destroy(config);
            }
        }
    }
}
