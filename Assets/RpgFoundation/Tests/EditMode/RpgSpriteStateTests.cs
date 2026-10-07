using NUnit.Framework;
using RpgFoundation.Presentation;
using Unity.Mathematics;
using UnityEngine;

namespace RpgFoundation.Tests
{
    public class RpgSpriteStateTests
    {
        [Test]
        public void AtlasBudget()
        {
            using var art = RpgArt.Build(6, k => Color.white);
            TestContext.WriteLine($"RPG atlas {art.Sheet.Size}, {art.Sheet.Count} frames; baseline 1024x256, 228 frames");
            Assert.AreEqual(new int2(1024, 256), art.Sheet.Size, "35 new frames fit the existing RGBA32 MiB");
            Assert.AreEqual(263, art.Sheet.Count);
            foreach (var monster in art.Monsters)
            {
                Assert.AreEqual(4, monster.Clip(CharacterClip.Run).Count);
                Assert.AreNotEqual(monster.Clip(CharacterClip.Run).First, monster.Clip(CharacterClip.Walk).First);
            }
        }

        [Test]
        public void ActorGenerationResetsClocksAndPausePreservesThem()
        {
            using var art = RpgArt.Build(0, k => Color.white);
            var animation = new RpgActorAnimation();
            var action = new CombatState();
            animation.Sample(4, art.Hero, action, SkillKind.None, false, 2f, 5f, 0.2f);
            Assert.AreEqual(CharacterClip.Walk, animation.Clip);
            float phase = animation.StridePhase;
            animation.Sample(4, art.Hero, action, SkillKind.None, false, 5f, 5f, 0.01f);
            Assert.AreEqual(CharacterClip.Run, animation.Clip);
            Assert.AreEqual(math.frac(phase + 0.03f), animation.StridePhase, 1e-5f);
            phase = animation.StridePhase;
            int frame = animation.Frame;
            for (int i = 0; i < 10; i++) animation.Sample(4, art.Hero, action, SkillKind.None, false, 5f, 5f, 0f);
            Assert.AreEqual(phase, animation.StridePhase);
            Assert.AreEqual(frame, animation.Frame);
            animation.Sample(5, art.Hero, action, SkillKind.None, false, 0f, 5f, 0f);
            Assert.AreEqual(5, animation.Generation);
            Assert.AreEqual(0f, animation.Time);
            Assert.AreEqual(0f, animation.StridePhase);
            Assert.AreEqual(CharacterClip.Idle, animation.Clip);
            animation.Sample(5, art.Hero, action, SkillKind.None, true, 0f, 5f, 0.3f);
            frame = animation.Frame;
            animation.Sample(5, art.Hero, action, SkillKind.None, true, 0f, 5f, 0f);
            Assert.AreEqual(frame, animation.Frame, "death clock also freezes");
        }

        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        [TestCase(float.NaN)]
        [TestCase(-1f)]
        public void InvalidDeathDeltaDoesNotPoisonTheFrameOrClock(float invalidDelta)
        {
            using var art = RpgArt.Build(0, k => Color.white);
            var animation = new RpgActorAnimation();
            animation.Sample(1, art.Hero, default, SkillKind.None, true, 0f, 5f, 0.1f);
            int frame = animation.Frame;
            float time = animation.Time;
            animation.Sample(1, art.Hero, default, SkillKind.None, true, 0f, 5f, invalidDelta);
            Assert.AreEqual(frame, animation.Frame);
            Assert.AreEqual(time, animation.Time);
            animation.Sample(1, art.Hero, default, SkillKind.None, true, 0f, 5f, 0.1f);
            Assert.AreEqual(art.Hero.Clip(CharacterClip.Death).First + 2, animation.Frame,
                "valid deltas still advance the death clip after invalid input");
        }

        [Test]
        public void HitAndDeathOverrideActionsWhileWindupAndRecoveryShareTheAttackStrip()
        {
            using var art = RpgArt.Build(0, k => Color.white);
            var animation = new RpgActorAnimation();
            var action = new CombatState { Phase = ActionPhase.Windup, PhaseTime = 0.9f, PhaseDuration = 1f };
            animation.Sample(1, art.Hero, action, SkillKind.None, false, 5f, 5f, 0.01f);
            Assert.AreEqual(CharacterClip.Attack, animation.Clip);
            Assert.AreEqual(art.Hero.Clip(CharacterClip.Attack).First + 1, animation.Frame);
            action.Phase = ActionPhase.Recover; action.PhaseTime = 0.1f;
            animation.Sample(1, art.Hero, action, SkillKind.None, false, 5f, 5f, 0.01f);
            Assert.AreEqual(art.Hero.Clip(CharacterClip.Attack).First + 2, animation.Frame);
            action.HitFlash = 0.12f;
            animation.Sample(1, art.Hero, action, SkillKind.None, false, 5f, 5f, 0.01f);
            Assert.AreEqual(CharacterClip.Hit, animation.Clip);
            animation.Sample(1, art.Hero, action, SkillKind.None, true, 5f, 5f, 0.01f);
            Assert.AreEqual(CharacterClip.Death, animation.Clip);
        }

        [TestCase(SkillKind.Projectile, CharacterClip.Cast)]
        [TestCase(SkillKind.Nova, CharacterClip.AreaCast)]
        [TestCase(SkillKind.Slam, CharacterClip.SlamCast)]
        [TestCase(SkillKind.Whirlwind, CharacterClip.Channel)]
        public void SkillsUseTheirAuthoredPoseAndSimulationProgress(SkillKind skill, CharacterClip expected)
        {
            using var art = RpgArt.Build(4, k => Color.white);
            var character = skill == SkillKind.Slam ? art.Monsters[3] : art.Hero;
            var animation = new RpgActorAnimation();
            var action = new CombatState { Phase = skill == SkillKind.Whirlwind ? ActionPhase.Channel : ActionPhase.Cast,
                PhaseTime = 0.14f, PhaseDuration = 0.3f };
            animation.Sample(1, character, action, skill, false, 0f, 5f, 0.016f);
            Assert.AreEqual(expected, animation.Clip);
            int expectedFrame = skill == SkillKind.Whirlwind ? character.Clip(expected).FrameAt(action.PhaseTime)
                : character.Clip(expected).FrameAtProgress(action.PhaseProgress);
            Assert.AreEqual(expectedFrame, animation.Frame);
            int frame = animation.Frame;
            animation.Sample(1, character, action, skill, false, 0f, 5f, 0f);
            Assert.AreEqual(frame, animation.Frame);
        }

        [Test]
        public void WeaponGripTracksAuthoredRunAndCastHands()
        {
            using var art = RpgArt.Build(0, k => Color.white);
            var run = art.Hero.Clip(CharacterClip.Run);
            var cast = art.Hero.Clip(CharacterClip.Cast);
            Assert.AreNotEqual(art.Hero.HandAt(CharacterClip.Run, run.First), art.Hero.HandAt(CharacterClip.Run, run.First + 2));
            Assert.Greater(art.Hero.HandAt(CharacterClip.Cast, cast.First + 2).x, art.Hero.HandAt(CharacterClip.Cast, cast.First).x,
                "projectile cast extends its weapon hand forwards");
            Assert.Greater(art.Hero.HandAt(CharacterClip.AreaCast, art.Hero.Clip(CharacterClip.AreaCast).First + 2).y,
                art.Hero.HandAt(CharacterClip.Cast, cast.First + 2).y, "nova raises the hand above projectile cast");
        }

#if !SPF_DOTNET_HARNESS
        [Test]
        public void HeroRunAndSkillStripsContainDifferentPixels()
        {
            using var art = RpgArt.Build(6, k => Color.white);
            var sheet = art.Sheet;
            var pixels = sheet.Texture.GetPixels32();
            foreach (var character in new[] { art.Hero, art.Monsters[0], art.Monsters[1], art.Monsters[2], art.Monsters[3], art.Monsters[4], art.Monsters[5] })
            {
                var run = character.Clip(CharacterClip.Run);
                for (int a = 0; a < run.Count; a++)
                    for (int b = a + 1; b < run.Count; b++) AssertDifferent(run.First + a, run.First + b);
                AssertDifferent(character.Clip(CharacterClip.Walk).First, run.First);
            }
            AssertDifferent(art.Hero.Clip(CharacterClip.Cast).First + 1, art.Hero.Clip(CharacterClip.AreaCast).First + 1);
            AssertDifferent(art.Hero.Clip(CharacterClip.Cast).First + 1, art.Hero.Clip(CharacterClip.Channel).First + 1);
            void AssertDifferent(int a, int b)
            {
                var sa = sheet[a].Pixels;
                var oa = sheet.Origins[a]; var ob = sheet.Origins[b];
                for (int y = 0; y < sa.y; y++)
                    for (int x = 0; x < sa.x; x++)
                        if (!pixels[(oa.y + y) * sheet.Size.x + oa.x + x].Equals(pixels[(ob.y + y) * sheet.Size.x + ob.x + x])) return;
                Assert.Fail($"Frames {a} and {b} repeat identical pixels");
            }
        }
#endif
    }
}
