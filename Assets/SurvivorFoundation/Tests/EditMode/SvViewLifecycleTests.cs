using System;
using System.Reflection;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Contracts.Weapons;
using SPF.Runtime.World;
using SPF.Presentation.ArtDirection;
using SPF.L2.Weapons;
using SPF.Presentation;
using SPF.Presentation.Sprites;
using SPF.Runtime.Session;
using SPF.Shell.CameraRig;
using SurvivorFoundation.Presentation;
using Unity.Mathematics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SurvivorFoundation.Tests
{
    public class SvViewLifecycleTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static SvTestWorld World(bool weapons = false) => new SvTestWorld(tweak: c =>
        {
            c.WeaponCombat = c.MobileSkills = weapons;
            c.Settings.Variant = SvVariant.GuardBeacon; c.Settings.BeaconHp = 250; c.Settings.GuardDurationTicks = 10000;
            if (weapons) c.WeaponProfiles = WeaponProfiles.CreateDefaults(30);
            c.Capacity.Enemies = 16; c.Capacity.Bullets = 32; c.Capacity.Gems = 16; c.Capacity.Events = 128;
            c.Settings.SpawnPerSecond = c.Settings.SpawnGrowth = c.Settings.EliteEvery = 0;
            c.Settings.XpBase = 100000;
            foreach (var enemy in c.Enemies) { enemy.Speed = enemy.Damage = 0; enemy.Hp = 10000; enemy.Shooter = false; }
        });
        sealed class View : IDisposable
        {
            readonly GameObject root;
            readonly RenderTier? previousTier;
            readonly SessionHost host;
            public readonly SvRenderer Renderer;
            public View(SvTestWorld world, bool natural = true)
            {
                previousTier = RenderCapabilities.Override; RenderCapabilities.Override = RenderTier.DataTexture;
                root = new GameObject("Sv lifecycle regression");
                host = root.AddComponent<SessionHost>(); host.enabled = false;
                Renderer = root.AddComponent<SvRenderer>(); Renderer.Host = host; Renderer.NaturalCharacters = natural;
                Rebind(world); Renderer.RenderFrame();
            }
            public void Rebind(SvTestWorld world) => typeof(SessionHost).GetField("<Session>k__BackingField", Private).SetValue(host, world.Session);
            public T Field<T>(string name) => (T)typeof(SvRenderer).GetField(name, Private).GetValue(Renderer);
            public void SetEnabled(bool enabled)
            {
                Renderer.enabled = enabled;
                // Harness lifecycle messages are explicit; callbacks must be idempotent in Unity.
                typeof(SvRenderer).GetMethod(enabled ? "OnEnable" : "OnDisable", Private)?.Invoke(Renderer, null);
            }
            public void Release() => typeof(SvRenderer).GetMethod("Release", Private).Invoke(Renderer, null);
            public void Dispose()
            {
                Release(); typeof(SessionHost).GetField("<Session>k__BackingField", Private).SetValue(host, null);
                Object.DestroyImmediate(root); RenderCapabilities.Override = previousTier;
            }
        }
        static void QueueFeedback(SvTestWorld t) => Assert.IsTrue(t.World.Resource(SvKeys.Feedback).TryAdd(new SvFeedback { Kind = SvFeedbackKind.LevelUp, Position = 0 }));
        static void RunAttack(SvTestWorld t)
        {
            var weapons = t.World.Resource(SvWeapons.Key);
            t.Spawn(1, new float2(1, 0)); t.Step(weapons.Current.DurationTicks);
            Assert.Greater(weapons.CueCount, 0);
        }

        [TestCase(false, false)] [TestCase(false, true)] [TestCase(true, false)]
        public void UnarmedOrNonNaturalHeroKeepsItsExistingBar(bool weapons, bool natural)
        {
            using var t = World(weapons); using var view = new View(t, natural);
            var bars = view.Field<SpriteBatch>("m_Health");
            Assert.AreEqual(6, bars.Count, "hero and beacon retain their three-sprite bars");
            Assert.AreEqual(t.Game.Hero + new float2(0, 1.55f), bars.Instances[0].Center);
            Assert.AreEqual(t.Runtime.Settings.BeaconPosition + new float2(0, 2.8f), bars.Instances[3].Center);
        }

        [Test]
        public void ArmedHeroBarStaysBelowRenderedFeetAndReservesDamageSpace()
        {
            using var t = World(true); using var view = new View(t);
            var weapon = t.World.Resource(SvWeapons.Key);
            float2 anchor = view.Field<SpriteBatch>("m_Health").Instances[0].Center - t.Game.Hero;
            foreach (int id in new[] { WeaponProfiles.Blade, WeaponProfiles.Sword, WeaponProfiles.Staff, WeaponProfiles.Bow })
            for (int side = -1; side <= 1; side += 2)
            {
                t.Game.Input = new InputFrame { Aim = new float2(side, 0) };
                for (int i = 0; i < weapon.Current.DurationTicks + 1; i++)
                { t.Step(); view.Renderer.RenderFrame(1f / 30); AssertHeroClearance(view, $"settle/turn {id}/{side}/{i}"); }
                Assert.IsTrue(weapon.RequestEquip(id));
                for (int i = 0; i < weapon.Profile(id).EquipTicks + 1; i++)
                { t.Step(); view.Renderer.RenderFrame(1f / 30); AssertHeroClearance(view, $"equip {id}/{side}/{i}"); }
                Assert.AreEqual(id, weapon.Equipment.EquippedId);
                for (int tick = 0; tick < weapon.Current.DurationTicks + 2; tick++)
                {
                    t.Game.Input = new InputFrame { Aim = new float2(side, 0), Held = 1u << SvWeapons.AttackButton,
                        Move = new float2(side * .2f, .1f) };
                    t.Step(); view.Renderer.SetQualityLevel(tick % 4); view.Renderer.RenderFrame(1f / 30);
                    var bars = view.Field<SpriteBatch>("m_Health"); var bar = bars.Instances[0];
                    Assert.IsTrue(view.Renderer.Characters.TryReadCurrent(new EntityHandle(-1, 1), out var motion));
                    Assert.Less(math.distance(anchor, bar.Center - motion.PreviousRoot), .00001f,
                        "equip, phase, facing, movement and quality cannot move the root-relative bar");
                    AssertHeroClearance(view, $"attack {id}/{side}/{tick}");
                    for (int foot = 4; foot <= 8; foot += 4)
                    {
                        var part = view.Renderer.Characters.ReadPart(foot);
                        float halfHeight = (math.abs(math.sin(part.Rotation) * part.Size.x) + math.abs(math.cos(part.Rotation) * part.Size.y)) * .5f;
                        Assert.Less(bar.Center.y + bar.Size.y * .5f, part.Center.y - halfHeight - .05f,
                            "full packed foot rectangle must remain visible above the bar");
                    }
                    Assert.AreEqual(6, bars.Count);
                    Assert.AreEqual(t.Runtime.Settings.BeaconPosition + new float2(0, 2.8f), bars.Instances[3].Center);
                }
            }
            t.Game.Input = default; t.Session.Pause();
            foreach (float hp in new[] { t.Game.MaxHp, t.Game.MaxHp * .4f, 0 })
            {
                t.Game.Hp = hp;
                var before = t.Session.CaptureSnapshot(); view.Renderer.RenderFrame(0);
                var bars = view.Field<SpriteBatch>("m_Health");
                Assert.AreEqual(6, bars.Count, "empty HP retains its visible frame and track");
                Assert.AreEqual(hp / t.Game.MaxHp, bars.Instances[2].Size.x / bars.Instances[1].Size.x, .002f);
                Assert.AreEqual(1f, bars.Instances[0].Color.w);
                Assert.Less(math.distance(anchor, bars.Instances[0].Center - t.Game.Hero), .00001f);
                CollectionAssert.AreEqual(before, t.Session.CaptureSnapshot(), "paused HP presentation never writes simulation/save state");
            }
            // Exercise the actual reservation method with an active label; this also checks its
            // early-return condition instead of testing the anchor helper in isolation.
            var pool = new SPF.Presentation.Combat.DamageNumberPool();
            pool.Emit(new EntityHandle(1, 1), t.Game.Hero, 10, false, 1, 1, 1d / 30);
            Assert.AreEqual(1, pool.Active);
            typeof(SvRenderer).GetField("m_DamageNumbers", Private).SetValue(view.Renderer, pool);
            typeof(SvRenderer).GetMethod("ReserveDamageLayout", Private).Invoke(view.Renderer,
                new object[] { t.World, new float4(-50, -50, 50, 50), 1f });
            var actual = view.Field<SpriteBatch>("m_Health").Instances[0];
            Assert.Greater(view.Renderer.DamageLayout.ActorLift(new float4(actual.Center - actual.Size * .5f, actual.Center + actual.Size * .5f)), 0,
                "damage labels must avoid the actual below-feet bar");
        }

        static void AssertHeroClearance(View view, string phase)
        {
            var bar = view.Field<SpriteBatch>("m_Health").Instances[0];
            var rect = new float4(bar.Center - bar.Size * .5f, bar.Center + bar.Size * .5f);
            for (int i = 0; i < view.Renderer.Characters.PartsDrawn; i++)
            {
                var part = view.Renderer.Characters.ReadPart(i);
                if (part.Color.w > .01f)
                    Assert.IsFalse(IntersectsSprite(rect, part), $"packed hero/weapon part {i} must clear HP during {phase}");
            }
        }

        [TestCase(false)] [TestCase(true)]
        public void ArmedHeroHealthClearsFirstVisibleFramesAfterSkippedPresentation(bool disabled)
        {
            using var t = World(true); using var view = new View(t);
            var weapon = t.World.Resource(SvWeapons.Key);
            foreach (int id in new[] { WeaponProfiles.Blade, WeaponProfiles.Sword, WeaponProfiles.Staff, WeaponProfiles.Bow })
            for (int side = -1; side <= 1; side += 2)
            {
                if (disabled) view.SetEnabled(false);
                // Deliberately skip all presentation during real simulation settle/equip ticks.
                t.Game.Input = default; t.Step(weapon.Current.DurationTicks + 1);
                Assert.IsTrue(weapon.RequestEquip(id)); t.Step(weapon.Profile(id).EquipTicks + 1);
                if (disabled) view.SetEnabled(true);
                for (int frame = 0; frame < 12; frame++)
                {
                    t.Game.Input = new InputFrame { Aim = new float2(side, 0), Held = 1u << SvWeapons.AttackButton,
                        Move = new float2(side * .2f, .1f) };
                    t.Step(); view.Renderer.RenderFrame(frame == 0 && !disabled ? .75f : 1f / 30);
                    AssertHeroClearance(view, $"{(disabled ? "reenable" : "long frame")} {id}/{side}/{frame}");
                }
                t.Session.Pause(); var before = t.Session.CaptureSnapshot();
                for (int frame = 0; frame < 4; frame++)
                { view.Renderer.RenderFrame(.75f); AssertHeroClearance(view, $"pause {id}/{side}/{frame}"); }
                CollectionAssert.AreEqual(before, t.Session.CaptureSnapshot());
                t.Session.Resume();
                for (int frame = 0; frame < 12; frame++)
                { t.Step(); view.Renderer.RenderFrame(1f / 30); AssertHeroClearance(view, $"resume {id}/{side}/{frame}"); }
            }
        }

        static bool IntersectsSprite(float4 rect, PackedSprite sprite)
        {
            float2 half = (rect.zw - rect.xy) * .5f, extent = math.abs(sprite.Size) * .5f;
            float2 delta = (rect.xy + rect.zw) * .5f - sprite.Center;
            float2 right = new float2(math.cos(sprite.Rotation), math.sin(sprite.Rotation));
            float2 up = new float2(-right.y, right.x);
            return math.abs(delta.x) < half.x + extent.x * math.abs(right.x) + extent.y * math.abs(up.x) &&
                math.abs(delta.y) < half.y + extent.x * math.abs(right.y) + extent.y * math.abs(up.y) &&
                math.abs(math.dot(delta, right)) < extent.x + math.dot(half, math.abs(right)) &&
                math.abs(math.dot(delta, up)) < extent.y + math.dot(half, math.abs(up));
        }

        [TestCase(false)] [TestCase(true)]
        public void SameTickRestoreClearsOldTransientFeedback(bool natural)
        {
            using var t = World(); using var view = new View(t, natural);
            var snapshot = t.Session.CaptureSnapshot(); uint tick = t.Session.Clock.NextTickIndex;
            TestContext.WriteLine("Ambient Unity delta=" + Time.deltaTime + "; lifecycle probe delta=0; effect lifetimes unchanged.");
            QueueFeedback(t); view.Renderer.RenderFrame(0f);
            Assert.Greater(view.Field<SpriteEffects>("m_Fx").Active, 0);
            view.Renderer.RenderFrame(0f);
            Assert.Greater(view.Field<SpriteEffects>("m_Fx").Active, 0, "Without restore, the same-tick transient must survive another controlled presentation read.");
            CollectionAssert.AreEqual(snapshot, t.Session.CaptureSnapshot());
            t.Session.RestoreSnapshot(snapshot);
            Assert.AreEqual(tick, t.Session.Clock.NextTickIndex);
            view.Renderer.RenderFrame(0f);
            Assert.Zero(view.Field<SpriteEffects>("m_Fx").Active, "Transient feedback belongs to the old timeline even when the numeric tick is unchanged.");
            CollectionAssert.AreEqual(snapshot, t.Session.CaptureSnapshot());
        }


        [TestCase(false)] [TestCase(true)]
        public void ExplicitLargeVisualDeltaCanExpireFeedbackWithoutRestore(bool natural)
        {
            using var t = World(); using var view = new View(t, natural);
            uint revision = t.Session.TimelineRevision;
            var snapshot = t.Session.CaptureSnapshot();
            QueueFeedback(t); view.Renderer.RenderFrame(0f);
            Assert.Greater(view.Field<SpriteEffects>("m_Fx").Active, 0);
            view.Renderer.RenderFrame(1f);
            Assert.Zero(view.Field<SpriteEffects>("m_Fx").Active, "A normal large visual step still ages effects; the production delta is not clamped and lifetimes are not extended.");
            Assert.AreEqual(revision, t.Session.TimelineRevision);
            CollectionAssert.AreEqual(snapshot, t.Session.CaptureSnapshot());
        }

        [Test]
        public void DisableClearsTransientStateAndReenableSkipsHiddenWeaponCues()
        {
            using var t = World(true); using var view = new View(t);
            QueueFeedback(t); view.Renderer.RenderFrame();
            var particles = view.Renderer.WeaponParticles;
            view.SetEnabled(false);
            RunAttack(t);
            var weapons = t.World.Resource(SvWeapons.Key);
            uint head = weapons.Equipment.CueSequence;
            var snapshot = t.Session.CaptureSnapshot();
            view.SetEnabled(true); view.Renderer.RenderFrame();
            Assert.AreSame(particles, view.Renderer.WeaponParticles, "Temporary disable retains warmed instance resources.");
            Assert.Zero(particles.Renderer.Pool.SpawnCount, "Time spent hidden must not replay release/impact cues.");
            Assert.AreEqual(head, view.Renderer.LastWeaponCueSequence);
            CollectionAssert.AreEqual(snapshot, t.Session.CaptureSnapshot());
        }

        [Test]
        public void RemovingHostReleasesPrivateBindingAndCanRebindSameSession()
        {
            using var t = World(true); using var view = new View(t);
            var host = view.Renderer.Host;
            view.Renderer.Host = null; view.Renderer.RenderFrame();
            Assert.IsNull(view.Renderer.WeaponParticles, "A removed Session binding cannot keep private particle resources alive.");
            view.Renderer.Host = host; view.Renderer.RenderFrame();
            Assert.IsNotNull(view.Renderer.WeaponParticles);
            view.Release(); view.Release();
        }


        [Test]
        public void DisableReleaseAndCameraReplacementDetachOnlyOwnedCallback()
        {
            using var t = World(true); using var view = new View(t);
            var cameraRoot = new GameObject("Lifecycle camera", typeof(Camera));
            var otherRoot = new GameObject("Replacement camera", typeof(Camera));
            try
            {
                var camera = cameraRoot.AddComponent<FollowCamera2D>();
                var other = otherRoot.AddComponent<FollowCamera2D>();
                view.Renderer.Camera = camera; view.Renderer.RenderFrame();
                Assert.IsNotNull(camera.UpdateTarget);
                view.SetEnabled(false);
                Assert.IsNull(camera.UpdateTarget, "Disabled view must not remain called by a live camera.");
                view.SetEnabled(true); view.Renderer.RenderFrame(); Assert.IsNotNull(camera.UpdateTarget);
                view.Renderer.Camera = other; view.Renderer.RenderFrame();
                Assert.IsNull(camera.UpdateTarget, "Camera replacement retires the old owned callback.");
                Assert.IsNotNull(other.UpdateTarget);
                Action<FollowCamera2D> external = _ => { };
                other.UpdateTarget = external;
                view.Release(); view.Release();
                Assert.AreSame(external, other.UpdateTarget, "Releasing this view must preserve a replacement owner's callback.");
            }
            finally { Object.DestroyImmediate(cameraRoot); Object.DestroyImmediate(otherRoot); }
        }

        [Test]
        public void CameraBeforeRenderDoesNotReadDisposedPublishedSession()
        {
            using var t = World(true); using var view = new View(t);
            var cameraRoot = new GameObject("Disposed Session camera", typeof(Camera));
            try
            {
                var camera = cameraRoot.AddComponent<FollowCamera2D>();
                view.Renderer.Camera = camera; view.Renderer.RenderFrame();
                Assert.IsNotNull(camera.UpdateTarget);
                t.Session.Dispose();
                Assert.DoesNotThrow(() => camera.UpdateTarget(camera), "The order-400 camera can run before the order-500 renderer unbinds.");
                view.Renderer.RenderFrame();
                Assert.IsNull(camera.UpdateTarget); Assert.IsNull(view.Renderer.WeaponParticles);
            }
            finally { Object.DestroyImmediate(cameraRoot); }
        }

        [Test]
        public void DistinctSessionsWithSameHandleTickAndRevisionRebindIndependently()
        {
            using var a = World(true); using var b = World(true); using var view = new View(a);
            var aw = a.World.Resource(SvWeapons.Key); var bw = b.World.Resource(SvWeapons.Key);
            Assert.AreEqual(aw.Owner, bw.Owner); Assert.AreEqual(a.Session.Clock.NextTickIndex, b.Session.Clock.NextTickIndex);
            Assert.AreEqual(a.Session.TimelineRevision, b.Session.TimelineRevision);
            var old = view.Renderer.WeaponParticles;
            view.Rebind(b); view.Renderer.RenderFrame();
            Assert.AreNotSame(old, view.Renderer.WeaponParticles);
            Assert.AreSame(bw, view.Field<WeaponRuntime>("m_WeaponRuntime"));
            Assert.Zero(view.Renderer.WeaponParticles.Renderer.Pool.ReservedCount);
        }


        [Test]
        public void RestoreRestartQualityAndResourceRebuildCannotChangeSimulation()
        {
            using var t = World(true); using var view = new View(t);
            RunAttack(t); view.Renderer.RenderFrame();
            var saved = t.Session.CaptureSnapshot();
            t.Session.RestoreSnapshot(saved); view.Renderer.RenderFrame();
            Assert.Zero(view.Renderer.WeaponParticles.Renderer.Pool.ReservedCount);
            Assert.Zero(t.World.Resource(SvWeapons.Key).CueCount);
            for (int quality = 0; quality < 4; quality++)
            {
                view.Renderer.SetQualityLevel(quality); view.Renderer.RenderFrame();
                CollectionAssert.AreEqual(saved, t.Session.CaptureSnapshot());
            }
            // Rebuilding private resources at the same Session/tick adopts the current cue head.
            var previous = view.Renderer.WeaponParticles;
            typeof(SvRenderer).GetMethod("Bind", Private).Invoke(view.Renderer, new object[] { t.Session });
            view.Renderer.RenderFrame();
            Assert.IsTrue(previous.Renderer.IsDisposed);
            Assert.AreNotSame(previous, view.Renderer.WeaponParticles);
            Assert.Zero(view.Renderer.WeaponParticles.Renderer.Pool.SpawnCount);
            CollectionAssert.AreEqual(saved, t.Session.CaptureSnapshot());
            t.Session.Restart(); var restarted = t.Session.CaptureSnapshot(); view.Renderer.RenderFrame();
            Assert.Zero(view.Renderer.WeaponParticles.Renderer.Pool.ReservedCount);
            CollectionAssert.AreEqual(restarted, t.Session.CaptureSnapshot());
        }

        [Test]
        public void ReleasingOneViewKeepsOtherPrivateInstancesAndSharedAssetsAlive()
        {
            using var t = World(true); using var first = new View(t); using var second = new View(t);
            var kept = second.Renderer.WeaponParticles;
            Assert.AreNotSame(first.Renderer.WeaponParticles, kept);
#if !SPF_DOTNET_HARNESS
            var ground = Resources.Load<Texture2D>("SPF/ArtDirection/SanctuaryGround");
            Assert.IsNotNull(ground, "The shipped backdrop resource must be available.");
            var field = typeof(SanctuaryBackdrop).GetField("m_GroundTexture", Private);
            Assert.AreSame(ground, field.GetValue(first.Field<SanctuaryBackdrop>("m_Sanctuary")));
            Assert.AreSame(ground, field.GetValue(second.Field<SanctuaryBackdrop>("m_Sanctuary")));
#endif
            var snapshot = t.Session.CaptureSnapshot();
            first.Release(); first.Release(); second.Renderer.RenderFrame();
            Assert.AreSame(kept, second.Renderer.WeaponParticles); Assert.IsFalse(kept.Renderer.IsDisposed);
#if !SPF_DOTNET_HARNESS
            Assert.IsTrue(ground != null, "A borrowing view must never destroy its shared Resources texture.");
            Assert.AreSame(ground, Resources.Load<Texture2D>("SPF/ArtDirection/SanctuaryGround"));
#endif
            CollectionAssert.AreEqual(snapshot, t.Session.CaptureSnapshot());
        }

        [Test]
        public void RecycledEnemyGenerationAndSyntheticHeroRemainSeparate()
        {
            using var t = World(true); var old = t.Spawn(1, new float2(5, 0)); using var view = new View(t);
            var hero = new EntityHandle(-1, 1);
            Assert.IsFalse(t.World.Registry.TryResolve(hero, out _, out _), "Synthetic hero view key is not a registry handle.");
            Assert.IsTrue(view.Renderer.Characters.TryRead(hero, out _)); Assert.IsTrue(view.Renderer.Characters.TryRead(old, out _));
            t.World.Resource(SimWorld.DestroyQueueKey).Request(old); t.Step();
            var fresh = t.Spawn(1, new float2(6, 0));
            Assert.AreEqual(old.Index, fresh.Index); Assert.AreNotEqual(old.Generation, fresh.Generation);
            var snapshot = t.Session.CaptureSnapshot(); view.Renderer.RenderFrame();
            Assert.IsFalse(view.Renderer.Characters.TryRead(old, out _)); Assert.IsTrue(view.Renderer.Characters.TryRead(fresh, out _));
            Assert.IsTrue(view.Renderer.Characters.TryRead(hero, out _));
            CollectionAssert.AreEqual(snapshot, t.Session.CaptureSnapshot());
        }

        [Test]
        public void CompletedManualStaffReleaseIsNotBlockedByIdlePhaseZero()
        {
            using var t = World(true); using var view = new View(t);
            var weapons = t.World.Resource(SvWeapons.Key); weapons.RequestEquip(WeaponProfiles.Staff);
            t.Step(weapons.Profile(WeaponProfiles.Staff).EquipTicks);
            t.Game.Input = new InputFrame { Pressed = 1u << SvWeapons.AttackButton }; t.Step(); t.Game.Input = default;
            t.Step(weapons.Current.DurationTicks);
            Assert.AreEqual(WeaponStage.Idle, weapons.View().Stage); Assert.Greater(weapons.Releases, 0);
            var snapshot = t.Session.CaptureSnapshot(); view.Renderer.RenderFrame();
            Assert.Greater(view.Renderer.WeaponParticles.Renderer.Pool.SpawnCount, 0);
            Assert.AreEqual(weapons.Equipment.CueSequence, view.Renderer.LastWeaponCueSequence);
            CollectionAssert.AreEqual(snapshot, t.Session.CaptureSnapshot());
        }

        [Test]
        public void TwoViewsReadWeaponCuesIndependentlyWithoutChangingSnapshot()
        {
            using var t = World(true); using var first = new View(t); using var second = new View(t);
            RunAttack(t); var weapons = t.World.Resource(SvWeapons.Key);
            var snapshot = t.Session.CaptureSnapshot(); int count = weapons.CueCount;
            first.Renderer.RenderFrame(); second.Renderer.RenderFrame();
            Assert.Greater(first.Renderer.WeaponParticles.Renderer.Pool.SpawnCount, 0);
            Assert.AreEqual(first.Renderer.WeaponParticles.Renderer.Pool.SpawnCount, second.Renderer.WeaponParticles.Renderer.Pool.SpawnCount);
            Assert.AreEqual(first.Renderer.LastWeaponCueSequence, second.Renderer.LastWeaponCueSequence);
            Assert.AreEqual(count, weapons.CueCount, "Weapon cue ring is a read-only bounded backlog.");
            first.Renderer.RenderFrame(); second.Renderer.RenderFrame();
            Assert.Zero(first.Renderer.WeaponParticles.Renderer.Pool.SpawnCount); Assert.Zero(second.Renderer.WeaponParticles.Renderer.Pool.SpawnCount);
            CollectionAssert.AreEqual(snapshot, t.Session.CaptureSnapshot());
        }
    }
}
