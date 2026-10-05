using PlatformerFoundation.Systems;
using SPF.L1.Spatial;
using SPF.Runtime.Composition;
using SPF.Runtime.World;
using Unity.Mathematics;
using UnityEngine;

namespace PlatformerFoundation
{
    /// <summary>The platformer as one module: two tile layers (collision, hazards), walkers, platforms, pooled coins.</summary>
    public sealed class PlModule : GameplayModuleAsset
    {
        public static readonly int2 MapSize = new int2(96, 24);

        public static PlModule Create()
        {
            var module = CreateInstance<PlModule>();
            module.hideFlags = HideFlags.DontSave;
            return module;
        }

        public override void DeclareData(WorldLayout layout)
        {
            layout.Table(PlKeys.Walker, 128).LevelScoped().Column(PlKeys.WalkerPosition).Column(PlKeys.WalkerPrev).Column(PlKeys.WalkerInfo);
            layout.Table(PlKeys.Platform, 64).LevelScoped().Column(PlKeys.PlatformPosition).Column(PlKeys.PlatformPrev).Column(PlKeys.PlatformInfo);
            layout.Table(PlKeys.Coin, 512).LevelScoped().Pooled().Column(PlKeys.CoinPosition);
            layout.Resource(PlKeys.Game, new PlGameState());
            layout.Resource(PlKeys.Map, new TileMap(MapSize, 1f));
            layout.Resource(PlKeys.Hazards, new TileMap(MapSize, 1f));
            layout.Resource(PlKeys.Feedback, new EventQueue<PlFeedback>(256, saved: false));
        }

        public override void RegisterSystems(SystemRegistry registry) => registry
            .Add(new FlowSystem())
            .Add(new PlatformSystem())
            .Add(new WalkerSystem())
            .Add(new HeroSystem());
    }

    public static class PlMode
    {
        /// <summary>60 ticks per second: platformer input and landings want the finer step.</summary>
        public static ModeDefinition Create(out GameplayModuleAsset module)
        {
            module = PlModule.Create();
            var settings = SessionSettings.Default;
            settings.TickRate = 60;
            settings.MaxTicksPerFrame = 4;
            return ModeDefinition.Create(new[] { module }, settings);
        }
    }
}

namespace PlatformerFoundation
{
    /// <summary>Level loading for tests, tools and level select.</summary>
    public static class PlLoader
    {
        public static void Load(SPF.Runtime.World.SimWorld world, PlGameState game, int level) => Systems.FlowSystem.Load(world, game, level);
    }
}
