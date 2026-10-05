using NUnit.Framework;
using SPF.Presentation;
using SPF.Presentation.Audio;
using UnityEngine;

namespace SPF.Tests.EditMode
{
    public class AudioTests
    {
        [Test]
        public void SynthIsDeterministicBoundedAndShaped()
        {
            var def = SfxDef.Create(SfxWave.Square, 440f, 110f, 0.2f, 0.8f).WithNoise(0.3f, 0.2f).WithVibrato(1f, 12f);
            int n = SfxSynth.SampleCount(def, 22050);
            Assert.AreEqual(4410, n);
            var a = new float[n];
            var b = new float[n];
            SfxSynth.Render(def, 22050, a);
            SfxSynth.Render(def, 22050, b);
            CollectionAssert.AreEqual(a, b, "same definition, same samples");

            float peak = 0f, energyStart = 0f, energyEnd = 0f;
            for (int i = 0; i < n; i++)
            {
                Assert.LessOrEqual(Mathf.Abs(a[i]), 1f);
                peak = Mathf.Max(peak, Mathf.Abs(a[i]));
                if (i < n / 4) energyStart += a[i] * a[i];
                if (i >= n * 3 / 4) energyEnd += a[i] * a[i];
            }
            Assert.Greater(peak, 0.3f, "audible");
            Assert.Less(energyEnd, energyStart, "release fades out");
            Assert.Less(Mathf.Abs(a[n - 1]), 0.05f, "ends near silence (no click)");

            def.Seed = 2;
            def.NoiseMix = 1f;
            SfxSynth.Render(def, 22050, b);
            CollectionAssert.AreNotEqual(a, b, "the seed changes the noise");
        }

        [Test]
        public void PitchSweepChangesZeroCrossingRate()
        {
            var def = SfxDef.Create(SfxWave.Sine, 200f, 1600f, 0.5f, 1f).WithEnvelope(0f, 0f, 1f, 0f);
            var s = new float[SfxSynth.SampleCount(def, 22050)];
            SfxSynth.Render(def, 22050, s);
            int Crossings(int from, int to) { int c = 0; for (int i = from + 1; i < to; i++) if ((s[i - 1] < 0f) != (s[i] < 0f)) c++; return c; }
            int q = s.Length / 4;
            Assert.Greater(Crossings(3 * q, s.Length), 3 * Crossings(0, q), "pitch rises over the sound");
        }

        [Test]
        public void VoicePoolLimitsSpamAndStealsByPriority()
        {
            var pool = new VoicePool(4);
            int hit = pool.AddSound(maxVoices: 2, minInterval: 0.05f);
            int music = pool.AddSound(maxVoices: 1, minInterval: 0f);
            int boom = pool.AddSound(maxVoices: 4, minInterval: 0f);

            Assert.GreaterOrEqual(pool.Acquire(hit, 0f, 1f), 0);
            Assert.AreEqual(-1, pool.Acquire(hit, 0.01f, 1f), "retrigger interval");
            Assert.GreaterOrEqual(pool.Acquire(hit, 0.06f, 1f), 0);
            int third = pool.Acquire(hit, 0.12f, 1f);
            Assert.GreaterOrEqual(third, 0, "over the voice limit the oldest instance restarts");
            Assert.AreEqual(2, CountSound(pool, hit, 0.13f));

            Assert.GreaterOrEqual(pool.Acquire(music, 0.2f, 10f, priority: 5), 0);
            Assert.GreaterOrEqual(pool.Acquire(boom, 0.2f, 1f), 0);
            Assert.AreEqual(4, pool.ActiveVoices(0.25f));
            // Full: a low-priority sound steals the oldest low-priority voice, never the music.
            Assert.GreaterOrEqual(pool.Acquire(boom, 0.3f, 1f), 0);
            Assert.AreEqual(1, CountSound(pool, music, 0.31f), "high-priority voice kept");
            // Voices free up when their sounds end.
            Assert.AreEqual(1, pool.ActiveVoices(5f));
        }

        static int CountSound(VoicePool pool, int sound, float now)
        {
            int n = 0;
            for (int v = 0; v < pool.VoiceCount; v++) if (pool.SoundOf(v) == sound) n++;
            return n;
        }

        [Test]
        public void SoundPlayerRegistersPlaysAndMutes()
        {
            var go = new GameObject("AudioTest");
            try
            {
                var player = SoundPlayer.Create(go.transform, voices: 4);
                int blip = player.Register("blip", SfxDef.Create(SfxWave.Square, 880f, 1320f, 0.08f));
                Assert.AreEqual(blip, player.Find("blip"));
                Assert.IsTrue(player.Play(blip));
                Assert.AreEqual(1, player.Played);
                player.Muted = true;
                Assert.IsFalse(player.Play(blip, 1f, 1.2f), "muted");
                player.Muted = false;
                player.SetVolume(SoundBus.Sfx, 0.25f);
                Assert.AreEqual(0.25f, player.GetVolume(SoundBus.Sfx));
            }
            finally { RenderObjects.Destroy(go); }
        }
    }
}
