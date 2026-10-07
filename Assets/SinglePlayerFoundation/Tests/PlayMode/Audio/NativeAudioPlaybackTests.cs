#if !SPF_DOTNET_HARNESS
using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using SPF.Presentation;
using SPF.Presentation.Audio;
using SPF.Testing;
using UnityEngine;
using UnityEngine.TestTools;

namespace SPF.Audio.Tests.PlayMode
{
    /// <summary>Real AudioSource/DSP validation. No microphone, capture-device permission, OS mixer
    /// or audio settings changes. Output history is primed before bounded DSP/frame observations.</summary>
    public sealed class NativeAudioPlaybackTests
    {
        [UnityTest]
        public IEnumerator ActualSourcesLoopPauseResumeCrossfadeAndRemainBounded()
        {
            var root = new GameObject("NativeAudioProbe"); AudioClip clip = null, silence = null;
            var report = new SignalReport();
            AudioListener observedListener = null;
            try
            {
                report.unityVersion = Application.unityVersion; report.batchMode = Application.isBatchMode;
                report.listenerVolume = AudioListener.volume; report.listenerPaused = AudioListener.pause;
                var existingListeners = UnityEngine.Object.FindObjectsOfType<AudioListener>(true);
                report.previousListeners = new string[existingListeners.Length];
                for (int i = 0; i < existingListeners.Length; i++)
                {
                    var listener = existingListeners[i];
                    report.previousListeners[i] = listener.name + ": enabled=" + listener.enabled + ", active=" + listener.gameObject.activeInHierarchy;
                }
                observedListener = UnityEngine.Object.FindObjectOfType<AudioListener>();
                if (observedListener == null) observedListener = root.AddComponent<AudioListener>();
                report.listener = observedListener;
                AudioSettings.GetDSPBufferSize(out report.dspBufferFrames, out report.dspBufferCount);
                var configuration = AudioSettings.GetConfiguration(); report.outputSampleRate = configuration.sampleRate;
                report.realVoices = configuration.numRealVoices; report.virtualVoices = configuration.numVirtualVoices;
                report.speakerMode = configuration.speakerMode.ToString();
                var player = SoundPlayer.Create(root.transform, 4); var bank = new SanctuaryAudioBank(player);
                Assert.AreEqual(6, player.GetComponents<AudioSource>().Length, "four pooled transients plus two music lanes");
                double before = AudioSettings.dspTime; yield return new WaitForSecondsRealtime(.15f);
                Assert.Greater(AudioSettings.dspTime, before, "Native playback unverified: the Unity audio DSP is not advancing on this runner.");
                Assert.IsTrue(observedListener != null && observedListener.isActiveAndEnabled, "the fixture needs a live enabled listener; no listener settings are overridden");
                // Short known waveform exercises a true source loop; authored 32/48-second streams are exercised below.
                float[] samples = new float[ProbeSampleCount]; for (int i = 0; i < samples.Length; i++) samples[i] = .1f * Mathf.Sin(2 * Mathf.PI * 400 * i / ProbeSampleRate);
                clip = AudioClip.Create("known-periodic-probe", samples.Length, 1, ProbeSampleRate, false); Assert.IsTrue(clip.SetData(samples, 0));
                var readback = new float[samples.Length]; Assert.IsTrue(clip.GetData(readback, 0));
                report.knownClipEnergy = Energy(readback); Assert.Greater(report.knownClipEnergy, SignalFloor);
                silence = AudioClip.Create("known-silence-control", samples.Length, 1, ProbeSampleRate, false);
                Array.Clear(readback, 0, readback.Length); Assert.IsTrue(silence.SetData(readback, 0));
                int shortLoop = player.Register("probe-loop", clip, SoundBus.Music);
                int silentLoop = player.Register("probe-silence-control", silence, SoundBus.Music);
                Assert.IsTrue(player.SetMusic(shortLoop, 0)); yield return new WaitForSecondsRealtime(1.1f);
                var source = player.MusicSource(0); Assert.IsTrue(source.isPlaying); Assert.IsTrue(source.loop);
                Assert.That(source.timeSamples, Is.InRange(0, ProbeSampleCount - 1));
                // Unity 2022.3 explicitly starts per-source history only on the FIRST call:
                // https://docs.unity3d.com/2022.3/Documentation/ScriptReference/AudioSource.GetOutputData.html
                // Waiting before that call cannot populate the observer. Never lower the real signal gate.
                var output = new float[512];
                yield return ObserveSignalWindow(player, source, output, report, "positive-before", true);
                player.ResetPlayback(); Assert.IsTrue(player.SetMusic(silentLoop, 0));
                Assert.AreSame(source, player.MusicSource(0), "negative control must exercise the same native source/history");
                yield return ObserveSignalWindow(player, source, output, report, "silent-control", false);
                player.ResetPlayback(); Assert.IsTrue(player.SetMusic(shortLoop, 0));
                yield return ObserveSignalWindow(player, source, output, report, "positive-after", true);
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
                report.completed = true;
                Debug.Log("SPF_NATIVE_AUDIO_PROBE: source loop, signal, pause/resume, background, repeated/interrupting music transition and bounded-source lifecycle passed. Perceived listening quality and physical Android/iOS output remain unverified.");
            }
            finally
            {
                try { SaveReport(report); }
                finally { RenderObjects.Destroy(root); if (clip != null) RenderObjects.Destroy(clip); if (silence != null) RenderObjects.Destroy(silence); }
            }
        }

        const float SignalFloor = .0001f; // Unchanged original real-source energy requirement.
        const int RetainedReads = 8;
        const int ProbeSampleRate = 24000, ProbeSampleCount = 24060; // 401 complete 400 Hz cycles: 1.0025 s.
        // A >1-second control period cannot alias all eight reads inside a 3-second window;
        // this avoids the old 0.1-second loop matching a 10 Hz frame cadence.

        static float Energy(float[] output)
        {
            float energy = 0;
            foreach (float value in output)
            {
                Assert.IsFalse(float.IsNaN(value) || float.IsInfinity(value), "native audio samples must be finite");
                energy += value * value;
            }
            return energy;
        }

        static IEnumerator ObserveSignalWindow(SoundPlayer player, AudioSource source, float[] output, SignalReport report, string phase, bool expectSignal)
        {
            report.phase = phase;
            source.GetOutputData(output, 0); // Allocate/prime this source's history before any retained sample.
            report.Add(phase + "/prime", player, source, Energy(output));
            double startDsp = AudioSettings.dspTime;
            float deadline = Time.realtimeSinceStartup + 3f;
            int frame = Time.frameCount;
            // Wait at least a full configured queue + two observation windows, after priming.
            double warmSeconds = Math.Max(.1, (report.dspBufferFrames * (double)Math.Max(1, report.dspBufferCount) + output.Length * 2.0) / Math.Max(1, report.outputSampleRate));
            while (AudioSettings.dspTime - startDsp < warmSeconds || Time.frameCount == frame)
            {
                yield return null;
                if (Time.realtimeSinceStartup >= deadline)
                { report.phase = phase + "/warmup-timeout"; Assert.Fail("Audio history warmup did not cross the required DSP/frame boundary within 3 seconds."); }
            }
            int firstPosition = source.timeSamples; bool advanced = false;
            double spacing = Math.Max(report.dspBufferFrames, output.Length) / (double)Math.Max(1, report.outputSampleRate);
            double nextDsp = AudioSettings.dspTime + spacing;
            for (int i = 0; i < RetainedReads; i++)
            {
                // Fast render frames must not repeatedly sample one unchanged DSP history block.
                do
                {
                    yield return null;
                    if (Time.realtimeSinceStartup >= deadline)
                    { report.phase = phase + "/observation-timeout"; Assert.Fail("Audio signal observation exceeded its bounded 3-second window."); }
                } while (AudioSettings.dspTime < nextDsp);
                source.GetOutputData(output, 0); float energy = Energy(output);
                nextDsp = AudioSettings.dspTime + spacing;
                report.Add(phase + "/retained", player, source, energy);
                advanced |= source.timeSamples != firstPosition;
                Assert.IsTrue(report.listener != null && report.listener.isActiveAndEnabled, "the observed listener disappeared or became disabled during the signal window");
                Assert.IsTrue(source.isPlaying, "known looping control must remain playing");
                Assert.That(source.timeSamples, Is.InRange(0, ProbeSampleCount - 1));
                Assert.Greater(source.volume, 0f, "production player gain must remain nonzero in both controls");
                if (expectSignal) Assert.Greater(energy, SignalFloor, "the warmed real source output must contain signal in every retained window");
                else Assert.LessOrEqual(energy, 1e-8f, "the same observer must reject the known-silent clip after its old history is flushed");
            }
            Assert.IsTrue(advanced, "isPlaying and an in-range cursor alone do not establish actual playback progress");
        }

        [Serializable] sealed class SignalReport
        {
            public string unityVersion, speakerMode, phase;
            public bool batchMode, listenerPaused, completed;
            public float listenerVolume, knownClipEnergy;
            public int dspBufferFrames, dspBufferCount, outputSampleRate, realVoices, virtualVoices, count;
            public string[] previousListeners;
            [NonSerialized] public AudioListener listener;
            public Observation[] observations = new Observation[32];
            public void Add(string stage, SoundPlayer player, AudioSource source, float energy)
            {
                Assert.Less(count, observations.Length, "diagnostic observation capacity must stay bounded");
                observations[count++] = new Observation { stage = stage, frame = Time.frameCount, dspTime = AudioSettings.dspTime,
                    samplePosition = source.timeSamples, energy = energy, sourceVolume = source.volume, sourceMuted = source.mute,
                    playing = source.isPlaying, virtualized = source.isVirtual, playerMuted = player.Muted, gamePaused = player.Paused,
                    background = player.Background, focused = Application.isFocused, clipState = source.clip != null ? source.clip.loadState.ToString() : "no-clip",
                    listenerPaused = AudioListener.pause, listenerVolume = AudioListener.volume,
                    observedListenerAlive = listener != null, observedListenerEnabled = listener != null && listener.isActiveAndEnabled };
            }
        }
        [Serializable] sealed class Observation
        {
            public string stage, clipState;
            public int frame, samplePosition;
            public double dspTime;
            public float energy, sourceVolume, listenerVolume;
            public bool playing, virtualized, sourceMuted, playerMuted, gamePaused, background, focused, listenerPaused, observedListenerAlive, observedListenerEnabled;
        }
        static void SaveReport(SignalReport report)
        {
            Array.Resize(ref report.observations, report.count);
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Artifacts"));
            Directory.CreateDirectory(directory);
            string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(Path.Combine(directory, "audio-native-signal.json"), json);
            Debug.Log("SPF_AUDIO_SIGNAL_OBSERVATION: " + json);
        }
    }
}
#endif
