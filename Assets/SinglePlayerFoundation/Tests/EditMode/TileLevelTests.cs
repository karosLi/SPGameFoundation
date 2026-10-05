using NUnit.Framework;
using SPF.L1.Spatial;
using Unity.Mathematics;
using UnityEngine;

namespace SPF.Tests.EditMode
{
    public class TileLevelTests
    {
        static readonly TileSymbol[] Legend =
        {
            new TileSymbol { Symbol = '#', Layer = 0, Value = 1 },
            new TileSymbol { Symbol = '=', Layer = 0, Value = 2 },
            new TileSymbol { Symbol = '^', Layer = 1, Value = 1 },
            new TileSymbol { Symbol = 'S', Marker = true },
            new TileSymbol { Symbol = 'o', Marker = true },
        };

        static readonly string[] Rows =
        {
            "   o  ",
            "S  == ",
            "##^^##",
        };

        [Test]
        public void TextRoundTripsThroughLayersAndMarkers()
        {
            var level = TileLevelAsset.Create(Legend, 2, Rows);
            try
            {
                Assert.AreEqual(6, level.Width);
                Assert.AreEqual(3, level.Height);
                Assert.AreEqual(1, level.Get(0, 0, 0), "bottom row is y = 0");
                Assert.AreEqual(1, level.Get(1, 2, 0), "spikes on the hazard layer");
                Assert.AreEqual(0, level.Get(0, 2, 0));
                Assert.AreEqual(2, level.Get(0, 3, 1));
                Assert.AreEqual(2, level.Markers.Count);
                CollectionAssert.AreEqual(new[] { "   o", "S  ==", "##^^##" }, level.ToRows(), "round trip (trailing spaces trimmed)");
            }
            finally { Object.DestroyImmediate(level); }
        }

        [Test]
        public void PaintingResizingAndCopyingToTileMaps()
        {
            var level = TileLevelAsset.Create(Legend, 2, Rows);
            using var map = new TileMap(new int2(8, 4), 1f);
            try
            {
                int revision = level.Revision;
                level.Paint('#', 5, 2);
                level.Paint('o', 0, 2);
                level.Paint(' ', 0, 0);
                Assert.Greater(level.Revision, revision);
                Assert.AreEqual(1, level.Get(0, 5, 2));
                Assert.AreEqual(0, level.Get(0, 0, 0), "erased");
                Assert.AreEqual(3, level.Markers.Count);

                level.Resize(3, 3, 2);
                Assert.AreEqual(1, level.Get(0, 1, 0), "kept the overlap");
                Assert.AreEqual(2, level.Markers.Count, "markers outside the new size are dropped");

                level.CopyTo(0, map);
                Assert.AreEqual(1, map[new int2(1, 0)]);
                Assert.AreEqual(0, map[new int2(6, 0)], "outside the level: cleared");
            }
            finally { Object.DestroyImmediate(level); }
        }
    }
}
