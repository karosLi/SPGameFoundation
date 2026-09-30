using NUnit.Framework;
using SPF.L1.Body;
using SPF.L1.Movement;
using Unity.Mathematics;

namespace SPF.Tests.EditMode
{
    public class L1BodyTests
    {
        const float Spacing = 0.5f;

        [Test]
        public void TrailPointsAreExactlySpacingApartAndSamplingFollowsThePath()
        {
            using var store = new BodyStore(4096, 1024);
            Assert.IsTrue(store.TryAllocate(64, out var trail));
            TrailMath.Reset(ref trail, store.Points, float2.zero, new float2(-1, 0), Spacing, 10);

            // Move right 5 units in small steps, then up 5 units.
            float2 head = float2.zero;
            for (int i = 0; i < 50; i++) { head.x += 0.1f; TrailMath.Advance(ref trail, store.Points, head, Spacing, 40); }
            for (int i = 0; i < 50; i++) { head.y += 0.1f; TrailMath.Advance(ref trail, store.Points, head, Spacing, 40); }

            for (int i = 1; i < trail.Count; i++)
            {
                float2 a = store.Points[trail.Slot(trail.Pushed - (uint)i)];
                float2 b = store.Points[trail.Slot(trail.Pushed - (uint)i - 1)];
                Assert.AreEqual(Spacing, math.distance(a, b), 1e-3f, $"segment {i}");
            }

            // 2.5 units behind the head the body is on the vertical leg; 7.5 behind on the horizontal leg.
            float2 onVertical = TrailMath.SampleBehind(trail, store.Points, head, 2.5f, Spacing);
            Assert.AreEqual(5f, onVertical.x, 0.05f);
            Assert.AreEqual(2.5f, onVertical.y, 0.05f);
            float2 onHorizontal = TrailMath.SampleBehind(trail, store.Points, head, 7.5f, Spacing);
            Assert.AreEqual(2.5f, onHorizontal.x, 0.1f);
            Assert.AreEqual(0f, onHorizontal.y, 0.1f);
        }

        [Test]
        public void SamplingBeyondTheTailClampsToTheOldestPoint()
        {
            using var store = new BodyStore(1024, 256);
            store.TryAllocate(16, out var trail);
            TrailMath.Reset(ref trail, store.Points, new float2(3, 3), new float2(0, -1), Spacing, 5);
            float2 tail = TrailMath.SampleBehind(trail, store.Points, new float2(3, 3), 100f, Spacing);
            Assert.AreEqual(3f, tail.x, 1e-4f);
            Assert.AreEqual(1f, tail.y, 1e-4f);
            Assert.AreEqual(2f, TrailMath.BodyLength(trail, new float2(3, 3), Spacing), 1e-4f);
        }

        [Test]
        public void ResizeKeepsPointsAndFreedSlabsAreReused()
        {
            using var store = new BodyStore(4096, 1024);
            store.TryAllocate(16, out var trail);
            TrailMath.Reset(ref trail, store.Points, float2.zero, new float2(-1, 0), Spacing, 16);
            var before = new float2[16];
            for (int i = 0; i < 16; i++) before[i] = TrailMath.SampleBehind(trail, store.Points, float2.zero, i * Spacing, Spacing);
            int oldStart = trail.Start;

            Assert.IsTrue(store.Resize(ref trail, 100));
            Assert.AreEqual(128, trail.Capacity);
            for (int i = 0; i < 16; i++)
                Assert.AreEqual(before[i], TrailMath.SampleBehind(trail, store.Points, float2.zero, i * Spacing, Spacing));

            store.TryAllocate(10, out var other);
            Assert.AreEqual(oldStart, other.Start, "the freed 16-point slab is reused");
        }

        [Test]
        public void PoolExhaustionFailsWithoutCorruption()
        {
            using var store = new BodyStore(64, 64);
            Assert.IsTrue(store.TryAllocate(64, out _));
            Assert.IsFalse(store.TryAllocate(16, out var none));
            Assert.IsFalse(none.IsAllocated);
        }

        [Test]
        public void TurnTowardsRespectsMaxAngle()
        {
            var heading = new float2(1, 0);
            var target = new float2(0, 1);
            float2 turned = Steering.TurnTowards(heading, target, math.radians(10f));
            Assert.AreEqual(math.radians(10f), math.atan2(turned.y, turned.x), 1e-4f);
            float2 snapped = Steering.TurnTowards(heading, target, math.PI);
            Assert.AreEqual(0f, snapped.x, 1e-5f);
            Assert.AreEqual(1f, snapped.y, 1e-5f);
        }
    }
}
