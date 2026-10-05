using System;
using SPF.Contracts;
using SPF.Runtime.Composition;
using SPF.Runtime.Session;
using SPF.Runtime.World;
using Unity.Mathematics;

namespace SurvivorFoundation.Tests
{
    sealed class SvTestWorld : IDisposable
    {
        public readonly SvConfig Config;
        public readonly SimSession Session;
        readonly GameplayModuleAsset m_Module;
        readonly ModeDefinition m_Mode;

        public SvTestWorld(uint seed = 5, Action<SvConfig> tweak = null, bool start = true)
        {
            Config = SvConfig.CreateDefault();
            tweak?.Invoke(Config);
            m_Mode = SvMode.Create(Config, out m_Module);
            Session = SimSession.Create(m_Mode, seed);
            Session.Start();
            if (start)
            {
                Game.Send(SvCommandKind.Start);
                Step();
            }
        }

        public SimWorld World => Session.World;
        public SvGameState Game => World.Resource(SvKeys.Game);
        public SvRuntime Runtime => World.Resource(SvKeys.Config);
        public int Enemies => World.Table(SvKeys.Enemy).Count;
        public int Bullets => World.Table(SvKeys.Bullet).Count;
        public int Gems => World.Table(SvKeys.Gem).Count;

        public void Step(int ticks = 1)
        {
            for (int i = 0; i < ticks; i++) Session.Step();
        }

        public void Input(float2 move) => Game.Input = InputFrame.Latch(Game.Input, new InputFrame { Move = move });

        public EntityHandle Spawn(int kind, float2 position, bool elite = false) =>
            SvSpawner.SpawnEnemy(World, Runtime, kind, position, elite);

        public void Dispose()
        {
            Session.Dispose();
            UnityEngine.Object.DestroyImmediate(m_Module);
            UnityEngine.Object.DestroyImmediate(m_Mode);
            UnityEngine.Object.DestroyImmediate(Config);
        }
    }
}
