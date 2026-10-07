#!/usr/bin/env python3
"""Run offline gates and save bounded evidence with exact checked file hashes."""
from datetime import datetime, timezone
import hashlib
import io
import json
from pathlib import Path
import platform
import sys
import unittest

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]


def main():
    start = datetime.now(timezone.utc)
    stream = io.StringIO()
    suite = unittest.defaultTestLoader.discover(str(HERE), pattern='test_audio.py')
    result = unittest.TextTestRunner(stream=stream, verbosity=2).run(suite)
    print(stream.getvalue(), end='')
    manifest = json.loads((HERE / 'manifest.json').read_text())
    files = [HERE / name for name in ('generate.py', 'test_audio.py', 'validate.py',
             'requirements.txt', 'budgets.json', 'score.json', 'events.json', 'manifest.json', 'LICENSE-CC0.txt')]
    for record in manifest['assets']:
        path = ROOT / record['file']
        files += [path, path.with_suffix('.wav.meta')]
    files += [ROOT / 'Docs/OriginalAudioAssets.md']
    evidence = {
        'scope': 'Exact uncommitted offline asset/source snapshot. Not Unity import, playback, perceptual, or physical-device acceptance.',
        'started_utc': start.isoformat(),
        'finished_utc': datetime.now(timezone.utc).isoformat(),
        'command': 'python Tools/AudioAuthoring/validate.py',
        'environment': {'python': platform.python_version(), 'system': platform.system(), 'machine': platform.machine(),
                        'numpy': manifest['numpy'], 'scipy': manifest['scipy']},
        'tests_run': result.testsRun,
        'failures': len(result.failures), 'errors': len(result.errors), 'skips': len(result.skipped),
        'passed': result.wasSuccessful() and not result.skipped,
        'notes': ['Full rerender test executes the generator twice with isolated outputs and compares every file byte.',
                  'Audition is tested as exact concatenation of shipped SFX plus documented silence.',
                  '4x interpolation is a numeric peak estimate, not a certified true-peak meter.',
                  'Listening and Unity/mobile target acceptance remain separate.'],
        'checked_file_sha256': {p.relative_to(ROOT).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in files},
        'test_log': stream.getvalue().splitlines(),
    }
    (HERE / 'validation.json').write_text(json.dumps(evidence, indent=2) + '\n')
    return 0 if evidence['passed'] else 1


if __name__ == '__main__':
    sys.exit(main())
