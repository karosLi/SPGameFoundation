using SPF.Contracts;
using SPF.Runtime.World;
using SPF.L1.Body;
using SPF.L1.Spatial;
using SPF.L2.Buffs;
using Unity.Mathematics;

namespace SnakeFoundation
{
    public static class SnakeKeys
    {
        public static readonly TableKey Snake = new TableKey("Snake");
        public static readonly ColumnKey<float2> Head = new ColumnKey<float2>(Snake, "Head");
        public static readonly ColumnKey<float2> PrevHead = new ColumnKey<float2>(Snake, "PrevHead");
        public static readonly ColumnKey<float2> Heading = new ColumnKey<float2>(Snake, "Heading");
        public static readonly ColumnKey<float> Speed = new ColumnKey<float>(Snake, "Speed");
        public static readonly ColumnKey<float> Mass = new ColumnKey<float>(Snake, "Mass");
        public static readonly ColumnKey<float> Radius = new ColumnKey<float>(Snake, "Radius");
        public static readonly ColumnKey<float> Length = new ColumnKey<float>(Snake, "Length");
        public static readonly ColumnKey<TrailState> Trail = new ColumnKey<TrailState>(Snake, "Trail");
        public static readonly ColumnKey<float> PrevArc = new ColumnKey<float>(Snake, "PrevArc");
        public static readonly ColumnKey<float4> Bounds = new ColumnKey<float4>(Snake, "Bounds");
        public static readonly ColumnKey<SnakeInfo> Info = new ColumnKey<SnakeInfo>(Snake, "Info");
        public static readonly ColumnKey<SnakeControl> Control = new ColumnKey<SnakeControl>(Snake, "Control");
        public static readonly ColumnKey<SnakeContact> Contact = new ColumnKey<SnakeContact>(Snake, "Contact");
        public static readonly ColumnKey<AIState> AI = new ColumnKey<AIState>(Snake, "AI");
        public static readonly ColumnKey<BuffSet> Buffs = new ColumnKey<BuffSet>(Snake, "Buffs");
        public static readonly ColumnKey<EffectiveStats> Stats = new ColumnKey<EffectiveStats>(Snake, "Stats");

        public static readonly TableKey Food = new TableKey("Food");
        public static readonly ColumnKey<float2> FoodPosition = new ColumnKey<float2>(Food, "Position");
        public static readonly ColumnKey<FoodInfo> FoodInfo = new ColumnKey<FoodInfo>(Food, "Info");

        public static readonly TableKey Prop = new TableKey("Prop");
        public static readonly ColumnKey<float2> PropPosition = new ColumnKey<float2>(Prop, "Position");
        public static readonly ColumnKey<PropInfo> PropInfo = new ColumnKey<PropInfo>(Prop, "Info");

        public static readonly TableKey Projectile = new TableKey("Projectile");
        public static readonly ColumnKey<float2> ProjectilePosition = new ColumnKey<float2>(Projectile, "Position");
        public static readonly ColumnKey<ProjectileState> ProjectileState = new ColumnKey<ProjectileState>(Projectile, "State");

        public static readonly ResourceKey<BodyStore> Bodies = new ResourceKey<BodyStore>("Snake.Bodies");
        public static readonly ResourceKey<SpatialGrid> BodyGrid = new ResourceKey<SpatialGrid>("Snake.BodyGrid");
        public static readonly ResourceKey<SpatialGrid> ItemGrid = new ResourceKey<SpatialGrid>("Snake.ItemGrid");
        public static readonly ResourceKey<SpatialGrid> HeadGrid = new ResourceKey<SpatialGrid>("Snake.HeadGrid");
        public static readonly ResourceKey<SnakeRuntimeConfig> Config = new ResourceKey<SnakeRuntimeConfig>("Snake.Config");
        public static readonly ResourceKey<SnakeGameState> Game = new ResourceKey<SnakeGameState>("Snake.Game");
        public static readonly ResourceKey<EventQueue<DeathEvent>> Deaths = new ResourceKey<EventQueue<DeathEvent>>("Snake.Deaths");
        public static readonly ResourceKey<EventQueue<EatCandidate>> Eats = new ResourceKey<EventQueue<EatCandidate>>("Snake.Eats");
        public static readonly ResourceKey<EventQueue<ProjectileHit>> Hits = new ResourceKey<EventQueue<ProjectileHit>>("Snake.Hits");
        public static readonly ResourceKey<EventQueue<FoodSpawnRequest>> FoodSpawns = new ResourceKey<EventQueue<FoodSpawnRequest>>("Snake.FoodSpawns");
        public static readonly ResourceKey<EventQueue<ProjectileSpawnRequest>> ProjectileSpawns = new ResourceKey<EventQueue<ProjectileSpawnRequest>>("Snake.ProjectileSpawns");
        public static readonly ResourceKey<EventQueue<FeedbackEvent>> Feedback = new ResourceKey<EventQueue<FeedbackEvent>>("Snake.Feedback");
        public static readonly ResourceKey<RegionPopulations> Populations = new ResourceKey<RegionPopulations>("Snake.Populations");
        /// <summary>Chunk bookkeeping for items removed by jobs: x = 0 food / 1 prop, y = chunk.</summary>
        public static readonly ResourceKey<EventQueue<int2>> RemovedItems = new ResourceKey<EventQueue<int2>>("Snake.RemovedItems");
        public static readonly ResourceKey<SnakeQuality> Quality = new ResourceKey<SnakeQuality>("Snake.Quality");
        public static readonly ResourceKey<Signals> Signal = new ResourceKey<Signals>("Snake.Signals");
    }
}
