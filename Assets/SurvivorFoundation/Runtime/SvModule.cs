using SPF.L1.Spatial;
using SPF.Runtime.Composition;
using SPF.Runtime.World;
using SurvivorFoundation.Systems;
using Unity.Mathematics;
using UnityEngine;

namespace SurvivorFoundation
{
    /// <summary>The whole bullet-heaven game as one module.</summary>
    public sealed class SvModule : GameplayModuleAsset
    {
        [SerializeField] SvConfig m_Config;

        public static SvModule Create(SvConfig config)
        {
            var module = CreateInstance<SvModule>();
            module.hideFlags = HideFlags.DontSave;
            module.m_Config = config;
            return module;
        }

        SvConfig Config => m_Config != null ? m_Config : (m_Config = SvConfig.CreateDefault());

        public override void DeclareData(WorldLayout layout)
        {
            var cap = Config.Capacity;
            var s = Config.Settings;
            layout.Table(SvKeys.Enemy, cap.Enemies).LevelScoped().Column(SvKeys.Position).Column(SvKeys.PrevPosition).Column(SvKeys.Info);
            layout.Table(SvKeys.Bullet, cap.Bullets).LevelScoped().Pooled().Column(SvKeys.BulletPosition).Column(SvKeys.BulletInfo);
            layout.Table(SvKeys.Gem, cap.Gems).LevelScoped().Pooled().Column(SvKeys.GemPosition).Column(SvKeys.GemInfo);
            layout.Resource(SvKeys.Config, SvRuntime.Bake(Config));
            layout.Resource(SvKeys.Game, new SvGameState(s.Variant == SvVariant.GuardBeacon || s.AnnularSkill.Enabled));
            // A window around the hero (the arena is much bigger): 96 x 96 world units in 2-unit cells.
            int cells = (int)math.ceil(96f / s.GridCell);
            layout.Resource(SvKeys.EnemyGrid, new SpatialGrid(new int2(cells), s.GridCell, cap.Enemies), levelScoped: true);
            layout.Resource(SvKeys.Hits, new EventQueue<SvHit>(cap.Events * 2), levelScoped: true);
            layout.Resource(SvKeys.Deaths, new EventQueue<SvDeath>(cap.Enemies), levelScoped: true);
            layout.Resource(SvKeys.BulletSpawns, new EventQueue<BulletSpawn>(cap.Events), levelScoped: true);
            layout.Resource(SvKeys.HeroDamage, new EventQueue<float>(cap.Events), levelScoped: true);
            layout.Resource(SvKeys.Collected, new EventQueue<int>(cap.Gems), levelScoped: true);
            layout.Resource(SvKeys.Feedback, new EventQueue<SvFeedback>(cap.Events, saved: false));
            layout.DestroyQueueCapacity = cap.Enemies;
        }

        public override void RegisterSystems(SystemRegistry registry) => registry
            .Add(new FlowSystem())
            .Add(new RewardSystem())
            .Add(new SpawnSystem())
            .Add(new HeroSystem())
            .Add(new EnemySystem())
            .Add(new BulletSystem())
            .Add(new EnemyGridSystem())
            .Add(new CollideSystem())
            .Add(new AnnularSkillSystem())
            .Add(new ResolveSystem());
    }

    public static class SvMode
    {
        public static ModeDefinition Create(SvConfig config, out GameplayModuleAsset module)
        {
            module = SvModule.Create(config);
            return ModeDefinition.Create(new[] { module }, SessionSettings.Default);
        }
    }
}

namespace SurvivorFoundation
{
    /// <summary>Spawning helpers for tests, tools and scripted scenes.</summary>
    public static class SvSpawner
    {
        public static SPF.Contracts.EntityHandle SpawnEnemy(SPF.Runtime.World.SimWorld world, SvRuntime config, int kind, Unity.Mathematics.float2 position, bool elite = false) =>
            Systems.SpawnSystem.SpawnEnemy(world, config, kind, position, elite);
    }
}
