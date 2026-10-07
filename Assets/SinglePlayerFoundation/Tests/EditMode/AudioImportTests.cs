#if UNITY_EDITOR && !SPF_DOTNET_HARNESS
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SPF.Tests.EditMode
{
    public sealed class AudioImportTests
    {
        [TestCase("knife_swing")] [TestCase("sword_swing")] [TestCase("bow_draw")] [TestCase("bow_release")]
        [TestCase("staff_cast")] [TestCase("impact_light")] [TestCase("impact_heavy")] [TestCase("hurt")]
        [TestCase("heal")] [TestCase("equip")] [TestCase("ui_confirm")] [TestCase("ui_cancel")]
        public void OriginalSfxImportsAsPreloadedMonoPcm(string name)
        {
            string path = "Assets/SinglePlayerFoundation/Resources/Audio/Sanctuary/" + name + ".wav";
            var importer = AssetImporter.GetAtPath(path) as AudioImporter; Assert.IsNotNull(importer, path);
            var settings = importer.defaultSampleSettings;
            Assert.AreEqual(AudioClipLoadType.DecompressOnLoad, settings.loadType);
            Assert.AreEqual(AudioCompressionFormat.PCM, settings.compressionFormat);
            Assert.AreEqual(AudioSampleRateSetting.OverrideSampleRate, settings.sampleRateSetting);
            Assert.AreEqual(24000, settings.sampleRateOverride); Assert.IsTrue(importer.forceToMono); Assert.IsTrue(settings.preloadAudioData);
            var clip = Resources.Load<AudioClip>("Audio/Sanctuary/" + name); Assert.IsNotNull(clip);
            Assert.AreEqual(1, clip.channels); Assert.AreEqual(24000, clip.frequency); Assert.AreEqual(AudioDataLoadState.Loaded, clip.loadState);
            var samples = new float[clip.samples]; Assert.IsTrue(clip.GetData(samples, 0));
            double sum = 0, square = 0; float peak = 0; foreach (float sample in samples) { sum += sample; square += sample * sample; peak = Mathf.Max(peak, Mathf.Abs(sample)); }
            Assert.Greater(peak, .5); Assert.Less(peak, .9); Assert.Less(System.Math.Abs(sum / samples.Length), .001);
            Assert.Greater(square / samples.Length, .001); Assert.Less(Mathf.Abs(samples[0]), .001); Assert.Less(Mathf.Abs(samples[samples.Length - 1]), .001);
        }
        [TestCase("exploration", 48)] [TestCase("combat", 32)]
        public void OriginalMusicImportsWithStreamingPolicy(string name, float duration)
        {
            var importer = AssetImporter.GetAtPath("Assets/SinglePlayerFoundation/Resources/Audio/Sanctuary/" + name + ".wav") as AudioImporter;
            Assert.IsNotNull(importer); var settings = importer.defaultSampleSettings;
            Assert.AreEqual(AudioClipLoadType.Streaming, settings.loadType); Assert.AreEqual(AudioCompressionFormat.Vorbis, settings.compressionFormat);
            Assert.AreEqual(.7f, settings.quality, 1e-5f); Assert.AreEqual(AudioSampleRateSetting.PreserveSampleRate, settings.sampleRateSetting);
            Assert.IsFalse(importer.forceToMono); Assert.IsFalse(settings.preloadAudioData);
            var clip = Resources.Load<AudioClip>("Audio/Sanctuary/" + name); Assert.IsNotNull(clip); Assert.AreEqual(2, clip.channels);
            Assert.AreEqual(44100, clip.frequency); Assert.AreEqual(duration, clip.length, .025f);
        }
    }
}
#endif
