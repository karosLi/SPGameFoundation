# Unity CI evidence parts

The self-hosted Unity job uploads `unity-test-results-self-hosted-part00` through
`part31`, creating only the parts that are needed. Each artifact contains one binary
part of at most 16 MiB and an identical `evidence-manifest.json` of at most 1 MiB.
The cap accommodates paired before/after live gait captures with original painted backgrounds; individual downloads remain unchanged. Only nonempty parts are uploaded.
The outer GitHub artifact uses no compression; the evidence ZIP is already compressed.
Each download therefore stays comfortably below a 32 MiB download limit.

`Tools/ci/local-unity-tests.sh` clears the dedicated `Artifacts/` directory before its
test run. Packaging preserves every regular file in that directory, including nested
screenshots, raw profiler captures, JSON, logs, reports, and test results. It rejects
symlinks rather than reading outside evidence. It does not collect `Library/`, caches,
the repository, or credential directories. Screenshot branch publication is unchanged.

The ZIP is limited to 32 parts (512 MiB compressed). Oversized evidence or a manifest
over 1 MiB fails the packaging step explicitly; files are never silently omitted.
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

The current restore helper also accepts the historical eight-part manifests. It still enforces their declared limit, verifies every part and file, and rejects unsupported/unbounded manifest capacities. Increasing this evidence transport cap does not change runtime memory, performance budgets, frame counts or test assertions.
