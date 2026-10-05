using System;
using SPF.Contracts;
using SPF.Runtime.Composition;
using SPF.Runtime.Session;
using SPF.Runtime.World;
using Unity.Mathematics;

namespace SnakeFoundation.Tests
{
    /// <summary>Builds a snake session for tests, optionally without AI / food so scenarios are controlled.</summary>
    sealed class SnakeTestWorld : IDisposable
    {
        public readonly SnakeConfig Config;
        public readonly SnakeGameModule Module;
        public readonly SimSession Session;

        public SnakeTestWorld(int aiPerRegion = 0, int foodPerChunk = 0, int propsPerChunk = 0, uint seed = 42, Action<SnakeConfig> tweak = null)
        {
            Config = SnakeConfig.CreateDefault();
            Config.AI.SnakesPerRegion = aiPerRegion;
            Config.Food.FoodPerChunk = foodPerChunk;
            Config.Food.PropsPerChunk = propsPerChunk;
            tweak?.Invoke(Config);
            Module = SnakeGameModule.Create(Config);
            Session = new SimSession(new IGameplayModule[] { Module }, SessionSettings.Default, seed);
            Session.Start();
        }

        public SimWorld World => Session.World;
        public SnakeGameState Game => World.Resource(SnakeKeys.Game);
        public SnakeRuntimeConfig Runtime => World.Resource(SnakeKeys.Config);

        public void Step(int ticks = 1)
        {
            for (int i = 0; i < ticks; i++) Session.Step();
        }

        public int PlayerRow
        {
            get
            {
                World.Registry.TryResolve(Game.Player, out _, out int row);
                return row;
            }
        }

        public int Row(EntityHandle handle) => World.Registry.TryResolve(handle, out _, out int row) ? row : -1;

        public void StartPlayer()
        {
            Game.RequestStart();
            Step();
        }

        /// <summary>Moves a snake (whole body) so its head is at <paramref name="head"/> facing <paramref name="heading"/>, laid out straight.</summary>
        public void Place(EntityHandle snake, float2 head, float2 heading)
        {
            int row = Row(snake);
            var bodies = World.Resource(SnakeKeys.Bodies);
            var trails = World.Column(SnakeKeys.Trail);
            var trail = trails[row];
            SPF.L1.Body.TrailMath.Reset(ref trail, bodies.Points, head, -heading, trail.Spacing, trail.Count);
            trails[row] = trail;
            World.Column(SnakeKeys.Head).Set(row, head);
            World.Column(SnakeKeys.PrevHead).Set(row, head);
            World.Column(SnakeKeys.Heading).Set(row, math.normalize(heading));
            World.Column(SnakeKeys.Control).Set(row, new SnakeControl { TargetDirection = math.normalize(heading) });
            var info = World.Column(SnakeKeys.Info)[row];
            info.Protection = 0f;
            World.Column(SnakeKeys.Info).Set(row, info);
        }

        public EntityHandle SpawnAI(float2 head, float2 heading, float mass)
        {
            var random = new Unity.Mathematics.Random(7);
            var handle = SnakeSpawner.SpawnSnake(World, Runtime, Game, Game.ActiveRegion, head, heading, mass, SnakeFlags.AI, 0, ref random);
            Place(handle, head, heading);
            return handle;
        }

        public void Dispose()
        {
            Session.Dispose();
            UnityEngine.Object.DestroyImmediate(Module);
            UnityEngine.Object.DestroyImmediate(Config);
        }
    }
}
