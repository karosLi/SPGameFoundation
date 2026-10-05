using System.Collections.Generic;
using NUnit.Framework;
using SPF.Contracts;

namespace RpgFoundation.Tests
{
    public class RpgSnapshotTests
    {
        /// <summary>Plays <paramref name="ticks"/> ticks with bot input, recording (or replaying) the input.</summary>
        static void Play(RpgTestWorld t, RpgBot bot, int ticks, List<InputFrame> inputs, bool replay)
        {
            for (int i = 0; i < ticks; i++)
            {
                if (replay) t.Input(inputs[i]);
                else
                {
                    var frame = bot.Think(t.World);
                    inputs.Add(frame);
                    t.Input(frame);
                }
                t.Step();
            }
        }

        [Test]
        public void MidFloorSnapshotContinuesIdentically()
        {
            using var a = new RpgTestWorld();
            using var bot = new RpgBot();
            var warmup = new List<InputFrame>();
            Play(a, bot, 240, warmup, false);
            Assert.Greater(a.World.Table(RpgKeys.Actor).Count, 1, "a fight is in progress");
            var snapshot = a.Session.CaptureSnapshot();

            var inputs = new List<InputFrame>();
            Play(a, bot, 300, inputs, false);
            var end = a.Session.CaptureSnapshot();
            uint endTick = a.Session.Clock.NextTickIndex;

            // A fresh session (menu state, never played) restored from the snapshot.
            using var b = new RpgTestWorld(start: false);
            b.Session.RestoreSnapshot(snapshot);
            CollectionAssert.AreEqual(snapshot, b.Session.CaptureSnapshot(), "restore is exact");
            Assert.AreEqual(RpgFlow.Playing, b.Game.Flow);
            Assert.GreaterOrEqual(b.HeroRow, 0);
            Play(b, null, 300, inputs, true);
            Assert.AreEqual(endTick, b.Session.Clock.NextTickIndex);
            CollectionAssert.AreEqual(end, b.Session.CaptureSnapshot(), "fresh session continues identically");

            // The original session rewound in place (stale rows, generations and caches from the future).
            a.Session.RestoreSnapshot(snapshot);
            Play(a, null, 300, inputs, true);
            CollectionAssert.AreEqual(end, a.Session.CaptureSnapshot(), "rewound session continues identically");
        }

        [Test]
        public void CorruptSnapshotRestartsTheSession()
        {
            using var a = new RpgTestWorld();
            a.Step(30);
            var snapshot = a.Session.CaptureSnapshot();
            System.Array.Resize(ref snapshot, snapshot.Length / 2);
            using var b = new RpgTestWorld();
            Assert.Throws<System.IO.EndOfStreamException>(() => b.Session.RestoreSnapshot(snapshot));
            Assert.AreEqual(0, b.World.Table(RpgKeys.Actor).Count, "half-restored state was discarded");
            Assert.AreEqual(0u, b.Session.Clock.NextTickIndex);
        }
    }
}
