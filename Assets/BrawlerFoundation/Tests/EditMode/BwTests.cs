using System;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Runtime.Composition;
using SPF.Runtime.Session;
using SPF.Runtime.World;
using Unity.Mathematics;

namespace BrawlerFoundation.Tests
{
    sealed class BwTestWorld : IDisposable
    {
        public readonly SimSession Session;
        readonly GameplayModuleAsset m_Module;
        readonly ModeDefinition m_Mode;

        public BwTestWorld(bool start = true, BwSharedCombatConfig? sharedCombat = null, bool mobileCombat = false)
        {
            m_Mode = mobileCombat ? BwMode.CreateMobileCombat(out m_Module) : sharedCombat.HasValue ? BwMode.CreateSharedCombat(sharedCombat.Value, out m_Module) : BwMode.Create(out m_Module);
            Session = SimSession.Create(m_Mode, 7);
            Session.Start();
            if (start) { Game.Send(BwCommandKind.Start); Step(); }
        }

        public SimWorld World => Session.World;
        public BwGameState Game => World.Resource(BwKeys.Game);
        public int Count => World.Table(BwKeys.Fighter).Count;
        public FighterInfo Info(int row) => World.Column(BwKeys.Info)[row];
        public float2 Position(int row) => World.Column(BwKeys.Position)[row];
        public int Player { get { for (int i = 0; i < Count; i++) if (Info(i).Team == 0) return i; return -1; } }

        public void Step(int ticks = 1) { for (int i = 0; i < ticks; i++) Session.Step(); }

        public void Press(int button, float moveX = 0f)
        {
            Game.Input = InputFrame.Latch(Game.Input, new InputFrame { Move = new float2(moveX, 0f), Pressed = 1u << button, Held = 1u << button });
            Step();
            Game.Input = default;
        }

        /// <summary>Only the player and one enemy at <paramref name="enemyX"/> (player at 0 facing right).</summary>
        public int Duel(float enemyX, byte variant = 0)
        {
            World.ClearLevel();
            BwSpawner.Spawn(World, 0, new float2(0f, 0f), 1f, 0);
            BwSpawner.Spawn(World, 1, new float2(enemyX, 0f), -1f, variant);
            Step();   // variant 0 is a training dummy: it neither walks nor strikes
            return 1;
        }

        public void Dispose()
        {
            Session.Dispose();
            UnityEngine.Object.DestroyImmediate(m_Module);
            UnityEngine.Object.DestroyImmediate(m_Mode);
        }
    }

    public class BwTests
    {
        [Test]
        public void StartSpawnsThePlayerAndTheFirstWave()
        {
            using var t = new BwTestWorld();
            Assert.AreEqual(BwFlow.Fighting, t.Game.Flow);
            Assert.AreEqual(1, t.Game.Wave);
            Assert.AreEqual(3, t.Count, "player + two enemies");
            Assert.GreaterOrEqual(t.Player, 0);
        }

        [Test]
        public void PunchesConnectThroughTheFistBoneOnlyInReach()
        {
            using var t = new BwTestWorld();
            int enemy = t.Duel(0.9f);
            float hp = t.Info(enemy).Hp;
            t.Press(BwButton.Punch);
            t.Step(20);
            Assert.Less(t.Info(enemy).Hp, hp, "jab lands at 0.9 m");
            Assert.AreEqual(hp - 8f, t.Info(enemy).Hp, 1e-3f, "one hit per swing");

            enemy = t.Duel(1.4f);
            hp = t.Info(enemy).Hp;
            t.Press(BwButton.Punch);
            t.Step(20);
            Assert.AreEqual(hp, t.Info(enemy).Hp, "a jab does not reach 1.4 m");

            t.Press(BwButton.Kick);
            t.Step(30);
            Assert.Less(t.Info(enemy).Hp, hp, "the kick's longer leg does");
            Assert.Greater(t.Position(enemy).x, 1.4f, "knocked back");
        }

        [Test]
        public void ChainedPunchesAlternateJabAndCross()
        {
            using var t = new BwTestWorld();
            int enemy = t.Duel(0.9f);
            float hp = t.Info(enemy).Hp;
            t.Press(BwButton.Punch);
            t.Step(5);
            t.Press(BwButton.Punch);   // queued during the jab
            Assert.AreEqual(AttackKind.Jab, t.Info(t.Player).Attack);
            for (int i = 0; i < 40 && t.Info(t.Player).Attack != AttackKind.Cross; i++) t.Step();
            Assert.AreEqual(AttackKind.Cross, t.Info(t.Player).Attack, "combo continues with the cross");
            t.Step(30);
            Assert.Less(t.Info(enemy).Hp, hp - 8f, "both landed");
        }

        [Test]
        public void EnemiesCloseInAndHitThePlayer()
        {
            using var t = new BwTestWorld();
            float hp = t.Info(t.Player).Hp;
            for (int i = 0; i < 60 * 8 && t.Info(t.Player).Hp >= hp; i++) t.Step();
            Assert.Less(t.Info(t.Player).Hp, hp, "the enemies walked over and struck");
        }

        [Test]
        public void KnockOutsClearWavesAndTheGameIsWon()
        {
            using var t = new BwTestWorld();
            for (int wave = 1; wave <= BwRules.Waves; wave++)
            {
                Assert.AreEqual(wave, t.Game.Wave);
                var info = t.World.Column(BwKeys.Info);
                for (int i = 0; i < t.Count; i++)
                {
                    var f = info[i];
                    if (f.Team == 1) { f.Hp = 0f; f.State = FighterState.KO; f.StateTime = 0f; info[i] = f; }
                }
                for (int i = 0; i < 60 * 4 && t.Game.Wave == wave && t.Game.Flow != BwFlow.Won; i++) t.Step();
            }
            Assert.AreEqual(BwFlow.Won, t.Game.Flow);
            for (int i = 0; i < t.Count; i++)
                if (t.Info(i).Team == 1) Assert.AreEqual(FighterState.KO, t.Info(i).State);
        }

        [Test]
        public void FightsAreDeterministicAndSnapshotsResume()
        {
            byte[] Run(out byte[] mid)
            {
                using var t = new BwTestWorld();
                mid = null;
                for (int i = 0; i < 600; i++)
                {
                    if (i % 37 == 0) t.Game.Input = InputFrame.Latch(t.Game.Input, new InputFrame { Pressed = 1u << (i % 74 == 0 ? BwButton.Kick : BwButton.Punch), Move = new float2(math.sin(i * 0.02f), 0f) });
                    t.Step();
                    if (i == 300) mid = t.Session.CaptureSnapshot();
                }
                return t.Session.CaptureSnapshot();
            }
            var a = Run(out var midA);
            var b = Run(out _);
            CollectionAssert.AreEqual(a, b);
            using var r = new BwTestWorld(start: false);
            r.Session.RestoreSnapshot(midA);
            for (int i = 301; i < 600; i++)
            {
                if (i % 37 == 0) r.Game.Input = InputFrame.Latch(r.Game.Input, new InputFrame { Pressed = 1u << (i % 74 == 0 ? BwButton.Kick : BwButton.Punch), Move = new float2(math.sin(i * 0.02f), 0f) });
                r.Step();
            }
            CollectionAssert.AreEqual(a, r.Session.CaptureSnapshot());
        }
    }
}
