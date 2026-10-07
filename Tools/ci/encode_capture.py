#!/usr/bin/env python3
"""Encode buffered Unity captures at measured 1x acquisition PTS, after capture.

Python uses only the standard library. Encoding requires FFmpeg with libx264 and
ffprobe on PATH. Source evidence is read-only; output MP4 and JSON must be new.
"""

import argparse
import csv
from dataclasses import dataclass
from decimal import Decimal, InvalidOperation, ROUND_HALF_EVEN
import hashlib
import json
import os
from pathlib import Path
import statistics
import subprocess
import tempfile


MICROSECONDS = Decimal(1000000)
MAX_TIMESTAMP = Decimal((1 << 63) - 1) / MICROSECONDS
FORMATS = {".png": "png", ".jpg": "jpeg"}


@dataclass
class Capture:
    directory: Path
    metadata: dict
    extension: str
    times: list
    pts_us: list
    hold_us: int
    sources: list


def digest_file(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def load_metadata(directory):
    path = directory / "capture.json"
    if not path.exists():
        return {}, ".png"  # Historical captures have no metadata.
    metadata = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(metadata, dict) or not metadata:
        raise ValueError("capture.json must be a nonempty object")
    extension = metadata.get("filename_extension", ".png")
    if (not isinstance(extension, str) or extension not in FORMATS or
            metadata.get("format") != FORMATS[extension]):
        raise ValueError("Unsupported or mismatched capture format/filename_extension")
    if "version" in metadata and (type(metadata["version"]) is not int or metadata["version"] != 1):
        raise ValueError("Unsupported capture metadata version")
    for name, expected in (("lossy", extension == ".jpg"), ("alpha_preserved", extension == ".png")):
        if name in metadata and metadata[name] is not expected:
            raise ValueError("Capture {} contradicts its format".format(name))
    if "jpeg_quality" in metadata:
        quality = metadata["jpeg_quality"]
        if (type(quality) is not int or
                not (1 <= quality <= 100 if extension == ".jpg" else quality == 0)):
            raise ValueError("Invalid jpeg_quality for capture format")
    if metadata.get("timestamp_source", "acquisition.csv") != "acquisition.csv":
        raise ValueError("Unsupported timestamp_source")
    return metadata, extension


def measured_timestamps(path):
    times = []
    with path.open(encoding="utf-8", newline="") as stream:
        reader = csv.DictReader(stream)
        fields = reader.fieldnames or []
        if (not {"frame", "acquisition_seconds"}.issubset(fields) or
                len(fields) != len(set(fields))):
            raise ValueError("Missing or duplicate acquisition.csv columns")
        for index, row in enumerate(reader):
            try:
                frame = int(row["frame"])
                value = Decimal(row["acquisition_seconds"])
            except (TypeError, ValueError, InvalidOperation) as error:
                raise ValueError("Invalid acquisition.csv row {}".format(index)) from error
            if None in row or frame != index:
                raise ValueError("Source frame sequence is incomplete or out of order")
            if (not value.is_finite() or value < 0 or value > MAX_TIMESTAMP or
                    (times and value <= times[-1])):
                raise ValueError("Need finite, nonnegative, strictly increasing timestamps within int64 microseconds")
            times.append(value)
    if len(times) < 2:
        raise ValueError("Need at least two measured timestamps")
    return times


def microseconds(seconds):
    return int((seconds * MICROSECONDS).to_integral_value(rounding=ROUND_HALF_EVEN))


def load_capture(directory):
    directory = Path(directory).resolve()
    metadata, extension = load_metadata(directory)
    times = measured_timestamps(directory / "acquisition.csv")
    if "frame_count" in metadata and (type(metadata["frame_count"]) is not int or
                                      metadata["frame_count"] != len(times)):
        raise ValueError("Metadata frame_count disagrees with acquisition.csv")
    # Round cumulative offsets, not each interval: interval rounding drifts.
    pts_us = [microseconds(value - times[0]) for value in times]
    hold_us = microseconds(times[-1] - times[-2])
    if pts_us[-1] + hold_us > (1 << 63) - 1:
        raise ValueError("Final hold exceeds int64 microsecond timestamps")
    if hold_us <= 0 or any(b <= a for a, b in zip(pts_us, pts_us[1:])):
        raise ValueError("Measured timestamps collapse at microsecond precision")
    expected = {"frame-{:03d}{}".format(index, extension) for index in range(len(times))}
    actual = {path.name for path in directory.glob("frame-*" + extension)}
    if actual != expected:
        raise ValueError("Missing or extra frames for declared filename_extension")
    sources = []
    for index in range(len(times)):
        path = directory / "frame-{:03d}{}".format(index, extension)
        if path.is_symlink() or not path.is_file():
            raise ValueError("Source frame must be a regular file: {}".format(path))
        with path.open("rb") as stream:
            header = stream.read(8)
        if not (header == b"\x89PNG\r\n\x1a\n" if extension == ".png" else header.startswith(b"\xff\xd8\xff")):
            raise ValueError("Source frame signature does not match declared format: {}".format(path))
        sources.append({"frame": index, "name": path.name, "sha256": digest_file(path)})
    return Capture(directory, metadata, extension, times, pts_us, hold_us, sources)


def concat_text(capture):
    entries = ["ffconcat version 1.0"]
    # Absolute, quoted paths keep temporary encoding files out of source evidence.
    for index, source in enumerate(capture.sources):
        path = str(capture.directory / source["name"])
        if "\n" in path or "\r" in path:
            raise ValueError("Capture paths cannot contain newlines")
        quoted = "'" + path.replace("'", "'\\''") + "'"
        duration = (capture.pts_us[index + 1] - capture.pts_us[index]
                    if index + 1 < len(capture.sources) else capture.hold_us)
        entries.extend(("file " + quoted, "option framerate 1000000",
                        "duration {:.6f}".format(Decimal(duration) / MICROSECONDS)))
    # One repeated final image supplies the measured final display hold.
    entries.extend(("file " + quoted, "option framerate 1000000"))
    return "\n".join(entries) + "\n"


def verify_timestamps(capture, probe):
    try:
        pts = [Decimal(frame["best_effort_timestamp_time"]) for frame in probe["frames"]]
    except (KeyError, TypeError, InvalidOperation) as error:
        raise ValueError("ffprobe did not return valid frame timestamps") from error
    expected = capture.pts_us + [capture.pts_us[-1] + capture.hold_us]
    if len(pts) != len(expected):
        raise ValueError("Encoder changed frame count beyond the documented final hold")
    if any(not value.is_finite() or value != Decimal(tick) / MICROSECONDS
           for value, tick in zip(pts, expected)):
        raise ValueError("Encoded timestamps differ from planned microsecond PTS/final hold")
    error = max(abs(value - capture.times[0] - pts[index])
                for index, value in enumerate(capture.times))
    return float(error * 1000)


def encode_capture(directory, output):
    output = Path(output).absolute()
    report_path = output.with_suffix(".json")
    if output.suffix.lower() != ".mp4":
        raise ValueError("Output must have an .mp4 suffix")
    for path in (output, report_path):
        if path.exists() or path.is_symlink():
            raise ValueError("Output already exists: {}".format(path))
    capture = load_capture(directory)
    if output.parent.resolve() == capture.directory or capture.directory in output.parent.resolve().parents:
        raise ValueError("Output must be outside the source capture directory")
    contents = concat_text(capture)
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="spf-encode-", dir=output.parent) as temporary:
        working = Path(temporary)
        concat = working / "encoding.ffconcat"
        video = working / "video.mp4"
        concat.write_text(contents, encoding="utf-8")
        subprocess.run(["ffmpeg", "-hide_banner", "-loglevel", "error", "-nostdin", "-n",
                        "-f", "concat", "-safe", "0", "-i", str(concat), "-an",
                        "-c:v", "libx264", "-threads", "1", "-bf", "0", "-crf", "18",
                        "-pix_fmt", "yuv420p", "-fps_mode", "vfr", "-enc_time_base", "1:1000000",
                        "-x264-params", "fps=30/1:force-cfr=0", "-level:v", "3.1",
                        "-video_track_timescale", "1000000", "-movflags", "+faststart", str(video)],
                       check=True)
        probe = json.loads(subprocess.check_output([
            "ffprobe", "-v", "error", "-select_streams", "v:0", "-show_entries",
            "frame=best_effort_timestamp_time", "-of", "json", str(video)]))
        error_ms = verify_timestamps(capture, probe)
        intervals = [float(b - a) for a, b in zip(capture.times, capture.times[1:])]
        span = float(capture.times[-1] - capture.times[0])
        metadata = capture.metadata
        report = {
            "source_format": FORMATS[capture.extension], "source_filename_extension": capture.extension,
            "source_lossy": capture.extension == ".jpg",
            "source_alpha_preserved": capture.extension == ".png",
            "source_jpeg_quality": metadata.get("jpeg_quality", None if capture.extension == ".jpg" else 0),
            "source_capture_metadata": metadata,
            "source_frames": len(capture.times), "encoded_frames": len(capture.times) + 1,
            "measured_acquisition_seconds": span, "mean_acquisition_hz": (len(capture.times) - 1) / span,
            "median_interval_ms": statistics.median(intervals) * 1000,
            "max_interval_ms": max(intervals) * 1000, "max_encoded_timestamp_error_ms": error_ms,
            "final_hold_seconds": intervals[-1],
            "encoded_final_hold_seconds": capture.hold_us / 1000000,
            "video_lossy": True, "video_alpha_preserved": False,
            "video_encoding": "libx264 CRF 18, yuv420p, microsecond VFR PTS",
            "note": "Supplied acquisition timestamps at 1x. No dropped or interpolated source frames; "
                    "one repeated final image retains its display duration. Synchronous readback can "
                    "affect cadence. This is not a mobile frame-pacing benchmark or a pixel-equality oracle.",
            "video_sha256": digest_file(video), "sources": capture.sources,
        }
        staged_report = working / "report.json"
        staged_report.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
        # Same-filesystem exclusive publication: never replace existing output, even
        # if it appeared during encoding. Only remove our own published file on error.
        os.link(video, output)
        try:
            os.link(staged_report, report_path)
        except OSError:
            if output.exists() and output.samefile(video):
                output.unlink()
            raise
    return report


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path, help="Directory containing acquisition.csv and source frames")
    parser.add_argument("output", type=Path, help="New .mp4 path outside the capture directory")
    args = parser.parse_args(argv)
    try:
        report = encode_capture(args.capture, args.output)
    except (OSError, ValueError, subprocess.CalledProcessError) as error:
        parser.exit(1, "encode_capture: {}\n".format(error))
    print(json.dumps({key: value for key, value in report.items() if key != "sources"}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
