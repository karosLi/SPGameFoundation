using NUnit.Framework;
using PlatformerFoundation.Presentation;
using Unity.Mathematics;

namespace PlatformerFoundation.Tests
{
    public class PlSpriteStateTests
    {
        [Test]
        public void AtlasBudget()
        {
            using var art = PlArt.Build();
            TestContext.WriteLine($"Platformer atlas {art.Sheet.Size}, {art.Sheet.Count} frames; baseline 512x64, 31 frames");
            Assert.AreEqual(new int2(512, 64), art.Sheet.Size, "walk must fit the original 128 KiB RGBA allocation");
            Assert.AreEqual(35, art.Sheet.Count);
            Assert.AreEqual(4, art.HeroWalk.Count);
            Assert.AreEqual(4, art.HeroRun.Count);
        }

#if !SPF_DOTNET_HARNESS
        [Test]
        public void EachWalkAndRunFrameHasDifferentPixels()
        {
            using var art = PlArt.Build();
            var sheet = art.Sheet;
            var pixels = sheet.Texture.GetPixels32();
            for (int a = art.HeroWalk.First; a < art.HeroRun.First + art.HeroRun.Count; a++)
                for (int b = a + 1; b < art.HeroRun.First + art.HeroRun.Count; b++)
                {
                    bool different = false;
                    var oa = sheet.Origins[a]; var ob = sheet.Origins[b];
                    for (int y = 0; y < 18; y++)
                        for (int x = 0; x < 16; x++)
                            different |= !pixels[(oa.y + y) * sheet.Size.x + oa.x + x].Equals(pixels[(ob.y + y) * sheet.Size.x + ob.x + x]);
                    Assert.IsTrue(different, $"Frames {a} and {b} must be visibly distinct");
                }
        }
#endif
    }
}
