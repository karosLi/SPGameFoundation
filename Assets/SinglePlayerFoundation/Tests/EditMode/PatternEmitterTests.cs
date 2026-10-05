using System.Collections.Generic;
using NUnit.Framework;
using SPF.L2.Combat;
using Unity.Mathematics;

namespace SPF.Tests.EditMode
{
    public class PatternEmitterTests
    {
        struct Collect : IBulletSink
        {
            public List<float2> Directions;
            public void Emit(float2 position, float2 direction, float speed) => Directions.Add(direction);
        }

        [Test]
        public void RingsCoverTheCircleAndSpiralsRotate()
        {
            var ring = PatternEmitter.Ring(8, 5f, 1f);
            var sink = new Collect { Directions = new List<float2>() };
            float timer = 0f, angle = 0f;
            Assert.AreEqual(1, ring.Update(ref timer, ref angle, 0.01f, float2.zero, float2.zero, ref sink));
            Assert.AreEqual(8, sink.Directions.Count);
            float2 sum = float2.zero;
            foreach (var d in sink.Directions) sum += d;
            Assert.Less(math.length(sum), 1e-4f, "evenly spread");
            Assert.AreEqual(0, ring.Update(ref timer, ref angle, 0.5f, float2.zero, float2.zero, ref sink), "waits for the interval");

            var spiral = PatternEmitter.Spiral(1, 5f, 0.1f, 0.3f);
            sink.Directions.Clear();
            timer = 0f; angle = 0f;
            Assert.AreEqual(3, spiral.Update(ref timer, ref angle, 0.25f, float2.zero, float2.zero, ref sink), "catches up on missed shots");
            Assert.AreEqual(0.3f, math.atan2(sink.Directions[1].y, sink.Directions[1].x), 1e-4f);
            Assert.AreEqual(0.6f, math.atan2(sink.Directions[2].y, sink.Directions[2].x), 1e-4f);
        }

        [Test]
        public void AimedFansCentreOnTheTarget()
        {
            var fan = PatternEmitter.AimedFan(5, 0.8f, 6f, 1f);
            var sink = new Collect { Directions = new List<float2>() };
            fan.Fire(0f, new float2(1f, 1f), new float2(1f, 10f), ref sink);
            Assert.AreEqual(5, sink.Directions.Count);
            Assert.AreEqual(0f, sink.Directions[2].x, 1e-5f, "middle bullet straight at the target");
            Assert.AreEqual(1f, sink.Directions[2].y, 1e-5f);
            Assert.AreEqual(-sink.Directions[0].x, sink.Directions[4].x, 1e-5f, "symmetric fan");
        }
    }
}
