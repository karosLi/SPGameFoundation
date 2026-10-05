using NUnit.Framework;
using SPF.L1.Navigation;
using SPF.L1.Spatial;
using SPF.L2.Progression;
using SPF.Shell.Input;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SPF.Tests.EditMode
{
    public class GridAStarTests
    {
        /// <summary>20 x 12 with a wall at x = 10 open only at the top (y = 10).</summary>
        static TileMap Maze()
        {
            var map = new TileMap(new int2(20, 12), 1f);
            for (int y = 0; y < 10; y++) map[new int2(10, y)] = 1;
            return map;
        }

        [Test]
        public void FindsOptimalPathsAroundWalls()
        {
            using var map = Maze();
            var view = map.AsView();
            using var scratch = new AStarScratch(20 * 12, Allocator.Persistent);
            var path = new NativeArray<int2>(256, Allocator.Temp);
            var s = scratch;
            int n = GridAStar.FindPath(view, new int2(2, 2), new int2(17, 2), path, ref s, diagonal: false);
            Assert.Greater(n, 0);
            Assert.AreEqual(new int2(2, 2), path[0]);
            Assert.AreEqual(new int2(17, 2), path[n - 1]);
            // Manhattan detour over the wall: 8 up, 15 across, 8 down = 31 steps.
            Assert.AreEqual(32, n, "optimal 4-connected length");
            for (int i = 1; i < n; i++)
            {
                Assert.AreEqual(1, math.csum(math.abs(path[i] - path[i - 1])));
                Assert.IsFalse(view.IsSolid(path[i]));
            }
            // 8-connected is shorter and never cuts the wall's corner.
            int d = GridAStar.FindPath(view, new int2(2, 2), new int2(17, 2), path, ref s, diagonal: true);
            Assert.Less(d, n);
            for (int i = 1; i < d; i++)
            {
                int2 step = path[i] - path[i - 1];
                if (math.all(step != 0)) Assert.IsFalse(view.IsSolid(new int2(path[i - 1].x + step.x, path[i - 1].y)) || view.IsSolid(new int2(path[i - 1].x, path[i - 1].y + step.y)), "no corner cutting");
            }
            int smooth = GridAStar.Smooth(view, path, d);
            Assert.Less(smooth, d, "string pulling drops waypoints");
            Assert.AreEqual(new int2(17, 2), path[smooth - 1]);
            path.Dispose();
        }

        [Test]
        public void BlockedMaskAndReachability()
        {
            using var map = Maze();
            var view = map.AsView();
            var s = new AStarScratch(20 * 12, Allocator.Persistent);
            var blocked = new NativeArray<byte>(20 * 12, Allocator.Temp);
            Assert.IsTrue(GridAStar.Reachable(view, new int2(2, 2), new int2(17, 2), ref s, blocked));
            blocked[view.Index(new int2(10, 10))] = 1;
            blocked[view.Index(new int2(10, 11))] = 1;
            Assert.IsFalse(GridAStar.Reachable(view, new int2(2, 2), new int2(17, 2), ref s, blocked), "the gap is closed");
            // Thousands of searches on one scratch stay correct (stamps, no clearing).
            for (int i = 0; i < 2000; i++) Assert.IsFalse(GridAStar.Reachable(view, new int2(2, 2), new int2(17, 2), ref s, blocked));
            blocked[view.Index(new int2(10, 11))] = 0;
            Assert.IsTrue(GridAStar.Reachable(view, new int2(2, 2), new int2(17, 2), ref s, blocked));
            blocked.Dispose();
            s.Dispose();
        }

        [Test]
        public void BatchJobSolvesManyRequests()
        {
            using var map = Maze();
            int count = 40, max = 64;
            var requests = new NativeArray<PathRequest>(count, Allocator.TempJob);
            for (int i = 0; i < count; i++) requests[i] = new PathRequest { Start = new int2(1 + i % 8, 1 + i % 9), Goal = new int2(18 - i % 6, 1 + i % 7) };
            requests[5] = new PathRequest { Start = new int2(1, 1), Goal = new int2(10, 3) };   // inside the wall: no path
            var paths = new NativeArray<int2>(count * max, Allocator.TempJob);
            var lengths = new NativeArray<int>(count, Allocator.TempJob);
            new AStarBatchJob { Map = map.AsView(), Requests = requests, Paths = paths, Lengths = lengths, MaxLength = max, Diagonal = true, SmoothPaths = true }.Schedule().Complete();
            for (int i = 0; i < count; i++)
            {
                if (i == 5) { Assert.AreEqual(0, lengths[i]); continue; }
                Assert.Greater(lengths[i], 0, $"request {i}");
                Assert.AreEqual(requests[i].Goal, paths[i * max + lengths[i] - 1]);
            }
            requests.Dispose(); paths.Dispose(); lengths.Dispose();
        }
    }

    public class GestureAndWaveTests
    {
        [Test]
        public void TapsDragsPinchesAndSwipes()
        {
            var g = new GestureTracker();
            g.BeginFrame(); g.Down(1, new float2(100, 100), 0f);
            g.BeginFrame(); g.Up(1, new float2(104, 102), 0.1f);
            Assert.AreEqual(1, g.Taps.Count, "a short press is a tap");

            g.BeginFrame(); g.Down(1, new float2(100, 100), 1f);
            g.BeginFrame(); g.Move(1, new float2(150, 100));
            Assert.AreEqual(new float2(50, 0), g.Drag, "dragging pans");
            g.BeginFrame(); g.Move(1, new float2(150, 130));
            Assert.AreEqual(new float2(0, 30), g.Drag);
            g.BeginFrame(); g.Up(1, new float2(150, 130), 3f);
            Assert.AreEqual(0, g.Taps.Count, "a drag is not a tap");
            Assert.AreEqual(0, g.Swipes.Count, "too slow for a swipe");

            g.BeginFrame(); g.Down(1, new float2(100, 100), 4f); g.Down(2, new float2(200, 100), 4f);
            g.BeginFrame(); g.Move(1, new float2(50, 100)); g.Move(2, new float2(250, 100));
            Assert.AreEqual(2f, g.Pinch, 1e-4f, "fingers twice as far apart");
            Assert.AreEqual(float2.zero, g.Drag, "no pan while pinching");
            g.BeginFrame(); g.Up(1, new float2(50, 100), 5f); g.Up(2, new float2(250, 100), 5f);

            g.BeginFrame(); g.Down(3, new float2(100, 100), 6f);
            g.BeginFrame(); g.Up(3, new float2(100, 30), 6.15f);
            Assert.AreEqual(1, g.Swipes.Count, "a quick stroke is a swipe");
            Assert.AreEqual(new int2(0, -1), GestureTracker.SwipeDirection(g.Swipes[0].from, g.Swipes[0].to));
        }

        [Test]
        public void WaveGroupsSpawnOnTimeAtAnyStep()
        {
            var group = new WaveGroup { Start = 2f, Kind = 1, Count = 5, Interval = 0.5f };
            Assert.AreEqual(0, WaveSchedule.Spawned(group, 1.9f));
            Assert.AreEqual(1, WaveSchedule.Spawned(group, 2f));
            Assert.AreEqual(3, WaveSchedule.Spawned(group, 3.1f));
            Assert.AreEqual(5, WaveSchedule.Spawned(group, 100f));
            Assert.AreEqual(4f, group.End);
            foreach (float dt in new[] { 1f / 30f, 1f / 60f, 0.37f })
            {
                int total = 0;
                for (float t = 0f; t < 6f; t += dt) total += WaveSchedule.Due(group, t, t + dt);
                Assert.AreEqual(5, total, $"step {dt}: each unit exactly once");
            }
        }
    }
}
