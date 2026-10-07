#!/usr/bin/env python3
"""S1a launcher. Default is read-only static preflight; Unity needs an explicit gate."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shlex
import subprocess
import sys
import uuid
import xml.etree.ElementTree as ET
from datetime import datetime, timezone

PROJECT = Path(__file__).resolve().parents[1]
EDITOR_VERSION = "2022.3.62f2"
LATIOS_COMMIT = "381a77dbf774ff603014d5695ef6c06abaa25d96"
LATIOS_URL = "https://github.com/Dreaming381/Latios-Framework.git#" + LATIOS_COMMIT
PINS = {"com.unity.entities": "1.3.5", "com.unity.entities.graphics": "1.4.2",
        "com.unity.burst": "1.8.18", "com.unity.audio.dspgraph": "0.1.0-preview.22",
        "com.unity.render-pipelines.universal": "14.0.11", "com.unity.test-framework": "1.1.33"}

PINS.update({"com.unity.modules." + name: "1.0.0" for name in (
    "animation", "audio", "assetbundle", "imageconversion", "imgui", "jsonserialize",
    "particlesystem", "physics", "physics2d", "ui", "uielements", "unitywebrequest")})
PINS["com.unity.ugui"] = "1.0.0"


def require(condition, message):
    if not condition:
        raise ValueError(message)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def validate_paths(project):
    require(project.name == "Latios2022Lab", "Only the dedicated Latios2022Lab path is supported.")
    require(not project.is_symlink(), "The lab project cannot be a symlink.")
    for child in project.rglob("*"):
        require(not child.is_symlink(), "Lab symlink is forbidden: " + str(child))
    require((project / "Assets/Latios2022Tests").is_dir(), "Lab test assets are missing.")
    require(project != project.parent, "Invalid project root.")
    require(not (project / "Temp/UnityLockfile").exists(), "The lab already has a Unity project lock. Do not kill or reuse another editor.")


def check_lock(path):
    lock = json.loads(path.read_text())['dependencies']
    entry = lock['com.latios.latiosframework']
    require(entry.get('source') == 'git', "Latios must resolve as the pinned Git package.")
    require(entry.get('hash') == LATIOS_COMMIT, "Resolved Latios commit differs from the pin.")
    require(entry.get('version') == LATIOS_URL, "Resolved Latios URL differs from the pin.")
    for name, version in PINS.items():
        require(lock.get(name, {}).get('version') == version, "Unexpected resolved package: " + name)
    return lock


def parse_project_version(text):
    versions = re.findall(r'^m_EditorVersion:\s*(\S+)\s*$', text, re.MULTILINE)
    require(versions == [EDITOR_VERSION], 'Unexpected or ambiguous Editor pin.')
    revisions = re.findall(r'^m_EditorVersionWithRevision:\s*(.+)$', text, re.MULTILINE)
    require(len(revisions) <= 1, 'Ambiguous Editor revision record.')
    if revisions:
        require(revisions[0].split()[0] == EDITOR_VERSION, 'Editor revision version differs from the pin.')
    return revisions[0] if revisions else None


def check_defines(settings):
    match = re.search(r'(?ms)^  scriptingDefineSymbols:\s*\n(.*?)(?=^  \S|\Z)', settings)
    require(match is not None, 'Missing scripting define settings.')
    targets = dict(re.findall(r'^    (\w+):[ \t]*(.*)$', match.group(1), re.MULTILINE))
    required = {'ENTITY_STORE_V1', 'UNITY_BURST_EXPERIMENTAL_ATOMIC_INTRINSICS'}
    for target in ('Standalone', 'Android', 'iPhone'):
        symbols = {s.strip() for s in targets.get(target, '').strip('"').split(';')}
        require(required <= symbols, 'Missing define group: ' + target)


def preflight(project=PROJECT):
    validate_paths(project)
    revision = parse_project_version((project / 'ProjectSettings/ProjectVersion.txt').read_text())
    manifest = json.loads((project / 'Packages/manifest.json').read_text())
    require(manifest['dependencies'] == dict(PINS, **{'com.latios.latiosframework': LATIOS_URL}), 'Manifest pins drifted.')
    settings = (project / 'ProjectSettings/ProjectSettings.asset').read_text()
    check_defines(settings)
    assemblies = []
    for path in (project / 'Assets').rglob('*.asmdef'):
        data = json.loads(path.read_text())
        require(not any(name.startswith('SPF.') for name in data.get('references', [])), 'Lab cannot depend on SPF assemblies.')
        assemblies.append(data['name'])
    require(len(assemblies) == len(set(assemblies)) == 4, 'Expected four independent lab assemblies.')
    for path in (project / 'Assets').rglob('*'):
        if path.suffix != '.meta':
            require(Path(str(path) + '.meta').is_file(), 'Missing Unity metadata: ' + str(path))
    lock_path = project / 'Packages/packages-lock.json'
    if lock_path.exists():
        check_lock(lock_path)
    return {'status': 'STATIC_PREFLIGHT_ONLY', 'editor': EDITOR_VERSION, 'latios_commit': LATIOS_COMMIT,
            'project': str(project), 'editor_revision': revision, 'resolved_lock': 'present; pins checked' if lock_path.exists() else 'NOT_RESOLVED',
            'native': 'NOT_RUN', 'assemblies': sorted(assemblies)}


def read_gate(path, project, phase):
    require(path is not None, '--execute requires a coordinator-issued --gate file; P0 is not implicitly complete.')
    gate = json.loads(Path(path).read_text())
    require(gate.get('schema') == 1, 'Unsupported gate schema.')
    require(Path(gate.get('project_path', '')).resolve() == project, 'Gate targets another project.')
    require(gate.get('p0_native_status') == 'passed', 'P0 native closure is still pending or failed.')
    require(re.fullmatch('[0-9a-f]{40}', gate.get('p0_commit', '')) is not None, 'Gate requires the precise verified P0 commit.')
    require(gate.get('p0_evidence_url', '').startswith('https://'), 'Gate requires an inspected P0 evidence URL.')
    require(gate.get('runner_reserved') is True and bool(gate.get('coordinator')), 'Runner ownership is not confirmed.')
    expiry = datetime.fromisoformat(gate['expires_utc'].replace('Z', '+00:00'))
    require(expiry.tzinfo is not None and expiry > datetime.now(timezone.utc), 'Runner gate expired.')
    require(phase in gate.get('allowed_phases', []), 'This phase is not authorized in the runner gate.')
    if phase == 'player-build':
        require(gate.get('s1a_editor_status') == 'passed', 'Player build requires the actual S1a Editor gates first.')
    return gate


def make_command(editor, phase, output, burst, target):
    command = [str(editor), '-batchmode', '-projectPath', str(PROJECT), '-buildTarget', target,
               '-logFile', str(output / 'unity.log'), '--burst-force-sync-compilation']
    # Native Apple Silicon must not inherit an x64 runner's Rosetta architecture.
    # This selects launch architecture only; it does not modify system/Editor preferences.
    if sys.platform == 'darwin':
        # sysctl observes the physical host even when Python/runner is under Rosetta.
        arm64 = subprocess.check_output(['/usr/sbin/sysctl', '-in', 'hw.optional.arm64'], text=True).strip() == '1'
        command = ['/usr/bin/arch', '-arm64' if arm64 else '-x86_64'] + command
    if burst == 'off':
        command += ['--burst-disable-compilation']
    if phase == 'import':
        command += ['-quit', '-executeMethod', 'Latios2022Lab.LabEnvironment.CaptureEnvironment']
    elif phase == 'player-build':
        command += ['-quit', '-executeMethod', 'Latios2022Lab.LabPlayerBuild.BuildSmokePlayer']
    else:
        command += ['-runTests', '-testPlatform', 'EditMode' if phase == 'editmode' else 'PlayMode',
                    '-assemblyNames', 'Latios2022Lab.Editor' if phase == 'editmode' else 'Latios2022Lab.PlayMode',
                    '-testResults', str(output / 'tests.xml')]
        # Intentionally no -quit with -runTests, and no -nographics for PlayMode/reentry.
    return command


def verify_editor_binary(project_command, output):
    # Only after the external gate: ask identity without ever passing -projectPath.
    version_command = project_command[:project_command.index('-batchmode')] + ['-version']
    observed = subprocess.run(version_command, cwd=output, capture_output=True, text=True,
                              timeout=60, check=False)
    text = observed.stdout + '\n' + observed.stderr
    (output / 'editor-version.txt').write_text(text)
    require(observed.returncode == 0, 'Installed Editor version query failed; project launch blocked.')
    versions = set(re.findall(r'(?<!\w)\d{4}\.\d+\.\d+[abfp]\d+(?!\w)', text))
    require(versions == {EDITOR_VERSION}, 'Installed Editor is not exactly ' + EDITOR_VERSION + '; project launch blocked.')
    return {'command': version_command, 'version': EDITOR_VERSION}


def check_results(path, phase):
    root = ET.parse(path).getroot()
    cases = list(root.iter('test-case'))
    expected = 49 if phase == 'editmode' else 1
    require(len(cases) == expected, 'Unexpected discovery count: expected ' + str(expected) + ', got ' + str(len(cases)))
    require(all(case.get('result') == 'Passed' for case in cases), 'Failed, skipped, or inconclusive tests are not a pass.')
    require(root.get('result') == 'Passed', 'The test run did not pass.')
    return len(cases)


def product_fingerprint():
    return {str(path.relative_to(PROJECT.parent)): sha(path)
            for directory in ('Packages', 'ProjectSettings')
            for path in (PROJECT.parent / directory).rglob('*') if path.is_file()}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('phase', choices=['preflight', 'import', 'editmode', 'playmode', 'player-build'])
    parser.add_argument('--editor', type=Path)
    parser.add_argument('--burst', choices=['on', 'off'], default='on')
    parser.add_argument('--target', choices=['StandaloneOSX', 'StandaloneLinux64', 'StandaloneWindows64'], default='StandaloneOSX')
    parser.add_argument('--execute', action='store_true')
    parser.add_argument('--gate', type=Path)
    args = parser.parse_args(argv)
    print(json.dumps(preflight(), indent=2))
    if args.phase == 'preflight':
        require(not args.execute, 'Preflight is read-only. Select an explicit native phase to execute.')
        return 0
    require(args.editor is not None and args.editor.is_absolute(), 'Supply the installed Editor executable as an absolute --editor path.')
    require(args.phase != 'player-build' or args.burst == 'on', 'The first IL2CPP smoke requires Burst on.')
    output = PROJECT / 'Artifacts' / (datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ') + '-' + args.phase + '-' + args.burst + '-' + uuid.uuid4().hex[:8])
    command = make_command(args.editor, args.phase, output, args.burst, args.target)
    print('PLAN ONLY: ' + shlex.join(command))
    if not args.execute:
        return 0
    gate = read_gate(args.gate, PROJECT, args.phase)
    require(args.editor.is_file() and os.access(args.editor, os.X_OK), 'Editor executable is missing or not executable.')
    before = product_fingerprint()
    # Directory creation is an exclusive lab launch lock, never a process-killing mechanism.
    launch_lock = PROJECT / '.launch-lock'
    launch_lock.mkdir()
    try:
        output.mkdir(parents=True, exist_ok=False)
        installed_editor = verify_editor_binary(command, output)
        provenance = {'source_commit': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=PROJECT, text=True).strip(),
                      'source_status': subprocess.check_output(['git', 'status', '--porcelain'], cwd=PROJECT, text=True),
                      'inputs_sha256': {str(p.relative_to(PROJECT)): sha(p) for folder in ('Assets', 'Packages', 'ProjectSettings', 'Tools') for p in (PROJECT / folder).rglob('*') if p.is_file() and '__pycache__' not in p.parts},
                      'command': command, 'installed_editor': installed_editor, 'gate': gate, 'root_product_inputs_before': before}
        (output / 'launch.json').write_text(json.dumps(provenance, indent=2) + '\n')
        env = os.environ.copy()
        cache = PROJECT / '.upm-cache'
        env.update(LATIOS_LAB_OUTPUT=str(output), LATIOS_LAB_EXPECT_BURST=args.burst,
                   UPM_CACHE_ROOT=str(cache), UPM_NPM_CACHE_PATH=str(cache / 'npm'),
                   UPM_CACHE_PATH=str(cache / 'packages'), UPM_GIT_CACHE_PATH=str(cache / 'git'))
        code = subprocess.run(command, cwd=PROJECT, env=env, check=False).returncode
        require(product_fingerprint() == before, 'Root product Packages/ProjectSettings changed: stop and investigate.')
        require(code == 0, 'Unity failed with exit code ' + str(code) + '; preserve ' + str(output))
        check_lock(PROJECT / 'Packages/packages-lock.json')
        (output / 'lab-inputs-after.json').write_text(json.dumps({str(p.relative_to(PROJECT)): sha(p) for directory in ('Packages', 'ProjectSettings') for p in (PROJECT / directory).rglob('*') if p.is_file()}, indent=2) + '\n')
        if args.phase in ('editmode', 'playmode'):
            check_results(output / 'tests.xml', args.phase)
        elif args.phase == 'import':
            require((output / 'environment.json').is_file(), 'Import produced no verified environment inventory.')
        else:
            require('Result=Succeeded' in (output / 'build.txt').read_text(), 'No successful build report. Player execution remains NOT_RUN.')
        evidence = {str(p.relative_to(output)): sha(p) for p in output.rglob('*') if p.is_file()}
        (output / 'sha256.json').write_text(json.dumps(evidence, indent=2) + '\n')
        print('Phase evidence: ' + str(output))
        return 0
    finally:
        launch_lock.rmdir()


if __name__ == '__main__':
    try:
        sys.exit(main())
    except (ValueError, KeyError, OSError, ET.ParseError, subprocess.TimeoutExpired) as exc:
        print('BLOCKED: ' + str(exc), file=sys.stderr)
        sys.exit(2)
