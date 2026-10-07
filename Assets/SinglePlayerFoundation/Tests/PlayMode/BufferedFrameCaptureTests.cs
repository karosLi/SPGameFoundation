#if !SPF_DOTNET_HARNESS
using System;
using System.Collections;
using System.IO;
using System.Globalization;
using NUnit.Framework;
using SPF.Testing;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace SPF.Tests.PlayMode
{
    public class BufferedFrameCaptureTests
    {
        #pragma warning disable CS0649 // Populated by JsonUtility.FromJson in the native test.
        [Serializable]
        sealed class CaptureMetadata
        {
            public int version, frame_count, width, height, jpeg_quality;
            public string format, filename_extension, compression_scope, timestamp_source;
            public bool lossy, alpha_preserved;
            public long raw_buffer_bytes;
        }

        #pragma warning restore CS0649

        [UnityTest]
        public IEnumerator DeferredEncodingKeepsDistinctActualFramesAndBoundedCapacity()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Actual render target required.");
            var target = new RenderTexture(16, 8, 0, RenderTextureFormat.ARGB32);
            target.Create();
            var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            var prior = RenderTexture.active;
            try
            {
                using (var frames = new BufferedFrameCapture(target, 2))
                {
                    RenderTexture.active = target; GL.Clear(false, true, Color.red); RenderTexture.active = prior;
                    Assert.IsTrue(frames.Capture(1.0));
                    yield return null;
                    RenderTexture.active = target; GL.Clear(false, true, Color.blue); RenderTexture.active = prior;
                    Assert.IsTrue(frames.Capture(1.0 + 1.0 / 60));
                    Assert.IsFalse(frames.Capture(2.0), "A full buffer must reject rather than overwrite a frame.");
                    Assert.AreEqual(16 * 8 * 4 * 2, frames.BufferBytes);
                    Assert.Greater(frames.ReadPixel(0, 4, 4).r, 240);
                    Assert.Greater(frames.ReadPixel(1, 4, 4).b, 240);
                    string directory = frames.Write("buffered-capture-regression", "Two synthetic solid render targets validate capture plumbing, not gameplay or performance.");
                    Assert.IsTrue(ImageConversion.LoadImage(decoded, File.ReadAllBytes(Path.Combine(directory, "frame-000.png"))));
                    Assert.Greater(decoded.GetPixel(4, 4).r, .95f, "Deferred PNG must encode the retained first frame, not the most recent target.");
                    Assert.IsTrue(ImageConversion.LoadImage(decoded, File.ReadAllBytes(Path.Combine(directory, "frame-001.png"))));
                    Assert.Greater(decoded.GetPixel(4, 4).b, .95f);
                    StringAssert.Contains("simulation_seconds_at_readback", File.ReadAllText(Path.Combine(directory, "acquisition.csv")));
                    StringAssert.Contains("duration ", File.ReadAllText(Path.Combine(directory, "acquisition.ffconcat")));
                    var pngMetadata = JsonUtility.FromJson<CaptureMetadata>(File.ReadAllText(Path.Combine(directory, "capture.json")));
                    Assert.AreEqual("png", pngMetadata.format); Assert.AreEqual(".png", pngMetadata.filename_extension);
                    Assert.IsFalse(pngMetadata.lossy); Assert.IsTrue(pngMetadata.alpha_preserved);
                    Assert.AreEqual(0, pngMetadata.jpeg_quality, "JPEG quality is not applicable to PNG.");
                    var retained = new Color32[frames.Count * frames.Width * frames.Height];
                    for (int frame = 0; frame < frames.Count; frame++)
                    {
                        ImageConversion.LoadImage(decoded, File.ReadAllBytes(Path.Combine(directory, "frame-" + frame.ToString("D3", CultureInfo.InvariantCulture) + ".png")));
                        for (int y = 0; y < frames.Height; y++)
                            for (int x = 0; x < frames.Width; x++)
                            {
                                var raw = frames.ReadPixel(frame, x, y);
                                retained[(frame * frames.Height + y) * frames.Width + x] = raw;
                                Assert.AreEqual(raw, (Color32)decoded.GetPixel(x, y), "The default PNG remains a lossless pixel oracle.");
                            }
                    }
                    string jpegDirectory = frames.Write("buffered-capture-jpeg-regression", "Deferred lossy review encoding plumbing only.", BufferedFrameFormat.Jpeg95Review);
                    Assert.AreEqual(frames.Count, Directory.GetFiles(jpegDirectory, "frame-*.jpg").Length);
                    for (int frame = 0; frame < frames.Count; frame++)
                    {
                        byte[] encoded = File.ReadAllBytes(Path.Combine(jpegDirectory, "frame-" + frame.ToString("D3", CultureInfo.InvariantCulture) + ".jpg"));
                        Assert.AreEqual(0xff, encoded[0]); Assert.AreEqual(0xd8, encoded[1], "The extension must identify actual JPEG bytes.");
                        Assert.IsTrue(ImageConversion.LoadImage(decoded, encoded));
                        Assert.AreEqual(frames.Width, decoded.width); Assert.AreEqual(frames.Height, decoded.height);
                        // This only checks retained-frame identity, never lossless pixel equality for JPEG.
                        Assert.Greater(frame == 0 ? decoded.GetPixel(4, 4).r : decoded.GetPixel(4, 4).b, .90f);
                        for (int y = 0; y < frames.Height; y++)
                            for (int x = 0; x < frames.Width; x++)
                                Assert.AreEqual(retained[(frame * frames.Height + y) * frames.Width + x], frames.ReadPixel(frame, x, y), "JPEG export must never mutate the raw pixel oracle.");
                    }
                    Assert.AreEqual(File.ReadAllText(Path.Combine(directory, "acquisition.csv")), File.ReadAllText(Path.Combine(jpegDirectory, "acquisition.csv")), "Format changes cannot retime or drop captured frames.");
                    Assert.AreEqual(File.ReadAllText(Path.Combine(directory, "acquisition.ffconcat")).Replace(".png", ".jpg"), File.ReadAllText(Path.Combine(jpegDirectory, "acquisition.ffconcat")), "All intervals and the explicit final hold must remain identical.");
                    var jpegMetadata = JsonUtility.FromJson<CaptureMetadata>(File.ReadAllText(Path.Combine(jpegDirectory, "capture.json")));
                    Assert.AreEqual(1, jpegMetadata.version); Assert.AreEqual("jpeg", jpegMetadata.format);
                    Assert.AreEqual(".jpg", jpegMetadata.filename_extension); Assert.AreEqual(95, jpegMetadata.jpeg_quality);
                    Assert.IsTrue(jpegMetadata.lossy); Assert.IsFalse(jpegMetadata.alpha_preserved);
                    Assert.AreEqual(frames.Count, jpegMetadata.frame_count); Assert.AreEqual(frames.BufferBytes, jpegMetadata.raw_buffer_bytes);
                    Assert.AreEqual(frames.Width, jpegMetadata.width); Assert.AreEqual(frames.Height, jpegMetadata.height);
                    Assert.AreEqual("acquisition.csv", jpegMetadata.timestamp_source);
                    StringAssert.Contains("Not a lossless pixel oracle", jpegMetadata.compression_scope);
                    StringAssert.Contains("not a lossless pixel oracle", File.ReadAllText(Path.Combine(jpegDirectory, "README.txt")));
                    Assert.Throws<ArgumentOutOfRangeException>(() => frames.Write("invalid-format", "invalid format", (BufferedFrameFormat)42));
                    Assert.Throws<ArgumentException>(() => frames.Write("../invalid", "invalid path"));
                }
            }
            finally { RenderTexture.active = prior; target.Release(); Object.Destroy(target); Object.Destroy(decoded); }
        }

        [Test]
        public void RawBufferLimitIsEnforcedBeforeAllocatingFrames()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Actual render target required.");
            var target = new RenderTexture(512, 512, 0, RenderTextureFormat.ARGB32); target.Create();
            try
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => new BufferedFrameCapture(target, 129));
                Assert.Throws<ArgumentOutOfRangeException>(() => new BufferedFrameCapture(target, 1));
            }
            finally { target.Release(); Object.Destroy(target); }
        }
    }
}
#endif
