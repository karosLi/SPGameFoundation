using System;
using System.IO;
using NUnit.Framework;
using SPF.Presentation.Combat;
using SPF.Presentation.Sprites;
using Unity.Mathematics;
using UnityEngine;

namespace SPF.Tests.EditMode
{
    public class WeaponProjectileArtTests
    {
        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public void ProjectilesHavePaddedDistinctSilhouettesAndRealAlphaEdges(int kind)
        {
            var canvas = WeaponProjectileArt.Draw(kind);
            Assert.That(canvas.Width, Is.EqualTo(kind == 1 ? 64 : 32));
            Assert.That(canvas.Height, Is.EqualTo(32));
            int opaque = 0, edge = 0;
            for (int y = 0; y < canvas.Height; y++) for (int x = 0; x < canvas.Width; x++)
            {
                var p = canvas.Get(x, y);
                if (p.a > 200) opaque++;
                if (p.a > 0 && p.a < 255) edge++;
                if (x < 2 || y < 2 || x >= canvas.Width - 2 || y >= canvas.Height - 2)
                    Assert.That(p.a, Is.Zero, "A transparent border keeps neighboring atlas art out of the silhouette.");
            }
            Assert.That(opaque, Is.GreaterThan(70));
            Assert.That(opaque, Is.LessThan(canvas.Pixels.Length * .55f), "The projectile must not regress to a solid rectangle.");
            Assert.That(edge, Is.GreaterThan(15));
            if (kind == 1)
            {
                Assert.That(canvas.Get(48, 19).a, Is.GreaterThan(0), "Arrowhead has visible width.");
                Assert.That(canvas.Get(27, 19).a, Is.Zero, "Shaft is narrower than the head.");
                Assert.That(canvas.Get(10, 20).a, Is.GreaterThan(0), "Fletching is distinct from a bar.");
            }
            // Optional source-art preview, not a camera capture or graphical-backend test.
            string output = Environment.GetEnvironmentVariable("SPF_PROJECTILE_ART_PREVIEW");
            if (!string.IsNullOrEmpty(output))
            {
                Directory.CreateDirectory(output);
                using var writer = new BinaryWriter(File.Create(Path.Combine(output, "projectile-" + kind + ".rgba")));
                writer.Write(canvas.Width); writer.Write(canvas.Height);
                foreach (var p in canvas.Pixels) { writer.Write(p.r); writer.Write(p.g); writer.Write(p.b); writer.Write(p.a); }
            }
        }
        [Test]
        public void CatalogAddsOnlyThreeBoundedRecordsToExistingAtlas()
        {
            var atlas = new SpriteAtlasBuilder();
            int existing = atlas.Add(new PixelCanvas(4, 4));
            var art = WeaponProjectileArt.AddTo(atlas);
            using var sheet = atlas.Build(128, FilterMode.Bilinear, 2, true);
            Assert.That(existing, Is.Zero); Assert.That(sheet.Count, Is.EqualTo(4));
            Assert.That(art.Bullet, Is.EqualTo(1)); Assert.That(art.Arrow, Is.EqualTo(2)); Assert.That(art.Spell, Is.EqualTo(3));
            Assert.That(sheet[art.Arrow].Pixels, Is.EqualTo(new int2(64, 32)));
            Assert.That(sheet[art.Spell].Pixels, Is.EqualTo(new int2(32, 32)));
        }
    }
}
