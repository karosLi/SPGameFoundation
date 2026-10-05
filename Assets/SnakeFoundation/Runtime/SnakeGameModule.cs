using SnakeFoundation.Systems;
using SPF.L1.Body;
using SPF.L1.Spatial;
using SPF.Runtime.Composition;
using SPF.Runtime.World;
using Unity.Mathematics;
using UnityEngine;

namespace SnakeFoundation
{
    /// <summary>
    /// The snake game mode as one gameplay module: declares all tables / resources and registers the
    /// systems in pipeline order. Configuration comes from a <see cref="SnakeConfig"/> asset (or defaults).
    /// </summary>
    [CreateAssetMenu(menuName = "SPF/Snake/Snake Game Module", fileName = "SnakeGameModule")]
    public sealed class SnakeGameModule : GameplayModuleAsset
    {
        [SerializeField] SnakeConfig m_Config;

        public override string Id => "Snake";

        public SnakeConfig Config
        {
            get => m_Config;
            set => m_Config = value;
        }

        public static SnakeGameModule Create(SnakeConfig config)
        {
            var module = CreateInstance<SnakeGameModule>();
            module.m_Config = config;
            return module;
        }

        public override void DeclareData(WorldLayout layout)
        {
            if (m_Config == null)
                m_Config = SnakeConfig.CreateDefault();
            var runtime = m_Config.Bake();
            var cap = runtime.Capacity;

            layout.Table(SnakeKeys.Snake, cap.Snakes)
                .Column(SnakeKeys.Head).Column(SnakeKeys.PrevHead).Column(SnakeKeys.Heading)
                .Column(SnakeKeys.Speed).Column(SnakeKeys.Mass).Column(SnakeKeys.Radius).Column(SnakeKeys.Length)
                .Column(SnakeKeys.Trail).Column(SnakeKeys.PrevArc).Column(SnakeKeys.Bounds)
                .Column(SnakeKeys.Info).Column(SnakeKeys.Control).Column(SnakeKeys.Contact).Column(SnakeKeys.AI)
                .Column(SnakeKeys.Buffs).Column(SnakeKeys.Stats);
            layout.Table(SnakeKeys.Food, cap.Food).TrackChangedRows()
                .Column(SnakeKeys.FoodPosition).Column(SnakeKeys.FoodInfo);
            layout.Table(SnakeKeys.Prop, cap.Props)
                .Column(SnakeKeys.PropPosition).Column(SnakeKeys.PropInfo);
            layout.Table(SnakeKeys.Projectile, cap.Projectiles)
                .Column(SnakeKeys.ProjectilePosition).Column(SnakeKeys.ProjectileState);
            layout.DestroyQueueCapacity = math.max(layout.DestroyQueueCapacity, cap.EventQueue);

            layout.Resource(SnakeKeys.Config, runtime);
            layout.Resource(SnakeKeys.Game, new SnakeGameState());
            layout.Resource(SnakeKeys.Quality, new SnakeQuality());
            layout.Resource(SnakeKeys.Populations, new RegionPopulations(runtime));
            layout.Resource(SnakeKeys.Bodies, new BodyStore(cap.Snakes * cap.AverageTrailPoints, cap.MaxTrailPoints));

            var cells = new int2(cap.GridCells);
            float bodyCell = cap.BodyGridCellSize > 0f ? cap.BodyGridCellSize : cap.GridCellSize;
            var bodyCells = (int2)math.ceil((float2)cells * cap.GridCellSize / bodyCell);
            layout.Resource(SnakeKeys.BodyGrid, new SpatialGrid(bodyCells, bodyCell, cap.BodyGridEntries, cap.LargeBodyRadius));
            var itemCells = (int2)math.ceil((float2)cells * cap.GridCellSize / cap.ItemGridCellSize);
            layout.Resource(SnakeKeys.ItemGrid, new CellListGrid(itemCells, cap.ItemGridCellSize, cap.Food + cap.Props, cap.Food + cap.Props));
            // Coarse grid over the largest region: one cell per chunk.
            float2 largest = 0f;
            for (int i = 0; i < runtime.Regions.Length; i++) largest = math.max(largest, runtime.Regions[i].Size);
            var coarse = (int2)math.ceil(largest / cap.ChunkSize);
            layout.Resource(SnakeKeys.HeadGrid, new SpatialGrid(coarse, cap.ChunkSize, cap.Snakes));

            layout.Resource(SnakeKeys.Deaths, new EventQueue<DeathEvent>(cap.Snakes));
            layout.Resource(SnakeKeys.Eats, new EventQueue<EatCandidate>(cap.EventQueue * 4));
            layout.Resource(SnakeKeys.Hits, new EventQueue<ProjectileHit>(cap.Projectiles));
            layout.Resource(SnakeKeys.FoodSpawns, new EventQueue<FoodSpawnRequest>(cap.EventQueue));
            layout.Resource(SnakeKeys.ProjectileSpawns, new EventQueue<ProjectileSpawnRequest>(cap.Projectiles));
            layout.Resource(SnakeKeys.Feedback, new EventQueue<FeedbackEvent>(cap.EventQueue));
            layout.Resource(SnakeKeys.RemovedItems, new EventQueue<int2>(cap.EventQueue));
            layout.Resource(SnakeKeys.Signal, new Signals());
            layout.Resource(SnakeKeys.Replay, new ReplayBuffer(30 * 60 * 20));
        }

        public override void RegisterSystems(SystemRegistry registry)
        {
            registry
                .Add(new ReplaySystem())
                .Add(new LifecycleSystem())
                .Add(new RegionSystem())
                .Add(new PopulationSystem())
                .Add(new WindowSystem())
                .Add(new ItemSpawnSystem())
                .Add(new StatsSystem())
                .Add(new PlayerControlSystem())
                .Add(new AISystem())
                .Add(new MovementSystem())
                .Add(new BodySystem())
                .Add(new BodyGridSystem())
                .Add(new ItemGridSystem())
                .Add(new HeadGridSystem())
                .Add(new ContactSystem())
                .Add(new ResolveSystem());
        }
    }
}
