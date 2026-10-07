#!/usr/bin/env python3
"""Standard-library validation tests; encoding smoke tests run separately with FFmpeg."""

from decimal import Decimal
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest import mock

import encode_capture as encoder


class CaptureEncoderTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.source = self.root / "capture with 'quotes'"
        self.source.mkdir()
        self.output = self.root / "review.mp4"
        self.csv(["12.000000000", "12.033333333", "12.066666666", "12.099999999"])
        self.frames()

    def csv(self, times, frames=None):
        if frames is None:
            frames = range(len(times))
        text = "frame,acquisition_seconds,simulation_seconds_at_readback\n"
        text += "".join("{},{},999.0\n".format(frame, time) for frame, time in zip(frames, times))
        (self.source / "acquisition.csv").write_text(text)

    def frames(self, extension=".png", count=4):
        for index in range(count):
            content = b"\x89PNG\r\n\x1a\n" if extension == ".png" else b"\xff\xd8\xff\xe0"
            (self.source / "frame-{:03d}{}".format(index, extension)).write_bytes(content + bytes([index]))

    def metadata(self, **changes):
        metadata = {"version": 1, "format": "png", "filename_extension": ".png",
                    "lossy": False, "alpha_preserved": True, "jpeg_quality": 0, "frame_count": 4,
                    "timestamp_source": "acquisition.csv"}
        metadata.update(changes)
        (self.source / "capture.json").write_text(json.dumps(metadata))

    def probe(self, capture=None):
        capture = capture or encoder.load_capture(self.source)
        pts = capture.pts_us + [capture.pts_us[-1] + capture.hold_us]
        return {"frames": [{"best_effort_timestamp_time": str(Decimal(value) / 1000000)} for value in pts]}

    def fake_ffmpeg(self, command, check):
        self.assertTrue(check)
        self.command = command
        self.concat = Path(command[command.index("-i") + 1]).read_text()
        Path(command[-1]).write_bytes(b"encoded fixture")
        return subprocess.CompletedProcess(command, 0)

    def test_legacy_png_and_declared_png(self):
        legacy = encoder.load_capture(self.source)
        self.assertEqual(".png", legacy.extension)
        self.assertEqual({}, legacy.metadata)
        self.assertEqual(4, len(legacy.sources))
        self.metadata()
        self.assertEqual(legacy.sources, encoder.load_capture(self.source).sources)
        # Historical PNG metadata may omit its extension; it still cannot claim JPEG.
        (self.source / "capture.json").write_text('{"format":"png"}')
        self.assertEqual(".png", encoder.load_capture(self.source).extension)

    def test_jpeg_metadata_is_authoritative_despite_stale_png_siblings(self):
        self.metadata(format="jpeg", filename_extension=".jpg", lossy=True,
                      alpha_preserved=False, jpeg_quality=95)
        self.frames(".jpg")
        capture = encoder.load_capture(self.source)
        self.assertEqual(["frame-{:03d}.jpg".format(i) for i in range(4)],
                         [source["name"] for source in capture.sources])
        self.assertNotIn(".png", encoder.concat_text(capture))
        (self.source / "frame-002.jpg").unlink()
        with self.assertRaisesRegex(ValueError, "Missing or extra"):
            encoder.load_capture(self.source)  # Never fills a JPEG hole with stale PNG.

    def test_invalid_metadata_fails(self):
        cases = [dict(format="jpeg"), dict(filename_extension=".jpeg"), dict(version=2),
                 dict(version=True), dict(filename_extension=[]), dict(lossy=True), dict(lossy=0), dict(alpha_preserved=False),
                 dict(jpeg_quality=95), dict(jpeg_quality=False), dict(frame_count=3),
                 dict(frame_count=True), dict(timestamp_source="simulation.csv")]
        for changes in cases:
            with self.subTest(changes=changes):
                self.metadata(**changes)
                with self.assertRaises(ValueError):
                    encoder.load_capture(self.source)
        for content in ("[]", "null", "{}", "{broken"):
            with self.subTest(content=content):
                (self.source / "capture.json").write_text(content)
                with self.assertRaises(ValueError):
                    encoder.load_capture(self.source)

    def test_invalid_jpeg_quality_fails(self):
        for quality in (-1, 0, 101, "95", True):
            self.metadata(format="jpeg", filename_extension=".jpg", lossy=True,
                          alpha_preserved=False, jpeg_quality=quality)
            with self.subTest(quality=quality), self.assertRaisesRegex(ValueError, "jpeg_quality"):
                encoder.load_capture(self.source)

    def test_invalid_acquisition_timestamps_fail(self):
        for times in (["0"], [], ["0", "0"], ["1", "0"], ["-1", "0"], ["0", "NaN"],
                      ["0", "Infinity"], ["-Infinity", "0"], ["0", ""], ["0", "abc"],
                      ["0", "1e999999999"], ["0", "5000000000000"]):
            with self.subTest(times=times):
                self.csv(times)
                with self.assertRaises(ValueError):
                    encoder.load_capture(self.source)

    def test_invalid_csv_headers_rows_and_sequence_fail(self):
        for frames in ([0, 2, 3, 4], [0, 0, 1, 2], [1, 2, 3, 4], [0, 1, 3, 2], [0, 1, "x", 3]):
            self.csv(["0", "0.03", "0.06", "0.09"], frames)
            with self.subTest(frames=frames), self.assertRaises(ValueError):
                encoder.load_capture(self.source)
        for contents in ("frame,simulation_seconds\n0,0\n1,1\n",
                         "frame,acquisition_seconds,acquisition_seconds\n0,0,0\n1,1,1\n",
                         "frame,acquisition_seconds\n0,0,extra\n1,1\n",
                         "frame,acquisition_seconds\n0\n1,1\n"):
            (self.source / "acquisition.csv").write_text(contents)
            with self.subTest(contents=contents), self.assertRaises(ValueError):
                encoder.load_capture(self.source)

    def test_missing_extra_and_wrong_format_frames_fail(self):
        extra = self.source / "frame-004.png"
        extra.write_bytes(b"extra")
        with self.assertRaisesRegex(ValueError, "Missing or extra"):
            encoder.load_capture(self.source)
        extra.unlink()
        last = self.source / "frame-003.png"
        last.unlink()
        with self.assertRaisesRegex(ValueError, "Missing or extra"):
            encoder.load_capture(self.source)
        last.write_bytes(b"\xff\xd8\xff fake jpg under png name")
        with self.assertRaisesRegex(ValueError, "signature"):
            encoder.load_capture(self.source)

    def test_symlink_source_frame_fails(self):
        frame = self.source / "frame-003.png"
        frame.unlink()
        frame.symlink_to(self.source / "frame-000.png")
        with self.assertRaisesRegex(ValueError, "regular file"):
            encoder.load_capture(self.source)

    def test_cumulative_rounding_avoids_drift_and_adds_exactly_one_hold(self):
        capture = encoder.load_capture(self.source)
        self.assertEqual([0, 33333, 66667, 100000], capture.pts_us)
        self.assertEqual(33333, capture.hold_us)
        text = encoder.concat_text(capture)
        self.assertEqual(5, text.count("option framerate 1000000"))
        self.assertEqual(4, text.count("duration "))
        self.assertEqual(2, text.count("frame-003.png"))
        self.assertIn("duration 0.033334", text)
        self.assertIn("'\\''quotes'\\''", text)
        self.assertLessEqual(encoder.verify_timestamps(capture, self.probe(capture)), .0005)

    def test_microsecond_collisions_fail_instead_of_dropping_frames(self):
        self.csv(["0", "0.0000001", "0.03", "0.06"])
        with self.assertRaisesRegex(ValueError, "collapse"):
            encoder.load_capture(self.source)
        self.csv(["0", "0.03", "0.0300006", "0.0300007"])
        with self.assertRaisesRegex(ValueError, "collapse"):
            encoder.load_capture(self.source)

    def test_probe_rejects_missing_extra_shifted_invalid_and_final_hold_pts(self):
        capture = encoder.load_capture(self.source)
        for index, value in ((0, "0.000001"), (1, "0.033334"), (2, "NaN"), (4, "0.166666")):
            probe = self.probe(capture)
            probe["frames"][index]["best_effort_timestamp_time"] = value
            with self.subTest(index=index, value=value), self.assertRaisesRegex(ValueError, "timestamps"):
                encoder.verify_timestamps(capture, probe)
        for probe in ({}, {"frames": [{}]}, {"frames": []}, {"frames": self.probe()["frames"][:-1]},
                      {"frames": self.probe()["frames"] + self.probe()["frames"][-1:]}):
            with self.subTest(probe=probe), self.assertRaises(ValueError):
                encoder.verify_timestamps(capture, probe)

    def test_encoding_preserves_inputs_and_reports_lossiness_and_hashes(self):
        self.metadata(format="jpeg", filename_extension=".jpg", lossy=True,
                      alpha_preserved=False, jpeg_quality=95)
        self.frames(".jpg")
        (self.source / "encoding.ffconcat").write_text("existing evidence, do not replace")
        before = {path.name: path.read_bytes() for path in self.source.iterdir()}
        with mock.patch.object(encoder.subprocess, "run", side_effect=self.fake_ffmpeg), \
                mock.patch.object(encoder.subprocess, "check_output", return_value=json.dumps(self.probe())):
            report = encoder.encode_capture(self.source, self.output)
        self.assertTrue(report["source_lossy"])
        self.assertEqual(95, report["source_jpeg_quality"])
        self.assertTrue(report["video_lossy"])
        self.assertFalse(report["video_alpha_preserved"])
        self.assertEqual(4, report["source_frames"])
        self.assertEqual(5, report["encoded_frames"])
        self.assertEqual(encoder.digest_file(self.output), report["video_sha256"])
        self.assertEqual(report, json.loads(self.output.with_suffix(".json").read_text()))
        self.assertEqual(before, {path.name: path.read_bytes() for path in self.source.iterdir()})
        self.assertIn("-n", self.command)
        self.assertNotIn("-r", self.command)
        self.assertEqual("vfr", self.command[self.command.index("-fps_mode") + 1])
        self.assertEqual("1:1000000", self.command[self.command.index("-enc_time_base") + 1])
        self.assertFalse(list(self.root.glob("spf-encode-*")))

    def test_existing_outputs_and_output_inside_capture_are_rejected(self):
        for path in (self.output, self.output.with_suffix(".json")):
            path.write_bytes(b"retain me")
            with self.subTest(path=path), mock.patch.object(encoder.subprocess, "run") as run:
                with self.assertRaisesRegex(ValueError, "already exists"):
                    encoder.encode_capture(self.source, self.output)
                run.assert_not_called()
                self.assertEqual(b"retain me", path.read_bytes())
            path.unlink()
        with self.assertRaisesRegex(ValueError, "outside"):
            encoder.encode_capture(self.source, self.source / "video.mp4")
        with self.assertRaisesRegex(ValueError, "suffix"):
            encoder.encode_capture(self.source, self.root / "output.json")
        self.output.symlink_to(self.root / "absent.mp4")
        with self.assertRaisesRegex(ValueError, "already exists"):
            encoder.encode_capture(self.source, self.output)

    def test_failed_probe_publishes_nothing(self):
        with mock.patch.object(encoder.subprocess, "run", side_effect=self.fake_ffmpeg), \
                mock.patch.object(encoder.subprocess, "check_output", return_value='{"frames":[]}'):
            with self.assertRaisesRegex(ValueError, "frame count"):
                encoder.encode_capture(self.source, self.output)
        self.assertFalse(self.output.exists())
        self.assertFalse(self.output.with_suffix(".json").exists())
        self.assertFalse(list(self.root.glob("spf-encode-*")))

    def test_publication_race_does_not_overwrite_another_report(self):
        link = os.link
        report_path = self.output.with_suffix(".json")

        def racing_link(source, destination):
            if destination == report_path:
                report_path.write_bytes(b"another invocation")
            return link(source, destination)

        with mock.patch.object(encoder.subprocess, "run", side_effect=self.fake_ffmpeg), \
                mock.patch.object(encoder.subprocess, "check_output", return_value=json.dumps(self.probe())), \
                mock.patch.object(encoder.os, "link", side_effect=racing_link):
            with self.assertRaises(FileExistsError):
                encoder.encode_capture(self.source, self.output)
        self.assertFalse(self.output.exists())
        self.assertEqual(b"another invocation", report_path.read_bytes())
        self.assertFalse(list(self.root.glob("spf-encode-*")))


if __name__ == "__main__":
    unittest.main()
