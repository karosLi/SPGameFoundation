#!/usr/bin/env python3
"""Validate actual NUnit cases and native evidence, including Unity's ignored-root label."""
from pathlib import Path
import math
import os
import re
import xml.etree.ElementTree as ET

# These are the existing optional/graphics-only skips in this bounded CI snapshot.
ALLOWED_SKIPS = {
    'editmode': {
        'SlingFoundation.Tests.SlTests.SearchShots': 'Explicit',
        'SnakeFoundation.Tests.SnakePreviewExport.ExportFrames': 'Explicit',
        'SPF.Tests.EditMode.BufferTextAllocationProbeTests.WarmSinglelineAndMultilineMeshes_ReportAllocations': 'Explicit',
        'SPF.Tests.EditMode.SpriteTests.GpuSpriteUploadAccountingIncludesArgumentsAndResetsOnReuse': 'Ignored',
    },
    'playmode': {
        'SurvivorFoundation.Tests.PlayMode.SvAllocationCaptureTests.NormalGameplayAllocationCallstacks': 'Explicit',
    },
}
MINIMUM_CASES = {'editmode': 726, 'playmode': 123}


def verify(root):
    root = Path(root)
    assert not list(root.glob('*noburst*')), 'A Burst-disabled fallback cannot validate this setup'
    summary = []
    for suite in ('editmode', 'playmode'):
        report = ET.parse(root / (suite + '-results.xml')).getroot()
        assert report.tag == 'test-run', suite + ': missing NUnit test-run'
        # Unity 2022 can propagate a single Ignore to this aggregate label despite zero failures.
        assert report.get('result') in ('Passed', 'Skipped:Ignored'), suite + ': unexpected aggregate result'
        assert int(report.get('failed')) == 0 and int(report.get('inconclusive', '0')) == 0, suite + ' failed'
        cases = list(report.iter('test-case'))
        assert len(cases) >= MINIMUM_CASES[suite], suite + ': incomplete full suite'
        assert len(cases) == int(report.get('total')), suite + ': case count differs from counters'
        passed = 0
        skipped = 0
        for case in cases:
            result = case.get('result')
            if result == 'Passed':
                passed += 1
            elif result == 'Skipped':
                skipped += 1
                name = case.get('fullname')
                assert name in ALLOWED_SKIPS[suite], suite + ': unexpected skipped test ' + str(name)
                assert case.get('label') == ALLOWED_SKIPS[suite][name], suite + ': unexpected skip label'
            else:
                raise AssertionError(suite + ': nonpassing test ' + str(case.get('fullname')) + ': ' + str(result))
        assert passed > 0 and passed == int(report.get('passed')), suite + ': invalid passed count'
        assert skipped == int(report.get('skipped')), suite + ': invalid skipped count'
        summary.append(suite + ': ' + ', '.join(key + '=' + str(report.get(key)) for key in ('total', 'passed', 'failed', 'skipped')))
    backend = (root / 'perf-physics-backend.txt').read_text()
    for required in (
        'EditorPrefs BurstCompilation: exists=True, value=True',
        'BurstCompiler.IsEnabled=True; Options.IsEnabled=True; EnableBurstCompilation=True',
        'JobsUtility.JobCompilerEnabled=True',
        'direct managed Execute=0; Run Burst=1; Schedule Burst=1',
        'actual physics first step Burst=True',
    ):
        assert required in backend, 'Missing native backend evidence: ' + required
    physics = (root / 'perf-physics.txt').read_text()
    assert '=== Physics2D: 600 bodies poured into a box ===' in physics
    assert 'actual Burst execution: warmup 60/60 steps; measured 300/300 steps' in physics
    match = re.search(r'step ms mean (\S+)', physics)
    assert match, 'Missing measured physics mean'
    mean = float(match.group(1))
    assert math.isfinite(mean) and 0 <= mean < 4.0, 'Original 4 ms mean physics budget was not met'
    summary.append(physics)
    return '\n'.join(summary)


if __name__ == '__main__':
    text = verify('Artifacts')
    print(text)
    if os.environ.get('GITHUB_STEP_SUMMARY'):
        with open(os.environ['GITHUB_STEP_SUMMARY'], 'a') as stream:
            stream.write(text + '\n')
