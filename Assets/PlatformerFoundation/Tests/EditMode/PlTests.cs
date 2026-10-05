using UnityEngine.TestTools.Constraints;
using System;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Runtime.Composition;
using SPF.Runtime.Session;
using SPF.Runtime.World;
using Unity.Mathematics;

namespace PlatformerFoundation.Tests
{
    static class AllocConstraint
    {
        public static AllocatingGCMemoryConstraint None => UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory();
    }

    sealed class PlTestWorld : IDisposable
    {
        public readonly SimSession Session;
        readonly GameplayModuleAsset m_Module;
        readonly ModeDefinition m_Mode;

        public PlTestWorld(bool start = true)
        {
            m_Mode = PlMode.Create(out m_Module);
            Session = SimSession.Create(m_Mode, 1);
            Session.Start();
            if (start) { Game.Send(PlCommandKind.Start); Step(); }
        }

        public SimWorld World => Session.World;
        public PlGameState Game => World.Resource(PlKeys.Game);

        public void Step(int ticks = 1) { for (int i = 0; i < ticks; i++) Session.Step(); }

        /// <summary>Holds a direction (and optionally jump) for some ticks; a jump press is sent on the first.</summary>
        public void Hold(float x, int ticks, bool jump = false)
        {
            for (int i = 0; i < ticks; i++)
            {
                uint bit = 1u << PlButton.Jump;
                Game.Input = InputFrame.Latch(Game.Input, new InputFrame { Move = new float2(x, 0f), Held = jump ? bit : 0u, Pressed = jump && i == 0 ? bit : 0u });
                Step();
            }
        }

        /// <summary>Puts the hero somewhere (standing still) for a scenario.</summary>
        public void Place(float2 p)
        {
            Game.Hero = Game.HeroPrev = p;
            Game.Motor = default;
            Game.Riding = -1;
        }

        public void Dispose()
        {
            Session.Dispose();
            UnityEngine.Object.DestroyImmediate(m_Module);
            UnityEngine.Object.DestroyImmediate(m_Mode);
        }
    }

    public class PlTests
    {
        [Test]
        public void LevelsBuildFromText()
        {
            using var t = new PlTestWorld();
            for (int level = 0; level < PlLevels.All.Length; level++)
            {
                PlLoader.Load(t.World, t.Game, level);
                Assert.AreEqual(PlFlow.Playing, t.Game.Flow);
                Assert.Greater(t.Game.CoinsInLevel, 5, $"level {level} has coins");
                Assert.Greater(t.World.Table(PlKeys.Walker).Count, 0);
                var map = t.World.Resource(PlKeys.Map).AsView();
                Assert.IsTrue(map.BoxGrounded(t.Game.Start, PlGameState.HeroHalf, PlTile.OneWay), $"level {level}: the start stands on ground");
                Assert.Greater(t.Game.GoalPosition.x, t.Game.Start.x + 40f, "the goal is far to the right");
            }
        }

        [Test]
        public void RunJumpAndCollectCoins()
        {
            using var t = new PlTestWorld();
            t.Step(5);
            Assert.IsTrue(t.Game.Motor.Grounded);
            float x0 = t.Game.Hero.x;
            t.Hold(1f, 30);
            Assert.Greater(t.Game.Hero.x, x0 + 2f, "runs right");
            // Level 1: a coin above the first ledge at x = 6; jump onto the ledge from the start.
            t.Place(new float2(5.5f, t.Game.Start.y));
            t.Step(3);
            int coins = t.Game.Coins;
            t.Hold(0.3f, 50, jump: true);
            Assert.Greater(t.Game.Coins, coins, "collected the coin over the ledge");
            Assert.AreEqual(1, t.World.Table(PlKeys.Coin).Count == t.Game.CoinsInLevel - t.Game.Coins ? 1 : 0, "collected coins were compacted away");
        }

        [Test]
        public void StompingWalkersKillsThemTouchingThemHurts()
        {
            using var t = new PlTestWorld();
            var walkers = t.World.Column(PlKeys.WalkerPosition);
            float2 w = walkers[0];
            int count = t.World.Table(PlKeys.Walker).Count;
            // Drop onto it from above.
            t.Place(w + new float2(0f, 3f));
            for (int i = 0; i < 60 && t.Game.Stomps == 0; i++) t.Step();
            Assert.AreEqual(1, t.Game.Stomps, "stomped");
            Assert.Greater(t.Game.Motor.Velocity.y, 0f, "bounced");
            t.Step(40);
            Assert.AreEqual(count - 1, t.World.Table(PlKeys.Walker).Count, "removed after its squash animation");

            // Walk into another one: lose a life and respawn at the start.
            t.Place(walkers[0] + new float2(-1.2f, 0.05f));
            int lives = t.Game.Lives;
            for (int i = 0; i < 60 && t.Game.Flow == PlFlow.Playing; i++) t.Hold(1f, 1);
            Assert.AreEqual(PlFlow.Dying, t.Game.Flow);
            Assert.AreEqual(lives - 1, t.Game.Lives);
            t.Game.Input = default;   // let go of the stick
            t.Step(70);
            Assert.AreEqual(PlFlow.Playing, t.Game.Flow);
            Assert.Less(math.distance(t.Game.Hero, t.Game.Start), 0.5f, "respawned");
        }

        [Test]
        public void SpikesAndPitsKillAndLivesRunOut()
        {
            using var t = new PlTestWorld();
            for (int life = 3; life > 0; life--)
            {
                t.Place(new float2(11.5f, 4f));   // over the first spike pit
                for (int i = 0; i < 120 && t.Game.Flow == PlFlow.Playing; i++) t.Step();
                Assert.AreEqual(PlFlow.Dying, t.Game.Flow);
                t.Step(70);
            }
            Assert.AreEqual(PlFlow.GameOver, t.Game.Flow);
            t.Game.Send(PlCommandKind.Retry);
            t.Step();
            Assert.AreEqual(PlFlow.Playing, t.Game.Flow);
            Assert.AreEqual(3, t.Game.Lives);
        }

        [Test]
        public void MovingPlatformsCarryTheHero()
        {
            using var t = new PlTestWorld();
            var platforms = t.World.Column(PlKeys.PlatformPosition);
            var info = t.World.Column(PlKeys.PlatformInfo);
            Assert.Greater(t.World.Table(PlKeys.Platform).Count, 0);
            float2 top = platforms[0] + new float2(0f, info[0].Half.y + PlGameState.HeroHalf.y + 0.3f);
            t.Place(top);
            for (int i = 0; i < 30 && t.Game.Riding < 0; i++) t.Step();
            Assert.AreEqual(0, t.Game.Riding, "landed on the platform");
            float offset = t.Game.Hero.x - platforms[0].x;
            t.Step(90);
            Assert.AreEqual(0, t.Game.Riding, "still riding");
            Assert.AreEqual(offset, t.Game.Hero.x - platforms[0].x, 0.05f, "moved with it");
        }

        [Test]
        public void ReachingTheGoalCompletesTheLevelAndTheLastWins()
        {
            using var t = new PlTestWorld();
            for (int level = 0; level < PlLevels.All.Length; level++)
            {
                Assert.AreEqual(level, t.Game.Level);
                t.Place(t.Game.GoalPosition + new float2(-1.5f, 0.2f));
                for (int i = 0; i < 120 && t.Game.Flow == PlFlow.Playing; i++) t.Hold(1f, 1);
                Assert.AreEqual(PlFlow.LevelComplete, t.Game.Flow, $"level {level} completed");
                t.Game.Send(PlCommandKind.NextLevel);
                t.Step();
            }
            Assert.AreEqual(PlFlow.Won, t.Game.Flow);
        }

        [Test]
        public void ReplaysAndSnapshotsAreExact()
        {
            byte[] Run(out byte[] mid)
            {
                using var t = new PlTestWorld();
                mid = null;
                for (int i = 0; i < 600; i++)
                {
                    t.Hold(math.sin(i * 0.02f) > -0.3f ? 1f : -0.5f, 1, jump: i % 45 < 12);
                    if (i == 300) mid = t.Session.CaptureSnapshot();
                }
                return t.Session.CaptureSnapshot();
            }
            var a = Run(out var midA);
            var b = Run(out _);
            CollectionAssert.AreEqual(a, b);
            using var r = new PlTestWorld(start: false);
            r.Session.RestoreSnapshot(midA);
            for (int i = 301; i < 600; i++) r.Hold(math.sin(i * 0.02f) > -0.3f ? 1f : -0.5f, 1, jump: i % 45 < 12);
            CollectionAssert.AreEqual(a, r.Session.CaptureSnapshot());
        }
    
        [Test]
        public void SteadyStateTicksDoNotAllocate()
        {
            using var t = new PlTestWorld();
            t.Hold(1f, 120, jump: true);
            Assert.That(() => { t.Hold(1f, 60, jump: true); t.Hold(-1f, 60); }, AllocConstraint.None);
        }
    }
}
