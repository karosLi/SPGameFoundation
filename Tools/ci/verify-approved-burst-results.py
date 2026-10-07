#!/usr/bin/env python3
"""Fail the setup gate on missing, managed-fallback, failed, or stale full-suite evidence."""
from pathlib import Path
import os
import xml.etree.ElementTree as ET

root = Path('Artifacts')
assert not list(root.glob('*noburst*')), 'A Burst-disabled fallback cannot validate this setup'
summary = []
for suite in ('editmode', 'playmode'):
    report = ET.parse(root / (suite + '-results.xml')).getroot()
    assert report.get('result') == 'Passed' and int(report.get('failed')) == 0, suite + ' failed'
    assert int(report.get('passed')) > 0, suite + ' executed no passing tests'
    summary.append(suite + ': ' + ', '.join(key + '=' + str(report.get(key)) for key in ('total', 'passed', 'failed', 'skipped')))
backend = (root / 'perf-physics-backend.txt').read_text()
assert 'EditorPrefs BurstCompilation: exists=True, value=True' in backend
assert 'direct managed Execute=0; Run Burst=1; Schedule Burst=1' in backend
assert 'actual physics first step Burst=True' in backend
physics = (root / 'perf-physics.txt').read_text()
assert 'actual Burst execution: warmup 60/60 steps; measured 300/300 steps' in physics, physics
summary.append(physics)
text = '\n'.join(summary)
print(text)
if os.environ.get('GITHUB_STEP_SUMMARY'):
    with open(os.environ['GITHUB_STEP_SUMMARY'], 'a') as stream:
        stream.write(text + '\n')
