using NUnit.Framework;
using SPF.Contracts;
using SPF.Contracts.Weapons;
using SPF.Presentation;
using SPF.Presentation.Particles;
using Unity.Mathematics;
using UnityEngine;

namespace SPF.Tests.EditMode
{
    public class WeaponParticleTransitionTests
    {
        static readonly EntityHandle Owner = new EntityHandle(3, 1);
        static readonly float4 View = new float4(-8, -5, 8, 5);
        static WeaponViewState Charge(uint pulse) => new WeaponViewState
        {
            ContentId = 1003, VisualId = 1003, Family = WeaponActionFamily.Cast,
            Stage = WeaponStage.Windup, ReleasePhase = .6f, ActionPulse = pulse
        };
        static void Update(WeaponParticlePresenter presenter, in WeaponViewState state)
        {
            presenter.UpdateEmitter(Owner, state, new float2(2, 1), new float2(2, 0), new float2(1, 0));
        }
        static void WarmCharge(WeaponParticlePresenter presenter, uint pulse)
        {
            var state = Charge(pulse);
            for (int i = 0; i < 3; i++)
            {
                presenter.BeginFrame(.05f, View); Update(presenter, state); presenter.EndFrame(new Bounds());
            }
        }
        static int Attached(WeaponParticlePresenter presenter)
        {
            int count = 0;
            foreach (var p in presenter.Renderer.Pool.CpuStates)
                if (p.Alive && p.Attachment.x != 0) count++;
            return count;
        }
        [TestCase(WeaponCueKind.Cancel)] [TestCase(WeaponCueKind.Equip)]
        public void RetainedPreviousActionStopCannotCancelCurrentCharge(WeaponCueKind kind)
        {
            using var presenter = new WeaponParticlePresenter(RenderTier.DataTexture, true, true);
            WarmCharge(presenter, 2);
            presenter.BeginFrame(0, View); Update(presenter, Charge(2));
            presenter.SubmitCue(new WeaponCue { Owner = Owner, Sequence = 1, ActionPulse = 1,
                ContentId = 1003, VisualId = 1003, Kind = kind });
            presenter.EndFrame(new Bounds());
            Assert.That(Attached(presenter), Is.GreaterThan(0), "The renderer can consume old cues after publishing the new action's socket.");
        }
        [Test]
        public void ChargeToNextChargeWithoutObservedReleaseRetiresOldActionParticles()
        {
            using var presenter = new WeaponParticlePresenter(RenderTier.DataTexture, true, true);
            WarmCharge(presenter, 1);
            Assert.That(Attached(presenter), Is.GreaterThan(0));
            presenter.BeginFrame(0, View); Update(presenter, Charge(2)); presenter.EndFrame(new Bounds());
            Assert.That(Attached(presenter), Is.Zero, "A skipped render interval must not carry the preceding pulse's attachment into the next charge.");
            WarmCharge(presenter, 2);
            Assert.That(Attached(presenter), Is.GreaterThan(0), "The new action can emit its own charge normally.");
        }
        [Test]
        public void RetainedReleaseUsesEventPositionInsteadOfCurrentActionMuzzle()
        {
            using var presenter = new WeaponParticlePresenter(RenderTier.DataTexture, true, true);
            presenter.BeginFrame(0, View); Update(presenter, Charge(2));
            var point = new float2(-2, 1);
            presenter.SubmitCue(new WeaponCue { Owner = Owner, Sequence = 1, ActionPulse = 1,
                ContentId = 1003, VisualId = 1003, Kind = WeaponCueKind.Release, Position = point, Direction = new float2(1, 0) });
            var pool = presenter.Renderer.Pool;
            Assert.That(pool.SpawnCount, Is.GreaterThan(0));
            for (int i = 0; i < pool.SpawnCount; i++)
                Assert.That(pool.Spawns[i].State.PositionAge.xy, Is.EqualTo(point), "World-space release origin belongs to the retained event.");
        }
        [Test]
        public void RetainedReleaseKeepsItsProfileAfterEquipAndDoesNotReplayOnPause()
        {
            using var presenter = new WeaponParticlePresenter(RenderTier.DataTexture, true, true);
            var bow = Charge(2); bow.ContentId = bow.VisualId = 1004; bow.Family = WeaponActionFamily.Draw;
            presenter.BeginFrame(0, View); Update(presenter, bow);
            var cue = new WeaponCue { Owner = Owner, Sequence = 1, ActionPulse = 1,
                ContentId = 1003, VisualId = 1003, Kind = WeaponCueKind.Release, Direction = new float2(1, 0) };
            presenter.SubmitCue(cue, family: WeaponActionFamily.Cast);
            Assert.That(presenter.Renderer.Pool.SpawnCount, Is.EqualTo(15), "Retained staff burst keeps its 11 streaks, core and 3 embers after switching to bow.");
            presenter.EndFrame(new Bounds());
            int count = presenter.Renderer.Pool.ReservedCount;
            for (int frame = 0; frame < 5; frame++)
            {
                presenter.BeginFrame(0, View); Update(presenter, bow); presenter.SubmitCue(cue, family: WeaponActionFamily.Cast);
                Assert.That(presenter.Renderer.Pool.SpawnCount, Is.Zero);
                presenter.EndFrame(new Bounds());
                Assert.That(presenter.Renderer.Pool.ReservedCount, Is.EqualTo(count));
            }
        }
        [TestCase(WeaponCueKind.Cancel)] [TestCase(WeaponCueKind.Equip)]
        public void CurrentActionStopStillRetiresAttachedCharge(WeaponCueKind kind)
        {
            using var presenter = new WeaponParticlePresenter(RenderTier.DataTexture, true, true);
            WarmCharge(presenter, 2);
            presenter.BeginFrame(0, View); Update(presenter, Charge(2));
            presenter.SubmitCue(new WeaponCue { Owner = Owner, Sequence = 1, ActionPulse = 2,
                ContentId = 1003, VisualId = 1003, Kind = kind });
            presenter.EndFrame(new Bounds());
            Assert.That(Attached(presenter), Is.Zero);
        }
    }
}
