using RpgFoundation.Systems;
using SPF.L1.Navigation;
using SPF.L1.Spatial;
using SPF.Runtime.Composition;
using SPF.Runtime.World;
using Unity.Mathematics;
using UnityEngine;

namespace RpgFoundation
{
    /// <summary>
    /// The RPG is composed of four modules (unlike the snake's single module) to exercise composition:
    /// they share keys through <see cref="RpgKeys"/> and each declares only its own data and systems.
    /// </summary>
    public abstract class RpgModuleBase : GameplayModuleAsset
    {
        [SerializeField] protected RpgConfig m_Config;

        public static T Create<T>(RpgConfig config) where T : RpgModuleBase
        {
            var module = CreateInstance<T>();
            module.hideFlags = HideFlags.DontSave;
            module.m_Config = config;
            return module;
        }

        protected RpgConfig Config => m_Config != null ? m_Config : (m_Config = RpgConfig.CreateDefault());
    }

    /// <summary>Config, game state and the event queues every other module uses.</summary>
    public sealed class RpgCoreModule : RpgModuleBase
    {
        public override void DeclareData(WorldLayout layout)
        {
            int events = Config.Capacity.EventQueue;
            layout.Resource(RpgKeys.Config, RpgRuntimeConfig.Bake(Config));
            layout.Resource(RpgKeys.Game, new RpgGameState());
            layout.Resource(RpgKeys.Feedback, new EventQueue<FeedbackEvent>(events, saved: false));
            layout.Resource(RpgKeys.Deaths, new EventQueue<DeathEvent>(Config.Capacity.Actors), levelScoped: true);
        }

        public override void RegisterSystems(SystemRegistry registry) { }
    }

    /// <summary>Tile map, floor generation and flow, the flow field towards the hero, stairs.</summary>
    public sealed class RpgDungeonModule : RpgModuleBase
    {
        public override void DeclareData(WorldLayout layout)
        {
            var d = Config.Dungeon;
            var size = new int2(d.Width, d.Height);
            layout.Resource(RpgKeys.Map, new TileMap(size, d.TileSize));
            layout.Resource(RpgKeys.Flow, new FlowField(size));
        }

        public override void RegisterSystems(SystemRegistry registry) => registry
            .Add(new FloorSystem())
            .Add(new FlowFieldSystem())
            .Add(new StairsSystem());
    }

    /// <summary>Hero and monsters: control, AI, movement, the actor grid, combat and projectiles.</summary>
    public sealed class RpgActorModule : RpgModuleBase
    {
        public override void DeclareData(WorldLayout layout)
        {
            var cap = Config.Capacity;
            layout.Table(RpgKeys.Actor, cap.Actors).LevelScoped()
                .Column(RpgKeys.Position).Column(RpgKeys.PrevPosition).Column(RpgKeys.Facing).Column(RpgKeys.MoveIntent)
                .Column(RpgKeys.Info).Column(RpgKeys.Health).Column(RpgKeys.Mana).Column(RpgKeys.Loadout).Column(RpgKeys.BaseStats).Column(RpgKeys.Stats)
                .Column(RpgKeys.Mods).Column(RpgKeys.Combat).Column(RpgKeys.Brain).Column(RpgKeys.Status);
            layout.Table(RpgKeys.Projectile, cap.Projectiles).LevelScoped()
                .Column(RpgKeys.ProjectilePosition).Column(RpgKeys.ProjectilePrev).Column(RpgKeys.ProjectileInfo);
            var d = Config.Dungeon;
            var cells = (int2)math.ceil(new float2(d.Width, d.Height) * d.TileSize / cap.GridCellSize);
            layout.Resource(RpgKeys.ActorGrid, new SpatialGrid(cells, cap.GridCellSize, cap.Actors));
            layout.Resource(RpgKeys.Hits, new EventQueue<HitEvent>(cap.EventQueue), levelScoped: true);
            layout.Resource(RpgKeys.ProjectileRequests, new EventQueue<ProjectileRequest>(cap.Projectiles), levelScoped: true);
            layout.DestroyQueueCapacity = math.max(layout.DestroyQueueCapacity, cap.Actors + cap.Projectiles);
        }

        public override void RegisterSystems(SystemRegistry registry) => registry
            .Add(new HeroControlSystem())
            .Add(new MonsterAISystem())
            .Add(new MovementSystem())
            .Add(new StatusSystem())
            .Add(new ActorGridSystem())
            .Add(new CombatSystem())
            .Add(new ResolveSystem())
            .Add(new ProjectileSpawnSystem());
    }

    /// <summary>Loot on the ground, experience, pickups and the inventory.</summary>
    public sealed class RpgRewardsModule : RpgModuleBase
    {
        public override void DeclareData(WorldLayout layout) =>
            layout.Table(RpgKeys.Item, Config.Capacity.Items).LevelScoped().Column(RpgKeys.ItemPosition).Column(RpgKeys.ItemInfo);

        public override void RegisterSystems(SystemRegistry registry) => registry
            .Add(new RewardSystem())
            .Add(new InventorySystem());
    }

    /// <summary>Builds the RPG mode (all four modules) for a config.</summary>
    public static class RpgMode
    {
        public static ModeDefinition Create(RpgConfig config, out GameplayModuleAsset[] modules)
        {
            modules = new GameplayModuleAsset[]
            {
                RpgModuleBase.Create<RpgCoreModule>(config),
                RpgModuleBase.Create<RpgDungeonModule>(config),
                RpgModuleBase.Create<RpgActorModule>(config),
                RpgModuleBase.Create<RpgRewardsModule>(config),
            };
            return ModeDefinition.Create(modules, SessionSettings.Default);
        }
    }
}
