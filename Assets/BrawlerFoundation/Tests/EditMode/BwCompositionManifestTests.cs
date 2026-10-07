using System;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Runtime.Composition;
using SPF.Runtime.World;

namespace BrawlerFoundation.Tests
{
    public class BwCompositionManifestTests
    {
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void ExistingNonWeaponModesRemainOptedOut(int mode)
        {
            var module = mode == 0 ? BwModule.Create() : mode == 1 ? BwModule.CreateSharedCombat(BwSharedCombatConfig.Default) :
                mode == 2 ? BwModule.CreateMobileCombat() : BwModule.CreateBeltScroller(BwBeltConfig.Default);
            try
            {
                Assert.IsNull(Describe(module));
                var report = CompositionPreflight.Validate(new IGameplayModule[] { module }, Settings());
                Assert.IsTrue(report.IsValid); Assert.AreEqual(1, report.LegacyModuleCount);
                Assert.AreEqual(0, report.Manifests.Count); Assert.AreEqual(0, report.Data.Count);
            }
            finally { UnityEngine.Object.DestroyImmediate(module); }
        }

        [Test]
        public void WeaponManifestReportsOnlyActualFighterTableAndWeaponResourceSlice()
        {
            var config = BwBeltConfig.Default; config.Fighters = 24; config.TargetsPerAttack = 12;
            var module = BwModule.CreateWeaponBelt(config);
            try
            {
                var manifest = Describe(module);
                Assert.AreEqual("brawler.weapon-belt", manifest.StableId); Assert.AreEqual(1, manifest.SchemaVersion);
                Assert.AreEqual(4, manifest.Data.Count, "This partial manifest does not claim complete Brawler save coverage.");
                var fighters = manifest.Data[0];
                Assert.AreSame(BwKeys.Fighter, fighters.Key); Assert.IsTrue(fighters.IsTable); Assert.IsTrue(fighters.IsOwner);
                Assert.IsTrue(fighters.LevelScoped); Assert.IsFalse(fighters.Pooled); Assert.AreEqual(1, fighters.SchemaVersion);
                Assert.AreEqual(config.Fighters, fighters.Capacity); Assert.AreEqual(config.Fighters, fighters.MaxCapacity);
                Assert.IsNotEmpty(fighters.CapacitySource); Assert.AreEqual(ResourceSaveRequirement.Unspecified, fighters.SaveRequirement);
                Resource(manifest.Data[1], BwWeapons.PoseKey, 1);
                Resource(manifest.Data[2], BwWeapons.Key, 32);
                Resource(manifest.Data[3], BwMobileSkills.Key, 4);
                var report = CompositionPreflight.Validate(new IGameplayModule[] { module }, Settings());
                Assert.IsTrue(report.IsValid); Assert.AreEqual(0, report.LegacyModuleCount); Assert.AreEqual(4, report.Data.Count);
                foreach (var data in report.Data) Assert.AreEqual(manifest.StableId, data.OwnerModuleId);
                using var world = WorldComposer.BuildWorld(new IGameplayModule[] { module }, Settings(), 11);
                Assert.AreEqual(report.Data[0].Capacity, world.Table(BwKeys.Fighter).Capacity);
                Assert.IsNotNull(world.Resource(BwWeapons.PoseKey));
                Assert.AreEqual(report.Data[2].Capacity, world.Resource(BwWeapons.Key).Projectiles.Length);
                Assert.AreEqual(report.Data[3].Capacity, world.Resource(BwMobileSkills.Key).Count);
                Assert.AreEqual(config.TargetsPerAttack, world.Resource(BwWeapons.Key).HistoryPerAttack);
                Assert.IsTrue(world.HasResource(BwBeltKeys.State), "Unlisted resources remain installed by the unchanged declaration.");
            }
            finally { UnityEngine.Object.DestroyImmediate(module); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ConflictingOptionalManifestFailsBeforeEitherDeclaration(bool overBudget)
        {
            var config = BwBeltConfig.Default; var module = BwModule.CreateWeaponBelt(config);
            try
            {
                var actual = new CountingModule(module);
                var conflict = new CountingModule(new ModuleManifest("brawler.conflict", 1, data: new[] {
                    overBudget ? ModuleDataDeclaration.Table(BwKeys.Fighter, 1, config.Fighters + 1,
                        "extension request", owner: false, levelScoped: true) :
                    ModuleDataDeclaration.Resource(BwWeapons.Key, 1, levelScoped: true) }));
                var modules = new IGameplayModule[] { actual, conflict };
                Assert.IsFalse(CompositionPreflight.Validate(modules, Settings()).IsValid);
                var error = Assert.Throws<ArgumentException>(() => { using var world = WorldComposer.BuildWorld(modules, Settings(), 11); });
                Assert.That(error.Message, Does.Contain(overBudget ? BwKeys.Fighter.Name : BwWeapons.Key.Name));
                Assert.AreEqual(0, actual.Declarations); Assert.AreEqual(0, conflict.Declarations);
            }
            finally { UnityEngine.Object.DestroyImmediate(module); }
        }

        static SessionSettings Settings() { var settings = SessionSettings.Default; settings.TickRate = 60; return settings; }
        static ModuleManifest Describe(BwModule module)
        {
            Assert.IsInstanceOf<ICompositionManifestProvider>(module);
            return ((ICompositionManifestProvider)(object)module).DescribeComposition();
        }
        static void Resource(ModuleDataDeclaration data, AccessKey key, int capacity)
        {
            Assert.AreSame(key, data.Key); Assert.IsFalse(data.IsTable); Assert.IsTrue(data.IsOwner); Assert.IsTrue(data.LevelScoped);
            Assert.AreEqual(1, data.SchemaVersion); Assert.AreEqual(capacity, data.Capacity); Assert.IsNotEmpty(data.CapacitySource);
            Assert.AreEqual(ResourceSaveRequirement.SnapshotHook, data.SaveRequirement);
        }
        sealed class CountingModule : IGameplayModule, ICompositionManifestProvider
        {
            readonly BwModule m_Module; readonly ModuleManifest m_Manifest;
            public int Declarations; public string Id => m_Module == null ? "conflict" : m_Module.Id;
            public CountingModule(BwModule module) { m_Module = module; }
            public CountingModule(ModuleManifest manifest) { m_Manifest = manifest; }
            public ModuleManifest DescribeComposition() => m_Module == null ? m_Manifest : Describe(m_Module);
            public void DeclareData(WorldLayout layout) { Declarations++; if (m_Module != null) m_Module.DeclareData(layout); }
            public void RegisterSystems(SystemRegistry registry) { if (m_Module != null) m_Module.RegisterSystems(registry); }
        }
    }
}
