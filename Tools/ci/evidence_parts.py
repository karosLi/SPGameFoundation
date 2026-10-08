#!/usr/bin/env python3
"""Package Unity's dedicated Artifacts directory and restore verified bounded parts."""

import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import shutil
import stat
import tempfile
import zipfile


PART_BYTES = 16 * 1024 * 1024
MAX_PARTS = 40
BLOCK_BYTES = 1024 * 1024
MAX_MANIFEST_BYTES = 1024 * 1024
PRIORITY_METADATA_BYTES = 1024 * 1024
METADATA_SUFFIXES = frozenset((".xml", ".csv", ".tsv", ".json", ".txt", ".md", ".log", ".ffconcat"))
MANIFEST_NAME = "evidence-manifest.json"
ARCHIVE_NAME = "evidence.zip"


def digest_file(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(BLOCK_BYTES), b""):
            digest.update(block)
    return digest.hexdigest()


def evidence_path(name):
    if not isinstance(name, str):
        raise ValueError("Invalid evidence path: {!r}".format(name))
    path = PurePosixPath(name)
    if (path.is_absolute() or len(path.parts) < 2
            or path.parts[0] != "Artifacts" or ".." in path.parts
            or "\\" in name or ":" in name or str(path) != name):
        raise ValueError("Invalid evidence path: {!r}".format(name))
    return path


def new_destination(path):
    path = Path(path).absolute()
    if path.exists() or path.is_symlink():
        raise ValueError("Output already exists; choose a new directory: {}".format(path))
    if not path.parent.is_dir():
        raise ValueError("Output parent directory does not exist: {}".format(path.parent))
    return path


def walk_error(error):
    # os.walk otherwise silently skips unreadable subdirectories.
    raise error


def evidence_priority(relative, size):
    """Order test results, small metadata, JPEG reviews, then other evidence.

    Native runners name their XML reports *-results.xml, including tagged retries.
    Results and JPEGs have no size cutoff; other metadata has an inclusive 1 MiB
    per-file limit. Filename/suffix matching is case-insensitive, with the exact
    POSIX path breaking ties. Complete ZIP members can span binary parts; all
    parts are still required for verified restoration.
    """
    path = PurePosixPath(relative)
    suffix = path.suffix.lower()
    if path.name.lower().endswith("-results.xml"):
        priority = -1
    elif suffix in METADATA_SUFFIXES and size <= PRIORITY_METADATA_BYTES:
        priority = 0
    elif suffix in (".jpg", ".jpeg"):
        priority = 1
    else:
        priority = 2
    return priority, relative


def package_evidence(source, output):
    source = Path(source).absolute()
    if source.name != "Artifacts" or source.is_symlink() or not source.is_dir():
        raise ValueError("Source must be a real, dedicated Artifacts directory")
    source = source.resolve()
    output = new_destination(output)
    if source == output.resolve() or source in output.resolve().parents:
        raise ValueError("Packaging output must be outside Artifacts")

    # Only this invocation's temporary directory is cleaned up, including on failure.
    with tempfile.TemporaryDirectory(prefix="spf-evidence-", dir=output.parent) as temporary:
        working = Path(temporary)
        archive = working / ARCHIVE_NAME
        files = []
        candidates = []
        for directory, directories, names in os.walk(source, followlinks=False, onerror=walk_error):
            directories.sort()
            for name in sorted(directories + names):
                if (Path(directory) / name).is_symlink():
                    raise ValueError("Symlinks are not evidence: {}".format(Path(directory) / name))
            for name in sorted(names):
                path = Path(directory) / name
                info = path.stat()
                if not stat.S_ISREG(info.st_mode):
                    raise ValueError("Non-regular evidence file: {}".format(path))
                relative = "Artifacts/" + path.relative_to(source).as_posix()
                evidence_path(relative)
                candidates.append((relative, path, info.st_size))
        candidates.sort(key=lambda entry: evidence_priority(entry[0], entry[2]))
        with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_DEFLATED) as zipped:
            for relative, path, _ in candidates:
                # Recheck after collecting/sorting rather than trusting the earlier walk.
                if path.is_symlink():
                    raise ValueError("Symlinks are not evidence: {}".format(path))
                if not stat.S_ISREG(path.stat().st_mode):
                    raise ValueError("Non-regular evidence file: {}".format(path))
                digest = hashlib.sha256()
                size = 0
                with path.open("rb") as incoming, zipped.open(relative, "w", force_zip64=True) as outgoing:
                    for block in iter(lambda: incoming.read(BLOCK_BYTES), b""):
                        outgoing.write(block)
                        digest.update(block)
                        size += len(block)
                files.append({"path": relative, "size": size, "sha256": digest.hexdigest()})

        if not files:
            raise ValueError("Artifacts contains no evidence files")
        size = archive.stat().st_size
        if size > PART_BYTES * MAX_PARTS:
            raise ValueError("Evidence ZIP is {} bytes; limit is {} bytes ({} parts). "
                             "No evidence was dropped or published.".format(
                                 size, PART_BYTES * MAX_PARTS, MAX_PARTS))
        parts_directory = working / "parts"
        parts_directory.mkdir()
        parts = []
        with archive.open("rb") as stream:
            for index in range(MAX_PARTS):
                block = stream.read(PART_BYTES)
                if not block:
                    break
                name = "{}.part{:02d}".format(ARCHIVE_NAME, index)
                folder = parts_directory / "part{:02d}".format(index)
                folder.mkdir()
                (folder / name).write_bytes(block)
                parts.append({"name": name, "size": len(block),
                              "sha256": hashlib.sha256(block).hexdigest()})
        manifest = {"version": 1, "part_size_bytes": PART_BYTES, "max_parts": MAX_PARTS,
                    "archive": {"name": ARCHIVE_NAME, "size": size, "sha256": digest_file(archive)},
                    "parts": parts, "files": files}
        encoded = json.dumps(manifest, indent=2, ensure_ascii=True) + "\n"
        if len(encoded.encode("utf-8")) > MAX_MANIFEST_BYTES:
            raise ValueError("Evidence manifest exceeds 1 MiB; no evidence was dropped or published")
        # A manifest is in every separately downloadable artifact.
        for folder in sorted(parts_directory.iterdir()):
            (folder / MANIFEST_NAME).write_text(encoded, encoding="utf-8")
        parts_directory.rename(output)
    return manifest


def restore_evidence(parts_directory, output):
    parts_directory = Path(parts_directory)
    output = new_destination(output)
    manifests = sorted(parts_directory.rglob(MANIFEST_NAME))
    if not manifests:
        raise ValueError("No {} found".format(MANIFEST_NAME))
    encoded = manifests[0].read_bytes()
    if len(encoded) > MAX_MANIFEST_BYTES:
        raise ValueError("Evidence manifest exceeds 1 MiB")
    if any(path.read_bytes() != encoded for path in manifests[1:]):
        raise ValueError("Manifests disagree; do not mix different CI runs")
    manifest = json.loads(encoded)
    if (manifest["version"] != 1 or manifest["part_size_bytes"] != PART_BYTES
            or manifest["max_parts"] not in (8, 32, MAX_PARTS)
            or manifest["archive"]["name"] != ARCHIVE_NAME):
        raise ValueError("Unsupported evidence manifest")
    parts = manifest["parts"]
    if not 1 <= len(parts) <= manifest["max_parts"]:
        raise ValueError("Invalid evidence part count")
    expected_names = ["{}.part{:02d}".format(ARCHIVE_NAME, index) for index in range(len(parts))]
    if [part["name"] for part in parts] != expected_names:
        raise ValueError("Evidence parts must be complete and ordered from part00")
    found = list(parts_directory.rglob(ARCHIVE_NAME + ".part*"))
    if sorted(path.name for path in found) != expected_names:
        raise ValueError("Missing, duplicate, or unexpected evidence part")
    paths = {path.name: path for path in found}
    inventory = {entry["path"]: entry for entry in manifest["files"]}
    if len(inventory) != len(manifest["files"]):
        raise ValueError("Duplicate file in evidence manifest")
    for name in inventory:
        evidence_path(name)

    with tempfile.TemporaryDirectory(prefix="spf-restore-", dir=output.parent) as temporary:
        working = Path(temporary)
        archive = working / ARCHIVE_NAME
        with archive.open("wb") as combined:
            for part in parts:
                path = paths[part["name"]]
                if not 0 < part["size"] <= PART_BYTES or path.stat().st_size != part["size"]:
                    raise ValueError("Evidence part size mismatch: {}".format(path.name))
                if digest_file(path) != part["sha256"]:
                    raise ValueError("Evidence part SHA-256 mismatch: {}".format(path.name))
                with path.open("rb") as incoming:
                    shutil.copyfileobj(incoming, combined, BLOCK_BYTES)
        if (archive.stat().st_size != manifest["archive"]["size"]
                or digest_file(archive) != manifest["archive"]["sha256"]):
            raise ValueError("Evidence ZIP size or SHA-256 mismatch")
        restored = working / "restored"
        restored.mkdir()
        with zipfile.ZipFile(archive) as zipped:
            if sorted(zipped.namelist()) != sorted(inventory):
                raise ValueError("Evidence ZIP files do not match the manifest")
            for info in zipped.infolist():
                relative = evidence_path(info.filename)
                if stat.S_ISLNK(info.external_attr >> 16):
                    raise ValueError("Evidence ZIP must not contain symlinks")
                entry = inventory[info.filename]
                if info.file_size != entry["size"]:
                    raise ValueError("Evidence file size mismatch: {}".format(info.filename))
                target = restored.joinpath(*relative.parts)
                target.parent.mkdir(parents=True, exist_ok=True)
                with zipped.open(info) as incoming, target.open("wb") as outgoing:
                    shutil.copyfileobj(incoming, outgoing, BLOCK_BYTES)
                if digest_file(target) != entry["sha256"]:
                    raise ValueError("Evidence file SHA-256 mismatch: {}".format(info.filename))
        restored.rename(output)
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    package = commands.add_parser("package", help="Create at most 40 bounded 16 MiB evidence parts")
    package.add_argument("--source", default="Artifacts")
    package.add_argument("--output", required=True)
    restore = commands.add_parser("restore", help="Verify, concatenate, and extract downloaded parts")
    restore.add_argument("--parts", required=True)
    restore.add_argument("--output", required=True)
    arguments = parser.parse_args()
    try:
        if arguments.command == "package":
            manifest = package_evidence(arguments.source, arguments.output)
        else:
            manifest = restore_evidence(arguments.parts, arguments.output)
    except (OSError, ValueError, KeyError, TypeError, zipfile.BadZipFile) as error:
        parser.exit(1, "Evidence error: {}\n".format(error))
    print("{}: {} files, {} parts, {} ZIP bytes; SHA-256 {}".format(
        arguments.command, len(manifest["files"]), len(manifest["parts"]),
        manifest["archive"]["size"], manifest["archive"]["sha256"]))


if __name__ == "__main__":
    main()
