using System;
using System.IO;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Runtime.Composition;
using SPF.Runtime.Session;
using SPF.Runtime.World;
using Unity.Mathematics;

namespace BrawlerFoundation.Tests
{
    public class BwComposedAbilitySaveTests
    {
        const string Domain = "stage-e-tests.same-runtime";
        sealed class Session : IDisposable
        {
            readonly GameplayModuleAsset module; readonly ModeDefinition mode;
            public readonly SimSession Simulation;
            public SimWorld World => Simulation.World;
            public BwGameState Game => World.Resource(BwKeys.Game);
            public Session(bool credit = true)
            {
                var belt = BwBeltConfig.Default; belt.Fighters = 16; belt.TargetsPerAttack = 8;
                mode = BwMode.CreateComposedAbilityBelt(belt, credit ? BwComposedAbilityConfig.Default : BwComposedAbilityConfig.DataVariant, out module);
                Simulation = SimSession.Create(mode, 7); Simulation.Start(); Game.Send(BwCommandKind.Start); Step(1);
                World.ClearLevel(); BwSpawner.Spawn(World, 0, new float2(0), 1, 0); BwSpawner.Spawn(World, 1, new float2(.9f, 0), -1, 0);
                var player = World.Column(BwKeys.Info)[0]; player.Hp = 50; World.Column(BwKeys.Info).Set(0, player);
                var enemy = World.Column(BwKeys.Info)[1]; enemy.Hp = enemy.MaxHp = 100; World.Column(BwKeys.Info).Set(1, enemy); Game.Flow = BwFlow.Fighting;
            }
            public void Step(int ticks) { for (int i = 0; i < ticks; i++) Simulation.Step(); }
            public void Press(int button) { Game.Input = new InputFrame { Pressed = 1u << button }; Step(1); Game.Input = default; }
            public void Dispose() { Simulation.Dispose(); UnityEngine.Object.DestroyImmediate(mode); UnityEngine.Object.DestroyImmediate(module); }
        }
        [Test]
        public void CompleteRecipePreservesEarnedCreditAndInFlightHealAgainstUninterruptedControl()
        {
            using var control = new Session(); control.Press(BwButton.Kick); control.Step(32);
            Assert.AreEqual(6, control.World.Resource(BwComposedAbilityState.Key).HealCredit);
            control.Press(BwBeltRules.HealButton); control.Step(4);
            byte[] envelope = BwComposedAbilitySave.Capture(control.Simulation, Domain);
            Assert.Throws<InvalidDataException>(() => BwWeaponSave.Describe(control.Simulation, Domain));
            Assert.AreEqual("brawler.composed-ability-belt", BwComposedAbilitySave.Describe(control.Simulation, Domain).ModeId);
            using var restored = new Session(); using (var stream = new MemoryStream(envelope)) BwComposedAbilitySave.Restore(stream, restored.Simulation, Domain);
            control.Step(10); restored.Step(10);
            CollectionAssert.AreEqual(control.Simulation.CaptureSnapshot(), restored.Simulation.CaptureSnapshot());
            Assert.AreEqual(74, restored.World.Column(BwKeys.Info)[0].Hp); Assert.AreEqual(0, restored.World.Resource(BwComposedAbilityState.Key).HealCredit);
            Assert.AreEqual(1, restored.World.Resource(BwComposedAbilityState.Key).AppliedHeals);
        }
        [Test]
        public void ChangedLocalRuleIsRejectedBeforeMutationWithoutChangingVisualIdentity()
        {
            using var source = new Session(); using var target = new Session(false);
            byte[] envelope = BwComposedAbilitySave.Capture(source.Simulation, Domain), before = target.Simulation.CaptureSnapshot();
            using var stream = new MemoryStream(envelope); Assert.Throws<InvalidDataException>(() => BwComposedAbilitySave.Restore(stream, target.Simulation, Domain));
            CollectionAssert.AreEqual(before, target.Simulation.CaptureSnapshot());
            var a = BwComposedAbilitySave.Describe(source.Simulation, Domain); var b = BwComposedAbilitySave.Describe(target.Simulation, Domain);
            Assert.AreNotEqual(a.ContentFingerprint, b.ContentFingerprint); Assert.AreEqual(a.VisualFingerprint, b.VisualFingerprint);
        }
    }
}
