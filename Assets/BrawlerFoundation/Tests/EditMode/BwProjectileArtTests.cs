using NUnit.Framework;
using BrawlerFoundation.Presentation;
using System.IO;
using System.Reflection;
using SPF.L2.Weapons;
using SPF.Contracts;
using SPF.Presentation;
using SPF.Presentation.Sprites;
using Unity.Mathematics;
using UnityEngine;

namespace BrawlerFoundation.Tests
{
    public class BwProjectileArtTests
    {
        [TestCase(RenderTier.GpuDriven)] [TestCase(RenderTier.DataTexture)]
        public void ActualProjectileBatchAlignsArrowTipToProjectedCollisionBoundaryWithoutWritingState(RenderTier tier)
        {
            using var rig = new BwRig(); using var art = BwArt.Build(rig, true, true);
            using var batch = new SpriteBatch(tier, art.Sheet.Texture, BlendKind.Translucent, 32);
            var go = new GameObject("Belt projectile pivot test"); go.SetActive(false); var renderer = go.AddComponent<BwRenderer>();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var artField = typeof(BwRenderer).GetField("m_Art", flags); var batchField = typeof(BwRenderer).GetField("m_Effects", flags);
            artField.SetValue(renderer, art); batchField.SetValue(renderer, batch);
            var draw = typeof(BwRenderer).GetMethod("DrawWeaponProjectiles", flags);
            float maximumError = 0;
            try
            {
                foreach (int id in new[] { WeaponProfiles.Bow, WeaponProfiles.Staff })
                {
                    using var weapons = new WeaponRuntime(WeaponProfiles.CreateDefaults(60), 60);
                    weapons.Owner = new EntityHandle(1, 1); weapons.RequestEquip(id);
                    for (int tick = 0; tick < weapons.Profile(id).EquipTicks; tick++) weapons.Step(true, true, false, false, false, new float2(1, 0), 0, 0, .9f);
                    weapons.Step(true, true, false, true, false, new float2(1, 0), 0, 0, .9f);
                    for (int tick = 0; tick <= weapons.Profile(id).ReleaseTick; tick++) weapons.Step(true, true, false, false, false, new float2(1, 0), 0, 0, .9f);
                    Assert.That(weapons.Projectiles[0].Active, Is.True);
                    for (int direction = 0; direction < 16; direction++) foreach (float scale in new[] { .4f, .9f, 1.3f })
                    {
                        float angle = direction * math.PI / 8; var shot = weapons.Projectiles[0];
                        shot.Direction = new float2(math.cos(angle), math.sin(angle)); shot.Scale = scale; shot.Height = 1.7f;
                        shot.Previous = new float2(-1.3f, .8f); shot.Position = shot.Previous + shot.Direction * .5f; weapons.Projectiles[0] = shot;
                        byte[] before = Snapshot(weapons);
                        foreach (float alpha in new[] { 0f, .35f, 1f })
                        {
                            batch.Clear(); draw.Invoke(renderer, new object[] { weapons, alpha }); Assert.That(batch.Count, Is.EqualTo(1));
                            var sprite = batch.Instances[0]; var ground = math.lerp(shot.Previous, shot.Position, alpha);
                            float2 expected = BwBeltRules.Project(ground, shot.Height);
                            if (id == WeaponProfiles.Bow)
                            {
                                expected = BwBeltRules.Project(ground + shot.Direction * (weapons.Profile(id).Radius * scale), shot.Height);
                                var actualTip = sprite.Center + new float2(math.cos(sprite.Rotation), math.sin(sprite.Rotation)) * (sprite.Size.x * (60f / 64 - .5f));
                                maximumError = math.max(maximumError, math.distance(actualTip, expected));
                                Assert.That(math.distance(actualTip, expected), Is.LessThan(.0005f), "The projected forward boundary uses ground direction/radius, including depth foreshortening.");
                            }
                            else Assert.That(math.distance(sprite.Center, expected), Is.LessThan(1e-6f), "Spell core stays centred.");
                            CollectionAssert.AreEqual(before, Snapshot(weapons));
                        }
                    }
                }
            }
            finally { artField.SetValue(renderer, null); batchField.SetValue(renderer, null); Object.DestroyImmediate(go); }
            TestContext.WriteLine("Belt arrow authored-tip maximum packed error=" + maximumError + "; tier=" + tier);
        }
        static byte[] Snapshot(WeaponRuntime weapons)
        {
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
            weapons.WriteSnapshot(writer); writer.Flush(); return stream.ToArray();
        }
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
