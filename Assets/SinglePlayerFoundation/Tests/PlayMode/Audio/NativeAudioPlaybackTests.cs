#if !SPF_DOTNET_HARNESS
using System;
using System.Collections;
using NUnit.Framework;
using SPF.Presentation;
using SPF.Presentation.Audio;
using SPF.Testing;
using UnityEngine;
using UnityEngine.TestTools;

namespace SPF.Audio.Tests.PlayMode
{
    /// <summary>Real AudioSource/DSP validation. No microphone, capture-device permission, OS mixer
    /// or audio settings changes. A stopped DSP is explicitly unverified, never a success.</summary>
    public sealed class NativeAudioPlaybackTests
    {
        [UnityTest]
        public IEnumerator ActualSourcesLoopPauseResumeCrossfadeAndRemainBounded()
        {
            var root = new GameObject("NativeAudioProbe"); AudioClip clip = null;
            try
            {
                if (UnityEngine.Object.FindObjectOfType<AudioListener>() == null) root.AddComponent<AudioListener>();
                var player = SoundPlayer.Create(root.transform, 4); var bank = new SanctuaryAudioBank(player);
                Assert.AreEqual(6, player.GetComponents<AudioSource>().Length, "four pooled transients plus two music lanes");
                double before = AudioSettings.dspTime; yield return new WaitForSecondsRealtime(.15f);
                if (AudioSettings.dspTime <= before) Assert.Ignore("Native playback unverified: the Unity audio DSP is not advancing on this runner.");
                // Short known waveform exercises a true source loop; authored 32/48-second streams are exercised below.
                float[] samples = new float[2400]; for (int i = 0; i < samples.Length; i++) samples[i] = .1f * Mathf.Sin(2 * Mathf.PI * 400 * i / 24000);
                clip = AudioClip.Create("known-periodic-probe", samples.Length, 1, 24000, false); Assert.IsTrue(clip.SetData(samples, 0));
                int shortLoop = player.Register("probe-loop", clip, SoundBus.Music);
                Assert.IsTrue(player.SetMusic(shortLoop, 0)); yield return new WaitForSecondsRealtime(.25f);
                var source = player.MusicSource(0); Assert.IsTrue(source.isPlaying); Assert.IsTrue(source.loop);
                Assert.That(source.timeSamples, Is.InRange(0, 2399));
                var output = new float[512]; source.GetOutputData(output, 0); float energy = 0;
                foreach (float value in output) energy += value * value;
                Assert.Greater(energy, .0001f, "the real source output contains signal; this is not a speaker-quality judgment");
                player.SetPaused(true); int pausedAt = source.timeSamples; yield return new WaitForSecondsRealtime(.15f);
                Assert.IsFalse(source.isPlaying); Assert.AreEqual(pausedAt, source.timeSamples);
                Assert.IsTrue(player.Play(bank.Confirm)); Assert.IsFalse(player.Play(bank.Knife));
                player.SetPaused(false); yield return new WaitForSecondsRealtime(.1f); Assert.IsTrue(source.isPlaying);
                player.SetBackground(true); Assert.IsFalse(player.Play(bank.Confirm)); player.SetBackground(false);
                Assert.IsTrue(player.SetMusic(bank.Exploration, .15f)); yield return new WaitForSecondsRealtime(.2f);
                int lane = player.Music.Track(0) == bank.Exploration ? 0 : 1;
                Assert.IsTrue(player.MusicSource(lane).isPlaying); int position = player.MusicSource(lane).timeSamples;
                Assert.IsFalse(player.SetMusic(bank.Exploration, 1)); yield return new WaitForSecondsRealtime(.1f);
                Assert.Greater(player.MusicSource(lane).timeSamples, position, "same track must not restart");
                player.SetMusic(bank.Combat, .3f); yield return new WaitForSecondsRealtime(.1f); player.SetMusic(bank.Exploration, .2f);
                yield return new WaitForSecondsRealtime(.3f); Assert.AreEqual(bank.Exploration, player.Music.Requested);
                Assert.AreEqual(6, player.GetComponents<AudioSource>().Length);
                player.enabled = false; yield return null;
                foreach (var voice in player.GetComponents<AudioSource>()) Assert.IsFalse(voice.isPlaying);
                player.enabled = true; Assert.IsTrue(player.SetMusic(bank.Combat, 0)); yield return new WaitForSecondsRealtime(.1f);
                Assert.IsTrue(player.MusicSource(0).isPlaying);
                int effect = player.Register("allocation-probe-sfx", clip, minInterval: 0, maxVoices: 4);
                Action warmPlayback = () => { for (int i = 0; i < 64; i++) { player.Play(effect); player.StopSound(effect); } };
                warmPlayback(); warmPlayback();
                using (var probe = new ManagedAllocationProbe())
                {
                    var controlBefore = probe.Calibrate(); var measured = probe.Measure(warmPlayback); var controlAfter = probe.Calibrate();
                    Assert.AreEqual(0, controlBefore.Empty.Value); Assert.Greater(controlBefore.RetainedArrays.Value, 0);
                    Assert.AreEqual(0, controlAfter.Empty.Value); Assert.Greater(controlAfter.RetainedArrays.Value, 0);
                    Assert.AreEqual(0, measured.Value, "current-thread synchronous warmed pooled playback only, excluding audio thread/native decoder allocations");
                }
                Debug.Log("SPF_NATIVE_AUDIO_PROBE: source loop, signal, pause/resume, background, repeated/interrupting music transition and bounded-source lifecycle passed. Perceived listening quality and physical Android/iOS output remain unverified.");
            }
            finally { RenderObjects.Destroy(root); if (clip != null) RenderObjects.Destroy(clip); }
        }
    }
}
#endif
