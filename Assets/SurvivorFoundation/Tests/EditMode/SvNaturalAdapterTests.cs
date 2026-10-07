using System;
using System.Reflection;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Contracts.Weapons;
using SPF.L2.Weapons;
using SPF.Presentation;
using SPF.Presentation.Animation;
using SurvivorFoundation.Presentation;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SurvivorFoundation.Tests
{
    public class SvNaturalAdapterTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly EntityHandle Hero = new EntityHandle(-1, 1);

        // Use the production renderer's preparation path with a deterministic presentation delta.
        // No hand-authored character inputs replace the adapter under test.
        sealed class View : IDisposable
        {
            readonly GameObject m_Object;
            readonly RenderTier? m_Tier;
            public readonly SvRenderer Renderer;
            public View(SvTestWorld t)
            {
                m_Tier = RenderCapabilities.Override; RenderCapabilities.Override = RenderTier.DataTexture;
                m_Object = new GameObject("Survivor adapter regression");
                Renderer = m_Object.AddComponent<SvRenderer>(); Renderer.NaturalCharacters = true;
                Call("Bind", t.Session);
            }
            public void Call(string name, params object[] args) => typeof(SvRenderer).GetMethod(name, Private).Invoke(Renderer, args);
            public void Draw(SvTestWorld t, float dt = 1f / 30)
            {
                Call("PrepareCharacters", t.World, t.Game, t.Game.Hero, 1f, new float4(-20, -20, 20, 20), dt);
                Renderer.Characters.Evaluate();
            }
            public GameplayCharacterInput Input(EntityHandle handle)
            {
                var inputs = (NativeArray<GameplayCharacterInput>)typeof(GameplayCharacterPresenter).GetField("m_Inputs", Private).GetValue(Renderer.Characters);
                for (int i = 0; i < Renderer.Characters.Count; i++) if (inputs[i].Handle == handle) return inputs[i];
                Assert.Fail("Missing submitted actor " + handle); return default;
            }
            public void Dispose()
            {
                Call("Release"); Object.DestroyImmediate(m_Object); RenderCapabilities.Override = m_Tier;
            }
        }

        static SvTestWorld World(bool weapons = true) => new SvTestWorld(tweak: c =>
        {
            c.WeaponCombat = c.MobileSkills = weapons;
            if (weapons) c.WeaponProfiles = WeaponProfiles.CreateDefaults(30);
            c.Settings.SpawnPerSecond = c.Settings.SpawnGrowth = c.Settings.EliteEvery = 0;
            c.Settings.XpBase = 100000;
            foreach (var e in c.Enemies) { e.Speed = 0; e.Hp = 10000; e.Damage = 0; e.Shooter = false; }
        });

        [TestCase(WeaponProfiles.Bow, WeaponStage.Windup)]
        [TestCase(WeaponProfiles.Bow, WeaponStage.Active)]
        [TestCase(WeaponProfiles.Bow, WeaponStage.Recovery)]
        [TestCase(WeaponProfiles.Staff, WeaponStage.Windup)]
        [TestCase(WeaponProfiles.Staff, WeaponStage.Active)]
        [TestCase(WeaponProfiles.Staff, WeaponStage.Recovery)]
        public void DamageAddsRecoilWithoutInterruptingAutomaticWeapon(int id, WeaponStage damageStage)
        {
            using var t = World(); using var reference = World();
            var weapon = t.World.Resource(SvWeapons.Key); var untouched = reference.World.Resource(SvWeapons.Key);
            weapon.RequestEquip(id); untouched.RequestEquip(id);
            t.Step(weapon.Profile(id).EquipTicks); reference.Step(untouched.Profile(id).EquipTicks);
            t.Spawn(1, new float2(5, 0)); reference.Spawn(1, new float2(5, 0));
            using var view = new View(t);
            int damageTick = damageStage == WeaponStage.Windup ? 2 : damageStage == WeaponStage.Active ? weapon.Current.ReleaseTick : weapon.Current.Active.Until;
            bool contactQueued = false, damaged = false, releaseObserved = false; float peak = 0;
            for (int frame = 0; frame < weapon.Current.DurationTicks * 2 + 35; frame++)
            {
                bool makeContact = !contactQueued && weapon.Equipment.Timeline.Running && weapon.Equipment.Timeline.Tick == damageTick - 2;
                bool applyDamage = contactQueued && !damaged;
                float hp = t.Game.Hp;
                int contactRow = -1;
                if (makeContact)
                {
                    // Real enemy contact queues damage for the following simulation tick. The
                    // reference gets the same actor/targeting geometry, with zero contact damage.
                    var attacker = t.Spawn(1, new float2(-.3f, 0)); reference.Spawn(1, new float2(-.3f, 0));
                    Assert.IsTrue(t.World.Registry.TryResolve(attacker, out _, out contactRow));
                    var info = t.World.Column(SvKeys.Info); var enemy = info[contactRow]; enemy.Damage = 9; info[contactRow] = enemy;
                }
                t.Step(); reference.Step();
                if (makeContact)
                {
                    contactQueued = true; Assert.AreEqual(hp, t.Game.Hp);
                    Assert.Greater(t.World.Resource(SvKeys.HeroDamage).Count, 0, "actual overlap must queue the hit");
                    var info = t.World.Column(SvKeys.Info); var enemy = info[contactRow]; enemy.Damage = 0; info[contactRow] = enemy;
                }
                if (applyDamage)
                {
                    damaged = true; Assert.AreEqual(hp - 9, t.Game.Hp, .0001f);
                    Assert.AreEqual(damageStage, weapon.View().Stage);
                }
                view.Draw(t);
                var input = view.Input(Hero); var expected = weapon.View();
                Assert.AreEqual(untouched.Equipment.Timeline.Tick, weapon.Equipment.Timeline.Tick);
                Assert.AreEqual(untouched.Equipment.Timeline.PulseId, weapon.Equipment.Timeline.PulseId);
                Assert.AreEqual(untouched.Releases, weapon.Releases, "damage cannot delay, cancel or duplicate a release");
                Assert.AreEqual(expected, input.Weapon, "adapter must preserve the complete authoritative weapon view");
                Assert.AreEqual(expected.Phase, input.Phase);
                if (expected.Stage != WeaponStage.Idle)
                    Assert.AreEqual(expected.Stage == WeaponStage.Recovery ? GameplayCharacterState.Recovery : GameplayCharacterState.Attack, input.State);
                Assert.IsTrue(view.Renderer.Characters.TryRead(Hero, out var motion));
                peak = math.max(peak, motion.Hit);
                if (applyDamage) Assert.Greater(motion.Hit, .1f, "actual HP damage must drive recoil during the weapon action");
                Assert.That(motion.Hit, Is.InRange(0f, 1f));
                if (weapon.Equipment.Timeline.Tick == weapon.Current.ReleaseTick)
                {
                    releaseObserved = true;
                    Assert.IsTrue(view.Renderer.Characters.TryReadWeapon(Hero, out var socket));
                    Assert.AreEqual(expected.ActionPulse, socket.ActionPulse);
                    float2 canonical = t.Game.Hero + expected.AimDirection * expected.MuzzleOffset.x * SvWeapons.ActorScale + new float2(0, expected.MuzzleOffset.y * SvWeapons.ActorScale);
                    Assert.Less(math.distance(canonical, socket.Muzzle), .17f, "recoil cannot detach the authored release socket");
                }
            }
            Assert.IsTrue(damaged); Assert.IsTrue(releaseObserved); Assert.Greater(peak, .8f);
            view.Renderer.Characters.TryRead(Hero, out var settled);
            Assert.Less(settled.Hit, .001f, "one damage response settles while autoattack keeps running");
            Assert.GreaterOrEqual(weapon.Releases, 2);
        }

        [TestCase(2, false)] [TestCase(2, true)]
        [TestCase(3, false)] [TestCase(3, true)]
        public void KilledZombieAndBruteRetainEffectiveScaleAndMotionRole(int kind, bool elite)
        {
            using var t = World(false); var handle = t.Spawn(kind, new float2(5, 0), elite);
            using var view = new View(t); view.Draw(t);
            var alive = view.Input(handle); float radius = t.World.Column(SvKeys.Info)[0].Radius;
            Assert.AreEqual(math.clamp(radius * 1.22f, .28f, .85f), alive.Scale);
            Assert.AreEqual(radius >= .65f ? 2 : 1, alive.MotionProfileId);
            Assert.IsTrue(t.World.Resource(SvKeys.Hits).TryAdd(new SvHit { Target = 0, Damage = 100000 }));
            t.Step();
            var feedback = t.World.Resource(SvKeys.Feedback); bool found = false;
            for (int i = 0; i < feedback.Count; i++) if (feedback[i].Kind == SvFeedbackKind.Death)
            { found = true; Assert.AreEqual(radius, feedback[i].Value); }
            Assert.IsTrue(found, "death must come from real damage resolution");
            view.Call("DrainFeedback", t.World); view.Draw(t);
            var dead = view.Input(new EntityHandle(-2, 1));
            Assert.AreEqual(GameplayCharacterState.Death, dead.State);
            Assert.AreEqual(alive.Scale, dead.Scale, "elite radius must survive table removal");
            Assert.AreEqual(alive.MotionProfileId, dead.MotionProfileId, "heavy/agile role must survive death");
            t.Step(); Assert.IsFalse(t.World.Registry.TryResolve(handle, out _, out _));
            view.Draw(t); Assert.AreEqual(alive.Scale, view.Input(new EntityHandle(-2, 1)).Scale);
        }
    }
}
