using NUnit.Framework;
using BrawlerFoundation.Presentation;

namespace BrawlerFoundation.Tests
{
    public class BwProjectileArtTests
    {
        [TestCase(false)] [TestCase(true)]
        public void ProjectileAtlasIsExplicitAndAddsOnlyThreeSprites(bool smooth)
        {
            using var rig = new BwRig();
            using var baseline = BwArt.Build(rig, smooth);
            using var disabled = BwArt.Build(rig, smooth, false);
            using var equipped = BwArt.Build(rig, smooth, true);
            Assert.That(disabled.Sheet.Size, Is.EqualTo(baseline.Sheet.Size));
            Assert.That(disabled.Sheet.Count, Is.EqualTo(baseline.Sheet.Count));
            for (int i = 0; i < baseline.Sheet.Count; i++) Assert.That(disabled.Sheet[i].Uv, Is.EqualTo(baseline.Sheet[i].Uv));
            Assert.That(equipped.Sheet.Count, Is.EqualTo(baseline.Sheet.Count + 3));
            Assert.That(equipped.WeaponProjectiles.Arrow, Is.GreaterThanOrEqualTo(baseline.Sheet.Count));
            Assert.That(equipped.Sheet.Size.x * equipped.Sheet.Size.y, Is.LessThanOrEqualTo(baseline.Sheet.Size.x * baseline.Sheet.Size.y * 2));
            TestContext.WriteLine("Brawler projectile atlas smooth=" + smooth + ": baseline=" + baseline.Sheet.Size + ", equipped=" + equipped.Sheet.Size);
        }
    }
}
