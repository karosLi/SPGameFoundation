using System;
using Unity.Mathematics;
using UnityEngine;

namespace SPF.Presentation.Audio
{
    public enum SfxWave : byte { Square, Saw, Triangle, Sine, Noise }

    /// <summary>
    /// Parameters of a procedural sound effect (sfxr style): one oscillator with an exponential pitch
    /// sweep, vibrato, optional noise mix, an attack / decay / sustain / release envelope and a one-pole
    /// low-pass. Small games get their whole sound set from a few lines of data and no audio assets.
    /// </summary>
    [Serializable]
    public struct SfxDef
    {
        public SfxWave Wave;
        /// <summary>Start and end frequency (Hz); the sweep is exponential. End ≤ 0 keeps the start pitch.</summary>
        public float Frequency, FrequencyEnd;
        public float Duration;
        /// <summary>Envelope times in seconds; the level holds at <see cref="Sustain"/> between decay and release.</summary>
        public float Attack, Decay, Release;
        public float Sustain;
        /// <summary>Square duty cycle (0.5 = plain square).</summary>
        public float Duty;
        /// <summary>Vibrato depth (semitones) and rate (Hz).</summary>
        public float Vibrato, VibratoRate;
        /// <summary>0 = pure oscillator, 1 = pure noise (noise changes value once per oscillator period).</summary>
        public float NoiseMix;
        /// <summary>Low-pass smoothing in [0, 1): higher is duller.</summary>
        public float LowPass;
        public float Volume;
        public uint Seed;

        public static SfxDef Create(SfxWave wave, float frequency, float frequencyEnd, float duration, float volume = 0.5f)
        {
            return new SfxDef
            {
                Wave = wave, Frequency = frequency, FrequencyEnd = frequencyEnd, Duration = duration,
                Attack = 0.005f, Decay = duration * 0.3f, Sustain = 0.6f, Release = duration * 0.5f,
                Duty = 0.5f, Volume = volume, Seed = 1,
            };
        }

        public SfxDef WithEnvelope(float attack, float decay, float sustain, float release)
        {
            var d = this;
            d.Attack = attack; d.Decay = decay; d.Sustain = sustain; d.Release = release;
            return d;
        }

        public SfxDef WithNoise(float mix, float lowPass = 0f) { var d = this; d.NoiseMix = mix; d.LowPass = lowPass; return d; }
        public SfxDef WithVibrato(float semitones, float rate) { var d = this; d.Vibrato = semitones; d.VibratoRate = rate; return d; }
        public SfxDef WithDuty(float duty) { var d = this; d.Duty = duty; return d; }
    }

    /// <summary>Renders <see cref="SfxDef"/> to samples (deterministic) and to AudioClips.</summary>
    public static class SfxSynth
    {
        public const int DefaultSampleRate = 22050;

        public static int SampleCount(in SfxDef def, int sampleRate) => math.max(1, (int)math.ceil(def.Duration * sampleRate));

        /// <summary>Mono samples in [-1, 1]; fills min(output.Length, SampleCount) samples.</summary>
        public static void Render(in SfxDef def, int sampleRate, Span<float> output)
        {
            int count = math.min(output.Length, SampleCount(def, sampleRate));
            float duration = math.max(def.Duration, 1e-4f);
            float f0 = math.max(def.Frequency, 1f);
            float ratio = def.FrequencyEnd > 0f ? def.FrequencyEnd / f0 : 1f;
            float duty = def.Duty > 0f && def.Duty < 1f ? def.Duty : 0.5f;
            float smooth = math.clamp(def.LowPass, 0f, 0.99f);
            uint state = def.Seed == 0 ? 0x9E3779B9u : def.Seed * 0x9E3779B9u | 1u;
            float noise = NextNoise(ref state);
            double phase = 0;
            float filtered = 0f;
            float dt = 1f / sampleRate;
            for (int i = 0; i < count; i++)
            {
                float t = i * dt;
                float freq = f0 * math.pow(ratio, t / duration);
                if (def.Vibrato != 0f)
                    freq *= math.exp2(def.Vibrato / 12f * math.sin(2f * math.PI * def.VibratoRate * t));
                phase += freq * dt;
                if (phase >= 1.0)
                {
                    phase -= math.floor((float)phase);
                    noise = NextNoise(ref state);
                }
                float p = (float)phase;
                float wave = def.Wave switch
                {
                    SfxWave.Square => p < duty ? 1f : -1f,
                    SfxWave.Saw => 2f * p - 1f,
                    SfxWave.Triangle => 1f - 4f * math.abs(p - 0.5f),
                    SfxWave.Sine => math.sin(2f * math.PI * p),
                    _ => noise,
                };
                float x = math.lerp(wave, noise, math.saturate(def.NoiseMix));
                filtered += (1f - smooth) * (x - filtered);
                output[i] = math.clamp(filtered * Envelope(def, t) * def.Volume, -1f, 1f);
            }
        }

        static float Envelope(in SfxDef d, float t)
        {
            float level;
            if (d.Attack > 0f && t < d.Attack) level = t / d.Attack;
            else if (d.Decay > 0f && t < d.Attack + d.Decay) level = math.lerp(1f, d.Sustain, (t - d.Attack) / d.Decay);
            else level = d.Decay > 0f ? d.Sustain : 1f;
            float remaining = d.Duration - t;
            if (d.Release > 0f && remaining < d.Release) level *= math.max(remaining, 0f) / d.Release;
            return level;
        }

        static float NextNoise(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state & 0xFFFFFF) / (float)0x800000 - 1f;
        }

        public static AudioClip CreateClip(string name, in SfxDef def, int sampleRate = DefaultSampleRate)
        {
            var samples = new float[SampleCount(def, sampleRate)];
            Render(def, sampleRate, samples);
            var clip = AudioClip.Create(name, samples.Length, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
