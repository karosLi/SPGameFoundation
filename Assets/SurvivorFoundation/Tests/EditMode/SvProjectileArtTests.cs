using NUnit.Framework;
using SurvivorFoundation.Presentation;
using UnityEngine;
using System.IO;
using System.Reflection;
using SPF.L2.Weapons;
using SPF.Contracts;
using SPF.Presentation;
using SPF.Presentation.Sprites;
using Unity.Mathematics;

namespace SurvivorFoundation.Tests
{
    public class SvProjectileArtTests
    {
        [TestCase(RenderTier.GpuDriven)] [TestCase(RenderTier.DataTexture)]
        public void ActualProjectileBatchAlignsArrowTipToCollisionBoundaryWithoutWritingState(RenderTier tier)
        {
            using var art = SvArt.Build(4, _ => Color.white, SvArtStyle.SmoothOutline, true);
            using var batch = new SpriteBatch(tier, art.Sheet.Texture, BlendKind.Translucent, 32);
            var go = new GameObject("Horde projectile pivot test"); go.SetActive(false); var renderer = go.AddComponent<SvRenderer>();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var artField = typeof(SvRenderer).GetField("m_Art", flags); var batchField = typeof(SvRenderer).GetField("m_Effects", flags);
            artField.SetValue(renderer, art); batchField.SetValue(renderer, batch);
            var draw = typeof(SvRenderer).GetMethod("DrawWeaponProjectiles", flags);
            float maximumError = 0;
            try
            {
                foreach (int id in new[] { WeaponProfiles.Bow, WeaponProfiles.Staff })
                {
                    using var weapons = new WeaponRuntime(WeaponProfiles.CreateDefaults(30), 30);
                    weapons.Owner = new EntityHandle(1, 1); weapons.RequestEquip(id);
                    for (int tick = 0; tick < weapons.Profile(id).EquipTicks; tick++) weapons.Step(true, true, false, false, false, new float2(1, 0), 0, 0, .66f);
                    weapons.Step(true, true, false, true, false, new float2(1, 0), 0, 0, .66f);
                    for (int tick = 0; tick <= weapons.Profile(id).ReleaseTick; tick++) weapons.Step(true, true, false, false, false, new float2(1, 0), 0, 0, .66f);
                    Assert.That(weapons.Projectiles[0].Active, Is.True);
                    for (int direction = 0; direction < 16; direction++) foreach (float scale in new[] { .4f, .66f, 1.3f })
                    {
                        float angle = direction * math.PI / 8; var shot = weapons.Projectiles[0];
                        shot.Direction = new float2(math.cos(angle), math.sin(angle)); shot.Scale = scale; shot.Height = 1.7f;
                        shot.Previous = new float2(-1.3f, .8f); shot.Position = shot.Previous + shot.Direction * .5f; weapons.Projectiles[0] = shot;
                        byte[] before = Snapshot(weapons);
                        foreach (float alpha in new[] { 0f, .35f, 1f })
                        {
                            batch.Clear(); draw.Invoke(renderer, new object[] { weapons, alpha }); Assert.That(batch.Count, Is.EqualTo(1));
                            var sprite = batch.Instances[0]; float2 expected = math.lerp(shot.Previous, shot.Position, alpha) + new float2(0, shot.Height);
                            if (id == WeaponProfiles.Bow)
                            {
                                expected += shot.Direction * (weapons.Profile(id).Radius * scale);
                                var actualTip = sprite.Center + new float2(math.cos(sprite.Rotation), math.sin(sprite.Rotation)) * (sprite.Size.x * (60f / 64 - .5f));
                                maximumError = math.max(maximumError, math.distance(actualTip, expected));
                                Assert.That(math.distance(actualTip, expected), Is.LessThan(.0005f), "Arrow tip tracks the collision boundary, not the centred quad's arbitrary extent.");
                            }
                            else Assert.That(math.distance(sprite.Center, expected), Is.LessThan(1e-6f), "Spell core stays centred.");
                            CollectionAssert.AreEqual(before, Snapshot(weapons));
                        }
                    }
                }
            }
            finally { artField.SetValue(renderer, null); batchField.SetValue(renderer, null); Object.DestroyImmediate(go); }
            TestContext.WriteLine("Horde arrow authored-tip maximum packed error=" + maximumError + "; tier=" + tier);
        }
        static byte[] Snapshot(WeaponRuntime weapons)
        {
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
            weapons.WriteSnapshot(writer); writer.Flush(); return stream.ToArray();
        }
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
