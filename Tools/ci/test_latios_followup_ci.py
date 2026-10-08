"""Temporary Git/parser fixtures only; never release CI, fetch artifacts or launch Unity."""
from contextlib import ExitStack
from datetime import datetime, timedelta, timezone
import copy
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import ANY, patch

spec = importlib.util.spec_from_file_location('followup_ci', Path(__file__).with_name('latios_followup_ci.py'))
c = importlib.util.module_from_spec(spec)
spec.loader.exec_module(c)


class ConditionalCiTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name).resolve()
        self.repo = self.root / 'release'; self.repo.mkdir()
        self.git('init', '-q'); self.git('config', 'user.name', 'Synthetic CI Fixture')
        self.git('config', 'user.email', 'fixture@local.invalid')
        self.write('Assets/Product.cs', 'unchanged product')
        self.write('.github/workflows/latios-lab.yml', 'unchanged Editor workflow')
        self.write('.github/workflows/unity-self-hosted.yml', 'unchanged root workflow')
        self.p0 = self.commit('synthetic P0')
        self.write(c.ci.REQUEST, json.dumps(dict(p0_commit=self.p0, p0_evidence_url='https://github.com/fixture/only/actions/runs/12/attempts/1')))
        self.write('Latios2022Lab/Assets/Frozen.cs', 'frozen native')
        self.native = self.commit('synthetic native')
        self.tree = self.git('rev-parse', 'HEAD^{tree}')
        self.binding = dict(run_id=123, attempt=1, head_sha=self.native,
                            run_url='https://github.com/fixture/only/actions/runs/123/attempts/1',
                            workflow=c.ci.WORKFLOW, artifacts=[dict(id=7, name='latios-s1a-123-1-patched-local-variant-part00',
                            size_in_bytes=5, sha256='4' * 64)], parts_manifest_sha256='1' * 64,
                            archive_sha256='2' * 64, archive_size=5, evidence_manifest_sha256='3' * 64)
        self.write(c.EDITOR_BINDING, json.dumps(self.binding))
        self.write(c.WORKFLOW, 'synthetic new workflow')
        self.tool = self.commit('synthetic reviewed preparation')
        self.request = dict(schema=c.SCHEMA, repository='fixture/only', tool_commit=self.tool,
                            source_commit=self.native, source_tree=self.tree, stage='repeat-clean-import', allowed_phases=['import'],
                            target='StandaloneOSX', architecture='x86_64', burst='on', p0_commit=self.p0,
                            p0_native_status='passed', p0_native_evidence_reviewed=True,
                            p0_evidence_url='https://github.com/fixture/only/actions/runs/12/attempts/1',
                            runner_reserved=True, coordinator='synthetic fixture only',
                            expires_utc=(datetime.now(timezone.utc) + timedelta(hours=1)).isoformat(),
                            experiment=c.followup.lab.variant_tools().experiment_for_arm('patched-local-variant'),
                            editor_artifacts=self.binding, editor_evidence_reviewed=True)
        self.write(c.REQUEST, json.dumps(self.request) + '\n')
        self.release = self.commit('synthetic separate import release')
        self.stack = ExitStack(); self.addCleanup(self.stack.close)
        self.stack.enter_context(patch.object(c, 'NATIVE_COMMIT', self.native))
        self.stack.enter_context(patch.object(c, 'NATIVE_TREE', self.tree))

    def git(self, *args):
        return subprocess.check_output(['git', *args], cwd=self.repo, text=True, stderr=subprocess.DEVNULL).strip()

    def write(self, name, text):
        path = self.repo / name; path.parent.mkdir(parents=True, exist_ok=True); path.write_text(text)

    def commit(self, message):
        self.git('add', '.'); self.git('commit', '-qm', message)
        return self.git('rev-parse', 'HEAD')

    def validate(self, **kwargs):
        return c.validate_release(self.repo, 'fixture/only', c.ci.BRANCH, self.release, **kwargs)

    def test_default_plan_does_no_git_network_or_native_work(self):
        with patch.object(c, 'validate_release', side_effect=AssertionError('not released')), \
             patch.object(c, 'prepare_gate', side_effect=AssertionError('no fetch')), \
             patch.object(c.followup, 'execute', side_effect=AssertionError('no native')):
            self.assertEqual(0, c.main([]))

    def test_separate_tool_checkout_solves_gate_commit_self_reference(self):
        self.assertEqual(self.request, self.validate())
        with patch.object(c, 'REPO', self.repo), self.assertRaisesRegex(ValueError, 'approved preparation checkout'):
            self.validate(prepared_checkout=True)
        tools = self.root / 'tools'
        self.git('worktree', 'add', '--detach', str(tools), self.tool)
        with patch.object(c, 'REPO', tools):
            self.assertEqual(self.tool, self.validate(prepared_checkout=True)['tool_commit'])

    def test_request_only_linear_parent_chain_accepts_second_explicit_release(self):
        self.request['expires_utc'] = (datetime.now(timezone.utc) + timedelta(hours=2)).isoformat()
        self.write(c.REQUEST, json.dumps(self.request)); self.release = self.commit('synthetic second request')
        self.validate()

    def test_source_preparation_push_without_request_delta_is_not_a_release(self):
        with self.assertRaisesRegex(ValueError, 'request-only child chain'):
            c.check_release_chain(self.repo, self.tool, self.tool)

    def test_release_cannot_smuggle_code_or_point_at_other_revision(self):
        self.write('Latios2022Lab/Tools/followup.py', 'unreviewed code')
        self.write(c.REQUEST, json.dumps(dict(self.request, coordinator='changed fixture')))
        self.release = self.commit('synthetic mixed release')
        with self.assertRaisesRegex(ValueError, 'only followup-request'): self.validate()
        with self.assertRaisesRegex(ValueError, 'exact triggering'):
            c.validate_release(self.repo, 'fixture/only', c.ci.BRANCH, self.tool)

    def test_prepared_source_cannot_change_product_or_original_editor_workflow(self):
        for name in ('Assets/Product.cs', '.github/workflows/latios-lab.yml', 'Latios2022Lab/Assets/Frozen.cs'):
            self.git('reset', '--hard', self.tool)
            self.write(name, 'changed disallowed input')
            changed = self.commit('synthetic bad preparation')
            request = dict(self.request, tool_commit=changed)
            with self.subTest(name=name), self.assertRaisesRegex(ValueError, 'native fixture/product'):
                c.check_prepared_source(self.repo, request)

    def test_request_rejects_unknown_fields_pending_review_expiry_foreign_native_or_artifacts(self):
        for mutation in (dict(extra=True), dict(editor_evidence_reviewed=False), dict(source_commit='0' * 40),
                         dict(source_tree='0' * 40), dict(editor_artifacts={}), dict(allowed_phases=['player-build']),
                         dict(expires_utc='2000-01-01T00:00:00Z'), dict(architecture='guess'), dict(runner_reserved=False)):
            with self.subTest(mutation=mutation), self.assertRaises(ValueError):
                c.check_request(dict(self.request, **mutation), self.repo, 'fixture/only', c.ci.BRANCH)
        with self.assertRaises(ValueError): c.check_request(self.request, self.repo, 'fixture/only', 'refs/heads/main')

    def test_player_release_requires_its_own_reviewed_repeat_transport(self):
        request = dict(self.request, stage='mac-il2cpp-smoke', allowed_phases=c.followup.STAGES['mac-il2cpp-smoke'])
        with self.assertRaisesRegex(ValueError, 'independent requests'):
            c.check_request(request, self.repo, 'fixture/only', c.ci.BRANCH)
        repeated = copy.deepcopy(self.binding); repeated['workflow'] = c.WORKFLOW
        repeated['artifacts'][0]['name'] = 'latios-followup-123-1-repeat-clean-import-part00'
        request.update(repeat_artifacts=repeated, repeat_evidence_reviewed=True)
        c.check_request(request, self.repo, 'fixture/only', c.ci.BRANCH)
        request['repeat_evidence_reviewed'] = False
        with self.assertRaisesRegex(ValueError, 'Repeat import'): c.check_request(request, self.repo, 'fixture/only', c.ci.BRANCH)

    def test_conditional_queue_allows_only_own_run_and_rejects_incomplete_inventory(self):
        c.check_own_queue('fixture/only', 7, lambda *args: {'total_count': 1, 'workflow_runs': [{'id': 7}]})
        for data in ({'total_count': 1, 'workflow_runs': [{'id': 8}]}, {'total_count': 2, 'workflow_runs': [{'id': 7}]}):
            with self.assertRaises(ValueError): c.check_own_queue('fixture/only', 7, lambda *args: data)

    def test_global_queue_blocks_diagnostics_unknown_or_changed_targets_but_allows_exact_hosted_harness(self):
        harness = (Path(__file__).resolve().parents[2] / '.github/workflows/harness.yml').read_bytes()
        self.write('.github/workflows/harness.yml', harness.decode()); head = self.commit('synthetic hosted definition')
        own = dict(id=7, path=c.WORKFLOW)
        hosted = dict(id=8, path='.github/workflows/harness.yml', head_sha=head)
        def fetch(rows): return lambda *args: {'total_count': len(rows), 'workflow_runs': rows}
        c.check_native_queue('fixture/only', 7, self.repo, fetch([own, hosted]))
        for path in c.NATIVE_WORKFLOWS | {'.github/workflows/unknown-matrix.yml'}:
            with self.subTest(path=path), self.assertRaises(ValueError):
                c.check_native_queue('fixture/only', 7, self.repo, fetch([own, dict(id=9, path=path)]))
        self.write('.github/workflows/harness.yml', harness.decode() + '\n# unreviewed change\n')
        hosted['head_sha'] = self.commit('synthetic changed definition')
        with self.assertRaisesRegex(ValueError, 'definition changed'):
            c.check_native_queue('fixture/only', 7, self.repo, fetch([hosted]))

    def test_hosted_restoration_materializes_gate_but_never_advances_native(self):
        review = self.root / 'review'
        calls = []
        def restore(repository, spec, path):
            calls.append((repository, spec, path)); result = path / 'restored/Artifacts'; result.mkdir(parents=True); return result
        with patch.object(c, 'validate_release', return_value=self.request), patch.object(c, 'check_own_queue'), patch.object(c, 'check_native_queue'), \
             patch.object(c.followup, 'check_gate') as local, patch.object(c.followup, 'check_remote') as remote, \
             patch.object(c.followup, 'execute', side_effect=AssertionError('hosted cannot execute')):
            request, gate_path = c.prepare_gate(self.repo, 'fixture/only', c.ci.BRANCH, self.release, review, 7, restore)
            gate = json.loads(gate_path.read_text())
            self.assertEqual(self.tool, gate['tool_commit']); self.assertEqual(self.native, gate['source_commit'])
            self.assertEqual(str(review / 'editor/restored/Artifacts'), gate['editor_evidence']['root'])
            self.assertEqual('3' * 64, gate['editor_evidence']['manifest_sha256'])
            self.assertNotIn('editor_artifacts', gate); self.assertEqual(1, len(calls))
            local.assert_called_once_with(gate); remote.assert_called_once_with(gate)
            with self.assertRaisesRegex(ValueError, 'fresh separate'):
                c.prepare_gate(self.repo, 'fixture/only', c.ci.BRANCH, self.release, review, 7, restore)

    def test_repeat_proof_binds_completed_release_request_and_stage(self):
        root = self.root / 'proof'; root.mkdir()
        spec = dict(self.binding, head_sha=self.release, run_id=99, workflow=c.WORKFLOW)
        context = dict(repository='fixture/only', ref=c.ci.BRANCH, run_id=99, attempt=1, release_commit=self.release,
                       stage='repeat-clean-import', tool_commit=self.tool,
                       request_sha256=c.ci.digest(self.repo / c.REQUEST))
        c.ci.write_json(root / 'followup-ci.json', context)
        c.check_repeat_identity(root, spec, self.request, self.repo, self.release)
        for mutation in (dict(stage='mac-il2cpp-smoke'), dict(run_id=100), dict(release_commit=self.tool), dict(request_sha256='0' * 64)):
            c.ci.write_json(root / 'followup-ci.json', dict(context, **mutation))
            with self.subTest(mutation=mutation), self.assertRaises(ValueError):
                c.check_repeat_identity(root, spec, self.request, self.repo, self.release)

    def test_native_failure_preserves_its_ci_context_and_does_not_auto_advance(self):
        workspace = self.root / 'native'; evidence = workspace / 'Artifacts'
        review = self.root / 'review'; review.mkdir(); (review / 'prerequisite-summary.json').write_text('{}')
        gate = review / 'gate.json'; gate.write_text('{}')
        def fail(*args, **kwargs):
            evidence.mkdir(parents=True)
            raise ValueError('Original import failed')
        with patch.object(c.sys, 'platform', 'darwin'), patch.object(c, 'prepare_gate', return_value=(self.request, gate)), \
             patch.object(c.followup, 'execute', side_effect=fail) as native:
            with self.assertRaisesRegex(ValueError, 'Original import failed'):
                c.execute(self.repo, 'fixture/only', c.ci.BRANCH, self.release, review, 7, 1, workspace, Path('/synthetic/Unity'))
            native.assert_called_once_with(gate, workspace, Path('/synthetic/Unity'), queue_check=ANY)
        context = json.loads((evidence / 'followup-ci.json').read_text())
        self.assertEqual(self.release, context['release_commit']); self.assertEqual(self.tool, context['tool_commit'])
        self.assertEqual('repeat-clean-import', context['stage'])
        self.assertTrue((evidence / 'evidence-sha256.json').is_file())

    def test_existing_or_symlink_workspace_refusal_cannot_rewrite_stale_evidence(self):
        workspace = self.root / 'old'; evidence = workspace / 'Artifacts'; evidence.mkdir(parents=True)
        marker = evidence / 'evidence-sha256.json'; marker.write_text('existing immutable evidence')
        link = self.root / 'linked'; link.symlink_to(workspace, target_is_directory=True)
        with patch.object(c.sys, 'platform', 'darwin'), patch.object(c, 'prepare_gate') as prepare, \
             patch.object(c.followup, 'execute') as native:
            for selected in (workspace, link):
                with self.subTest(path=selected), self.assertRaises(ValueError):
                    c.execute(self.repo, 'fixture/only', c.ci.BRANCH, self.release, self.root / 'review', 7, 1, selected, Path('/Unity'))
            prepare.assert_not_called(); native.assert_not_called()
        self.assertEqual('existing immutable evidence', marker.read_text())
        self.assertEqual([marker], list(evidence.iterdir()))

    def test_always_packaging_cannot_follow_refused_workspace_symlinks(self):
        workspace = self.root / 'old'; (workspace / 'Artifacts').mkdir(parents=True)
        link = self.root / 'linked'; link.symlink_to(workspace, target_is_directory=True)
        with patch.object(c.ci, 'package') as pack:
            with self.assertRaises(ValueError): c.package(link, self.root / 'packed', 7, 1, self.release)
            pack.assert_not_called()
        self.assertFalse((self.root / 'packed').exists())

    def test_packaging_refuses_other_runs_even_when_directory_is_real(self):
        workspace = self.root / 'old'; evidence = workspace / 'Artifacts'; evidence.mkdir(parents=True)
        c.ci.write_json(evidence / 'followup-ci.json', dict(run_id=6, attempt=1, release_commit=self.release))
        with patch.object(c.ci, 'package') as pack:
            with self.assertRaisesRegex(ValueError, 'stale evidence'):
                c.package(workspace, self.root / 'packed', 7, 1, self.release)
            pack.assert_not_called()
        self.assertFalse((self.root / 'packed').exists())

    def test_workflow_does_not_launch_for_preparation_or_modify_existing_gate(self):
        root = Path(__file__).resolve().parents[2]
        text = (root / c.WORKFLOW).read_text()
        self.assertIn('branches: [dot/latios-lab-validate]', text)
        self.assertIn('paths: [Latios2022Lab/followup-request.json]', text)
        self.assertNotIn('workflow_dispatch', text)
        self.assertNotIn('paths: [Latios2022Lab/ci-request.json]', text)
        self.assertNotIn('pull_request', text)
        self.assertIn('needs: followup-gate', text)
        self.assertIn('runs-on: [self-hosted, unity]', text)
        self.assertEqual(1, text.count('runs-on: [self-hosted, unity]'))
        self.assertIn('group: latios-s1a-control', text); self.assertIn('cancel-in-progress: false', text)
        self.assertIn('contents: read\n  actions: read', text)
        self.assertNotIn(': write', text); self.assertNotIn('secrets.', text.replace('secrets.GITHUB_TOKEN', ''))
        self.assertIn('ref: ${{ steps.release.outputs.tool_commit }}', text)
        self.assertIn('ref: ${{ needs.followup-gate.outputs.tool_commit }}', text)
        self.assertNotIn('${{ runner.temp }}', text)
        self.assertNotIn('continue-on-error', text)
        self.assertEqual(4, text.count('persist-credentials: false'))
        for index in range(32): self.assertIn('/parts/part{:02d}/'.format(index), text)
        self.assertFalse((root / c.REQUEST).exists(), 'Preparation must not ship an executable release request')


if __name__ == '__main__':
    unittest.main()
