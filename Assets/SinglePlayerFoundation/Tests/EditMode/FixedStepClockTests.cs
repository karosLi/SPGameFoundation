using NUnit.Framework;
using SPF.Runtime.Scheduling;

namespace SPF.Tests.EditMode
{
    public class FixedStepClockTests
    {
        [Test]
        public void AccumulatesPartialFrames()
        {
            var clock = new FixedStepClock(tickRate: 30, maxTicksPerFrame: 3);
            Assert.AreEqual(0, clock.Advance(1f / 60f));
            Assert.AreEqual(1, clock.Advance(1f / 60f));
            Assert.Less(clock.Alpha, 0.01f);
        }

        [Test]
        public void LongFramesDropExcessTicks()
        {
            var clock = new FixedStepClock(tickRate: 30, maxTicksPerFrame: 3);
            Assert.AreEqual(3, clock.Advance(1f));
            Assert.AreEqual(27, clock.DroppedTicks);
            Assert.Less(clock.Alpha, 1f);
        }

        [Test]
        public void TickTimesAreSequential()
        {
            var clock = new FixedStepClock(tickRate: 20, maxTicksPerFrame: 3);
            var a = clock.NextTick();
            var b = clock.NextTick();
            Assert.AreEqual(0u, a.Tick);
            Assert.AreEqual(1u, b.Tick);
            Assert.AreEqual(0.05f, b.DeltaTime, 1e-6f);
            Assert.AreEqual(0.05, b.ElapsedTime, 1e-9);
        }
    }
}
