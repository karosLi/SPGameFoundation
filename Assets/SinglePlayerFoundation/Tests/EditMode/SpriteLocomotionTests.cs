using NUnit.Framework;
using SPF.Presentation.Animation;
using SPF.Presentation.Sprites;
using Unity.Mathematics;

namespace SPF.Tests.EditMode
{
    public class SpriteLocomotionTests
    {
        [Test]
        public void SpeedHysteresisKeepsTheSameFootPhaseAcrossWalkAndRun()
        {
            var clock = new SpriteLocomotionClock();
            clock.Advance(0.13f, 2f, 5f);
            Assert.AreEqual(GameplayLocomotionState.Walk, clock.State);
            float phase = clock.Phase;
            clock.Advance(0.01f, 4f, 5f);
            Assert.AreEqual(GameplayLocomotionState.Run, clock.State);
            Assert.AreEqual(math.frac(phase + 0.024f), clock.Phase, 1e-6f);
            for (int i = 0; i < 10; i++) clock.Advance(0.01f, i % 2 == 0 ? 3.5f : 3.7f, 5f);
            Assert.AreEqual(GameplayLocomotionState.Run, clock.State, "run remains latched inside the dead band");
            phase = clock.Phase;
            clock.Advance(0.01f, 2.9f, 5f);
            Assert.AreEqual(GameplayLocomotionState.Walk, clock.State);
            Assert.AreEqual(math.frac(phase + 0.0174f), clock.Phase, 1e-6f);
            var walk = new SpriteClip(10, 4, 6f, true);
            var run = new SpriteClip(30, 4, 12f, true);
            Assert.AreEqual(clock.Frame(walk) - walk.First, clock.Frame(run) - run.First,
                "sampling strips of different cadence preserves the normalized contact phase");
        }

        [Test]
        public void PauseAirAndDisabledLocomotionDoNotAdvanceTheStride()
        {
            var clock = new SpriteLocomotionClock();
            clock.Advance(0.1f, 4f, 5f);
            float phase = clock.Phase, time = clock.Time;
            clock.Advance(0f, 0f, 5f, false, false);
            Assert.AreEqual(GameplayLocomotionState.Run, clock.State);
            Assert.AreEqual(phase, clock.Phase);
            Assert.AreEqual(time, clock.Time);
            clock.Advance(0.1f, 4f, 5f, grounded: false);
            Assert.AreEqual(GameplayLocomotionState.Air, clock.State);
            Assert.AreEqual(phase, clock.Phase);
            clock.Advance(0.1f, 4f, 5f, enabled: false);
            Assert.AreEqual(GameplayLocomotionState.Idle, clock.State);
            Assert.AreEqual(phase, clock.Phase);
            clock.Advance(0.1f, 0.07f, 5f);
            Assert.AreEqual(GameplayLocomotionState.Idle, clock.State);
            clock.Advance(0.1f, 0.09f, 5f);
            clock.Advance(0.1f, 0.06f, 5f);
            Assert.AreEqual(GameplayLocomotionState.Walk, clock.State);
            clock.Advance(0.1f, 0.04f, 5f);
            Assert.AreEqual(GameplayLocomotionState.Idle, clock.State);
        }

        [Test]
        public void CadenceFollowsDistanceAndNotFrameRate()
        {
            var fast = new SpriteLocomotionClock();
            var slow = new SpriteLocomotionClock();
            for (int i = 0; i < 24; i++) fast.Advance(1f / 120f, 2f, 5f);
            for (int i = 0; i < 6; i++) slow.Advance(1f / 30f, 2f, 5f);
            Assert.AreEqual(fast.Phase, slow.Phase, 1e-5f);
            Assert.AreEqual(0.24f, fast.Phase, 1e-5f);
        }
    }
}
