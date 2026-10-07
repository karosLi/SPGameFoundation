#if !SPF_DOTNET_HARNESS
using System;
using System.Collections;
using System.IO;
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
