using System;
using System.Collections.Generic;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Runtime.Composition;
using SPF.Runtime.World;

namespace SPF.Tests.EditMode
{
    public class CompositionContractTests
    {
        static readonly TableKey Things = new TableKey("Manifest.Things");
        static readonly ColumnKey<int> Values = new ColumnKey<int>(Things, "Value");
        static readonly ResourceKey<object> Plain = new ResourceKey<object>("Manifest.Plain");
        static readonly ResourceKey<OwnedResource> Owned = new ResourceKey<OwnedResource>("Manifest.Owned");
        static ModuleCapability Cap(string id, int version = 1) => new ModuleCapability(id, version);
        static ModuleManifest Manifest(string id = "owner", ModuleCapability[] provides = null,
            ModuleCapability[] requires = null, params ModuleDataDeclaration[] data) =>
            new ModuleManifest(id, 1, provides, requires, data);
        static ModuleDataDeclaration Table(int capacity = 4, bool owner = true, int schema = 1,
            bool level = false, int max = 0) => ModuleDataDeclaration.Table(Things, schema, capacity,
                "test count", owner, level, maxCapacity: max);
        static CompositionReport Check(params ModuleManifest[] manifests)
        {
            var modules = new IGameplayModule[manifests.Length];
            for (int i = 0; i < modules.Length; i++) modules[i] = new Module("slot" + i, manifests[i]);
            return CompositionPreflight.Validate(modules, SessionSettings.Default);
        }
        static void Error(CompositionReport report, string text)
        {
            Assert.IsFalse(report.IsValid);
            Assert.That(string.Join("; ", report.Errors), Does.Contain(text));
            Assert.Throws<ArgumentException>(report.ThrowIfInvalid);
        }

        [Test]
        public void MissingProviderFailsBeforeAnyDeclarationOrResourceAcquisition()
        {
            int calls = 0;
            var modules = new[] { new Module("first", Manifest(), l => calls++),
                new Module("later", Manifest("later", requires: new[] { Cap("missing") }), l => calls++) };
            Assert.Throws<ArgumentException>(() => { using var w = WorldComposer.BuildWorld(modules, SessionSettings.Default, 1); });
            Assert.AreEqual(0, calls);
        }
        [Test]
        public void MissingProviderNamesModuleAndCapability()
        {
            var report = Check(Manifest("consumer", requires: new[] { Cap("motion") }));
            Error(report, "motion"); Assert.AreEqual("consumer", report.Errors[0].ModuleId);
        }
        [Test]
        public void DuplicateAndWrongVersionProvidersAreRejected()
        {
            Error(Check(Manifest("a", new[] { Cap("motion") }), Manifest("b", new[] { Cap("motion", 2) })), "provider");
            Error(Check(Manifest("a", new[] { Cap("motion", 2) }), Manifest("b", requires: new[] { Cap("motion") })), "version");
        }
        [Test]
        public void DependencyCyclesFailWithoutReorderingInput()
        {
            Error(Check(Manifest("a", new[] { Cap("a") }, new[] { Cap("b") }),
                Manifest("b", new[] { Cap("b") }, new[] { Cap("a") })), "cycle");
        }
        [Test]
        public void ProviderMayBeListedLaterAndExplicitDeclarationOrderRemains()
        {
            var events = new List<string>();
            var modules = new[] {
                new Module("consumer", Manifest("consumer", requires: new[] { Cap("motion") }), l => events.Add("consumer")),
                new Module("provider", Manifest("provider", provides: new[] { Cap("motion") }), l => events.Add("provider")) };
            var report = CompositionPreflight.Validate(modules, SessionSettings.Default);
            Assert.IsTrue(report.IsValid); Assert.AreEqual("consumer", report.Manifests[0].StableId);
            using var world = WorldComposer.BuildWorld(modules, SessionSettings.Default, 1);
            CollectionAssert.AreEqual(new[] { "consumer", "provider" }, events);
        }
        [Test]
        public void DuplicateManifestIdsAndInvalidVersionsFail()
        {
            Error(Check(Manifest(), Manifest()), "owner");
            Error(Check(new ModuleManifest("bad", 0)), "schema");
            Error(Check(Manifest("bad", new[] { Cap("", 0) })), "capability");
        }
        [Test]
        public void DuplicateCapabilityDeclarationsAreNotSilentlyIgnored()
        {
            Error(Check(Manifest(provides: new[] { Cap("a"), Cap("a") })), "provider");
            Error(Check(Manifest(provides: new[] { Cap("a") }, requires: new[] { Cap("a"), Cap("a") })), "requirement");
        }
        [Test]
        public void SameNameDifferentRuntimeKeysCannotMasqueradeAsOneDeclaration()
        {
            Error(Check(Manifest(data: new[] { Table() }), Manifest("other", data: new[] {
                ModuleDataDeclaration.Table(new TableKey(Things.Name), 1, 4, "other") })), "key identity");
        }
        [Test]
        public void ConflictingSchemaAndScopeAreRejected()
        {
            Error(Check(Manifest(data: new[] { Table() }), Manifest("extension", data: new[] { Table(owner: false, schema: 2) })), "schema");
            Error(Check(Manifest(data: new[] { Table() }), Manifest("extension", data: new[] { Table(owner: false, level: true) })), "scope");
        }
        [Test]
        public void TableOwnerMustBeUniqueAndPresent()
        {
            Error(Check(Manifest(data: new[] { Table(owner: false) })), "owner");
            Error(Check(Manifest(data: new[] { Table() }), Manifest("other", data: new[] { Table() })), "owner");
        }
        [Test]
        public void CapacityMergeRetainsMaximumAndReportsOwner()
        {
            var report = Check(Manifest(data: new[] { Table() }), Manifest("extension", data: new[] { Table(8, false) }));
            Assert.IsTrue(report.IsValid); Assert.AreEqual(8, report.Data[0].Capacity);
            Assert.AreEqual("owner", report.Data[0].OwnerModuleId);
            var modules = new[] { new Module("owner", report.Manifests[0], l => l.Table(Things, 4).Column(Values)),
                new Module("extension", report.Manifests[1], l => l.Table(Things, 8)) };
            using var world = WorldComposer.BuildWorld(modules, SessionSettings.Default, 1);
            Assert.AreEqual(8, world.Table(Things).Capacity);
        }
        [Test]
        public void ExplicitCapacityBudgetRejectsExtensionOverrun()
        { Error(Check(Manifest(data: new[] { Table(max: 6) }), Manifest("extension", data: new[] { Table(8, false) })), "capacity"); }
        [Test]
        public void InvalidCapacityAndMissingSourceFail()
        {
            Error(Check(Manifest(data: new[] { Table(0) })), "capacity");
            Error(Check(Manifest(data: new[] { ModuleDataDeclaration.Table(Things, 1, 4, null) })), "source");
        }
        [Test]
        public void DuplicateResourcesAndUnsupportedScopeOrSaveClaimsFail()
        {
            var resource = ModuleDataDeclaration.Resource(Plain, 1);
            Error(Check(Manifest(data: new[] { resource }), Manifest("other", data: new[] { resource })), "resource");
            Error(Check(Manifest(data: new[] { ModuleDataDeclaration.Resource(Plain, 1, levelScoped: true) })), "IResettableResource");
            Error(Check(Manifest(data: new[] { ModuleDataDeclaration.Resource(Plain, 1, save: ResourceSaveRequirement.SnapshotHook) })), "ISnapshotResource");
        }
        [Test]
        public void NullDeclarationsAndInvalidEnumAreRejected()
        {
            Error(Check(Manifest(data: new ModuleDataDeclaration[] { null })), "declaration");
            Error(Check(Manifest(data: new[] { ModuleDataDeclaration.Resource<object>(null, 1) })), "key");
            Error(Check(Manifest(data: new[] { ModuleDataDeclaration.Resource(Plain, 1, save: (ResourceSaveRequirement)42) })), "save");
        }
        [Test]
        public void ManifestCopiesInputArraysAndExposesReadOnlyCollections()
        {
            var provided = new[] { Cap("original") }; var data = new[] { Table() };
            var manifest = Manifest(provides: provided, data: data);
            provided[0] = Cap("changed"); data[0] = null;
            Assert.AreEqual("original", manifest.Provides[0].Id); Assert.IsNotNull(manifest.Data[0]);
            Assert.Throws<NotSupportedException>(() => ((IList<ModuleCapability>)manifest.Provides)[0] = Cap("changed"));
        }
        [Test]
        public void InvalidSettingsAreCheckedBeforeDescriptionCallbacks()
        {
            var module = new Module("owner", Manifest());
            var settings = SessionSettings.Default; settings.TickRate = 0;
            Assert.Throws<ArgumentOutOfRangeException>(() => CompositionPreflight.Validate(new[] { module }, settings));
            Assert.AreEqual(0, module.Descriptions);
        }
        [Test]
        public void LegacyAndOptOutModulesStayValidAndUnclaimed()
        {
            var report = CompositionPreflight.Validate(new IGameplayModule[] { new Legacy(), new Module("optout", null) }, SessionSettings.Default);
            Assert.IsTrue(report.IsValid); Assert.AreEqual(2, report.LegacyModuleCount); Assert.AreEqual(0, report.Manifests.Count);
        }
        [Test]
        public void IsolatedExampleKeepsRealOwnershipAtAcceptedRegistration()
        {
            var manifest = Manifest("example", new[] { Cap("example.points") }, data: new[] {
                Table(), ModuleDataDeclaration.Resource(Owned, 1, levelScoped: true, save: ResourceSaveRequirement.SnapshotHook) });
            OwnedResource resource = null;
            var module = new Module("example", manifest, layout => {
                layout.Table(Things, 4).Column(Values); layout.Resource(Owned, resource = new OwnedResource(), true); });
            var report = CompositionPreflight.Validate(new[] { module }, SessionSettings.Default);
            Assert.IsNull(resource); Assert.IsTrue(report.IsValid); Assert.AreEqual("example", report.Data[1].OwnerModuleId);
            using (var world = WorldComposer.BuildWorld(new[] { module }, SessionSettings.Default, 1))
            { Assert.AreSame(resource, world.Resource(Owned)); Assert.AreEqual(0, resource.Disposals); }
            Assert.AreEqual(1, resource.Disposals);
        }
        [Test]
        public void MixedLegacyModuleCannotImplicitlySatisfyCapabilityRequirement()
        {
            var report = CompositionPreflight.Validate(new IGameplayModule[] { new Legacy(),
                new Module("consumer", Manifest("consumer", requires: new[] { Cap("legacy") })) }, SessionSettings.Default);
            Error(report, "missing"); Assert.AreEqual(1, report.LegacyModuleCount);
        }
        [Test]
        public void DuplicateLocalDeclarationAndPooledConflictFail()
        {
            Error(Check(Manifest(data: new[] { Table(), Table() })), "duplicate data declaration");
            Error(Check(Manifest(data: new[] { Table() }), Manifest("extension", data: new[] {
                ModuleDataDeclaration.Table(Things, 1, 4, "test", owner: false, pooled: true) })), "pooled");
        }
        [Test]
        public void InvalidModuleSlotsFailBeforeMetadataCallbacks()
        {
            var module = new Module("same", Manifest());
            Assert.Throws<ArgumentException>(() => CompositionPreflight.Validate(new[] { module, null }, SessionSettings.Default));
            Assert.Throws<ArgumentException>(() => CompositionPreflight.Validate(new[] { module, module }, SessionSettings.Default));
            Assert.AreEqual(0, module.Descriptions);
        }
        [Test]
        public void RejectedMetadataDoesNotTransferEvenAnExistingExternalResource()
        {
            var resource = new OwnedResource();
            var module = new Module("owner", Manifest(requires: new[] { Cap("missing") }),
                layout => layout.Resource(Owned, resource));
            Assert.Throws<ArgumentException>(() => { using var world = WorldComposer.BuildWorld(new[] { module }, SessionSettings.Default, 1); });
            Assert.AreEqual(0, resource.Disposals); resource.Dispose();
        }
        sealed class Module : IGameplayModule, ICompositionManifestProvider
        {
            readonly ModuleManifest m_Manifest; readonly Action<WorldLayout> m_Declare;
            public int Descriptions; public string Id { get; }
            public Module(string id, ModuleManifest manifest, Action<WorldLayout> declare = null)
            { Id = id; m_Manifest = manifest; m_Declare = declare; }
            public ModuleManifest DescribeComposition() { Descriptions++; return m_Manifest; }
            public void DeclareData(WorldLayout layout) => m_Declare?.Invoke(layout);
            public void RegisterSystems(SystemRegistry registry) { }
        }
        sealed class Legacy : IGameplayModule
        {
            public string Id => "legacy";
            public void DeclareData(WorldLayout layout) { }
            public void RegisterSystems(SystemRegistry registry) { }
        }
        sealed class OwnedResource : IDisposable, IResettableResource, ISnapshotResource
        {
            public int Disposals;
            public void Dispose() { Disposals++; }
            public void OnReset() { }
            public void WriteSnapshot(System.IO.BinaryWriter writer) { }
            public void ReadSnapshot(System.IO.BinaryReader reader) { }
        }
    }
}
