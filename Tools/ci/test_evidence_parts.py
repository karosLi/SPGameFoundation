#!/usr/bin/env python3
"""Standard-library tests: python3 -m unittest discover -s Tools/ci -p 'test_*.py'."""

import base64
import hashlib
import io
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest import mock
import zipfile

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
        self.write("Screenshots/review/frame0000.jpg", b"JPEG review fixture")
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

    def test_archive_and_manifest_prioritize_results_metadata_then_jpegs_globally(self):
        fixtures = {
            "00-raw.png": b"PNG evidence retained",
            "01-oversized.xml": b"x" * (evidence.PRIORITY_METADATA_BYTES + 1),
            "02-unknown.bin": b"binary evidence retained",
            "reports/at-limit.XML": b"x" * evidence.PRIORITY_METADATA_BYTES,
            "reports/data.tsv": b"tick\tphase\n0\twalk\n",
            "reports/perf.txt": b"performance report",
            "reports/summary.md": b"review notes",
            "z-capture/.metadata.JSON": b'{"frame_count": 2}',
            "z-capture/acquisition.csv": b"frame,seconds\n0,0\n1,0.016\n",
            "z-capture/empty.log": b"",
            "z-capture/frame0000.JPG": b"JPEG review fixture 0",
            "z-capture/frame0001.jpeg": b"JPEG review fixture 1",
            "z-capture/frames.ffconcat": b"ffconcat version 1.0\nfile frame0000.JPG\nduration 0.016\n",
            "z-capture/large.jpg": b"j" * (evidence.PRIORITY_METADATA_BYTES + 1),
            "z-results.xml": b"<test-run result='Passed'/>",
        }
        # Deliberately oppose both lexical and priority order during creation.
        for name in reversed(sorted(fixtures)):
            self.write(name, fixtures[name])
        expected = ["Artifacts/" + name for name in (
            "z-results.xml",
            "reports/at-limit.XML", "reports/data.tsv", "reports/perf.txt", "reports/summary.md",
            "z-capture/.metadata.JSON", "z-capture/acquisition.csv", "z-capture/empty.log",
            "z-capture/frames.ffconcat",
            "z-capture/frame0000.JPG", "z-capture/frame0001.jpeg", "z-capture/large.jpg",
            "00-raw.png", "01-oversized.xml", "02-unknown.bin",
        )]
        manifest = evidence.package_evidence(self.source, self.parts)
        self.assertEqual(expected, [entry["path"] for entry in manifest["files"]])
        combined = b"".join((self.parts / "part{:02d}".format(index) / part["name"]).read_bytes()
                            for index, part in enumerate(manifest["parts"]))
        with zipfile.ZipFile(io.BytesIO(combined)) as zipped:
            self.assertEqual(expected, zipped.namelist())
            self.assertEqual(expected, [info.filename for info in sorted(
                zipped.infolist(), key=lambda info: info.header_offset)])
            for name, content in fixtures.items():
                self.assertEqual(content, zipped.read("Artifacts/" + name))
        # Packaging identical evidence again must preserve archive/part/file hashes and order.
        repeated = evidence.package_evidence(self.source, self.root / "parts-again")
        self.assertEqual(manifest, repeated)
        evidence.restore_evidence(self.parts, self.restored)
        restored = {p.relative_to(self.restored / "Artifacts").as_posix(): p.read_bytes()
                    for p in (self.restored / "Artifacts").rglob("*") if p.is_file()}
        self.assertEqual(fixtures, restored)

    def test_only_exact_grip_png_groups_precede_jpegs_and_restore_unchanged(self):
        fixtures = {
            "editmode-results.xml": b"<test-run passed='1'/>",
            "Screenshots/WeaponMotion/blade-grip-isolated-gpu/capture.json": b"{}",
            "Screenshots/WeaponMotion/blade-grip-isolated-gpu/frame0000.png": b"lossless GPU frame",
            "Screenshots/WeaponMotion/blade-grip-isolated-fallback/frame0000.PNG": b"lossless fallback frame",
            "Screenshots/00-review.jpg": b"ordinary review frame",
            "Screenshots/WeaponMotion/blade-grip-isolated-gpu-extra/frame0000.png": b"other group",
            "Screenshots/WeaponMotion/blade-grip-isolated-gpu/nested/frame0000.png": b"nested group",
            "Screenshots/ordinary.png": b"ordinary PNG",
        }
        for name in reversed(sorted(fixtures)):
            self.write(name, fixtures[name])
        expected = ["Artifacts/" + name for name in (
            "editmode-results.xml",
            "Screenshots/WeaponMotion/blade-grip-isolated-gpu/capture.json",
            "Screenshots/WeaponMotion/blade-grip-isolated-fallback/frame0000.PNG",
            "Screenshots/WeaponMotion/blade-grip-isolated-gpu/frame0000.png",
            "Screenshots/00-review.jpg",
            "Screenshots/WeaponMotion/blade-grip-isolated-gpu-extra/frame0000.png",
            "Screenshots/WeaponMotion/blade-grip-isolated-gpu/nested/frame0000.png",
            "Screenshots/ordinary.png",
        )]
        manifest = evidence.package_evidence(self.source, self.parts)
        self.assertEqual(expected, [entry["path"] for entry in manifest["files"]])
        combined = b"".join((self.parts / "part{:02d}".format(index) / part["name"]).read_bytes()
                            for index, part in enumerate(manifest["parts"]))
        with zipfile.ZipFile(io.BytesIO(combined)) as zipped:
            self.assertEqual(expected, zipped.namelist())
            for name, content in fixtures.items():
                self.assertEqual(content, zipped.read("Artifacts/" + name))
        evidence.restore_evidence(self.parts, self.restored)
        for name, content in fixtures.items():
            self.assertEqual(content, (self.source / name).read_bytes())
            self.assertEqual(content, (self.restored / "Artifacts" / name).read_bytes())

    def test_large_native_results_precede_metadata_and_media_without_losing_evidence(self):
        # Real native NUnit XML can exceed the small-metadata limit. Random base64
        # keeps valid XML large even after ZIP compression; media crosses part00.
        results = b"<test-run><output>" + base64.b64encode(os.urandom(1024 * 1024)) + b"</output></test-run>"
        self.assertGreater(len(results), evidence.PRIORITY_METADATA_BYTES)
        fixtures = {
            "00-summary.json": b'{"failed": 1}',
            "01-unrelated.xml": b"x" * (evidence.PRIORITY_METADATA_BYTES + 1),
            "Screenshots/frame.jpg": b"JPEG review fixture",
            "Screenshots/frame.png": os.urandom(evidence.PART_BYTES),
            "editmode-results.xml": results,
            "nested/PlayMode-noburst-results.XML": results,
        }
        for name in reversed(sorted(fixtures)):
            self.write(name, fixtures[name])
        expected = ["Artifacts/" + name for name in (
            "editmode-results.xml", "nested/PlayMode-noburst-results.XML",
            "00-summary.json", "Screenshots/frame.jpg", "01-unrelated.xml", "Screenshots/frame.png",
        )]
        manifest = evidence.package_evidence(self.source, self.parts)
        self.assertEqual(expected, [entry["path"] for entry in manifest["files"]])
        self.assertEqual(2, len(manifest["parts"]))
        combined = b"".join((self.parts / "part{:02d}".format(index) / part["name"]).read_bytes()
                            for index, part in enumerate(manifest["parts"]))
        with zipfile.ZipFile(io.BytesIO(combined)) as zipped:
            self.assertEqual(expected, zipped.namelist())
            self.assertEqual(expected, [info.filename for info in sorted(
                zipped.infolist(), key=lambda info: info.header_offset)])
            # These complete result members fit in part00; this is fixture-specific.
            self.assertLess(zipped.getinfo("Artifacts/00-summary.json").header_offset, evidence.PART_BYTES)
            for name, content in fixtures.items():
                self.assertEqual(content, zipped.read("Artifacts/" + name))
        evidence.restore_evidence(self.parts, self.restored)
        for name, content in fixtures.items():
            self.assertEqual(content, (self.source / name).read_bytes())
            self.assertEqual(content, (self.restored / "Artifacts" / name).read_bytes())

    def test_result_priority_uses_result_filename_not_arbitrary_xml_size(self):
        for name in ("editmode-results.xml", "playmode-results.xml", "editmode-noburst-results.xml",
                     "nested/PlayMode-noburst-results.XML", "font-probe-missing-canvas-results.xml"):
            path = "Artifacts/" + name
            for size in (0, evidence.PRIORITY_METADATA_BYTES, evidence.PART_BYTES + 1):
                with self.subTest(path=path, size=size):
                    self.assertEqual((-1, path), evidence.evidence_priority(path, size))
        for name in ("results.xml", "report.xml", "editmode-results.xml.bin", "editmode-results.xml/other.xml"):
            path = "Artifacts/" + name
            self.assertEqual((2, path), evidence.evidence_priority(path, evidence.PRIORITY_METADATA_BYTES + 1))

    def test_first_result_can_span_parts_and_still_restores_completely(self):
        results = b"<test-run><output>" + base64.b64encode(os.urandom(3000)) + b"</output></test-run>"
        self.write("editmode-results.xml", results)
        self.write("00-summary.json", b'{"failed": 1}')
        with mock.patch.object(evidence, "PART_BYTES", 1024):
            manifest = evidence.package_evidence(self.source, self.parts)
            self.assertEqual("Artifacts/editmode-results.xml", manifest["files"][0]["path"])
            combined = b"".join(path.read_bytes() for path in sorted(self.parts.rglob("*.part*")))
            with zipfile.ZipFile(io.BytesIO(combined)) as zipped:
                self.assertGreater(zipped.getinfo("Artifacts/editmode-results.xml").compress_size, evidence.PART_BYTES)
            evidence.restore_evidence(self.parts, self.restored)
        self.assertEqual(results, (self.restored / "Artifacts/editmode-results.xml").read_bytes())
        self.assertEqual(b'{"failed": 1}', (self.restored / "Artifacts/00-summary.json").read_bytes())

    def test_priority_size_boundary_applies_only_to_metadata(self):
        limit = evidence.PRIORITY_METADATA_BYTES
        self.assertEqual(1024 * 1024, limit)
        for suffix in (".xml", ".csv", ".tsv", ".json", ".txt", ".md", ".log", ".ffconcat"):
            for case in (suffix, suffix.upper()):
                path = "Artifacts/report" + case
                with self.subTest(path=path):
                    self.assertEqual((0, path), evidence.evidence_priority(path, 0))
                    self.assertEqual((0, path), evidence.evidence_priority(path, limit))
                    self.assertEqual((2, path), evidence.evidence_priority(path, limit + 1))
        for suffix in (".jpg", ".jpeg", ".JPG", ".JPEG"):
            path = "Artifacts/review" + suffix
            self.assertEqual((1, path), evidence.evidence_priority(path, limit + 1))
        for name in ("frame.png", "frame.PNG", "report.xml.bin", "report", "capture.raw"):
            path = "Artifacts/" + name
            self.assertEqual((2, path), evidence.evidence_priority(path, 0))

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
        content = os.urandom(40 * 1024)
        self.write("profiler/large.raw", content)
        with mock.patch.object(evidence, "PART_BYTES", 1024):
            with self.assertRaisesRegex(ValueError, "limit is 40960 bytes \\(40 parts\\).*No evidence was dropped"):
                evidence.package_evidence(self.source, self.parts)
        self.assertFalse(self.parts.exists())
        self.assertEqual({"Artifacts"}, {p.name for p in self.root.iterdir()})
        self.assertEqual(content, (self.source / "profiler/large.raw").read_bytes())
        # Verify this bounded fixture really needs exactly 41 parts, including ZIP
        # overhead. Only the test overrides the cap; the production default is 40.
        with mock.patch.object(evidence, "PART_BYTES", 1024), mock.patch.object(evidence, "MAX_PARTS", 41):
            manifest = evidence.package_evidence(self.source, self.parts)
        self.assertEqual(41, len(manifest["parts"]))

    def test_legacy_eight_and_thirty_two_part_manifests_still_restore(self):
        self.package_small()
        for limit in (8, 32):
            with self.subTest(max_parts=limit):
                self.alter_manifest(lambda manifest: manifest.update(max_parts=limit))
                restored = self.root / "restored-{}".format(limit)
                with mock.patch.object(evidence, "PART_BYTES", 1024):
                    evidence.restore_evidence(self.parts, restored)
                self.assertEqual((self.source / "profiler/story.raw").read_bytes(),
                                 (restored / "Artifacts/profiler/story.raw").read_bytes())

    def test_manifest_cannot_claim_unbounded_capacity(self):
        self.package_small()
        for limit in (0, 9, 31, 33, 39, 41, 9999):
            with self.subTest(max_parts=limit):
                self.alter_manifest(lambda manifest: manifest.update(max_parts=limit))
                with mock.patch.object(evidence, "PART_BYTES", 1024):
                    with self.assertRaisesRegex(ValueError, "Unsupported evidence manifest"):
                        evidence.restore_evidence(self.parts, self.restored)
                self.assertFalse(self.restored.exists())

    def test_forty_parts_restore_and_legacy_limits_remain_enforced(self):
        self.assertEqual(40, evidence.MAX_PARTS)
        self.assertEqual(16 * 1024 * 1024, evidence.PART_BYTES)
        self.write("captures/continuous.raw", os.urandom(39 * 1024))
        with mock.patch.object(evidence, "PART_BYTES", 1024):
            manifest = evidence.package_evidence(self.source, self.parts)
            self.assertEqual(40, manifest["max_parts"])
            self.assertEqual(40, len(manifest["parts"]))
            self.assertEqual(["evidence.zip.part{:02d}".format(index) for index in range(40)],
                             [part["name"] for part in manifest["parts"]])
            for part in manifest["parts"]:
                self.assertLessEqual(part["size"], 1024)
            for limit in (8, 32):
                with self.subTest(max_parts=limit):
                    self.alter_manifest(lambda manifest: manifest.update(max_parts=limit))
                    with self.assertRaisesRegex(ValueError, "Invalid evidence part count"):
                        evidence.restore_evidence(self.parts, self.restored)
                    self.assertFalse(self.restored.exists())
            self.alter_manifest(lambda manifest: manifest.update(max_parts=40))
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
