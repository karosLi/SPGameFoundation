using System.Collections;
using BrawlerFoundation.Game;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Presentation;
using SPF.Testing;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace BrawlerFoundation.Tests.PlayMode
{
    public class BwSharedCombatPlayTests
    {
        [UnityTest]
        public IEnumerator SharedCombatFactorySupportsTouchHitAndRestart([Values(RenderTier.GpuDriven, RenderTier.DataTexture)] RenderTier tier)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("No graphics device");
            if (tier == RenderTier.GpuDriven && !SystemInfo.supportsComputeShaders) Assert.Ignore("No compute shader support");
            RenderCapabilities.Override = tier;
            var game = BwGameBootstrap.CreateSharedCombat();
            try
            {
                yield return null;
                Assert.IsTrue(game.SharedCombatEnabled);
                Assert.IsTrue(game.Session.World.HasResource(BwKeys.SharedCombat));
                Assert.AreEqual(64, game.Session.World.Table(BwKeys.Fighter).Capacity, "matches unchanged presentation capacity");
                UIDriver.Click(game.StartButton.gameObject);
                yield return UIDriver.WaitUntil(() => game.State.Flow == BwFlow.Fighting, 5f);
                game.Session.Sync(); game.Host.enabled = false;
                var world = game.Session.World;
                world.ClearLevel();
                BwSpawner.Spawn(world, 0, float2.zero, 1, 0);
                BwSpawner.Spawn(world, 1, new float2(.9f, 0), -1, 0);
                var target = world.Table(BwKeys.Fighter).Handles[1];
                var info = world.Column(BwKeys.Info);
                var f = info[1]; f.Hp = f.MaxHp = 100; info[1] = f;
                game.State.Input = default;
                yield return null;
                UIDriver.Click(game.PunchButton.gameObject);
                yield return null; yield return null;
                for (int tick = 0; tick < 11; tick++) game.Session.Step();
                Assert.AreEqual(92, world.Column(BwKeys.Info)[1].Hp, .0001f, "touch input reaches one stable-handle jab");
                Assert.AreEqual(1, world.Resource(BwKeys.SharedCombat).Attacks[0].History.Count);
                yield return null;
                Assert.Greater(game.Renderer.PartsDrawn, 0, "existing skeletal renderer remains usable");

                game.State.Flow = BwFlow.Lost; game.State.Version++;
                yield return null;
                Assert.IsTrue(game.EndPanel.gameObject.activeInHierarchy);
                UIDriver.Click(game.AgainButton.gameObject);
                game.Session.Step();
                Assert.AreEqual(BwFlow.Fighting, game.State.Flow);
                Assert.IsFalse(world.Registry.TryResolve(target, out _, out _));
                var shared = world.Resource(BwKeys.SharedCombat);
                for (int i = 0; i < shared.Attacks.Length; i++) Assert.AreEqual(0, shared.Attacks[i].History.Pulse);
                Assert.AreEqual(0, shared.RejectedHits);
                yield return null;
                Assert.IsFalse(game.EndPanel.gameObject.activeInHierarchy);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                RenderCapabilities.Override = null;
                if (Camera.main != null) Object.Destroy(Camera.main.gameObject);
                Object.Destroy(game.gameObject);
            }
        }
    }
}
