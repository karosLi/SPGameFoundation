#!/usr/bin/env python3
"""Default-closed repeat import / Mac IL2CPP execution of unchanged, reviewed Lab source."""
import argparse
from datetime import datetime, timezone
import difflib
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import plistlib
import re
import shutil
import subprocess
import sys
import uuid

import lab

REPO = Path(__file__).resolve().parents[2]
SCHEMA = 'latios-followup-v1'
STAGES = {'repeat-clean-import': ['import'], 'mac-il2cpp-smoke': ['import', 'player-build', 'player-run']}
GENERATED_SCENE = {'Assets/Latios2022Tests/Generated.meta', 'Assets/Latios2022Tests/Generated/Smoke.unity',
                   'Assets/Latios2022Tests/Generated/Smoke.unity.meta'}
require = lab.require


def module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


ci = module('latios_followup_ci', REPO / 'Tools/ci/latios_lab_ci.py')


def canonical(path):
    path = Path(path)
    require(path.is_absolute() and str(path) == str(path.resolve()), 'Use an absolute canonical path.')
    require(not any(p.is_symlink() for p in (path, *path.parents)), 'Symlink ancestry is forbidden.')
    return path


def read_bundle(binding):
    require(isinstance(binding, dict) and set(binding) == {'root', 'manifest_sha256', 'reviewed', 'run_url'},
            'Incomplete evidence binding.')
    require(binding['reviewed'] is True, 'Coordinator must review original source/package/native evidence.')
    root = canonical(binding['root'])
    manifest = root / 'evidence-sha256.json'
    require(manifest.is_file() and ci.digest(manifest) == binding['manifest_sha256'], 'Evidence manifest hash mismatch.')
    files = json.loads(manifest.read_text())
    require(isinstance(files, dict) and 0 < len(files) <= 50000, 'Unbounded evidence manifest.')
    actual = set()
    for path in root.rglob('*'):
        canonical(path)
        if path.is_file():
            actual.add(path.relative_to(root).as_posix())
        require(len(actual) <= 50001, 'Unbounded evidence inventory.')
    require(actual == set(files) | {'evidence-sha256.json'}, 'Evidence inventory is incomplete or has extra files.')
    total = 0
    for name, expected in files.items():
        require(isinstance(name, str) and '\\' not in name and not Path(name).is_absolute()
                and all(part not in ('', '.', '..') for part in name.split('/')), 'Unsafe evidence member.')
        path = canonical(root / name)
        total += path.stat().st_size
        require(total <= 2 * 1024**3, 'Evidence exceeds the bounded 2 GiB review limit.')
        require(ci.digest(path) == expected, 'Evidence file hash mismatch: ' + name)
    return root


def source_inputs(repo, commit):
    result = {}
    for row in ci.git(repo, 'ls-tree', '-r', commit, '--', 'Latios2022Lab').splitlines():
        header, name = row.split('\t', 1)
        require(header.split()[0] not in ('120000', '160000'), 'Native source symlinks/submodules are forbidden.')
        relative = name.removeprefix('Latios2022Lab/')
        if relative.startswith(('Assets/', 'Tools/', 'Variants/')):
            data = subprocess.check_output(['git', 'show', commit + ':' + name], cwd=repo)
            result[relative] = hashlib.sha256(data).hexdigest()
    require(result, 'Native source is unavailable.')
    return result


def frozen_inputs(project):
    return {name: digest for name, digest in ci.lab_source_fingerprint(project).items() if name not in GENERATED_SCENE}


def check_native_identity(summary, gate):
    require(summary.get('source_commit') == gate['source_commit'] and summary.get('source_tree') == gate['source_tree'],
            'Native evidence belongs to another exact source.')
    require(summary.get('experiment') == gate['experiment'] and summary.get('evidence_label') == 'patched-local-variant',
            'Native evidence is not the separately labelled patched arm.')
    require(not any(summary.get(key) for key in ('error', 'experiment_final_errors', 'evidence_collection_error', 'final_integrity_error')),
            'Native evidence contains unresolved integrity errors.')


def check_archived_record(record, gate):
    require(record.get('source_commit') == gate['source_commit'] and record.get('experiment') == gate['experiment'],
            'Archived experiment/source binding differs.')
    workspace = Path(record['workspace'])
    package = workspace / 'variant-packages/com.latios.latiosframework'
    require(workspace.is_absolute() and record['project_path'] == str(workspace / 'source/Latios2022Lab')
            and record['package_path'] == str(package) and record['manifest_url'] == 'file:' + str(package),
            'Archived experiment package paths differ.')
    return package


def check_archived_packages(output, record, gate):
    # Archived paths describe the original Mac run; never require or reopen that old workspace.
    variant = lab.variant_tools()
    package = check_archived_record(record, gate)
    baseline_path = lab.PROJECT / variant.BASELINE_LOCK
    require(ci.digest(baseline_path) == variant.BASELINE_LOCK_SHA256 == record['baseline_lock_sha256'], 'Frozen lock anchor differs.')
    baseline = json.loads(baseline_path.read_text())['dependencies']
    lock = json.loads((output / 'packages-lock.json').read_text())['dependencies']
    expected = dict(baseline)
    expected[variant.PACKAGE_NAME] = dict(version=record['manifest_url'], source='local', depth=0,
                                          dependencies=baseline[variant.PACKAGE_NAME]['dependencies'])
    require(lock == expected, 'Archived full dependency graph differs.')
    environment = json.loads((output / 'environment.json').read_text())
    rows = environment['packages']
    packages = {row['name']: row for row in rows}
    require(len(packages) == len(rows), 'Duplicate archived registered package.')
    for name, version in lab.RESOLVED_PINS.items():
        require(packages.get(name, {}).get('version') == version, 'Archived package version differs: ' + name)
    actual = packages.get(variant.PACKAGE_NAME, {})
    require(all(actual.get(key) == value for key, value in dict(version='0.11.5', source='Local',
                resolvedPath=str(package), packageId=variant.PACKAGE_NAME + '@' + record['manifest_url']).items()),
            'Archived registered Latios provenance differs.')
    require(environment.get('experimentId') == gate['experiment']['id']
            and environment.get('experimentArm') == gate['experiment']['arm'], 'Archived environment arm differs.')
    return environment


def phase_outputs(root, phase, burst):
    result = []
    for path in (root / 'native').glob('*/launch.json'):
        launch = json.loads(path.read_text())
        command = launch.get('command', [])
        entry = {'import': 'Latios2022Lab.LabEnvironment.CaptureEnvironment',
                 'player-build': 'Latios2022Lab.LabPlayerBuild.BuildSmokePlayer',
                 'editmode': 'EditMode', 'playmode': 'PlayMode'}[phase]
        if entry in command and ('--burst-disable-compilation' in command) == (burst == 'off'):
            result.append(path.parent)
    require(len(result) == 1, 'Expected one exact native output for ' + phase + '/' + burst)
    return result[0]


def check_phase_output(output, gate, phase, burst, inputs):
    launch = json.loads((output / 'launch.json').read_text())
    require(launch.get('source_commit') == gate['source_commit'], 'Native launch source mismatch.')
    require(launch.get('installed_editor', {}).get('version') == lab.EDITOR_VERSION, 'Wrong native Editor.')
    require(launch.get('experiment', {}).get('experiment') == gate['experiment'], 'Native launch arm mismatch.')
    record = launch['experiment']
    check_archived_record(record, gate)
    runtime_gate = launch.get('gate', {})
    require(runtime_gate.get('source_commit') == gate['source_commit'] and runtime_gate.get('experiment') == gate['experiment']
            and runtime_gate.get('project_path') == record['project_path'], 'Archived launch gate/source/project differs.')
    binding = runtime_gate.get('experiment_record', {})
    require(binding.get('path') == record['workspace'] + '/experiment-record.json'
            and re.fullmatch('[0-9a-f]{64}', binding.get('sha256', '')), 'Archived sealed-record binding differs.')
    version_command = launch['installed_editor'].get('command', [])
    require(len(version_command) == 4 and version_command[0] == '/usr/bin/arch'
            and version_command[1] in ('-arm64', '-x86_64') and Path(version_command[2]).is_absolute()
            and version_command[3:] == ['-version'], 'Archived native Editor command is invalid.')
    native_output = Path(record['project_path']) / 'Artifacts' / output.name
    expected_command = version_command[:-1] + ['-batchmode', '-projectPath', record['project_path'], '-buildTarget', 'StandaloneOSX',
                       '-logFile', str(native_output / 'unity.log'), '--burst-force-sync-compilation']
    if burst == 'off':
        expected_command += ['--burst-disable-compilation']
    if phase == 'import':
        expected_command += ['-quit', '-executeMethod', 'Latios2022Lab.LabEnvironment.CaptureEnvironment']
    elif phase == 'player-build':
        expected_command += ['-quit', '-executeMethod', 'Latios2022Lab.LabPlayerBuild.BuildSmokePlayer']
    else:
        expected_command += ['-runTests', '-testPlatform', 'EditMode' if phase == 'editmode' else 'PlayMode',
                             '-assemblyNames', 'Latios2022Lab.Editor' if phase == 'editmode' else 'Latios2022Lab.PlayMode',
                             '-testResults', str(native_output / 'tests.xml')]
    require(launch.get('command') == expected_command, 'Archived native phase command/project/target differs.')
    expected = {name: digest for name, digest in inputs.items() if name.startswith(('Assets/', 'Tools/'))}
    actual = {name: digest for name, digest in launch.get('inputs_sha256', {}).items()
              if name.startswith(('Assets/', 'Tools/')) and name not in ('Assets/UniversalRenderPipelineGlobalSettings.asset',
                                                                      'Assets/UniversalRenderPipelineGlobalSettings.asset.meta')}
    require(actual == expected, 'Native source input inventory differs.')
    integrity = json.loads((output / 'post-run-integrity.json').read_text())
    require(integrity.get('unity_exit_code') == 0 and integrity.get('root_product_inputs_unchanged') is True
            and integrity.get('secondary_errors') == {}, 'Native phase integrity/exit did not pass.')
    require((output / 'unity.log').is_file() and (output / 'unity.log').stat().st_size > 0, 'Original native log is missing.')
    if phase in ('editmode', 'playmode'):
        lab.check_results(output / 'tests.xml', phase)
    elif phase == 'import':
        record = launch['experiment']
        environment = check_archived_packages(output, record, gate)
        require(environment.get('editor') == lab.EDITOR_VERSION and environment.get('buildTarget') == 'StandaloneOSX'
                and environment.get('burstEnabled') is True and environment.get('burstSafety') is True,
                'Import environment/target/Burst/safety mismatch.')
        require(environment.get('experimentRecordSha256') == launch['gate']['experiment_record']['sha256'],
                'Native environment sealed-record mismatch.')
    return launch


def check_gate(gate, repo=REPO):
    require(gate.get('schema') == SCHEMA and gate.get('stage') in STAGES, 'Unknown conditional gate schema/stage.')
    require(gate.get('allowed_phases') == STAGES[gate['stage']], 'Conditional gate phases differ from its exact stage.')
    require(gate.get('target') == 'StandaloneOSX' and gate.get('burst') == 'on', 'Only Mac Burst-on is selected.')
    require(gate.get('architecture') in ('arm64', 'x86_64'), 'Explicit selected Mac player architecture is required.')
    for name in ('tool_commit', 'source_commit', 'source_tree', 'p0_commit'):
        require(re.fullmatch('[0-9a-f]{40}', gate.get(name, '')), 'Missing exact ' + name)
    require(ci.git(repo, 'rev-parse', 'HEAD') == gate['tool_commit'], 'Preparation tool revision differs from the gate.')
    require(not ci.git(repo, 'status', '--porcelain', '--untracked-files=normal'), 'Preparation checkout must be clean.')
    require(ci.git(repo, 'rev-parse', gate['source_commit'] + '^{tree}') == gate['source_tree'], 'Native source tree mismatch.')
    subprocess.run(['git', 'merge-base', '--is-ancestor', gate['p0_commit'], gate['source_commit']], cwd=repo, check=True)
    changed = ci.git(repo, 'diff', '--name-only', gate['p0_commit'], gate['source_commit'], '--').splitlines()
    require(all(name.startswith('Latios2022Lab/') or name in ci.ALLOWED_FILES for name in changed),
            'Native source includes product/unreviewed changes beyond P0.')
    if ci.ROOT_WORKFLOW in changed:
        old = ci.git(repo, 'show', gate['p0_commit'] + ':' + ci.ROOT_WORKFLOW)
        new = ci.git(repo, 'show', gate['source_commit'] + ':' + ci.ROOT_WORKFLOW)
        require(old.count('  push:\n') == 1 and ci.EXCLUSION not in old
                and new == old.replace('  push:\n', '  push:\n' + ci.EXCLUSION, 1), 'Root workflow delta exceeds its exact exclusion.')
    require(gate.get('experiment') == lab.variant_tools().experiment_for_arm('patched-local-variant'),
            'Only the fixed one-line patched arm is selected.')
    p0 = dict(gate, schema=1, approved_source_commit=gate['source_commit'], allowed_phases=['import', 'editmode', 'playmode'])
    ci.check_request(p0, gate.get('repository', ''), ci.BRANCH)
    inputs = source_inputs(repo, gate['source_commit'])
    root = read_bundle(gate['editor_evidence'])
    summary = json.loads((root / 'ci-summary.json').read_text())
    check_native_identity(summary, gate)
    require(summary.get('editor_control') == 'PASSED', 'All patched Editor controls must pass first.')
    phases = summary.get('phases', [])
    require([(p.get('phase'), p.get('burst')) for p in phases] == list(ci.PHASES), 'Editor phases/order differ.')
    for phase in phases:
        require(phase.get('status') == 'PASSED' and phase.get('exit_code') == 0
                and not any(phase.get(key) for key in ('post_phase_errors', 'evidence_collection_error',
                                                     'experiment_before_errors', 'experiment_after_errors')),
                'A required Editor phase failed, skipped, or is incomplete.')
        launch = check_phase_output(phase_outputs(root, phase['phase'], phase['burst']), gate, phase['phase'], phase['burst'], inputs)
        if phase['phase'] == 'import':
            original_record, original_binding = launch['experiment'], launch['gate']['experiment_record']
        else:
            require(launch['experiment'] == original_record and launch['gate']['experiment_record'] == original_binding,
                    'Editor phases used different sealed experiment records/workspaces.')
    if gate['stage'] == 'mac-il2cpp-smoke':
        repeat_root = read_bundle(gate.get('repeat_evidence', {}))
        require(repeat_root != root, 'Repeat import must have separate evidence/project/cache.')
        repeat = json.loads((repeat_root / 'followup-summary.json').read_text())
        check_native_identity(repeat, gate)
        require(repeat.get('stage') == 'repeat-clean-import' and repeat.get('status') == 'PASSED'
                and repeat.get('fresh_project') is True and repeat.get('repeat_clean_import') == 'PASSED',
                'Fresh repeat-clean-import has not passed.')
        # CI restores identical immutable evidence into different run-specific review directories.
        require({k: v for k, v in repeat.get('editor_evidence', {}).items() if k != 'root'}
                == {k: v for k, v in gate['editor_evidence'].items() if k != 'root'},
                'Repeat used different Editor prerequisites.')
        repeated = check_phase_output(phase_outputs(repeat_root, 'import', 'on'), gate, 'import', 'on', inputs)
        original = json.loads((phase_outputs(root, 'import', 'on') / 'launch.json').read_text())
        require(repeated['experiment']['workspace'] != original['experiment']['workspace']
                and repeated['experiment']['project_path'] != original['experiment']['project_path'],
                'Repeat import reused the original project/cache.')
    return inputs


def check_remote(gate, fetch=ci.api_get):
    p0 = dict(gate, schema=1, approved_source_commit=gate['source_commit'], allowed_phases=['import', 'editmode', 'playmode'])
    ci.check_p0(p0, gate['repository'], ci.BRANCH, fetch)
    match = re.fullmatch(r'https://github\.com/' + re.escape(gate['repository']) + r'/actions/runs/([1-9][0-9]*)/attempts/([1-9][0-9]*)',
                         gate['editor_evidence']['run_url'])
    require(match, 'Editor evidence must identify an exact native workflow run/attempt.')
    run = fetch(gate['repository'], 'runs/{}/attempts/{}'.format(*match.groups()))
    require(run.get('id') == int(match[1]) and run.get('run_attempt') == int(match[2])
            and run.get('repository', {}).get('full_name') == gate['repository'] and run.get('head_sha') == gate['source_commit']
            and run.get('path') == ci.WORKFLOW and run.get('status') == 'completed' and run.get('conclusion') == 'success',
            'Editor prerequisite is not a successful exact-source Lab native run.')
    ci.check_queue(gate['repository'], fetch)
    for status in ('queued', 'in_progress', 'waiting', 'pending', 'requested'):
        runs = fetch(gate['repository'], 'workflows/latios-lab.yml/runs?status=' + status + '&per_page=1')
        require(runs.get('total_count') == 0, 'Lab native queue is not released: ' + status)


def check_player_result(result):
    expected = {'editor': lab.EDITOR_VERSION, 'platform': 'OSXPlayer', 'burst': True, 'passed': True,
                'message': 'Core collection chain and Psyshock array/query smoke passed.'}
    require(set(result) == set(expected) | {'cpu'} and all(type(result.get(k)) is type(v) and result.get(k) == v for k, v in expected.items())
            and isinstance(result.get('cpu'), str) and bool(result['cpu'].strip()), 'Actual frozen player smoke result did not pass.')


def check_build_report(path, project):
    report = dict(line.split('=', 1) for line in path.read_text().splitlines())
    require(all(report.get(k) == v for k, v in {'Target': 'StandaloneOSX', 'Backend': 'IL2CPP', 'Result': 'Succeeded',
                'Errors': '0', 'Execution': 'NOT_RUN', 'Output': str(project / 'Builds/IL2CPP/Latios2022Lab.app')}.items()),
            'No exact successful Mac IL2CPP build report; missing module/SDK is a blocker, never permission to install or use Mono.')
    settings = (project / 'ProjectSettings/ProjectSettings.asset').read_text()
    backend = re.findall(r'(?m)^  scriptingBackend:\s*\n((?:    [^\n]*\n)+)', settings)
    require(len(backend) == 1 and re.findall(r'(?m)^    Standalone: ([0-9]+)$', backend[0]) == ['1'],
            'Actual normalized Standalone scripting backend is not IL2CPP.')


def check_architecture(executable, selected, output):
    require(selected in ('arm64', 'x86_64'), 'Unsupported selected architecture.')
    command = ['/usr/bin/lipo', '-archs', str(executable)]
    result = subprocess.run(command, text=True, capture_output=True, timeout=30, check=False)
    record = dict(command=command, exit_code=result.returncode, stdout=result.stdout, stderr=result.stderr, selected=selected)
    ci.write_json(output / 'player-architecture.json', record)
    require(result.returncode == 0 and selected in result.stdout.split(), 'Built app lacks the selected architecture; no substitution.')
    return record


def build_inventory(bundle):
    canonical(bundle)
    require(bundle.is_dir(), 'Built application is missing.')
    result = {}
    for path in sorted(bundle.rglob('*')):
        name = path.relative_to(bundle).as_posix()
        require(path.resolve().is_relative_to(bundle), 'Built app contains an external symlink.')
        if path.is_symlink():
            require(path.exists(), 'Built app contains a broken symlink.')
            result[name] = {'symlink': os.readlink(path)}
        elif path.is_file():
            result[name] = {'sha256': ci.digest(path), 'bytes': path.stat().st_size}
    require(result, 'Built application is empty.')
    return result


def configure_identity(project, run_id, home):
    require(re.fullmatch('[0-9a-f]{32}', run_id), 'Invalid execution nonce.')
    company, product = 'Latios2022Lab-' + run_id, 'ConditionalSmoke'
    support = canonical(home / 'Library/Application Support')
    paths = [support / company / product, support / ('unity.' + company + '.' + product),
             home / 'Library/Caches' / ('unity.' + company + '.' + product)]
    for path in paths:
        canonical(path)
        require(not path.exists(), 'Refusing existing application data for this run.')
    path = project / 'ProjectSettings/ProjectSettings.asset'
    before = path.read_text()
    after = before
    for key, value in (('companyName', company), ('productName', product)):
        after, count = re.subn(r'(?m)^  ' + key + r':[^\n]*$', '  ' + key + ': ' + value, after)
        require(count == 1, 'Ambiguous/missing normalized application identity: ' + key)
    # Only this owned, disposable lab is changed. The frozen C# and all oracles stay byte-identical.
    paths[0].mkdir(parents=True, exist_ok=False)
    path.write_text(after)
    return {'company': company, 'product': product, 'run_id': run_id, 'data_path': str(paths[0]), 'alternate_paths': [str(p) for p in paths[1:]],
            'settings_before_sha256': hashlib.sha256(before.encode()).hexdigest(), 'settings_after_sha256': ci.digest(path)}


def run_process(command, cwd, log, env, seconds):
    with log.open('xb') as stream:
        process = subprocess.Popen(command, cwd=cwd, env=env, stdout=stream, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            return process.wait(timeout=seconds)
        except subprocess.TimeoutExpired:
            ci.terminate_owned_group(process)
            return 124
        except BaseException:
            ci.terminate_owned_group(process)
            raise


def native_phase(native, phase, project, editor, output, gate, record, baseline):
    output.mkdir(parents=True, exist_ok=False)
    native.preflight(project, record)
    lock_dir = project / '.launch-lock'
    lock_dir.mkdir()
    try:
        command = native.make_command(editor, phase, output, 'on', 'StandaloneOSX')
        installed = native.verify_editor_binary(command, output)
        ci.write_json(output / 'launch.json', {'source_commit': gate['source_commit'], 'source_tree': gate['source_tree'],
                      'tool_commit': gate['tool_commit'], 'command': command, 'installed_editor': installed, 'gate': gate,
                      'inputs_sha256': ci.lab_source_fingerprint(project), 'experiment': record})
        env = {k: v for k, v in os.environ.items() if k != 'GITHUB_TOKEN' and not k.startswith('LATIOS_LAB_')}
        cache = project / '.upm-cache'
        env.update(LATIOS_LAB_OUTPUT=str(output), LATIOS_LAB_EXPECT_BURST='on',
                   LATIOS_LAB_EXPERIMENT_ID=record['experiment']['id'], LATIOS_LAB_EXPERIMENT_ARM=record['experiment']['arm'],
                   LATIOS_LAB_EXPERIMENT_RECORD_SHA256=gate['experiment_record']['sha256'], LATIOS_LAB_PACKAGE_PATH=record['package_path'],
                   UPM_CACHE_ROOT=str(cache), UPM_NPM_CACHE_PATH=str(cache / 'npm'), UPM_CACHE_PATH=str(cache / 'packages'), UPM_GIT_CACHE_PATH=str(cache / 'git'))
        code = run_process(command, project, output / 'console.log', env, ci.MAX_SECONDS)
        errors = {}
        try:
            native.variant_tools().verify(project, record)
            require(ci.product_fingerprint(project.parent) == baseline, 'Product inputs changed.')
        except Exception as error:
            errors['integrity'] = str(error)
        ci.write_json(output / 'post-run-integrity.json', {'unity_exit_code': code, 'secondary_errors': errors,
                      'root_product_inputs_unchanged': ci.product_fingerprint(project.parent) == baseline})
        require(code == 0, 'Native ' + phase + ' failed with exit ' + str(code) + '; preserve its original logs.')
        require(not errors, 'Post-phase integrity failed: ' + str(errors))
        lock = native.check_lock(project / 'Packages/packages-lock.json', record)
        native.check_manifest(json.loads((project / 'Packages/manifest.json').read_text()), lock, record)
        if phase == 'import':
            check_phase_output(output, gate, phase, 'on', source_inputs(REPO, gate['source_commit']))
        else:
            check_build_report(output / 'build.txt', project)
    finally:
        lock_dir.rmdir()


def run_player(project, output, gate, identity):
    require(identity['run_id'] == gate['run_id'] and identity['company'] == 'Latios2022Lab-' + gate['run_id']
            and identity['product'] == 'ConditionalSmoke', 'Player identity belongs to another run.')
    require(ci.git(project, 'rev-parse', 'HEAD') == gate['source_commit']
            and ci.git(project, 'rev-parse', 'HEAD^{tree}') == gate['source_tree'], 'Player source binding differs.')
    bundle = project / 'Builds/IL2CPP/Latios2022Lab.app'
    inventory = build_inventory(bundle)
    ci.write_json(output / 'player-build-sha256.json', inventory)
    info = plistlib.loads((bundle / 'Contents/Info.plist').read_bytes())
    name = info.get('CFBundleExecutable', '')
    require(isinstance(name, str) and re.fullmatch('[A-Za-z0-9_.-]+', name), 'Unsafe app executable name.')
    executable = canonical(bundle / 'Contents/MacOS' / name)
    require(executable.is_file() and os.access(executable, os.X_OK), 'Built player executable is unavailable.')
    architecture = check_architecture(executable, gate['architecture'], output)
    data = canonical(identity['data_path'])
    require(data.is_dir() and not list(data.iterdir()) and all(not Path(p).exists() for p in identity['alternate_paths']),
            'Refusing existing player output/application data.')
    result_path = data / 'latios-s1a-smoke.json'
    command = ['/usr/bin/arch', '-' + gate['architecture'], str(executable), '-batchmode', '-logFile', str(output / 'player.log')]
    launch = dict(command=command, run_id=gate['run_id'], source_commit=gate['source_commit'], source_tree=gate['source_tree'],
                  tool_commit=gate['tool_commit'], experiment_record=gate['experiment_record'], application_identity=identity,
                  build_inventory_sha256=ci.digest(output / 'player-build-sha256.json'), architecture=architecture, result_path=str(result_path),
                  started_utc=datetime.now(timezone.utc).isoformat())
    ci.write_json(output / 'player-launch.json', launch)
    env = {k: v for k, v in os.environ.items() if k != 'GITHUB_TOKEN' and not k.startswith('LATIOS_LAB_')}
    env['LATIOS_LAB_EXPECT_BURST'] = 'on'
    code = run_process(command, project, output / 'player-console.log', env, 180)
    launch.update(exit_code=code, finished_utc=datetime.now(timezone.utc).isoformat())
    ci.write_json(output / 'player-launch.json', launch)
    # Preserve any actual failure result before validating it.
    if result_path.exists():
        canonical(result_path)
        require(result_path.stat().st_size <= 1024 * 1024, 'Player result exceeds 1 MiB.')
        shutil.copyfile(result_path, output / 'player-result.json')
    require(code == 0, 'Actual player failed/timed out; build success is insufficient.')
    require(result_path.is_file(), 'Actual player produced no new result.')
    result = json.loads(result_path.read_text())
    check_player_result(result)
    log = output / 'player.log'
    require(log.is_file() and log.stat().st_size > 0, 'Original player log is missing.')
    text = log.read_text(errors='replace')
    marker = 'LATIOS_S1A_SMOKE '
    require(text.count(marker) == 1, 'Player log has missing/duplicate smoke marker.')
    logged, _ = json.JSONDecoder().raw_decode(text.split(marker, 1)[1].lstrip())
    require(logged == result, 'Actual result and player log disagree.')
    require(build_inventory(bundle) == inventory, 'Built app changed during execution.')
    launch.update(result_sha256=ci.digest(output / 'player-result.json'), player_log_sha256=ci.digest(log), status='PASSED',
                  scope='Frozen DisposeWorld/pair/query smoke only; remove/destroy and full lifecycle remain Editor evidence.')
    ci.write_json(output / 'player-launch.json', launch)


def execute(gate_path, workspace, editor, queue_check=None):
    gate_path = canonical(gate_path)
    gate_hash = ci.digest(gate_path)
    gate = json.loads(gate_path.read_text())
    inputs = check_gate(gate)
    check_remote(gate)
    if queue_check is not None:
        queue_check()
    require(sys.platform == 'darwin', 'Only the approved existing Mac runner may execute.')
    workspace = ci.validate_workspace(REPO, workspace)
    editor = canonical(editor)
    require(editor.is_file() and os.access(editor, os.X_OK), 'Installed exact Editor unavailable; no installation attempted.')
    workspace.mkdir()
    output = workspace / 'Artifacts'; output.mkdir()
    working = workspace / 'source'; project = working / 'Latios2022Lab'
    baseline = ci.product_fingerprint(REPO)
    summary = dict(source_commit=gate['source_commit'], source_tree=gate['source_tree'], tool_commit=gate['tool_commit'],
                   experiment=gate['experiment'], evidence_label='patched-local-variant', stage=gate['stage'], status='RUNNING',
                   fresh_project=False, editor_evidence=gate['editor_evidence'], repeat_clean_import='NOT_RUN',
                   player_build='NOT_RUN', player_execution='NOT_RUN', original_control='FAILED_HISTORICAL',
                   mobile='NOT_RUN', s1a='INCOMPLETE', s2='NOT_RUN')
    record = None
    try:
        subprocess.run(['git', 'worktree', 'add', '--detach', str(working), gate['source_commit']], cwd=REPO, check=True)
        require(not any((project / name).exists() for name in ('Library', '.upm-cache', 'Temp', '.launch-lock', 'Builds', 'Artifacts')),
                'Every conditional execution requires new project/Library/cache/build/evidence directories.')
        require(ci.product_fingerprint(working) == baseline, 'Product baseline differs between tool and native source.')
        summary['fresh_project'] = True
        native = module('latios_frozen_source', project / 'Tools/lab.py')
        require(ci.lab_source_fingerprint(project) == inputs, 'Detached native source differs from reviewed source.')
        # Preparation is owned by the separate tool checkout; the native project's copy remains a frozen input.
        record = lab.variant_tools().prepare(workspace, project, gate['experiment'], gate['source_commit'])
        record_path = workspace / 'experiment-record.json'
        runtime = dict(gate, project_path=str(project), run_id=uuid.uuid4().hex,
                       experiment_record={'path': str(record_path), 'sha256': ci.digest(record_path)})
        runtime_path = output / 'runtime-gate.json'
        ci.write_json(runtime_path, runtime)
        runtime_hash = ci.digest(runtime_path)
        ci.write_json(output / 'release-gate.json', gate)
        ci.write_json(output / 'frozen-lab-source-sha256.json', inputs)
        shutil.copyfile(record_path, output / 'experiment-record.json')
        identity = None
        for index, phase in enumerate(gate['allowed_phases']):
            require(ci.digest(gate_path) == gate_hash and ci.digest(runtime_path) == runtime_hash,
                    'Conditional gate changed during execution.')
            check_gate(gate); check_remote(gate)
            if queue_check is not None:
                queue_check()
            current = frozen_inputs(project)
            require(current == inputs and ci.product_fingerprint(REPO) == baseline
                    and ci.product_fingerprint(working) == baseline, 'Frozen native/product inputs changed.')
            require(ci.digest(record_path) == runtime['experiment_record']['sha256'], 'Sealed experiment record changed.')
            errors = ci.experiment_snapshot(native.variant_tools(), workspace, project, output, str(index) + '-before', record)
            require(not errors, 'Pre-phase package integrity failed: ' + str(errors))
            if phase == 'player-build':
                settings = project / 'ProjectSettings/ProjectSettings.asset'
                before = settings.read_text()
                shutil.copyfile(settings, output / 'player-settings-before.asset')
                identity = configure_identity(project, runtime['run_id'], Path.home())
                after = settings.read_text()
                shutil.copyfile(settings, output / 'player-settings-configured.asset')
                (output / 'player-settings-identity.patch').write_text(''.join(difflib.unified_diff(
                    before.splitlines(keepends=True), after.splitlines(keepends=True), fromfile='before', tofile='configured')))
                ci.write_json(output / 'application-identity.json', identity)
            if phase != 'player-run':
                native_phase(native, phase, project, editor, project / 'Artifacts' / phase, runtime, record, baseline)
                if phase == 'player-build':
                    shutil.copyfile(settings, output / 'player-settings-after-build.asset')
                    identity['settings_after_build_sha256'] = ci.digest(settings)
                    (output / 'player-settings-build.patch').write_text(''.join(difflib.unified_diff(
                        after.splitlines(keepends=True), settings.read_text().splitlines(keepends=True),
                        fromfile='configured', tofile='after-build')))
                    ci.write_json(output / 'application-identity.json', identity)
                summary['repeat_clean_import' if phase == 'import' and gate['stage'] == 'repeat-clean-import'
                        else 'build_project_import' if phase == 'import' else 'player_build'] = 'PASSED'
            else:
                run_player(project, output, runtime, identity)
                summary['player_execution'] = 'PASSED'
            errors = ci.experiment_snapshot(native.variant_tools(), workspace, project, output, str(index) + '-after', record)
            require(not errors, 'Post-phase package integrity failed: ' + str(errors))
            ci.collect(project, output, str(index) + '-' + phase)
            ci.write_json(output / 'followup-summary.json', summary)
        summary['status'] = 'PASSED'
    except Exception as error:
        summary.update(status='FAILED', error=str(error))
        raise
    finally:
        try:
            require(ci.digest(gate_path) == gate_hash and ci.product_fingerprint(REPO) == baseline, 'Final gate/product integrity failed.')
            if project.exists():
                require(ci.product_fingerprint(working) == baseline, 'Final detached product integrity failed.')
                current = frozen_inputs(project)
                require(current == inputs, 'Final frozen native source changed.')
                if record is not None:
                    require(ci.digest(runtime_path) == runtime_hash, 'Final runtime gate changed.')
                    require(ci.digest(record_path) == runtime['experiment_record']['sha256'], 'Final sealed record changed.')
                    errors = ci.experiment_snapshot(native.variant_tools(), workspace, project, output, 'final', record)
                    require(not errors, 'Final package integrity failed: ' + str(errors))
                ci.collect(project, output, 'final')
        except Exception as error:
            summary.update(status='FAILED', final_integrity_error=str(error))
        ci.write_json(output / 'followup-summary.json', summary)
        ci.write_evidence_hashes(output)
    require(summary['status'] == 'PASSED', 'Final evidence/integrity collection failed.')
    return 0


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--gate', type=Path)
    parser.add_argument('--workspace', type=Path)
    parser.add_argument('--editor', type=Path)
    parser.add_argument('--execute', action='store_true')
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args(argv)
    if not args.execute and not args.check:
        print(json.dumps({'status': 'PLAN_ONLY', 'native': 'NOT_RUN', 'stages': STAGES,
                          'prerequisites': 'Reviewed same-source patched Editor evidence; separate fresh repeat proof before player; released queue.'}, indent=2))
        return 0
    require(args.gate is not None, 'An explicit fresh conditional --gate is required.')
    if args.check:
        require(not args.execute, 'Choose offline gate checking or explicit execution.')
        check_gate(json.loads(canonical(args.gate).read_text()))
        print('LOCAL_PREREQUISITES_CHECKED; remote state and native execution NOT_RUN')
        return 0
    require(args.workspace is not None and args.editor is not None, 'Explicit new --workspace and installed --editor are required.')
    return execute(args.gate, args.workspace, args.editor)


if __name__ == '__main__':
    try:
        sys.exit(main())
    except (ValueError, KeyError, OSError, subprocess.SubprocessError) as error:
        print('BLOCKED: ' + str(error), file=sys.stderr)
        sys.exit(2)
