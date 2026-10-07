using NUnit.Framework;
using SurvivorFoundation.Presentation;
using UnityEngine;

namespace SurvivorFoundation.Tests
{
    public class SvProjectileArtTests
    {
        [TestCase(SvArtStyle.Pixel)] [TestCase(SvArtStyle.SmoothOutline)]
        public void ProjectileAtlasIsExplicitAndAddsOnlyThreeSprites(SvArtStyle style)
        {
            using var baseline = SvArt.Build(4, _ => Color.white, style);
            using var disabled = SvArt.Build(4, _ => Color.white, style, false);
            using var equipped = SvArt.Build(4, _ => Color.white, style, true);
            Assert.That(disabled.Sheet.Size, Is.EqualTo(baseline.Sheet.Size));
            Assert.That(disabled.Sheet.Count, Is.EqualTo(baseline.Sheet.Count));
            for (int i = 0; i < baseline.Sheet.Count; i++) Assert.That(disabled.Sheet[i].Uv, Is.EqualTo(baseline.Sheet[i].Uv));
            Assert.That(equipped.Sheet.Count, Is.EqualTo(baseline.Sheet.Count + 3));
            Assert.That(equipped.WeaponProjectiles.Spell, Is.GreaterThanOrEqualTo(baseline.Sheet.Count));
            TestContext.WriteLine("Survivor projectile atlas style=" + style + ": baseline=" + baseline.Sheet.Size + ", equipped=" + equipped.Sheet.Size);
        }
    }
}
