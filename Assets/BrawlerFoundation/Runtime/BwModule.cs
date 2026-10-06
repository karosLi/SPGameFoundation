using BrawlerFoundation.Systems;
using SPF.Runtime.Composition;
using SPF.Runtime.World;
using Unity.Mathematics;
using UnityEngine;

namespace BrawlerFoundation
{
    /// <summary>The brawler as one module: a fighter table (skeletal animators in a column), the shared rig, flow, fighters, combat.</summary>
    public sealed class BwModule : GameplayModuleAsset
    {
        bool m_SharedCombat;
        bool m_MobileSkills;
        BwSharedCombatConfig m_CombatConfig;

        public static BwModule Create()
        {
            var module = CreateInstance<BwModule>();
            module.hideFlags = HideFlags.DontSave;
            return module;
        }

        public static BwModule CreateSharedCombat(BwSharedCombatConfig config)
        {
            config.Validate();
            var module = Create();
            module.m_SharedCombat = true;
            module.m_CombatConfig = config;
            return module;
        }

        public static BwModule CreateMobileCombat()
        {
            var module = CreateSharedCombat(BwSharedCombatConfig.Default);
            module.m_MobileSkills = true;
            return module;
        }

        public override void DeclareData(WorldLayout layout)
        {
            layout.Table(BwKeys.Fighter, m_SharedCombat ? m_CombatConfig.Fighters : 64).LevelScoped()
                .Column(BwKeys.Position).Column(BwKeys.Prev).Column(BwKeys.Info).Column(BwKeys.Anim);
            layout.Resource(BwKeys.Rig, new BwRig());
            layout.Resource(BwKeys.Game, new BwGameState());
            layout.Resource(BwKeys.Feedback, new EventQueue<BwFeedback>(128, saved: false));
            if (m_MobileSkills) layout.Resource(BwMobileSkills.Key, BwMobileSkills.Create(), levelScoped: true);
            if (m_SharedCombat) layout.Resource(BwKeys.SharedCombat, new BwSharedCombatState(m_CombatConfig), levelScoped: true);
        }

        public override void RegisterSystems(SystemRegistry registry)
        {
            registry.Add(new FlowSystem()).Add(new FighterSystem()).Add(new CombatSystem());
            if (m_MobileSkills) registry.Add(new MobileSkillSystem());
        }
    }

    public static class BwMode
    {
        public static ModeDefinition CreateMobileCombat(out GameplayModuleAsset module)
        {
            module = BwModule.CreateMobileCombat();
            var settings = SessionSettings.Default;
            settings.TickRate = 60; settings.MaxTicksPerFrame = 4;
            return ModeDefinition.Create(new[] { module }, settings);
        }

        public static ModeDefinition CreateSharedCombat(BwSharedCombatConfig config, out GameplayModuleAsset module)
        {
            module = BwModule.CreateSharedCombat(config);
            var settings = SessionSettings.Default;
            settings.TickRate = 60;
            settings.MaxTicksPerFrame = 4;
            return ModeDefinition.Create(new[] { module }, settings);
        }

        public static ModeDefinition Create(out GameplayModuleAsset module)
        {
            module = BwModule.Create();
            var settings = SessionSettings.Default;
            settings.TickRate = 60;
            settings.MaxTicksPerFrame = 4;
            return ModeDefinition.Create(new[] { module }, settings);
        }
    }

    /// <summary>Spawning for tests and tools.</summary>
    public static class BwSpawner
    {
        public static void Spawn(SimWorld world, byte team, float2 position, float facing, byte variant) =>
            FlowSystem.Spawn(world, team, position, facing, variant);
    }
}
