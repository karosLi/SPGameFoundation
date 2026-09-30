using System.Collections;
using System.IO;
using NUnit.Framework;
using SnakeFoundation.Game;
using SPF.Contracts;
using SPF.Presentation;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace SnakeFoundation.Tests.PlayMode
{
    /// <summary>
    /// UI automation: boots the full game (simulation, rendering, UI) and plays it through the UI like a
    /// user — menu, joystick, boost, skill, death, play again, back to menu, portals — asserting on
    /// visible UI state and on the simulation. Screenshots are saved when a GPU is available.
    /// </summary>
    public class SnakeUITests
    {
        SnakeGameBootstrap m_Game;
        SnakeConfig m_Config;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            RenderCapabilities.Override = null;
            m_Config = SnakeConfig.CreateDefault();
            m_Config.AI.SnakesPerRegion = 40;
            m_Config.Food.FoodPerChunk = 60;
            m_Game = SnakeGameBootstrap.Create(m_Config, seed: 1234, ui: true, perfHud: true);
            yield return UIDriver.WaitSeconds(0.5f);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (m_Game != null)
            {
                if (Camera.main != null) Object.Destroy(Camera.main.gameObject);
                Object.Destroy(m_Game.gameObject);
            }
            Object.Destroy(m_Config);
            RenderCapabilities.Override = null;
            yield return null;
        }

        SnakeGameState State => m_Game.State;

        int PlayerRow
        {
            get
            {
                var world = m_Game.Session.World;
                return world.Registry.TryResolve(State.Player, out _, out int row) ? row : -1;
            }
        }

        IEnumerator StartFromMenu()
        {
            Assert.IsTrue(m_Game.Hud.MenuPanel.gameObject.activeSelf, "menu is shown at boot");
            UIDriver.Click(m_Game.Hud.StartButton.gameObject);
            yield return UIDriver.WaitUntil(() => State.Flow == GameFlow.Playing, 3f);
            Assert.AreEqual(GameFlow.Playing, State.Flow);
            yield return null;
        }

        /// <summary>Moves the player (whole body, straight) to a position; used to set up scenarios quickly.</summary>
        void PlacePlayer(float2 head, float2 heading)
        {
            var world = m_Game.Session.World;
            int row = PlayerRow;
            var config = world.Resource(SnakeKeys.Config);
            var trails = world.Column(SnakeKeys.Trail);
            var trail = trails[row];
            SPF.L1.Body.TrailMath.Reset(ref trail, world.Resource(SnakeKeys.Bodies).Points, head, -heading, config.Settings.TrailSpacing, trail.Count);
            trails[row] = trail;
            world.Column(SnakeKeys.Head).Set(row, head);
            world.Column(SnakeKeys.PrevHead).Set(row, head);
            world.Column(SnakeKeys.Heading).Set(row, math.normalize(heading));
            var info = world.Column(SnakeKeys.Info)[row];
            info.Protection = 0f;
            world.Column(SnakeKeys.Info).Set(row, info);
        }

        [UnityTest]
        public IEnumerator MenuStartsTheGameAndTheHudShowsLiveStats()
        {
            yield return StartFromMenu();
            Assert.IsFalse(m_Game.Hud.MenuPanel.gameObject.activeSelf);
            Assert.IsTrue(m_Game.Hud.HudPanel.gameObject.activeSelf);
            yield return UIDriver.WaitSeconds(1f);
            StringAssert.StartsWith("Mass ", m_Game.Hud.ScoreText.text);
            StringAssert.StartsWith("Rank ", m_Game.Hud.RankText.text);
            Assert.AreEqual("Mainland", m_Game.Hud.RegionText.text);
            StringAssert.Contains("1. ", m_Game.Hud.LeaderboardText.text);
            Screenshot("hud");
        }

        [UnityTest]
        public IEnumerator JoystickSteersTheSnake()
        {
            yield return StartFromMenu();
            PlacePlayer(float2.zero, new float2(1, 0));
            yield return UIDriver.Drag(m_Game.Hud.Joystick.gameObject, new Vector2(0, 200));
            yield return UIDriver.WaitSeconds(1.2f);
            var heading = m_Game.Session.World.Column(SnakeKeys.Heading)[PlayerRow];
            UIDriver.Release(m_Game.Hud.Joystick.gameObject);
            Assert.Greater(heading.y, 0.9f, $"dragging up turned the snake up (heading {heading})");
        }

        [UnityTest]
        public IEnumerator HoldingBoostSpeedsUpAndCostsMass()
        {
            yield return StartFromMenu();
            var world = m_Game.Session.World;
            world.Column(SnakeKeys.Mass).Set(PlayerRow, 60f);
            UIDriver.Press(m_Game.Hud.BoostButton.gameObject);
            yield return UIDriver.WaitSeconds(1.5f);
            float speed = world.Column(SnakeKeys.Speed)[PlayerRow];
            float mass = world.Column(SnakeKeys.Mass)[PlayerRow];
            UIDriver.Release(m_Game.Hud.BoostButton.gameObject);
            Assert.Greater(speed, world.Resource(SnakeKeys.Config).Settings.BaseSpeed * 1.5f);
            Assert.Less(mass, 60f);
        }

        [UnityTest]
        public IEnumerator SkillButtonFiresAProjectile()
        {
            yield return StartFromMenu();
            var world = m_Game.Session.World;
            world.Column(SnakeKeys.Mass).Set(PlayerRow, 80f);
            UIDriver.Click(m_Game.Hud.SkillButton.gameObject);
            yield return UIDriver.WaitUntil(() => world.Table(SnakeKeys.Projectile).Count > 0, 1f);
            Assert.Greater(world.Table(SnakeKeys.Projectile).Count, 0);
        }

        [UnityTest]
        public IEnumerator DyingShowsGameOverAndPlayAgainRespawns()
        {
            yield return StartFromMenu();
            var region = m_Game.Session.World.Resource(SnakeKeys.Config).Regions[0];
            PlacePlayer(new float2(region.Max.x - 4f, 0), new float2(1, 0));
            m_Game.InputRouter.Scripted.Active = true;
            m_Game.InputRouter.Scripted.Command = new PlayerCommand { Direction = new float2(1, 0) };
            yield return UIDriver.WaitUntil(() => State.Flow == GameFlow.GameOver, 3f);
            m_Game.InputRouter.Scripted.Active = false;
            Assert.AreEqual(GameFlow.GameOver, State.Flow);
            yield return null;
            Assert.IsTrue(m_Game.Hud.GameOverPanel.gameObject.activeSelf);
            StringAssert.Contains("wall", m_Game.Hud.GameOverText.text);
            Screenshot("game-over");

            UIDriver.Click(m_Game.Hud.PlayAgainButton.gameObject);
            yield return UIDriver.WaitUntil(() => State.Flow == GameFlow.Playing, 3f);
            Assert.AreEqual(GameFlow.Playing, State.Flow);
            yield return null;
            Assert.IsTrue(m_Game.Hud.HudPanel.gameObject.activeSelf);
        }

        [UnityTest]
        public IEnumerator MenuButtonResetsTheWorldToAttractMode()
        {
            yield return StartFromMenu();
            var region = m_Game.Session.World.Resource(SnakeKeys.Config).Regions[0];
            PlacePlayer(new float2(region.Min.x + 4f, 0), new float2(-1, 0));
            m_Game.InputRouter.Scripted.Active = true;
            m_Game.InputRouter.Scripted.Command = new PlayerCommand { Direction = new float2(-1, 0) };
            yield return UIDriver.WaitUntil(() => State.Flow == GameFlow.GameOver, 3f);
            m_Game.InputRouter.Scripted.Active = false;
            yield return null;
            UIDriver.Click(m_Game.Hud.MenuButton.gameObject);
            yield return null;
            yield return null;
            Assert.AreEqual(GameFlow.Attract, State.Flow);
            Assert.IsTrue(m_Game.Hud.MenuPanel.gameObject.activeSelf);
        }

        [UnityTest]
        public IEnumerator SkinButtonCyclesSkins()
        {
            string before = m_Game.Hud.SkinButton.GetComponentInChildren<UnityEngine.UI.Text>().text;
            UIDriver.Click(m_Game.Hud.SkinButton.gameObject);
            yield return null;
            yield return UIDriver.WaitSeconds(0.3f);
            string after = m_Game.Hud.SkinButton.GetComponentInChildren<UnityEngine.UI.Text>().text;
            Assert.AreNotEqual(before, after);
            Assert.AreEqual(1, State.PlayerSkin);
        }

        [UnityTest]
        public IEnumerator EnteringAPortalSwitchesToTheArena()
        {
            yield return StartFromMenu();
            var portal = m_Game.Session.World.Resource(SnakeKeys.Config).Portals[0];
            PlacePlayer(portal.Position - new float2(10, 0), new float2(1, 0));
            m_Game.InputRouter.Scripted.Active = true;
            m_Game.InputRouter.Scripted.Command = new PlayerCommand { Direction = new float2(1, 0) };
            yield return UIDriver.WaitUntil(() => State.ActiveRegion == 1, 3f);
            m_Game.InputRouter.Scripted.Active = false;
            Assert.AreEqual(1, State.ActiveRegion);
            yield return UIDriver.WaitSeconds(0.5f);
            Assert.AreEqual("Arena", m_Game.Hud.RegionText.text);
            Screenshot("arena");
        }

        [UnityTest]
        public IEnumerator RunsOneMinuteOfAttractModeWithoutErrors()
        {
            // A long soak: AI-only world with UI, rendering and adaptive quality active.
            float end = Time.realtimeSinceStartup + 60f;
            int frames = 0;
            while (Time.realtimeSinceStartup < end)
            {
                frames++;
                yield return null;
            }
            LogAssert.NoUnexpectedReceived();
            Assert.Greater(m_Game.Session.Pipeline.Stats.TickCount, 60);
            TestContext.Progress.WriteLine($"soak: {frames} frames, {m_Game.Session.Pipeline.Stats.TickCount} ticks, quality level {m_Game.Quality.Level}, tier {m_Game.WorldRenderer.Tier}, visible snakes {m_Game.WorldRenderer.LastVisibleSnakes}");
        }

        static void Screenshot(string name)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                return;
            string dir = Path.Combine(Application.dataPath, "..", "Artifacts", "Screenshots");
            Directory.CreateDirectory(dir);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
        }
    }
}
