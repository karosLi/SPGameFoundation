using System.Collections;
using System.IO;
using NUnit.Framework;
using RpgFoundation.Game;
using SPF.Contracts;
using SPF.Presentation;
using SPF.Runtime.World;
using SPF.Testing;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace RpgFoundation.Tests.PlayMode
{
    /// <summary>
    /// UI automation of the dungeon RPG: boots the whole game (four simulation modules, renderer, HUD) and
    /// plays it through the UI — menu, joystick, attack button, bag, stairs, continue, death and retry —
    /// asserting on UI state and on the simulation. Saves go to a temporary directory.
    /// </summary>
    public class RpgUITests
    {
        RpgGameBootstrap m_Game;
        RpgConfig m_Config;
        string m_SaveDir;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            RenderCapabilities.Override = null;
            m_SaveDir = Path.Combine(Path.GetTempPath(), "rpg-ui-" + System.Guid.NewGuid().ToString("N"));
            RpgGameBootstrap.SaveDirectoryOverride = m_SaveDir;
            m_Config = RpgConfig.CreateDefault();
            m_Game = RpgGameBootstrap.Create(m_Config, seed: 21, ui: true, perfHud: true);
            yield return UIDriver.WaitSeconds(0.3f);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Destroy();
            if (m_Config != null) Object.Destroy(m_Config);
            RpgGameBootstrap.SaveDirectoryOverride = null;
            RenderCapabilities.Override = null;
            if (Directory.Exists(m_SaveDir)) Directory.Delete(m_SaveDir, true);
            yield return null;
        }

        void Destroy()
        {
            if (m_Game == null) return;
            if (Camera.main != null) Object.Destroy(Camera.main.gameObject);
            Object.Destroy(m_Game.gameObject);
            m_Game = null;
        }

        RpgGameState State => m_Game.State;

        /// <summary>Completes in-flight jobs before touching simulation data from a test coroutine.</summary>
        SimWorld World
        {
            get
            {
                m_Game.Session.Sync();
                return m_Game.Session.World;
            }
        }

        int HeroRow => World.Registry.TryResolve(State.Hero, out _, out int row) ? row : -1;

        void ClearMonsters()
        {
            var world = World;
            var infos = world.Column(RpgKeys.Info);
            var handles = world.Table(RpgKeys.Actor).Handles;
            for (int row = world.Table(RpgKeys.Actor).Count - 1; row >= 0; row--)
                if (infos[row].Team == Team.Monsters) world.DestroyEntity(handles[row]);
            State.MonstersAlive = 0;
            State.BossAlive = false;
        }

        IEnumerator StartNewGame()
        {
            Assert.IsTrue(m_Game.Hud.MenuPanel.gameObject.activeInHierarchy, "menu shown at start");
            UIDriver.Click(m_Game.Hud.NewGameButton.gameObject);
            yield return UIDriver.WaitUntil(() => State.Flow == RpgFlow.Playing, 5f);
            Assert.AreEqual(RpgFlow.Playing, State.Flow);
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator NewGameShowsTheDungeonAndTheHud()
        {
            yield return StartNewGame();
            Assert.IsTrue(m_Game.Hud.HudPanel.gameObject.activeInHierarchy);
            Assert.IsFalse(m_Game.Hud.MenuPanel.gameObject.activeInHierarchy);
            Assert.Greater(m_Game.WorldRenderer.TileQuads, 200, "map mesh built");
            Assert.Greater(m_Game.WorldRenderer.LastActorsDrawn, 0, "at least the hero is drawn");
            StringAssert.Contains("Floor 1", m_Game.Hud.StatsText.text);
        }

        [UnityTest]
        public IEnumerator JoystickMovesTheHero()
        {
            yield return StartNewGame();
            ClearMonsters();
            float2 before = World.Column(RpgKeys.Position)[HeroRow];
            yield return UIDriver.Drag(m_Game.Hud.Joystick.gameObject, new Vector2(200, 0));
            yield return UIDriver.WaitSeconds(0.6f);
            UIDriver.Release(m_Game.Hud.Joystick.gameObject);
            float2 after = World.Column(RpgKeys.Position)[HeroRow];
            Assert.Greater(math.distance(before, after), 0.3f, "the hero walked (or slid along a wall)");
        }

        [UnityTest]
        public IEnumerator AttackButtonKillsAMonsterAndShowsDamage()
        {
            yield return StartNewGame();
            ClearMonsters();
            var world = World;
            float2 hero = world.Column(RpgKeys.Position)[HeroRow];
            var map = world.Resource(RpgKeys.Map).AsView();
            float2 spot = hero;
            for (int k = 0; k < 8; k++)
            {
                float2 p = hero + new float2(math.cos(k * 0.785f), math.sin(k * 0.785f)) * 0.95f;
                if (!map.IsSolidAt(p)) { spot = p; break; }
            }
            RpgSpawner.SpawnMonster(world, m_Game.Runtime, 1, spot, 1);
            State.MonstersAlive = 1;
            UIDriver.Press(m_Game.Hud.AttackButton.gameObject);
            int maxText = 0;
            float end = Time.realtimeSinceStartup + 8f;
            while (State.MonstersAlive > 0 && Time.realtimeSinceStartup < end)
            {
                maxText = Mathf.Max(maxText, m_Game.WorldRenderer.EffectsActive);
                yield return null;
            }
            UIDriver.Release(m_Game.Hud.AttackButton.gameObject);
            Assert.AreEqual(0, State.MonstersAlive, "the slime died");
            Assert.AreEqual(1, State.Profile.Kills);
            Assert.Greater(maxText, 0, "hit sparks and damage numbers were shown");
        }

        [UnityTest]
        public IEnumerator SkillButtonCastsAFireballWithEffects()
        {
            yield return StartNewGame();
            ClearMonsters();
            var world = World;
            float2 hero = world.Column(RpgKeys.Position)[HeroRow];
            var map = world.Resource(RpgKeys.Map).AsView();
            float2 spot = hero;
            for (int k = 0; k < 16; k++)
            {
                float2 p = hero + new float2(math.cos(k * 0.39f), math.sin(k * 0.39f)) * 4f;
                if (!map.IsSolidAt(p) && map.LineOfSight(hero, p)) { spot = p; break; }
            }
            var target = RpgSpawner.SpawnMonster(world, m_Game.Runtime, 3, spot, 1);
            State.MonstersAlive = 1;
            float mana = World.Column(RpgKeys.Mana)[HeroRow].Current;
            UIDriver.Click(m_Game.Hud.SkillButtons[0].gameObject);
            bool exploded = false;
            float end = Time.realtimeSinceStartup + 4f;
            while (!exploded && Time.realtimeSinceStartup < end)
            {
                yield return null;
                exploded = !World.Registry.TryResolve(target, out _, out int row) || World.Column(RpgKeys.Health)[row].Current < World.Column(RpgKeys.Health)[row].Max;
            }
            Assert.IsTrue(exploded, "the fireball hit the brute");
            Assert.Less(World.Column(RpgKeys.Mana)[HeroRow].Current, mana + 1f, "mana was spent (regen aside)");
            Assert.Greater(m_Game.WorldRenderer.EffectsActive, 0, "explosion / numbers on screen");
        }

        [UnityTest]
        public IEnumerator BagEquipsGear()
        {
            yield return StartNewGame();
            ClearMonsters();
            int sword = m_Game.Runtime.GearId(GearSlot.Weapon, 2);
            State.Profile.Inventory.Add(sword);
            State.Version++;
            UIDriver.Click(m_Game.Hud.BagButton.gameObject);
            yield return null;
            yield return null;
            Assert.IsTrue(m_Game.Hud.BagPanel.gameObject.activeInHierarchy, "bag opened");
            Assert.IsTrue(m_Game.Hud.BagSlots[0].gameObject.activeInHierarchy, "the sword is listed");
            UIDriver.Click(m_Game.Hud.BagSlots[0].gameObject);
            yield return UIDriver.WaitUntil(() => State.Profile.Weapon == sword, 2f);
            Assert.AreEqual(sword, State.Profile.Weapon);
            yield return UIDriver.WaitSeconds(0.3f);
            StringAssert.Contains("Sword +2", m_Game.Hud.EquippedText.text);
        }

        [UnityTest]
        public IEnumerator StairsDescendAndContinueFromTheSave()
        {
            yield return StartNewGame();
            ClearMonsters();
            var world = World;
            var map = world.Resource(RpgKeys.Map).AsView();
            world.Column(RpgKeys.Position).Set(HeroRow, map.CenterOf(State.StairsCell));
            yield return UIDriver.WaitUntil(() => m_Game.Hud.ClearPanel.gameObject.activeInHierarchy, 3f);
            Assert.AreEqual(RpgFlow.FloorClear, State.Flow);
            UIDriver.Click(m_Game.Hud.DescendButton.gameObject);
            yield return UIDriver.WaitUntil(() => State.Flow == RpgFlow.Playing && State.Profile.Floor == 2, 3f);
            Assert.AreEqual(2, State.Profile.Floor);
            yield return null;   // autosave happens in LateUpdate
            yield return null;
            Assert.IsTrue(m_Game.HasSave, "floor start saved");

            // A fresh game instance continues on floor 2.
            Destroy();
            yield return null;
            m_Game = RpgGameBootstrap.Create(m_Config, seed: 22, ui: true);
            yield return UIDriver.WaitSeconds(0.3f);
            Assert.IsTrue(m_Game.Hud.ContinueButton.interactable, "continue enabled");
            UIDriver.Click(m_Game.Hud.ContinueButton.gameObject);
            yield return UIDriver.WaitUntil(() => State.Flow == RpgFlow.Playing, 3f);
            Assert.AreEqual(2, State.Profile.Floor);
        }

        [UnityTest]
        public IEnumerator DeathShowsRetryWhichRebuildsTheFloor()
        {
            yield return StartNewGame();
            ClearMonsters();
            var world = World;
            var health = world.Column(RpgKeys.Health);
            var h = health[HeroRow];
            h.Current = 1f;
            health[HeroRow] = h;
            float2 hero = world.Column(RpgKeys.Position)[HeroRow];
            RpgSpawner.SpawnMonster(world, m_Game.Runtime, 3, hero + new float2(0.5f, 0f), 1);
            yield return UIDriver.WaitUntil(() => m_Game.Hud.DeadPanel.gameObject.activeInHierarchy, 8f);
            Assert.AreEqual(RpgFlow.Dead, State.Flow);
            UIDriver.Click(m_Game.Hud.RetryButton.gameObject);
            yield return UIDriver.WaitUntil(() => State.Flow == RpgFlow.Playing, 3f);
            Assert.Greater(State.MonstersAlive, 0);
            Assert.IsTrue(m_Game.Hud.HudPanel.gameObject.activeInHierarchy);
        }
    }

    /// <summary>
    /// The bot plays for a while on each render tier: no errors, progress, and a screenshot of the scene
    /// (Artifacts/Screenshots/rpg-&lt;tier&gt;.png) for visual review.
    /// </summary>
    public class RpgAutoPlayTests
    {
        [UnityTest]
        public IEnumerator BotPlaysWithoutErrors([Values(RenderTier.GpuDriven, RenderTier.DataTexture)] RenderTier tier)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("No graphics device");
            if (tier == RenderTier.GpuDriven && !SystemInfo.supportsComputeShaders)
                Assert.Ignore("No compute shader support");
            RenderCapabilities.Override = tier;
            string dir = Path.Combine(Path.GetTempPath(), "rpg-bot-" + System.Guid.NewGuid().ToString("N"));
            RpgGameBootstrap.SaveDirectoryOverride = dir;
            var config = RpgConfig.CreateDefault();
            var game = RpgGameBootstrap.Create(config, seed: 5, ui: true);
            var target = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32);
            var read = new Texture2D(960, 540, TextureFormat.RGBA32, false);
            try
            {
                yield return null;
                game.NewGame(4242);
                game.AutoPlay = true;
                // Real time, not ticks: the first run in an editor pays Burst / shader compilation, so wait
                // for progress (with a generous timeout) instead of a fixed duration.
                var state = game.State;
                float end = Time.realtimeSinceStartup + 45f;
                yield return UIDriver.WaitSeconds(6f);
                while (state.Profile.Kills < 2 && Time.realtimeSinceStartup < end) yield return null;
                game.Session.Sync();
                Assert.Greater(state.Profile.Kills, 0, "the bot fought");
                Assert.Greater(game.WorldRenderer.LastActorsDrawn, 0);
                Assert.Greater(game.WorldRenderer.SpritesDrawn, 500, "tiles, actors, weapons, effects are sprites");

                game.CameraRig.Camera.targetTexture = target;
                yield return null;
                yield return null;
                var previous = RenderTexture.active;
                RenderTexture.active = target;
                read.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
                read.Apply(false);
                RenderTexture.active = previous;
                game.CameraRig.Camera.targetTexture = null;
                string shots = Path.Combine(Application.dataPath, "..", "Artifacts", "Screenshots");
                Directory.CreateDirectory(shots);
                File.WriteAllBytes(Path.Combine(shots, $"rpg-{(tier == RenderTier.GpuDriven ? "gpu" : "datatex")}.png"), read.EncodeToPNG());
                var pixels = read.GetPixels32();
                int lit = 0;
                for (int i = 0; i < pixels.Length; i += 7) if (pixels[i].r + pixels[i].g + pixels[i].b > 90) lit++;
                Assert.Greater(lit, pixels.Length / 7 / 50, "the scene is drawn (tiles, actors)");
                TestContext.WriteLine($"bot ({tier}): floor {state.Profile.Floor}, level {state.Profile.Level}, kills {state.Profile.Kills}, gold {state.Profile.Gold}");
            }
            finally
            {
                RenderCapabilities.Override = null;
                RpgGameBootstrap.SaveDirectoryOverride = null;
                if (Camera.main != null) Object.Destroy(Camera.main.gameObject);
                Object.Destroy(game.gameObject);
                Object.Destroy(config);
                target.Release();
                Object.Destroy(target);
                Object.Destroy(read);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
