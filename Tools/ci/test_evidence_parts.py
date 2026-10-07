#!/usr/bin/env python3
"""Standard-library tests: python3 -m unittest discover -s Tools/ci -p 'test_*.py'."""

import hashlib
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest import mock

import evidence_parts as evidence


class EvidencePartsTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.source = self.root / "Artifacts"
        self.source.mkdir()
        self.parts = self.root / "parts"
        self.restored = self.root / "restored"

    def write(self, name, content):
        path = self.source / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(content)

    def package_small(self):
        self.write("profiler/story.raw", os.urandom(3000))
        with mock.patch.object(evidence, "PART_BYTES", 1024):
            return evidence.package_evidence(self.source, self.parts)

    def alter_manifest(self, change):
        for path in self.parts.rglob(evidence.MANIFEST_NAME):
            manifest = json.loads(path.read_text())
            change(manifest)
            path.write_text(json.dumps(manifest))

    def test_round_trip_all_evidence_and_real_part_size_bound(self):
        # Incompressible data exercises the actual 16 MiB boundary, including ZIP overhead.
        self.write("profiler/story.raw", os.urandom(evidence.PART_BYTES + 1024))
        self.write("Screenshots/portrait.png", b"screenshot fixture")
        self.write("editmode-results.xml", b"<test-run result='Passed'/>")
        self.write("playmode.log", b"Unity output\n")
        self.write("perf-story.txt", b"Story GC allocation samples\n")
        self.write("nested/.capture.json", b'{"samples": [0, 16]}')
        (self.root / "Library").mkdir()
        (self.root / "Library" / "excluded.cache").write_bytes(b"not test evidence")
        manifest = evidence.package_evidence(self.source, self.parts)
        self.assertEqual(2, len(manifest["parts"]))
        self.assertLessEqual(len(manifest["parts"]), evidence.MAX_PARTS)
        for index, part in enumerate(manifest["parts"]):
            folder = self.parts / "part{:02d}".format(index)
            self.assertEqual({part["name"], evidence.MANIFEST_NAME}, {p.name for p in folder.iterdir()})
            self.assertLessEqual(part["size"], 16 * 1024 * 1024)
            self.assertEqual(part["sha256"], evidence.digest_file(folder / part["name"]))
        # GitHub downloads can be extracted into arbitrarily named directories.
        (self.parts / "part00").rename(self.parts / "unity-test-results-self-hosted-part00")
        evidence.restore_evidence(self.parts, self.restored)
        originals = {p.relative_to(self.source): p.read_bytes() for p in self.source.rglob("*") if p.is_file()}
        restored = {p.relative_to(self.restored / "Artifacts"): p.read_bytes()
                    for p in (self.restored / "Artifacts").rglob("*") if p.is_file()}
        self.assertEqual(originals, restored)
        self.assertEqual(len(originals), len(manifest["files"]))
        self.assertFalse((self.restored / "Library").exists())

    def test_missing_part_fails_without_partial_output(self):
        self.package_small()
        next(self.parts.rglob("*.part01")).unlink()
        with mock.patch.object(evidence, "PART_BYTES", 1024):
            with self.assertRaisesRegex(ValueError, "Missing, duplicate"):
                evidence.restore_evidence(self.parts, self.restored)
        self.assertFalse(self.restored.exists())

    def test_corrupt_part_fails_sha256_check(self):
        self.package_small()
        path = next(self.parts.rglob("*.part01"))
        content = bytearray(path.read_bytes())
        content[0] ^= 1
        path.write_bytes(content)
        with mock.patch.object(evidence, "PART_BYTES", 1024):
            with self.assertRaisesRegex(ValueError, "part SHA-256 mismatch"):
                evidence.restore_evidence(self.parts, self.restored)
        self.assertFalse(self.restored.exists())

    def test_archive_and_file_hashes_are_verified_separately(self):
        self.package_small()
        self.alter_manifest(lambda manifest: manifest["archive"].update(sha256="0" * 64))
        with mock.patch.object(evidence, "PART_BYTES", 1024):
            with self.assertRaisesRegex(ValueError, "ZIP size or SHA-256 mismatch"):
                evidence.restore_evidence(self.parts, self.restored)
        combined = b"".join(path.read_bytes() for path in sorted(self.parts.rglob("*.part*")))
        self.alter_manifest(lambda manifest: manifest["archive"].update(sha256=hashlib.sha256(combined).hexdigest()))
        self.alter_manifest(lambda manifest: manifest["files"][0].update(sha256="0" * 64))
        with mock.patch.object(evidence, "PART_BYTES", 1024):
            with self.assertRaisesRegex(ValueError, "file SHA-256 mismatch"):
                evidence.restore_evidence(self.parts, self.restored)
        self.assertFalse(self.restored.exists())

    def test_oversize_archive_fails_without_dropping_files(self):
        self.write("profiler/large.raw", os.urandom((evidence.MAX_PARTS + 1) * 1024))
        with mock.patch.object(evidence, "PART_BYTES", 1024):
            with self.assertRaisesRegex(ValueError, "limit is 32768 bytes.*No evidence was dropped"):
                evidence.package_evidence(self.source, self.parts)
        self.assertFalse(self.parts.exists())
        self.assertEqual({"Artifacts"}, {p.name for p in self.root.iterdir()})
        self.assertEqual((evidence.MAX_PARTS + 1) * 1024, (self.source / "profiler/large.raw").stat().st_size)

    def test_legacy_eight_part_manifest_still_restores(self):
        self.package_small()
        self.alter_manifest(lambda manifest: manifest.update(max_parts=8))
        with mock.patch.object(evidence, "PART_BYTES", 1024):
            evidence.restore_evidence(self.parts, self.restored)
        self.assertEqual((self.source / "profiler/story.raw").read_bytes(),
                         (self.restored / "Artifacts/profiler/story.raw").read_bytes())

    def test_manifest_cannot_claim_unbounded_capacity(self):
        self.package_small()
        self.alter_manifest(lambda manifest: manifest.update(max_parts=9999))
        with mock.patch.object(evidence, "PART_BYTES", 1024):
            with self.assertRaisesRegex(ValueError, "Unsupported evidence manifest"):
                evidence.restore_evidence(self.parts, self.restored)
        self.assertFalse(self.restored.exists())

    def test_new_capacity_retains_more_than_eight_parts(self):
        self.write("captures/continuous.raw", os.urandom(9 * 1024))
        with mock.patch.object(evidence, "PART_BYTES", 1024):
            manifest = evidence.package_evidence(self.source, self.parts)
            self.assertGreater(len(manifest["parts"]), 8)
            self.assertLessEqual(len(manifest["parts"]), evidence.MAX_PARTS)
            evidence.restore_evidence(self.parts, self.restored)
        self.assertEqual((self.source / "captures/continuous.raw").read_bytes(),
                         (self.restored / "Artifacts/captures/continuous.raw").read_bytes())

    def test_manifest_size_is_bounded_and_empty_evidence_fails(self):
        with self.assertRaisesRegex(ValueError, "no evidence files"):
            evidence.package_evidence(self.source, self.parts)
        self.write("playmode.log", b"Unity output")
        with mock.patch.object(evidence, "MAX_MANIFEST_BYTES", 1):
            with self.assertRaisesRegex(ValueError, "manifest exceeds"):
                evidence.package_evidence(self.source, self.parts)
        self.assertFalse(self.parts.exists())

    def test_symlink_cannot_pull_in_outside_files(self):
        external = self.root / "outside.txt"
        external.write_bytes(b"outside evidence")
        (self.source / "link").symlink_to(external)
        with self.assertRaisesRegex(ValueError, "Symlinks are not evidence"):
            evidence.package_evidence(self.source, self.parts)
        self.assertFalse(self.parts.exists())

    def test_output_cannot_be_inside_artifacts_or_overwrite_existing_data(self):
        with self.assertRaisesRegex(ValueError, "outside Artifacts"):
            evidence.package_evidence(self.source, self.source / "parts")
        self.parts.mkdir()
        retained = self.parts / "keep.txt"
        retained.write_bytes(b"keep me")
        with self.assertRaisesRegex(ValueError, "already exists"):
            evidence.package_evidence(self.source, self.parts)
        self.assertEqual(b"keep me", retained.read_bytes())

    def test_different_run_manifests_and_duplicate_parts_fail(self):
        self.package_small()
        path = next(self.parts.rglob(evidence.MANIFEST_NAME))
        original = path.read_bytes()
        path.write_bytes(original + b"\n")
        with self.assertRaisesRegex(ValueError, "Manifests disagree"):
            evidence.restore_evidence(self.parts, self.restored)
        path.write_bytes(original)
        duplicate = self.parts / "duplicate"
        duplicate.mkdir()
        part = next(self.parts.rglob("*.part00"))
        (duplicate / part.name).write_bytes(part.read_bytes())
        with mock.patch.object(evidence, "PART_BYTES", 1024):
            with self.assertRaisesRegex(ValueError, "Missing, duplicate"):
                evidence.restore_evidence(self.parts, self.restored)

    def test_unsafe_manifest_paths_fail_before_extraction(self):
        self.package_small()
        self.alter_manifest(lambda manifest: manifest["files"][0].update(path="Artifacts/../../escape"))
        with mock.patch.object(evidence, "PART_BYTES", 1024):
            with self.assertRaisesRegex(ValueError, "Invalid evidence path"):
                evidence.restore_evidence(self.parts, self.restored)
        self.assertFalse(self.restored.exists())


if __name__ == "__main__":
    unittest.main()
