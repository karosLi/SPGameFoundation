using System;
using SPF.Contracts;
using SPF.Runtime.Composition;
using SPF.Runtime.Session;
using SPF.Runtime.World;
using Unity.Mathematics;

namespace RpgFoundation.Tests
{
    /// <summary>An RPG session for tests: new game started, helpers to place actors and drive input.</summary>
    sealed class RpgTestWorld : IDisposable
    {
        public readonly RpgConfig Config;
        public readonly SimSession Session;
        readonly GameplayModuleAsset[] m_Modules;
        readonly ModeDefinition m_Mode;

        public RpgTestWorld(uint seed = 7, uint runSeed = 99, Action<RpgConfig> tweak = null, bool start = true)
        {
            Config = RpgConfig.CreateDefault();
            tweak?.Invoke(Config);
            m_Mode = RpgMode.Create(Config, out m_Modules);
            Session = SimSession.Create(m_Mode, seed);
            Session.Start();
            if (start)
            {
                Game.Profile.Reset(runSeed, World.Resource(RpgKeys.Config).StartPotions);
                Game.Send(RpgCommandKind.NewGame);
                Step();
            }
        }

        public SimWorld World => Session.World;
        public RpgGameState Game => World.Resource(RpgKeys.Game);
        public RpgRuntimeConfig Runtime => World.Resource(RpgKeys.Config);

        public void Step(int ticks = 1)
        {
            for (int i = 0; i < ticks; i++) Session.Step();
        }

        public void Input(InputFrame frame) => Game.Input = InputFrame.Latch(Game.Input, frame);

        public int HeroRow => World.Registry.TryResolve(Game.Hero, out _, out int row) ? row : -1;
        public int Row(EntityHandle h) => World.Registry.TryResolve(h, out _, out int row) ? row : -1;
        public float2 HeroPosition => World.Column(RpgKeys.Position)[HeroRow];

        /// <summary>Removes every monster (controlled scenarios).</summary>
        public void ClearMonsters()
        {
            var infos = World.Column(RpgKeys.Info);
            var handles = World.Table(RpgKeys.Actor).Handles;
            for (int row = World.Table(RpgKeys.Actor).Count - 1; row >= 0; row--)
                if (infos[row].Team == Team.Monsters)
                    World.DestroyEntity(handles[row]);
            Game.MonstersAlive = 0;
            Game.BossAlive = false;
        }

        public EntityHandle SpawnMonster(int kind, float2 position) => RpgSpawner.SpawnMonster(World, Runtime, kind, position, Game.Profile.Floor);

        /// <summary>A walkable position next to the hero (first free tile around it).</summary>
        public float2 FreeSpotNearHero(float distance)
        {
            var map = World.Resource(RpgKeys.Map).AsView();
            float2 hero = HeroPosition;
            for (int k = 0; k < 16; k++)
            {
                float a = k * math.PI / 8f;
                float2 p = hero + new float2(math.cos(a), math.sin(a)) * distance;
                if (!map.IsSolidAt(p) && map.LineOfSight(hero, p)) return p;
            }
            return hero;
        }

        public void Dispose()
        {
            Session.Dispose();
            foreach (var m in m_Modules) UnityEngine.Object.DestroyImmediate(m);
            UnityEngine.Object.DestroyImmediate(m_Mode);
            UnityEngine.Object.DestroyImmediate(Config);
        }
    }
}
