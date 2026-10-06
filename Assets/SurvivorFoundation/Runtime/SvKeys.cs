using SPF.Contracts;
using SPF.L1.Spatial;
using SPF.Runtime.World;
using Unity.Mathematics;

namespace SurvivorFoundation
{
    public static class SvKeys
    {
        // Enemies: handle table (deaths go through the destroy queue), periodically sorted by Morton cell.
        public static readonly TableKey Enemy = new TableKey("Sv.Enemy");
        public static readonly ColumnKey<float2> Position = new ColumnKey<float2>(Enemy, "Position");
        public static readonly ColumnKey<float2> PrevPosition = new ColumnKey<float2>(Enemy, "PrevPosition");
        public static readonly ColumnKey<EnemyInfo> Info = new ColumnKey<EnemyInfo>(Enemy, "Info");

        // Bullets and gems: pooled (handle-free) tables, compacted every tick.
        public static readonly TableKey Bullet = new TableKey("Sv.Bullet");
        public static readonly ColumnKey<float2> BulletPosition = new ColumnKey<float2>(Bullet, "Position");
        public static readonly ColumnKey<BulletInfo> BulletInfo = new ColumnKey<BulletInfo>(Bullet, "Info");

        public static readonly TableKey Gem = new TableKey("Sv.Gem");
        public static readonly ColumnKey<float2> GemPosition = new ColumnKey<float2>(Gem, "Position");
        public static readonly ColumnKey<GemInfo> GemInfo = new ColumnKey<GemInfo>(Gem, "Info");

        public static readonly ResourceKey<SvCrossedBladeState> CrossedBlades = new ResourceKey<SvCrossedBladeState>("Sv.CrossedBlades.V1");
        public static readonly ResourceKey<SvRuntime> Config = new ResourceKey<SvRuntime>("Sv.Config");
        public static readonly ResourceKey<SvGameState> Game = new ResourceKey<SvGameState>("Sv.Game");
        public static readonly ResourceKey<SpatialGrid> EnemyGrid = new ResourceKey<SpatialGrid>("Sv.EnemyGrid");
        public static readonly ResourceKey<EventQueue<SvHit>> Hits = new ResourceKey<EventQueue<SvHit>>("Sv.Hits");
        public static readonly ResourceKey<EventQueue<SvDeath>> Deaths = new ResourceKey<EventQueue<SvDeath>>("Sv.Deaths");
        public static readonly ResourceKey<EventQueue<BulletSpawn>> BulletSpawns = new ResourceKey<EventQueue<BulletSpawn>>("Sv.BulletSpawns");
        public static readonly ResourceKey<EventQueue<float>> HeroDamage = new ResourceKey<EventQueue<float>>("Sv.HeroDamage");
        public static readonly ResourceKey<EventQueue<int>> Collected = new ResourceKey<EventQueue<int>>("Sv.Collected");
        public static readonly ResourceKey<EventQueue<SvFeedback>> Feedback = new ResourceKey<EventQueue<SvFeedback>>("Sv.Feedback");
    }
}
