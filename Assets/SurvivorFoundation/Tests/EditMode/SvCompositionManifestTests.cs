using System;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Runtime.Composition;
using SPF.Runtime.World;
using UnityEngine;

namespace SurvivorFoundation.Tests
{
    public class SvCompositionManifestTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void NonWeaponConfigurationsRemainOptedOut(bool mobileSkills)
        {
            var config = SvConfig.CreateDefault(); config.MobileSkills = mobileSkills;
            var module = SvModule.Create(config);
            try
            {
                Assert.IsNull(Describe(module));
                var report = CompositionPreflight.Validate(new IGameplayModule[] { module }, SessionSettings.Default);
                Assert.IsTrue(report.IsValid); Assert.AreEqual(1, report.LegacyModuleCount);
                Assert.AreEqual(0, report.Manifests.Count); Assert.AreEqual(0, report.Data.Count);
            }
            finally { UnityEngine.Object.DestroyImmediate(module); UnityEngine.Object.DestroyImmediate(config); }
        }

        [Test]
        public void UnconfiguredModuleDescribesNoManifestOrLazyConfig()
        {
            var module = SvModule.Create(null);
            try
            {
                Assert.IsNull(Describe(module)); Assert.IsNull(Describe(module));
                var field = typeof(SvModule).GetField("m_Config", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.IsNull(field.GetValue(module), "Description must not invoke the lazy Config getter.");
            }
            finally { UnityEngine.Object.DestroyImmediate(module); }
        }

        [Test]
        public void WeaponManifestReportsOnlyActualTablesAndWeaponResourceSlice()
        {
            var config = CreateConfig(); var module = SvModule.Create(config);
            try
            {
                var manifest = Describe(module);
                Assert.AreEqual("survivor.weapon-combat", manifest.StableId); Assert.AreEqual(1, manifest.SchemaVersion);
                Assert.IsNull(config.WeaponProfiles, "Description must not bake default profiles.");
                Assert.IsFalse(config.MobileSkills, "WeaponCombat already installs skills without changing the setting.");
                Assert.AreEqual(6, manifest.Data.Count, "This is a partial manifest, not an audit of all Survivor resources or saves.");
                Table(manifest.Data[0], SvKeys.Enemy, 24, false);
                Table(manifest.Data[1], SvKeys.Bullet, 48, true);
                Table(manifest.Data[2], SvKeys.Gem, 32, true);
                Resource(manifest.Data[3], SvWeapons.PoseKey, 1);
                Resource(manifest.Data[4], SvWeapons.Key, 32);
                Resource(manifest.Data[5], SvMobileSkills.Key, 4);
                var report = CompositionPreflight.Validate(new IGameplayModule[] { module }, SessionSettings.Default);
                Assert.IsTrue(report.IsValid); Assert.AreEqual(0, report.LegacyModuleCount); Assert.AreEqual(6, report.Data.Count);
                foreach (var data in report.Data) Assert.AreEqual(manifest.StableId, data.OwnerModuleId);
                using var world = WorldComposer.BuildWorld(new IGameplayModule[] { module }, SessionSettings.Default, 11);
                Assert.AreEqual(report.Data[0].Capacity, world.Table(SvKeys.Enemy).Capacity);
                Assert.AreEqual(report.Data[1].Capacity, world.Table(SvKeys.Bullet).Capacity);
                Assert.AreEqual(report.Data[2].Capacity, world.Table(SvKeys.Gem).Capacity);
                Assert.IsNotNull(world.Resource(SvWeapons.PoseKey));
                Assert.AreEqual(report.Data[4].Capacity, world.Resource(SvWeapons.Key).Projectiles.Length);
                Assert.AreEqual(report.Data[5].Capacity, world.Resource(SvMobileSkills.Key).Count);
                Assert.IsTrue(world.HasResource(SvKeys.Config), "Unlisted resources remain installed by the unchanged declaration.");
            }
            finally { UnityEngine.Object.DestroyImmediate(module); UnityEngine.Object.DestroyImmediate(config); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ConflictingOptionalManifestFailsBeforeEitherDeclaration(bool overBudget)
        {
            var config = CreateConfig(); var module = SvModule.Create(config);
            try
            {
                var actual = new CountingModule(module);
                var conflict = new CountingModule(new ModuleManifest("survivor.conflict", 1, data: new[] {
                    overBudget ? ModuleDataDeclaration.Table(SvKeys.Enemy, 1, config.Capacity.Enemies + 1,
                        "extension request", owner: false, levelScoped: true) :
                    ModuleDataDeclaration.Resource(SvWeapons.Key, 1, levelScoped: true) }));
                var modules = new IGameplayModule[] { actual, conflict };
                var report = CompositionPreflight.Validate(modules, SessionSettings.Default);
                Assert.IsFalse(report.IsValid);
                var error = Assert.Throws<ArgumentException>(() => { using var world = WorldComposer.BuildWorld(modules, SessionSettings.Default, 11); });
                Assert.That(error.Message, Does.Contain(overBudget ? SvKeys.Enemy.Name : SvWeapons.Key.Name));
                Assert.AreEqual(0, actual.Declarations); Assert.AreEqual(0, conflict.Declarations);
            }
            finally { UnityEngine.Object.DestroyImmediate(module); UnityEngine.Object.DestroyImmediate(config); }
        }

        static SvConfig CreateConfig()
        {
            var config = SvConfig.CreateDefault(); config.WeaponCombat = true;
            config.Capacity.Enemies = 24; config.Capacity.Bullets = 48; config.Capacity.Gems = 32; config.Capacity.Events = 64;
            config.Settings.MaxEnemies = 24;
            return config;
        }
        static ModuleManifest Describe(SvModule module)
        {
            Assert.IsInstanceOf<ICompositionManifestProvider>(module);
            return ((ICompositionManifestProvider)(object)module).DescribeComposition();
        }
        static void Table(ModuleDataDeclaration data, TableKey key, int capacity, bool pooled)
        {
            Assert.AreSame(key, data.Key); Assert.IsTrue(data.IsTable); Assert.IsTrue(data.IsOwner); Assert.IsTrue(data.LevelScoped);
            Assert.AreEqual(1, data.SchemaVersion); Assert.AreEqual(capacity, data.Capacity); Assert.AreEqual(capacity, data.MaxCapacity);
            Assert.AreEqual(pooled, data.Pooled); Assert.IsNotEmpty(data.CapacitySource);
            Assert.AreEqual(ResourceSaveRequirement.Unspecified, data.SaveRequirement);
        }
        static void Resource(ModuleDataDeclaration data, AccessKey key, int capacity)
        {
            Assert.AreSame(key, data.Key); Assert.IsFalse(data.IsTable); Assert.IsTrue(data.IsOwner); Assert.IsTrue(data.LevelScoped);
            Assert.AreEqual(1, data.SchemaVersion); Assert.AreEqual(capacity, data.Capacity); Assert.IsNotEmpty(data.CapacitySource);
            Assert.AreEqual(ResourceSaveRequirement.SnapshotHook, data.SaveRequirement);
        }
        sealed class CountingModule : IGameplayModule, ICompositionManifestProvider
        {
            readonly SvModule m_Module; readonly ModuleManifest m_Manifest;
            public int Declarations; public string Id => m_Module == null ? "conflict" : m_Module.Id;
            public CountingModule(SvModule module) { m_Module = module; }
            public CountingModule(ModuleManifest manifest) { m_Manifest = manifest; }
            public ModuleManifest DescribeComposition() => m_Module == null ? m_Manifest : Describe(m_Module);
            public void DeclareData(WorldLayout layout) { Declarations++; if (m_Module != null) m_Module.DeclareData(layout); }
            public void RegisterSystems(SystemRegistry registry) { if (m_Module != null) m_Module.RegisterSystems(registry); }
        }
    }
}
