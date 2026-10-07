using BrawlerFoundation.Systems;
using SPF.Runtime.Composition;
using SPF.Runtime.World;
using Unity.Mathematics;
using UnityEngine;

namespace BrawlerFoundation
{
    /// <summary>The brawler as one module: a fighter table (skeletal animators in a column), the shared rig, flow, fighters, combat.</summary>
    public sealed class BwModule : GameplayModuleAsset, ICompositionManifestProvider
    {
        bool m_SharedCombat;
        bool m_MobileSkills;
        bool m_BeltScroller;
        bool m_Weapons;
        bool m_ComposedAbilities;
        bool m_DamageNumbers;
        SPF.L2.Combat.CriticalDamageRule m_CriticalRule;
        int m_DamageJournalCapacity;
        BwComposedAbilityConfig m_AbilityConfig;
        SPF.L2.Weapons.WeaponProfile[] m_WeaponProfiles;
        BwBeltConfig m_BeltConfig;
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

        public static BwModule CreateBeltScroller(BwBeltConfig config)
        {
            config.Validate();
            var module = CreateSharedCombat(new BwSharedCombatConfig { Fighters = config.Fighters, TargetsPerAttack = config.TargetsPerAttack });
            module.m_BeltScroller = module.m_MobileSkills = true; module.m_BeltConfig = config;
            return module;
        }

        public static BwModule CreateWeaponBelt(BwBeltConfig config, SPF.L2.Weapons.WeaponProfile[] profiles = null)
        { var module = CreateBeltScroller(config); module.m_Weapons = true; module.m_WeaponProfiles = profiles; return module; }

        public static BwModule CreateComposedAbilityBelt(BwBeltConfig config, BwComposedAbilityConfig abilities)
        {
            abilities.Validate(); var module = CreateWeaponBelt(config);
            module.m_ComposedAbilities = true; module.m_AbilityConfig = abilities; return module;
        }

        public static BwModule CreateDamageNumbersBelt(BwBeltConfig config, BwComposedAbilityConfig abilities,
            SPF.L2.Combat.CriticalDamageRule criticalRule, int journalCapacity = AppliedDamageJournal.DefaultCapacity)
        {
            criticalRule.Validate();
            if (journalCapacity < 2 || journalCapacity > AppliedDamageJournal.DefaultCapacity) throw new System.ArgumentOutOfRangeException(nameof(journalCapacity));
            var module = CreateComposedAbilityBelt(config, abilities);
            module.m_DamageNumbers = true; module.m_CriticalRule = criticalRule; module.m_DamageJournalCapacity = journalCapacity;
            return module;
        }

        /// <summary>Cold, partial metadata for the opt-in weapon belt: its fighter table and
        /// weapon/pose/skill resource slice only. Other resources and complete save coverage are not
        /// claimed; SnapshotHook checks the resource type's interface, not a full save contract.</summary>
        public ModuleManifest DescribeComposition()
        {
            if (!m_Weapons) return null;
            var data = new System.Collections.Generic.List<ModuleDataDeclaration>
            {
                // The belt's grids and scratch buffers use the same fixed fighter budget.
                ModuleDataDeclaration.Table(BwKeys.Fighter, 1, m_CombatConfig.Fighters, "BwBeltConfig.Fighters",
                    levelScoped: true, maxCapacity: m_CombatConfig.Fighters),
                ModuleDataDeclaration.Resource(BwWeapons.PoseKey, 1, levelScoped: true,
                    save: ResourceSaveRequirement.SnapshotHook, capacity: 1, capacitySource: "One ActionPoseClock per weapon belt"),
                // Primary bound only; targets-per-attack/cues/profile contents remain runtime contracts.
                ModuleDataDeclaration.Resource(BwWeapons.Key, 1, levelScoped: true,
                    save: ResourceSaveRequirement.SnapshotHook, capacity: 32, capacitySource: "WeaponRuntime default projectile capacity"),
                ModuleDataDeclaration.Resource(BwMobileSkills.Key, 1, levelScoped: true,
                    save: ResourceSaveRequirement.SnapshotHook, capacity: 4, capacitySource: "BwBeltRules.CreateSkills authored slots")
            };
            if (m_ComposedAbilities) data.Add(ModuleDataDeclaration.Resource(BwComposedAbilityState.Key, 1, levelScoped: true,
                save: ResourceSaveRequirement.SnapshotHook, capacity: 1, capacitySource: "One bounded player heal-credit and pending action owner"));
            if (m_DamageNumbers)
            {
                data.Add(ModuleDataDeclaration.Resource(AppliedDamageJournal.Key, 1, levelScoped: true,
                    save: ResourceSaveRequirement.SnapshotHook, capacity: m_DamageJournalCapacity, capacitySource: "Authored settled damage ring"));
                data.Add(ModuleDataDeclaration.Resource(SPF.L2.Combat.CriticalDamageState.Key, 1, levelScoped: true,
                    save: ResourceSaveRequirement.SnapshotHook, capacity: 1, capacitySource: "One deterministic outgoing accepted-hit cadence"));
                return new ModuleManifest("brawler.damage-numbers", 1, data: data.ToArray());
            }
            return new ModuleManifest(m_ComposedAbilities ? "brawler.composed-ability-belt" : "brawler.weapon-belt", 1, data: data.ToArray());
        }

        public override void DeclareData(WorldLayout layout)
        {
            var fighters = layout.Table(BwKeys.Fighter, m_SharedCombat ? m_CombatConfig.Fighters : 64).LevelScoped()
                .Column(BwKeys.Position).Column(BwKeys.Prev).Column(BwKeys.Info).Column(BwKeys.Anim);
            if (m_BeltScroller)
            {
                fighters.Column(BwBeltKeys.Ground).Column(BwBeltKeys.PreviousGround).Column(BwBeltKeys.Motion);
                layout.Resource(BwBeltKeys.State, new BwBeltState(m_BeltConfig), levelScoped: true);
            }
            if (m_BeltScroller) layout.Resource(BwWeapons.PoseKey, new SPF.L2.Skills.ActionPoseClock(), levelScoped: true);
            if (m_Weapons) layout.Resource(BwWeapons.Key, new SPF.L2.Weapons.WeaponRuntime(m_WeaponProfiles ?? SPF.L2.Weapons.WeaponProfiles.CreateDefaults(60), 60, targetsPerAttack: m_BeltConfig.TargetsPerAttack), levelScoped: true);
            if (m_ComposedAbilities) layout.Resource(BwComposedAbilityState.Key, new BwComposedAbilityState(m_AbilityConfig), levelScoped: true);
            layout.Resource(BwKeys.Rig, new BwRig());
            layout.Resource(BwKeys.Game, new BwGameState());
            layout.Resource(BwKeys.Feedback, new EventQueue<BwFeedback>(128, saved: false));
            if (m_MobileSkills) layout.Resource(BwMobileSkills.Key, m_ComposedAbilities ? m_AbilityConfig.CreateSkills() : m_BeltScroller ? BwBeltRules.CreateSkills() : BwMobileSkills.Create(), levelScoped: true);
            if (m_SharedCombat) layout.Resource(BwKeys.SharedCombat, new BwSharedCombatState(m_CombatConfig), levelScoped: true);
            if (m_DamageNumbers)
            {
                layout.Resource(AppliedDamageJournal.Key, new AppliedDamageJournal(m_DamageJournalCapacity), levelScoped: true);
                layout.Resource(SPF.L2.Combat.CriticalDamageState.Key, new SPF.L2.Combat.CriticalDamageState(m_CriticalRule), levelScoped: true);
            }
        }

        public override void RegisterSystems(SystemRegistry registry)
        {
            if (m_BeltScroller)
            { registry.Add(new BeltFlowSystem()).Add(new BeltFighterSystem(m_ComposedAbilities)).Add(new BeltCombatSystem()); if (m_Weapons) registry.Add(new BeltWeaponSystem()); if (m_ComposedAbilities) registry.Add(new BeltComposedAbilitySystem()); return; }
            registry.Add(new FlowSystem()).Add(new FighterSystem()).Add(new CombatSystem());
            if (m_MobileSkills) registry.Add(new MobileSkillSystem());
        }
    }

    public static class BwMode
    {
        public static ModeDefinition CreateDamageNumbersBelt(BwBeltConfig config, BwComposedAbilityConfig abilities,
            SPF.L2.Combat.CriticalDamageRule criticalRule, out GameplayModuleAsset module, int journalCapacity = AppliedDamageJournal.DefaultCapacity)
        {
            module = BwModule.CreateDamageNumbersBelt(config, abilities, criticalRule, journalCapacity);
            var settings = SessionSettings.Default; settings.TickRate = 60; settings.MaxTicksPerFrame = 4;
            return ModeDefinition.Create(new[] { module }, settings);
        }

        public static ModeDefinition CreateComposedAbilityBelt(BwBeltConfig config, BwComposedAbilityConfig abilities, out GameplayModuleAsset module)
        {
            module = BwModule.CreateComposedAbilityBelt(config, abilities);
            var settings = SessionSettings.Default; settings.TickRate = 60; settings.MaxTicksPerFrame = 4;
            return ModeDefinition.Create(new[] { module }, settings);
        }

        public static ModeDefinition CreateWeaponBelt(BwBeltConfig config, out GameplayModuleAsset module)
        {
            module = BwModule.CreateWeaponBelt(config);
            var settings = SessionSettings.Default; settings.TickRate = 60; settings.MaxTicksPerFrame = 4;
            return ModeDefinition.Create(new[] { module }, settings);
        }

        public static ModeDefinition CreateBeltScroller(BwBeltConfig config, out GameplayModuleAsset module)
        {
            module = BwModule.CreateBeltScroller(config);
            var settings = SessionSettings.Default; settings.TickRate = 60; settings.MaxTicksPerFrame = 4;
            return ModeDefinition.Create(new[] { module }, settings);
        }

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
        public static void Spawn(SimWorld world, byte team, float2 position, float facing, byte variant)
        {
            if (world.HasResource(BwBeltKeys.State)) BeltFlowSystem.Spawn(world, team, position, facing, variant);
            else FlowSystem.Spawn(world, team, position, facing, variant);
        }
    }
}
