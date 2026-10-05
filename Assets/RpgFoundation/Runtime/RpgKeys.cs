using SPF.Contracts;
using SPF.L1.Navigation;
using SPF.L1.Spatial;
using SPF.L2.Combat;
using SPF.L2.Stats;
using SPF.Runtime.World;
using Unity.Mathematics;

namespace RpgFoundation
{
    public static class RpgKeys
    {
        public static readonly TableKey Actor = new TableKey("Rpg.Actor");
        public static readonly ColumnKey<float2> Position = new ColumnKey<float2>(Actor, "Position");
        public static readonly ColumnKey<float2> PrevPosition = new ColumnKey<float2>(Actor, "PrevPosition");
        public static readonly ColumnKey<float2> Facing = new ColumnKey<float2>(Actor, "Facing");
        public static readonly ColumnKey<float2> MoveIntent = new ColumnKey<float2>(Actor, "MoveIntent");
        public static readonly ColumnKey<ActorInfo> Info = new ColumnKey<ActorInfo>(Actor, "Info");
        public static readonly ColumnKey<Health> Health = new ColumnKey<Health>(Actor, "Health");
        public static readonly ColumnKey<Health> Mana = new ColumnKey<Health>(Actor, "Mana");
        public static readonly ColumnKey<Loadout> Loadout = new ColumnKey<Loadout>(Actor, "Loadout");
        public static readonly ColumnKey<StatBlock> BaseStats = new ColumnKey<StatBlock>(Actor, "BaseStats");
        public static readonly ColumnKey<StatBlock> Stats = new ColumnKey<StatBlock>(Actor, "Stats");
        public static readonly ColumnKey<ModifierSet> Mods = new ColumnKey<ModifierSet>(Actor, "Mods");
        public static readonly ColumnKey<CombatState> Combat = new ColumnKey<CombatState>(Actor, "Combat");
        public static readonly ColumnKey<Brain> Brain = new ColumnKey<Brain>(Actor, "Brain");
        public static readonly ColumnKey<StatusState> Status = new ColumnKey<StatusState>(Actor, "Status");

        public static readonly TableKey Projectile = new TableKey("Rpg.Projectile");
        public static readonly ColumnKey<float2> ProjectilePosition = new ColumnKey<float2>(Projectile, "Position");
        public static readonly ColumnKey<float2> ProjectilePrev = new ColumnKey<float2>(Projectile, "Prev");
        public static readonly ColumnKey<ProjectileInfo> ProjectileInfo = new ColumnKey<ProjectileInfo>(Projectile, "Info");

        public static readonly TableKey Prop = new TableKey("Rpg.Prop");
        public static readonly ColumnKey<float2> PropPosition = new ColumnKey<float2>(Prop, "Position");
        public static readonly ColumnKey<PropInfo> PropInfo = new ColumnKey<PropInfo>(Prop, "Info");

        public static readonly TableKey Item = new TableKey("Rpg.Item");
        public static readonly ColumnKey<float2> ItemPosition = new ColumnKey<float2>(Item, "Position");
        public static readonly ColumnKey<ItemInfo> ItemInfo = new ColumnKey<ItemInfo>(Item, "Info");

        public static readonly ResourceKey<RpgRuntimeConfig> Config = new ResourceKey<RpgRuntimeConfig>("Rpg.Config");
        public static readonly ResourceKey<RpgGameState> Game = new ResourceKey<RpgGameState>("Rpg.Game");
        public static readonly ResourceKey<TileMap> Map = new ResourceKey<TileMap>("Rpg.Map");
        public static readonly ResourceKey<FlowField> Flow = new ResourceKey<FlowField>("Rpg.Flow");
        public static readonly ResourceKey<SpatialGrid> ActorGrid = new ResourceKey<SpatialGrid>("Rpg.ActorGrid");
        public static readonly ResourceKey<EventQueue<HitEvent>> Hits = new ResourceKey<EventQueue<HitEvent>>("Rpg.Hits");
        public static readonly ResourceKey<EventQueue<ProjectileRequest>> ProjectileRequests = new ResourceKey<EventQueue<ProjectileRequest>>("Rpg.ProjectileRequests");
        public static readonly ResourceKey<EventQueue<DeathEvent>> Deaths = new ResourceKey<EventQueue<DeathEvent>>("Rpg.Deaths");
        public static readonly ResourceKey<EventQueue<FeedbackEvent>> Feedback = new ResourceKey<EventQueue<FeedbackEvent>>("Rpg.Feedback");
    }
}
