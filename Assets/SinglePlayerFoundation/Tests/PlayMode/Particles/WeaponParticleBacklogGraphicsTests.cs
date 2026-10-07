#if !SPF_DOTNET_HARNESS
using NUnit.Framework;
using SPF.Contracts;
using SPF.Contracts.Weapons;
using SPF.Presentation;
using SPF.Presentation.Particles;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace SPF.Particles.Tests.PlayMode
{
    public class WeaponParticleBacklogGraphicsTests
    {
        [TestCase(false)] [TestCase(true)]
        public void ActualBackendKeepsRetainedEventsSeparateFromNewCharge(bool forceCpu)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Particle backlog graphics unverified: no graphics device.");
            using var presenter = new WeaponParticlePresenter(RenderTier.GpuDriven, forceCpu: forceCpu);
            if (!forceCpu && presenter.Renderer.Backend != ParticleBackend.GpuCompute) Assert.Ignore("Particle backlog compute unverified: capability gate selected fallback.");
            var owner = new EntityHandle(3, 1); var view = new float4(-4, -4, 4, 4);
            var state = new WeaponViewState { ContentId = 1003, VisualId = 1003, Family = WeaponActionFamily.Cast,
                Stage = WeaponStage.Windup, ReleasePhase = .6f, ActionPulse = 1 };
            var output = new ParticleState[presenter.Renderer.Pool.Capacity];
            void Frame(float dt)
            {
                presenter.BeginFrame(dt, view);
                presenter.UpdateEmitter(owner, state, new float2(2, 1), new float2(2, 0), new float2(1, 0));
            }
            void Finish() => presenter.EndFrame(new Bounds(Vector3.zero, new Vector3(8, 8, 10)), 30);
            int ReadAttached()
            {
                if (forceCpu) presenter.Renderer.Pool.CpuStates.CopyTo(output); else presenter.Renderer.ReadbackForValidation(output);
                int count = 0; foreach (var p in output) if (p.Alive && p.Attachment.x != 0) count++;
                return count;
            }
            for (int i = 0; i < 3; i++) { Frame(.05f); Finish(); }
            Assert.That(ReadAttached(), Is.GreaterThan(0));
            state.ActionPulse = 2; Frame(0); Finish(); Assert.That(ReadAttached(), Is.Zero);
            for (int i = 0; i < 3; i++) { Frame(.05f); Finish(); }
            int charge = ReadAttached(); Assert.That(charge, Is.GreaterThan(0));
            Frame(0);
            var cue = new WeaponCue { Owner = owner, ContentId = 1003, VisualId = 1003, ActionPulse = 1,
                Sequence = 1, Kind = WeaponCueKind.Cancel, Direction = new float2(1, 0), Position = new float2(-2, 1) };
            presenter.SubmitCue(cue, family: WeaponActionFamily.Cast);
            cue.Sequence = 2; cue.Kind = WeaponCueKind.Release; presenter.SubmitCue(cue, family: WeaponActionFamily.Cast);
            Finish(); Assert.That(ReadAttached(), Is.EqualTo(charge));
            int detached = 0;
            foreach (var p in output) if (p.Alive && p.Attachment.x == 0)
            { detached++; Assert.That(math.distance(p.PositionAge.xy, cue.Position), Is.LessThan(1e-5f)); }
            Assert.That(detached, Is.EqualTo(15));
            Frame(0); cue.Sequence = 3; cue.ActionPulse = 2; cue.Kind = WeaponCueKind.Cancel;
            presenter.SubmitCue(cue); Finish(); Assert.That(ReadAttached(), Is.Zero);
            presenter.Clear(); ReadAttached(); foreach (var p in output) Assert.That(p.Alive, Is.False);
        }
    }
}
#endif
