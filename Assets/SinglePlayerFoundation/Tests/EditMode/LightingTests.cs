using NUnit.Framework;
using SPF.Presentation.Sprites;
using Unity.Mathematics;
using UnityEngine;

namespace SPF.Tests.EditMode
{
    public class LightingTests
    {
        static float3 Decode(Color32 c) => new float3(c.r, c.g, c.b) / 255f * 2f - 1f;

        [Test]
        public void BakedNormalsBevelOutwardAndStayFlatInside()
        {
            // One 12x12 frame: an opaque square with a transparent border of 2 px, at (1, 1) in a 16x16 atlas.
            var size = new int2(16, 16);
            var pixels = new Color32[size.x * size.y];
            for (int y = 3; y < 11; y++)
                for (int x = 3; x < 11; x++)
                    pixels[y * size.x + x] = new Color32(128, 128, 128, 255);
            var settings = NormalMapBaker.Settings.Default;
            settings.Detail = 0f;
            var normals = NormalMapBaker.Bake(pixels, size, new[] { new int2(1, 1) }, new[] { new int2(12, 12) }, settings);

            var centre = Decode(normals[7 * size.x + 7]);
            var left = Decode(normals[7 * size.x + 3]);
            var right = Decode(normals[7 * size.x + 10]);
            var top = Decode(normals[10 * size.x + 7]);
            Assert.Greater(centre.z, 0.95f, "flat plateau faces the viewer");
            Assert.Less(left.x, -0.2f, "left edge leans left");
            Assert.Greater(right.x, 0.2f, "right edge leans right");
            Assert.Greater(top.y, 0.2f, "top edge leans up");
            Assert.AreEqual(new Color32(128, 128, 255, 255), normals[0], "outside any frame: flat");
        }

        [Test]
        public void LightsFallOffWithDistanceAndFavourFacingNormals()
        {
            SpriteLighting.Begin(new Color(0.1f, 0.1f, 0.1f));
            Assert.IsTrue(SpriteLighting.Add(new float2(0f, 0f), 5f, Color.white, 1f, 1f));
            var up = new float3(0f, 0f, 1f);
            float near = SpriteLighting.Evaluate(new float2(0.5f, 0f), up).x;
            float far = SpriteLighting.Evaluate(new float2(4f, 0f), up).x;
            float outside = SpriteLighting.Evaluate(new float2(6f, 0f), up).x;
            Assert.Greater(near, far);
            Assert.AreEqual(0.1f, outside, 1e-5f, "beyond the radius: ambient only");
            float facing = SpriteLighting.Evaluate(new float2(2f, 0f), math.normalize(new float3(-1f, 0f, 1f))).x;
            float away = SpriteLighting.Evaluate(new float2(2f, 0f), math.normalize(new float3(1f, 0f, 1f))).x;
            Assert.Greater(facing, away, "a slope facing the light is brighter");
            for (int i = 1; i < SpriteLighting.MaxLights; i++) SpriteLighting.Add(new float2(i, 0f), 1f, Color.red);
            Assert.IsFalse(SpriteLighting.Add(float2.zero, 1f, Color.red), "fixed light budget");
            SpriteLighting.Apply();
        }
    }
}
