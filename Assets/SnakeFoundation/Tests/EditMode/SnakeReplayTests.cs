using NUnit.Framework;
using Unity.Mathematics;

namespace SnakeFoundation.Tests
{
    public class SnakeReplayTests
    {
        static float Hash(SnakeTestWorld t)
        {
            float hash = t.World.Table(SnakeKeys.Food).Count;
            var heads = t.World.Column(SnakeKeys.Head);
            var masses = t.World.Column(SnakeKeys.Mass);
            for (int i = 0; i < t.World.Table(SnakeKeys.Snake).Count; i++)
                hash += heads[i].x * 0.37f + heads[i].y * 0.11f + masses[i] * (i + 1);
            return hash;
        }

        [Test]
        public void ARecordedRunReplaysExactly()
        {
            ReplayFrame[] recording;
            float expected;
            float2 expectedPlayerHead;
            using (var a = new SnakeTestWorld(aiPerRegion: 30, foodPerChunk: 30, seed: 99))
            {
                var replay = a.World.Resource(SnakeKeys.Replay);
                replay.Mode = ReplayMode.Record;
                a.Step(5);
                a.Game.RequestStart();
                for (int i = 0; i < 240; i++)
                {
                    a.Game.Command = new PlayerCommand
                    {
                        Direction = new float2(math.cos(i * 0.07f), math.sin(i * 0.03f)),
                        Boost = i % 40 < 8,
                        Skill = i % 60 == 0,
                    };
                    a.Step();
                }
                recording = replay.ToArray();
                expected = Hash(a);
                expectedPlayerHead = a.PlayerRow >= 0 ? a.World.Column(SnakeKeys.Head)[a.PlayerRow] : float2.zero;
            }

            using var b = new SnakeTestWorld(aiPerRegion: 30, foodPerChunk: 30, seed: 99);
            b.World.Resource(SnakeKeys.Replay).Load(recording);
            b.Step(recording.Length);
            Assert.AreEqual(expected, Hash(b));
            if (b.PlayerRow >= 0)
                Assert.AreEqual(expectedPlayerHead, b.World.Column(SnakeKeys.Head)[b.PlayerRow]);
        }
    }
}
