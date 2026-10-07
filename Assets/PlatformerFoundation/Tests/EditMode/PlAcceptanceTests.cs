using NUnit.Framework;
using SPF.Contracts;
using SPF.Runtime.Session;
using SPF.Testing;
using Unity.Mathematics;

namespace PlatformerFoundation.Tests
{
    public class PlAcceptanceTests
    {
        static GameplayAcceptanceCase Create()
        {
            var mode = PlMode.Create(out var module); var session = SimSession.Create(mode, 123);
            var game = session.World.Resource(PlKeys.Game);
            return new GameplayAcceptanceCase("platformer.classic", session, PlKeys.Walker,
                () => { game.Send(PlCommandKind.Start); session.Step(); },
                i => game.Input = InputFrame.Latch(game.Input, new InputFrame { Move = new float2(.2f, 0), Held = 1u, Pressed = i % 16 == 0 ? 1u : 0u }),
                () => game.Input = default, () => game.Input.Held == 0 && game.Input.Pressed == 0 && math.all(game.Input.Move == float2.zero),
                null, "Unsupported: PlRenderer has no per-mode quality setter; renderer/backend and locomotion rebind are separate existing graphics PlayMode tests.",
                "This EditMode adapter creates no views; PlPlayTests.LocomotionTransitionsPauseAndRebind owns real view rebind coverage.",
                () => { UnityEngine.Object.DestroyImmediate(module); UnityEngine.Object.DestroyImmediate(mode); });
        }
        [Test] public void SharedPooledCapacityAndCompaction() => GameplayAcceptance.PooledCapacityAndCompaction(Create);
        [Test] public void SharedQueuedArrivalOrder() => GameplayAcceptance.QueuedArrivalOrder(Create);
        [Test] public void SharedFactoryLifecycle() => GameplayAcceptance.Lifecycle(Create);
        [Test] public void SharedCapacityIdentityAndStructure() => GameplayAcceptance.CapacityIdentityAndDeferredStructure(Create);
        [Test] public void SharedSaveReplayAndDoubleSession() => GameplayAcceptance.SaveReplayAndDoubleSession(Create);
        [Test] public void SharedInputInterruption() => GameplayAcceptance.InputInterruption(Create);
        [Test] public void QualityAndViewsHaveExplicitUnsupportedReasons()
        { using var a = Create(); Assert.IsNull(a.Present); StringAssert.Contains("Unsupported:", a.PresentationScope); Assert.IsNotEmpty(a.UnsupportedViews); }
        [Test] public void SharedEvidenceExport() { using var a = Create(); a.Start(); AcceptanceEvidence.WriteIfRequested(a, "PlMode-default", "landscape"); }
    }
}
