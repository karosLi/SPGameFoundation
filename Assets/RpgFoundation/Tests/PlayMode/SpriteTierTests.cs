using System.Collections;
using System.IO;
using NUnit.Framework;
using RpgFoundation.Presentation;
using SPF.Presentation;
using SPF.Presentation.Sprites;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace RpgFoundation.Tests.PlayMode
{
    /// <summary>
    /// The sprite batch must draw the same image on both tiers (structured buffer vs data texture): a sheet
    /// of characters, weapons (rotated, mirrored), tinted and flashing sprites, rendered by each tier into a
    /// render texture and compared. Both captures go to Artifacts/Screenshots for review.
    /// </summary>
    public class SpriteTierTests
    {
        const int Width = 640, Height = 360;

        [UnityTest]
        public IEnumerator SpritesLookTheSameOnBothTiers()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("No graphics device");
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("GPU tier unavailable");
            var art = RpgArt.Build(4, k => new Color(0.3f + 0.2f * k, 0.8f - 0.15f * k, 0.4f, 1f));
            var cameraObject = new GameObject("SpriteTestCamera", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 4f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.05f, 0.08f);
            camera.transform.position = new Vector3(0f, 0f, -50f);
            camera.nearClipPlane = 1f;
            camera.farClipPlane = 100f;
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = target;
            var read = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            var frames = new Color32[3][];
            try
            {
                // GPU tier, data-texture tier with packed (RGBA32UI) instances, data-texture float fallback.
                for (int pass = 0; pass < 3; pass++)
                {
                    var tier = pass == 0 ? RenderTier.GpuDriven : RenderTier.DataTexture;
                    SpriteBatch.PackedTexturesOverride = pass == 2 ? false : (bool?)null;
                    using var batch = new SpriteBatch(tier, art.Sheet.Texture, BlendKind.Opaque, 512);
                    if (pass == 1)
                    {
                        TestContext.WriteLine($"packed data textures supported: {batch.PackedTextures}");
                        var api = SystemInfo.graphicsDeviceType;
                        if (api == GraphicsDeviceType.Metal || api == GraphicsDeviceType.Vulkan || api == GraphicsDeviceType.Direct3D11 || api == GraphicsDeviceType.Direct3D12)
                            Assert.IsTrue(batch.PackedTextures, "desktop / modern APIs take the packed (RGBA32UI) path");
                    }
                    if (pass == 2) Assert.IsFalse(batch.PackedTextures);
                    for (int f = 0; f < 4; f++)
                    {
                        Fill(batch, art);
                        batch.Draw(new Bounds(Vector3.zero, new Vector3(1000, 1000, 1000)));
                        yield return null;
                    }
                    var previous = RenderTexture.active;
                    RenderTexture.active = target;
                    read.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                    read.Apply(false);
                    RenderTexture.active = previous;
                    frames[pass] = read.GetPixels32();
                    string dir = Path.Combine(Application.dataPath, "..", "Artifacts", "Screenshots");
                    Directory.CreateDirectory(dir);
                    File.WriteAllBytes(Path.Combine(dir, $"sprites-{(pass == 0 ? "gpu" : pass == 1 ? "datatex" : "datatex-float")}.png"), read.EncodeToPNG());
                }

                int lit = 0;
                foreach (var a in frames[0]) if (a.r + a.g + a.b > 60) lit++;
                Assert.Greater(lit, frames[0].Length / 50, "sprites were drawn");
                for (int other = 1; other < 3; other++)
                {
                    int differing = 0;
                    for (int i = 0; i < frames[0].Length; i++)
                    {
                        var a = frames[0][i];
                        var b = frames[other][i];
                        int d = math.max(math.abs(a.r - b.r), math.max(math.abs(a.g - b.g), math.abs(a.b - b.b)));
                        if (d > 24) differing++;
                    }
                    TestContext.WriteLine($"sprites: {lit} lit pixels, {differing} differ from the GPU tier ({(other == 1 ? "packed" : "float")} data texture)");
                    Assert.Less(differing, frames[0].Length / 200, "every path draws the same image");
                }
            }
            finally
            {
                SpriteBatch.PackedTexturesOverride = null;
                camera.targetTexture = null;
                Object.Destroy(cameraObject);
                target.Release();
                Object.Destroy(target);
                Object.Destroy(read);
                art.Dispose();
            }
        }

        static void Fill(SpriteBatch batch, RpgArt art)
        {
            batch.Clear();
            var sheet = art.Sheet;
            int n = 0;
            void Character(CharacterArt c, int clip, int frame)
            {
                var sc = c.Clips[clip];
                float2 p = new float2(-6f + (n % 8) * 1.6f, 2.5f - (n / 8) * 1.8f);
                float side = n % 2 == 0 ? 1f : -1f;
                batch.Add(p, new float2(c.Size.x * side, c.Size.y), sheet[sc.First + frame % sc.Count].Uv, 1f + n * 0.01f, n % 3 == 0 ? new float4(1f) : new float4(1f, 0.8f, 0.8f, 1f), 0f, n % 5 == 0 ? 0.6f : 0f);
                n++;
            }
            for (int clip = 0; clip < 6; clip++) Character(art.Hero, clip, clip);
            foreach (var m in art.Monsters) for (int clip = 0; clip < 6; clip += 2) Character(m, clip, 1);
            for (int w = 0; w < art.Weapons.Length; w++)
            {
                var weapon = art.Weapons[w];
                if (weapon == null) continue;
                batch.Add(new float2(-6f + w * 1.8f, -3f), weapon.Size * 1.5f, sheet[weapon.Frame].Uv, 0.5f, new float4(1f), w * 0.7f);
            }
        }
    }
}
