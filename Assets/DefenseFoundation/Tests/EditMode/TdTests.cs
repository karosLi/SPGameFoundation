using UnityEngine.TestTools.Constraints;
using System;
using NUnit.Framework;
using SPF.L1.Navigation;
using SPF.L2.Progression;
using SPF.Runtime.Composition;
using SPF.Runtime.Session;
using SPF.Runtime.World;
using Unity.Mathematics;

namespace DefenseFoundation.Tests
{
    static class AllocConstraint
    {
        public static AllocatingGCMemoryConstraint None => UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory();
    }

    sealed class TdTestWorld : IDisposable
    {
        public readonly SimSession Session;
        readonly GameplayModuleAsset m_Module;
        readonly ModeDefinition m_Mode;

        public TdTestWorld(bool start = true)
        {
            m_Mode = TdMode.Create(out m_Module);
            Session = SimSession.Create(m_Mode, 3);
            Session.Start();
            if (start) { Game.Send(TdCommandKind.Start); Step(); }
        }

        public SimWorld World => Session.World;
        public TdGameState Game => World.Resource(TdKeys.Game);
        public TdRules Rules => World.Resource(TdKeys.Rules);
        public int Enemies => World.Table(TdKeys.Enemy).Count;
        public int Towers => World.Table(TdKeys.Tower).Count;

        public void Step(int ticks = 1) { for (int i = 0; i < ticks; i++) Session.Step(); }

        public void Build(int2 cell, TowerKind kind) { Game.Send(TdCommandKind.Build, cell, kind); Step(); }

        /// <summary>Flow-field steps from the spawn to the base (the current route length).</summary>
        public int RouteLength()
        {
            Step();
            var map = World.Resource(TdKeys.Map).AsView();
            return World.Resource(TdKeys.Flow).AsView(map).DistanceAt(Rules.Spawn);
        }

        public void Dispose()
        {
            Session.Dispose();
            UnityEngine.Object.DestroyImmediate(m_Module);
            UnityEngine.Object.DestroyImmediate(m_Mode);
        }
    }

    public class TdTests
    {
        [Test]
        public void TowersCostGoldAndEnemiesRouteAroundThem()
        {
            using var t = new TdTestWorld();
            Assert.AreEqual(TdFlow.Building, t.Game.Flow);
            int route = t.RouteLength();
            Assert.AreEqual(23, route, "straight across an open middle row");
            int gold = t.Game.Gold;
            t.Build(new int2(12, 7), TowerKind.Arrow);
            Assert.AreEqual(1, t.Towers);
            Assert.AreEqual(gold - t.Rules.Towers[(int)TowerKind.Arrow].Cost, t.Game.Gold);
            Assert.Greater(t.RouteLength(), route, "the route bends around the tower");
        }

        [Test]
        public void BuildsThatWouldSealThePathAreRefused()
        {
            using var t = new TdTestWorld();
            t.Game.Gold = 10000;
            // A wall across column 6 with a single gap at y = 13.
            for (int y = 0; y < 13; y++) t.Game.Send(TdCommandKind.Build, new int2(6, y), TowerKind.Arrow);
            t.Step();
            Assert.AreEqual(13, t.Towers);
            Assert.Greater(t.RouteLength(), 23, "the route goes through the gap");
            int rejected = t.Game.LastRejected;
            t.Build(new int2(6, 13), TowerKind.Arrow);
            Assert.AreEqual(13, t.Towers, "sealing the gap is not allowed");
            Assert.AreEqual(rejected + 1, t.Game.LastRejected);
            Assert.IsFalse(TdQueries.CanBuild(t.Session, new int2(6, 13), TowerKind.Arrow, out string reason));
            StringAssert.Contains("path", reason);
            t.Game.Gold = 0;
            Assert.IsFalse(TdQueries.CanBuild(t.Session, new int2(3, 3), TowerKind.Cannon, out reason));
            StringAssert.Contains("gold", reason);
        }

        [Test]
        public void TowersShootEnemiesForGoldAndLeaksCostLives()
        {
            using var t = new TdTestWorld();
            t.Game.Gold = 1000;
            // Arrow and cannon towers along the middle of the route.
            for (int x = 6; x <= 16; x += 2) t.Build(new int2(x, 8), x % 4 == 0 ? TowerKind.Cannon : TowerKind.Arrow);
            for (int x = 7; x <= 17; x += 2) t.Build(new int2(x, 6), TowerKind.Frost);
            int gold = t.Game.Gold;
            t.Game.Send(TdCommandKind.NextWave);
            t.Step();
            for (int i = 0; i < 30 * 60 && t.Game.Flow == TdFlow.Wave; i++) t.Step();
            Assert.AreEqual(TdFlow.Building, t.Game.Flow, "wave 1 cleared");
            Assert.AreEqual(t.Rules.Waves[0][0].Count, t.Game.Kills, "every runner was shot");
            Assert.Greater(t.Game.Gold, gold, "kill rewards and the wave bonus");
            Assert.AreEqual(t.Rules.StartLives, t.Game.Lives, "nothing leaked");

            // Undefended: everything leaks.
            using var u = new TdTestWorld();
            u.Game.Send(TdCommandKind.NextWave);
            u.Step();
            for (int i = 0; i < 30 * 60 && u.Game.Flow == TdFlow.Wave; i++) u.Step();
            Assert.AreEqual(u.Rules.StartLives - u.Rules.Waves[0][0].Count, u.Game.Lives);
        }

        [Test]
        public void FrostSlowsAndCannonsSplash()
        {
            using var t = new TdTestWorld();
            t.Game.Gold = 1000;
            t.Build(new int2(5, 8), TowerKind.Frost);
            var map = t.World.Resource(TdKeys.Map).AsView();
            var e = TdQueries.SpawnEnemy(t.World, 2, map.CenterOf(new int2(4, 7)));
            bool slowed = false;
            for (int i = 0; i < 60 && !slowed; i++)
            {
                t.Step();
                if (t.World.Registry.TryResolve(e, out _, out int row)) slowed = t.World.Column(TdKeys.Info)[row].Slow > 0f;
            }
            Assert.IsTrue(slowed, "frost slows");

            using var c = new TdTestWorld();
            c.Game.Gold = 1000;
            c.Build(new int2(5, 8), TowerKind.Cannon);
            var view = c.World.Resource(TdKeys.Map).AsView();
            for (int k = 0; k < 4; k++) TdQueries.SpawnEnemy(c.World, 2, view.CenterOf(new int2(4, 7)) + new float2(0.1f * k, 0f));
            float Total()
            {
                float hp = 0f;
                var infos = c.World.Column(TdKeys.Info);
                for (int i = 0; i < c.Enemies; i++) hp += infos[i].Hp;
                return hp;
            }
            float before = Total();
            for (int i = 0; i < 45; i++) c.Step();
            int damaged = 0;
            var infos2 = c.World.Column(TdKeys.Info);
            for (int i = 0; i < c.Enemies; i++) if (infos2[i].Hp < infos2[i].MaxHp) damaged++;
            Assert.Greater(damaged, 1, "one shell hurt several enemies");
            Assert.Less(Total(), before);
        }

        [Test]
        public void UpgradeAndSell()
        {
            using var t = new TdTestWorld();
            t.Game.Gold = 1000;
            t.Build(new int2(3, 3), TowerKind.Arrow);
            int gold = t.Game.Gold;
            t.Game.Send(TdCommandKind.Upgrade, new int2(3, 3));
            t.Step();
            Assert.AreEqual(2, t.World.Column(TdKeys.TowerInfo)[0].Level);
            Assert.AreEqual(gold - t.Rules.UpgradeCost(TowerKind.Arrow, 1), t.Game.Gold);
            int invested = t.World.Column(TdKeys.TowerInfo)[0].Invested;
            gold = t.Game.Gold;
            t.Game.Send(TdCommandKind.Sell, new int2(3, 3));
            t.Step();
            Assert.AreEqual(0, t.Towers);
            Assert.AreEqual(gold + invested * 7 / 10, t.Game.Gold);
            Assert.AreEqual(TdTile.Ground, t.World.Resource(TdKeys.Map)[new int2(3, 3)]);
        }

        [Test]
        public void ShortCampaignWinsAndLivesRunningOutLoses()
        {
            using var t = new TdTestWorld();
            var rules = t.Rules;
            rules.Waves.RemoveRange(2, rules.Waves.Count - 2);
            rules.Waves[1] = new[] { new WaveGroup { Start = 0f, Kind = 3, Count = 4, Interval = 0.3f } };
            t.Game.Gold = 2000;
            for (int x = 4; x <= 18; x += 2) { t.Build(new int2(x, 8), TowerKind.Cannon); t.Build(new int2(x + 1, 6), TowerKind.Arrow); }
            for (int i = 0; i < 30 * 120 && t.Game.Flow != TdFlow.Won && t.Game.Flow != TdFlow.Lost; i++)
            {
                if (t.Game.Flow == TdFlow.Building) t.Game.Send(TdCommandKind.NextWave);
                t.Step();
            }
            Assert.AreEqual(TdFlow.Won, t.Game.Flow);

            using var u = new TdTestWorld();
            u.Game.Lives = 2;
            u.Game.Send(TdCommandKind.NextWave);
            for (int i = 0; i < 30 * 40 && u.Game.Flow != TdFlow.Lost; i++) u.Step();
            Assert.AreEqual(TdFlow.Lost, u.Game.Flow);
        }

        [Test]
        public void DeterministicWithSnapshots()
        {
            byte[] Run(out byte[] mid)
            {
                using var t = new TdTestWorld();
                t.Game.Gold = 500;
                for (int x = 5; x <= 17; x += 3) t.Game.Send(TdCommandKind.Build, new int2(x, 8), (TowerKind)(x % 3));
                mid = null;
                for (int i = 0; i < 900; i++)
                {
                    if (t.Game.Flow == TdFlow.Building) t.Game.Send(TdCommandKind.NextWave);
                    t.Step();
                    if (i == 450) mid = t.Session.CaptureSnapshot();
                }
                return t.Session.CaptureSnapshot();
            }
            var a = Run(out var midA);
            CollectionAssert.AreEqual(a, Run(out _));
            using var r = new TdTestWorld(start: false);
            r.Session.RestoreSnapshot(midA);
            for (int i = 451; i < 900; i++)
            {
                if (r.Game.Flow == TdFlow.Building) r.Game.Send(TdCommandKind.NextWave);
                r.Step();
            }
            CollectionAssert.AreEqual(a, r.Session.CaptureSnapshot());
        }
    
        [Test]
        public void SteadyStateWaveTicksDoNotAllocate()
        {
            using var t = new TdTestWorld();
            t.Game.Gold = 1000;
            for (int x = 6; x <= 16; x += 2) t.Build(new int2(x, 8), x % 4 == 0 ? TowerKind.Cannon : TowerKind.Arrow);
            t.Game.Send(TdCommandKind.NextWave);
            t.Step(120);
            Assert.That(() => t.Step(240), AllocConstraint.None);
        }
    }
}
