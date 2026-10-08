#!/usr/bin/env python3
"""Synthetic API/ZIP tests. No live GitHub or blob requests are made."""

import copy
import hashlib
import io
import json
from pathlib import Path
import stat
import struct
import tempfile
import unittest
from unittest import mock
import warnings
import zipfile

import evidence_parts
import latios_followup_artifacts as artifacts


REPOSITORY = 'example/SPGameFoundation'
HEAD = 'a' * 40
RUN = 456
ATTEMPT = 2
BASE = artifacts.API_ROOT + '/repos/' + REPOSITORY + '/actions'
SIGNED_URL = 'https://productionresultssa1.blob.core.windows.net/actions-results/item?sig=private-signature'


def sha(content):
    return hashlib.sha256(content).hexdigest()


def zip_bytes(entries):
    result = io.BytesIO()
    with warnings.catch_warnings():
        warnings.simplefilter('ignore', UserWarning)
        with zipfile.ZipFile(result, 'w', compression=zipfile.ZIP_STORED) as zipped:
            for name, value in entries:
                zipped.writestr(name, value)
    return result.getvalue()


class Fixture:
    def __init__(self):
        self.files = {'report.json': b'{"result":"passed"}\n',
                      'native/profiler.bin': hashlib.shake_256(b'synthetic').digest(2400)}
        self.producer = json.dumps({name: sha(value) for name, value in self.files.items()},
                                   sort_keys=True).encode()
        self.files[artifacts.PRODUCER_MANIFEST] = self.producer
        self.manifest = {'version': 1, 'part_size_bytes': evidence_parts.PART_BYTES,
                         'max_parts': evidence_parts.MAX_PARTS,
                         'files': [{'path': 'Artifacts/' + name, 'size': len(value), 'sha256': sha(value)}
                                   for name, value in self.files.items()]}
        self.replace_archive(zip_bytes(('Artifacts/' + name, value) for name, value in self.files.items()))
        self.run = {'id': RUN, 'run_attempt': ATTEMPT, 'head_sha': HEAD,
                    'path': '.github/workflows/latios-lab.yml', 'status': 'completed', 'conclusion': 'success',
                    'repository': {'full_name': REPOSITORY}, 'head_repository': {'full_name': REPOSITORY}}
        self.fetch_calls = []
        self.download_calls = []
        self.build()

    def replace_archive(self, archive):
        self.archive = archive
        self.part_data = [archive[index:index + evidence_parts.PART_BYTES]
                          for index in range(0, len(archive), evidence_parts.PART_BYTES)]
        self.manifest['archive'] = {'name': 'evidence.zip', 'size': len(archive), 'sha256': sha(archive)}
        self.manifest['parts'] = [{'name': 'evidence.zip.part{:02d}'.format(index),
                                   'size': len(value), 'sha256': sha(value)}
                                  for index, value in enumerate(self.part_data)]

    def build(self, wrapper_change=None, encoded_change=None):
        encoded = json.dumps(self.manifest, sort_keys=True).encode()
        self.wrappers = {}
        self.metadata = {}
        reviewed = []
        for index, value in enumerate(self.part_data):
            artifact_id = 1000 + index
            entries = [('evidence-manifest.json', encoded_change(index, encoded) if encoded_change else encoded),
                       ('evidence.zip.part{:02d}'.format(index), value)]
            if wrapper_change:
                entries = wrapper_change(index, entries)
            wrapper = zip_bytes(entries)
            self.wrappers[artifact_id] = wrapper
            reviewed.append({'id': artifact_id, 'name': 'latios-s1a-456-2-patched-local-variant-part{:02d}'.format(index),
                             'size_in_bytes': len(wrapper), 'sha256': sha(wrapper)})
            self.metadata[artifact_id] = dict(reviewed[-1], digest='sha256:' + sha(wrapper), expired=False,
                                              workflow_run={'id': RUN, 'head_sha': HEAD})
        self.spec = {'run_id': RUN, 'attempt': ATTEMPT, 'head_sha': HEAD,
                     'run_url': 'https://github.com/{}/actions/runs/456/attempts/2'.format(REPOSITORY),
                     'workflow': '.github/workflows/latios-lab.yml', 'artifacts': reviewed,
                     'parts_manifest_sha256': sha(encoded), 'archive_sha256': sha(self.archive),
                     'archive_size': len(self.archive), 'evidence_manifest_sha256': sha(self.producer)}

    def fetch(self, url):
        self.fetch_calls.append(url)
        if url == BASE + '/runs/456/attempts/2':
            return copy.deepcopy(self.run)
        return copy.deepcopy(self.metadata[int(url.rsplit('/', 1)[1])])

    def download(self, url, output, expected_size):
        self.download_calls.append((url, expected_size))
        output.write_bytes(self.wrappers[int(url.split('/')[-2])])


class ArtifactRestorationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        # Small synthetic slices exercise every part without allocating 16 MiB.
        self.part_patch = mock.patch.object(evidence_parts, 'PART_BYTES', 1024)
        self.part_patch.start()
        self.addCleanup(self.part_patch.stop)
        self.fixture = Fixture()
        self.output = self.root / 'downloaded'
        # Any accidental default network access fails the test immediately.
        self.network = mock.patch.object(artifacts.urllib.request.OpenerDirector, 'open',
                                         side_effect=AssertionError('Live network is forbidden'))
        self.network.start()
        self.addCleanup(self.network.stop)

    def restore(self):
        return artifacts.restore_artifacts(REPOSITORY, self.fixture.spec, self.output,
                                           fetch=self.fixture.fetch, downloader=self.fixture.download)

    def rejected(self, pattern=None, before_restore=True):
        with mock.patch.object(evidence_parts, 'restore_evidence', wraps=evidence_parts.restore_evidence) as restore:
            with self.assertRaisesRegex((ValueError, zipfile.BadZipFile), pattern or '.'):
                self.restore()
            if before_restore:
                restore.assert_not_called()
        self.assertFalse(self.output.exists())
        self.assertEqual([], list(self.root.iterdir()))

    def test_restores_exact_files_and_keeps_wrappers_and_safe_receipts(self):
        with mock.patch.object(evidence_parts, 'restore_evidence', wraps=evidence_parts.restore_evidence) as restore:
            result = self.restore()
            restore.assert_called_once()
        self.assertEqual(self.output.resolve() / 'restored/Artifacts', result)
        self.assertEqual(self.fixture.files, {path.relative_to(result).as_posix(): path.read_bytes()
                                             for path in result.rglob('*') if path.is_file()})
        self.assertEqual(len(self.fixture.part_data), len(list((self.output / 'wrappers').glob('*.zip'))))
        receipt = json.loads((self.output / 'receipts.json').read_text())
        self.assertEqual(self.fixture.spec['artifacts'], receipt['artifacts'])
        self.assertNotIn('sig=', (self.output / 'receipts.json').read_text())
        self.assertEqual(BASE + '/runs/456/attempts/2', self.fixture.fetch_calls[0])

    def test_accepts_real_packager_with_zip64_local_headers(self):
        source = self.root / 'Artifacts'
        source.mkdir()
        for name, value in self.fixture.files.items():
            path = source / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(value)
        packaged = self.root / 'packaged'
        manifest = evidence_parts.package_evidence(source, packaged)
        self.fixture.manifest = manifest
        self.fixture.replace_archive(b''.join((packaged / 'part{:02d}'.format(index) / entry['name']).read_bytes()
                                             for index, entry in enumerate(manifest['parts'])))
        self.fixture.build()
        self.assertEqual(self.fixture.producer, (self.restore() / artifacts.PRODUCER_MANIFEST).read_bytes())

    def test_invalid_specs_fail_before_requests(self):
        cases = [lambda spec: spec.update(extra='unreviewed'),
                 lambda spec: spec.update(run_id=True), lambda spec: spec.update(attempt=0),
                 lambda spec: spec.update(head_sha='a' * 39),
                 lambda spec: spec.update(run_url='https://evil.test/download'),
                 lambda spec: spec.update(workflow='.github/workflows/unity-self-hosted.yml'),
                 lambda spec: spec.update(workflow=[]),
                 lambda spec: spec.update(archive_size=artifacts.MAX_ARCHIVE_BYTES + 1),
                 lambda spec: spec.update(archive_size=True),
                 lambda spec: spec.update(parts_manifest_sha256='z' * 64),
                 lambda spec: spec.update(artifacts=[]),
                 lambda spec: spec['artifacts'][0].update(id=0),
                 lambda spec: spec['artifacts'][0].update(url='https://evil.test'),
                 lambda spec: spec['artifacts'][0].update(name='latios-early-review'),
                 lambda spec: spec['artifacts'][0].update(name='latios-s1a-456-1-patched-local-variant-part00'),
                 lambda spec: spec['artifacts'][1].update(name='another-part01'),
                 lambda spec: spec['artifacts'][1].update(id=1000),
                 lambda spec: spec['artifacts'][0].update(size_in_bytes=artifacts.MAX_WRAPPER_BYTES + 1),
                 lambda spec: spec['artifacts'].pop(),
                 lambda spec: spec['artifacts'].reverse()]
        for mutate in cases:
            with self.subTest(mutate=mutate):
                self.fixture = Fixture()
                mutate(self.fixture.spec)
                self.rejected()
                self.assertEqual([], self.fixture.fetch_calls)

    def test_wrong_official_run_rejected_before_download(self):
        changes = [{'id': RUN + 1}, {'run_attempt': 1}, {'head_sha': 'b' * 40},
                   {'status': 'in_progress'}, {'conclusion': 'failure'},
                   {'path': '.github/workflows/other.yml'}, {'repository': {'full_name': 'other/repo'}},
                   {'head_repository': {'full_name': 'fork/repo'}}]
        for changeset in changes:
            with self.subTest(changes=changeset):
                self.fixture = Fixture()
                self.fixture.run.update(changeset)
                self.rejected('run|repository')
                self.assertEqual([], self.fixture.download_calls)

    def test_wrong_official_artifact_rejected_before_any_download(self):
        changes = [{'id': 999}, {'name': 'early-review'}, {'size_in_bytes': 1},
                   {'digest': None}, {'digest': 'sha256:' + '0' * 64}, {'expired': True}, {'expired': 0},
                   {'workflow_run': {'id': 999, 'head_sha': HEAD}},
                   {'workflow_run': {'id': RUN, 'head_sha': 'b' * 40}}]
        for changeset in changes:
            with self.subTest(changes=changeset):
                self.fixture = Fixture()
                self.fixture.metadata[1000 + len(self.fixture.part_data) - 1].update(changeset)
                self.rejected('artifact|Artifact')
                self.assertEqual([], self.fixture.download_calls)

    def test_corrupt_download_and_wrong_api_reviewed_digest(self):
        self.fixture.wrappers[1000] += b'extra'
        self.rejected('wrapper size or digest')
        self.fixture = Fixture()
        self.fixture.spec['artifacts'][0]['sha256'] = '0' * 64
        self.fixture.metadata[1000]['digest'] = 'sha256:' + '0' * 64
        self.rejected('wrapper size or digest')

    def test_followup_workflow_uses_followup_run_attempt_names(self):
        self.fixture.run['path'] = '.github/workflows/latios-followup.yml'
        self.fixture.spec['workflow'] = self.fixture.run['path']
        for reviewed in self.fixture.spec['artifacts']:
            reviewed['name'] = reviewed['name'].replace('latios-s1a-', 'latios-followup-')
            self.fixture.metadata[reviewed['id']]['name'] = reviewed['name']
        self.assertTrue(self.restore().is_dir())

    def test_corrupt_parts_and_archive_hash_are_checked_before_restore(self):
        self.fixture.part_data[0] = b'!' + self.fixture.part_data[0][1:]
        self.fixture.build()
        self.rejected('part size or digest')
        self.fixture = Fixture()
        self.fixture.manifest['archive']['sha256'] = '0' * 64
        self.fixture.build()
        self.fixture.spec['archive_sha256'] = '0' * 64
        self.rejected('archive size or digest')

    def test_wrapper_requires_exact_unique_root_names(self):
        changes = [lambda entries: [(entries[0][0], entries[0][1]), ('../escape', b'bad')],
                   lambda entries: entries + [('extra.txt', b'bad')],
                   lambda entries: [entries[0], entries[0]],
                   lambda entries: [entries[1]],
                   lambda entries: [entries[0], ('nested/' + entries[1][0], entries[1][1])],
                   lambda entries: [entries[0], ('evidence.zip.part31', entries[1][1])]]
        for change in changes:
            with self.subTest(change=change):
                self.fixture = Fixture()
                self.fixture.build(wrapper_change=lambda index, entries: change(entries) if index == 0 else entries)
                self.rejected('Wrapper|directory')

    def test_wrapper_links_nonregular_members_and_oversized_manifest(self):
        for mode in (stat.S_IFLNK, stat.S_IFIFO, stat.S_IFDIR):
            with self.subTest(mode=mode):
                self.fixture = Fixture()
                def change(index, entries):
                    info = zipfile.ZipInfo(entries[1][0])
                    info.external_attr = (mode | 0o644) << 16
                    return [entries[0], (info, entries[1][1])] if index == 0 else entries
                self.fixture.build(wrapper_change=change)
                self.rejected('regular files')
        self.fixture = Fixture()
        self.fixture.build(encoded_change=lambda index, encoded: b'x' * (evidence_parts.MAX_MANIFEST_BYTES + 1))
        self.rejected('member exceeds')

    def test_wrapper_crc_is_checked(self):
        value = bytearray(self.fixture.wrappers[1000])
        with zipfile.ZipFile(io.BytesIO(value)) as zipped:
            member = zipped.infolist()[0]
            header = struct.unpack('<4s5H3L2H', value[member.header_offset:member.header_offset + 30])
            value[member.header_offset + 30 + header[-2] + header[-1]] ^= 1
        self.fixture.wrappers[1000] = bytes(value)
        self.fixture.spec['artifacts'][0]['sha256'] = sha(value)
        self.fixture.metadata[1000]['digest'] = 'sha256:' + sha(value)
        self.rejected('CRC')

    def test_large_central_directory_is_rejected_before_zipfile_parsing(self):
        value = bytearray(self.fixture.wrappers[1000])
        offset = value.rfind(b'PK\x05\x06')
        struct.pack_into('<HH', value, offset + 8, 65535, 65535)
        self.fixture.wrappers[1000] = bytes(value)
        self.fixture.spec['artifacts'][0]['sha256'] = sha(value)
        self.fixture.metadata[1000]['digest'] = 'sha256:' + sha(value)
        with mock.patch.object(artifacts.zipfile, 'ZipFile') as parser:
            self.rejected('directory exceeds')
            parser.assert_not_called()

    def test_parts_manifests_must_match_byte_for_byte_and_reviewed_hash(self):
        self.fixture.build(encoded_change=lambda index, encoded: encoded + (b' ' if index == 1 else b''))
        self.rejected('manifests disagree')
        self.fixture = Fixture()
        self.fixture.spec['parts_manifest_sha256'] = '0' * 64
        self.rejected('manifest digest')

    def test_manifest_requires_complete_bounded_parts(self):
        changes = [lambda manifest: manifest['parts'].pop(),
                   lambda manifest: manifest['parts'][0].update(name='evidence.zip.part01'),
                   lambda manifest: manifest['parts'][0].update(size=0),
                   lambda manifest: manifest['parts'][0].update(size=evidence_parts.PART_BYTES + 1),
                   lambda manifest: manifest['archive'].update(size=artifacts.MAX_ARCHIVE_BYTES + 1),
                   lambda manifest: manifest['archive'].update(sha256='0' * 64),
                   lambda manifest: manifest.update(max_parts=1000),
                   lambda manifest: manifest.update(version=True)]
        for change in changes:
            with self.subTest(change=change):
                self.fixture = Fixture()
                change(self.fixture.manifest)
                self.fixture.build()
                self.rejected()

    def test_manifest_bounds_file_count_and_member_sum_before_restore(self):
        with mock.patch.object(artifacts, 'MAX_FILES', 2):
            self.rejected('count exceeds')
        with mock.patch.object(artifacts, 'MAX_RESTORED_BYTES', 2500):
            self.rejected('member sum')
        self.fixture.manifest['files'][0]['size'] = artifacts.MAX_RESTORED_BYTES + 1
        self.fixture.build()
        self.rejected('file inventory')

    def test_invalid_file_inventory_and_path_collision(self):
        changes = [lambda manifest: manifest['files'].append(manifest['files'][0]),
                   lambda manifest: manifest['files'][0].update(path='Artifacts/../escape'),
                   lambda manifest: manifest['files'][0].update(path='Artifacts/native'),
                   lambda manifest: manifest['files'][0].update(path='Artifacts/\x00hidden'),
                   lambda manifest: manifest['files'][0].update(size=True)]
        for change in changes:
            with self.subTest(change=change):
                self.fixture = Fixture()
                change(self.fixture.manifest)
                self.fixture.build()
                self.rejected()

    def test_inner_archive_requires_regular_unique_exact_members(self):
        normal = [('Artifacts/' + name, value) for name, value in self.fixture.files.items()]
        for variant in ('traversal', 'duplicate', 'extra', 'missing', 'symlink', 'fifo'):
            with self.subTest(variant=variant):
                self.fixture = Fixture()
                entries = list(normal)
                if variant == 'traversal':
                    entries[0] = ('../escape', entries[0][1])
                elif variant == 'duplicate':
                    entries.append(entries[0])
                elif variant == 'extra':
                    entries.append(('Artifacts/extra', b'bad'))
                elif variant == 'missing':
                    entries.pop()
                else:
                    info = zipfile.ZipInfo(entries[0][0])
                    info.external_attr = ((stat.S_IFLNK if variant == 'symlink' else stat.S_IFIFO) | 0o644) << 16
                    entries[0] = (info, entries[0][1])
                self.fixture.replace_archive(zip_bytes(entries))
                self.fixture.build()
                self.rejected('inventory|regular files')

    def test_inner_archive_declared_member_size_is_preflighted(self):
        self.fixture.manifest['files'][0]['size'] += 1
        self.fixture.build()
        self.rejected('Inner member size')

    def test_original_file_hash_still_verified_by_unchanged_restorer(self):
        self.fixture.manifest['files'][0]['sha256'] = '0' * 64
        self.fixture.build()
        self.rejected('file SHA-256 mismatch', before_restore=False)

    def test_wrong_producer_manifest_and_missing_inventory_coverage(self):
        self.fixture.spec['evidence_manifest_sha256'] = '0' * 64
        self.rejected('producer manifest')
        self.fixture = Fixture()
        self.fixture.producer = b'{}'
        self.fixture.files[artifacts.PRODUCER_MANIFEST] = self.fixture.producer
        for entry in self.fixture.manifest['files']:
            if entry['path'] == 'Artifacts/' + artifacts.PRODUCER_MANIFEST:
                entry.update(size=2, sha256=sha(b'{}'))
        self.fixture.replace_archive(zip_bytes(('Artifacts/' + name, value)
                                               for name, value in self.fixture.files.items()))
        self.fixture.build()
        self.rejected('does not exactly cover', before_restore=False)

    def test_duplicate_json_members_are_rejected(self):
        encoded = json.dumps(self.fixture.manifest).encode()
        ambiguous = b'{"version": 1,' + encoded[1:]
        self.fixture.build(encoded_change=lambda index, content: ambiguous)
        self.fixture.spec['parts_manifest_sha256'] = sha(ambiguous)
        self.rejected('ambiguous evidence JSON')

    def test_missing_download_and_sanitized_adapter_exceptions(self):
        self.fixture.download = lambda *args: None
        self.rejected('wrapper size or digest')
        self.fixture = Fixture()
        self.fixture.fetch = mock.Mock(side_effect=RuntimeError(SIGNED_URL + ' TOKEN'))
        self.rejected(r'^Official artifact metadata retrieval failed\.$')
        self.fixture = Fixture()
        self.fixture.download = mock.Mock(side_effect=RuntimeError(SIGNED_URL + ' TOKEN'))
        self.rejected(r'^Official artifact ZIP download failed\.$')


class FakeResponse(io.BytesIO):
    def __init__(self, status, body=b'', headers=None):
        super().__init__(body)
        self.status = status
        self.headers = headers or {}


class NetworkAdapterTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.output = Path(self.temporary.name) / 'artifact.zip'
        environment = mock.patch.dict(artifacts.os.environ, {'GITHUB_TOKEN': 'test-token-only'})
        environment.start()
        self.addCleanup(environment.stop)
        self.endpoint = BASE + '/artifacts/1000/zip'

    def test_redirect_download_strips_authorization(self):
        requests = []
        def open_request(request):
            requests.append(request)
            if len(requests) == 1:
                return FakeResponse(302, headers={'Location': SIGNED_URL})
            return FakeResponse(200, b'ZIP', {'Content-Length': '3'})
        with mock.patch.object(artifacts, '_open', side_effect=open_request):
            artifacts.download_artifact(self.endpoint, self.output, 3)
        self.assertEqual('Bearer test-token-only', requests[0].get_header('Authorization'))
        self.assertEqual(self.endpoint, requests[0].full_url)
        self.assertEqual(SIGNED_URL, requests[1].full_url)
        self.assertIsNone(requests[1].get_header('Authorization'))
        self.assertIsNone(requests[1].get_header('Cookie'))
        self.assertEqual(b'ZIP', self.output.read_bytes())

    def test_unsafe_or_unsigned_redirects_are_rejected_without_blob_fetch(self):
        urls = ['http://productionresultssa1.blob.core.windows.net/a?sig=s',
                'https://evil.test/a?sig=s', 'https://productionresultssa1.blob.core.windows.net.evil.test/a?sig=s',
                'https://attacker.blob.core.windows.net/a?sig=s',
                'https://token@productionresultssa1.blob.core.windows.net/a?sig=s',
                'https://productionresultssa1.blob.core.windows.net:443/a?sig=s',
                'https://productionresultssa1.blob.core.windows.net/a',
                'https://productionresultssa1.blob.core.windows.net/a?sig=s#fragment',
                'https://productionresultssa1.blob.core.windows.net/a?sig=one&sig=two']
        for url in urls:
            with self.subTest(url=url), mock.patch.object(artifacts, '_open',
                    return_value=FakeResponse(302, headers={'Location': url})) as opening:
                with self.assertRaisesRegex(ValueError, r'^Official artifact ZIP download failed\.$'):
                    artifacts.download_artifact(self.endpoint, self.output, 3)
                self.assertEqual(1, opening.call_count)
                self.assertFalse(self.output.exists())

    def test_signed_blob_redirect_is_not_followed(self):
        with mock.patch.object(artifacts, '_open', side_effect=[FakeResponse(302, headers={'Location': SIGNED_URL}),
                FakeResponse(302, headers={'Location': 'https://evil.test'})]) as opening:
            with self.assertRaisesRegex(ValueError, 'download failed'):
                artifacts.download_artifact(self.endpoint, self.output, 3)
            self.assertEqual(2, opening.call_count)
            self.assertFalse(self.output.exists())
        self.assertIsNone(artifacts._NoRedirect().redirect_request(None, None, 302, '', {}, 'https://evil.test'))

    def test_download_size_bound_and_exception_sanitization(self):
        for body in (b'Z', b'ZIP-too-large'):
            with self.subTest(body=body), mock.patch.object(artifacts, '_open', side_effect=[
                    FakeResponse(302, headers={'Location': SIGNED_URL}), FakeResponse(200, body)]):
                if self.output.exists():
                    self.output.unlink()
                with self.assertRaisesRegex(ValueError, r'^Official artifact ZIP download failed\.$'):
                    artifacts.download_artifact(self.endpoint, self.output, 3)
                self.assertLessEqual(self.output.stat().st_size, 3)
        with mock.patch.object(artifacts, '_open', side_effect=RuntimeError(SIGNED_URL + ' test-token-only')):
            with self.assertRaisesRegex(ValueError, r'^Official artifact ZIP download failed\.$'):
                artifacts.download_artifact(self.endpoint, self.output, 3)

    def test_default_api_json_fetch_is_bounded_and_never_redirects(self):
        with mock.patch.object(artifacts, '_open', return_value=FakeResponse(200, b'{"id":123}')):
            self.assertEqual({'id': 123}, artifacts.fetch_json(BASE + '/artifacts/123'))
        for response in (FakeResponse(302, headers={'Location': SIGNED_URL}),
                         FakeResponse(200, b'x' * (artifacts.MAX_API_BYTES + 1))):
            with mock.patch.object(artifacts, '_open', return_value=response):
                with self.assertRaisesRegex(ValueError, r'^Official artifact metadata retrieval failed\.$'):
                    artifacts.fetch_json(BASE + '/artifacts/123')

    def test_api_token_never_sent_to_user_supplied_host(self):
        with mock.patch.object(artifacts, '_open') as opening:
            with self.assertRaises(ValueError):
                artifacts.download_artifact('https://evil.test/artifacts/123/zip', self.output, 3)
            opening.assert_not_called()


if __name__ == '__main__':
    unittest.main()
