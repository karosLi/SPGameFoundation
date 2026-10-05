using DefenseFoundation.Systems;
using SPF.L1.Navigation;
using SPF.L1.Spatial;
using SPF.Runtime.Composition;
using SPF.Runtime.World;
using Unity.Mathematics;
using UnityEngine;

namespace DefenseFoundation
{
    public sealed class TdModule : GameplayModuleAsset
    {
        public static TdModule Create()
        {
            var module = CreateInstance<TdModule>();
            module.hideFlags = HideFlags.DontSave;
            return module;
        }

        public override void DeclareData(WorldLayout layout)
        {
            var size = TdRules.MapSize;
            layout.Table(TdKeys.Enemy, 1024).LevelScoped().Column(TdKeys.Position).Column(TdKeys.PrevPosition).Column(TdKeys.Info);
            layout.Table(TdKeys.Tower, 256).LevelScoped().Column(TdKeys.TowerInfo);
            layout.Table(TdKeys.Shot, 2048).LevelScoped().Pooled().Column(TdKeys.ShotPosition).Column(TdKeys.ShotInfo);
            layout.Resource(TdKeys.Game, new TdGameState());
            layout.Resource(TdKeys.Rules, TdRules.CreateDefault());
            layout.Resource(TdKeys.Map, new TileMap(size, 1f));
            layout.Resource(TdKeys.Flow, new FlowField(size));
            layout.Resource(TdKeys.Grid, new SpatialGrid(size, 1f, 1024), levelScoped: true);
            layout.Resource(TdKeys.Hits, new EventQueue<TdHit>(8192), levelScoped: true);
            layout.Resource(TdKeys.ShotSpawns, new EventQueue<ShotInfo>(512), levelScoped: true);
            layout.Resource(TdKeys.Rewards, new EventQueue<int>(1024), levelScoped: true);
            layout.Resource(TdKeys.Leaks, new EventQueue<int>(1024), levelScoped: true);
            layout.Resource(TdKeys.Feedback, new EventQueue<TdFeedback>(4096, saved: false));
        }

        public override void RegisterSystems(SystemRegistry registry) => registry
            .Add(new CommandSystem())
            .Add(new WaveSystem())
            .Add(new PathSystem())
            .Add(new EnemyMoveSystem())
            .Add(new GridSystem())
            .Add(new CombatSystem())
            .Add(new ResolveSystem());
    }

    public static class TdMode
    {
        public static ModeDefinition Create(out GameplayModuleAsset module)
        {
            module = TdModule.Create();
            return ModeDefinition.Create(new[] { module }, SessionSettings.Default);
        }
    }

    /// <summary>Queries for UI previews (main thread, between ticks).</summary>
    public static class TdQueries
    {
        /// <summary>Whether a tower could be built there now, and why not.</summary>
        public static bool CanBuild(SPF.Runtime.Session.SimSession session, int2 cell, TowerKind kind, out string reason)
        {
            var pipeline = session.Pipeline;
            for (int i = 0; i < pipeline.SystemCount; i++)
                if (pipeline.GetSystem(i) is CommandSystem commands)
                {
                    var world = session.World;
                    return commands.CanBuild(world, world.Resource(TdKeys.Game), world.Resource(TdKeys.Rules), world.Resource(TdKeys.Map), cell, kind, out reason);
                }
            reason = "no command system";
            return false;
        }

        /// <summary>Spawns an enemy (tests, tools).</summary>
        public static SPF.Contracts.EntityHandle SpawnEnemy(SimWorld world, int kind, float2 position, int wave = 0) =>
            WaveSystem.Spawn(world, world.Resource(TdKeys.Rules), kind, position, wave);
    }
}
