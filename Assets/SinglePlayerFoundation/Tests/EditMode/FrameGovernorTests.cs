using NUnit.Framework;
using SPF.Shell.Performance;

namespace SPF.Tests.EditMode
{
    public class FrameGovernorTests
    {
        static int Run(FrameBudget b, float ms, float seconds)
        {
            int changes = 0;
            for (float t = 0f; t < seconds; t += ms / 1000f)
                if (b.Feed(ms / 1000f)) changes++;
            return changes;
        }

        [Test]
        public void DegradesUnderSustainedLoadAndRecoversSlowly()
        {
            var b = new FrameBudget();
            Run(b, 16.6f, 5f);
            Assert.AreEqual(0, b.Level, "on budget: full quality");

            Run(b, 25f, 3f);
            Assert.AreEqual(1, b.Level, "two seconds over budget: one step down");
            Run(b, 25f, 20f);
            Assert.AreEqual(b.MaxLevel, b.Level, "clamped at the cheapest level");

            Run(b, 10f, 4f);
            Assert.AreEqual(b.MaxLevel, b.Level, "recovery needs a longer stretch of headroom");
            Run(b, 10f, 7f);
            Assert.AreEqual(b.MaxLevel - 1, b.Level, "then one step back up");
        }

        [Test]
        public void IgnoresHitchesAndBorderlineFrames()
        {
            var b = new FrameBudget();
            for (int i = 0; i < 20; i++) b.Feed(0.5f);       // loading hitches
            Assert.AreEqual(0, b.Level);
            Assert.AreEqual(0, Run(b, 18f, 30f), "inside the hysteresis band nothing moves");
        }

        [Test]
        public void IdleThrottleWakesOnActivity()
        {
            var idle = new IdleThrottle { IdleAfterSeconds = 1f };
            Assert.IsFalse(idle.Feed(0.5f, false));
            Assert.IsTrue(idle.Feed(0.6f, false));
            Assert.IsTrue(idle.Idle);
            Assert.IsTrue(idle.Feed(0.016f, true));
            Assert.IsFalse(idle.Idle);
        }
    }
}
