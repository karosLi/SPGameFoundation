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
using BrawlerFoundation.Presentation;
using Unity.Mathematics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BrawlerFoundation.Tests
{
    public class BwViewLifecycleTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static BwTestWorld World(bool weapons = false) => new BwTestWorld(weapons: weapons);
        sealed class View : IDisposable
        {
            readonly GameObject root;
            readonly RenderTier? previousTier;
            readonly SessionHost host;
            public readonly BwRenderer Renderer;
            public View(BwTestWorld world, bool natural = true)
            {
                previousTier = RenderCapabilities.Override; RenderCapabilities.Override = RenderTier.DataTexture;
                root = new GameObject("Bw lifecycle regression");
                host = root.AddComponent<SessionHost>(); host.enabled = false;
                Renderer = root.AddComponent<BwRenderer>(); Renderer.Host = host; Renderer.NaturalCharacters = natural;
                Rebind(world); Renderer.RenderFrame();
            }
            public void Rebind(BwTestWorld world) => typeof(SessionHost).GetField("<Session>k__BackingField", Private).SetValue(host, world.Session);
            public T Field<T>(string name) => (T)typeof(BwRenderer).GetField(name, Private).GetValue(Renderer);
            public void SetEnabled(bool enabled)
            {
                Renderer.enabled = enabled;
                // Harness lifecycle messages are explicit; callbacks must be idempotent in Unity.
                typeof(BwRenderer).GetMethod(enabled ? "OnEnable" : "OnDisable", Private)?.Invoke(Renderer, null);
            }
            public void Release() => typeof(BwRenderer).GetMethod("Release", Private).Invoke(Renderer, null);
            public void Dispose()
            {
                Release(); typeof(SessionHost).GetField("<Session>k__BackingField", Private).SetValue(host, null);
                Object.DestroyImmediate(root); RenderCapabilities.Override = previousTier;
            }
        }
        static void QueueFeedback(BwTestWorld t) => Assert.IsTrue(t.World.Resource(BwKeys.Feedback).TryAdd(new BwFeedback { Kind = BwFeedbackKind.Hit, Position = 0, Value = 1 }));
        static void RunAttack(BwTestWorld t)
        {
            var weapons = t.World.Resource(BwWeapons.Key);
            weapons.RequestEquip(WeaponProfiles.Staff); t.Step(weapons.Profile(WeaponProfiles.Staff).EquipTicks);
            t.Press(BwButton.Punch); t.Step(weapons.Current.DurationTicks);
            Assert.Greater(weapons.CueCount, 0);
        }

        [TestCase(false)] [TestCase(true)]
        public void SameTickRestoreClearsOldTransientFeedback(bool natural)
        {
            using var t = World(); using var view = new View(t, natural);
            var snapshot = t.Session.CaptureSnapshot(); uint tick = t.Session.Clock.NextTickIndex;
            QueueFeedback(t); view.Renderer.RenderFrame();
            Assert.Greater(view.Field<SpriteEffects>("m_Fx").Active, 0);
            t.Session.RestoreSnapshot(snapshot);
            Assert.AreEqual(tick, t.Session.Clock.NextTickIndex);
            view.Renderer.RenderFrame();
            Assert.Zero(view.Field<SpriteEffects>("m_Fx").Active, "Transient feedback belongs to the old timeline even when the numeric tick is unchanged.");
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
            var weapons = t.World.Resource(BwWeapons.Key);
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
        public void DistinctSessionsWithSameHandleTickAndRevisionRebindIndependently()
        {
            using var a = World(true); using var b = World(true); using var view = new View(a);
            var aw = a.World.Resource(BwWeapons.Key); var bw = b.World.Resource(BwWeapons.Key);
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
            Assert.Zero(t.World.Resource(BwWeapons.Key).CueCount);
            for (int quality = 0; quality < 4; quality++)
            {
                view.Renderer.SetQualityLevel(quality); view.Renderer.RenderFrame();
                CollectionAssert.AreEqual(saved, t.Session.CaptureSnapshot());
            }
            // Rebuilding private resources at the same Session/tick adopts the current cue head.
            var previous = view.Renderer.WeaponParticles;
            typeof(BwRenderer).GetMethod("Bind", Private).Invoke(view.Renderer, new object[] { t.Session });
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
        public void RecycledFighterGenerationDoesNotReuseOldCharacterState()
        {
            using var t = World(true); using var view = new View(t);
            var old = t.World.Table(BwKeys.Fighter).Handles[1];
            Assert.IsTrue(view.Renderer.Characters.TryRead(old, out _));
            t.World.Resource(SimWorld.DestroyQueueKey).Request(old); t.Step();
            BwSpawner.Spawn(t.World, 1, new float2(5, 0), -1, 0);
            var fresh = t.World.Table(BwKeys.Fighter).Handles[t.Count - 1];
            Assert.AreEqual(old.Index, fresh.Index); Assert.AreNotEqual(old.Generation, fresh.Generation);
            var snapshot = t.Session.CaptureSnapshot(); view.Renderer.RenderFrame();
            Assert.IsFalse(view.Renderer.Characters.TryRead(old, out _)); Assert.IsTrue(view.Renderer.Characters.TryRead(fresh, out _));
            CollectionAssert.AreEqual(snapshot, t.Session.CaptureSnapshot());
        }

        [Test]
        public void TwoViewsReadWeaponCuesIndependentlyWithoutChangingSnapshot()
        {
            using var t = World(true); using var first = new View(t); using var second = new View(t);
            RunAttack(t); var weapons = t.World.Resource(BwWeapons.Key);
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
