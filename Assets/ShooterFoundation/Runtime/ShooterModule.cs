using SPF.L1.Spatial;
using SPF.Runtime.Composition;
using SPF.Runtime.World;
using Unity.Mathematics;
using UnityEngine;

namespace ShooterFoundation
{
    public sealed class ShooterModule : GameplayModuleAsset
    {
        ShooterSettings m_Settings;
        public static ShooterModule Create(ShooterSettings settings)
        {
            settings.Validate(); var m = CreateInstance<ShooterModule>(); m.hideFlags = HideFlags.DontSave; m.m_Settings = settings; return m;
        }
        public override void DeclareData(WorldLayout l)
        {
            var s = m_Settings;
            l.Table(ShooterKeys.Enemy, s.EnemyCapacity).Pooled().LevelScoped().Column(ShooterKeys.EnemyPosition).Column(ShooterKeys.EnemyPrevious).Column(ShooterKeys.Enemies);
            l.Table(ShooterKeys.Bullet, s.BulletCapacity).Pooled().LevelScoped().Column(ShooterKeys.BulletPosition).Column(ShooterKeys.BulletPrevious).Column(ShooterKeys.Bullets);
            l.Table(ShooterKeys.Pickup, s.PickupCapacity).Pooled().LevelScoped().Column(ShooterKeys.PickupPosition).Column(ShooterKeys.Pickups);
            l.Resource(ShooterKeys.State, new ShooterState()); l.Resource(ShooterKeys.Rules, new ShooterRules(s));
            var grid = new SpatialGrid((int2)math.ceil((s.ArenaHalf * 2f + 6f) / s.GridCell), s.GridCell, s.EnemyCapacity) { Origin = -s.ArenaHalf - 3f };
            l.Resource(ShooterKeys.Grid, grid, levelScoped: true);
            l.Resource(ShooterKeys.Scratch, new ShooterScratch(s.BulletCapacity));
            l.Resource(ShooterKeys.Feedback, new EventQueue<ShooterFeedback>(1024, saved: false), levelScoped: true);
        }
        public override void RegisterSystems(SystemRegistry r) => r.Add(new ShooterTickSystem());
    }
    public static class ShooterMode
    {
        public static ModeDefinition Create(ShooterConfig config, out GameplayModuleAsset module)
        {
            module = ShooterModule.Create(config.Settings); return ModeDefinition.Create(new[] { module }, SessionSettings.Default);
        }
    }
    /// <summary>Bounded spawn helpers for scripted encounters and tests. Call only between ticks / ApplyCommands.</summary>
    public static class ShooterSpawner
    {
        public static int Enemy(SimWorld w, float2 position, byte kind = 0, float hp = -1f, float speed = -1f)
        {
            var state = w.Resource(ShooterKeys.State); var s = w.Resource(ShooterKeys.Rules).Settings;
            int row = w.Spawn(ShooterKeys.Enemy); if (row < 0) { state.Run.DroppedSpawns++; return -1; }
            float health = hp > 0f ? hp : s.EnemyHp * (1f + state.Run.Wave * 0.14f) * (kind == 2 ? 3f : 1f);
            var positions = w.Column(ShooterKeys.EnemyPosition); var previous = w.Column(ShooterKeys.EnemyPrevious); var infos = w.Column(ShooterKeys.Enemies);
            positions[row] = previous[row] = position;
            infos[row] = new ShooterEnemy { Id = ++state.Run.NextEnemyId, Kind = kind, Hp = health, MaxHp = health,
                Radius = kind == 2 ? 0.60f : 0.38f, Speed = speed >= 0f ? speed : s.EnemySpeed, BaseX = position.x,
                FireTimer = s.EnemyFireInterval + (state.Run.NextEnemyId % 4) * 0.2f };
            return row;
        }
        public static int Bullet(SimWorld w, float2 position, float2 velocity, bool hostile = false, float damage = 12f, float radius = 0.10f)
        {
            int row = w.Spawn(ShooterKeys.Bullet); if (row < 0) { w.Resource(ShooterKeys.State).Run.DroppedSpawns++; return -1; }
            var positions = w.Column(ShooterKeys.BulletPosition); var previous = w.Column(ShooterKeys.BulletPrevious); var infos = w.Column(ShooterKeys.Bullets);
            positions[row] = previous[row] = position;
            infos[row] = new ShooterBullet { Velocity = velocity, Hostile = hostile, Damage = damage, Radius = radius, Life = 5f }; return row;
        }
        public static int Pickup(SimWorld w, float2 position, bool heal)
        {
            int row = w.Spawn(ShooterKeys.Pickup); if (row < 0) { w.Resource(ShooterKeys.State).Run.DroppedSpawns++; return -1; }
            var positions = w.Column(ShooterKeys.PickupPosition); var infos = w.Column(ShooterKeys.Pickups);
            positions[row] = position; infos[row] = new ShooterPickup { Heal = heal, Life = 10f }; return row;
        }
    }
}
