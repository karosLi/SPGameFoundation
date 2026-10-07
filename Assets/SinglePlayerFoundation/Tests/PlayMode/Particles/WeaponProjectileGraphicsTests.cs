#if !SPF_DOTNET_HARNESS
using System.Collections;
using System.IO;
using NUnit.Framework;
using SPF.Presentation;
using SPF.Presentation.Combat;
using SPF.Presentation.Sprites;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace SPF.Particles.Tests.PlayMode
{
    public class WeaponProjectileGraphicsTests
    {
        [UnityTest]
        public IEnumerator ArrowTipPivotMatchesForwardBoundaryPixels([Values(false, true)] bool fallback)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Arrow pivot pixels unverified: no graphics device.");
            if (!fallback && RenderCapabilities.Detect() != RenderTier.GpuDriven) Assert.Ignore("Arrow pivot structured draw unverified: graphics capability selected fallback.");
            var builder = new SpriteAtlasBuilder(); var art = WeaponProjectileArt.AddTo(builder);
            using var sheet = builder.Build(128, FilterMode.Bilinear, 2, true);
            using var batch = new SpriteBatch(fallback ? RenderTier.DataTexture : RenderTier.GpuDriven, sheet.Texture, BlendKind.Translucent, 2);
            batch.Warmup(2);
            var go = new GameObject("Arrow collision-boundary pixel validation"); var camera = go.AddComponent<Camera>();
            camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 1.5f; camera.aspect = 2;
            camera.transform.position = new Vector3(0, 0, -10); camera.cullingMask = 1 << 30;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
            var target = new RenderTexture(512, 256, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { antiAliasing = 1 };
            target.Create(); camera.targetTexture = target;
            var read = new Texture2D(512, 256, TextureFormat.RGBA32, false, true);
            try
            {
                for (int facing = -1; facing <= 1; facing += 2)
                {
                    var direction = new float2(facing, 0); var boundary = new float2(facing * .09f, facing * .5f);
                    batch.Add(WeaponProjectileArt.ArrowCentre(boundary, direction, .66f), new float2(.66f, .22f), sheet[art.Arrow].Uv, 0, new float4(1), facing > 0 ? 0 : math.PI);
                }
                yield return null;
                batch.Draw(new Bounds(Vector3.zero, new Vector3(6, 3, 10)), 30); camera.Render();
                var previous = RenderTexture.active;
                try { RenderTexture.active = target; read.ReadPixels(new Rect(0, 0, 512, 256), 0, 0, false); read.Apply(false, false); }
                finally { RenderTexture.active = previous; }
                var pixels = read.GetPixels32();
                for (int facing = -1; facing <= 1; facing += 2)
                {
                    int min = 512, max = -1;
                    for (int y = facing > 0 ? 128 : 0; y < (facing > 0 ? 256 : 128); y++) for (int x = 0; x < 512; x++)
                        if (pixels[y * 512 + x].a > 2) { min = math.min(min, x); max = math.max(max, x); }
                    Assert.That(max, Is.GreaterThan(min));
                    float boundaryPixel = 256 + facing * .09f * 512 / 6;
                    float leadingPixel = (facing > 0 ? max : min) + .5f;
                    Assert.That(math.abs(leadingPixel - boundaryPixel), Is.LessThanOrEqualTo(2), "Bilinear/antialias coverage must end at the authored tip's collision boundary within two pixels.");
                }
                Assert.That(batch.Count, Is.EqualTo(2));
                string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Artifacts/Screenshots/Weapons"));
                Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory, "arrow-tip-pivot-" + (fallback ? "fallback" : "gpu") + ".png"), read.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null; Object.DestroyImmediate(go); Object.DestroyImmediate(read);
                target.Release(); Object.DestroyImmediate(target);
            }
        }
        [UnityTest]
        public IEnumerator ProjectileCatalogUsesOneBatchAndVisibleNonRectangularPixels([Values(false, true)] bool fallback)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Projectile graphics unverified: no graphics device.");
            if (!fallback && RenderCapabilities.Detect() != RenderTier.GpuDriven) Assert.Ignore("Projectile structured draw unverified: graphics capability selected fallback.");
            var builder = new SpriteAtlasBuilder(); var art = WeaponProjectileArt.AddTo(builder);
            using var sheet = builder.Build(128, FilterMode.Bilinear, 2, true);
            using var batch = new SpriteBatch(fallback ? RenderTier.DataTexture : RenderTier.GpuDriven, sheet.Texture, BlendKind.Translucent, 3);
            batch.Warmup(3);
            var go = new GameObject("Projectile silhouette validation"); var camera = go.AddComponent<Camera>();
            camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 2;
            camera.transform.position = new Vector3(0, 0, -10); camera.cullingMask = 1 << 30;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
            var target = new RenderTexture(512, 256, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { antiAliasing = 1 };
            target.Create(); camera.targetTexture = target;
            var read = new Texture2D(512, 256, TextureFormat.RGBA32, false, true);
            try
            {
                batch.Add(new float2(-2, 0), new float2(1, .7f), sheet[art.Bullet].Uv, 0, new float4(1));
                batch.Add(float2.zero, new float2(1.8f, .8f), sheet[art.Arrow].Uv, 0, new float4(1));
                batch.Add(new float2(2, 0), new float2(1), sheet[art.Spell].Uv, 0, new float4(1));
                yield return null;
                batch.Draw(new Bounds(Vector3.zero, new Vector3(8, 4, 10)), 30); camera.Render();
                var previous = RenderTexture.active;
                try { RenderTexture.active = target; read.ReadPixels(new Rect(0, 0, 512, 256), 0, 0, false); read.Apply(false, false); }
                finally { RenderTexture.active = previous; }
                var pixels = read.GetPixels32(); int magenta = 0;
                for (int shape = 0; shape < 3; shape++)
                {
                    int centre = 128 + 128 * shape, occupied = 0;
                    for (int y = 88; y < 168; y++) for (int x = centre - 60; x < centre + 60; x++)
                    {
                        var p = pixels[y * 512 + x]; if (p.a > 2) occupied++;
                        if (p.r > 240 && p.b > 240 && p.g < 15) magenta++;
                    }
                    Assert.That(occupied, Is.GreaterThan(70), "Every catalog silhouette must actually render.");
                    Assert.That(occupied, Is.LessThan(4800), "Transparent mask must not become a solid rectangle.");
                }
                Assert.That(magenta, Is.Zero); Assert.That(batch.Count, Is.EqualTo(3));
                string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Artifacts/Screenshots/Weapons"));
                Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory, "projectile-catalog-" + (fallback ? "fallback" : "gpu") + ".png"), read.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null; Object.DestroyImmediate(go); Object.DestroyImmediate(read);
                target.Release(); Object.DestroyImmediate(target);
            }
        }
    }
}
#endif
