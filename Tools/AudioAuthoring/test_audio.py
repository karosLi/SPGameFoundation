#!/usr/bin/env python3
"""Offline PCM/importer regression checks. Does not listen or execute Unity.

Run from any directory: python -m unittest discover -s Tools/AudioAuthoring -v
Full deterministic rerenders use temporary isolated outputs, never real assets.
"""
from copy import deepcopy
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import unittest
import wave

import numpy as np
import generate

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
BUDGETS = json.loads((HERE / 'budgets.json').read_text())
EXPECTED = {
    'exploration': (44100, 2, 48.0), 'combat': (44100, 2, 32.0),
    'knife_swing': (24000, 1, .25), 'sword_swing': (24000, 1, .39),
    'bow_draw': (24000, 1, .62), 'bow_release': (24000, 1, .40),
    'staff_cast': (24000, 1, .78), 'impact_light': (24000, 1, .25),
    'impact_heavy': (24000, 1, .48), 'hurt': (24000, 1, .48),
    'heal': (24000, 1, 1.15), 'equip': (24000, 1, .43),
    'ui_confirm': (24000, 1, .32), 'ui_cancel': (24000, 1, .30),
}


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read_pcm(path):
    with wave.open(str(path), 'rb') as audio:
        if audio.getsampwidth() != 2 or audio.getcomptype() != 'NONE':
            raise AssertionError('Expected uncompressed signed 16-bit PCM.')
        rate, channels, frames = audio.getframerate(), audio.getnchannels(), audio.getnframes()
        raw = np.frombuffer(audio.readframes(frames), dtype='<i2')
        if raw.size != frames * channels:
            raise AssertionError('Truncated PCM data.')
        x = raw.astype(np.float64) / 32768
        if channels > 1:
            x = x.reshape(-1, channels)
        return x, rate, channels


def numeric_failures(record, samples):
    """Separate validator so deliberately broken fixtures prove rejection."""
    failures = []
    music = record['loop']
    kind = 'bgm' if music else 'sfx'
    if not np.isfinite(samples).all():
        failures.append('non-finite')
    if record['peak_dbfs'] > BUDGETS[kind + '_sample_peak_dbfs_max']:
        failures.append('sample-peak')
    if record['oversampled_peak_dbfs'] > BUDGETS['oversampled_peak_dbfs_max']:
        failures.append('oversampled-peak')
    lo, hi = BUDGETS[kind + '_rms_dbfs_range']
    if not lo <= record['rms_dbfs'] <= hi:
        failures.append('rms')
    if np.max(np.abs(record['dc'])) > BUDGETS['absolute_dc_max']:
        failures.append('dc')
    if record['clipped_samples'] != 0:
        failures.append('clipping')
    if music:
        if record['endpoint_step'] > BUDGETS['bgm_seam_step_max']:
            failures.append('loop-step')
        if record['seam_slope_error'] > BUDGETS['bgm_seam_slope_error_max']:
            failures.append('loop-slope')
        if record['endpoint_step'] > record['max_adjacent_step']:
            failures.append('loop-step-outlier')
        if record['mono_rms_ratio'] < BUDGETS['bgm_mono_rms_ratio_min']:
            failures.append('mono-cancellation')
        if record['stereo_correlation'] > BUDGETS['bgm_stereo_correlation_max']:
            failures.append('dual-mono')
    else:
        if samples[0] != 0 or samples[-1] != 0:
            failures.append('nonzero-edge')
        if max(abs(samples[1]), abs(samples[-2])) > BUDGETS['sfx_edge_adjacent_step_max']:
            failures.append('edge-step')
    return failures


class AudioAssetTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.manifest = json.loads((HERE / 'manifest.json').read_text())
        cls.assets = {Path(r['file']).stem: r for r in cls.manifest['assets']}
        cls.pcm, cls.measured = {}, {}
        for name, record in cls.assets.items():
            path = ROOT / record['file']
            x, rate, channels = read_pcm(path)
            cls.pcm[name] = (x, rate, channels)
            cls.measured[name] = generate.stats(path, x, rate, name in generate.SCORE)

    def test_bank_is_complete_and_contains_no_unlisted_wav(self):
        self.assertEqual(set(EXPECTED), set(self.assets))
        self.assertEqual(set(EXPECTED), {p.stem for p in generate.DEST.glob('*.wav')})

    def test_headers_exact_frames_channels_rates_and_pcm_size(self):
        for name, (rate, channels, seconds) in EXPECTED.items():
            with self.subTest(asset=name):
                x, actual_rate, actual_channels = self.pcm[name]
                self.assertEqual((rate, channels), (actual_rate, actual_channels))
                self.assertEqual(round(seconds * rate), len(x))
                self.assertEqual(44 + x.size * 2, self.assets[name]['bytes'])
                self.assertEqual(seconds, self.assets[name]['duration_seconds'])

    def test_manifest_metrics_hashes_and_sources_match_disk(self):
        for name, expected in self.assets.items():
            with self.subTest(asset=name):
                self.assertEqual(expected, self.measured[name])
        self.assertEqual(self.manifest['generator_sha256'], sha(HERE / 'generate.py'))
        self.assertEqual(self.manifest['score_sha256'], sha(HERE / 'score.json'))
        self.assertEqual(self.manifest['events_sha256'], sha(HERE / 'events.json'))
        self.assertEqual(generate.SEED, self.manifest['seed'])

    def test_peak_rms_dc_clip_loop_and_mono_regression_gates(self):
        for name, record in self.measured.items():
            with self.subTest(asset=name):
                self.assertEqual([], numeric_failures(record, self.pcm[name][0]))

    def test_source_delivery_and_decoded_sfx_budgets(self):
        records = self.measured.values()
        self.assertLessEqual(sum(r['bytes'] for r in records), BUDGETS['all_source_wav_bytes_max'])
        self.assertLessEqual(sum(r['bytes'] for r in records if not r['loop']), BUDGETS['sfx_source_wav_bytes_max'])
        decoded = sum(r['float32_decoded_payload_bytes'] for r in records if not r['loop'])
        self.assertLessEqual(decoded, BUDGETS['sfx_float32_payload_bytes_max'])
        self.assertEqual(decoded, self.manifest['totals']['sfx_float32_decoded_payload_bytes'])
        for r in records:
            with self.subTest(asset=r['file']):
                self.assertLessEqual(r['bytes'], BUDGETS[('bgm' if r['loop'] else 'sfx') + '_file_bytes_max'])
                self.assertLess(r['bytes'], BUDGETS['delivery_file_bytes_exclusive_max'])

    def test_meta_enums_preload_guid_and_mobile_default_inheritance(self):
        guids = set()
        for name, record in self.assets.items():
            with self.subTest(asset=name):
                path = ROOT / record['file']
                text = path.with_suffix('.wav.meta').read_text()
                guid = re.search(r'^guid: ([a-f0-9]{32})$', text, re.M).group(1)
                expected = hashlib.md5(('SPGameFoundation/OriginalSanctuaryAudio/' + path.name).encode()).hexdigest()
                self.assertEqual(guid, expected)
                self.assertNotIn(guid, guids)
                guids.add(guid)
                music = record['loop']
                fields = {'loadType': 2 if music else 0, 'sampleRateSetting': 0 if music else 2,
                          'sampleRateOverride': 44100 if music else 24000,
                          'compressionFormat': 1 if music else 0, 'quality': .7 if music else 1,
                          'forceToMono': 0 if music else 1, 'normalize': 0,
                          'preloadAudioData': 0 if music else 1, 'loadInBackground': 0, 'ambisonic': 0, '3D': 0}
                for key, expected_value in fields.items():
                    match = re.findall(r'^\s+' + key + r': ([\d.]+)$', text, re.M)
                    self.assertEqual([float(expected_value)], [float(v) for v in match], key)
                self.assertIn('  platformSettingOverrides: {}\n', text)
                self.assertRegex(text, r'(?m)^    preloadAudioData: [01]$')
                self.assertNotRegex(text, r'(?m)^  preloadAudioData:')

    def test_score_events_have_exact_bar_counts_and_rendered_percussion_lengths(self):
        score = json.loads((HERE / 'score.json').read_text())
        events = json.loads((HERE / 'events.json').read_text())
        self.assertEqual(json.loads(json.dumps(generate.SCORE)), score)
        for name in generate.SCORE:
            spec = score[name]
            self.assertEqual(16, spec['bars'])
            self.assertEqual(spec['bars'], len(spec['chords']))
            self.assertEqual(spec['bars'], len(spec['melody']))
            self.assertEqual(EXPECTED[name][2], spec['bars'] * 4 * 60 / spec['bpm'])
            self.assertGreater(len(events[name]), 200)
            for inst, note, start, seconds, gain, pan in events[name]:
                self.assertTrue(0 <= start < EXPECTED[name][2])
                self.assertTrue(0 < seconds < 5)
                self.assertTrue(0 < gain <= 1 and -1 <= pan <= 1)
                if inst in ('frame', 'low', 'rim', 'shaker'):
                    rendered = generate.drum(inst, np.random.default_rng(1))
                    self.assertEqual(len(rendered) / generate.SR, seconds)
                else:
                    self.assertTrue(0 <= note <= 127)

    def test_qa_rejects_clipping_dc_silence_and_broken_loop_fixtures(self):
        base = self.measured['exploration']
        x = self.pcm['exploration'][0]
        for key, value, reason in [('clipped_samples', 1, 'clipping'),
                                   ('peak_dbfs', -.5, 'sample-peak'),
                                   ('oversampled_peak_dbfs', 0, 'oversampled-peak'),
                                   ('dc', [.01, .01], 'dc'),
                                   ('rms_dbfs', -80, 'rms'),
                                   ('endpoint_step', .3, 'loop-step'),
                                   ('seam_slope_error', .2, 'loop-slope'),
                                   ('mono_rms_ratio', .1, 'mono-cancellation')]:
            with self.subTest(fixture=reason):
                broken = deepcopy(base)
                broken[key] = value
                self.assertIn(reason, numeric_failures(broken, x))

    def test_export_refuses_nonfinite_overrange_and_invalid_shape(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'invalid.wav'
            for x in [np.array([0., 1.01]), np.array([0., np.nan]),
                      np.array([np.inf]), np.array([]), np.zeros((3, 3)), np.zeros((2, 2, 2))]:
                with self.subTest(shape=x.shape):
                    with self.assertRaises(ValueError):
                        generate.save(path, x, 24000)
                    self.assertFalse(path.exists())

    def test_two_full_rerenders_are_byte_identical_and_isolated(self):
        before = sha(HERE / 'manifest.json')
        with tempfile.TemporaryDirectory() as folder:
            outputs = [Path(folder) / 'first', Path(folder) / 'second']
            for out in outputs:
                result = subprocess.run([sys.executable, str(HERE / 'generate.py'), '--out', str(out)],
                                        cwd=folder, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                        text=True, timeout=180)
                self.assertEqual(0, result.returncode, result.stderr)
                regenerated = json.loads((out / 'authoring/manifest.json').read_text())
                for record in regenerated['assets']:
                    name = Path(record['file']).stem
                    self.assertEqual(self.assets[name]['sha256'], record['sha256'], name)
                    self.assertEqual((generate.DEST / (name + '.wav.meta')).read_bytes(),
                                     (out / (name + '.wav.meta')).read_bytes())
                for filename in ('score.json', 'events.json'):
                    self.assertEqual((HERE / filename).read_bytes(), (out / 'authoring' / filename).read_bytes())
                audition = out / 'audition/Sanctuary_SFX_Audition.wav'
                self.assertEqual(self.manifest['audition']['sha256'], sha(audition))
                self.assertLess(audition.stat().st_size, BUDGETS['delivery_file_bytes_exclusive_max'])
                reel, rate, channels = read_pcm(audition)
                self.assertEqual((24000, 1), (rate, channels))
                cursor = 0
                for cue, name in zip(self.manifest['audition_cues'], generate.SFX_DURATIONS):
                    clip = self.pcm[name][0]
                    self.assertEqual(name, cue['name'])
                    self.assertEqual(cursor / rate, cue['first_seconds'])
                    np.testing.assert_array_equal(reel[cursor:cursor + len(clip)], clip)
                    cursor += len(clip)
                    self.assertFalse(np.any(reel[cursor:cursor + rate // 2]))
                    cursor += rate // 2
                    self.assertEqual(cursor / rate, cue['second_seconds'])
                    np.testing.assert_array_equal(reel[cursor:cursor + len(clip)], clip)
                    cursor += len(clip)
                    self.assertFalse(np.any(reel[cursor:cursor + rate]))
                    cursor += rate
                self.assertEqual(len(reel), cursor)
            for path in outputs[0].rglob('*'):
                if path.is_file():
                    self.assertEqual(path.read_bytes(), (outputs[1] / path.relative_to(outputs[0])).read_bytes(), str(path))
        self.assertEqual(before, sha(HERE / 'manifest.json'))


if __name__ == '__main__':
    unittest.main(verbosity=2)
