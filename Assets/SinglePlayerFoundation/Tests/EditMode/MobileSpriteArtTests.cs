using System;
using NUnit.Framework;
using SPF.Presentation;
using SPF.Presentation.Sprites;
using Unity.Mathematics;
using UnityEngine;

namespace SPF.Tests.EditMode
{
    public class MobileSpriteArtTests
    {
        [Test]
        public void DownsampleUsesAlphaWeightedColourAndPreservesCoverage()
        {
            var source = new PixelCanvas(4, 2);
            source.Pixels[0] = new Color32(240, 80, 20, 255);
            source.Pixels[1] = new Color32(0, 0, 255, 0); // invisible blue must not contaminate the edge
            source.Pixels[2] = new Color32(255, 255, 255, 255);
            source.Pixels[3] = new Color32(0, 0, 0, 255);
            source.Pixels[6] = new Color32(255, 255, 255, 255);
            source.Pixels[7] = new Color32(0, 0, 0, 255);
            var result = SmoothSpriteArt.Downsample(source, 2);
            Assert.AreEqual(2, result.Width);
            Assert.AreEqual(1, result.Height);
            Assert.AreEqual(new Color32(240, 80, 20, 64), result.Pixels[0]);
            Assert.AreEqual(new Color32(128, 128, 128, 255), result.Pixels[1]);
            var copy = SmoothSpriteArt.Downsample(source, 1);
            Assert.AreEqual(source.Pixels[0], copy.Pixels[0]);
            Assert.AreNotSame(source.Pixels, copy.Pixels);
            Assert.AreEqual(0, copy.Pixels[1].a, "RGB bleed must not change zero-alpha coverage");
        }

        [Test]
        public void TransparentRgbBleedPreservesCoverageAndDoesNotSmear()
        {
            var canvas = new PixelCanvas(5, 3);
            var red = new Color32(240, 20, 40, 128);
            canvas.Pixels[1 * 5 + 1] = red;
            SmoothSpriteArt.BleedTransparentRgb(canvas);
            Assert.AreEqual(red, canvas.Get(1, 1), "partial-alpha colour and alpha stay exact");
            Assert.AreEqual(new Color32(240, 20, 40, 0), canvas.Get(2, 1));
            Assert.AreEqual(new Color32(240, 20, 40, 0), canvas.Get(0, 0), "diagonal neighbour is padded");
            Assert.AreEqual(default(Color32), canvas.Get(3, 1), "snapshot makes padding exactly one pixel");
            Assert.AreEqual(128, canvas.Get(1, 1).a);
        }

        [Test]
        public void DownsampleAndAtlasRejectInvalidInputs()
        {
            var builder = new SpriteAtlasBuilder();
            Assert.Throws<ArgumentNullException>(() => builder.Add((PixelCanvas)null));
            Assert.Throws<ArgumentException>(() => builder.Add(new Color32[3], 2, 2));
            Assert.Throws<ArgumentException>(() => builder.Add(new PixelCanvas(0, 1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => builder.Build(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => builder.Build(32, padding: -1));
            Assert.Throws<ArgumentException>(() => builder.Build(32, padding: 0, extrudeEdges: true));
            builder.Add(new PixelCanvas(33, 1));
            Assert.Throws<ArgumentException>(() => builder.Build(32));
            Assert.Throws<ArgumentOutOfRangeException>(() => SmoothSpriteArt.Downsample(new PixelCanvas(2, 2), 0));
            Assert.Throws<ArgumentException>(() => SmoothSpriteArt.Downsample(new PixelCanvas(3, 2), 2));
        }

        [Test]
        public void ExtrudedFramesHavePrivateGuttersAndPreserveContentUv()
        {
            var builder = new SpriteAtlasBuilder();
            for (int i = 0; i < 12; i++) builder.Add(new PixelCanvas(7 + i % 3, 5 + i % 4));
            using var sheet = builder.Build(64, FilterMode.Bilinear, 2, true);
            Assert.AreEqual(FilterMode.Bilinear, sheet.Texture.filterMode);
            for (int a = 0; a < sheet.Count; a++)
            {
                var pa = sheet.Origins[a];
                var sa = sheet[a].Pixels;
                Assert.IsTrue(math.all(pa >= 2) && math.all(pa + sa + 2 <= sheet.Size));
                Assert.AreEqual((float)sa.x / sheet.Size.x, sheet[a].Uv.z);
                Assert.AreEqual((float)pa.y / sheet.Size.y, sheet[a].Uv.y);
                for (int b = a + 1; b < sheet.Count; b++)
                {
                    var pb = sheet.Origins[b];
                    var sb = sheet[b].Pixels;
                    bool gutterOverlap = pa.x - 2 < pb.x + sb.x + 2 && pb.x - 2 < pa.x + sa.x + 2
                        && pa.y - 2 < pb.y + sb.y + 2 && pb.y - 2 < pa.y + sa.y + 2;
                    Assert.IsFalse(gutterOverlap, "each frame owns all its extrusion pixels");
                }
            }
        }

        [Test]
        public void DefaultAtlasPackingAndFilterStayBackwardCompatible()
        {
            var builder = new SpriteAtlasBuilder();
            builder.Add(new PixelCanvas(8, 8));
            builder.Add(new PixelCanvas(8, 8));
            using var sheet = builder.Build(32);
            Assert.AreEqual(FilterMode.Point, sheet.Texture.filterMode);
            Assert.AreEqual(new int2(1, 1), sheet.Origins[0]);
            Assert.AreEqual(new int2(10, 1), sheet.Origins[1], "legacy one-pixel gap remains unchanged");
            Assert.AreEqual(new int2(32, 16), sheet.Size);
        }

        [Test]
        public void BlobShadowUsesGroundHeightAndCallerDepthWithoutAllocating()
        {
            var mask = BlobShadow.CreateCanvas(16, 8);
            Assert.AreEqual(0, mask.Get(0, 0).a);
            Assert.Greater(mask.Get(8, 4).a, 220);
            using var batch = new SpriteBatch(RenderTier.DataTexture, null, BlendKind.Translucent, 4);
            var profile = BlobShadowProfile.Default;
            profile.Size = new float2(2f, 1f);
            profile.Offset = new float2(0.25f, -0.5f);
            profile.Color = new float4(0f, 0f, 0f, 0.8f);
            profile.HeightFade = 0.25f;
            profile.HeightSpread = 0.5f;
            var uv = new float4(0f, 0f, 1f, 1f);
            Assert.IsTrue(BlobShadow.Add(batch, uv, new float2(2f, 3f), 7f, profile, 2f));
            var sprite = batch.Instances[0];
            Assert.AreEqual(new float2(2.25f, 2.5f), sprite.Center);
            Assert.AreEqual(new float2(4f, 2f), sprite.Size);
            Assert.AreEqual(7f, sprite.Depth);
            Assert.AreEqual(0.4f, sprite.Color.w, 1f / 255f);
            Assert.IsFalse(BlobShadow.Add(batch, uv, float2.zero, 0f, profile, 4f));
            Assert.IsFalse(BlobShadow.Add(batch, uv, float2.zero, 0f, profile, scale: 0f));
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 256; i++)
            {
                batch.Clear();
                BlobShadow.Add(batch, uv, float2.zero, 0f, profile, i % 5);
            }
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0L, bytes);
        }

#if !SPF_DOTNET_HARNESS
        // Unity stubs do not retain texture pixels. Run import/extrusion assertions only in real Unity.
        [Test]
        public void ImportedPixelsAreCopiedAndExtrudedWithoutNeighbourBleed()
        {
            var red = new Color32(230, 20, 30, 255);
            var blue = new Color32(10, 40, 240, 255);
            var source = new[] { red, red, red, red };
            var builder = new SpriteAtlasBuilder();
            builder.Add(source, 2, 2, "red");
            source[0] = blue;
            builder.Add(new[] { blue, blue, blue, blue }, 2, 2, "blue");
            using var sheet = builder.Build(16, FilterMode.Bilinear, 2, true);
            var pixels = sheet.Texture.GetPixels32();
            for (int frame = 0; frame < 2; frame++)
            {
                var origin = sheet.Origins[frame];
                for (int y = -2; y < 4; y++)
                for (int x = -2; x < 4; x++)
                    Assert.AreEqual(frame == 0 ? red : blue, pixels[(origin.y + y) * sheet.Size.x + origin.x + x]);
            }
        }

        [Test]
        public void ReadableTextureCanBeImportedWithoutTakingOwnership()
        {
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                var color = new Color32(12, 34, 56, 78);
                source.SetPixels32(new[] { color, color, color, color });
                source.Apply();
                var builder = new SpriteAtlasBuilder();
                builder.Add(source, "import");
                using (var sheet = builder.Build())
                {
                    int2 origin = sheet.Origins[0];
                    Assert.AreEqual(color, sheet.Texture.GetPixels32()[origin.y * sheet.Size.x + origin.x]);
                    Assert.AreEqual(0, sheet.Find("import"));
                }
                Assert.IsTrue(source != null, "sheet disposal cannot destroy caller-owned art");
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }
#endif
    }
}
