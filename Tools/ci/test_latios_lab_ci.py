"""Synthetic parser/runner fixtures only. These tests never launch Unity or assert real P0 success."""
import copy
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


def api_fixture(repository, suffix):
    if '/jobs?' in suffix:
        return {'total_count': 1, 'jobs': [{'id': 456, 'name': 'unity-tests', 'conclusion': 'success',
                 'steps': [{'name': 'Run EditMode and PlayMode tests', 'conclusion': 'success'}]}]}
    if suffix.startswith('workflows/'):
        return {'total_count': 0, 'workflow_runs': []}
    return dict(id=123, run_attempt=2, repository={'full_name': repository}, head_sha='a' * 40,
                path=ci.ROOT_WORKFLOW, status='completed', conclusion='success')


class GateTests(unittest.TestCase):
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
        for i in range(32):
            self.assertEqual(1, workflow.count('name: latios-s1a-part%02d' % i))
        self.assertNotIn('latios-s1a-part32', workflow)
        root = (REPO / ci.ROOT_WORKFLOW).read_text()
        self.assertEqual(1, root.count(ci.EXCLUSION))
        self.assertIn('  pull_request:\n', root)
        self.assertIn('  workflow_dispatch:\n', root)
        self.assertIn('load_request(repo)', source := Path(ci.__file__).read_text())
        self.assertNotIn('p0_native_status=\"passed\"', source)


if __name__ == '__main__':
    unittest.main()
