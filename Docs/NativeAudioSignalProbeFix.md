# Native audio signal observer correction

## Observed failure

Exact remote `3ac697` / source `5f86119`, native run `37617046417`: EditMode 1,410 pass / 0 fail / 5 skip; PlayMode 164 pass / 1 fail / 1 skip. The sole failure was `NativeAudioPlaybackTests.ActualSourcesLoopPauseResumeCrossfadeAndRemainBounded`: its first `AudioSource.GetOutputData` read had zero energy. DSP advancement, clip SetData, isPlaying, loop and cursor-range checks had passed. Those facts do not by themselves prove audible output or cursor advancement.

## Confirmed fixture/API mismatch

The version-matching [Unity 2022.3 GetOutputData documentation](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/AudioSource.GetOutputData.html) states that output-history allocation/recording starts on the first per-source call. Initially that history is empty. The old fixture waited 0.25 seconds **before** its first read, which could not warm a history that did not exist. This is a test-observer error, not evidence requiring a production fade or mixer change. The immediate music request has lane gain 1 and source gain 0.8 × 0.55 = 0.44.

## Bounded correction and controls

- Explicitly prime GetOutputData, then cross a frame boundary and a DSP interval covering the configured queue plus two observation windows (minimum 0.1 seconds, bounded by 3 seconds per phase).
- Retain eight actual-source output windows, spaced by at least one configured DSP buffer or 512-frame analysis window (whichever is longer), for a known 400 Hz sine, eight for known zero PCM on the **same AudioSource**, then eight for the sine again. Positive windows retain the original energy threshold `> 0.0001`; silent windows must be `<= 1e-8`. No muted global listener or settings workaround is used.
- Require sample-position changes, not merely an in-range value. Controls contain 401 full 400 Hz cycles (24,060 frames at 24 kHz, 1.0025 seconds) and the first playback runs longer than one loop. This period cannot alias all eight retained cursor reads within the 3-second phase bound; the old 0.1-second period could alias a 10 Hz frame cadence. Deadlines are checked immediately after every yielded frame. A stopped DSP now fails explicitly instead of being treated as output verification.
- Keep the existing listener topology unchanged; create the original fixture-owned listener only if none exists. Record existing enabled/active states and assert that the observed listener remains alive and enabled through each window. No existing listener is enabled/disabled or replaced on a hypothesis. No AudioListener.pause/volume, AudioSettings configuration, Editor preference, OS setting, microphone or runner setting changes.
- Write `Artifacts/audio-native-signal.json` and the same diagnostic log on pass or failure: primed/retained energies, frame/DSP times, source positions, volume/mute/virtualization/load state, player pause/background/focus state, initial listener states, DSP buffer configuration and completion flag. These diagnostic allocations are outside the retained playback allocation probe.

Production SoundPlayer, buses, music fades, samples and simulation are unchanged. API compilation is only API validation; the corrected signal test still needs the exact new commit executed by native Unity. Success must include both positive controls and the zero-PCM negative control, not merely a green compilation or continuing isPlaying flag. Physical speaker quality and mobile routing remain separate gates.

## Local validation

The corrected native fixture compiles against the actual Unity 2022.3.62f2 API with both `net8.0` and `netstandard2.1`: zero errors, seven pre-existing warnings per target. [Diagnosis record](validation/NativeAudioSignalProbeFix-20261007.json) preserves the original zero-energy NUnit failure, exact XML/fixture/log hashes, unchanged threshold, and the pending-native status. No production source changed; no new shared helper was extracted. The existing runtime aggregate is retained, not claimed as a new execution of this native fixture.
