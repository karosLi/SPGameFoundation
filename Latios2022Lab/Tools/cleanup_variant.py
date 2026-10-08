#!/usr/bin/env python3
"""Frozen, full-tree Local control/cleanup-query variant. Never launches Unity.

Only anonymous official Git objects are materialized, without checkout, filters,
archive export rules, newline conversion, hooks, or an existing PackageCache.
The test-only miniature repositories exercise this code; they are not Unity proof.
"""
import copy
import difflib
import hashlib
import io
import json
import os
from pathlib import Path, PurePosixPath
import re
import stat
import subprocess
import tarfile
import unicodedata

PROJECT = Path(__file__).resolve().parents[1]
EXPERIMENT_ID = 'latios-0.11.5-cleanup-query-v1'
UPSTREAM_URL = 'https://github.com/Dreaming381/Latios-Framework.git'
UPSTREAM_COMMIT = '381a77dbf774ff603014d5695ef6c06abaa25d96'
UPSTREAM_TREE = '4790057a1964150f2ca815f89cc85498bc1cb43e'
SOURCE_INVENTORY_SHA256 = 'da4d10946620e4b6d23cf5214c802d90ebdb1c7f5e77a7e923e442b1ac5a0106'
PATCH_SHA256 = '6736b6b8f1c0ca32151a44163afc9a75fcf133a70dbe15b4175a8d8f3ab994a0'
BEFORE_SHA256 = '5a72addfdfde3c358876267b1fe89a5e100c0f3d162ee3ec45861dc502f609dc'
AFTER_SHA256 = '087ddf701e638d56fb01b80ef4becc27ae7e0516c504fc3e4bdc09d2dc99ab6d'
AFTER_GIT_BLOB = '86695fa96a1347d6bcb21b48359cd471418ef11d'
FILE_COUNT = 1235
TOTAL_BYTES = 23986607
MAX_FILES = 2048
MAX_ENTRIES = 4096
MAX_FILE_BYTES = 16 * 1024 * 1024
MAX_CAPTURE_BYTES = 64 * 1024 * 1024
EDITOR_MANIFEST_ADDITIONS = {'com.unity.toolchain.macos-arm64-linux-x86_64': '2.0.5'}
TARGET = 'Core/Internal/CollectionComponentOperations.cs'
PACKAGE_NAME = 'com.latios.latiosframework'
ARMS = ('unpatched-local-control', 'patched-local-variant')
BASELINE_LOCK = 'Docs/Validation/20261008-second-import/packages-lock.json'
BASELINE_LOCK_SHA256 = '0226f8bb362698bb4dfab670ca76a49f260b08d248721bfe086c34ba9a0365e7'
FROZEN_OWNED_INPUTS = {
    'Assets/Latios2022Tests/Editor/CoreTests.cs': 'fa100d3d5b764f3d243f3f259f40b16988713db1d9497e808e35bd413def5bf4',
    'Assets/Latios2022Tests/Editor/CoreTests.cs.meta': 'fa0c60b3abfaab580ea98d4b07a8f1529e04d23495ead5bed1562ec9e356d84f',
    'Assets/Latios2022Tests/Editor/Latios2022Lab.Editor.asmdef': 'fd8cd8b50d0979908f82bda87cfca7604956d6c7182293e90bb557ff9d1c7dcb',
    'Assets/Latios2022Tests/Editor/Latios2022Lab.Editor.asmdef.meta': '872e363a953aaf564af01512ecfecbafd18479f3d5783516a021eca55c2c2836',
    'Assets/Latios2022Tests/Editor/PsyshockTests.cs': 'b50626c09002353027e085c3bb16f9adaae354ac745f6097b6fc1dae6d70e74c',
    'Assets/Latios2022Tests/Editor/PsyshockTests.cs.meta': '9f84a3df8a39e1bc94fe50763ea4ebeb7b44be70982a27655416a7b049b8e5ec',
    'Assets/Latios2022Tests/EditorTools/Latios2022Lab.EditorTools.asmdef': '0d9294444253e6b4d408a6ca8d2d0c5f876ef807c3bf849c15bfeea4c584a678',
    'Assets/Latios2022Tests/EditorTools/Latios2022Lab.EditorTools.asmdef.meta': '49444edfca583caed0f7794d53f5a4d6e9202af971bb4ced6eae474ac01b7d42',
    'Assets/Latios2022Tests/PlayMode/Latios2022Lab.PlayMode.asmdef': 'dea470f2d225ec4c8b7a5618bb6c610fa9bbed33f43eb12cfbcce35b0888e823',
    'Assets/Latios2022Tests/PlayMode/Latios2022Lab.PlayMode.asmdef.meta': '3716e004388c6cb34a44fc75deae9aa03febce15a686c532b2338657dc4a6227',
    'Assets/Latios2022Tests/PlayMode/PlayModeTests.cs': 'f3b97a1be7a6e917a4f1305e32d644555978ce97e54df70ad1aa69063d86786c',
    'Assets/Latios2022Tests/PlayMode/PlayModeTests.cs.meta': 'c5dab6e3324b760eb2f51400c0837c40adcbc03442fd7fc88d50d37341a9cc63',
    'Assets/Latios2022Tests/Runtime/CollectionProbe.cs': 'c438517d078bb123381c67ba508e65a8b18430b94255ac7d8534ce4f5057ee9b',
    'Assets/Latios2022Tests/Runtime/CollectionProbe.cs.meta': '1a712cbfd7544dcfd26e675be9d3f9cabfb7eb381926d266202c52b3d6568c75',
    'Assets/Latios2022Tests/Runtime/LabCollection.cs': 'a667221f9032cd13870d842ca6af42384398c1af4140b355c5a5348501eca069',
    'Assets/Latios2022Tests/Runtime/LabCollection.cs.meta': 'f9918f1663e065e754d40d6311411596e1f2a0079dc4290e8fd94655a843deb3',
    'Assets/Latios2022Tests/Runtime/LabPlayerSmoke.cs': 'a0f110ce8ee87bb8530500658a106a1a8cc34c2133449b932e2c49a992e5cb03',
    'Assets/Latios2022Tests/Runtime/LabPlayerSmoke.cs.meta': 'f1951df762273df49a4f6c9e17065550c45ecba541642c4ee44694ad5e701f27',
    'Assets/Latios2022Tests/Runtime/LabRunPolicy.cs': '2173e39e37e073b49623bb976abc0aae6eeedfc3460b7c1e4c49c669e041967d',
    'Assets/Latios2022Tests/Runtime/LabRunPolicy.cs.meta': 'fb94c1286ce526a84bc803a05592d4f26de1983bbb34294af90ae3b40cb380e7',
    'Assets/Latios2022Tests/Runtime/LabWorld.cs': 'f627b34ed475dc9dadd60f9378e809b6f1a2c4e4f31046a494681f8713910370',
    'Assets/Latios2022Tests/Runtime/LabWorld.cs.meta': 'c356669f9614f10f1c722d801e17b9d895b8540f3798046e0e6afbbc047248d7',
    'Assets/Latios2022Tests/Runtime/Latios2022Lab.Runtime.asmdef': '9d0b0c01433ac9170f05469ec944c815314b04ddc8cda216cf4918915982c6a0',
    'Assets/Latios2022Tests/Runtime/Latios2022Lab.Runtime.asmdef.meta': '42f2ef44d316b32b906a624d8b29ee10b68441547298061b10042454eefb41d7',
    'Assets/Latios2022Tests/Runtime/PsyshockProbe.cs': '55aa253aa446743da4db0f76054ea537ebf3d77b44fc70bbb69598c9f5b93ae1',
    'Assets/Latios2022Tests/Runtime/PsyshockProbe.cs.meta': 'e06dbeab7808df3dbd3c185251daa7dab7acede2a87d6f927c9115dbad624a3d',
}
SOURCE_MANIFEST_SHA256 = 'eb12fd99c903f4329905ceef9a78df0decf1e1ef3801a206cfb4e3c15d19b5ba'
OLD = b'RemoveComponent(context->addQuery, t)'
NEW = b'RemoveComponent(context->removeQuery, t)'


def require(ok, message):
    if not ok:
        raise ValueError(message)


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def git_blob(data):
    return hashlib.sha1(b'blob ' + str(len(data)).encode('ascii') + b'\0' + data).hexdigest()


def json_bytes(value):
    return (json.dumps(value, indent=2, sort_keys=True) + '\n').encode('utf-8')


def _unique_pairs(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, 'Duplicate JSON key: ' + key)
        result[key] = value
    return result


def _json(data):
    return json.loads(data, object_pairs_hook=_unique_pairs)


def _write_json(path, value):
    with Path(path).open('xb') as stream:
        stream.write(json_bytes(value))


def _canonical(path):
    path = Path(path)
    require(path.is_absolute() and str(path) == str(path.resolve()), 'Path must be canonical and absolute: ' + str(path))
    require(not any(parent.is_symlink() for parent in (path, *path.parents)), 'Symlink ancestry is forbidden: ' + str(path))
    return path


def _safe_name(name):
    require(isinstance(name, str) and name and '\\' not in name and ':' not in name,
            'Unsafe package path: ' + repr(name))
    require(not any(ord(c) < 32 or ord(c) == 127 for c in name), 'Control character in package path.')
    parts = name.split('/')
    require(all(part and part not in ('.', '..') and part.casefold() != '.git' for part in parts),
            'Path traversal or Git metadata in package path: ' + name)
    require(not PurePosixPath(name).is_absolute(), 'Absolute package path: ' + name)
    return parts


def _check_names(names):
    seen = {}
    files = set()
    for name in names:
        parts = _safe_name(name)
        require(name not in files, 'Duplicate package path: ' + name)
        files.add(name)
        for count in range(1, len(parts) + 1):
            prefix = '/'.join(parts[:count])
            folded = unicodedata.normalize('NFC', prefix).casefold()
            require(folded not in seen or seen[folded] == prefix, 'Case/Unicode package path collision: ' + prefix)
            seen[folded] = prefix
    require(not any('/'.join(name.split('/')[:i]) in files
                    for name in files for i in range(1, len(name.split('/')))), 'File/directory package collision.')


def _tree_sha(files):
    root = {}
    for row in files:
        node = root
        parts = _safe_name(row['path'])
        for name in parts[:-1]:
            node = node.setdefault(name, {})
        node[parts[-1]] = (row['mode'], row['git_blob'])

    def tree(node):
        entries = []
        for name, value in node.items():
            is_dir = isinstance(value, dict)
            mode, oid = ('40000', tree(value)) if is_dir else value
            entries.append((name.encode('utf-8') + (b'/' if is_dir else b''),
                            mode.encode('ascii') + b' ' + name.encode('utf-8') + b'\0' + bytes.fromhex(oid)))
        data = b''.join(value for _, value in sorted(entries))
        return hashlib.sha1(b'tree ' + str(len(data)).encode('ascii') + b'\0' + data).hexdigest()
    return tree(root)


def _spec():
    return dict(schema=1, id=EXPERIMENT_ID, upstream_url=UPSTREAM_URL,
                upstream_commit=UPSTREAM_COMMIT, upstream_tree=UPSTREAM_TREE,
                source_inventory_sha256=SOURCE_INVENTORY_SHA256, patch_sha256=PATCH_SHA256,
                file_count=FILE_COUNT, total_bytes=TOTAL_BYTES, target=TARGET,
                before_sha256=BEFORE_SHA256, after_sha256=AFTER_SHA256, after_git_blob=AFTER_GIT_BLOB,
                arms=list(ARMS), baseline_lock_path=BASELINE_LOCK, baseline_lock_sha256=BASELINE_LOCK_SHA256,
                frozen_owned_inputs=FROZEN_OWNED_INPUTS, source_manifest_sha256=SOURCE_MANIFEST_SHA256)


def _contract(project=None):
    project = Path(project) if project is not None else PROJECT
    folder = _canonical(project) / 'Variants'
    for name in ('cleanup-query-v1.json', 'cleanup-query-v1.patch', 'cleanup-query-v1.source-inventory.json'):
        _canonical(folder / name)
    require(_json((folder / 'cleanup-query-v1.json').read_bytes()) == _spec(), 'Frozen cleanup variant specification drifted.')
    patch = (folder / 'cleanup-query-v1.patch').read_bytes()
    require(sha256(patch) == PATCH_SHA256, 'Frozen cleanup patch differs from the approved patch.')
    raw = (folder / 'cleanup-query-v1.source-inventory.json').read_bytes()
    require(sha256(raw) == SOURCE_INVENTORY_SHA256, 'Frozen official source inventory drifted.')
    inventory = _json(raw)
    require(set(inventory) == {'schema', 'upstream_commit', 'upstream_tree', 'file_count', 'total_bytes', 'files'}
            and inventory['schema'] == 1 and inventory['upstream_commit'] == UPSTREAM_COMMIT
            and inventory['upstream_tree'] == UPSTREAM_TREE, 'Invalid source inventory identity.')
    files = inventory['files']
    require(len(files) == inventory['file_count'] == FILE_COUNT
            and sum(row['size'] for row in files) == inventory['total_bytes'] == TOTAL_BYTES,
            'Incomplete source inventory.')
    _check_names([row['path'] for row in files])
    require(files == sorted(files, key=lambda row: row['path']), 'Source inventory must have fixed path order.')
    for row in files:
        require(set(row) == {'path', 'mode', 'size', 'git_blob', 'sha256'}
                and row['mode'] in ('100644', '100755') and type(row['size']) is int and row['size'] >= 0
                and re.fullmatch('[0-9a-f]{40}', row['git_blob']) and re.fullmatch('[0-9a-f]{64}', row['sha256']),
                'Invalid source file record.')
    require(_tree_sha(files) == UPSTREAM_TREE, 'Source inventory does not reconstruct the frozen Git tree.')
    return inventory, patch


def check_owned_inputs(project):
    project = _canonical(project)
    actual = set()
    for path in (project / 'Assets').rglob('*'):
        relative = path.relative_to(project).as_posix()
        if (path.suffix == '.cs' and '/EditorTools/' not in relative) or path.suffix == '.asmdef':
            actual.update((relative, relative + '.meta'))
    require(actual == set(FROZEN_OWNED_INPUTS), 'Frozen owned oracle file set drifted.')
    for relative, expected in FROZEN_OWNED_INPUTS.items():
        require(sha256(_canonical(project / relative).read_bytes()) == expected,
                'Frozen owned oracle bytes drifted: ' + relative)
    return dict(FROZEN_OWNED_INPUTS)


def experiment_for_arm(arm):
    return check_experiment(dict(schema=1, id=EXPERIMENT_ID, arm=arm, upstream_commit=UPSTREAM_COMMIT,
                                 upstream_tree=UPSTREAM_TREE, patch_sha256=PATCH_SHA256,
                                 source_inventory_sha256=SOURCE_INVENTORY_SHA256))


def check_experiment(obj):
    if obj is None:
        return None
    require(isinstance(obj, dict), 'Experiment must be a complete object, or absent.')
    expected = dict(schema=1, id=EXPERIMENT_ID, arm=obj.get('arm'), upstream_commit=UPSTREAM_COMMIT,
                    upstream_tree=UPSTREAM_TREE, patch_sha256=PATCH_SHA256,
                    source_inventory_sha256=SOURCE_INVENTORY_SHA256)
    require(type(obj.get('schema')) is int and obj == expected and obj.get('arm') in ARMS,
            'Unknown, incomplete, or drifted cleanup experiment.')
    _contract()
    return copy.deepcopy(expected)


def _git(repository, *arguments):
    # Do not load caller Git configuration, replace objects, run hooks, or prompt.
    env = {key: value for key, value in os.environ.items() if not key.startswith('GIT_') and key not in ('GH_TOKEN', 'GITHUB_TOKEN')}
    env.update(GIT_CONFIG_NOSYSTEM='1', GIT_CONFIG_GLOBAL=os.devnull, GIT_CONFIG_COUNT='0',
               GIT_TERMINAL_PROMPT='0', GIT_NO_REPLACE_OBJECTS='1')
    command = ['git', '-c', 'credential.helper=', '-c', 'core.hooksPath=' + os.devnull,
               '-c', 'http.followRedirects=false', '-c', 'http.extraHeader=',
               '-c', 'protocol.file.allow=never', '-C', str(repository), *arguments]
    return subprocess.check_output(command, env=env, stderr=subprocess.PIPE, timeout=300)


def _fetch_upstream(workspace):
    objects = workspace / 'upstream-objects.git'
    require(not objects.exists() and not objects.is_symlink(), 'Upstream Git store must be new.')
    _git(workspace, 'init', '--bare', '--template=', str(objects))
    _git(objects, 'fetch', '--no-tags', '--depth=1', UPSTREAM_URL, UPSTREAM_COMMIT)
    require(_git(objects, 'rev-parse', 'FETCH_HEAD').decode().strip() == UPSTREAM_COMMIT, 'Fetched upstream commit mismatch.')
    require(_git(objects, 'rev-parse', UPSTREAM_COMMIT + '^{tree}').decode().strip() == UPSTREAM_TREE,
            'Fetched upstream tree mismatch.')
    return objects


def _materialize(objects, destination, expected):
    require(not destination.exists() and not destination.is_symlink(), 'Source snapshot destination must be new.')
    raw = _git(objects, 'ls-tree', '-rz', UPSTREAM_COMMIT)
    rows = []
    for line in raw.split(b'\0')[:-1]:
        meta, path = line.split(b'\t', 1)
        mode, kind, oid = meta.decode('ascii').split(' ')
        require(kind == 'blob' and mode in ('100644', '100755'), 'Symlink, submodule, or unsupported Git mode.')
        rows.append(dict(path=path.decode('utf-8'), mode=mode, git_blob=oid))
    _check_names([row['path'] for row in rows])
    rows.sort(key=lambda row: row['path'])
    require(rows == [{key: row[key] for key in ('path', 'mode', 'git_blob')} for row in expected['files']],
            'Fetched Git tree file inventory mismatch.')
    destination.mkdir()
    # cat-file returns literal Git blob bytes; no checkout/smudge/archive semantics.
    for row in expected['files']:
        data = _git(objects, 'cat-file', 'blob', row['git_blob'])
        require(len(data) == row['size'] and sha256(data) == row['sha256'] and git_blob(data) == row['git_blob'],
                'Fetched Git blob differs from frozen inventory: ' + row['path'])
        target = destination / row['path']
        target.parent.mkdir(parents=True, exist_ok=True)
        with target.open('xb') as stream:
            stream.write(data)
        target.chmod(int(row['mode'][-3:], 8))
    require(inventory(destination) == expected['files'], 'Pristine materialization differs from raw Git objects.')


def _bounded_walk(root, rejected=None):
    """Bound directory enumeration too, before retaining arbitrary entry lists."""
    pending = [root]
    count = 0
    while pending:
        directory = pending.pop()
        dirs, files = [], []
        try:
            require(not directory.is_symlink(), 'Symlink directory during enumeration.')
            with os.scandir(directory) as entries:
                for entry in entries:
                    count += 1
                    require(count <= MAX_ENTRIES, 'Package entry count exceeds limit; remaining entries omitted.')
                    (dirs if entry.is_dir(follow_symlinks=False) else files).append(entry.name)
        except (ValueError, OSError) as exc:
            if rejected is None:
                raise
            rejected.append(str(exc))
            return
        yield directory, dirs, files
        pending.extend(directory / name for name in reversed(sorted(dirs)))


def inventory(root):
    """List every ordinary file, rejecting unsafe paths, links and special files."""
    root = _canonical(root)
    require(root.is_dir(), 'Package directory is missing: ' + str(root))
    rows = []
    all_names = []
    total = 0
    for directory, dirs, files in _bounded_walk(root):
        require(len(all_names) + len(dirs) + len(files) <= MAX_ENTRIES, 'Package entry count exceeds bounded inventory.')
        for name in sorted(dirs + files):
            path = Path(directory) / name
            relative = path.relative_to(root).as_posix()
            all_names.append(relative)
            mode = path.lstat().st_mode
            require(stat.S_ISREG(mode) or stat.S_ISDIR(mode), 'Symlink or special package entry: ' + relative)
            if stat.S_ISREG(mode):
                require(path.stat().st_nlink == 1, 'Hard-linked package file: ' + relative)
                permissions = stat.S_IMODE(mode)
                require(permissions in (0o644, 0o755), 'Package file mode drift: ' + relative)
                require(len(rows) < MAX_FILES and path.stat().st_size <= MAX_FILE_BYTES, 'Package file exceeds bounded inventory.')
                with path.open('rb') as stream:
                    data = stream.read(MAX_FILE_BYTES + 1)
                total += len(data)
                require(len(data) <= MAX_FILE_BYTES and total <= MAX_CAPTURE_BYTES, 'Package bytes exceed bounded inventory.')
                rows.append(dict(path=relative, mode='100' + format(permissions, '03o'), size=len(data),
                                 git_blob=git_blob(data), sha256=sha256(data)))
    # Include directory spelling as well as files, including otherwise empty dirs.
    seen = {}
    for name in all_names:
        _safe_name(name)
        folded = unicodedata.normalize('NFC', name).casefold()
        require(folded not in seen or seen[folded] == name, 'Case/Unicode package path collision: ' + name)
        seen[folded] = name
    _check_names([row['path'] for row in rows])
    expected_dirs = {'/'.join(row['path'].split('/')[:i]) for row in rows for i in range(1, len(row['path'].split('/')))}
    actual_dirs = {name for name in all_names if (root / name).is_dir()}
    require(actual_dirs == expected_dirs, 'Unexpected empty package directory.')
    return sorted(rows, key=lambda row: row['path'])


def _patched_files(source):
    result = copy.deepcopy(source)
    targets = [row for row in result if row['path'] == TARGET]
    require(len(targets) == 1 and targets[0]['sha256'] == BEFORE_SHA256, 'Patch target is not the frozen source.')
    targets[0].update(size=targets[0]['size'] + len(NEW) - len(OLD), sha256=AFTER_SHA256, git_blob=AFTER_GIT_BLOB)
    return result


def _exact_patch(before, after):
    require(sha256(before) == BEFORE_SHA256 and before.count(OLD) == 1, 'Patch preimage mismatch.')
    require(after == before.replace(OLD, NEW, 1) and sha256(after) == AFTER_SHA256, 'Patch postimage mismatch.')
    patch = ''.join(difflib.unified_diff(before.decode('utf-8').splitlines(keepends=True),
                                       after.decode('utf-8').splitlines(keepends=True),
                                       fromfile='a/' + TARGET, tofile='b/' + TARGET, n=3)).encode('utf-8')
    require(sha256(patch) == PATCH_SHA256, 'Exact one-line diff differs from the approved patch.')
    return patch


def _paths(workspace, project, check_package=True):
    workspace, project = _canonical(workspace), _canonical(project)
    require(project == workspace / 'source/Latios2022Lab', 'Experiment project must be the isolated workspace source copy.')
    require(project.is_dir(), 'Runtime lab project is missing.')
    pristine = workspace / 'upstream-source' / PACKAGE_NAME
    package = workspace / 'variant-packages' / PACKAGE_NAME
    if check_package:
        for path in (pristine, package):
            _canonical(path)
    return workspace, project, pristine, package


def prepare(workspace, project, experiment, source_commit):
    experiment = check_experiment(experiment)
    require(experiment is not None, 'Preparing a Local package requires an explicit experiment.')
    require(isinstance(source_commit, str) and re.fullmatch('[0-9a-f]{40}', source_commit), 'Missing exact prepared source commit.')
    workspace, project, pristine, package = _paths(workspace, project)
    require(project != PROJECT, 'Cannot change the reviewed source manifest.')
    for path in (pristine.parent, package.parent, workspace / 'experiment-record.json', workspace / 'source-manifest.json'):
        require(not path.exists() and not path.is_symlink(), 'Variant inputs must be fresh: ' + str(path))
    for relative in ('Library', 'Temp', '.upm-cache', '.launch-lock', 'Packages/packages-lock.json'):
        require(not (project / relative).exists() and not (project / relative).is_symlink(), 'Runtime lab is not fresh: ' + relative)
    expected, patch = _contract(project)
    check_owned_inputs(project)
    baseline = _canonical(project / BASELINE_LOCK).read_bytes()
    require(sha256(baseline) == BASELINE_LOCK_SHA256, 'Historical non-Latios lock trust anchor drifted.')
    manifest_path = _canonical(project / 'Packages/manifest.json')
    before = manifest_path.read_bytes()
    require(sha256(before) == SOURCE_MANIFEST_SHA256, 'Reviewed source manifest drifted.')
    manifest = _json(before)
    require(set(manifest) == {'dependencies'} and manifest['dependencies'].get(PACKAGE_NAME) == UPSTREAM_URL + '#' + UPSTREAM_COMMIT,
            'Runtime manifest must start with the original pinned Git dependency.')
    objects = _fetch_upstream(workspace)
    pristine.parent.mkdir()
    _materialize(objects, pristine, expected)
    package.parent.mkdir()
    package.mkdir()
    for row in expected['files']:
        target = package / row['path']
        target.parent.mkdir(parents=True, exist_ok=True)
        with target.open('xb') as stream:
            stream.write((pristine / row['path']).read_bytes())
        target.chmod(int(row['mode'][-3:], 8))
    applied = experiment['arm'] == 'patched-local-variant'
    if applied:
        target = package / TARGET
        original = target.read_bytes()
        updated = original.replace(OLD, NEW, 1)
        require(_exact_patch(original, updated) == patch, 'Patch bytes mismatch.')
        target.write_bytes(updated)
    expected_package = _patched_files(expected['files']) if applied else expected['files']
    require(inventory(package) == expected_package, 'Prepared package is not the exact authorized arm.')
    url = 'file:' + str(package)
    manifest['dependencies'][PACKAGE_NAME] = url
    after = json_bytes(manifest)
    # Change only the Latios value while preserving original manifest formatting.
    old_value = json.dumps(UPSTREAM_URL + '#' + UPSTREAM_COMMIT).encode('utf-8')
    require(before.count(old_value) == 1, 'Manifest pinned URL is ambiguous.')
    after = before.replace(old_value, json.dumps(url).encode('utf-8'), 1)
    require(_json(after) == manifest, 'Unexpected runtime manifest change.')
    record = dict(schema=1, experiment=experiment, source_commit=source_commit,
                  workspace=str(workspace), project_path=str(project), pristine_path=str(pristine),
                  package_path=str(package), manifest_url=url, source_inventory_sha256=SOURCE_INVENTORY_SHA256,
                  package_inventory_sha256=sha256(json_bytes(expected_package)),
                  manifest_before_sha256=sha256(before), manifest_after_sha256=sha256(after),
                  baseline_lock_sha256=BASELINE_LOCK_SHA256)
    with (workspace / 'source-manifest.json').open('xb') as stream:
        stream.write(before)
    manifest_path.write_bytes(after)
    verify(project, record)
    _write_json(workspace / 'experiment-record.json', record)
    return record


def _record(record, check_package=True):
    require(isinstance(record, dict) and type(record.get('schema')) is int and record['schema'] == 1, 'Invalid experiment runtime record.')
    require(set(record) == {'schema', 'experiment', 'source_commit', 'workspace', 'project_path', 'pristine_path',
                           'package_path', 'manifest_url', 'source_inventory_sha256', 'package_inventory_sha256',
                           'manifest_before_sha256', 'manifest_after_sha256', 'baseline_lock_sha256'},
            'Unknown or missing experiment runtime fields.')
    experiment = check_experiment(record['experiment'])
    require(experiment is not None and re.fullmatch('[0-9a-f]{40}', record['source_commit']), 'Invalid runtime source identity.')
    workspace, project, pristine, package = _paths(record['workspace'], record['project_path'], check_package=check_package)
    require(record['pristine_path'] == str(pristine) and record['package_path'] == str(package)
            and record['manifest_url'] == 'file:' + str(package), 'Runtime package paths differ from the canonical layout.')
    require(record['source_inventory_sha256'] == SOURCE_INVENTORY_SHA256
            and record['baseline_lock_sha256'] == BASELINE_LOCK_SHA256, 'Runtime trust anchor differs from the frozen contract.')
    for field in ('package_inventory_sha256', 'manifest_before_sha256', 'manifest_after_sha256'):
        require(isinstance(record[field], str) and re.fullmatch('[0-9a-f]{64}', record[field]), 'Invalid runtime digest: ' + field)
    return workspace, project, pristine, package


def file_url(record):
    _record(record)
    return record['manifest_url']


def _baseline_lock(project):
    raw = _canonical(project / BASELINE_LOCK).read_bytes()
    require(sha256(raw) == BASELINE_LOCK_SHA256, 'Historical full dependency graph drifted.')
    return _json(raw)['dependencies']


def check_lock(lock_dict, record):
    _, project, _, _ = _record(record)
    require(isinstance(lock_dict, dict), 'Expected a real Unity lock object.')
    if set(lock_dict) == {'dependencies'}:
        lock = lock_dict['dependencies']
    else:
        lock = lock_dict
    baseline = _baseline_lock(project)
    require(isinstance(lock, dict) and set(lock) == set(baseline), 'Real local lock dependency set drifted.')
    for name, entry in baseline.items():
        if name != PACKAGE_NAME:
            require(lock[name] == entry, 'Non-Latios dependency graph drifted: ' + name)
    expected = dict(version=record['manifest_url'], source='local', depth=0,
                    dependencies=baseline[PACKAGE_NAME]['dependencies'])
    require(lock.get(PACKAGE_NAME) == expected, 'Latios must have a real Local lock with the exact canonical package path and dependencies.')
    return lock


def check_registered(records, record):
    _, _, _, package = _record(record)
    if isinstance(records, dict):
        require(set(records) == {'packages'}, 'Unexpected registered package wrapper.')
        records = records['packages']
    require(isinstance(records, list) and all(isinstance(row, dict) for row in records), 'Missing real registered package inventory.')
    require(len({row.get('name') for row in records}) == len(records), 'Duplicate registered package identity.')
    latios = [row for row in records if row.get('name') == PACKAGE_NAME]
    require(len(latios) == 1, 'Missing registered Latios package.')
    entry = latios[0]
    require(entry.get('source') == 'Local' and entry.get('resolvedPath') == str(package)
            and entry.get('packageId') == PACKAGE_NAME + '@' + record['manifest_url']
            and entry.get('version') == '0.11.5', 'Registered Latios provenance is not the exact Local arm.')
    _canonical(entry['resolvedPath'])
    return entry


def _check_manifest(workspace, project, record):
    original = _canonical(workspace / 'source-manifest.json').read_bytes()
    require(sha256(original) == SOURCE_MANIFEST_SHA256 == record['manifest_before_sha256'],
            'Original authored manifest snapshot drifted.')
    old = json.dumps(UPSTREAM_URL + '#' + UPSTREAM_COMMIT).encode('utf-8')
    require(original.count(old) == 1, 'Original Latios manifest URL is ambiguous.')
    prepared = original.replace(old, json.dumps(record['manifest_url']).encode('utf-8'), 1)
    require(sha256(prepared) == record['manifest_after_sha256'], 'Prepared manifest hash differs from exact authorized replacement.')
    expected = _json(prepared)
    current = _json(_canonical(project / 'Packages/manifest.json').read_bytes())
    require(isinstance(current, dict) and set(current) == {'dependencies'}
            and isinstance(current['dependencies'], dict), 'Unexpected runtime manifest configuration.')
    additions = {name: version for name, version in current['dependencies'].items()
                 if name not in expected['dependencies']}
    if additions:
        require(all(EDITOR_MANIFEST_ADDITIONS.get(name) == version for name, version in additions.items()),
                'Unreviewed runtime manifest dependency addition.')
        lock_path = _canonical(project / 'Packages/packages-lock.json')
        require(lock_path.is_file(), 'Editor manifest addition requires the matching real lock.')
        lock = check_lock(_json(lock_path.read_bytes()), record)
        require(all(lock.get(name, {}).get('version') == version
                    and lock[name].get('source') == 'registry' for name, version in additions.items()),
                'Editor-added toolchain does not match the real lock.')
        current = copy.deepcopy(current)
        for name in additions:
            del current['dependencies'][name]
    require(current == expected, 'Runtime manifest differs beyond the authorized Local Latios dependency.')


def verify(project, record):
    workspace, expected_project, pristine, package = _record(record)
    require(_canonical(project) == expected_project, 'Runtime record targets another lab project.')
    expected, _ = _contract(expected_project)
    check_owned_inputs(expected_project)
    source_files = inventory(pristine)
    package_files = inventory(package)
    require(source_files == expected['files'], 'Pristine upstream snapshot drifted.')
    applied = record['experiment']['arm'] == 'patched-local-variant'
    expected_files = _patched_files(expected['files']) if applied else expected['files']
    require(package_files == expected_files, 'Local arm full package inventory drifted.')
    require(sha256(json_bytes(package_files)) == record['package_inventory_sha256'], 'Runtime package inventory hash mismatch.')
    changed = [before['path'] for before, after in zip(source_files, package_files) if before != after]
    require(changed == ([TARGET] if applied else []), 'Expected exactly one changed file for variant and none for control.')
    diff = _exact_patch((pristine / TARGET).read_bytes(), (package / TARGET).read_bytes()) if applied else b''
    _check_manifest(workspace, expected_project, record)
    baseline = _baseline_lock(expected_project)
    lock_path = expected_project / 'Packages/packages-lock.json'
    if lock_path.exists() or lock_path.is_symlink():
        check_lock(_json(_canonical(lock_path).read_bytes()), record)
    return dict(valid=True, experiment=record['experiment'], source_inventory_sha256=SOURCE_INVENTORY_SHA256,
                package_inventory_sha256=sha256(json_bytes(package_files)), file_count=len(package_files),
                total_bytes=sum(row['size'] for row in package_files), changed_paths=changed,
                applied_patch_sha256=sha256(diff) if applied else None,
                source_files=source_files, package_files=package_files,
                non_latios_dependency_count=len(baseline) - 1)


def _capture_tree(root, destination):
    """Capture regular bytes safely even after drift; never follow a bad entry."""
    result = dict(files=[], rejected=[], archive=destination.name)
    try:
        root = _canonical(root)
        require(root.is_dir(), 'Snapshot source directory is missing.')
    except (ValueError, OSError) as exc:
        result['rejected'].append(str(exc))
        return result
    total = 0
    with tarfile.open(destination, 'x', format=tarfile.PAX_FORMAT) as archive:
        for directory, dirs, files in _bounded_walk(root, result['rejected']):
            for name in list(dirs):
                path = Path(directory) / name
                if path.is_symlink():
                    result['rejected'].append(path.relative_to(root).as_posix() + ': symlink directory')
                    dirs.remove(name)
            for name in sorted(files):
                if len(result['files']) >= MAX_FILES or total >= MAX_CAPTURE_BYTES:
                    result['rejected'].append('Snapshot file/total-byte limit reached; remaining entries omitted.')
                    dirs.clear()
                    break
                path = Path(directory) / name
                relative = path.relative_to(root).as_posix()
                try:
                    _safe_name(relative)
                    require(stat.S_ISREG(path.lstat().st_mode) and not path.is_symlink(), 'Unsafe file type')
                    require(path.stat().st_nlink == 1, 'Hard-linked file')
                    flags = os.O_RDONLY | getattr(os, 'O_NOFOLLOW', 0)
                    fd = os.open(path, flags)
                    with os.fdopen(fd, 'rb') as stream:
                        info = os.fstat(stream.fileno())
                        require(stat.S_ISREG(info.st_mode) and info.st_nlink == 1, 'Unsafe opened file')
                        require(info.st_size <= MAX_FILE_BYTES and info.st_size <= MAX_CAPTURE_BYTES - total,
                                'File exceeds per-file or remaining total-byte limit; omitted')
                        data = stream.read(min(MAX_FILE_BYTES, MAX_CAPTURE_BYTES - total) + 1)
                        require(len(data) <= MAX_FILE_BYTES and len(data) <= MAX_CAPTURE_BYTES - total,
                                'File grew past snapshot byte limit; omitted')
                    total += len(data)
                    row = dict(path=relative, mode='100' + format(stat.S_IMODE(info.st_mode), '03o'), size=len(data),
                               sha256=sha256(data), git_blob=git_blob(data))
                    result['files'].append(row)
                    member = tarfile.TarInfo(relative)
                    member.size = len(data)
                    member.mode = stat.S_IMODE(info.st_mode)
                    member.mtime = 0
                    archive.addfile(member, io.BytesIO(data))
                except (ValueError, OSError) as exc:
                    result['rejected'].append(relative + ': ' + str(exc))
    result['files'].sort(key=lambda row: row['path'])
    result['total_bytes'] = total
    result['archive_sha256'] = sha256(destination.read_bytes())
    return result


def capture(workspace, artifacts, snapshot, record):
    """Write a new immutable evidence set; report drift without losing its bytes."""
    requested_workspace = _canonical(workspace)
    workspace, project, pristine, package = _record(record, check_package=False)
    require(requested_workspace == workspace, 'Evidence workspace mismatch.')
    artifacts = _canonical(artifacts)
    require(artifacts == workspace / 'Artifacts', 'Evidence must stay in the isolated workspace Artifacts directory.')
    require(isinstance(snapshot, str) and re.fullmatch('[A-Za-z0-9][A-Za-z0-9_.-]{0,95}', snapshot), 'Unsafe snapshot name.')
    folder = artifacts / 'cleanup-variant' / snapshot
    _canonical(folder)
    folder.mkdir(parents=True, exist_ok=False)
    report = dict(valid=False, errors=[], snapshot=snapshot)
    try:
        verification = verify(project, record)
        report.update({key: value for key, value in verification.items() if key not in ('source_files', 'package_files')})
    except (ValueError, OSError) as exc:
        report['errors'].append(str(exc))
    _write_json(folder / 'record.json', record)
    _write_json(folder / 'spec.json', _spec())
    source = _capture_tree(pristine, folder / 'source.tar')
    arm = _capture_tree(package, folder / 'package.tar')
    _write_json(folder / 'source-inventory.json', source)
    _write_json(folder / 'package-inventory.json', arm)
    before_files = {row['path']: row for row in source['files']}
    after_files = {row['path']: row for row in arm['files']}
    differences = [dict(path=name, before=before_files.get(name), after=after_files.get(name))
                   for name in sorted(set(before_files) | set(after_files))
                   if before_files.get(name) != after_files.get(name)]
    _write_json(folder / 'file-differences.json', differences)
    if TARGET in before_files and TARGET in after_files:
        if max(before_files[TARGET]['size'], after_files[TARGET]['size']) <= 128 * 1024:
            with tarfile.open(folder / 'source.tar') as archive:
                before_target = archive.extractfile(TARGET).read()
            with tarfile.open(folder / 'package.tar') as archive:
                after_target = archive.extractfile(TARGET).read()
            actual_diff = ''.join(difflib.unified_diff(
                before_target.decode('utf-8', errors='backslashreplace').splitlines(keepends=True),
                after_target.decode('utf-8', errors='backslashreplace').splitlines(keepends=True),
                fromfile='a/' + TARGET, tofile='b/' + TARGET, n=3)).encode('utf-8')
            with (folder / 'actual-target.diff').open('xb') as stream:
                stream.write(actual_diff)
            report['actual_target_diff_sha256'] = sha256(actual_diff)
        else:
            report['errors'].append('Target exceeds bounded text-diff limit; full bytes retained in package archives.')
    expected, _ = _contract()
    expected_arm = (_patched_files(expected['files']) if record['experiment']['arm'] == ARMS[1]
                    else expected['files'])
    if source['files'] != expected['files'] or arm['files'] != expected_arm:
        report['errors'].append('Captured full inventories differ from the frozen arm (drift, omission, or a change during capture).')
    report['errors'].extend(source['rejected'] + arm['rejected'])
    report['valid'] = report['valid'] and not report['errors']
    for name in ('cleanup-query-v1.patch', 'cleanup-query-v1.source-inventory.json'):
        with (folder / name).open('xb') as stream:
            stream.write((_canonical(project / 'Variants' / name)).read_bytes())
    original_manifest = workspace / 'source-manifest.json'
    if original_manifest.is_file() and not original_manifest.is_symlink():
        with (folder / 'source-manifest.json').open('xb') as stream:
            stream.write(_canonical(original_manifest).read_bytes())
    for name in ('manifest.json', 'packages-lock.json'):
        path = project / 'Packages' / name
        if path.is_file() and not path.is_symlink():
            with (folder / name).open('xb') as stream:
                stream.write(_canonical(path).read_bytes())
    _write_json(folder / 'integrity-report.json', report)
    return report
