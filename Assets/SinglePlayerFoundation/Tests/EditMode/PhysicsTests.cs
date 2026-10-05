using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using SPF.L1.Physics;
using Unity.Mathematics;

namespace SPF.Tests.EditMode
{
    public class PhysicsTests
    {
        const float Dt = 1f / 60f;

        static PhysicsWorld2D World(int capacity = 512)
        {
            var w = new PhysicsWorld2D(capacity);
            w.Add(PhysicsWorld2D.BoxBody(new float2(0f, -0.5f), new float2(50f, 0.5f), 0f, 0f));   // ground (static)
            return w;
        }

        static void Run(PhysicsWorld2D w, float seconds)
        {
            for (int i = 0; i < (int)(seconds / Dt); i++) w.Step(Dt);
        }

        [Test]
        public void BoxFallsAndRestsOnTheGround()
        {
            using var w = World();
            int box = w.AddBox(new float2(0f, 5f), new float2(0.5f, 0.5f));
            Run(w, 3f);
            var b = w[box];
            Assert.AreEqual(0.5f, b.Position.y, 0.03f, "resting on the ground surface");
            Assert.AreEqual(0f, b.Angle, 0.01f);
            Assert.AreEqual(0, b.Awake, "a box at rest falls asleep");
        }

        [Test]
        public void TallStackStaysUpright()
        {
            using var w = World();
            var ids = new int[10];
            for (int i = 0; i < ids.Length; i++) ids[i] = w.AddBox(new float2(0f, 0.5f + i * 1.0f), new float2(0.5f, 0.5f));
            Run(w, 6f);
            var top = w[ids[ids.Length - 1]];
            Assert.AreEqual(0f, top.Position.x, 0.1f, "the stack did not topple");
            Assert.AreEqual(9.5f, top.Position.y, 0.15f);
            Assert.AreEqual(0, top.Awake, "the stack sleeps");
        }

        [Test]
        public void PyramidSettlesAndSleeps()
        {
            using var w = World();
            int rows = 12, count = 0;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < rows - r; c++, count++)
                    w.AddBox(new float2((c - (rows - r - 1) * 0.5f) * 1.05f, 0.5f + r * 1.0f), new float2(0.5f, 0.5f));
            Run(w, 8f);
            int awake = 0;
            float topY = 0f;
            for (int i = 1; i < w.HighWater; i++) { if (w[i].Awake != 0) awake++; topY = math.max(topY, w[i].Position.y); }
            Assert.AreEqual(0, awake, "all asleep");
            Assert.AreEqual(rows - 0.5f, topY, 0.3f, "the pyramid kept its height");
        }

        [Test]
        public void BallBouncesWithRestitutionAndRolls()
        {
            using var w = World();
            int ball = w.AddCircle(new float2(0f, 5f), 0.5f, restitution: 0.8f);
            float peak = 0f;
            bool hitGround = false;
            for (int i = 0; i < 180; i++)
            {
                w.Step(Dt);
                var b = w[ball];
                if (b.Position.y < 0.6f) hitGround = true;
                if (hitGround) peak = math.max(peak, b.Position.y);
            }
            Assert.IsTrue(hitGround);
            Assert.Greater(peak, 2.5f, "bounced back up");
            Assert.Less(peak, 5f, "lost energy");

            // A ball given a sideways push rolls (friction turns it).
            using var r = World();
            int roller = r.AddCircle(new float2(0f, 0.5f), 0.5f);
            var rb = r[roller];
            rb.Velocity = new float2(4f, 0f);
            r[roller] = rb;
            Run(r, 0.5f);
            Assert.Less(r[roller].AngularVelocity, -1f, "rolling clockwise to the right");
        }

        [Test]
        public void FastBallDoesNotTunnelThroughAThinWall()
        {
            using var w = World();
            w.AddBox(new float2(5f, 2f), new float2(0.05f, 2f), 0f, 0f);   // thin static wall
            int ball = w.AddCircle(new float2(0f, 2f), 0.2f);
            var b = w[ball];
            b.Velocity = new float2(120f, 0f);   // 2 m per step: ten times the wall thickness
            b.GravityScale = 0f;
            w[ball] = b;
            Run(w, 0.5f);
            Assert.Less(w[ball].Position.x, 5f, "speculative contacts stop it at the wall");
        }

        [Test]
        public void JointsHoldAPendulumAndARope()
        {
            using var w = World();
            int anchor = w.AddBox(new float2(0f, 10f), new float2(0.1f, 0.1f), 0f, 0f);
            int bob = w.AddCircle(new float2(3f, 10f), 0.3f);
            w.AddJoint(JointKind.Rod, anchor, bob, new float2(0f, 10f), new float2(3f, 10f));
            float maxErr = 0f;
            for (int i = 0; i < 240; i++)
            {
                w.Step(Dt);
                maxErr = math.max(maxErr, math.abs(math.distance(w[bob].Position, new float2(0f, 10f)) - 3f));
            }
            Assert.Less(maxErr, 0.08f, "rod length held while swinging");

            int hinged = w.AddBox(new float2(5f, 8f), new float2(1f, 0.1f));
            w.AddRevolute(anchor, hinged, new float2(4f, 8f));   // hinge at its left end... to a far anchor: falls and swings
            int rope = w.AddCircle(new float2(-2f, 9f), 0.2f);
            w.AddJoint(JointKind.Rope, anchor, rope, new float2(0f, 10f), new float2(-2f, 9f));
            Run(w, 2f);
            float ropeLen = math.distance(new float2(-2f, 9f), new float2(0f, 10f));
            Assert.LessOrEqual(math.distance(w[rope].Position, new float2(0f, 10f)), ropeLen + 0.08f, "rope never stretches");
        }

        [Test]
        public void HardHitsAreReported()
        {
            using var w = World();
            int box = w.AddBox(new float2(0f, 8f), new float2(0.5f, 0.5f));
            w.Settings.EventImpulse = 2f;
            bool reported = false;
            for (int i = 0; i < 120 && !reported; i++)
            {
                w.Step(Dt);
                for (int e = 0; e < w.EventCount; e++)
                    if (w.Events[e].A == 0 && w.Events[e].B == box && w.Events[e].Impulse > 2f) reported = true;
            }
            Assert.IsTrue(reported, "landing from 8 m is a strong hit");
            Run(w, 2f);
            Assert.AreEqual(0, w.EventCount, "resting contact is not an event");
        }

        [Test]
        public void QueriesFindBodies()
        {
            using var w = World();
            int box = w.AddBox(new float2(3f, 2f), new float2(0.5f, 0.5f), math.PI / 4f, 0f);
            int ball = w.AddCircle(new float2(-3f, 2f), 0.5f, 0f);
            Assert.IsTrue(w.Raycast(new float2(0f, 2f), new float2(10f, 2f), out var hit));
            Assert.AreEqual(box, hit.Body);
            Assert.AreEqual(3f - math.SQRT2 * 0.5f, hit.Point.x, 1e-3f, "hits the rotated box's corner");
            Assert.IsTrue(w.Raycast(new float2(0f, 2f), new float2(-10f, 2f), out hit));
            Assert.AreEqual(ball, hit.Body);
            Assert.AreEqual(1f, hit.Normal.x, 1e-3f);
            Assert.AreEqual(ball, w.OverlapPoint(new float2(-3.2f, 2.2f)));
            Assert.AreEqual(-1, w.OverlapPoint(new float2(0f, 5f)));
        }

        static byte[] Snapshot(PhysicsWorld2D w)
        {
            using var ms = new MemoryStream();
            using (var writer = new BinaryWriter(ms)) w.WriteSnapshot(writer);
            return ms.ToArray();
        }

        static PhysicsWorld2D Scene()
        {
            var w = World();
            for (int i = 0; i < 30; i++) w.AddBox(new float2((i % 6) * 1.1f - 3f, 0.5f + (i / 6) * 1.05f), new float2(0.5f, 0.5f));
            int ball = w.AddCircle(new float2(-10f, 2f), 0.4f, 4f);
            var b = w[ball];
            b.Velocity = new float2(25f, 3f);
            w[ball] = b;
            return w;
        }

        [Test]
        public void DeterministicAndSnapshotsContinueExactly()
        {
            using var a = Scene();
            using var b = Scene();
            byte[] mid = null;
            for (int i = 0; i < 240; i++)
            {
                a.Step(Dt);
                b.Step(Dt);
                if (i == 119) mid = Snapshot(a);
            }
            CollectionAssert.AreEqual(Snapshot(a), Snapshot(b), "same inputs, same world");

            using var c = World();
            using (var reader = new BinaryReader(new MemoryStream(mid))) c.ReadSnapshot(reader);
            for (int i = 120; i < 240; i++) c.Step(Dt);
            CollectionAssert.AreEqual(Snapshot(a), Snapshot(c), "restored mid-run (warm-start state included), identical end");
        }

        [Test]
        public void SlotsAreReusedAndJointsGoWithTheirBody()
        {
            using var w = World(8);
            int x = w.AddCircle(new float2(0f, 3f), 0.5f);
            int y = w.AddCircle(new float2(2f, 3f), 0.5f);
            w.AddJoint(JointKind.Rod, x, y, new float2(0f, 3f), new float2(2f, 3f));
            Assert.AreEqual(1, w.JointCount);
            w.Remove(x);
            Assert.AreEqual(0, w.JointCount);
            Assert.AreEqual(x, w.AddBox(new float2(0f, 5f), new float2(0.5f, 0.5f)), "freed slot reused");
            for (int i = 0; i < 10; i++) w.AddCircle(new float2(i, 10f), 0.2f);
            Assert.AreEqual(8, w.HighWater, "capacity respected");
        }

        [Test]
        public void Benchmark()
        {
            using var w = new PhysicsWorld2D(2048);
            w.AddBox(new float2(0f, -0.5f), new float2(40f, 0.5f), 0f, 0f);
            w.AddBox(new float2(-20f, 20f), new float2(0.5f, 20f), 0f, 0f);
            w.AddBox(new float2(20f, 20f), new float2(0.5f, 20f), 0f, 0f);
            var rnd = new Random(5);
            for (int i = 0; i < 600; i++)
            {
                var p = new float2(rnd.NextFloat(-18f, 18f), 2f + i * 0.08f);
                if (i % 3 == 0) w.AddCircle(p, rnd.NextFloat(0.25f, 0.45f));
                else w.AddBox(p, rnd.NextFloat2(0.2f, 0.45f), rnd.NextFloat(math.PI));
            }
            for (int i = 0; i < 60; i++) w.Step(Dt);   // warm-up (Burst compile in the editor)
            var watch = Stopwatch.StartNew();
            double worst = 0;
            int steps = 300, peakManifolds = 0;
            for (int i = 0; i < steps; i++)
            {
                long t0 = watch.ElapsedTicks;
                w.Step(Dt);
                worst = math.max(worst, (watch.ElapsedTicks - t0) * 1000.0 / Stopwatch.Frequency);
                peakManifolds = math.max(peakManifolds, w.Stats.Manifolds);
            }
            double mean = watch.Elapsed.TotalMilliseconds / steps;
            var s = w.Stats;
            string report = $"=== Physics2D: 600 bodies poured into a box ===\nstep ms mean {mean:F3}  worst {worst:F3}\n" +
                            $"bodies {s.Bodies}  awake {s.AwakeBodies}  pairs {s.Pairs}  manifolds {s.Manifolds} (peak {peakManifolds})  islands {s.Islands}\n";
            TestContext.WriteLine(report);
            string dir = Path.Combine(UnityEngine.Application.dataPath, "..", "Artifacts");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "perf-physics.txt"), report);
            Assert.Less(mean, 50.0);
        }
    }
}
