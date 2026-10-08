# Deliberately released S1a Editor CI

Status, 2026-10-08: [first native import failed](../Latios2022Lab/Docs/ImportCorrection-20261008.md); its lock/logs are preserved, all test phases remain NOT_RUN. The corrected candidate has static checks only and no S1a completion claim. The existing [lab protocol](../Latios2022Lab/README.md) and [blueprint](LatiosUnity2022IntegrationBlueprint.md) remain the acceptance authority.

Preparation correction (2026-10-08): the owned `LabPlayerBuild` Editor helper now
fully qualifies `UnityEditor.Build.Il2CppCodeGeneration.OptimizeSize`. The exact
[2022.3.62f2 source](https://github.com/Unity-Technologies/UnityCsReference/blob/2022.3.62f2/Editor/Mono/EditorUserBuildSettings.bindings.cs#L24-L40)
declares the enum in `UnityEditor.Build`; the helper previously imported only
`UnityEditor.Build.Reporting`. This fixes source-level name resolution before the
first import. It does not change the original Latios package pin, the 49+1 test
semantics, player build policy, root project or any actual native result. The
reviewed source commit used by a later request must include this correction.

## Release process and exact scope

Only the execution lead may release the approved existing Mac runner, after inspecting the precise P0 native XML/logs/artifacts and confirming the queue is free. A green workflow alone is insufficient: review its actual Burst execution and any fallback/skip evidence. This workflow does not provision runners, install Unity/modules, sign in, install credentials, change security settings or write persistent Burst preferences.

1. Prepare `dot/latios-lab-validate` from the **exact verified P0 Git commit**. Cherry-pick only the lab preparation/review and this CI change. Do not use the integration branch containing A1–A3 runtime changes. The validator compares actual Git trees and ancestry against P0 and rejects product `Assets`, `Packages`, `ProjectSettings`, unrelated tools/docs or other workflow changes.
2. Review the prepared lab source commit. The allowed delta from P0 consists only of `Latios2022Lab/**`, `Tools/ci/latios_lab_ci.py`, `Tools/ci/test_latios_lab_ci.py`, `.github/workflows/latios-lab.yml`, `Docs/Latios2022LabPreparation.md`, `Docs/Latios2022LabCI.md`, and the exact one-line root-workflow exclusion described below. Symlinks/submodules in lab/CI source are rejected.
3. After P0 and runner release, add/update `Latios2022Lab/ci-request.json` in a **separate gate-only commit**. `approved_source_commit` points to the reviewed prepared commit. All its fields must come from actual inspection/reservation, never a template claiming success. Correction source removes the prior short-lived request; the new release must be a gate-only child of the reviewed correction, preserving fast-forward history from the old release.
4. Push that release commit to the exact branch. Only changes to this request path trigger the lab push workflow. Other stage pushes do not. Manual dispatch exists for an execution lead using GitHub UI/API; it still requires the same exact branch, committed gate, prepared source and P0 checks. The current connector does not expose dispatch; no dispatch call is assumed here.
5. A hosted static job validates the gate, live read-only GitHub Actions run/attempt/job/step, source delta and Python checks before queuing native work. The native job revalidates P0, expiry and the absence of queued/running SPF native workflow work before every phase. The coordinator's reservation is still necessary: an API queue snapshot is not an atomic cross-workflow lock.

The sole existing-gate change is `push.branches-ignore: [dot/latios-lab-validate]` in `unity-self-hosted.yml`. All pull requests and all other push branches retain that suite; manual root dispatch remains available. This is a lab-only execution branch, not a product merge branch. A lab check never replaces a required SPF check on a product PR. The lab validator verifies that the root workflow differs from P0 by **exactly this line**. The ordinary .NET harness workflow stays unchanged.

## Request fields (no fabricated passing example)

| Field | Required source / meaning |
| --- | --- |
| `schema` | Integer `1` |
| `repository` | Actual `owner/repository`, exactly matching the current Actions repository |
| `p0_commit` | Full 40-character SHA of the native-verified P0 source |
| `approved_source_commit` | Full SHA of the reviewed prepared lab source; release delta must be only the request file |
| `p0_native_status` | `passed` only after genuine native acceptance |
| `p0_native_evidence_reviewed` | Boolean true only after inspecting original P0 artifacts, including Burst/fallback and skip status |
| `p0_evidence_url` | Exact `https://github.com/<owner>/<repository>/actions/runs/<run-id>/attempts/<attempt>` |
| `runner_reserved` | Boolean true only for an actual released exclusive reservation |
| `coordinator` | Responsible execution lead |
| `expires_utc` | Real timezone-qualified future reservation expiry; rechecked for every phase |
| `allowed_phases` | Exactly `['import', 'editmode', 'playmode']` in that order |

The live API must report the same repository, exact P0 SHA, run ID/attempt, `.github/workflows/unity-self-hosted.yml`, successful completed run, successful `unity-tests` job and successful `Run EditMode and PlayMode tests` step. API checks do **not** pretend to parse/approve the native artifacts: that part is explicitly the lead's attestation. Wrong/missing/expired gates, unavailable API, mismatched source, incomplete job inventory or a busy native queue fail closed before a native phase. No stored credential is required; GitHub's standard job-scoped token has only `contents: read` and `actions: read`, is not persisted, printed or passed to the Unity subprocess. Redirects fail instead of forwarding that token.

## Isolation and bounded execution

The Actions checkout has a fresh run-ID/attempt-specific subdirectory, with `persist-credentials: false`. Its first native-job step derives workspace/evidence paths from the assigned runner's `RUNNER_TEMP` and writes only job-local `GITHUB_ENV`; it does not use the unavailable `runner` context in job-level `env`. A second fresh detached Git worktree is created under `$RUNNER_TEMP/latios-s1a-<run-id>-<attempt>/source`; the only Unity project path is its `Latios2022Lab`. This project has a new Library and project-local UPM cache; no root product Library/cache is opened or deleted. Root `Packages`/`ProjectSettings` hashes are compared in both source checkout and isolated worktree. The runtime gate is materialized with this actual absolute project path after validation; no Mac username/path is committed.

The launcher uses an explicitly configured `UNITY_EDITOR_PATH`, or the existing Hub path for exactly 2022.3.62f2. The underlying lab launcher verifies the executable version before importing and selects native ARM64 on physical Apple Silicon, even if the runner uses Rosetta. It uses per-process Burst controls and safety witnesses. No `-nographics` or weakened retry is used.

One sequential run performs:

1. Clean import, actual environment inventory and Unity-generated lock capture, Burst on
2. EditMode Burst on, then off
3. PlayMode Burst on, then off

Each phase has a 30-minute wall-clock limit. A timeout sends TERM, then KILL after 20 seconds if necessary, only to that phase's newly created process group; it never searches for or kills other Editors. A failed/blocked/timed-out phase stops all later phases. The known pinned-package reactive-cleanup risk is a legitimate strict control-group failure, not a waiver. Run-specific files and native failure evidence are preserved for diagnosis; no product cache cleanup is attempted.

`ci-summary.json` explicitly lists every phase as NOT_RUN/RUNNING/PASSED/FAILED, records exit status/command and exact source commit/tree, and leaves `s1a: INCOMPLETE`. Repeated clean import, desktop IL2CPP build **and separate player execution**, and Android/iOS device acceptance remain NOT_RUN. The existing manual `player-build` command still needs a new explicit gate after actual Editor acceptance; this workflow cannot call it.

## Evidence and restoration

The lab's own `Artifacts` evidence set preserves original phase directories, full Unity/launcher logs, XML, environment inventory, actual package lock and normalized lab settings, gate and source pins. Collection runs even after a failing launcher exits before its success hash file. `evidence-sha256.json` inventories all retained evidence. No Library/cache or environment/credential dump is uploaded.

- `latios-s1a-early-review` includes a ZIP capped at 16 MiB. **Complete XML, JSON, text metadata and every retained file under `lab-inputs/ProjectSettings/` are included**, including normalized `.asset` settings. Original log files are represented here by explicitly labelled 64 KiB head/tail excerpts linked to the complete originals by SHA-256; excerpts never replace originals.
- `latios-s1a-part00` through `part31` use the existing [bounded evidence format](CiEvidence.md): at most 32 binary payloads of at most 16 MiB, plus their repeated manifests. Full logs and original XML/settings/locks remain there. The root packager is unchanged.
- Exceeding either bound is a reported failure, never silent dropping. If early ZIP exceeds its cap, the full parts are already packaged and remain independently uploadable; the oversized early ZIP is not published. Packaging/upload steps run after native failure. A gate failure before workspace creation may have only the Actions diagnostic log and cannot be reported as native execution.

Download all available numbered parts from the same run, then restore/check hashes with the existing tool:

```sh
python3 Tools/ci/evidence_parts.py restore --parts /path/to/downloaded-parts --output /new/restored-directory
```

The actual lock is retained as evidence but **not automatically committed or pushed**. Review it with environment evidence before any subsequent source commit. A successful workflow establishes only its recorded Editor-control scope, never the remaining player/device gates or product adoption.

## Static verification

```sh
PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s Tools/ci -p test_latios_lab_ci.py -v
PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s Latios2022Lab/Tools -p 'test_*.py' -v
```

Tests use synthetic parser/API fixtures, temporary Git repositories and a stub process callback. They never launch Unity, call live Actions or create a valid repository release gate. Coverage includes missing/invalid/expired gates; wrong branch/repository/commit/attempt/workflow; failed/skipped jobs/steps; queue occupancy; root/runtime/dependency/settings diffs; gate-only release; exact exclusion; first-failure preservation; and evidence caps. GitHub API/workflow semantics follow the official [workflow run endpoints](https://docs.github.com/en/rest/actions/workflow-runs#get-a-workflow-run-attempt) and [workflow syntax](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax).

### Workflow context correction, 2026-10-07

[Run 37654425385](https://github.com/karosLi/SPGameFoundation/actions/runs/37654425385), source `d6753f4c7b12d0d60b303d07fb7dbb8c1eceaafa`, failed before job creation: the Actions API returned zero jobs. The original job-level `env` referenced `runner.temp`, but [GitHub's context-availability table](https://docs.github.com/en/actions/reference/workflows-and-actions/contexts#context-availability) excludes `runner` from `jobs.<job_id>.env`. This is a workflow-validation failure; Unity/native phases were never run. Moving those paths into the assigned runner's first shell step follows the documented [GITHUB_ENV mechanism](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-commands#setting-an-environment-variable). New regression tests reject unsupported job-env expression contexts and execute only that path-initialization shell against a temporary environment file, including paths with spaces and missing `RUNNER_TEMP`. Remote validation of the correction remains pending publication; static/YAML tests alone do not establish GitHub acceptance or native execution.
