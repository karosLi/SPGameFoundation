using System;
using NUnit.Framework;
using SPF.Contracts;
using SPF.Contracts.Weapons;
using SPF.L2.Weapons;
using Unity.Mathematics;
using SPF.Presentation;
using SPF.Presentation.Audio;
using SPF.Testing;
using UnityEngine;

namespace SPF.Tests.EditMode
{
    public sealed class MobileAudioTests
    {
        GameObject m_Root;
        AudioClip m_Clip;
        SoundPlayer m_Player;
        SanctuaryAudioBank m_Bank;
        [SetUp] public void Setup()
        {
            m_Root = new GameObject("AudioContractTest"); m_Player = SoundPlayer.Create(m_Root.transform, 4);
            m_Clip = AudioClip.Create("borrowed-test-wave", 24000, 1, 24000, false);
            m_Bank = new SanctuaryAudioBank(m_Player, _ => m_Clip);
        }
        [TearDown] public void TearDown() { RenderObjects.Destroy(m_Root); RenderObjects.Destroy(m_Clip); }
        [Test] public void InitializationIsIdempotentAndCapacityIsImmutable()
        {
            var first = m_Player.VoiceSource(0); var music = m_Player.MusicSource(1);
            m_Player.Initialize(4); Assert.AreSame(first, m_Player.VoiceSource(0)); Assert.AreSame(music, m_Player.MusicSource(1));
            Assert.Throws<InvalidOperationException>(() => m_Player.Initialize(5));
            Assert.Throws<ArgumentOutOfRangeException>(() => m_Player.Initialize(33));
            Assert.AreEqual(14, m_Player.SoundCount);
        }
        [Test] public void DuplicateRegistrationFailsBeforeGrowingBank()
        {
            Assert.Throws<ArgumentException>(() => m_Player.Register("sanctuary/heal", m_Clip)); Assert.AreEqual(14, m_Player.SoundCount);
            Assert.Throws<ArgumentOutOfRangeException>(() => m_Player.Register("invalid", m_Clip, minInterval: float.NaN));
        }
        [Test] public void BusVolumeMuteAndMasterRefreshExistingSources()
        {
            m_Player.Master = .5f; m_Player.SetVolume(SoundBus.Sfx, .4f); Assert.IsTrue(m_Player.Play(m_Bank.Heal, .5f));
            Assert.AreEqual(.08f, m_Player.VoiceSource(0).volume, 1e-6f);
            m_Player.SetMuted(SoundBus.Sfx, true); Assert.AreEqual(0, m_Player.VoiceSource(0).volume); Assert.IsFalse(m_Player.Play(m_Bank.Knife));
            Assert.IsTrue(m_Player.Play(m_Bank.Confirm));
            m_Player.SetMuted(SoundBus.Sfx, false); Assert.AreEqual(.08f, m_Player.VoiceSource(0).volume, 1e-6f);
            m_Player.Master = float.NaN; Assert.AreEqual(0, m_Player.Master);
        }
        [Test] public void NullAndFailedRequestsDoNotConsumeVoiceOrPlayedCount()
        {
            int missing = m_Player.Register("missing", (AudioClip)null);
            Assert.IsFalse(m_Player.Play(missing)); Assert.AreEqual(1, m_Player.FailedClips); Assert.AreEqual(0, m_Player.Played);
            Assert.IsFalse(m_Player.Play(m_Bank.Knife, float.NaN)); Assert.IsFalse(m_Player.Play(m_Bank.Knife, pitch: float.PositiveInfinity));
            Assert.IsFalse(m_Player.Play(m_Bank.Exploration)); Assert.AreEqual(0, m_Player.Pool.ActiveVoices(Time.unscaledTime));
        }
        [Test] public void PauseDropsGameTransientsButUiIsAvailableAndBackgroundBlocksBoth()
        {
            Assert.IsTrue(m_Player.Play(m_Bank.Knife)); m_Player.SetPaused(true);
            Assert.AreEqual(0, m_Player.Pool.ActiveVoices(Time.unscaledTime)); Assert.IsFalse(m_Player.Play(m_Bank.Heal)); Assert.IsTrue(m_Player.Play(m_Bank.Confirm));
            m_Player.SetBackground(true); Assert.AreEqual(0, m_Player.Pool.ActiveVoices(Time.unscaledTime)); Assert.IsFalse(m_Player.Play(m_Bank.Cancel));
            m_Player.SetBackground(false); Assert.IsFalse(m_Player.Play(m_Bank.Heal)); m_Player.SetPaused(false); Assert.IsTrue(m_Player.Play(m_Bank.Heal));
        }
        [Test] public void TwoDimensionalAttenuationIsExplicitAndFinite()
        {
            Assert.IsFalse(m_Player.PlayAt(m_Bank.Knife, new Vector2(21, 0), Vector2.zero, 20));
            Assert.IsTrue(m_Player.PlayAt(m_Bank.Knife, new Vector2(10, 0), Vector2.zero, 20));
            Assert.AreEqual(.35f, m_Player.VoiceSource(0).panStereo, 1e-6f);
            Assert.AreEqual(.32f, m_Player.VoiceSource(0).volume, 1e-6f);
            Assert.IsFalse(m_Player.PlayAt(m_Bank.Heal, Vector2.zero, Vector2.zero, float.NaN));
        }
        [Test] public void SaturationPreservesHigherPriorityAndClearResetsRetriggerClock()
        {
            var pool = new VoicePool(2); int hurt = pool.AddSound(1, .5f), hit = pool.AddSound(2, 0);
            pool.Acquire(hurt, 1, 3, 4); pool.Acquire(hit, 1, 3, 1); pool.Acquire(hit, 1.1f, 3, 1);
            Assert.AreEqual(hurt, pool.SoundOf(0)); Assert.AreEqual(1, pool.Stolen);
            pool.Clear(); Assert.GreaterOrEqual(pool.Acquire(hurt, 0, 1, 4), 0, "new timeline may rewind time");
            Assert.AreEqual(-1, pool.Acquire(-1, 0, 1)); Assert.AreEqual(-1, pool.Acquire(hit, float.NaN, 1));
        }
        [Test] public void MusicRepeatsDoNotRestartAndReversalKeepsInstantaneousGains()
        {
            var fade = new MusicCrossfade(); Assert.IsTrue(fade.Request(1, 0)); Assert.AreEqual(1, fade.Gain(0));
            fade.Request(2, 2); fade.Advance(.5f); float a = fade.Gain(0), b = fade.Gain(1);
            Assert.IsFalse(fade.Request(2, 5)); Assert.AreEqual(a, fade.Gain(0));
            Assert.IsTrue(fade.Request(1, 1)); Assert.AreEqual(a, fade.Gain(0)); Assert.AreEqual(b, fade.Gain(1));
            fade.Advance(1); Assert.AreEqual(1, fade.Gain(0)); Assert.AreEqual(-1, fade.Track(1)); Assert.AreEqual(0, fade.Gain(1));
        }
        [Test] public void ThirdTrackStealsOnlyQuieterMusicLaneAndStopActuallyClearsBoth()
        {
            var fade = new MusicCrossfade(); fade.Request(1, 0); fade.Request(2, 2); fade.Advance(.25f);
            float keep = fade.Gain(0); fade.Request(3, 1); Assert.AreEqual(1, fade.Track(0)); Assert.AreEqual(keep, fade.Gain(0)); Assert.AreEqual(3, fade.Track(1));
            fade.Advance(1); Assert.AreEqual(-1, fade.Track(0)); Assert.AreEqual(3, fade.Track(1));
            fade.Request(-1, .5f); fade.Advance(.5f); Assert.AreEqual(-1, fade.Track(0)); Assert.AreEqual(-1, fade.Track(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => fade.Advance(float.NaN));
        }
        [Test] public void MusicPauseFreezesCrossfadeAndBusSettingsApplyToBothLanes()
        {
            m_Player.SetMusic(m_Bank.Exploration, 0); m_Player.SetMusic(m_Bank.Combat, 2); m_Player.TickAudio(.5f);
            float first = m_Player.Music.Gain(0); m_Player.SetPaused(true); m_Player.TickAudio(3); Assert.AreEqual(first, m_Player.Music.Gain(0));
            m_Player.SetMuted(SoundBus.Music, true); Assert.AreEqual(0, m_Player.MusicSource(0).volume); Assert.AreEqual(0, m_Player.MusicSource(1).volume);
            m_Player.SetPaused(false); m_Player.TickAudio(2); Assert.AreEqual(-1, m_Player.Music.Track(0)); Assert.AreEqual(m_Bank.Combat, m_Player.Music.Track(1));
            m_Player.ResetPlayback(); Assert.AreEqual(-1, m_Player.Music.Track(1)); Assert.IsNull(m_Player.MusicSource(1).clip);
        }
        [Test] public void CursorIsIndependentNonDestructiveAndSameTickRestorePrimesBacklog()
        {
            var cursor = new WeaponAudioCursor(m_Player, m_Bank); var session = new object(); var weapon = new object(); var owner = new EntityHandle(7, 2);
            Assert.IsTrue(cursor.Synchronize(session, 1, 0, weapon, 1, 10, owner, 8));
            var cue = new WeaponCue { Owner = owner, Sequence = 8, Kind = WeaponCueKind.Release, ActionPulse = 2 };
            Assert.IsFalse(cursor.Submit(cue, WeaponActionFamily.Slash, Vector2.zero)); cue.Sequence++;
            Assert.IsTrue(cursor.Submit(cue, WeaponActionFamily.Slash, Vector2.zero)); Assert.IsFalse(cursor.Submit(cue, WeaponActionFamily.Slash, Vector2.zero));
            Assert.AreEqual(9u, cue.Sequence, "input value/ring remains untouched");
            Assert.IsTrue(cursor.Synchronize(session, 2, 0, weapon, 2, 10, owner, 9)); Assert.IsFalse(cursor.Submit(cue, WeaponActionFamily.Slash, Vector2.zero));
            Assert.AreEqual(1, cursor.Accepted); Assert.AreEqual(0, m_Player.Pool.ActiveVoices(Time.unscaledTime));
        }
        [Test] public void CursorRebindGenerationLevelAndBackwardsTickNeverReplay()
        {
            var c = new WeaponAudioCursor(m_Player, m_Bank); object session = new object(), weapon = new object(); var owner = new EntityHandle(1, 1);
            c.Synchronize(session, 1, 0, weapon, 1, 10, owner, 10);
            Assert.IsFalse(c.Synchronize(session, 1, 0, weapon, 1, 11, owner, 11));
            Assert.IsTrue(c.Synchronize(session, 1, 0, weapon, 1, 2, owner, 2)); Assert.AreEqual(2u, c.Sequence);
            Assert.IsTrue(c.Synchronize(session, 1, 1, weapon, 1, 2, owner, 2));
            Assert.IsTrue(c.Synchronize(new object(), 1, 1, weapon, 1, 2, owner, 2));
            var stale = new WeaponCue { Owner = owner, Sequence = 99, Kind = WeaponCueKind.Release };
            c.Synchronize(session, 1, 1, weapon, 1, 2, new EntityHandle(1, 2), 2);
            Assert.IsFalse(c.Submit(stale, WeaponActionFamily.Cast, Vector2.zero));
        }
        [Test] public void CancelNeverManufacturesReleaseOrCancelsAnotherDrawPulse()
        {
            var c = new WeaponAudioCursor(m_Player, m_Bank); var owner = new EntityHandle(1, 1);
            c.Synchronize(new object(), 0, 0, new object(), 0, 0, owner, 0);
            c.ObserveMelee(new WeaponViewState { Family = WeaponActionFamily.Draw, ActionPulse = 2, Stage = WeaponStage.Windup, ReleasePhase = .6f }, Vector2.zero, Vector2.zero);
            var cue = new WeaponCue { Owner = owner, Sequence = 1, Kind = WeaponCueKind.Begin, ActionPulse = 2 };
            Assert.IsTrue(c.Submit(cue, WeaponActionFamily.Draw, Vector2.zero));
            cue.Sequence++; cue.Kind = WeaponCueKind.Cancel; cue.ActionPulse = 1; Assert.IsFalse(c.Submit(cue, WeaponActionFamily.Draw, Vector2.zero));
            Assert.AreEqual(1, m_Player.Pool.ActiveVoices(Time.unscaledTime));
            cue.Sequence++; cue.ActionPulse = 2; c.Submit(cue, WeaponActionFamily.Draw, Vector2.zero);
            Assert.AreEqual(0, m_Player.Pool.ActiveVoices(Time.unscaledTime)); Assert.AreEqual(1, m_Player.Played);
        }
        [Test] public void MeleeSoundUsesAuthoritativeContactAndDoesNotPlayOnWindupOrRestore()
        {
            var c = new WeaponAudioCursor(m_Player, m_Bank); var session = new object(); var weapon = new object(); var owner = new EntityHandle(1, 1);
            c.Synchronize(session, 0, 0, weapon, 0, 0, owner, 0);
            var view = new WeaponViewState { Family = WeaponActionFamily.Slash, ContactPhase = .3f, Stage = WeaponStage.Idle };
            Assert.IsFalse(c.ObserveMelee(view, Vector2.zero, Vector2.zero));
            view.ActionPulse = 1; view.Stage = WeaponStage.Windup; view.Phase = .2f; Assert.IsFalse(c.ObserveMelee(view, Vector2.zero, Vector2.zero));
            view.Stage = WeaponStage.Active; view.Phase = .3f; Assert.IsTrue(c.ObserveMelee(view, Vector2.zero, Vector2.zero));
            Assert.IsFalse(c.ObserveMelee(view, Vector2.zero, Vector2.zero));
            c.Synchronize(session, 1, 0, weapon, 1, 0, owner, 0); Assert.IsFalse(c.ObserveMelee(view, Vector2.zero, Vector2.zero));
            Assert.AreEqual(1, m_Player.Played);
        }
        [Test] public void BindDuringWindupAllowsFutureContactButNeverReplaysPastContact()
        {
            var c = new WeaponAudioCursor(m_Player, m_Bank); var owner = new EntityHandle(2, 1);
            c.Synchronize(new object(), 0, 0, new object(), 0, 5, owner, 1);
            var view = new WeaponViewState { Family = WeaponActionFamily.Thrust, ActionPulse = 3, ContactPhase = .3f, Stage = WeaponStage.Windup, Phase = .1f };
            Assert.IsFalse(c.ObserveMelee(view, Vector2.zero, Vector2.zero)); view.Stage = WeaponStage.Active; view.Phase = .3f;
            Assert.IsTrue(c.ObserveMelee(view, Vector2.zero, Vector2.zero)); Assert.IsFalse(c.ObserveMelee(view, Vector2.zero, Vector2.zero));
        }
        [Test] public void ActualWeaponCancelAllStopsDrawWithoutRequiringCancelEvent()
        {
            using var runtime = new WeaponRuntime(new[] { WeaponProfiles.CreateDefaults(60)[3] }, 60);
            runtime.Owner = new EntityHandle(1, 1); var c = new WeaponAudioCursor(m_Player, m_Bank);
            c.Synchronize(new object(), 0, 0, runtime, runtime.Revision, runtime.Tick, runtime.Owner, 0);
            c.ObserveMelee(runtime.View(1), Vector2.zero, Vector2.zero);
            runtime.Step(true, true, false, true, false, new float2(1, 0), float2.zero, 0, 1);
            c.ObserveMelee(runtime.View(1), Vector2.zero, Vector2.zero); Assert.IsTrue(c.Submit(runtime.Cues[0], WeaponActionFamily.Draw, Vector2.zero));
            Assert.AreEqual(1, m_Player.Pool.ActiveVoices(Time.unscaledTime));
            runtime.Step(true, true, true, false, false, new float2(1, 0), float2.zero, 0, 1);
            Assert.AreEqual(0, runtime.CueCount, "legacy CancelAll has no Cancel event");
            c.ObserveMelee(runtime.View(1), Vector2.zero, Vector2.zero); Assert.AreEqual(0, m_Player.Pool.ActiveVoices(Time.unscaledTime));
        }
        [Test] public void ImpactBudgetMergesRingAndLegacyFactsWithoutDroppingLowQualityRingHits()
        {
            var budget = new ImpactAudioBudget();
            budget.Request(1, false, Vector2.zero); budget.Request(1, true, new Vector2(2, 3));
            Assert.IsTrue(budget.TryTake(1, out bool heavy, out Vector2 at)); Assert.IsTrue(heavy); Assert.AreEqual(new Vector2(2, 3), at);
            budget.Request(1, false, Vector2.zero); Assert.IsFalse(budget.TryTake(1, out _, out _));
            budget.Request(2, false, Vector2.zero); Assert.IsTrue(budget.TryTake(2, out heavy, out _)); Assert.IsFalse(heavy, "a ring-only nonlethal hit still sounds");
            budget.Request(3, true, Vector2.zero); budget.Reset(); Assert.IsFalse(budget.TryTake(3, out _, out _));
        }
        [Test] public void InaudibleHeavyImpactCannotMaskNearbyLightImpact()
        {
            var budget = new ImpactAudioBudget(); var far = new Vector2(40, 0); var near = new Vector2(2, 0);
            budget.Request(1, true, far, SoundPlayer.DistanceGain(far, Vector2.zero));
            budget.Request(1, false, near, SoundPlayer.DistanceGain(near, Vector2.zero));
            Assert.IsTrue(budget.TryTake(1, out bool heavy, out Vector2 at)); Assert.IsFalse(heavy); Assert.AreEqual(near, at);
            budget.Request(2, false, new Vector2(12, 0), .3f); budget.Request(2, false, near, .9f);
            budget.TryTake(2, out _, out at); Assert.AreEqual(near, at, "same-severity audible candidate wins");
        }
        [Test] public void DisabledPlayerRejectsNewMusicRequests()
        {
            m_Player.enabled = false; Assert.IsFalse(m_Player.SetMusic(m_Bank.Combat, 0)); Assert.AreEqual(-1, m_Player.Music.Requested);
        }
        [Test] public void SequencedCueWrapStillRejectsDuplicates()
        {
            var c = new WeaponAudioCursor(m_Player, m_Bank); var owner = new EntityHandle(1, 1);
            c.Synchronize(new object(), 0, 0, new object(), 0, 0, owner, uint.MaxValue);
            var cue = new WeaponCue { Owner = owner, Sequence = 1, Kind = WeaponCueKind.Release };
            Assert.IsTrue(c.Submit(cue, WeaponActionFamily.Cast, Vector2.zero)); Assert.IsFalse(c.Submit(cue, WeaponActionFamily.Cast, Vector2.zero));
        }
        [Test] public void WarmedPoolCursorAndFadeAllocateNoManagedMemoryWithControls()
        {
            var cursor = new WeaponAudioCursor(m_Player, m_Bank); var session = new object(); var weapon = new object(); var owner = new EntityHandle(3, 1);
            cursor.Synchronize(session, 0, 0, weapon, 0, 0, owner, 1);
            var cue = new WeaponCue { Owner = owner, Sequence = 1, Kind = WeaponCueKind.Release };
            var fade = new MusicCrossfade(); fade.Request(1, 0); fade.Request(2, 2);
            var pool = new VoicePool(8); int sound = pool.AddSound(3, 0); int tick = 0;
            Action work = () => { for (int i = 0; i < 64; i++) { pool.Acquire(sound, tick++ * .02f, .1f); fade.Advance(.01f); cursor.Submit(cue, WeaponActionFamily.Slash, Vector2.zero); } };
            work(); work(); using var probe = new ManagedAllocationProbe(); var before = probe.Calibrate(); var sample = probe.Measure(work); var after = probe.Calibrate();
            Assert.AreEqual(0, before.Empty.Value); Assert.Greater(before.RetainedArrays.Value, 0); Assert.AreEqual(0, after.Empty.Value); Assert.Greater(after.RetainedArrays.Value, 0);
            Assert.AreEqual(0, sample.Value, "pure warmed presentation policies only; native mixer checked separately");
        }
    }
}
