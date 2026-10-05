using System;
using NUnit.Framework;
using SPF.L1.Physics;
using SPF.Runtime.Composition;
using SPF.Runtime.Session;
using Unity.Mathematics;

namespace SlingFoundation.Tests
{
    sealed class SlTestWorld : IDisposable
    {
        public readonly SimSession Session;
        readonly GameplayModuleAsset m_Module;
        readonly ModeDefinition m_Mode;

        public SlTestWorld(bool start = true, uint seed = 1)
        {
            m_Mode = SlMode.Create(out m_Module);
            Session = SimSession.Create(m_Mode, seed);
            Session.Start();
            if (start) { Game.Send(SlCommandKind.Start); Step(); }
        }

        public SlGameState Game => Session.World.Resource(SlKeys.Game);
        public PhysicsWorld2D Physics => Session.World.Resource(SlKeys.Physics);
        public void Step(int ticks = 1) { for (int i = 0; i < ticks; i++) Session.Step(); }

        /// <summary>Fires one bird and runs until the shot is over; returns targets downed.</summary>
        public int Shoot(float2 pull, int maxTicks = 60 * 14)
        {
            int before = Game.TargetsLeft;
            Game.Send(SlCommandKind.Launch, pull);
            Step();
            for (int i = 0; i < maxTicks && Game.Flow == SlFlow.Flying; i++) Step();
            return before - Game.TargetsLeft;
        }

        public void Dispose()
        {
            Session.Dispose();
            UnityEngine.Object.DestroyImmediate(m_Module);
            UnityEngine.Object.DestroyImmediate(m_Mode);
        }
    }

    public class SlTests
    {
        /// <summary>A pull (drag down-left of the sling) that sends the bird into the level 0 tower.</summary>
        internal static readonly float2 GoodShot = new float2(-2.4f, -0.4f);   // centre of a region of pulls that all hit (see SearchShots)

        [Test]
        public void LevelsBuildAndStandStill()
        {
            for (int level = 0; level < SlLevels.Count; level++)
            {
                using var t = new SlTestWorld();
                if (level > 0) { SlLevels.Build(t.Physics, t.Game, level); t.Step(); }
                int targets = t.Game.TargetsLeft;
                Assert.Greater(targets, 0);
                t.Step(60 * 4);
                Assert.AreEqual(targets, t.Game.TargetsLeft, $"level {level}: nothing breaks on its own");
                Assert.AreEqual(SlFlow.Aiming, t.Game.Flow);
                Assert.AreEqual(0, t.Physics.Stats.AwakeBodies, $"level {level}: the structure settles and sleeps");
            }
        }

        [Test]
        public void ShotsDownTargetsAndClearTheLevel()
        {
            using var t = new SlTestWorld();
            int downed = t.Shoot(GoodShot);
            TestContext.WriteLine($"first shot downed {downed}, score {t.Game.Score}, flow {t.Game.Flow}");
            Assert.Greater(downed, 0, "the bird knocked the tower over");
            Assert.Greater(t.Game.Score, 0);
            Assert.AreEqual(SlFlow.Aiming, t.Game.Flow, "a target is left: next bird");
            Assert.AreEqual(SlRules.BirdsPerLevel - 1, t.Game.BirdsLeft);

            // Knock the remaining targets off the world: the level clears, with a bonus for unused birds.
            int score = t.Game.Score;
            for (int i = 0; i < t.Physics.HighWater; i++)
                if (t.Game.Kind[i] == PieceKind.Target)
                {
                    var b = t.Physics[i];
                    b.Position = new float2(30f, -10f);
                    t.Physics[i] = b;
                }
            t.Step(2);
            Assert.AreEqual(0, t.Game.TargetsLeft);
            t.Game.Send(SlCommandKind.Launch, new float2(0.5f, -0.2f));
            t.Step(120);
            Assert.AreEqual(SlFlow.LevelClear, t.Game.Flow);
            Assert.GreaterOrEqual(t.Game.Score - score, 1000 + 2000, "target points and the unused-bird bonus");
            t.Game.Send(SlCommandKind.NextLevel);
            t.Step();
            Assert.AreEqual(1, t.Game.Level);
            Assert.AreEqual(SlFlow.Aiming, t.Game.Flow);
        }

        [Test]
        public void MissingEveryShotFails()
        {
            using var t = new SlTestWorld();
            for (int i = 0; i < SlRules.BirdsPerLevel; i++) t.Shoot(new float2(0.5f, -0.2f));   // weak shots backwards
            Assert.AreEqual(SlFlow.Failed, t.Game.Flow);
            t.Game.Send(SlCommandKind.Retry);
            t.Step();
            Assert.AreEqual(SlFlow.Aiming, t.Game.Flow);
            Assert.AreEqual(SlRules.BirdsPerLevel, t.Game.BirdsLeft);
        }

        [Test]
        public void ShotsAreDeterministicAndSnapshotsResume()
        {
            byte[] Run(out byte[] mid)
            {
                using var t = new SlTestWorld();
                t.Game.Send(SlCommandKind.Launch, GoodShot);
                mid = null;
                for (int i = 0; i < 300; i++)
                {
                    t.Step();
                    if (i == 90) mid = t.Session.CaptureSnapshot();
                }
                return t.Session.CaptureSnapshot();
            }
            var a = Run(out var midA);
            var b = Run(out _);
            CollectionAssert.AreEqual(a, b, "same shot, same collapse");

            using var r = new SlTestWorld(start: false);
            r.Session.RestoreSnapshot(midA);
            for (int i = 91; i < 300; i++) r.Step();
            CollectionAssert.AreEqual(a, r.Session.CaptureSnapshot(), "restored mid-flight, identical outcome");
        }

        [Test, Explicit("tuning aid")]
        public void SearchShots()
        {
            for (float x = -3f; x <= -1.5f; x += 0.2f)
                for (float y = -2f; y <= 0.2f; y += 0.2f)
                {
                    using var t = new SlTestWorld();
                    int downed = t.Shoot(new float2(x, y));
                    if (downed > 0) TestContext.WriteLine($"pull ({x:F1}, {y:F1}) downed {downed}");
                }
        }
    }
}
