using System;
using NUnit.Framework;
using SPF.Presentation.Combat;
using SPF.Runtime.Composition;
using SPF.Runtime.Session;
using SPF.Testing;
using Unity.Mathematics;

namespace ShooterFoundation.Tests
{
    public class ShooterAcceptanceTests
    {
        static GameplayAcceptanceCase Create()
        {
            var config = ShooterConfig.CreateDefault();
            config.Settings.SpawnWaves = false;
            config.Settings.EnemyCapacity = 8; config.Settings.BulletCapacity = 32; config.Settings.PickupCapacity = 8;
            var mode = ShooterMode.Create(config, out var module);
            var session = SimSession.Create(mode, 123);
            var state = session.World.Resource(ShooterKeys.State);
            var visuals = new CombatVfxPool(8);
            return new GameplayAcceptanceCase("shooter.default", session, ShooterKeys.Enemy,
                () => { state.Send(ShooterCommandKind.Start); session.Step(); state.Send(ShooterCommandKind.Choose, 0); session.Step(); },
                i => state.Run.Move = new float2((i / 8 & 1) == 0 ? .25f : -.25f, .1f),
                state.CancelInput, () => math.all(state.Run.Move == float2.zero) && math.all(state.Run.Drag == float2.zero),
                (quality, i) => { visuals.BeginFrame(1f / 30, quality); for (int k = 0; k < 24; k++) visuals.Emit(VfxProfile.Impact, new float2(k, i), ((ulong)(i + 1) << 32) | (uint)(k + 1)); },
                "Actual CombatVfxPool admission/overflow under qualities 0 and 3; no renderer pixels or GPU execution.",
                "This EditMode adapter creates no views. Real multi-view/host retirement is covered by SessionHostRetirementTests and graphics PlayMode suites.",
                () => { UnityEngine.Object.DestroyImmediate(module); UnityEngine.Object.DestroyImmediate(mode); UnityEngine.Object.DestroyImmediate(config); },
                () => ShooterSpawner.Enemy(session.World, float2.zero, hp: 100, speed: 0),
                row => session.World.Column(ShooterKeys.Enemies)[row].Id);
        }

        [Test] public void SharedPooledCapacityAndCompaction() => GameplayAcceptance.PooledCapacityAndCompaction(Create);
        [Test] public void HandleDestroyQueueIsExplicitlyNotApplicable()
        { using var a = Create(); foreach (var table in a.Session.World.Tables) Assert.IsTrue(table.IsPooled, "Shooter uses pool rows plus spawn IDs, not EntityHandle/destroy-queue identity."); }
        [Test] public void SharedFactoryLifecycle() => GameplayAcceptance.Lifecycle(Create);
        [Test] public void SharedCapacityIdentityAndStructure() => GameplayAcceptance.PooledIdentityAndCompaction(Create);
        [Test] public void SharedSaveReplayAndDoubleSession() => GameplayAcceptance.SaveReplayAndDoubleSession(Create);
        [Test] public void SharedInputInterruption() => GameplayAcceptance.InputInterruption(Create);
        [Test] public void SharedQualityIsolation() => GameplayAcceptance.QualityIsolation(Create);
        [Test] public void SharedEvidenceExport() { using var a = Create(); a.Start(); AcceptanceEvidence.WriteIfRequested(a, "enemy8-bullet32-pickup8-no-waves", "portrait"); }
    }
}
