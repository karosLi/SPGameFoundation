#!/usr/bin/env python3
"""Python-only contracts using miniature real Git objects, never Unity evidence.

The synthetic lock and PackageInfo objects below are negative-test fixtures only.
No native process, gate, package cache, or fabricated native output is produced.
"""
import copy
import difflib
import importlib.util
import json
from pathlib import Path
import shutil
import stat
import subprocess
import tarfile
import tempfile
import unittest
from unittest import mock

SPEC = importlib.util.spec_from_file_location('cleanup_variant', Path(__file__).with_name('cleanup_variant.py'))
cv = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(cv)


class FrozenOfficialContractTests(unittest.TestCase):
    def test_checked_in_full_source_inventory_reconstructs_official_tree(self):
        inventory, patch = cv._contract()
        self.assertEqual((inventory['file_count'], inventory['total_bytes']), (1235, 23986607))
        self.assertEqual(cv._tree_sha(inventory['files']), '4790057a1964150f2ca815f89cc85498bc1cb43e')
        self.assertEqual(cv.sha256(patch), '6736b6b8f1c0ca32151a44163afc9a75fcf133a70dbe15b4175a8d8f3ab994a0')
        names = {row['path'] for row in inventory['files']}
        self.assertIn('LICENSE.md', names)
        self.assertTrue(any('THIRD' in name.upper() for name in names))
        self.assertEqual(len(cv.check_owned_inputs(cv.PROJECT)), 26)

    def test_absent_experiment_is_original_git_control(self):
        self.assertIsNone(cv.check_experiment(None))

    def test_request_requires_every_fixed_field_and_rejects_unknowns(self):
        good = cv.experiment_for_arm(cv.ARMS[0])
        for key in good:
            with self.subTest(missing=key):
                altered = dict(good)
                del altered[key]
                with self.assertRaises(ValueError):
                    cv.check_experiment(altered)
            with self.subTest(drift=key):
                with self.assertRaises(ValueError):
                    cv.check_experiment(dict(good, **{key: 'unapproved'}))
        for changed in (dict(good, extra=True), dict(good, schema=True), {}, [], 'patched-local-variant'):
            with self.assertRaises(ValueError):
                cv.check_experiment(changed)

    def test_paths_reject_traversal_aliases_and_case_directory_collisions(self):
        for names in (['../escape'], ['/absolute'], ['a//b'], ['a/./b'], ['a/../b'], ['a\\b'],
                      ['a:b'], ['.git/config'], ['a\nb'], ['a', 'a'], ['a', 'a/file'],
                      ['Core/a', 'core/b'], ['a/CAFÉ', 'a/CAFE\u0301']):
            with self.subTest(names=names), self.assertRaises(ValueError):
                cv._check_names(names)

    def test_duplicate_json_fields_are_rejected(self):
        with self.assertRaises(ValueError):
            cv._json(b'{"schema":1,"schema":1}')


class MiniatureGitMaterializationTests(unittest.TestCase):
    """Monkeypatch frozen constants solely inside clearly synthetic unit fixtures."""
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='cleanup-python-fixture-')
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name).resolve()
        self.repo = self.root / 'fixture.git'
        self.repo.mkdir()
        self._git('init', '--bare', '--template=')
        self.before = (b'// PYTHON-ONLY GIT FIXTURE; NOT UNITY EVIDENCE\n'
                       b'void Fixture()\n{\n    if (needsRemove)\n'
                       b'        EntityManager.RemoveComponent(context->addQuery, t);\n}\n')
        self.after = self.before.replace(cv.OLD, cv.NEW)
        self.blobs = {cv.TARGET: self.before, 'LICENSE.md': b'fixture license bytes\r\n',
                      'THIRD PARTY NOTICES.md': b'fixture notices\x00\xff\r\n',
                      'binary.dat': bytes(range(256)), '.gitattributes': b'*.dat filter=forbidden\n*.cs text eol=crlf\n',
                      'tool.sh': b'#!/bin/sh\necho never executed\n',
                      'package.json': b'{"name":"com.latios.latiosframework","version":"0.11.5"}\n'}
        files = []
        for name, data in sorted(self.blobs.items()):
            oid = self._git('hash-object', '-w', '--stdin', data=data).decode().strip()
            files.append(dict(path=name, mode='100755' if name == 'tool.sh' else '100644', size=len(data),
                              git_blob=oid, sha256=cv.sha256(data)))
        tree = self._make_tree(files)
        self.assertEqual(cv._tree_sha(files), tree)
        commit = self._git('-c', 'user.name=Python fixture', '-c', 'user.email=fixture@example.invalid',
                           'commit-tree', tree, data=b'TEST FIXTURE ONLY\n').decode().strip()
        self.source = self.root / 'reviewed/Latios2022Lab'
        (self.source / 'Variants').mkdir(parents=True)
        (self.source / 'Packages').mkdir()
        self.owned = {'Assets/Latios2022Tests/Editor/CoreTests.cs': b'// Python fixture only\n',
                      'Assets/Latios2022Tests/Editor/CoreTests.cs.meta': b'fixture metadata\n',
                      'Assets/Latios2022Tests/Editor/Fixture.asmdef': b'{"name":"PythonFixtureOnly"}\n',
                      'Assets/Latios2022Tests/Editor/Fixture.asmdef.meta': b'fixture assembly metadata\n'}
        for name, data in self.owned.items():
            path = self.source / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
        self.baseline = {'dependencies': {
            cv.PACKAGE_NAME: {'version': cv.UPSTREAM_URL + '#' + commit, 'source': 'git', 'hash': commit,
                              'depth': 0, 'dependencies': {'fixture.dependency': '1.0.0'}},
            'fixture.dependency': {'version': '1.0.0', 'source': 'registry', 'depth': 0,
                                   'url': 'https://fixture.invalid', 'dependencies': {}}}}
        lock_path = self.source / cv.BASELINE_LOCK
        lock_path.parent.mkdir(parents=True)
        lock_path.write_bytes(cv.json_bytes(self.baseline))
        manifest = {'dependencies': {cv.PACKAGE_NAME: cv.UPSTREAM_URL + '#' + commit,
                                     'fixture.dependency': '1.0.0'}}
        (self.source / 'Packages/manifest.json').write_bytes(cv.json_bytes(manifest))
        inventory = dict(schema=1, upstream_commit=commit, upstream_tree=tree, files=files,
                         file_count=len(files), total_bytes=sum(row['size'] for row in files))
        raw = cv.json_bytes(inventory)
        patch = ''.join(difflib.unified_diff(self.before.decode().splitlines(keepends=True),
                                           self.after.decode().splitlines(keepends=True),
                                           fromfile='a/' + cv.TARGET, tofile='b/' + cv.TARGET, n=3)).encode()
        patched = mock.patch.multiple(cv, PROJECT=self.source, UPSTREAM_COMMIT=commit, UPSTREAM_TREE=tree,
                                      SOURCE_INVENTORY_SHA256=cv.sha256(raw), PATCH_SHA256=cv.sha256(patch),
                                      BEFORE_SHA256=cv.sha256(self.before), AFTER_SHA256=cv.sha256(self.after),
                                      AFTER_GIT_BLOB=cv.git_blob(self.after), FILE_COUNT=len(files),
                                      TOTAL_BYTES=sum(row['size'] for row in files),
                                      FROZEN_OWNED_INPUTS={name: cv.sha256(data) for name, data in self.owned.items()},
                                      SOURCE_MANIFEST_SHA256=cv.sha256(cv.json_bytes(manifest)),
                                      BASELINE_LOCK_SHA256=cv.sha256(cv.json_bytes(self.baseline)))
        patched.start()
        self.addCleanup(patched.stop)
        self.expected = inventory
        (self.source / 'Variants/cleanup-query-v1.source-inventory.json').write_bytes(raw)
        (self.source / 'Variants/cleanup-query-v1.patch').write_bytes(patch)
        (self.source / 'Variants/cleanup-query-v1.json').write_bytes(cv.json_bytes(cv._spec()))
        self.fetch = mock.patch.object(cv, '_fetch_upstream', return_value=self.repo)
        self.fetch.start()
        self.addCleanup(self.fetch.stop)
        self.workspace = self.root / 'run'
        self.project = self.workspace / 'source/Latios2022Lab'
        shutil.copytree(self.source, self.project)
        (self.workspace / 'Artifacts').mkdir()

    def _git(self, *args, data=None):
        return subprocess.check_output(['git', '-c', 'core.hooksPath=/dev/null', '-C', str(self.repo), *args],
                                       input=data, stderr=subprocess.PIPE)

    def _make_tree(self, files):
        root = {}
        for row in files:
            node = root
            parts = row['path'].split('/')
            for name in parts[:-1]:
                node = node.setdefault(name, {})
            node[parts[-1]] = row
        def write(node):
            records = []
            for name, entry in sorted(node.items()):
                if 'git_blob' in entry:
                    records.append((entry['mode'] + ' blob ' + entry['git_blob'] + '\t' + name).encode() + b'\0')
                else:
                    records.append(('040000 tree ' + write(entry) + '\t' + name).encode() + b'\0')
            return self._git('mktree', '-z', data=b''.join(records)).decode().strip()
        return write(root)

    def prepare(self, arm='patched-local-variant'):
        return cv.prepare(self.workspace, self.project, cv.experiment_for_arm(arm), 'a' * 40)

    def local_lock_fixture(self, record):
        # Deliberately synthetic Python object, not written as claimed native evidence.
        result = copy.deepcopy(self.baseline)
        result['dependencies'][cv.PACKAGE_NAME] = dict(version=record['manifest_url'], source='local', depth=0,
                                                     dependencies={'fixture.dependency': '1.0.0'})
        return result

    def registered_fixture(self, record):
        return [{'name': cv.PACKAGE_NAME, 'version': '0.11.5', 'source': 'Local',
                 'resolvedPath': record['package_path'], 'packageId': cv.PACKAGE_NAME + '@' + record['manifest_url']}]

    def test_control_materializes_literal_blobs_and_modes_without_cache(self):
        record = self.prepare('unpatched-local-control')
        report = cv.verify(self.project, record)
        self.assertEqual(report['changed_paths'], [])
        self.assertIsNone(report['applied_patch_sha256'])
        self.assertEqual(report['source_files'], report['package_files'])
        for root in (Path(record['pristine_path']), Path(record['package_path'])):
            for name, data in self.blobs.items():
                self.assertEqual((root / name).read_bytes(), data)
            self.assertEqual(stat.S_IMODE((root / 'tool.sh').stat().st_mode), 0o755)
        self.assertFalse((self.project / 'Packages/packages-lock.json').exists())
        self.assertEqual(json.loads((self.workspace / 'experiment-record.json').read_text()), record)

    def test_variant_has_exact_single_diff_and_retains_source_licenses_and_binary(self):
        source_manifest = (self.source / 'Packages/manifest.json').read_bytes()
        record = self.prepare()
        report = cv.verify(self.project, record)
        self.assertEqual(report['changed_paths'], [cv.TARGET])
        self.assertEqual(report['applied_patch_sha256'], cv.PATCH_SHA256)
        self.assertEqual((Path(record['pristine_path']) / cv.TARGET).read_bytes(), self.before)
        self.assertEqual((Path(record['package_path']) / cv.TARGET).read_bytes(), self.after)
        for name in ('LICENSE.md', 'THIRD PARTY NOTICES.md', 'binary.dat', 'package.json'):
            self.assertEqual((Path(record['package_path']) / name).read_bytes(), self.blobs[name])
        self.assertEqual((self.source / 'Packages/manifest.json').read_bytes(), source_manifest)
        self.assertEqual(cv.file_url(record), 'file:' + str(self.workspace / 'variant-packages' / cv.PACKAGE_NAME))
        changed = (self.project / 'Packages/manifest.json').read_bytes()
        self.assertEqual(changed.replace(json.dumps(record['manifest_url']).encode(),
                                         json.dumps(cv.UPSTREAM_URL + '#' + cv.UPSTREAM_COMMIT).encode()), source_manifest)

    def test_prepare_rejects_fixture_tamper_before_git_materialization(self):
        path = self.project / next(iter(self.owned))
        path.write_bytes(b'// changed oracle\n')
        with self.assertRaisesRegex(ValueError, 'oracle'):
            self.prepare()
        cv._fetch_upstream.assert_not_called()

    def test_prepare_rejects_extra_fixture_and_asmdef(self):
        (self.project / 'Assets/Extra.cs').write_bytes(b'// extra\n')
        with self.assertRaisesRegex(ValueError, 'oracle file set'):
            self.prepare()

    def test_prepare_rejects_existing_lock_cache_and_source_reuse(self):
        for name in ('Library', 'Temp', '.upm-cache', '.launch-lock', 'Packages/packages-lock.json'):
            with self.subTest(name=name):
                path = self.project / name
                path.write_bytes(b'not a native result')
                with self.assertRaisesRegex(ValueError, 'fresh'):
                    self.prepare()
                path.unlink()
        self.prepare()
        with self.assertRaisesRegex(ValueError, 'fresh'):
            self.prepare()

    def test_prepare_rejects_modified_manifest_spec_patch_inventory_or_lock_anchor(self):
        for name in ('Packages/manifest.json', 'Variants/cleanup-query-v1.json',
                     'Variants/cleanup-query-v1.patch', 'Variants/cleanup-query-v1.source-inventory.json', cv.BASELINE_LOCK):
            path = self.project / name
            original = path.read_bytes()
            with self.subTest(name=name):
                path.write_bytes(original + b' ')
                # Spec JSON whitespace is immaterial; meaningful field corruption is not.
                if name.endswith('cleanup-query-v1.json'):
                    bad = json.loads(original)
                    bad['upstream_commit'] = 'b' * 40
                    path.write_bytes(cv.json_bytes(bad))
                with self.assertRaises(ValueError):
                    self.prepare()
                path.write_bytes(original)

    def test_second_change_deleted_added_meta_mode_and_pristine_drift_fail(self):
        record = self.prepare()
        package = Path(record['package_path'])
        original = (package / 'LICENSE.md').read_bytes()
        (package / 'LICENSE.md').write_bytes(b'another change')
        with self.assertRaisesRegex(ValueError, 'inventory drifted'):
            cv.verify(self.project, record)
        (package / 'LICENSE.md').write_bytes(original)
        (package / 'new.meta').write_bytes(b'Unity-generated extra file')
        with self.assertRaisesRegex(ValueError, 'inventory drifted'):
            cv.verify(self.project, record)
        (package / 'new.meta').unlink()
        (package / 'LICENSE.md').unlink()
        with self.assertRaisesRegex(ValueError, 'inventory drifted'):
            cv.verify(self.project, record)
        (package / 'LICENSE.md').write_bytes(original)
        (package / 'LICENSE.md').chmod(0o755)
        with self.assertRaisesRegex(ValueError, 'inventory drifted'):
            cv.verify(self.project, record)
        (package / 'LICENSE.md').chmod(0o644)
        (Path(record['pristine_path']) / 'LICENSE.md').write_bytes(b'pristine tamper')
        with self.assertRaisesRegex(ValueError, 'Pristine'):
            cv.verify(self.project, record)

    def test_verifier_rejects_link_hardlink_case_collision_and_empty_directory(self):
        record = self.prepare()
        package = Path(record['package_path'])
        outside = self.root / 'outside'
        outside.write_bytes(b'must never follow')
        bad = package / 'bad-link'
        bad.symlink_to(outside)
        with self.assertRaisesRegex(ValueError, 'Symlink'):
            cv.verify(self.project, record)
        bad.unlink()
        bad.hardlink_to(outside)
        with self.assertRaisesRegex(ValueError, 'Hard-linked'):
            cv.verify(self.project, record)
        bad.unlink()
        (package / 'license.MD').write_bytes(b'collision')
        with self.assertRaisesRegex(ValueError, 'collision'):
            cv.verify(self.project, record)
        (package / 'license.MD').unlink()
        (package / 'empty').mkdir()
        with self.assertRaisesRegex(ValueError, 'empty'):
            cv.verify(self.project, record)

    def test_wrong_path_or_record_hash_is_not_accepted(self):
        record = self.prepare()
        for key, value in (('package_path', str(self.root)), ('pristine_path', str(self.root)),
                           ('manifest_url', 'file:' + str(self.root)), ('source_inventory_sha256', 'a' * 64),
                           ('package_inventory_sha256', 'a' * 64), ('baseline_lock_sha256', 'a' * 64)):
            with self.subTest(key=key), self.assertRaises(ValueError):
                cv.verify(self.project, dict(record, **{key: value}))
        with self.assertRaises(ValueError):
            cv.verify(self.source, record)

    def test_real_lock_contract_checks_whole_graph_not_just_versions(self):
        record = self.prepare()
        lock = self.local_lock_fixture(record)
        self.assertEqual(cv.check_lock(lock, record), lock['dependencies'])
        for key, value in (('source', 'git'), ('version', 'file:' + str(self.root)), ('hash', cv.UPSTREAM_COMMIT),
                           ('depth', 1), ('dependencies', {})):
            changed = copy.deepcopy(lock)
            changed['dependencies'][cv.PACKAGE_NAME][key] = value
            with self.subTest(key=key), self.assertRaises(ValueError):
                cv.check_lock(changed, record)
        for key, value in (('version', '2.0.0'), ('source', 'local'), ('depth', 1), ('dependencies', {'extra': '1'})):
            changed = copy.deepcopy(lock)
            changed['dependencies']['fixture.dependency'][key] = value
            with self.subTest(dependency_key=key), self.assertRaises(ValueError):
                cv.check_lock(changed, record)
        changed = copy.deepcopy(lock)
        changed['dependencies']['unreviewed'] = {}
        with self.assertRaises(ValueError):
            cv.check_lock(changed, record)

    def test_package_info_requires_actual_local_path_package_id_and_version(self):
        record = self.prepare()
        rows = self.registered_fixture(record)
        self.assertEqual(cv.check_registered(rows, record), rows[0])
        for key, value in (('source', 'Git'), ('source', 'Embedded'), ('version', '0.11.5-patched'),
                           ('resolvedPath', str(self.root)), ('packageId', cv.PACKAGE_NAME + '@0.11.5')):
            bad = [dict(rows[0], **{key: value})]
            with self.subTest(key=key, value=value), self.assertRaises(ValueError):
                cv.check_registered(bad, record)
        with self.assertRaises(ValueError):
            cv.check_registered(rows * 2, record)

    def test_capture_freezes_full_original_and_variant_with_no_overwrite(self):
        record = self.prepare()
        report = cv.capture(self.workspace, self.workspace / 'Artifacts', 'before-native', record)
        self.assertTrue(report['valid'])
        folder = self.workspace / 'Artifacts/cleanup-variant/before-native'
        for name, target_bytes in (('source.tar', self.before), ('package.tar', self.after)):
            with tarfile.open(folder / name) as archive:
                self.assertEqual(set(archive.getnames()), set(self.blobs))
                self.assertEqual(archive.extractfile(cv.TARGET).read(), target_bytes)
                self.assertEqual(archive.getmember('tool.sh').mode, 0o755)
        self.assertEqual((folder / 'actual-target.diff').read_bytes(),
                         (self.source / 'Variants/cleanup-query-v1.patch').read_bytes())
        differences = json.loads((folder / 'file-differences.json').read_text())
        self.assertEqual([row['path'] for row in differences], [cv.TARGET])
        with self.assertRaises(FileExistsError):
            cv.capture(self.workspace, self.workspace / 'Artifacts', 'before-native', record)
        with self.assertRaises(ValueError):
            cv.capture(self.root, self.workspace / 'Artifacts', 'other', record)

    def test_capture_preserves_changed_bytes_and_reports_symlink_without_following(self):
        record = self.prepare()
        package = Path(record['package_path'])
        (package / 'LICENSE.md').write_bytes(b'changed license')
        (package / 'unsafe').symlink_to(self.root / 'outside-secret')
        report = cv.capture(self.workspace, self.workspace / 'Artifacts', 'failed', record)
        self.assertFalse(report['valid'])
        self.assertTrue(report['errors'])
        folder = self.workspace / 'Artifacts/cleanup-variant/failed'
        with tarfile.open(folder / 'package.tar') as archive:
            self.assertNotIn('unsafe', archive.getnames())
            self.assertEqual(archive.extractfile('LICENSE.md').read(), b'changed license')

    def test_capture_reports_package_root_symlink_without_following(self):
        record = self.prepare()
        package = Path(record['package_path'])
        saved = self.root / 'saved-package'
        package.rename(saved)
        package.symlink_to(saved, target_is_directory=True)
        report = cv.capture(self.workspace, self.workspace / 'Artifacts', 'root-symlink', record)
        self.assertFalse(report['valid'])
        self.assertFalse((self.workspace / 'Artifacts/cleanup-variant/root-symlink/package.tar').exists())

    def test_manifest_other_dependency_and_original_snapshot_drift_fail(self):
        record = self.prepare()
        path = self.project / 'Packages/manifest.json'
        original = path.read_bytes()
        for key, value in (('fixture.dependency', '2.0.0'), ('new.package', '1.0.0')):
            modified = json.loads(original)
            modified['dependencies'][key] = value
            path.write_bytes(cv.json_bytes(modified))
            with self.subTest(key=key), self.assertRaisesRegex(ValueError, 'manifest'):
                cv.verify(self.project, record)
            report = cv.capture(self.workspace, self.workspace / 'Artifacts', key, record)
            self.assertFalse(report['valid'])
        path.write_bytes(original)
        with self.assertRaisesRegex(ValueError, 'Prepared manifest hash'):
            cv.verify(self.project, dict(record, manifest_after_sha256='a' * 64))
        (self.workspace / 'source-manifest.json').write_bytes(b'original manifest tampered')
        with self.assertRaisesRegex(ValueError, 'Original authored manifest'):
            cv.verify(self.project, record)

    def test_capture_bounds_large_files_and_reports_omission(self):
        record = self.prepare()
        extra = Path(record['package_path']) / 'oversized.bin'
        extra.write_bytes(b'x' * 1025)
        with mock.patch.object(cv, 'MAX_FILE_BYTES', 1024):
            report = cv.capture(self.workspace, self.workspace / 'Artifacts', 'oversized', record)
        self.assertFalse(report['valid'])
        self.assertTrue(any('limit' in error for error in report['errors']))
        with tarfile.open(self.workspace / 'Artifacts/cleanup-variant/oversized/package.tar') as archive:
            self.assertNotIn('oversized.bin', archive.getnames())

    def test_capture_bounds_entry_and_total_byte_counts(self):
        record = self.prepare()
        for field, limit in (('MAX_ENTRIES', 1), ('MAX_FILES', 1), ('MAX_CAPTURE_BYTES', 80)):
            with self.subTest(field=field), mock.patch.object(cv, field, limit):
                report = cv.capture(self.workspace, self.workspace / 'Artifacts', field, record)
                self.assertFalse(report['valid'])
                self.assertTrue(report['errors'])

    def test_real_git_symlink_and_submodule_modes_are_rejected(self):
        link_oid = self._git('hash-object', '-w', '--stdin', data=b'../outside').decode().strip()
        for mode, kind, oid in (('120000', 'blob', link_oid), ('160000', 'commit', cv.UPSTREAM_COMMIT)):
            tree = self._git('mktree', data=(mode + ' ' + kind + ' ' + oid + '\tescape\n').encode()).decode().strip()
            commit = self._git('-c', 'user.name=Python fixture', '-c', 'user.email=fixture@example.invalid',
                               'commit-tree', tree, data=b'UNSAFE PYTHON FIXTURE ONLY\n').decode().strip()
            destination = self.root / ('rejected-' + mode)
            with self.subTest(mode=mode), mock.patch.object(cv, 'UPSTREAM_COMMIT', commit):
                with self.assertRaisesRegex(ValueError, 'Symlink, submodule'):
                    cv._materialize(self.repo, destination, self.expected)
            self.assertFalse(destination.exists())

    def test_materialization_checks_frozen_sha256_not_only_git_path_and_blob(self):
        wrong = copy.deepcopy(self.expected)
        wrong['files'][0]['sha256'] = 'a' * 64
        with self.assertRaisesRegex(ValueError, 'Fetched Git blob differs'):
            cv._materialize(self.repo, self.root / 'wrong-sha256', wrong)

    def test_capture_rechecks_archive_bytes_after_initial_verification(self):
        record = self.prepare()
        real_capture = cv._capture_tree
        def changed_after_verify(root, destination):
            if root == Path(record['package_path']):
                (root / 'LICENSE.md').write_bytes(b'changed during evidence capture')
            return real_capture(root, destination)
        with mock.patch.object(cv, '_capture_tree', side_effect=changed_after_verify):
            report = cv.capture(self.workspace, self.workspace / 'Artifacts', 'concurrent-drift', record)
        self.assertFalse(report['valid'])
        self.assertTrue(any('during capture' in error for error in report['errors']))

    def test_materialization_rejects_symlink_git_tree_before_writing(self):
        self._git('config', 'core.autocrlf', 'true')
        self._git('config', 'filter.forbidden.smudge', 'exit 97')
        # Even hostile local filter settings do not run through cat-file.
        cv._materialize(self.repo, self.root / 'literal-source', self.expected)
        self.assertEqual((self.root / 'literal-source' / cv.TARGET).read_bytes(), self.before)
        real_git = cv._git
        with mock.patch.object(cv, '_git', side_effect=lambda repo, *args:
                               b'120000 blob ' + b'a' * 40 + b'\tlink\0'
                               if args[0] == 'ls-tree' else real_git(repo, *args)):
            with self.assertRaisesRegex(ValueError, 'Symlink'):
                cv._materialize(self.repo, self.root / 'rejected', self.expected)
        self.assertFalse((self.root / 'rejected').exists())


if __name__ == '__main__':
    unittest.main()
