#if !SPF_DOTNET_HARNESS
using System;
using System.Collections;
using System.Reflection;
using DefenseFoundation.Game;
using NUnit.Framework;
using SPF.Presentation;
using SPF.Shell.UI;
using SPF.Testing;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace DefenseFoundation.Tests.PlayMode
{
    public class TdHudTests
    {
        static readonly string[] Names = { "ARROW", "CANNON", "FROST" };

        // Bind once outside any measurement. A closed delegate calls the actual Update method
        // without MethodInfo.Invoke's argument/boxing allocations or a production-only test API.
        static Action BindUpdate(TdHud hud) => (Action)typeof(TdHud)
            .GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
            .CreateDelegate(typeof(Action), hud);

        static Text Label(Button button) => button.GetComponentInChildren<Text>(true);

        static void Step(TdGameBootstrap game, Action update)
        {
            game.Session.Sync();
            game.Session.Step();
            game.Session.Sync();
            update();
        }

        static void AddReward(TdGameBootstrap game, Action update, int gold)
        {
            int before = game.State.Gold, kills = game.State.Kills, version = game.State.Version;
            Assert.IsTrue(game.Session.World.Resource(TdKeys.Rewards).TryAdd(gold));
            Step(game, update);
            Assert.AreEqual(before + gold, game.State.Gold);
            Assert.AreEqual(kills + 1, game.State.Kills);
            Assert.Greater(game.State.Version, version, "The real reward consumer must invalidate the HUD.");
        }

        static void AssertStats(TdGameBootstrap game)
        {
            string prefix = $"Gold {game.State.Gold}   Lives {game.State.Lives}   ";
            Assert.GreaterOrEqual(game.Hud.StatsText.Length, prefix.Length);
            for (int i = 0; i < prefix.Length; i++) Assert.AreEqual(prefix[i], game.Hud.StatsText[i]);
        }

        [UnityTest]
        public IEnumerator RewardsSelectionUpgradeSellAndRuleChangesKeepLabelsCurrent()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("No graphics device");
            var previousTier = RenderCapabilities.Override;
            RenderCapabilities.Override = RenderTier.DataTexture;
            var game = TdGameBootstrap.Create();
            var camera = game.CameraRig.gameObject;
            try
            {
                yield return null;
                var update = BindUpdate(game.Hud);
                UIDriver.Click(game.Hud.StartButton.gameObject);
                Step(game, update);
                game.Select(new int2(8, 7));
                update();
                var buildLabels = new Text[3];
                var buildStrings = new string[3];
                for (int i = 0; i < 3; i++)
                {
                    buildLabels[i] = Label(game.Hud.BuildButtons[i]);
                    buildStrings[i] = buildLabels[i].text;
                    Assert.AreEqual($"{Names[i]} {game.Session.World.Resource(TdKeys.Rules).Towers[i].Cost}", buildStrings[i]);
                    Assert.AreEqual(30, buildLabels[i].fontSize);
                    Assert.AreEqual(TextAnchor.MiddleCenter, buildLabels[i].alignment);
                    Assert.IsFalse(buildLabels[i].raycastTarget);
                }
                game.State.Gold = 0;
                game.State.Version++;
                update();
                foreach (var button in game.Hud.BuildButtons) Assert.IsFalse(button.interactable);
                AddReward(game, update, 25);
                for (int i = 0; i < 3; i++)
                {
                    Assert.IsTrue(game.Hud.BuildButtons[i].interactable, "Rewards must still update affordability.");
                    Assert.AreSame(buildStrings[i], buildLabels[i].text, "Unchanged prices retain their strings.");
                }
                AssertStats(game);
                UIDriver.Click(game.Hud.BuildButtons[(int)TowerKind.Arrow].gameObject);
                Step(game, update);
                Assert.IsTrue(game.Hud.TowerPanel.gameObject.activeInHierarchy);
                var upgrade = Label(game.Hud.UpgradeButton);
                var sell = Label(game.Hud.SellButton);
                Assert.AreEqual("UPGRADE 7", upgrade.text);
                Assert.AreEqual("SELL 7", sell.text);
                string upgradeText = upgrade.text, sellText = sell.text;
                game.State.Gold = 6;
                game.State.Version++;
                update();
                Assert.IsFalse(game.Hud.UpgradeButton.interactable);
                AddReward(game, update, 1);
                Assert.IsTrue(game.Hud.UpgradeButton.interactable);
                Assert.AreSame(upgradeText, upgrade.text);
                Assert.AreSame(sellText, sell.text);
                AssertStats(game);

                int lives = game.State.Lives;
                Assert.IsTrue(game.Session.World.Resource(TdKeys.Leaks).TryAdd(1));
                Step(game, update);
                Assert.AreEqual(lives - 1, game.State.Lives);
                Assert.AreSame(upgradeText, upgrade.text);
                Assert.AreSame(sellText, sell.text);
                AssertStats(game);

                UIDriver.Click(game.Hud.UpgradeButton.gameObject);
                Step(game, update);
                Assert.AreEqual(2, game.Session.World.Column(TdKeys.TowerInfo)[0].Level);
                Assert.AreEqual("UPGRADE 15", upgrade.text);
                Assert.AreEqual("SELL 11", sell.text);
                Assert.IsFalse(game.Hud.UpgradeButton.interactable);
                AddReward(game, update, 100);
                UIDriver.Click(game.Hud.UpgradeButton.gameObject);
                Step(game, update);
                Assert.AreEqual("MAX", upgrade.text);
                Assert.AreEqual("SELL 22", sell.text);
                Assert.IsFalse(game.Hud.UpgradeButton.interactable);

                game.Select(new int2(10, 6));
                update();
                UIDriver.Click(game.Hud.BuildButtons[(int)TowerKind.Cannon].gameObject);
                Step(game, update);
                Assert.AreEqual("UPGRADE 18", upgrade.text);
                Assert.AreEqual("SELL 17", sell.text);
                game.Select(new int2(8, 7));
                update();
                Assert.AreEqual("MAX", upgrade.text);
                Assert.AreEqual("SELL 22", sell.text);
                game.Select(new int2(10, 6));
                update();
                Assert.AreEqual("UPGRADE 18", upgrade.text);
                Assert.AreEqual("SELL 17", sell.text);

                // Rules may be customized: caches use current numeric values, not startup rules.
                var rules = game.Session.World.Resource(TdKeys.Rules);
                var cannon = rules.Towers[(int)TowerKind.Cannon];
                cannon.Cost = 40;
                rules.Towers[(int)TowerKind.Cannon] = cannon;
                game.Hud.Refresh();
                update();
                Assert.AreEqual("UPGRADE 30", upgrade.text);
                Assert.AreEqual("SELL 17", sell.text, "Refund reflects investment, not the new build price.");
                int gold = game.State.Gold;
                UIDriver.Click(game.Hud.SellButton.gameObject);
                Step(game, update);
                Assert.AreEqual(gold + 17, game.State.Gold);
                Assert.AreEqual(1, game.Session.World.Table(TdKeys.Tower).Count);
                Assert.IsFalse(game.Hud.TowerPanel.gameObject.activeInHierarchy);
                Assert.IsFalse(game.Hud.BuildPanel.gameObject.activeInHierarchy);
                game.Select(new int2(10, 6));
                update();
                Assert.IsTrue(game.Hud.BuildPanel.gameObject.activeInHierarchy);
                Assert.AreEqual("CANNON 40", buildLabels[(int)TowerKind.Cannon].text);
                AssertStats(game);

                // Restart uses the existing HUD. A prior MAX state must not leak into a new tower.
                game.StartGame();
                Step(game, update);
                game.Select(new int2(8, 7));
                update();
                UIDriver.Click(game.Hud.BuildButtons[(int)TowerKind.Arrow].gameObject);
                Step(game, update);
                Assert.AreEqual("UPGRADE 7", upgrade.text);
                Assert.AreEqual("SELL 7", sell.text);
            }
            finally
            {
                RenderCapabilities.Override = previousTier;
                Object.Destroy(game.gameObject);
                Object.Destroy(camera);
            }
        }

        // The removed expressions, intentionally retained only as a diagnostic comparison.
        // Text already has these values, so the old setter's equality guard cannot undo the
        // strings and boxed numeric arguments allocated before UIFactory.SetText is entered.
        static void FormerEagerLabels(TdGameBootstrap game, bool buildPanel)
        {
            var rules = game.Session.World.Resource(TdKeys.Rules);
            if (buildPanel)
            {
                for (int i = 0; i < 3; i++)
                    UIFactory.SetText(game.Hud.BuildButtons[i], $"{Names[i]} {rules.Towers[i].Cost}");
            }
            else
            {
                var tower = game.Session.World.Column(TdKeys.TowerInfo)[0];
                int cost = rules.UpgradeCost(tower.Kind, tower.Level);
                UIFactory.SetText(game.Hud.UpgradeButton, tower.Level < 3 ? $"UPGRADE {cost}" : "MAX");
                UIFactory.SetText(game.Hud.SellButton, $"SELL {tower.Invested * 7 / 10}");
            }
        }

        static void RefreshLoop(TdGameBootstrap game, Action update, bool buildPanel, bool formerExpressions, int count)
        {
            for (int i = 0; i < count; i++)
            {
                // Same invalidation as a reward, without simulation/frame-runner noise in this
                // narrow synchronous probe. The behavioral test above uses the real reward queue.
                game.State.Gold++;
                game.State.Version++;
                update();
                if (formerExpressions) FormerEagerLabels(game, buildPanel);
            }
        }

        [UnityTest]
        public IEnumerator WarmGoldOnlyRefreshDoesNotAllocate([Values(false, true)] bool buildPanel)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("No graphics device");
            var previousTier = RenderCapabilities.Override;
            RenderCapabilities.Override = RenderTier.DataTexture;
            var game = TdGameBootstrap.Create();
            var camera = game.CameraRig.gameObject;
            try
            {
                yield return null;
                var update = BindUpdate(game.Hud);
                UIDriver.Click(game.Hud.StartButton.gameObject);
                Step(game, update);
                game.State.Gold = 1000;
                game.Select(new int2(8, 7));
                update();
                if (!buildPanel)
                {
                    UIDriver.Click(game.Hud.BuildButtons[(int)TowerKind.Arrow].gameObject);
                    Step(game, update);
                }
                Assert.IsTrue((buildPanel ? game.Hud.BuildPanel : game.Hud.TowerPanel).gameObject.activeInHierarchy);
                // No HUD/audio is disabled. Only these synchronous calls are measured; deferred
                // canvas work and other threads remain covered by the original whole-frame test.
                RefreshLoop(game, update, buildPanel, false, 32);
                RefreshLoop(game, update, buildPanel, true, 32);
                const int count = 256;
                Action formerRefresh = () => RefreshLoop(game, update, buildPanel, true, count);
                Action fixedRefresh = () => RefreshLoop(game, update, buildPanel, false, count);
                using var probe = new ManagedAllocationProbe();
                var calibrationBefore = probe.Calibrate();
                var oldA = probe.Measure(formerRefresh);
                var fixedA = probe.Measure(fixedRefresh);
                var fixedB = probe.Measure(fixedRefresh);
                var oldB = probe.Measure(formerRefresh);
                var calibrationAfter = probe.Calibrate();
                TestContext.WriteLine($"TdHud {(buildPanel ? "build" : "tower")} label probe; Unity {Application.unityVersion}; OS {SystemInfo.operatingSystem}; graphics {SystemInfo.graphicsDeviceType}; {count} synchronous refreshes/window. Metric={probe.Metric}. Retained-array/empty calibration: before={calibrationBefore.RetainedArrays.Value}/{calibrationBefore.Empty.Value}, after={calibrationAfter.RetainedArrays.Value}/{calibrationAfter.Empty.Value}.");
                TestContext.WriteLine($"Current-thread {probe.Metric} / independent process-wide generation-0 collections: former A={oldA.Value}/{oldA.Collections}, fixed A={fixedA.Value}/{fixedA.Collections}, fixed B={fixedB.Value}/{fixedB.Collections}, former B={oldB.Value}/{oldB.Collections}. Former adds the removed expressions to the same production refresh; this is not a full-checkout A/B or a whole-frame measurement.");
                Assert.Greater(oldA.Value, 0, "Control must demonstrate the removed formatting allocations.");
                Assert.Greater(oldB.Value, 0, "Repeated control must still allocate.");
                Assert.AreEqual(0, fixedA.Value, "Warmed gold/version changes must not recreate unchanged labels.");
                Assert.AreEqual(0, fixedB.Value, "Repeated warmed refresh must remain allocation-free.");
                AssertStats(game);
            }
            finally
            {
                RenderCapabilities.Override = previousTier;
                Object.Destroy(game.gameObject);
                Object.Destroy(camera);
            }
        }
    }
}
#endif
