#!/usr/bin/env python3
"""Restore reviewed, bounded Latios evidence from official GitHub artifact IDs.

No caller-supplied download URL is accepted. The default network adapters read
GITHUB_TOKEN (the workflow grants actions:read) only for api.github.com. A ZIP
redirect is captured without following it, then downloaded with a separate,
unauthenticated, non-redirecting request to a supported Actions blob host.

Tests may inject fetch(api_url) -> JSON and downloader(api_url, path, size).
Neither adapter's exception text is exposed, because it may contain a signed URL.
The destination is created atomically after verification and retains the wrapper
ZIPs, reviewed hash receipts, parts and the original restored evidence.
"""

import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import stat
import struct
import tempfile
import urllib.error
import urllib.parse
import urllib.request
import zipfile

import evidence_parts


MAX_ARCHIVE_BYTES = 512 * 1024 * 1024
MAX_RESTORED_BYTES = 2 * 1024 * 1024 * 1024
MAX_FILES = 50000
MAX_WRAPPER_BYTES = evidence_parts.PART_BYTES + evidence_parts.MAX_MANIFEST_BYTES + 65536
MAX_API_BYTES = 1024 * 1024
API_ROOT = 'https://api.github.com'
WORKFLOWS = frozenset(('.github/workflows/latios-lab.yml',
                       '.github/workflows/latios-followup.yml'))
SPEC_KEYS = frozenset(('run_id', 'attempt', 'head_sha', 'run_url', 'workflow',
                       'artifacts', 'parts_manifest_sha256', 'archive_sha256',
                       'archive_size', 'evidence_manifest_sha256'))
ARTIFACT_KEYS = frozenset(('id', 'name', 'size_in_bytes', 'sha256'))
PRODUCER_MANIFEST = 'evidence-sha256.json'


def require(condition, message):
    if not condition:
        raise ValueError(message)


def _integer(value, minimum, maximum):
    return type(value) is int and minimum <= value <= maximum


def _sha(value, length=64):
    return isinstance(value, str) and re.fullmatch(r'[0-9a-f]{%d}' % length, value) is not None


def _exact_keys(value, keys):
    return isinstance(value, dict) and set(value) == set(keys)


def validate_spec(repository, spec):
    """Validate every reviewed field before making requests or creating files."""
    require(isinstance(repository, str) and len(repository) <= 200
            and re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9-]*/[A-Za-z0-9_.-]+', repository)
            and repository.split('/')[1] not in ('.', '..'), 'Invalid GitHub repository.')
    require(_exact_keys(spec, SPEC_KEYS), 'Invalid artifact prerequisite fields.')
    require(_integer(spec['run_id'], 1, 2**63 - 1)
            and _integer(spec['attempt'], 1, 2**31 - 1), 'Invalid run or attempt.')
    require(_sha(spec['head_sha'], 40), 'Invalid prerequisite head SHA.')
    require(isinstance(spec['workflow'], str) and spec['workflow'] in WORKFLOWS,
            'Unapproved prerequisite workflow.')
    expected_url = 'https://github.com/{}/actions/runs/{}/attempts/{}'.format(
        repository, spec['run_id'], spec['attempt'])
    require(spec['run_url'] == expected_url, 'Prerequisite URL must identify the exact run attempt.')
    for key in ('parts_manifest_sha256', 'archive_sha256', 'evidence_manifest_sha256'):
        require(_sha(spec[key]), 'Invalid reviewed evidence digest.')
    require(_integer(spec['archive_size'], 1, MAX_ARCHIVE_BYTES), 'Evidence archive exceeds its bound.')
    artifacts = spec['artifacts']
    require(isinstance(artifacts, list) and 1 <= len(artifacts) <= evidence_parts.MAX_PARTS,
            'Invalid reviewed artifact count.')
    identifiers = set()
    prefix = None
    for index, artifact in enumerate(artifacts):
        require(_exact_keys(artifact, ARTIFACT_KEYS), 'Invalid reviewed artifact fields.')
        require(_integer(artifact['id'], 1, 2**63 - 1)
                and artifact['id'] not in identifiers, 'Invalid or duplicate artifact ID.')
        identifiers.add(artifact['id'])
        require(_integer(artifact['size_in_bytes'], 1, MAX_WRAPPER_BYTES)
                and _sha(artifact['sha256']), 'Invalid reviewed wrapper size or digest.')
        name = artifact['name']
        match = re.fullmatch(r'([A-Za-z0-9][A-Za-z0-9_.-]{0,199})-part([0-9]{2})', name) \
            if isinstance(name, str) else None
        require(match is not None and int(match[2]) == index,
                'Reviewed artifacts must be complete and ordered from part00; no early or extra artifacts.')
        if prefix is None:
            prefix = match[1]
        require(match[1] == prefix, 'Reviewed artifacts must have one evidence prefix.')
        producer = 'latios-s1a' if spec['workflow'] == '.github/workflows/latios-lab.yml' else 'latios-followup'
        require(match[1].startswith('{}-{}-{}-'.format(producer, spec['run_id'], spec['attempt'])),
                'Artifact name does not bind the reviewed workflow run attempt.')
    require(len(artifacts) == (spec['archive_size'] + evidence_parts.PART_BYTES - 1)
            // evidence_parts.PART_BYTES, 'Reviewed artifact count does not match archive bounds.')
    return spec


def _unique_object(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, 'Duplicate JSON member.')
        result[key] = value
    return result


def _json(encoded):
    try:
        return json.loads(encoded, object_pairs_hook=_unique_object,
                          parse_constant=lambda _: (_ for _ in ()).throw(ValueError('Nonfinite JSON value.')))
    except (UnicodeError, ValueError, RecursionError):
        raise ValueError('Invalid or ambiguous evidence JSON.') from None


class _NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, response, code, message, headers, new_url):
        return None


def _open(request):
    # A fresh opener has no cookie or credential store. Never follow redirects,
    # including a second redirect from the signed blob destination.
    opener = urllib.request.build_opener(_NoRedirect())
    try:
        return opener.open(request, timeout=60)
    except urllib.error.HTTPError as error:
        if error.code in (301, 302, 303, 307, 308):
            return error
        error.close()
        raise ValueError('Official artifact request failed.') from None
    except Exception:
        raise ValueError('Official artifact request failed.') from None


def _api_request(url):
    parsed = urllib.parse.urlsplit(url)
    require(parsed.scheme == 'https' and parsed.netloc == 'api.github.com'
            and not parsed.query and not parsed.fragment
            and re.fullmatch(r'/repos/[A-Za-z0-9-]+/[A-Za-z0-9_.-]+/actions/'
                             r'(runs/[0-9]+/attempts/[0-9]+|artifacts/[0-9]+(?:/zip)?)', parsed.path),
            'Only official artifact API endpoints are allowed.')
    token = os.environ.get('GITHUB_TOKEN', '')
    require(token and not any(character.isspace() for character in token),
            'GITHUB_TOKEN with actions:read is required.')
    return urllib.request.Request(url, headers={
        'Authorization': 'Bearer ' + token, 'Accept': 'application/vnd.github+json',
        'X-GitHub-Api-Version': '2022-11-28', 'User-Agent': 'SPGameFoundation-evidence'})


def fetch_json(url):
    try:
        with _open(_api_request(url)) as response:
            require(response.status == 200, 'Official metadata request did not succeed.')
            encoded = response.read(MAX_API_BYTES + 1)
            require(len(encoded) <= MAX_API_BYTES, 'Official metadata exceeds its bound.')
        return _json(encoded)
    except Exception:
        raise ValueError('Official artifact metadata retrieval failed.') from None


def _blob_url(url):
    try:
        require(isinstance(url, str) and 0 < len(url) <= 16384
                and not any(ord(character) <= 32 or ord(character) == 127 for character in url),
                'Unsupported artifact redirect.')
        parsed = urllib.parse.urlsplit(url)
        # GitHub Actions v4's supported production Azure storage accounts.
        # Other hosts require an explicit reviewed adapter change, never a broad
        # *.windows.net allowlist or a caller-supplied alternate destination.
        require(parsed.scheme == 'https' and parsed.hostname is not None
                and re.fullmatch(r'productionresultssa[0-9]+\.blob\.core\.windows\.net', parsed.hostname)
                and parsed.netloc == parsed.hostname and parsed.path.startswith('/')
                and parsed.path != '/' and parsed.query and not parsed.fragment,
                'Unsupported artifact redirect.')
        query = urllib.parse.parse_qs(parsed.query, keep_blank_values=True)
        require(len(query.get('sig', [])) == 1 and query['sig'][0], 'Unsigned artifact redirect.')
        return url
    except Exception:
        raise ValueError('Unsupported artifact redirect.') from None


def download_artifact(url, output, expected_size):
    """Capture official redirect, then download with no token and no redirects."""
    try:
        require(_integer(expected_size, 1, MAX_WRAPPER_BYTES), 'Invalid wrapper download bound.')
        require(url.endswith('/zip'), 'Artifact download requires the official ZIP endpoint.')
        with _open(_api_request(url)) as response:
            require(response.status == 302, 'Official artifact ZIP redirect is missing.')
            signed_url = _blob_url(response.headers.get('Location'))
        request = urllib.request.Request(signed_url, headers={'User-Agent': 'SPGameFoundation-evidence'})
        with _open(request) as response:
            require(response.status == 200, 'Artifact blob download did not succeed.')
            length = response.headers.get('Content-Length')
            require(length is None or length == str(expected_size), 'Wrapper response size mismatch.')
            count = 0
            with Path(output).open('xb') as stream:
                while True:
                    block = response.read(min(evidence_parts.BLOCK_BYTES, expected_size - count + 1))
                    if not block:
                        break
                    count += len(block)
                    require(count <= expected_size, 'Wrapper download exceeds its bound.')
                    stream.write(block)
            require(count == expected_size, 'Wrapper download size mismatch.')
    except Exception:
        # Exception chaining and HTTPError messages can expose signed URLs.
        raise ValueError('Official artifact ZIP download failed.') from None


def _fetch(fetch, url):
    try:
        result = fetch(url)
    except Exception:
        raise ValueError('Official artifact metadata retrieval failed.') from None
    require(isinstance(result, dict), 'Official artifact metadata must be an object.')
    return result


def _verify_run(run, repository, spec):
    require(type(run.get('id')) is int and run['id'] == spec['run_id']
            and type(run.get('run_attempt')) is int and run['run_attempt'] == spec['attempt']
            and run.get('head_sha') == spec['head_sha']
            and run.get('path') == spec['workflow']
            and run.get('status') == 'completed' and run.get('conclusion') == 'success',
            'Prerequisite run attempt is not the reviewed successful workflow/head.')
    require(isinstance(run.get('repository'), dict)
            and run['repository'].get('full_name') == repository
            and isinstance(run.get('head_repository'), dict)
            and run['head_repository'].get('full_name') == repository,
            'Prerequisite run repository mismatch.')


def _verify_artifact(metadata, reviewed, spec):
    require(type(metadata.get('id')) is int and metadata['id'] == reviewed['id']
            and metadata.get('name') == reviewed['name']
            and type(metadata.get('size_in_bytes')) is int
            and metadata['size_in_bytes'] == reviewed['size_in_bytes']
            and metadata.get('digest') == 'sha256:' + reviewed['sha256']
            and metadata.get('expired') is False, 'Official artifact metadata differs from reviewed evidence.')
    run = metadata.get('workflow_run')
    require(isinstance(run, dict) and type(run.get('id')) is int
            and run['id'] == spec['run_id'] and run.get('head_sha') == spec['head_sha'],
            'Artifact workflow run or head mismatch.')


def _regular_member(info):
    mode = info.external_attr >> 16
    require(info.orig_filename == info.filename and not info.is_dir()
            and not (info.external_attr & 0x10)
            and stat.S_IFMT(mode) in (0, stat.S_IFREG)
            and not info.flag_bits & 1
            and info.compress_type in (zipfile.ZIP_STORED, zipfile.ZIP_DEFLATED),
            'Evidence ZIP members must be unencrypted regular files.')


def _zip_directory_bound(path, maximum_entries):
    # Check the end record before ZipFile allocates objects for an attacker-sized
    # central directory. These bounded archives never need a ZIP64 end record
    # (ZIP64 local member headers emitted by evidence_parts remain supported).
    size = path.stat().st_size
    with path.open('rb') as stream:
        require(stream.read(4) == b'PK\x03\x04', 'Evidence ZIP must start with a file header.')
        stream.seek(max(0, size - 65557))
        tail = stream.read(65557)
    offset = tail.rfind(b'PK\x05\x06')
    require(offset >= 0 and len(tail) - offset >= 22, 'Evidence ZIP end record is missing.')
    _, disk, start_disk, disk_count, count, directory_size, directory_offset, comment = \
        struct.unpack('<4s4H2LH', tail[offset:offset + 22])
    require(disk == start_disk == 0 and disk_count == count
            and 1 <= count <= maximum_entries and count != 65535
            and directory_size <= evidence_parts.MAX_MANIFEST_BYTES + maximum_entries * 128
            and directory_size >= count * 46
            and directory_offset + directory_size == size - len(tail) + offset
            and offset + 22 + comment == len(tail), 'Evidence ZIP directory exceeds its bound or is noncanonical.')


def _unpack_wrapper(path, folder, index):
    expected_part = '{}.part{:02d}'.format(evidence_parts.ARCHIVE_NAME, index)
    _zip_directory_bound(path, 2)
    with zipfile.ZipFile(path) as archive:
        entries = archive.infolist()
        require(len(entries) == 2 and sorted(entry.filename for entry in entries)
                == sorted((evidence_parts.MANIFEST_NAME, expected_part)),
                'Wrapper must contain exactly one manifest and its expected part.')
        for info in entries:
            _regular_member(info)
            limit = evidence_parts.MAX_MANIFEST_BYTES if info.filename == evidence_parts.MANIFEST_NAME \
                else evidence_parts.PART_BYTES
            require(_integer(info.file_size, 1, limit), 'Wrapper member exceeds its bound.')
        folder.mkdir()
        for info in entries:
            count = 0
            with archive.open(info) as source, (folder / info.filename).open('xb') as target:
                while True:
                    block = source.read(min(evidence_parts.BLOCK_BYTES, info.file_size - count + 1))
                    if not block:
                        break
                    count += len(block)
                    require(count <= info.file_size, 'Wrapper member exceeds its declared size.')
                    target.write(block)
            require(count == info.file_size, 'Wrapper member size mismatch.')
    return (folder / evidence_parts.MANIFEST_NAME).read_bytes()


def _evidence_name(name):
    require(isinstance(name, str) and not any(ord(character) < 32 or ord(character) == 127
                                            for character in name), 'Invalid evidence file path.')
    evidence_parts.evidence_path(name)


def _validate_manifest(encoded, spec):
    require(len(encoded) <= evidence_parts.MAX_MANIFEST_BYTES
            and hashlib.sha256(encoded).hexdigest() == spec['parts_manifest_sha256'],
            'Parts manifest digest differs from reviewed evidence.')
    manifest = _json(encoded)
    require(_exact_keys(manifest, ('version', 'part_size_bytes', 'max_parts', 'archive', 'parts', 'files'))
            and type(manifest['version']) is int and manifest['version'] == 1
            and type(manifest['part_size_bytes']) is int
            and manifest['part_size_bytes'] == evidence_parts.PART_BYTES
            and type(manifest['max_parts']) is int and manifest['max_parts'] in (8, evidence_parts.MAX_PARTS),
            'Unsupported bounded evidence manifest.')
    archive = manifest['archive']
    require(_exact_keys(archive, ('name', 'size', 'sha256'))
            and archive['name'] == evidence_parts.ARCHIVE_NAME
            and type(archive['size']) is int and archive['size'] == spec['archive_size']
            and archive['sha256'] == spec['archive_sha256'], 'Reviewed archive binding mismatch.')
    parts = manifest['parts']
    require(isinstance(parts, list) and len(parts) == len(spec['artifacts'])
            and 1 <= len(parts) <= manifest['max_parts'], 'Missing or extra evidence parts.')
    remaining = spec['archive_size']
    for index, part in enumerate(parts):
        require(_exact_keys(part, ('name', 'size', 'sha256'))
                and part['name'] == '{}.part{:02d}'.format(evidence_parts.ARCHIVE_NAME, index)
                and type(part['size']) is int and part['size'] == min(remaining, evidence_parts.PART_BYTES)
                and part['size'] > 0 and _sha(part['sha256']), 'Invalid evidence part inventory.')
        remaining -= part['size']
    require(remaining == 0, 'Evidence parts do not cover the reviewed archive.')
    files = manifest['files']
    require(isinstance(files, list) and 1 <= len(files) <= MAX_FILES, 'Restored file count exceeds its bound.')
    inventory = {}
    total_size = 0
    for entry in files:
        require(_exact_keys(entry, ('path', 'size', 'sha256'))
                and _integer(entry['size'], 0, MAX_RESTORED_BYTES) and _sha(entry['sha256']),
                'Invalid restored file inventory.')
        _evidence_name(entry['path'])
        require(entry['path'] not in inventory, 'Duplicate restored evidence file.')
        inventory[entry['path']] = entry
        total_size += entry['size']
        require(total_size <= MAX_RESTORED_BYTES, 'Restored member sum exceeds its bound.')
    for name in inventory:
        require(not any(parent.as_posix() in inventory for parent in Path(name).parents),
                'Evidence file and directory paths collide.')
    producer = inventory.get('Artifacts/' + PRODUCER_MANIFEST)
    require(producer is not None and producer['sha256'] == spec['evidence_manifest_sha256']
            and producer['size'] <= evidence_parts.MAX_MANIFEST_BYTES,
            'Original producer manifest differs from reviewed evidence.')
    return manifest, inventory


def _preflight_archive(parts_directory, working, manifest, inventory):
    """Bound the real ZIP directory before the unchanged restorer extracts it."""
    path = working / 'checked-evidence.zip'
    with path.open('xb') as output:
        for index, part in enumerate(manifest['parts']):
            source = parts_directory / 'part{:02d}'.format(index) / part['name']
            require(source.stat().st_size == part['size']
                    and evidence_parts.digest_file(source) == part['sha256'], 'Evidence part size or digest mismatch.')
            with source.open('rb') as incoming:
                shutil.copyfileobj(incoming, output, evidence_parts.BLOCK_BYTES)
    require(path.stat().st_size == manifest['archive']['size']
            and evidence_parts.digest_file(path) == manifest['archive']['sha256'],
            'Evidence archive size or digest mismatch.')
    _zip_directory_bound(path, MAX_FILES)
    with zipfile.ZipFile(path) as archive:
        entries = archive.infolist()
        require(len(entries) <= MAX_FILES and sorted(info.filename for info in entries) == sorted(inventory),
                'Inner archive does not exactly cover the bounded inventory.')
        total = 0
        for info in entries:
            _regular_member(info)
            _evidence_name(info.filename)
            require(info.file_size == inventory[info.filename]['size'], 'Inner member size differs from inventory.')
            total += info.file_size
            require(total <= MAX_RESTORED_BYTES, 'Inner member sum exceeds its bound.')
    path.unlink()


def verify_producer_inventory(artifacts, expected_sha256):
    """Bind the original producer manifest and all original files exactly."""
    artifacts = Path(artifacts)
    producer = artifacts / PRODUCER_MANIFEST
    require(producer.is_file() and not producer.is_symlink()
            and producer.stat().st_size <= evidence_parts.MAX_MANIFEST_BYTES
            and evidence_parts.digest_file(producer) == expected_sha256,
            'Original producer manifest digest mismatch.')
    inventory = _json(producer.read_bytes())
    require(isinstance(inventory, dict) and len(inventory) <= MAX_FILES,
            'Invalid original producer inventory.')
    for name, digest in inventory.items():
        _evidence_name('Artifacts/' + name)
        require(name != PRODUCER_MANIFEST and _sha(digest), 'Invalid original evidence digest.')
    found = {}
    size = 0
    for path in artifacts.rglob('*'):
        require(not path.is_symlink(), 'Restored evidence contains a symlink.')
        if path.is_dir():
            continue
        require(stat.S_ISREG(path.stat().st_mode), 'Restored evidence is not a regular file.')
        name = path.relative_to(artifacts).as_posix()
        size += path.stat().st_size
        require(len(found) < MAX_FILES and size <= MAX_RESTORED_BYTES, 'Restored evidence exceeds its bound.')
        found[name] = evidence_parts.digest_file(path)
    found.pop(PRODUCER_MANIFEST, None)
    require(found == inventory, 'Original producer inventory does not exactly cover restored evidence.')


def restore_artifacts(repository, spec, destination, fetch=fetch_json, downloader=download_artifact):
    """Return canonical destination/restored/Artifacts only after all checks pass."""
    validate_spec(repository, spec)
    destination = evidence_parts.new_destination(destination).resolve()
    base = API_ROOT + '/repos/' + repository + '/actions'
    run = _fetch(fetch, base + '/runs/{}/attempts/{}'.format(spec['run_id'], spec['attempt']))
    _verify_run(run, repository, spec)
    # Validate all metadata before downloading any wrapper.
    for reviewed in spec['artifacts']:
        metadata = _fetch(fetch, base + '/artifacts/{}'.format(reviewed['id']))
        _verify_artifact(metadata, reviewed, spec)
    with tempfile.TemporaryDirectory(prefix='spf-prerequisite-', dir=destination.parent) as temporary:
        working = Path(temporary)
        wrappers = working / 'wrappers'
        parts = working / 'parts'
        wrappers.mkdir()
        parts.mkdir()
        encoded = None
        receipts = []
        for index, reviewed in enumerate(spec['artifacts']):
            wrapper = wrappers / 'artifact-{}.zip'.format(reviewed['id'])
            try:
                downloader(base + '/artifacts/{}/zip'.format(reviewed['id']), wrapper,
                           reviewed['size_in_bytes'])
            except Exception:
                raise ValueError('Official artifact ZIP download failed.') from None
            require(wrapper.is_file() and not wrapper.is_symlink()
                    and wrapper.stat().st_size == reviewed['size_in_bytes']
                    and evidence_parts.digest_file(wrapper) == reviewed['sha256'],
                    'Downloaded wrapper size or digest differs from reviewed evidence.')
            current = _unpack_wrapper(wrapper, parts / 'part{:02d}'.format(index), index)
            require(encoded is None or current == encoded, 'Evidence manifests disagree between wrappers.')
            encoded = current
            receipts.append(dict(reviewed))
        manifest, inventory = _validate_manifest(encoded, spec)
        _preflight_archive(parts, working, manifest, inventory)
        # This unchanged implementation verifies the archive and every original
        # file hash again while it restores the evidence transactionally.
        evidence_parts.restore_evidence(parts, working / 'restored')
        verify_producer_inventory(working / 'restored' / 'Artifacts', spec['evidence_manifest_sha256'])
        receipt = dict(spec, repository=repository, artifacts=receipts)
        (working / 'receipts.json').write_text(json.dumps(receipt, indent=2, sort_keys=True) + '\n', encoding='utf-8')
        working.rename(destination)
    return destination / 'restored' / 'Artifacts'
