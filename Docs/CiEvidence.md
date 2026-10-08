# Unity CI evidence parts

The self-hosted Unity job uploads `unity-test-results-self-hosted-part00` through
`part39`, creating only the parts that are needed. Each artifact contains one binary
part of at most 16 MiB and an identical `evidence-manifest.json` of at most 1 MiB.
The cap accommodates paired before/after live gait captures with original painted backgrounds; individual downloads remain unchanged. Only nonempty parts are uploaded.
The outer GitHub artifact uses no compression; the evidence ZIP is already compressed.
Each download therefore stays comfortably below a 32 MiB download limit.

`Tools/ci/local-unity-tests.sh` clears the dedicated `Artifacts/` directory before its
test run. Packaging preserves every regular file in that directory, including nested
screenshots, raw profiler captures, JSON, logs, reports, and test results. It rejects
symlinks rather than reading outside evidence. It does not collect `Library/`, caches,
the repository, or credential directories. Screenshot branch publication is unchanged.

The ZIP is limited to 40 parts (640 MiB compressed), increased from 32 parts (512 MiB).
The individual 16 MiB part and 1 MiB manifest caps are unchanged: each artifact's
payload plus ZIP wrapper metadata remains below the 32 MB download-tool cap.
Oversized evidence or a manifest over 1 MiB fails the packaging step explicitly;
files are never silently omitted.
Packaging also runs after test failures, and does not change test assertions or outcomes.
Output lives in the runner's temporary directory, outside `Artifacts/`. Only temporary
files created by this invocation are cleaned up; existing output is never overwritten.

## Download and restore

1. Download every `unity-test-results-self-hosted-partNN` artifact from the same job/run
   attempt. The manifest lists the complete ordered set and all sizes and SHA-256 hashes.
2. Unzip each GitHub artifact into its own directory under a common `downloads/` folder.
   Preserve each binary part's filename. Repeated identical manifests are expected.
3. Run from the repository with Python 3 (standard library only):

   ```sh
   python3 Tools/ci/evidence_parts.py restore --parts downloads --output restored-evidence
   ```

The helper finds parts recursively, rejects missing/duplicate parts or mixed manifests,
checks each part's size and SHA-256, concatenates them in `part00`, `part01`, ... order,
checks the complete ZIP's size and SHA-256, then extracts and verifies every file against
the inventory. The result is `restored-evidence/Artifacts/`. Extraction cannot escape
that directory, and failed verification leaves no partial output directory. Choose an
output directory that does not already exist.

These `.partNN` files are binary slices of one ZIP, not independently extractable ZIPs.
For manual inspection, concatenate only the parts listed in the manifest in order;
verify every part and the complete ZIP against the manifest before unzipping.

## Local packaging and checks

```sh
python3 Tools/ci/evidence_parts.py package --source Artifacts --output evidence-parts
python3 -m unittest discover -s Tools/ci -p 'test_*.py'
```

Tests generate fixtures and cover byte-exact restoration across the real 16 MiB boundary,
raw/nested/hidden evidence inclusion, missing/corrupt/duplicate parts, mixed manifests,
archive and file hashes, size-limit failure without dropped files, symlink rejection,
safe extraction, and protection of existing output.

The current restore helper also accepts the historical eight- and 32-part manifests.
It still enforces their declared limit, verifies every part and file, and rejects
unsupported/unbounded manifest capacities. Small fixtures exercise exactly 40 parts,
rejection of evidence requiring 41 parts, and restoration of historical manifests;
the existing real 16 MiB boundary test is retained. Increasing this evidence transport
cap does not change runtime memory, mobile performance budgets, capture quality/FPS,
frame counts or test assertions. The workflow only adds uploads for `part32` through
`part39`; runner, permissions, dispatch inputs and recording conditions are unchanged.

## Deferred motion-review encoding

`BufferedFrameCapture.Write(name, scenario)` still defaults to lossless RGBA PNG.
Its optional `BufferedFrameFormat.Jpeg95Review` mode uses Unity's built-in
`ImageConversion.EncodeToJPG(texture, 95)` only **after** all raw RGBA target readbacks
have been acquired. The bounded raw buffer, dimensions, acquisition loop, frame count,
clock annotations, contact/GC/pixel assertions, and timing values are unchanged.
Every acquired frame is saved; this is no frame dropping, resampling, interpolation,
or change to the gameplay or capture cadence. Synchronous readback itself can still
influence cadence, so these Editor captures are not mobile frame-pacing benchmarks.

Only the continuous `weapon-belt-live-*`, `weapon-horde-live-*`,
`grounded-belt-live-*`, and `grounded-horde-live-*` review sequences opt into JPEG95.
Static contact/parity screenshots and all other default captures remain PNG. JPEG95
is **lossy RGB with alpha discarded**, so its files are visual-review evidence, never
a lossless pixel-equality oracle. Raw `ReadPixel` checks still read the unmodified
in-memory RGBA buffers. JPEG review files do not retain those exact original bytes;
use a default PNG capture when a persisted pixel oracle is needed. Existing downloaded
PNG evidence is unchanged; this optimization applies to future test runs.

Each deferred capture now includes `capture.json` (version 1):

- `format`: `png` or `jpeg`; `filename_extension`: `.png` or `.jpg`
- `jpeg_quality`: 95 for JPEG; 0 means not applicable for PNG
- `lossy`, `alpha_preserved`, `frame_count`, `width`, `height`, `raw_buffer_bytes`
- `compression_scope`: the explicit pixel/alpha tradeoff and acquisition boundary
- `timestamp_source`: `acquisition.csv`

Files retain the `frame-000`, `frame-001`, ... numbering, with the declared extension.
`acquisition.csv` has the unchanged header
`frame,acquisition_seconds,simulation_seconds_at_readback` and unchanged measured
values. `acquisition.ffconcat` references the matching file extensions and retains
all measured intervals, including the documented repeated final image/display hold.
A consumer should validate the metadata format/extension pair, use PNG for historical
captures without metadata, and reject missing or mixed frame sequences rather than
silently inventing frames. Encode user-facing clips at 1x actual acquisition PTS, never
by relabeling the sequence as a fixed 30/60 Hz video. The requested 30 Hz capture target
is not a claim that the observed acquisition cadence achieved it.

## Reproducible 1x review video

After restoring and verifying the complete evidence archive, encode a finished,
immutable capture directory with the repository helper. Python uses only its standard
library; `ffmpeg` (with `libx264`) and `ffprobe` must be on PATH. Encoding is offline,
after acquisition; the helper does not run Unity or alter gameplay/capture settings.

```sh
python3 Tools/ci/encode_capture.py \
  restored-evidence/Artifacts/Screenshots/WeaponMotion/grounded-belt-live-gpu \
  review/grounded-belt-live-gpu.mp4
python3 -m unittest discover -s Tools/ci -p 'test_*.py'
```

Choose a new `.mp4` path outside the capture directory. Both that path and its sibling
`.json` report must be absent, including dangling symlinks. Temporary concat/video
files are kept outside source evidence and removed after success or failure; the
original `acquisition.ffconcat` and any historical `encoding.ffconcat` are untouched.
Verified outputs are published using exclusive same-filesystem hard links, so the
output filesystem must support hard links. Existing files are never overwritten.

The importable `encode_capture.py` helper:

- Treats `capture.json`'s `.jpg`/`.png` declaration as authoritative, validates its
  format/lossiness fields, and defaults historical missing metadata/extension to PNG.
  It never uses stale PNG siblings to fill holes in a declared JPEG sequence. Other
  extensions are ignored; missing/extra declared-extension frames, mismatched file
  signatures, symlink frames, or disagreement with a declared frame count fail.
- Requires at least two sequential CSV rows and finite, nonnegative, strictly
  increasing acquisition timestamps. Simulation-clock annotations do not set PTS.
  Invalid timestamps, int64 microsecond overflow, or intervals that collapse at
  microsecond precision fail rather than being silently clamped or dropped.
- Subtracts the first acquisition timestamp and rounds each **cumulative** offset
  to a microsecond before deriving concat durations, avoiding interval-rounding drift.
  It adds exactly one repeated last image, whose PTS retains the final measured
  interval rounded to a microsecond. `ffprobe` must confirm all source frames plus
  that one hold and every planned PTS, including the final hold, before publication.
- Uses H.264/libx264 CRF 18, `yuv420p`, variable frame rate and a 1,000,000 Hz MP4
  track timebase. The nominal x264 `fps=30/1` hint does not resample acquisition PTS;
  there is no output `-r`, frame interpolation, scaling, or dropped source frame.
  Current bounded, even-dimension captures are the intended input. Corrupt images,
  unsupported dimensions/codecs or incompatible FFmpeg options fail explicitly.
- Writes source filenames/SHA-256 hashes, video SHA-256, acquisition cadence,
  measured timestamp error, measured/encoded final hold, and original capture
  metadata into the JSON report. It discloses source JPEG lossiness/quality/alpha
  separately from the **lossy, alpha-discarding MP4**, even for lossless PNG input.
  Keep the original PNG/raw evidence for pixel assertions. This helper does not
  establish native capture provenance or substitute for archive-manifest verification.

Local verification of this tooling change: all 30 Python CI-tooling tests pass
(15 encoder tests and 15 archive tests). The encoder tests use mocked subprocesses,
so they require no FFmpeg installation. Separate real FFmpeg/ffprobe 7.1.5 smoke runs
encoded historical PNG and a JPEG-format fixture with stale PNG siblings: each kept
3 source frames plus one final hold, with a maximum timestamp error of 0.000416 ms.
A complete historical 160-frame PNG sequence retained all 160 frames plus one hold
with maximum error 0.000500 ms (the half-microsecond rounding bound). A four-color
synthetic sequence also decoded in exact source order plus only the repeated final
color, with 0.000333 ms maximum PTS error; source bytes and existing outputs remained
unchanged. These are encoder checks, not native Unity JPEG
execution, new gameplay visuals, or Android/iOS performance evidence. Unity-produced
JPEG size/quality and the changed native fixture still await the next graphics run.
Bit-identical MP4 bytes across FFmpeg/libx264 versions are not promised.

## Archive priority and verification

Packaging now sorts the complete inventory globally before writing the ZIP:

1. Native test-result XML named `*-results.xml`, without a size cutoff. This is
   the native runners' naming contract, including `editmode-results.xml`,
   `playmode-results.xml`, and tagged retries such as `editmode-noburst-results.xml`.
2. Small metadata (at most 1 MiB per file): `.xml`, `.csv`, `.tsv`, `.json`, `.txt`,
   `.md`, `.log`, and `.ffconcat`
3. Lossless `.png` frames directly in the two `Screenshots/WeaponMotion/blade-grip-isolated-{gpu,fallback}` directories, without a size cutoff
4. JPEG review files: `.jpg` or `.jpeg`, without a size cutoff
5. All remaining files, including other PNGs, raw captures and oversized non-result XML
   or other metadata

Result filename and suffix matching are case-insensitive; within each tier the exact
relative POSIX path is the deterministic tie-breaker. Both ZIP member order and manifest order follow
this key. No files are omitted, recompressed into a different image format by the
packager, or excluded from the existing SHA-256 inventory. The archive size cap
is 40 x 16 MiB. Priority puts all result XML ahead of binary media, including
reports over 1 MiB, but does not promise that every result fits in `part00`: a first
member whose compressed bytes and ZIP header exceed 16 MiB necessarily spans parts,
and several result files can also exceed that first part together. Files are never
truncated or split into replacement members. The standard restore command still
requires **all** ZIP parts.
This is not an independently extractable-part format or a promise that a partial
archive is fully verified.

The targeted PNG priority follows the interrupted 3c45520 upload: its close-up
tests passed, but those frames were in the unpublished tail. Promoting the two
exact directories changes ordering only; prefix lookalikes and nested directories
do not receive priority. Metadata remains ahead of the images. The original run's
manifest stays immutable, and its recovery uses the original pinned packager;
this ordering applies only to subsequent runs. It does not convert incomplete
old evidence into a visual pass or guarantee a network upload succeeds.
The focused exact-directory ordering case fails against the previous packager.
The updated tooling passes all **34** Python tests (19 archive, 15 encoder),
including byte-exact restoration and unchanged-source checks. Native upload of
the new ordering remains pending; no C# or capture behavior changed.

The result-priority regression uses XML over 1 MiB alongside small metadata,
oversized unrelated XML and media, checks actual ZIP/header/manifest order across
the real 16 MiB boundary, and verifies byte-exact restoration and unchanged sources.
A separate bounded-part fixture verifies restoration when the first result itself
spans parts. These checks preserve the existing safety, hash and capacity tests.
The focused tests failed on the previous ordering; after the fix, all 33 Python
CI-tooling tests pass (18 archive and 15 encoder tests). This is transport validation;
it does not establish new Unity or .NET test results.

Earlier JPEG/metadata-priority verification:

- The native `BufferedFrameCaptureTests.DeferredEncodingKeepsDistinctActualFramesAndBoundedCapacity`
  fixture checks default PNG raw-pixel equality, real JPEG file headers/decoding,
  retained first/last-frame identity, dimensions, every retained raw pixel after
  JPEG export, identical CSV/timing/final-hold data across formats, format metadata,
  lossy disclosure and invalid format rejection. The existing capacity/path checks
  remain. This test requires a real Unity graphics device.
- `python3 -m unittest discover -s Tools/ci -p 'test_*.py'` passes all 15 tests,
  including actual ZIP/header/inventory order, the inclusive 1 MiB metadata boundary,
  deterministic repeated packaging, and byte-exact restoration with JPEGs across the
  real 16 MiB split boundary. Existing security/hash/limit tests remain.
- The complete 76-assembly .NET harness builds and passes 980 logic tests with no
  failures; the opt-in `SearchShots`/`ExportFrames` tooling cases remain explicitly
  skipped. The new image-encoding fixture is native-only and is not counted as
  executed by this stub harness. Build-time NuGet vulnerability-audit warnings were
  caused by the read-only default HTTP cache, not C# compile errors.
- The changed C# helper, fixture and both gameplay call sites compile against the
  actual Unity 2022.3.62f2 managed DLLs, including ImageConversionModule, rather than
  an invented JPEG stub API. This is API compilation only; native execution and
  actual Unity JPEG size/quality are pending the next exact-commit graphics CI run.

### Size estimate from verified native source frames

A format-only experiment on nine existing PNGs from native run
[37574715913](https://github.com/karosLi/SPGameFoundation/actions/runs/37574715913),
commit `4af9b87cc32fcd1a5f10a4bd2817848044ad4649`, verified each source's bytes and
SHA-256 against that run's evidence manifest before encoding a separate JPEG copy.
The samples were belt fallback/GPU frames 000, 080, 159 and horde fallback frames
000, 034, 068, all from their `grounded-*-live-*` sequences.

Pillow 12.3.0 quality 95, 4:4:4 subsampling produced 820,522 bytes from 2,822,474 PNG
bytes: **70.93% smaller** (71.22% smaller after deflate). The worst sampled RGB PSNR
was 40.82 dB and the largest mean absolute channel error was 1.67/255, confirming the
output is lossy. This is an estimate of the transport opportunity, **not** a claim
about Unity's encoder size/subsampling/quality, a guarantee for every scene, or a new
art/gameplay render. Original PNGs were not changed. Actual encoder savings must be
measured from the next Unity-produced JPEG artifacts; all static PNG evidence still
contributes to the total package size.
