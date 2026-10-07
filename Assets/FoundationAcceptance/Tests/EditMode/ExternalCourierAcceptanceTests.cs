using System.IO;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Runtime.Composition;
using SPF.Runtime.Scheduling;
using SPF.Runtime.Session;
using SPF.Runtime.World;
using SPF.Testing;
using Unity.Jobs;

namespace ExternalAcceptance.Tests
{
    // Test-only external rule consumer. No core or public game enums changed; this is not a playable demo.
    sealed class CourierModule : IGameplayModule
    {
        internal static readonly TableKey Parcel = new TableKey("ExternalCourier.Parcel");
        internal static readonly ColumnKey<int> Distance = new ColumnKey<int>(Parcel, "Distance");
        internal static readonly ResourceKey<CourierState> State = new ResourceKey<CourierState>("ExternalCourier.State");
        public string Id => "ExternalCourier";
        public void DeclareData(WorldLayout layout)
        { layout.Table(Parcel, 8).LevelScoped().Column(Distance); layout.Resource(State, new CourierState()); }
        public void RegisterSystems(SystemRegistry registry) => registry.Add(new CourierSystem());
    }
    sealed class CourierState : ISnapshotResource, IResettableResource
    {
        internal int Move, Delivered;
        public void OnReset() { Move = Delivered = 0; }
        public void WriteSnapshot(BinaryWriter w) { w.Write(Move); w.Write(Delivered); }
        public void ReadSnapshot(BinaryReader r) { Move = r.ReadInt32(); Delivered = r.ReadInt32(); }
    }
    sealed class CourierSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.Move;
        public override void Declare(AccessDeclaration a) => a.Write(CourierModule.Distance).Write(CourierModule.State);
        public override JobHandle OnTick(in SimContext c, JobHandle dependency)
        {
            dependency.Complete(); var state = c.World.Resource(CourierModule.State);
            var positions = c.World.Column(CourierModule.Distance);
            for (int i = 0; i < c.Count(CourierModule.Parcel); i++)
            { positions[i] += state.Move; if (positions[i] >= 16) { positions[i] -= 16; state.Delivered++; } }
            return dependency;
        }
    }
    public class ExternalCourierAcceptanceTests
    {
        static GameplayAcceptanceCase Create() => CreateWithSave(true);
        static GameplayAcceptanceCase CreateWithSave(bool supportSave)
        {
            var s = new SimSession(new IGameplayModule[] { new CourierModule() }, SessionSettings.Default, 123);
            var state = s.World.Resource(CourierModule.State);
            return new GameplayAcceptanceCase("external.courier-test-only", s, CourierModule.Parcel,
                () => s.World.CreateEntity(CourierModule.Parcel, out _), i => state.Move = i % 3 + 1,
                () => state.Move = 0, () => state.Move == 0, null,
                "Unsupported: test-only rule module has no renderer or quality controls; it is not a polished playable demo.",
                "Unsupported: no views or asynchronous assets are created by this test-only module.",
                readState: s.CaptureSnapshot,
                unsupportedSave: supportSave ? null : "Unsupported: this negative adapter deliberately opts out of save/restore.");
        }
        [Test] public void ExternalPooledStorageIsNotApplicable()
        { using var a = Create(); foreach (var t in a.Session.World.Tables) Assert.IsFalse(t.IsPooled, "External Courier uses a handle table, so pooled compaction is unsupported/not applicable."); }
        [Test] public void SharedQueuedArrivalOrder() => GameplayAcceptance.QueuedArrivalOrder(Create);
        [Test] public void MissingOptionalCapabilitiesFailWithExplicitReasons()
        {
            StringAssert.Contains("save unsupported", Assert.Throws<System.InvalidOperationException>(() =>
                GameplayAcceptance.SaveReplayAndDoubleSession(() => CreateWithSave(false))).Message);
            StringAssert.Contains("quality unsupported", Assert.Throws<System.InvalidOperationException>(() => GameplayAcceptance.QualityIsolation(Create)).Message);
            StringAssert.Contains("pooled storage unsupported", Assert.Throws<System.InvalidOperationException>(() => GameplayAcceptance.PooledCapacityAndCompaction(Create)).Message);
        }
        [Test] public void ExternalFactoryLifecycle() => GameplayAcceptance.Lifecycle(Create);
        [Test] public void ExternalCapacityIdentityAndStructure() => GameplayAcceptance.CapacityIdentityAndDeferredStructure(Create);
        [Test] public void ExternalSaveReplayAndDoubleSession() => GameplayAcceptance.SaveReplayAndDoubleSession(Create);
        [Test] public void ExternalInputInterruption() => GameplayAcceptance.InputInterruption(Create);
        [Test] public void ExternalUnsupportedPresentationIsExplicit()
        { using var a = Create(); Assert.IsNull(a.Present); StringAssert.Contains("Unsupported:", a.PresentationScope); StringAssert.Contains("Unsupported:", a.UnsupportedViews); }
        [Test] public void ExternalEvidenceExport() { using var a = Create(); a.Start(); AcceptanceEvidence.WriteIfRequested(a, "parcel8-rule-only", "none"); }
    }
}
