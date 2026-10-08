import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
from datetime import datetime, timedelta, timezone

spec = importlib.util.spec_from_file_location('lab', Path(__file__).with_name('lab.py'))
lab = importlib.util.module_from_spec(spec)
spec.loader.exec_module(lab)


class LauncherTests(unittest.TestCase):
    def test_static_preflight_keeps_native_status_unrun(self):
        report = lab.preflight()
        self.assertEqual('STATIC_PREFLIGHT_ONLY', report['status'])
        self.assertEqual('NOT_RUN', report['native'])

    def test_unity_normalized_version_record_is_accepted_without_relaxing_pin(self):
        self.assertIsNone(lab.parse_project_version('m_EditorVersion: 2022.3.62f2\n'))
        self.assertEqual('2022.3.62f2 (abcdef123456)', lab.parse_project_version(
            'm_EditorVersion: 2022.3.62f2\nm_EditorVersionWithRevision: 2022.3.62f2 (abcdef123456)\n'))
        for text in ('m_EditorVersion: 2022.3.62f1\n',
                     'm_EditorVersion: 2022.3.62f2\nm_EditorVersion: 2022.3.62f2\n',
                     'm_EditorVersion: 2022.3.62f2\nm_EditorVersionWithRevision: 6000.3.8f1 (test)\n'):
            with self.assertRaises(ValueError): lab.parse_project_version(text)

    def test_normalized_define_order_is_accepted_and_missing_define_rejected(self):
        settings = (lab.PROJECT / 'ProjectSettings/ProjectSettings.asset').read_text()
        reordered = settings.replace('ENTITY_STORE_V1;UNITY_BURST_EXPERIMENTAL_ATOMIC_INTRINSICS',
                                     'UNITY_BURST_EXPERIMENTAL_ATOMIC_INTRINSICS;ENTITY_STORE_V1')
        lab.check_defines(reordered)
        with self.assertRaises(ValueError): lab.check_defines(reordered.replace(';ENTITY_STORE_V1', ''))

    def test_read_only_preflight_never_launches_process(self):
        with patch.object(lab.subprocess, 'run', side_effect=AssertionError('Native launch forbidden')):
            self.assertEqual(0, lab.main(['preflight']))

    def test_dry_plan_never_launches_process(self):
        with patch.object(lab.subprocess, 'run', side_effect=AssertionError('Native launch forbidden')):
            self.assertEqual(0, lab.main(['editmode', '--editor', '/not-installed/Unity']))

    def test_execute_without_gate_fails_before_process(self):
        with patch.object(lab.subprocess, 'run', side_effect=AssertionError('Native launch forbidden')):
            with self.assertRaisesRegex(ValueError, 'coordinator-issued'):
                lab.main(['import', '--editor', '/not-installed/Unity', '--execute'])

    def test_command_is_isolated_and_test_runner_owns_exit(self):
        c = lab.make_command(Path('/editor/Unity'), 'editmode', Path('/evidence'), 'on', 'StandaloneOSX')
        self.assertEqual(str(lab.PROJECT), c[c.index('-projectPath') + 1])
        self.assertNotIn('-quit', c)
        self.assertNotIn('-nographics', c)
        self.assertIn('Latios2022Lab.Editor', c)

    def test_burst_off_is_per_process_not_preference_mutation(self):
        c = lab.make_command(Path('/editor/Unity'), 'playmode', Path('/evidence'), 'off', 'StandaloneOSX')
        self.assertIn('--burst-disable-compilation', c)
        self.assertNotIn('-nographics', c)

    def test_rosetta_runner_still_launches_physical_arm64_host(self):
        with patch.object(lab.sys, 'platform', 'darwin'), patch.object(lab.subprocess, 'check_output', return_value='1\n'):
            c = lab.make_command(Path('/editor/Unity'), 'import', Path('/evidence'), 'on', 'StandaloneOSX')
            self.assertEqual(['/usr/bin/arch', '-arm64'], c[:2])

    def test_import_requires_real_capture_entrypoint(self):
        c = lab.make_command(Path('/editor/Unity'), 'import', Path('/evidence'), 'on', 'StandaloneOSX')
        self.assertIn('Latios2022Lab.LabEnvironment.CaptureEnvironment', c)
        self.assertIn('-quit', c)

    def test_player_build_cannot_be_managed_control(self):
        with self.assertRaisesRegex(ValueError, 'IL2CPP smoke requires Burst'):
            lab.main(['player-build', '--editor', '/editor/Unity', '--burst', 'off'])

    def valid_gate(self):
        return dict(schema=1, project_path=str(lab.PROJECT), p0_native_status='passed',
                    p0_commit='a' * 40, p0_evidence_url='https://example.invalid/test-fixture-only',
                    runner_reserved=True, coordinator='unit-test-only',
                    expires_utc=(datetime.now(timezone.utc) + timedelta(minutes=1)).isoformat(),
                    allowed_phases=['import', 'editmode', 'playmode', 'player-build'])

    def test_gate_rejects_pending_foreign_expired_and_unreserved(self):
        mutations = [dict(p0_native_status='pending'), dict(project_path='/elsewhere/Latios2022Lab'),
                     dict(p0_commit='short'), dict(p0_evidence_url=''), dict(runner_reserved=False),
                     dict(expires_utc='2000-01-01T00:00:00Z'), dict(allowed_phases=[])]
        with tempfile.TemporaryDirectory() as tmp:
            p = Path(tmp) / 'gate.json'
            for mutation in mutations:
                with self.subTest(mutation=mutation):
                    data = self.valid_gate(); data.update(mutation); p.write_text(json.dumps(data))
                    with self.assertRaises(ValueError): lab.read_gate(p, lab.PROJECT, 'import')

    def test_gate_allows_only_explicit_phase_and_player_needs_editor_gate(self):
        with tempfile.TemporaryDirectory() as tmp:
            p = Path(tmp) / 'gate.json'; p.write_text(json.dumps(self.valid_gate()))
            self.assertEqual(1, lab.read_gate(p, lab.PROJECT, 'import')['schema'])
            with self.assertRaisesRegex(ValueError, 'actual S1a Editor gates'):
                lab.read_gate(p, lab.PROJECT, 'player-build')

    def test_lock_parser_rejects_floating_git_and_dependency_drift(self):
        # Synthetic parser inputs live only in a temporary directory, never Packages/.
        data = {'dependencies': {k: {'version': v} for k, v in lab.RESOLVED_PINS.items()}}
        data['dependencies']['com.latios.latiosframework'] = dict(source='git', hash=lab.LATIOS_COMMIT, version=lab.LATIOS_URL)
        with tempfile.TemporaryDirectory() as tmp:
            p = Path(tmp) / 'synthetic-parser-input.json'
            p.write_text(json.dumps(data)); lab.check_lock(p)
            for name, key, value in [('com.latios.latiosframework', 'hash', 'main'),
                                      ('com.unity.burst', 'version', '1.8.27'),
                                      ('com.unity.collections', 'version', '2.5.2'),
                                      ('com.unity.test-framework', 'version', '1.1.33')]:
                bad = copy.deepcopy(data); bad['dependencies'][name][key] = value; p.write_text(json.dumps(bad))
                with self.assertRaises(ValueError): lab.check_lock(p)

    def test_manifest_accepts_only_observed_toolchain_with_matching_lock(self):
        manifest = json.loads((lab.PROJECT / 'Packages/manifest.json').read_text())
        self.assertEqual({}, lab.check_manifest(manifest))
        toolchain, version = next(iter(lab.EDITOR_MANIFEST_ADDITIONS.items()))
        manifest['dependencies'][toolchain] = version
        lock = {toolchain: dict(version=version, source='registry')}
        self.assertEqual({toolchain: version}, lab.check_manifest(manifest, lock))
        for bad_lock in (None, {}, {toolchain: dict(version='2.0.6', source='registry')},
                         {toolchain: dict(version=version, source='local')}):
            with self.subTest(lock=bad_lock), self.assertRaises(ValueError):
                lab.check_manifest(manifest, bad_lock)
        for extra in ({'com.unity.toolchain.macos-arm64-linux-x86_64': '2.0.6'},
                      {'com.unity.entities': '1.3.8'}, {'unreviewed.package': '1.0.0'},
                      {'unreviewed.package': None}):
            bad = copy.deepcopy(manifest); bad['dependencies'].update(extra)
            with self.subTest(extra=extra), self.assertRaises(ValueError): lab.check_manifest(bad, lock)
        manifest['scopedRegistries'] = []
        with self.assertRaises(ValueError): lab.check_manifest(manifest, lock)

    def test_legacy_duplicate_runner_configuration_and_missing_nunit_are_rejected(self):
        for kind in ('Editor', 'PlayMode'):
            path = lab.PROJECT / ('Assets/Latios2022Tests/' + kind + '/Latios2022Lab.' + kind + '.asmdef')
            data = json.loads(path.read_text()); lab.check_test_assembly(data)
            for mutation in ({'optionalUnityReferences': ['TestAssemblies']},
                             {'precompiledReferences': []}, {'overrideReferences': False},
                             {'defineConstraints': []}, {'autoReferenced': True},
                             {'references': data['references'] + ['UnityEngine.TestRunner']}):
                with self.subTest(kind=kind, mutation=mutation), self.assertRaises(ValueError):
                    lab.check_test_assembly(dict(data, **mutation))

    def test_registered_package_inventory_cannot_hide_urp_lock_mismatch(self):
        records = [{'name': name, 'version': version} for name, version in lab.RESOLVED_PINS.items()]
        lock = {r['name']: {'version': r['version']} for r in records}
        with tempfile.TemporaryDirectory() as tmp:
            p = Path(tmp) / 'synthetic-environment.json'
            p.write_text(json.dumps({'packages': records})); lab.check_registered_packages(p, lock)
            lock['com.unity.render-pipelines.universal']['version'] = '14.0.11'
            with self.assertRaisesRegex(ValueError, 'disagree'): lab.check_registered_packages(p, lock)
            lock['com.unity.render-pipelines.universal']['version'] = '14.0.12'
            p.write_text(json.dumps({'packages': records + [records[0]]}))
            with self.assertRaisesRegex(ValueError, 'Duplicate'): lab.check_registered_packages(p, lock)

    def test_test_results_reject_skips_and_wrong_discovery_count(self):
        with tempfile.TemporaryDirectory() as tmp:
            p = Path(tmp) / 'tests.xml'
            for text in ('<test-run result="Passed"/>', '<test-run result="Passed"><test-case result="Skipped"/></test-run>'):
                p.write_text(text)
                with self.assertRaises(ValueError): lab.check_results(p, 'playmode')
            p.write_text('<test-run result="Passed"><test-case result="Passed"/></test-run>')
            self.assertEqual(1, lab.check_results(p, 'playmode'))

    def test_project_symlink_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            link = Path(tmp) / 'Latios2022Lab'; link.symlink_to(lab.PROJECT, target_is_directory=True)
            with self.assertRaisesRegex(ValueError, 'symlink'): lab.validate_paths(link)

    def test_editor_identity_check_never_receives_project_arguments(self):
        with tempfile.TemporaryDirectory() as tmp:
            command = ['/usr/bin/arch', '-arm64', '/editor/Unity', '-batchmode', '-projectPath', str(lab.PROJECT)]
            reply = lab.subprocess.CompletedProcess([], 0, '2022.3.62f2\n', '')
            with patch.object(lab.subprocess, 'run', return_value=reply) as launch:
                lab.verify_editor_binary(command, Path(tmp))
                self.assertEqual(['/usr/bin/arch', '-arm64', '/editor/Unity', '-version'], launch.call_args.args[0])
                self.assertNotIn('-projectPath', launch.call_args.args[0])
                self.assertEqual(Path(tmp), launch.call_args.kwargs['cwd'])

    def test_wrong_or_failed_editor_version_is_blocked(self):
        with tempfile.TemporaryDirectory() as tmp:
            for code, version in ((0, '6000.3.8f1'), (1, '2022.3.62f2'), (0, 'unknown')):
                reply = lab.subprocess.CompletedProcess([], code, version, '')
                with patch.object(lab.subprocess, 'run', return_value=reply):
                    with self.assertRaises(ValueError):
                        lab.verify_editor_binary(['/editor/Unity', '-batchmode'], Path(tmp))

    def test_destroy_oracle_precedes_whole_world_teardown(self):
        source = (lab.PROJECT / 'Assets/Latios2022Tests/Runtime/CollectionProbe.cs').read_text()
        branch = source[source.index('else if (exit == CollectionExit.DestroyEntity)'):source.index('                world.Dispose();')]
        self.assertIn('world.initializationSystemGroup.Update()', branch)
        self.assertIn('witness[0] != 1', branch)
        self.assertIn('world.EntityManager.Exists(owner)', branch)
        self.assertIn('output[0] != ExpectedSum', branch)

    def test_playmode_witness_uses_requested_mode_and_shared_runtime_policy(self):
        root = lab.PROJECT / 'Assets/Latios2022Tests'
        playmode = (root / 'PlayMode/PlayModeTests.cs').read_text()
        policy = (root / 'Runtime/LabRunPolicy.cs').read_text()
        editor = (root / 'EditorTools/LabEnvironment.cs').read_text()
        self.assertIn('expectedBurst = LabRunPolicy.VerifyRequestedMode()', playmode)
        self.assertIn('PsyshockProbe.RunQueries(expectedBurst)', playmode)
        self.assertNotIn('RunQueries(BurstCompiler.IsEnabled)', playmode)
        self.assertIn('LATIOS_LAB_EXPECT_BURST', policy)
        self.assertIn('BurstCompiler.IsEnabled != expected', policy)
        self.assertIn('Application.unityVersion != EditorVersion', policy)
        self.assertIn('UNITY_EDITOR && !ENABLE_UNITY_COLLECTIONS_CHECKS', policy)
        self.assertIn('!BurstCompiler.Options.EnableBurstSafetyChecks', policy)
        self.assertNotIn('using UnityEditor', policy)
        self.assertIn('LabRunPolicy.VerifyRequestedMode()', editor)

    def test_each_assembly_declares_its_direct_api_dependencies(self):
        required = {
            'Latios2022Lab.Runtime': {'Latios.Core', 'Latios.Transforms', 'Latios.Psyshock', 'Unity.Entities', 'Unity.Collections', 'Unity.Jobs', 'Unity.Mathematics', 'Unity.Burst'},
            'Latios2022Lab.EditorTools': {'Latios2022Lab.Runtime', 'Latios.Core', 'Unity.Entities', 'Unity.Burst'},
            'Latios2022Lab.Editor': {'Latios2022Lab.Runtime', 'Latios2022Lab.EditorTools', 'Latios.Core', 'Unity.Entities', 'Unity.Collections', 'UnityEngine.TestRunner', 'UnityEditor.TestRunner'},
            'Latios2022Lab.PlayMode': {'Latios2022Lab.Runtime', 'Unity.Burst', 'UnityEngine.TestRunner'},
        }
        for path in (lab.PROJECT / 'Assets').rglob('*.asmdef'):
            data = json.loads(path.read_text())
            self.assertLessEqual(required[data['name']], set(data['references']), str(path))

    def test_product_manifest_remains_without_latios(self):
        data = json.loads((lab.PROJECT.parent / 'Packages/manifest.json').read_text())
        self.assertNotIn('com.latios.latiosframework', data['dependencies'])


if __name__ == '__main__':
    unittest.main()
