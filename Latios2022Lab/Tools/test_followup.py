import copy
from datetime import datetime, timedelta, timezone
import json
from pathlib import Path
import plistlib
import subprocess
import tempfile
import unittest
from unittest.mock import patch, Mock
import xml.etree.ElementTree as ET

import followup as f


class FollowupTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name).resolve()

    def binding(self, root):
        f.ci.write_evidence_hashes(root)
        return dict(root=str(root), manifest_sha256=f.ci.digest(root / 'evidence-sha256.json'), reviewed=True,
                    run_url='https://github.com/example/fixture/actions/runs/123/attempts/1')

    def gate(self):
        return dict(schema=f.SCHEMA, stage='repeat-clean-import', allowed_phases=['import'], target='StandaloneOSX',
                    burst='on', architecture='arm64', tool_commit='a' * 40, source_commit='b' * 40, source_tree='c' * 40,
                    p0_commit='d' * 40, repository='example/fixture', p0_native_status='passed',
                    p0_native_evidence_reviewed=True, p0_evidence_url='https://github.com/example/fixture/actions/runs/122/attempts/1',
                    runner_reserved=True, coordinator='synthetic-test-only',
                    expires_utc=(datetime.now(timezone.utc) + timedelta(minutes=5)).isoformat(),
                    experiment=f.lab.variant_tools().experiment_for_arm('patched-local-variant'))

    def synthetic_phase(self, root, gate, phase, burst, workspace='/synthetic/original'):
        output = root / 'native' / (phase + '-' + burst)
        output.mkdir(parents=True)
        with patch.object(f.lab, 'PROJECT', Path(workspace + '/source/Latios2022Lab')), \
             patch.object(f.lab.sys, 'platform', 'darwin'), patch.object(f.lab.subprocess, 'check_output', return_value='1\n'):
            command = f.lab.make_command(Path('/fixture/Unity'), phase, Path(workspace + '/source/Latios2022Lab/Artifacts') / output.name, burst, 'StandaloneOSX')
        record = dict(source_commit=gate['source_commit'], experiment=gate['experiment'], workspace=workspace,
                      project_path=workspace + '/source/Latios2022Lab', package_path=workspace + '/variant-packages/com.latios.latiosframework',
                      baseline_lock_sha256=f.lab.variant_tools().BASELINE_LOCK_SHA256)
        record['manifest_url'] = 'file:' + record['package_path']
        f.ci.write_json(output / 'launch.json', dict(command=command, source_commit=gate['source_commit'],
                       installed_editor={'version': f.lab.EDITOR_VERSION, 'command': ['/usr/bin/arch', '-arm64', '/fixture/Unity', '-version']},
                       inputs_sha256={'Assets/probe.cs': 'e' * 64}, experiment=record,
                       gate=dict(source_commit=gate['source_commit'], experiment=gate['experiment'], project_path=record['project_path'],
                                 experiment_record={'path': workspace + '/experiment-record.json', 'sha256': 'f' * 64})))
        f.ci.write_json(output / 'post-run-integrity.json', dict(unity_exit_code=0, root_product_inputs_unchanged=True, secondary_errors={}))
        (output / 'unity.log').write_text('Synthetic parser fixture. Not native evidence.\n')
        if phase == 'import':
            baseline = json.loads((f.lab.PROJECT / f.lab.variant_tools().BASELINE_LOCK).read_text())['dependencies']
            baseline[f.lab.variant_tools().PACKAGE_NAME] = dict(version=record['manifest_url'], source='local', depth=0,
                                                              dependencies=baseline[f.lab.variant_tools().PACKAGE_NAME]['dependencies'])
            f.ci.write_json(output / 'packages-lock.json', {'dependencies': baseline})
            rows = [dict(name=name, version=version) for name, version in f.lab.RESOLVED_PINS.items()]
            rows.append(dict(name=f.lab.variant_tools().PACKAGE_NAME, version='0.11.5', source='Local',
                             resolvedPath=record['package_path'], packageId=f.lab.variant_tools().PACKAGE_NAME + '@' + record['manifest_url']))
            f.ci.write_json(output / 'environment.json', dict(editor=f.lab.EDITOR_VERSION, buildTarget='StandaloneOSX', burstEnabled=True,
                burstSafety=True, experimentRecordSha256='f' * 64, experimentId=gate['experiment']['id'], experimentArm=gate['experiment']['arm'], packages=rows))
        else:
            xml = ET.Element('test-run', result='Passed')
            for name in sorted(f.lab.expected_test_identities(phase)):
                ET.SubElement(xml, 'test-case', result='Passed', fullname=name)
            ET.ElementTree(xml).write(output / 'tests.xml')
        return output

    def editor_evidence(self, gate):
        root = self.root / 'editor'
        root.mkdir()
        summary = dict(source_commit=gate['source_commit'], source_tree=gate['source_tree'], experiment=gate['experiment'],
                       evidence_label='patched-local-variant', editor_control='PASSED',
                       phases=[dict(phase=phase, burst=burst, status='PASSED', exit_code=0) for phase, burst in f.ci.PHASES])
        f.ci.write_json(root / 'ci-summary.json', summary)
        for phase, burst in f.ci.PHASES:
            self.synthetic_phase(root, gate, phase, burst)
        gate['editor_evidence'] = self.binding(root)
        return root

    def check_gate(self, gate):
        def git(repo, *args):
            return {('rev-parse', 'HEAD'): 'a' * 40, ('status', '--porcelain', '--untracked-files=normal'): '',
                    ('rev-parse', 'b' * 40 + '^{tree}'): 'c' * 40,
                    ('diff', '--name-only', 'd' * 40, 'b' * 40, '--'): ''}[args]
        with patch.object(f.ci, 'git', side_effect=git), patch.object(f.subprocess, 'run') as run, \
             patch.object(f, 'source_inputs', return_value={'Assets/probe.cs': 'e' * 64}):
            result = f.check_gate(gate)
            self.assertEqual(['git', 'merge-base', '--is-ancestor', 'd' * 40, 'b' * 40], run.call_args.args[0])
            return result

    def test_default_and_missing_release_never_launch_or_query_remote(self):
        with patch.object(f, 'execute', side_effect=AssertionError('native forbidden')), \
             patch.object(f, 'check_remote', side_effect=AssertionError('network forbidden')):
            self.assertEqual(0, f.main([]))
            with self.assertRaisesRegex(ValueError, 'explicit fresh'):
                f.main(['--execute'])

    def test_archived_controls_must_have_exact_source_all_tests_and_phase_order(self):
        gate = self.gate(); root = self.editor_evidence(gate)
        self.check_gate(gate)
        original = json.loads((root / 'ci-summary.json').read_text())
        for change in ('source', 'tree', 'failed', 'order', 'post-errors'):
            data = copy.deepcopy(original)
            if change == 'source': data['source_commit'] = '9' * 40
            if change == 'tree': data['source_tree'] = '9' * 40
            if change == 'failed': data['phases'][1]['status'] = 'FAILED'
            if change == 'order': data['phases'].reverse()
            if change == 'post-errors': data['phases'][1]['post_phase_errors'] = {'original': 'failure'}
            f.ci.write_json(root / 'ci-summary.json', data)
            gate['editor_evidence'] = self.binding(root)
            with self.subTest(change=change), self.assertRaises(ValueError): self.check_gate(gate)

    def test_gate_rejects_expiry_unknown_arm_architecture_and_unreleased_phase(self):
        gate = self.gate(); self.editor_evidence(gate)
        for mutation in (dict(expires_utc='2000-01-01T00:00:00Z'), dict(runner_reserved=False), dict(architecture='riscv'),
                         dict(allowed_phases=['player-build']), dict(target='StandaloneWindows64'), dict(experiment={})):
            with self.subTest(mutation=mutation), self.assertRaises(ValueError): self.check_gate(dict(gate, **mutation))

    def test_launch_cannot_relabel_unrelated_command_or_another_phase_workspace(self):
        gate = self.gate(); root = self.editor_evidence(gate)
        path = f.phase_outputs(root, 'editmode', 'on') / 'launch.json'
        original = json.loads(path.read_text())
        for kind in ('command', 'source', 'workspace', 'sealed-record'):
            data = copy.deepcopy(original)
            if kind == 'command': data['command'] = ['/unrelated/executable', 'EditMode']
            if kind == 'source': data['experiment']['source_commit'] = '0' * 40
            if kind == 'workspace': data['experiment']['workspace'] = '/other'
            if kind == 'sealed-record': data['gate']['experiment_record']['sha256'] = '1' * 64
            f.ci.write_json(path, data); gate['editor_evidence'] = self.binding(root)
            with self.subTest(kind=kind), self.assertRaises(ValueError): self.check_gate(gate)

    def test_generated_scene_exception_cannot_hide_new_code_or_other_assets(self):
        project = self.root / 'Latios2022Lab'
        (project / 'Assets/Latios2022Tests/Generated/EditorTools').mkdir(parents=True)
        (project / 'Tools').mkdir(); (project / 'Variants').mkdir()
        for name in f.GENERATED_SCENE: (project / name).write_text('generated scene fixture')
        self.assertEqual({}, f.frozen_inputs(project))
        extra = 'Assets/Latios2022Tests/Generated/EditorTools/Extra.cs'
        (project / extra).write_text('unapproved code')
        self.assertEqual({extra}, set(f.frozen_inputs(project)))

    def test_player_requires_physically_separate_same_source_repeat(self):
        gate = self.gate(); self.editor_evidence(gate)
        gate.update(stage='mac-il2cpp-smoke', allowed_phases=f.STAGES['mac-il2cpp-smoke'])
        with self.assertRaisesRegex(ValueError, 'Incomplete evidence'): self.check_gate(gate)
        repeat = self.root / 'repeat'; repeat.mkdir()
        summary = dict(source_commit=gate['source_commit'], source_tree=gate['source_tree'], experiment=gate['experiment'],
                       evidence_label='patched-local-variant', stage='repeat-clean-import', status='PASSED', fresh_project=True,
                       repeat_clean_import='PASSED', editor_evidence=dict(gate['editor_evidence'], root='/synthetic/prior-restoration'))
        f.ci.write_json(repeat / 'followup-summary.json', summary)
        output = self.synthetic_phase(repeat, gate, 'import', 'on', '/synthetic/repeat')
        gate['repeat_evidence'] = self.binding(repeat)
        self.check_gate(gate)
        launch = json.loads((output / 'launch.json').read_text())
        launch['experiment']['workspace'] = '/synthetic/original'
        launch['experiment']['project_path'] = '/synthetic/original/source/Latios2022Lab'
        f.ci.write_json(output / 'launch.json', launch)
        gate['repeat_evidence'] = self.binding(repeat)
        with self.assertRaises(ValueError): self.check_gate(gate)

    def test_native_proof_rejects_oracle_tamper_skips_and_changed_dependency_graph(self):
        gate = self.gate(); root = self.editor_evidence(gate)
        output = f.phase_outputs(root, 'editmode', 'on')
        xml = ET.parse(output / 'tests.xml'); xml.getroot()[0].set('result', 'Skipped'); xml.write(output / 'tests.xml')
        gate['editor_evidence'] = self.binding(root)
        with self.assertRaisesRegex(ValueError, 'skipped'): self.check_gate(gate)
        xml.getroot()[0].set('result', 'Passed'); xml.write(output / 'tests.xml')
        output = f.phase_outputs(root, 'import', 'on')
        lock = json.loads((output / 'packages-lock.json').read_text()); lock['dependencies']['com.unity.collections']['version'] = '2.5.2'
        f.ci.write_json(output / 'packages-lock.json', lock); gate['editor_evidence'] = self.binding(root)
        with self.assertRaisesRegex(ValueError, 'dependency graph'): self.check_gate(gate)

    def test_bundle_rejects_tamper_missing_extra_and_symlink(self):
        root = self.root / 'proof'; root.mkdir(); (root / 'proof.json').write_text('{}')
        binding = self.binding(root); self.assertEqual(root, f.read_bundle(binding))
        (root / 'proof.json').write_text('{ }')
        with self.assertRaisesRegex(ValueError, 'hash mismatch'): f.read_bundle(binding)
        (root / 'proof.json').write_text('{}'); (root / 'extra').write_text('extra')
        with self.assertRaisesRegex(ValueError, 'extra files'): f.read_bundle(binding)
        (root / 'extra').unlink(); (root / 'proof.json').unlink()
        with self.assertRaises(ValueError): f.read_bundle(binding)
        (root / 'proof.json').symlink_to(self.root / 'absent')
        with self.assertRaises(ValueError): f.read_bundle(binding)

    def test_remote_rejects_failed_editor_and_busy_lab_queue(self):
        gate = self.gate(); self.editor_evidence(gate)
        run = dict(id=123, run_attempt=1, repository={'full_name': gate['repository']}, head_sha=gate['source_commit'],
                   path=f.ci.WORKFLOW, status='completed', conclusion='success')
        def fetch(repo, suffix):
            return run if suffix.startswith('runs/') else {'total_count': 0}
        with patch.object(f.ci, 'check_p0'), patch.object(f.ci, 'check_queue'):
            f.check_remote(gate, fetch)
            run['conclusion'] = 'failure'
            with self.assertRaisesRegex(ValueError, 'successful exact-source'): f.check_remote(gate, fetch)
            run['conclusion'] = 'success'
            with self.assertRaisesRegex(ValueError, 'queue'):
                f.check_remote(gate, lambda repo, suffix: run if suffix.startswith('runs/') else {'total_count': 1})

    def result(self):
        return dict(editor=f.lab.EDITOR_VERSION, platform='OSXPlayer', cpu='Synthetic CPU', burst=True, passed=True,
                    message='Core collection chain and Psyshock array/query smoke passed.')

    def test_result_parser_rejects_false_or_coerced_success_and_wrong_platform(self):
        f.check_player_result(self.result())
        for mutation in (dict(passed=False), dict(passed=1), dict(burst=False), dict(editor='6000.0.1f1'),
                         dict(platform='OSXEditor'), dict(message='Started'), dict(cpu='')):
            with self.subTest(mutation=mutation), self.assertRaises(ValueError): f.check_player_result(dict(self.result(), **mutation))

    def player_fixture(self):
        project = self.root / 'Latios2022Lab'; project.mkdir()
        (project / 'ProjectSettings').mkdir()
        (project / 'ProjectSettings/ProjectSettings.asset').write_text('PlayerSettings:\n  companyName: Original\n  productName: Original\n  scriptingBackend:\n    Standalone: 1\n')
        home = self.root / 'home'; (home / 'Library/Application Support').mkdir(parents=True)
        gate = dict(self.gate(), run_id='f' * 32, experiment_record={'path': '/synthetic/experiment-record.json', 'sha256': '9' * 64})
        identity = f.configure_identity(project, gate['run_id'], home)
        bundle = project / 'Builds/IL2CPP/Latios2022Lab.app'
        (bundle / 'Contents/MacOS').mkdir(parents=True)
        executable = bundle / 'Contents/MacOS/Smoke'; executable.write_bytes(b'Synthetic, never executed'); executable.chmod(0o755)
        (bundle / 'Contents/Info.plist').write_bytes(plistlib.dumps({'CFBundleExecutable': 'Smoke'}))
        output = self.root / 'output'; output.mkdir()
        return project, output, gate, identity

    def test_identity_changes_only_disposable_names_and_refuses_old_directory(self):
        project, output, gate, identity = self.player_fixture()
        settings = (project / 'ProjectSettings/ProjectSettings.asset').read_text()
        self.assertEqual(settings, 'PlayerSettings:\n  companyName: Latios2022Lab-' + gate['run_id'] + '\n  productName: ConditionalSmoke\n  scriptingBackend:\n    Standalone: 1\n')
        with self.assertRaisesRegex(ValueError, 'existing application data'):
            f.configure_identity(project, gate['run_id'], self.root / 'home')

    def test_build_report_and_actual_settings_reject_mono_missing_errors_wrong_target(self):
        project, output, gate, identity = self.player_fixture()
        report = dict(Target='StandaloneOSX', Backend='IL2CPP', Result='Succeeded', Errors='0', Warnings='0',
                      Output=str(project / 'Builds/IL2CPP/Latios2022Lab.app'), Execution='NOT_RUN')
        path = output / 'build.txt'
        def write(data): path.write_text(''.join(k + '=' + v + '\n' for k, v in data.items()))
        write(report); f.check_build_report(path, project)
        for mutation in (dict(Backend='Mono'), dict(Errors='1'), dict(Target='StandaloneWindows64'), dict(Result='Failed')):
            write(dict(report, **mutation))
            with self.assertRaises(ValueError): f.check_build_report(path, project)
        write(report)
        settings = project / 'ProjectSettings/ProjectSettings.asset'; settings.write_text(settings.read_text().replace('Standalone: 1', 'Standalone: 0'))
        with self.assertRaisesRegex(ValueError, 'backend'): f.check_build_report(path, project)

    def test_architecture_rejects_wrong_slice_and_tool_failure(self):
        for status, architectures in ((0, 'x86_64'), (1, 'arm64')):
            with patch.object(f.subprocess, 'run', return_value=subprocess.CompletedProcess([], status, architectures, '')):
                with self.assertRaisesRegex(ValueError, 'selected architecture'):
                    f.check_architecture(Path('/synthetic/player'), 'arm64', self.root)

    def test_owned_timeout_terminates_only_created_process(self):
        process = Mock(); process.wait.side_effect = subprocess.TimeoutExpired('synthetic', 180)
        with patch.object(f.subprocess, 'Popen', return_value=process), patch.object(f.ci, 'terminate_owned_group') as stop:
            self.assertEqual(124, f.run_process(['synthetic'], self.root, self.root / 'console', {}, 180))
            stop.assert_called_once_with(process)

    def test_player_requires_fresh_result_matching_log_exit_source_and_run(self):
        # Every subcase owns fresh app/data/output paths. No native process is executed.
        for scenario in ('pass', 'stale', 'missing', 'failure-with-pass', 'log-mismatch', 'wrong-source', 'wrong-run'):
            with self.subTest(scenario=scenario), tempfile.TemporaryDirectory() as tmp:
                saved = self.root; self.root = Path(tmp).resolve()
                try:
                    project, output, gate, identity = self.player_fixture()
                    result_path = Path(identity['data_path']) / 'latios-s1a-smoke.json'
                    if scenario == 'stale': result_path.write_text(json.dumps(self.result()))
                    if scenario == 'wrong-run': identity['run_id'] = '0' * 32
                    def run(command, cwd, log, env, seconds):
                        self.assertEqual(['/usr/bin/arch', '-arm64'], command[:2])
                        if scenario != 'missing': result_path.write_text(json.dumps(self.result()))
                        result = dict(self.result(), passed=False) if scenario == 'log-mismatch' else self.result()
                        (output / 'player.log').write_text('LATIOS_S1A_SMOKE ' + json.dumps(result) + '\n')
                        return 1 if scenario == 'failure-with-pass' else 0
                    values = ['0' * 40, gate['source_tree']] if scenario == 'wrong-source' else [gate['source_commit'], gate['source_tree']]
                    with patch.object(f.ci, 'git', side_effect=values), patch.object(f, 'check_architecture', return_value={'selected': 'arm64'}), \
                         patch.object(f, 'run_process', side_effect=run) as launched:
                        if scenario == 'pass':
                            f.run_player(project, output, gate, identity)
                            manifest = json.loads((output / 'player-launch.json').read_text())
                            self.assertEqual('PASSED', manifest['status']); self.assertEqual(gate['run_id'], manifest['run_id'])
                            self.assertEqual(gate['source_commit'], manifest['source_commit'])
                            self.assertEqual(f.ci.digest(output / 'player-result.json'), manifest['result_sha256'])
                        else:
                            with self.assertRaises(ValueError): f.run_player(project, output, gate, identity)
                            if scenario in ('stale', 'wrong-source', 'wrong-run'): launched.assert_not_called()
                finally:
                    self.root = saved

    def test_app_inventory_allows_internal_framework_link_rejects_escape(self):
        bundle = self.root / 'test.app'; bundle.mkdir(); (bundle / 'real').write_bytes(b'code')
        (bundle / 'link').symlink_to('real')
        self.assertEqual({'symlink': 'real'}, f.build_inventory(bundle)['link'])
        (bundle / 'outside').symlink_to(self.root / 'outside')
        with self.assertRaisesRegex(ValueError, 'external symlink'): f.build_inventory(bundle)


if __name__ == '__main__':
    unittest.main()
