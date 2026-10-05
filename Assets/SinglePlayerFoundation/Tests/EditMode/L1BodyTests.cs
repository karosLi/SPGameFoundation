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
            for (int i = 0; i < 50; i++) { head.x += 0.1f; TrailMath.Advance(ref trail, store.Points, head, 40); }
            for (int i = 0; i < 50; i++) { head.y += 0.1f; TrailMath.Advance(ref trail, store.Points, head, 40); }

            for (int i = 1; i < trail.Count; i++)
            {
                float2 a = store.Points[trail.Slot(trail.Pushed - (uint)i)];
                float2 b = store.Points[trail.Slot(trail.Pushed - (uint)i - 1)];
                Assert.AreEqual(Spacing, math.distance(a, b), 1e-3f, $"segment {i}");
            }

            // 2.5 units behind the head the body is on the vertical leg; 7.5 behind on the horizontal leg.
            float2 onVertical = TrailMath.SampleBehind(trail, store.Points, head, 2.5f);
            Assert.AreEqual(5f, onVertical.x, 0.05f);
            Assert.AreEqual(2.5f, onVertical.y, 0.05f);
            float2 onHorizontal = TrailMath.SampleBehind(trail, store.Points, head, 7.5f);
            Assert.AreEqual(2.5f, onHorizontal.x, 0.1f);
            Assert.AreEqual(0f, onHorizontal.y, 0.1f);
        }

        [Test]
        public void SamplingBeyondTheTailClampsToTheOldestPoint()
        {
            using var store = new BodyStore(1024, 256);
            store.TryAllocate(16, out var trail);
            TrailMath.Reset(ref trail, store.Points, new float2(3, 3), new float2(0, -1), Spacing, 5);
            float2 tail = TrailMath.SampleBehind(trail, store.Points, new float2(3, 3), 100f);
            Assert.AreEqual(3f, tail.x, 1e-4f);
            Assert.AreEqual(1f, tail.y, 1e-4f);
            Assert.AreEqual(2f, TrailMath.BodyLength(trail, new float2(3, 3)), 1e-4f);
        }

        [Test]
        public void ResizeKeepsPointsAndFreedSlabsAreReused()
        {
            using var store = new BodyStore(4096, 1024);
            store.TryAllocate(16, out var trail);
            TrailMath.Reset(ref trail, store.Points, float2.zero, new float2(-1, 0), Spacing, 16);
            var before = new float2[16];
            for (int i = 0; i < 16; i++) before[i] = TrailMath.SampleBehind(trail, store.Points, float2.zero, i * Spacing);
            int oldStart = trail.Start;

            Assert.IsTrue(store.Resize(ref trail, 100));
            Assert.AreEqual(128, trail.Capacity);
            for (int i = 0; i < 16; i++)
                Assert.AreEqual(before[i], TrailMath.SampleBehind(trail, store.Points, float2.zero, i * Spacing));

            store.TryAllocate(10, out var other);
            Assert.AreEqual(oldStart, other.Start, "the freed 16-point slab is reused");
        }

        [Test]
        public void RespaceKeepsThePathAndTheBodyLength()
        {
            using var store = new BodyStore(4096, 1024);
            store.TryAllocate(256, out var trail);
            TrailMath.Reset(ref trail, store.Points, float2.zero, new float2(-1, 0), Spacing, 20);
            // A curved path: an arc of radius 6.
            float2 head = float2.zero;
            for (int i = 1; i <= 200; i++)
            {
                float a = i * 0.01f;
                head = new float2(math.sin(a), 1f - math.cos(a)) * 6f;
                TrailMath.Advance(ref trail, store.Points, head, 60);
            }
            float length = TrailMath.BodyLength(trail, head);
            var before = new float2[20];
            for (int i = 0; i < before.Length; i++) before[i] = TrailMath.SampleBehind(trail, store.Points, head, i * 1.3f);
            uint version = trail.Version;

            Assert.IsTrue(store.Respace(ref trail, head, 1.25f, 64));
            Assert.AreEqual(1.25f, trail.Spacing);
            Assert.Greater(trail.Version, version, "mirrors must re-upload");
            Assert.AreEqual(length, TrailMath.BodyLength(trail, head), 1.25f, "length kept up to one new spacing");
            for (int i = 0; i < before.Length && i * 1.3f < length - 1.25f; i++)
            {
                float2 after = TrailMath.SampleBehind(trail, store.Points, head, i * 1.3f);
                // Chords of a radius-6 arc with 1.25 spacing stay within ~3 cm of the original path.
                Assert.AreEqual(0f, math.distance(before[i], after), 0.05f, $"sample {i}");
            }
            // Advancing continues with the new spacing.
            head += new float2(0f, 3f);
            TrailMath.Advance(ref trail, store.Points, head, 60);
            float2 a0 = store.Points[trail.Slot(trail.Pushed - 1)], a1 = store.Points[trail.Slot(trail.Pushed - 2)];
            Assert.AreEqual(1.25f, math.distance(a0, a1), 1e-3f);
        }

        /// <summary>
        /// Incremental block bounds against a brute-force scan: the result must contain every kept point and
        /// the head, and may only add points of the oldest block (expired up to one block ago).
        /// </summary>
        [Test]
        public void TrailBoundsMatchBruteForce()
        {
            using var store = new BodyStore(8192, 1024);
            store.TryAllocate(64, out var trail);
            TrailMath.Reset(ref trail, store.Points, float2.zero, new float2(-1, 0), Spacing, 30);
            uint built = 0;
            var random = new Random(9);
            float2 head = float2.zero, heading = new float2(1, 0);
            for (int tick = 0; tick < 3000; tick++)
            {
                heading = math.normalize(heading + random.NextFloat2(-0.4f, 0.4f));
                head += heading * random.NextFloat(0.1f, 1.7f);
                int desired = 20 + (int)(60 + 50 * math.sin(tick * 0.01f));   // grows and shrinks
                if (tick % 700 == 350) Assert.IsTrue(store.Resize(ref trail, trail.Capacity * 2));
                if (desired > trail.Capacity) desired = trail.Capacity;   // full slab: oldest block slot gets reused
                uint pushedBefore = trail.Pushed;
                TrailMath.Advance(ref trail, store.Points, head, desired);
                if (built != trail.Version) { TrailBounds.Rebuild(trail, store.Points, store.BlockBounds); built = trail.Version; }
                else TrailBounds.Append(trail, store.Points, store.BlockBounds, pushedBefore);
                float4 box = TrailBounds.Compute(trail, store.Points, store.BlockBounds, head);

                float2 min = head, max = head;
                uint oldest = trail.Pushed - (uint)trail.Count;
                for (uint g = oldest; g < trail.Pushed; g++) { min = math.min(min, store.Points[trail.Slot(g)]); max = math.max(max, store.Points[trail.Slot(g)]); }
                Assert.IsTrue(math.all(box.xy <= min) && math.all(box.zw >= max), $"tick {tick}: bounds contain every kept point");
                // Slack: at most the expired part of the oldest block (< 16 points, each ≤ Spacing apart).
                float slack = TrailBounds.BlockSize * Spacing + 1e-3f;
                Assert.IsTrue(math.all(box.xy >= min - slack) && math.all(box.zw <= max + slack), $"tick {tick}: bounds stay tight");
            }
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
