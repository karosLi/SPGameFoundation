using System;
using System.Reflection;
using BrawlerFoundation.Presentation;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Presentation;
using SPF.Presentation.Animation;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BrawlerFoundation.Tests
{
    public class BwNaturalAdapterTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        sealed class View : IDisposable
        {
            readonly GameObject m_Object;
            readonly RenderTier? m_Tier;
            public readonly BwRenderer Renderer;
            public View(BwTestWorld t)
            {
                m_Tier = RenderCapabilities.Override; RenderCapabilities.Override = RenderTier.DataTexture;
                m_Object = new GameObject("Belt adapter regression");
                Renderer = m_Object.AddComponent<BwRenderer>(); Renderer.NaturalCharacters = true;
                Call("Bind", t.Session);
            }
            void Call(string name, params object[] args) => typeof(BwRenderer).GetMethod(name, Private).Invoke(Renderer, args);
            public GameplayCharacterInput Draw(BwTestWorld t, float alpha = 1)
            {
                Call("DrawNatural", t.World, t.Game, t.World.Resource(BwKeys.Rig), alpha, t.Count);
                var inputs = (NativeArray<GameplayCharacterInput>)typeof(GameplayCharacterPresenter).GetField("m_Inputs", Private).GetValue(Renderer.Characters);
                var handle = t.World.Table(BwKeys.Fighter).Handles[0];
                for (int i = 0; i < Renderer.Characters.Count; i++) if (inputs[i].Handle == handle) return inputs[i];
                Assert.Fail("Missing player"); return default;
            }
            public void Dispose() { Call("Release"); Object.DestroyImmediate(m_Object); RenderCapabilities.Override = m_Tier; }
        }
        static void Place(BwTestWorld t, float2 hero, float2 enemy)
        {
            t.World.ClearLevel(); BwSpawner.Spawn(t.World, 0, hero, 1, 0); BwSpawner.Spawn(t.World, 1, enemy, -1, 0);
            t.Game.Flow = BwFlow.Fighting;
        }

        [TestCase(1f, 0f)] [TestCase(-1f, 0f)]
        [TestCase(0f, 1f)] [TestCase(0f, -1f)]
        public void HoldingOutwardAtArenaBoundarySettlesToIdle(float x, float y)
        {
            using var t = new BwTestWorld(belt: BwBeltConfig.Default);
            var direction = new float2(x, y);
            var boundary = direction * new float2(BwRules.ArenaHalf, BwBeltRules.DepthHalf);
            Place(t, boundary - direction * .12f, -boundary);
            using var view = new View(t); var motion = default(GameplayCharacterMotion);
            t.Game.Input = new InputFrame { Move = direction };
            bool traveled = false;
            for (int tick = 0; tick < 90; tick++)
            {
                t.Step(); var input = view.Draw(t); motion.Step(input, 1f / 60);
                traveled |= math.lengthsq(input.Velocity) > .01f;
            }
            Assert.IsTrue(traveled); Assert.AreEqual(boundary, t.World.Column(BwBeltKeys.Ground)[0]);
            Assert.Greater(math.length(t.World.Column(BwBeltKeys.Motion)[0].GroundVelocity), 1f, "fixture keeps commanding outward motion");
            Assert.AreEqual(float2.zero, view.Draw(t).Velocity, "clamped travel has no foot velocity");
            Assert.AreEqual(GameplayLocomotionState.Idle, motion.Locomotion); Assert.IsFalse(motion.Moving);
            Assert.Less(motion.Move, .001f); Assert.IsTrue(motion.FarFoot.InStance); Assert.IsTrue(motion.NearFoot.InStance);
            var far = motion.FarFoot.Position; var near = motion.NearFoot.Position;
            for (int tick = 0; tick < 12; tick++) { t.Step(); motion.Step(view.Draw(t), 1f / 60); }
            Assert.AreEqual(far, motion.FarFoot.Position); Assert.AreEqual(near, motion.NearFoot.Position);
        }

        [Test]
        public void SeparationUsesFinalProjectedGroundTravelAndPauseKeepsTheSamePair()
        {
            using var t = new BwTestWorld(belt: BwBeltConfig.Default);
            Place(t, float2.zero, new float2(.1f, .1f)); using var view = new View(t);
            view.Draw(t); t.Step(); var input = view.Draw(t);
            Assert.AreEqual(float2.zero, t.World.Column(BwBeltKeys.Motion)[0].GroundVelocity, "no movement command or knockback");
            var displacement = t.World.Column(BwBeltKeys.Ground)[0] - t.World.Column(BwBeltKeys.PreviousGround)[0];
            var expected = BwBeltRules.Project(displacement / (float)t.Session.Clock.StepSeconds, 0);
            Assert.Greater(math.length(expected), .1f); Assert.AreEqual(expected, input.Velocity);
            var sampled = view.Draw(t, .5f); Assert.AreEqual(input.Velocity, sampled.Velocity, "interpolation does not rescale simulation-pair velocity");
            // Seed the real presenter at a deterministic frame delta even in an EditMode runner.
            var presenter = view.Renderer.Characters;
            presenter.Begin(1f / 60, 0); presenter.Submit(input); presenter.Evaluate();
            Assert.IsTrue(presenter.TryRead(input.Handle, out var moving)); Assert.IsTrue(moving.Moving);
            t.Session.Pause(); view.Draw(t); presenter.TryRead(input.Handle, out var paused);
            for (int frame = 0; frame < 12; frame++)
            {
                Assert.AreEqual(input.Velocity, view.Draw(t).Velocity); presenter.TryRead(input.Handle, out var held);
                Assert.AreEqual(paused.Phase, held.Phase); Assert.AreEqual(paused.NearFoot.Position, held.NearFoot.Position); Assert.AreEqual(paused.FarFoot.Position, held.FarFoot.Position);
                Assert.AreEqual(paused.NearFoot.Phase, held.NearFoot.Phase); Assert.AreEqual(paused.FarFoot.Phase, held.FarFoot.Phase);
            }
        }
    }
}
