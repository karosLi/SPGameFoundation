"""Synthetic parser/runner fixtures only. These tests never launch Unity or assert real P0 success."""
import copy
from contextlib import ExitStack
from datetime import datetime, timedelta, timezone
import importlib.util
import json
import os
import re
from pathlib import Path
import subprocess
import tempfile
import textwrap
import unittest
from unittest.mock import Mock, patch
import zipfile

spec = importlib.util.spec_from_file_location('ci', Path(__file__).with_name('latios_lab_ci.py'))
ci = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ci)
REPO = Path(__file__).resolve().parents[2]


def request_fixture():
    return dict(schema=1, repository='fixture/only', p0_commit='a' * 40,
                approved_source_commit='b' * 40, p0_native_status='passed',
                p0_native_evidence_reviewed=True, runner_reserved=True, coordinator='UNIT TEST ONLY',
                expires_utc=(datetime.now(timezone.utc) + timedelta(hours=4)).isoformat(),
                allowed_phases=['import', 'editmode', 'playmode'],
                p0_evidence_url='https://github.com/fixture/only/actions/runs/123/attempts/2')


def experiment_fixture(arm='patched-local-variant'):
    spec = json.loads((REPO / 'Latios2022Lab/Variants/cleanup-query-v1.json').read_text())
    return dict({key: spec[key] for key in ('schema', 'id', 'upstream_commit', 'upstream_tree',
                                           'patch_sha256', 'source_inventory_sha256')}, arm=arm)


def api_fixture(repository, suffix):
    if '/jobs?' in suffix:
        return {'total_count': 1, 'jobs': [{'id': 456, 'name': 'unity-tests', 'conclusion': 'success',
                 'steps': [{'name': 'Run EditMode and PlayMode tests', 'conclusion': 'success'}]}]}
    if suffix.startswith('workflows/'):
        return {'total_count': 0, 'workflow_runs': []}
    return dict(id=123, run_attempt=2, repository={'full_name': repository}, head_sha='a' * 40,
                path=ci.ROOT_WORKFLOW, status='completed', conclusion='success')


class GateTests(unittest.TestCase):
    def test_experiment_identity_is_exact_and_git_control_does_not_load_variant(self):
        with patch.object(ci, 'load_cleanup_variant', side_effect=AssertionError('unneeded module')):
            ci.check_request(request_fixture(), 'fixture/only', ci.BRANCH)
            self.assertEqual('git-control', ci.evidence_label(request_fixture()))
        for arm in ci.EVIDENCE_LABELS[1:]:
            request = dict(request_fixture(), experiment=experiment_fixture(arm))
            ci.check_request(request, 'fixture/only', ci.BRANCH)
            self.assertEqual(arm, ci.evidence_label(request))
        for mutation in ({'id': 'other'}, {'arm': '../other'}, {'arm': 'patched\nINJECTED=1'},
                         {'upstream_commit': '1' * 40}, {'upstream_tree': '2' * 40},
                         {'patch_sha256': '3' * 64}, {'source_inventory_sha256': '4' * 64},
                         {'extra': True}):
            with self.subTest(mutation=mutation), self.assertRaises(ValueError):
                ci.check_request(dict(request_fixture(), experiment=dict(experiment_fixture(), **mutation)),
                                 'fixture/only', ci.BRANCH)
        with self.assertRaises(ValueError):
            ci.check_request(dict(request_fixture(), experiment=None), 'fixture/only', ci.BRANCH)

    def test_variant_loader_uses_selected_checkout_and_requires_local_module(self):
        with tempfile.TemporaryDirectory() as temporary:
            repo = Path(temporary)
            path = repo / 'Latios2022Lab/Tools/cleanup_variant.py'
            path.parent.mkdir(parents=True)
            path.write_text('CHECKOUT_SENTINEL = 17\n')
            self.assertEqual(17, ci.load_cleanup_variant(repo).CHECKOUT_SENTINEL)
            path.unlink()
            with self.assertRaisesRegex(ValueError, 'unavailable'):
                ci.load_cleanup_variant(repo)

    def test_request_rejects_missing_pending_wrong_branch_and_foreign_evidence(self):
        for mutation in ({'p0_commit': ''}, {'p0_native_status': 'pending'}, {'runner_reserved': False},
                         {'p0_native_evidence_reviewed': False}, {'coordinator': ''},
                         {'approved_source_commit': 'main'}, {'expires_utc': '2000-01-01T00:00:00Z'},
                         {'p0_evidence_url': 'https://github.com/other/repo/actions/runs/123/attempts/2'},
                         {'p0_evidence_url': 'https://github.com/fixture/only/actions/runs/123'},
                         {'allowed_phases': ['import', 'editmode', 'playmode', 'player-build']}):
            with self.subTest(mutation=mutation), self.assertRaises(ValueError):
                ci.check_request(dict(request_fixture(), **mutation), 'fixture/only', ci.BRANCH)
        with self.assertRaisesRegex(ValueError, 'deliberate'):
            ci.check_request(request_fixture(), 'fixture/only', 'refs/heads/dot/latios-stages')

    def test_missing_gate_blocks_before_network_or_native(self):
        with tempfile.TemporaryDirectory() as tmp, patch.object(ci, 'check_p0', side_effect=AssertionError('network')), \
                patch.object(ci, 'run_owned_process', side_effect=AssertionError('Unity')):
            with self.assertRaises(FileNotFoundError):
                ci.validate(Path(tmp), 'fixture/only', ci.BRANCH, 'a' * 40)

    def test_live_api_rejects_wrong_sha_attempt_workflow_and_non_success(self):
        for mutation in ({'head_sha': 'c' * 40}, {'run_attempt': 3}, {'path': '.github/workflows/harness.yml'},
                         {'status': 'in_progress'}, {'conclusion': 'failure'}, {'conclusion': 'skipped'},
                         {'conclusion': 'cancelled'}, {'repository': {'full_name': 'other/repo'}}):
            def fetch(repository, suffix):
                return dict(api_fixture(repository, suffix), **mutation)
            with self.subTest(mutation=mutation), self.assertRaises(ValueError):
                ci.check_p0(request_fixture(), 'fixture/only', ci.BRANCH, fetch)

    def test_successful_workflow_cannot_hide_skipped_native_job_or_step(self):
        for part in ('job', 'step', 'inventory'):
            def fetch(repository, suffix):
                data = api_fixture(repository, suffix)
                if '/jobs?' in suffix:
                    if part == 'job': data['jobs'][0]['conclusion'] = 'skipped'
                    elif part == 'step': data['jobs'][0]['steps'][0]['conclusion'] = 'skipped'
                    else: data['total_count'] = 101
                return data
            with self.subTest(part=part), self.assertRaises(ValueError):
                ci.check_p0(request_fixture(), 'fixture/only', ci.BRANCH, fetch)
        result = ci.check_p0(request_fixture(), 'fixture/only', ci.BRANCH, api_fixture)
        self.assertEqual('a' * 40, result['head_sha'])
        self.assertIn('coordinator reviewed', result['scope'])

    def test_queue_requires_no_existing_spf_work(self):
        ci.check_queue('fixture/only', api_fixture)
        with self.assertRaisesRegex(ValueError, 'queue'):
            ci.check_queue('fixture/only', lambda *args: {'total_count': 1})


class GitIsolationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.repo = Path(self.temporary.name) / 'repo'
        self.repo.mkdir()
        self.git('init', '-q')
        self.git('config', 'user.name', 'Fixture Only')
        self.git('config', 'user.email', 'fixture@example.invalid')
        self.put(ci.ROOT_WORKFLOW, 'name: root\non:\n  push:\n  pull_request:\njobs: unchanged\n')
        self.put('Packages/manifest.json', '{}')
        self.put('ProjectSettings/ProjectVersion.txt', 'fixture')
        self.put('Assets/runtime.cs', 'unchanged')
        self.baseline = self.commit()
        self.put('Latios2022Lab/README.md', 'Fixture source only')
        self.prepared = self.commit()
        self.request = dict(request_fixture(), p0_commit=self.baseline, approved_source_commit=self.prepared)
        self.put(ci.REQUEST, json.dumps(self.request))
        self.release = self.commit()

    def git(self, *args):
        return subprocess.check_output(['git', *args], cwd=self.repo, text=True, stderr=subprocess.STDOUT).strip()

    def put(self, name, text):
        path = self.repo / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text)

    def commit(self):
        self.git('add', '.')
        self.git('commit', '-qm', 'Synthetic parser fixture')
        return self.git('rev-parse', 'HEAD')

    def test_real_git_allowlist_accepts_lab_only_release(self):
        result = ci.check_source(self.repo, self.request, self.release)
        self.assertEqual(self.release, result['source_commit'])

    def test_rejects_root_runtime_packages_and_settings_changes(self):
        for name in ('Assets/runtime.cs', 'Packages/manifest.json', 'ProjectSettings/ProjectVersion.txt'):
            with self.subTest(name=name):
                self.git('reset', '--hard', self.release)
                self.put(name, 'changed')
                bad = self.commit()
                # Move reviewed source pin too: P0 comparison must still catch forbidden delta.
                request = dict(self.request, approved_source_commit=bad)
                self.put(ci.REQUEST, json.dumps(request))
                release = self.commit()
                with self.assertRaisesRegex(ValueError, 'Non-lab'):
                    ci.check_source(self.repo, request, release)

    def test_release_cannot_change_source_beside_gate(self):
        self.put('Latios2022Lab/README.md', 'Unreviewed change')
        with self.assertRaisesRegex(ValueError, 'only ci-request'):
            ci.check_source(self.repo, self.request, self.commit())

    def test_root_workflow_allows_only_exact_push_exclusion(self):
        for suffix in ('', '    branches-ignore: [other]\n'):
            with self.subTest(suffix=suffix):
                self.git('reset', '--hard', self.release)
                old = self.git('show', self.baseline + ':' + ci.ROOT_WORKFLOW)
                self.put(ci.ROOT_WORKFLOW, old.replace('  push:\n', '  push:\n' + ci.EXCLUSION + suffix))
                prepared = self.commit()
                request = dict(self.request, approved_source_commit=prepared)
                self.put(ci.REQUEST, json.dumps(request))
                release = self.commit()
                if suffix:
                    with self.assertRaisesRegex(ValueError, 'only exclude'):
                        ci.check_source(self.repo, request, release)
                else:
                    ci.check_source(self.repo, request, release)

    def test_dirty_source_is_blocked(self):
        self.put('untracked.txt', 'dirty')
        with self.assertRaisesRegex(ValueError, 'clean'):
            ci.check_source(self.repo, self.request, self.release)


class ExecutionTests(unittest.TestCase):
    def test_compiler_text_capture_is_owned_complete_and_immutable_per_phase(self):
        with tempfile.TemporaryDirectory() as tmp:
            project = Path(tmp) / 'Latios2022Lab'; output = Path(tmp) / 'captured'
            bee = project / 'Library/Bee/artifacts/control.dag'; bee.mkdir(parents=True)
            expected = {}
            for suffix in ('.rsp', '.rsp2', '.UnityAdditionalFile.txt'):
                path = bee / ('Latios2022Lab.Editor' + suffix)
                path.write_bytes(('exact compiler input ' + suffix + '\n').encode())
                expected[path.relative_to(project).as_posix()] = path.read_bytes()
            generated = project / 'Temp/GeneratedCode/Latios2022Lab.Runtime/Collection.g.cs'
            generated.parent.mkdir(parents=True); generated.write_bytes(b'// already emitted source\n')
            expected[generated.relative_to(project).as_posix()] = generated.read_bytes()
            (bee / 'Other.Assembly.rsp').write_text('not owned')
            (bee / 'Latios2022Lab.Editor.dll').write_bytes(b'binary not collected')
            foreign = project / 'Temp/GeneratedCode/Other.Assembly/Foreign.g.cs'
            foreign.parent.mkdir(parents=True); foreign.write_text('not owned')
            report = ci.collect_compiler_text(project, output)
            self.assertEqual('COLLECTED', report['status'])
            self.assertEqual(set(expected), {r['path'] for r in report['files']})
            for name, data in expected.items():
                self.assertEqual(data, (output / name).read_bytes())
            self.assertEqual(1, report['generated_sources']['Latios2022Lab.Runtime'])
            self.assertEqual(0, report['generated_sources']['Latios2022Lab.Editor'])
            self.assertEqual(9, len(report['missing_response_patterns']))
            generated.write_text('later content must not replace the original phase')
            self.assertEqual(report, ci.collect_compiler_text(project, output))
            self.assertEqual(expected[generated.relative_to(project).as_posix()],
                             (output / generated.relative_to(project)).read_bytes())

    def test_compiler_text_capture_reports_caps_missing_files_and_refuses_symlinks(self):
        with tempfile.TemporaryDirectory() as tmp:
            project = Path(tmp) / 'Latios2022Lab'
            bee = project / 'Library/Bee/artifacts/control.dag'; bee.mkdir(parents=True)
            outside = Path(tmp) / 'outside.txt'; outside.write_text('must not be copied')
            (bee / 'Latios2022Lab.Editor.rsp').symlink_to(outside)
            (bee / 'Latios2022Lab.Runtime.rsp').write_text('over the cap')
            with patch.object(ci, 'COMPILER_FILE_BYTES', 4):
                report = ci.collect_compiler_text(project, Path(tmp) / 'captured')
            self.assertEqual('INCOMPLETE', report['status'])
            self.assertFalse(report['files'])
            self.assertEqual({'symlink refused', 'text capture limit exceeded'},
                             {entry['reason'] for entry in report['issues']})
            empty = ci.collect_compiler_text(Path(tmp) / 'empty-project', Path(tmp) / 'empty-output')
            self.assertEqual(12, len(empty['missing_response_patterns']))
            self.assertFalse(empty['files'])
            self.assertTrue(all(count == 0 for count in empty['generated_sources'].values()))

    def test_compiler_collection_failure_preserves_native_logs_and_hash_manifest(self):
        with tempfile.TemporaryDirectory() as tmp:
            project = Path(tmp) / 'Latios2022Lab'; artifacts = Path(tmp) / 'Artifacts'; artifacts.mkdir()
            native = project / 'Artifacts/failed-import'; native.mkdir(parents=True)
            (native / 'unity.log').write_text('original compiler error')
            bee = project / 'Library/Bee/artifacts/control.dag'; bee.mkdir(parents=True)
            (bee / 'Latios2022Lab.Editor.rsp').write_text('compiler response')
            with patch.object(ci, 'COMPILER_TOTAL_BYTES', 1), self.assertRaisesRegex(ValueError, 'capture incomplete'):
                ci.collect(project, artifacts, '00-import-on')
            self.assertEqual('original compiler error', (artifacts / 'native/failed-import/unity.log').read_text())
            manifest = json.loads((artifacts / 'evidence-sha256.json').read_text())
            self.assertIn('native/failed-import/unity.log', manifest)
            self.assertIn('compiler-text/00-import-on/index.json', manifest)

    def test_native_failure_remains_primary_when_compiler_capture_also_fails(self):
        with tempfile.TemporaryDirectory() as tmp:
            repo = Path(tmp) / 'checkout'; repo.mkdir()
            workspace = Path(tmp) / 'isolated'
            editor = Path(tmp) / 'Unity'; editor.write_text('NONEXECUTED FIXTURE'); editor.chmod(0o700)
            def fake_worktree(command, **kwargs):
                project = workspace / 'source/Latios2022Lab'
                (project / 'Packages').mkdir(parents=True)
                (project / 'ProjectSettings').mkdir()
            def fake_phase(command, cwd, log):
                native = cwd / 'Artifacts/failed-import'; native.mkdir(parents=True)
                (native / 'unity.log').write_text('original Unity error DC0061 fixture')
                bee = cwd / 'Library/Bee/artifacts/control.dag'; bee.mkdir(parents=True)
                (bee / 'Latios2022Lab.EditorTools.rsp').write_text('exceeds synthetic capture cap')
                log.write_text('original launcher failure')
                return 7
            with patch.object(ci.sys, 'platform', 'darwin'), patch.object(ci, 'validate', return_value=(request_fixture(), {})), \
                    patch.object(ci, 'check_p0', return_value={}), patch.object(ci, 'check_queue'), \
                    patch.object(ci.subprocess, 'run', side_effect=fake_worktree), \
                    patch.object(ci, 'run_owned_process', side_effect=fake_phase), patch.object(ci, 'COMPILER_TOTAL_BYTES', 1):
                with self.assertRaisesRegex(ValueError, 'First control-group failure: import Burst on'):
                    ci.execute(repo, workspace, 'fixture/only', ci.BRANCH, 'b' * 40, editor)
            artifacts = workspace / 'Artifacts'
            summary = json.loads((artifacts / 'ci-summary.json').read_text())
            self.assertIn('First control-group failure', summary['error'])
            self.assertEqual(7, summary['phases'][0]['exit_code'])
            self.assertEqual('FAILED', summary['phases'][0]['status'])
            self.assertIn('capture incomplete', summary['phases'][0]['evidence_collection_error'])
            self.assertIn('capture incomplete', summary['evidence_collection_error'])
            self.assertEqual('original Unity error DC0061 fixture', (artifacts / 'native/failed-import/unity.log').read_text())
            hashes = json.loads((artifacts / 'evidence-sha256.json').read_text())
            self.assertEqual(ci.digest(artifacts / 'ci-summary.json'), hashes['ci-summary.json'])

    def test_workspace_canonicalization_blocks_nested_and_symlink_ancestry(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp).resolve()
            repo = root / 'repo'; repo.mkdir()
            other = root / 'other'; other.mkdir()
            with self.assertRaisesRegex(ValueError, 'outside'):
                ci.validate_workspace(repo, other / '../repo/new-run')
            link = root / 'link'; link.symlink_to(other, target_is_directory=True)
            with self.assertRaisesRegex(ValueError, 'symlinks'):
                ci.validate_workspace(repo, link / 'new-run')
            self.assertEqual(root / 'new-run', ci.validate_workspace(repo, root / 'new-run'))

    def test_timeout_kills_surviving_children_after_launcher_exits(self):
        process = Mock(pid=1234)
        process.poll.return_value = -15
        with patch.object(ci.os, 'killpg') as kill, patch.object(ci.time, 'monotonic', side_effect=[0, 21]):
            ci.terminate_owned_group(process)
        self.assertEqual([(1234, ci.signal.SIGTERM), (1234, 0), (1234, ci.signal.SIGKILL)],
                         [call.args for call in kill.call_args_list])
        process.wait.assert_called_once_with()

    def test_complete_large_xml_is_early_and_oversize_early_is_not_published(self):
        with tempfile.TemporaryDirectory() as tmp:
            workspace = Path(tmp) / 'run'
            artifacts = workspace / 'Artifacts'; artifacts.mkdir(parents=True)
            xml = '<test-run result="Failed">' + '<!--' + ('x' * (1024 * 1024 + 7)) + '--></test-run>'
            (artifacts / 'tests.xml').write_text(xml)
            output = Path(tmp) / 'parts'; output.mkdir()
            ci.package(workspace, output)
            with zipfile.ZipFile(output / 'early/early.zip') as archive:
                self.assertEqual(xml.encode(), archive.read('tests.xml'))
            oversized = Path(tmp) / 'oversized'; oversized.mkdir()
            with patch.object(ci, 'EARLY_BYTES', 1), self.assertRaisesRegex(ValueError, 'Early evidence'):
                ci.package(workspace, oversized)
            self.assertFalse((oversized / 'early').exists())
            self.assertTrue((oversized / 'parts/part00/evidence-manifest.json').exists())

    def test_early_evidence_preserves_complete_normalized_project_settings(self):
        with tempfile.TemporaryDirectory() as tmp:
            workspace = Path(tmp) / 'run'
            artifacts = workspace / 'Artifacts'
            settings = artifacts / 'lab-inputs/ProjectSettings/ProjectSettings.asset'
            settings.parent.mkdir(parents=True)
            original = b'%YAML 1.1\nPlayerSettings:\n  scriptingDefineSymbols:\n    Standalone: ENTITY_STORE_V1\n'
            settings.write_bytes(original)
            compiler = artifacts / 'compiler-text/00-import-on/Library/Bee/artifacts/control.dag/Latios2022Lab.Editor.rsp'
            compiler.parent.mkdir(parents=True); compiler.write_bytes(b'-langversion:9.0\n')
            generated = artifacts / 'compiler-text/00-import-on/Temp/GeneratedCode/Latios2022Lab.Runtime/Collection.g.cs'
            generated.parent.mkdir(parents=True); generated.write_bytes(b'// emitted text\n')
            (artifacts / 'unrelated.asset').write_bytes(b'not a lab setting')
            output = Path(tmp) / 'output'; output.mkdir()
            ci.package(workspace, output)
            with zipfile.ZipFile(output / 'early/early.zip') as archive:
                self.assertEqual(original, archive.read('lab-inputs/ProjectSettings/ProjectSettings.asset'))
                self.assertEqual(compiler.read_bytes(), archive.read(compiler.relative_to(artifacts).as_posix()))
                self.assertEqual(generated.read_bytes(), archive.read(generated.relative_to(artifacts).as_posix()))
                self.assertNotIn('unrelated.asset', archive.namelist())
            manifest = json.loads((output / 'parts/part00/evidence-manifest.json').read_text())
            self.assertTrue(manifest['parts'])

    def test_first_failure_preserved_without_later_phase_or_player(self):
        with tempfile.TemporaryDirectory() as tmp:
            repo = Path(tmp) / 'checkout'; repo.mkdir()
            workspace = Path(tmp) / 'isolated'
            editor = Path(tmp) / 'Unity'; editor.write_text('NOT AN EXECUTABLE TEST FIXTURE'); editor.chmod(0o700)
            called = []
            def fake_worktree(command, **kwargs):
                project = workspace / 'source/Latios2022Lab'
                (project / 'Packages').mkdir(parents=True)
                (project / 'ProjectSettings').mkdir()
            def fake_phase(command, cwd, log):
                phase = command[2]; called.append((phase, command[4]))
                native = cwd / 'Artifacts' / phase; native.mkdir(parents=True, exist_ok=True)
                (native / 'unity.log').write_text('actual failure output fixture')
                (native / 'tests.xml').write_text('<test-run result="Failed"/>')
                log.write_text('phase console fixture')
                return 7 if phase == 'editmode' else 0
            with patch.object(ci.sys, 'platform', 'darwin'), patch.object(ci, 'validate', return_value=(request_fixture(), {})), \
                    patch.object(ci, 'check_p0', return_value={}), patch.object(ci, 'check_queue'), \
                    patch.object(ci.subprocess, 'run', side_effect=fake_worktree), \
                    patch.object(ci, 'run_owned_process', side_effect=fake_phase):
                with self.assertRaisesRegex(ValueError, 'First control-group failure'):
                    ci.execute(repo, workspace, 'fixture/only', ci.BRANCH, 'b' * 40, editor)
            self.assertEqual([('import', 'on'), ('editmode', 'on')], called)
            summary = json.loads((workspace / 'Artifacts/ci-summary.json').read_text())
            self.assertEqual(['PASSED', 'FAILED', 'NOT_RUN', 'NOT_RUN', 'NOT_RUN'], [r['status'] for r in summary['phases']])
            self.assertEqual('NOT_RUN', summary['player_build'])
            self.assertEqual('INCOMPLETE', summary['s1a'])
            self.assertTrue((workspace / 'Artifacts/native/editmode/tests.xml').exists())
            output = Path(tmp) / 'output'; output.mkdir()
            ci.package(workspace, output)
            self.assertLessEqual((output / 'early/early.zip').stat().st_size, ci.EARLY_BYTES)
            with zipfile.ZipFile(output / 'early/early.zip') as archive:
                self.assertIn('native/editmode/tests.xml', archive.namelist())
                self.assertIn(b'EXCERPT ONLY', archive.read('native/editmode/unity.log.excerpt.txt'))
            manifest = json.loads((output / 'parts/part00/evidence-manifest.json').read_text())
            self.assertEqual(32, manifest['max_parts'])
            self.assertLessEqual(max(p['size'] for p in manifest['parts']), 16 * 1024 * 1024)

    def test_source_has_no_fallback_player_or_preference_changes(self):
        self.assertEqual((('import', 'on'), ('editmode', 'on'), ('editmode', 'off'),
                          ('playmode', 'on'), ('playmode', 'off')), ci.PHASES)
        source = Path(ci.__file__).read_text()
        self.assertNotIn('defaults write', source)
        self.assertNotIn('local-unity-tests.sh', source)
        self.assertNotIn('--burst-disable-compilation', source)
        self.assertNotIn('-nographics', source)
        self.assertIn('start_new_session=True', source)
        self.assertIn("if key != 'GITHUB_TOKEN'", source)

    def test_job_env_expressions_use_only_supported_github_contexts(self):
        # GitHub's jobs.<job_id>.env context excludes runner/env/steps/job.
        # YAML parsing alone cannot detect a server-side expression-context error.
        allowed = {'github', 'needs', 'strategy', 'matrix', 'vars', 'secrets', 'inputs'}
        def check(source):
            blocks = re.findall(r'(?m)^    env:\n((?:^      [^\n]*\n)+)', source)
            for block in blocks:
                contexts = re.findall(r'\$\{\{\s*([A-Za-z_]\w*)[.\[]', block)
                self.assertLessEqual(set(contexts), allowed)
        workflow = (REPO / ci.WORKFLOW).read_text()
        check(workflow)
        with self.assertRaises(AssertionError):
            check('    env:\n      LAB_WORKSPACE: ${{ runner.temp }}/lab\n')
        self.assertNotIn('${{ runner.temp }}', workflow)

    def test_runner_path_step_writes_only_job_local_environment(self):
        workflow = (REPO / ci.WORKFLOW).read_text()
        start = workflow.index('      - name: Initialize run-specific paths on the assigned runner')
        stop = workflow.index('      - uses:', start)
        step = workflow[start:stop]
        script = textwrap.dedent(step.split('        run: |\n', 1)[1])
        self.assertLess(start, workflow.index('          path: ${{ env.LAB_SOURCE }}'))
        self.assertNotIn('${{', script)
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary).resolve()
            runner_temp = root / 'runner temp'; runner_temp.mkdir()
            checkout = root / 'checkout'; checkout.mkdir()
            environment_file = root / 'github-env'
            env = {'PATH': os.environ.get('PATH', ''), 'RUNNER_TEMP': str(runner_temp),
                   'GITHUB_RUN_ID': '123', 'GITHUB_RUN_ATTEMPT': '2', 'GITHUB_ENV': str(environment_file)}
            subprocess.run(['bash', '-e', '-c', script], env=env, cwd=checkout, check=True)
            entries = dict(line.split('=', 1) for line in environment_file.read_text().splitlines())
            self.assertEqual({'LAB_WORKSPACE': str(runner_temp / 'latios-s1a-123-2'),
                              'LAB_EVIDENCE': str(runner_temp / 'latios-s1a-evidence-123-2')}, entries)
            for value in entries.values():
                self.assertEqual(Path(value), ci.validate_workspace(checkout, Path(value)))
                self.assertFalse(Path(value).exists(), 'Initialization must not create native/worktree paths.')
            self.assertEqual([], list(checkout.iterdir()))
            self.assertEqual([], list(runner_temp.iterdir()))
            before = environment_file.read_bytes()
            del env['RUNNER_TEMP']
            result = subprocess.run(['bash', '-e', '-c', script], env=env, cwd=checkout, capture_output=True)
            self.assertNotEqual(0, result.returncode)
            self.assertEqual(before, environment_file.read_bytes())

    def test_workflow_is_dedicated_fail_closed_and_bounded(self):
        workflow = (REPO / ci.WORKFLOW).read_text()
        self.assertIn('branches: [dot/latios-lab-validate]', workflow)
        self.assertIn('paths: [Latios2022Lab/ci-request.json]', workflow)
        self.assertIn('needs: release-gate', workflow)
        self.assertIn('test "$UNITY_SELF_HOSTED" = true', workflow)
        self.assertNotIn("if: vars.UNITY_SELF_HOSTED", workflow)
        self.assertIn('cancel-in-progress: false', workflow)
        self.assertIn('contents: read', workflow)
        self.assertIn('actions: read', workflow)
        self.assertNotIn('contents: write', workflow)
        self.assertEqual(2, workflow.count('persist-credentials: false'))
        label = 'latios-s1a-${{ github.run_id }}-${{ github.run_attempt }}-${{ needs.release-gate.outputs.evidence-label }}'
        self.assertIn('evidence-label: ${{ steps.release.outputs.evidence_label }}', workflow)
        self.assertIn('validate --github-output "$GITHUB_OUTPUT"', workflow)
        self.assertIn('name: ' + label + '-early-review', workflow)
        for i in range(32):
            self.assertEqual(1, workflow.count('name: ' + label + '-part%02d' % i))
        self.assertNotIn(label + '-part32', workflow)
        root = (REPO / ci.ROOT_WORKFLOW).read_text()
        self.assertEqual(1, root.count(ci.EXCLUSION))
        self.assertIn('  pull_request:\n', root)
        self.assertIn('  workflow_dispatch:\n', root)
        self.assertIn('load_request(repo)', source := Path(ci.__file__).read_text())
        self.assertNotIn('p0_native_status=\"passed\"', source)


class ExperimentExecutionTests(unittest.TestCase):
    """Exercise real CI sequencing with synthetic native work and package-module boundaries."""
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name).resolve()
        self.repo = self.root / 'checkout'; self.repo.mkdir()
        self.workspace = self.root / 'isolated'
        self.editor = self.root / 'Unity'
        self.editor.write_text('NONEXECUTED FIXTURE'); self.editor.chmod(0o700)
        self.request = dict(request_fixture(), experiment=experiment_fixture())
        self.calls = []
        self.events = []
        self.native_effect = lambda command, project: 0
        self.validation_effect = None
        real = ci.load_cleanup_variant(REPO)
        self.variant = Mock()
        self.variant.check_experiment.side_effect = real.check_experiment
        self.variant.prepare.side_effect = self.prepare
        self.variant.verify.side_effect = self.verify
        self.variant.capture.side_effect = self.capture

    def prepare(self, workspace, project, experiment, source_commit):
        self.events.append('prepare')
        package = workspace / 'variant-packages/com.latios.latiosframework'
        package.mkdir(parents=True)
        (package / 'owned.cs').write_text('frozen package')
        record = dict(schema=1, experiment=experiment, source_commit=source_commit,
                      workspace=str(workspace), project_path=str(project), package_path=str(package))
        ci.write_json(workspace / 'experiment-record.json', record)
        return record

    def verify(self, project, record):
        self.events.append('verify')
        if (Path(record['package_path']) / 'owned.cs').read_text() != 'frozen package':
            raise ValueError('Injected package drift')
        return {'valid': True}

    def capture(self, workspace, artifacts, snapshot, record):
        self.events.append('capture:' + snapshot)
        target = artifacts / 'cleanup-variant' / snapshot
        target.mkdir(parents=True, exist_ok=True)
        ci.write_json(target / 'record.json', record)
        data = (Path(record['package_path']) / 'owned.cs').read_bytes()
        (target / 'package.tar').write_bytes(data)
        valid = data == b'frozen package'
        return {'valid': valid, 'errors': [] if valid else ['Injected package drift']}

    def worktree(self, command, **kwargs):
        self.events.append('worktree')
        project = self.workspace / 'source/Latios2022Lab'
        for folder in ('Packages', 'ProjectSettings', 'Assets/Latios2022Tests', 'Tools', 'Variants'):
            (project / folder).mkdir(parents=True, exist_ok=True)
        (project / 'Assets/Latios2022Tests/Frozen.cs').write_text('frozen fixture')

    def validate(self, *args):
        self.events.append('release')
        if self.validation_effect:
            self.validation_effect()
        ci.check_request(self.request, 'fixture/only', ci.BRANCH)
        return copy.deepcopy(self.request), {'p0_verified': {'fixture': True}}

    def phase(self, command, project, log):
        self.events.append('native')
        self.calls.append(command)
        log.write_text('Synthetic native invocation, not Unity evidence.')
        native = project / 'Artifacts' / ('%s-%s' % (command[2], command[4]))
        native.mkdir(parents=True)
        (native / 'unity.log').write_text('Original synthetic phase evidence')
        return self.native_effect(command, project)

    def execute(self, queue_effect=None):
        with ExitStack() as stack:
            for patcher in (patch.object(ci.sys, 'platform', 'darwin'),
                            patch.object(ci, 'load_cleanup_variant', return_value=self.variant),
                            patch.object(ci, 'validate', side_effect=self.validate),
                            patch.object(ci, 'check_queue', side_effect=queue_effect or (lambda *a: self.events.append('queue'))),
                            patch.object(ci.subprocess, 'run', side_effect=self.worktree),
                            patch.object(ci, 'run_owned_process', side_effect=self.phase)):
                stack.enter_context(patcher)
            return ci.execute(self.repo, self.workspace, 'fixture/only', ci.BRANCH, 'c' * 40, self.editor)

    def summary(self):
        return json.loads((self.workspace / 'Artifacts/ci-summary.json').read_text())

    def test_two_arms_use_distinct_workspaces_bindings_and_control_nonreproduction_stops(self):
        for arm in ci.EVIDENCE_LABELS[1:]:
            self.workspace = self.root / arm
            self.request['experiment'] = experiment_fixture(arm)
            self.calls.clear(); self.events.clear()
            control = arm == 'unpatched-local-control'
            if control:
                with self.assertRaisesRegex(ValueError, 'unexpectedly passed'):
                    self.execute()
            else:
                self.assertEqual(0, self.execute())
            expected_phases = ci.PHASES[:2] if control else ci.PHASES
            self.assertEqual(list(expected_phases), [(call[2], call[4]) for call in self.calls])
            for call in self.calls:
                self.assertEqual(str(self.workspace / 'experiment-record.json'), call[call.index('--experiment-record') + 1])
            gate = json.loads((self.workspace / 'runtime-gate.json').read_text())
            self.assertEqual({'path': str(self.workspace / 'experiment-record.json'),
                              'sha256': ci.digest(self.workspace / 'experiment-record.json')}, gate['experiment_record'])
            self.assertEqual(self.request['experiment'], gate['experiment'])
            self.assertEqual('c' * 40, gate['source_commit'])
            self.assertEqual(self.request['approved_source_commit'], gate['approved_source_commit'])
            self.assertLess(self.events.index('queue'), self.events.index('prepare'))
            self.assertEqual(len(expected_phases) + 1, self.events.count('release'))
            self.assertEqual(len(expected_phases) + 1, self.events.count('queue'))
            self.assertEqual(len(expected_phases) * 2 + 2, self.events.count('verify'))
            summary = self.summary()
            self.assertEqual(arm, summary['evidence_label'])
            self.assertEqual('FAILED' if control else 'PASSED', summary['editor_control'])
            if control:
                self.assertEqual('UNEXPECTED_PASS', summary['control_observation'])
                self.assertEqual(0, summary['phases'][1]['exit_code'])
                self.assertEqual('UNEXPECTED_PASS', summary['phases'][1]['status'])
                self.assertTrue(all(phase['status'] == 'NOT_RUN' for phase in summary['phases'][2:]))
            self.assertEqual('INCOMPLETE', summary['s1a'])
            self.assertEqual('NOT_RUN', summary['mobile'])

    def test_prepare_requires_valid_release_queue_fresh_workspace_and_editor(self):
        for blocked in ('release', 'queue', 'workspace', 'editor'):
            with self.subTest(blocked=blocked):
                self.workspace = self.root / blocked
                self.variant.prepare.reset_mock()
                self.validation_effect = (lambda: (_ for _ in ()).throw(ValueError('release blocked'))) if blocked == 'release' else None
                if blocked == 'workspace':
                    self.workspace.mkdir()
                if blocked == 'editor':
                    self.editor.unlink()
                queue = (lambda *a: (_ for _ in ()).throw(ValueError('queue blocked'))) if blocked == 'queue' else None
                with self.assertRaises(ValueError):
                    self.execute(queue)
                self.variant.prepare.assert_not_called()
                self.assertFalse(self.calls)

    def test_gate_record_and_release_drift_stop_before_next_phase(self):
        for drift in ('runtime-gate.json', 'experiment-record.json', 'release'):
            with self.subTest(drift=drift):
                self.workspace = self.root / ('drift-' + drift)
                self.calls.clear()
                self.request = dict(request_fixture(), experiment=experiment_fixture())
                def native(command, project):
                    if drift == 'release':
                        self.request['coordinator'] = 'changed release'
                    else:
                        path = self.workspace / drift
                        path.write_text(path.read_text() + ' ')
                    return 0
                self.native_effect = native
                with self.assertRaisesRegex(ValueError, 'changed'):
                    self.execute()
                self.assertEqual(1, len(self.calls))
                self.assertEqual('FAILED', self.summary()['editor_control'])

    def test_package_drift_is_captured_and_native_failure_remains_primary(self):
        for native_result in (0, 7, RuntimeError('Original launcher exception')):
            with self.subTest(native_result=native_result):
                self.workspace = self.root / ('package-' + str(len(list(self.root.iterdir()))))
                self.calls.clear()
                def native(command, project):
                    (self.workspace / 'variant-packages/com.latios.latiosframework/owned.cs').write_text('injected change')
                    if isinstance(native_result, Exception):
                        raise native_result
                    return native_result
                self.native_effect = native
                expected = 'Post-phase verification' if native_result == 0 else 'First control-group failure' if native_result == 7 else 'Original launcher exception'
                with self.assertRaisesRegex((ValueError, RuntimeError), expected):
                    self.execute()
                self.assertEqual(1, len(self.calls))
                summary = self.summary()
                self.assertEqual('FAILED', summary['phases'][0]['status'])
                self.assertIn('Injected package drift', summary['phases'][0]['experiment_after_errors']['verification'])
                captured = self.workspace / 'Artifacts/cleanup-variant/00-import-on-after/package.tar'
                self.assertEqual(b'injected change', captured.read_bytes())
                self.assertEqual('INCOMPLETE', summary['s1a'])

    def test_runtime_bindings_are_checked_after_final_success_and_first_native_failure(self):
        for filename, key in (('runtime-gate.json', 'runtime_gate'), ('experiment-record.json', 'experiment_record')):
            for native_result in (0, 7, RuntimeError('Original launcher exception')):
                with self.subTest(filename=filename, native_result=native_result):
                    self.workspace = self.root / ('binding-' + str(len(list(self.root.iterdir()))))
                    self.calls.clear()
                    target = ci.PHASES[-1] if native_result == 0 else ci.PHASES[0]
                    def native(command, project):
                        if (command[2], command[4]) != target:
                            return 0
                        path = self.workspace / filename
                        path.write_text(path.read_text() + ' ')
                        if isinstance(native_result, Exception):
                            raise native_result
                        return native_result
                    self.native_effect = native
                    expected = 'Post-phase verification' if native_result == 0 else 'First control-group failure' if native_result == 7 else 'Original launcher exception'
                    with self.assertRaisesRegex((ValueError, RuntimeError), expected):
                        self.execute()
                    summary = self.summary()
                    index = len(ci.PHASES) - 1 if native_result == 0 else 0
                    phase = summary['phases'][index]
                    self.assertEqual(index + 1, len(self.calls))
                    self.assertEqual('FAILED', summary['editor_control'])
                    self.assertEqual('FAILED', phase['status'])
                    self.assertEqual(None if isinstance(native_result, Exception) else native_result, phase['exit_code'])
                    self.assertIn('changed during the native phase', phase['post_phase_errors'][key])
                    self.assertIn(expected, summary['error'])
                    self.assertTrue(all(row['status'] == 'NOT_RUN' for row in summary['phases'][index + 1:]))

    def test_frozen_fixture_change_blocks_but_generated_urp_asset_is_retained(self):
        def native(command, project):
            (project / 'Assets/UniversalRenderPipelineGlobalSettings.asset').write_text('Generated URP fixture')
            (project / 'Assets/Latios2022Tests/Frozen.cs').write_text('unauthorized edit')
            return 0
        self.native_effect = native
        with self.assertRaisesRegex(ValueError, 'Frozen Lab source changed'):
            self.execute()
        self.assertEqual(1, len(self.calls))
        asset = self.workspace / 'Artifacts/editor-generated-assets/00-import-on/Assets/UniversalRenderPipelineGlobalSettings.asset'
        self.assertEqual('Generated URP fixture', asset.read_text())
        project = self.workspace / 'source/Latios2022Lab'
        self.assertNotIn('Assets/UniversalRenderPipelineGlobalSettings.asset', ci.lab_source_fingerprint(project))

    def test_capture_failure_does_not_hide_expected_control_failure(self):
        self.request['experiment'] = experiment_fixture('unpatched-local-control')
        def native(command, project):
            self.variant.capture.side_effect = ValueError('Injected capture failure')
            return 7
        self.native_effect = native
        with self.assertRaisesRegex(ValueError, 'First control-group failure'):
            self.execute()
        summary = self.summary()
        self.assertEqual('FAILED', summary['editor_control'])
        self.assertEqual('unpatched-local-control', summary['evidence_label'])
        self.assertIn('Injected capture failure', summary['phases'][0]['experiment_after_errors']['capture'])
        self.assertIn('First control-group failure', summary['error'])
        self.assertEqual('NOT_RUN', summary['phases'][1]['status'])

    def test_early_review_keeps_small_metadata_and_full_parts_keep_package_snapshots(self):
        self.assertEqual(0, self.execute())
        output = self.root / 'evidence'; output.mkdir()
        ci.package(self.workspace, output)
        with zipfile.ZipFile(output / 'early/early.zip') as archive:
            self.assertIn('cleanup-variant/final/record.json', archive.namelist())
            self.assertFalse(any(name.endswith('.tar') for name in archive.namelist()))
            self.assertIn('experiment-record.json', archive.namelist())
        manifest = json.loads((output / 'parts/part00/evidence-manifest.json').read_text())
        self.assertEqual(32, manifest['max_parts'])
        archived = {row['path'] for row in manifest['files']}
        self.assertIn('Artifacts/cleanup-variant/final/package.tar', archived)
        self.assertIn('Artifacts/cleanup-variant/before-native/package.tar', archived)
        oversized = self.root / 'oversized'; oversized.mkdir()
        with patch.object(ci, 'EXPERIMENT_METADATA_BYTES', 1), self.assertRaisesRegex(ValueError, 'metadata exceeds'):
            ci.package(self.workspace, oversized)
        self.assertTrue((oversized / 'parts/part00/evidence-manifest.json').exists())
        self.assertFalse((oversized / 'early').exists())


if __name__ == '__main__':
    unittest.main()
