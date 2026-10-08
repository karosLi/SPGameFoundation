# Deliberately released S1a Editor CI

Status, 2026-10-08: [the exact b6ef271 patched Local Editor experiment](../Latios2022Lab/Docs/PatchedLocalEditorMilestone-20261008.md) passed import, 49/49 EditMode on/off and 1/1 PlayMode on/off in run 37813671446 attempt 1. All 327 archive members, 24 package tars and 12 genuine locks are verified. Original Git and unpatched Local controls remain four-failure results; repeat/player/mobile gates remain NOT_RUN and S1a incomplete. The [lab protocol](../Latios2022Lab/README.md) and [blueprint](LatiosUnity2022IntegrationBlueprint.md) remain the acceptance authority.

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

The lab's own `Artifacts` evidence set preserves original phase directories, full Unity/launcher logs, XML, environment inventory, actual package lock and normalized lab settings, gate and source pins. Collection runs even after a failing launcher exits before its success hash file. `evidence-sha256.json` inventories all retained evidence. No bulk Library/cache, DLL or environment/credential dump is uploaded.

The [owned-domain review](../Latios2022Lab/Docs/CompilationDomainReview-20261008.md) adds per-phase compiler-text snapshots: only four exact Lab assemblies' `.rsp`, `.rsp2` and `.UnityAdditionalFile.txt` files under Bee artifact directories, plus already emitted `.cs` under their exact `Temp/GeneratedCode/<assembly>` directories. Limits are 128 files, 2 MiB/file and 8 MiB/snapshot; symlinks and non-UTF8 content are refused. Paths inside response files are not followed. Missing response files and unavailable emitted source are explicitly indexed. No generator-output define is enabled. Snapshots are immutable; collection trouble is recorded separately without replacing a native failure, and final summary hashes are refreshed.

- `latios-s1a-early-review` includes a ZIP capped at 16 MiB. **Complete XML, JSON, text metadata and every retained file under `lab-inputs/ProjectSettings/` and `compiler-text/` are included**, including normalized `.asset` settings and owned response/source text. Original log files are represented here by explicitly labelled 64 KiB head/tail excerpts linked to the complete originals by SHA-256; excerpts never replace originals.
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

## Explicit Local cleanup experiment

The [frozen two-arm protocol](../Latios2022Lab/Docs/CleanupVariantProtocol-20261008.md) is an optional named request profile. Without it, Git pin/lock rules remain strict. A committed `experiment` object must exactly match `cleanup_variant.experiment_for_arm(arm)`; this is metadata, not an automatic P0/runner approval. The lead releases the unpatched Local control and patched Local variant independently with the same reviewed source and fresh workspaces.

The runtime gate binds the full experiment-record bytes, paths, upstream inventory, arm and patch identity. Source/oracle and whole external-package integrity are checked before/after every phase; no real Local lock is supplied until Unity creates it. A failed control stays FAILED. A control EditMode/on exit 0 is recorded as UNEXPECTED_PASS and stops the wrapper for review, without altering the native XML or launching the other arm. Only the patched arm can complete this experiment's five-phase Editor acceptance; original S1a/player/mobile status is separate.

Current artifact names include `latios-s1a-<run>-<attempt>-<git-control-or-arm>-early-review` / `-partNN`. Full evidence includes immutable package tar snapshots and hash inventories; early review includes bounded complete metadata, never the tar payload. The 16 MiB early and existing full archive limits are unchanged. Historical artifacts retain their original names and identities.

## Conditional repeat and player preparation

`Latios2022Lab/Tools/followup.py` is a separate, default-closed local runner. It does not change this workflow, issue a CI request, reserve a runner, edit frozen C# or promote the old control. [The milestone](../Latios2022Lab/Docs/PatchedLocalEditorMilestone-20261008.md) records precise native scope and all retained evidence hashes.

No arguments print a plan only. `--check --gate /absolute/released.json` verifies local prerequisites without remote calls or native launch. `--execute --gate /absolute/released.json --workspace /new/absolute/workspace --editor /absolute/installed/Unity` also verifies live P0/Editor workflow state and both native queues before each phase. Only the execution lead may use it after reserving the existing Mac. No valid release example is committed.

The external JSON gate contains:

- `schema`: `latios-followup-v1`; `stage`: `repeat-clean-import` or `mac-il2cpp-smoke`
- `allowed_phases`: exactly `["import"]` or `["import", "player-build", "player-run"]`, respectively
- `tool_commit`: exact clean checkout SHA of this preparation tool; `source_commit` and `source_tree`: exact historical native source verified by Editor and reused without source edits for repeat and player
- `target`: `StandaloneOSX`; `burst`: `on`; `architecture`: explicitly selected `arm64` or `x86_64`. The tool checks/launches this slice; it does not retarget the build or silently use Rosetta/an alternate slice
- `repository`, `p0_commit`, `p0_native_status`, `p0_native_evidence_reviewed`, `p0_evidence_url`, `runner_reserved`, `coordinator`, `expires_utc`: same truthful P0/review/short-lived reservation facts as above
- `experiment`: the exact existing patched Local profile, including official commit/tree and approved patch/inventory hashes
- `editor_evidence`: `{root, manifest_sha256, reviewed, run_url}`. `root` is the canonical directory containing restored `ci-summary.json` and `evidence-sha256.json`; the digest binds the latter, not the ZIP's separate part manifest. `reviewed` means original package/native/source evidence was inspected, not just a green badge. `run_url` identifies the exact completed run and attempt
- For player only, `repeat_evidence`: the same four-field evidence binding for the separately completed `repeat-clean-import` receipt. Its original source, Editor prerequisite and physically separate project/cache must match; its `run_url` is the coordinator's evidence location, not an automatically fabricated Actions run

Each stage creates its own new detached worktree, source-preserving package arm, Library/cache, real lock and immutable snapshots. The external tool's new revision is never relabelled as the older native source. The original source remains selectable specifically so a Python-only preparation change does not force changes to its compiled fixtures or rewrite old evidence. `lab.py` and its existing schema remain untouched; this wrapper owns only the additional conditional sequence and uses the frozen source's existing command/environment helpers.

Player preparation changes only run-unique company/product names in disposable normalized settings; the existing build helper selects IL2CPP/OptimizeSize/Development. Before/configured/after-build settings and explicit deltas/hashes are retained. A fresh application-data directory, exact build report, actual IL2CPP setting, selected executable architecture, bounded execution, new result, matching log JSON and unchanged app inventory are all required. The original frozen result has no nonce, so `player-launch.json` binds its hash to the source/tree, sealed package record, run UUID, unique application identity, actual executable/app hash, command, exit and original log. A stale result, missing JSON, timeout or nonzero exit cannot pass.

Original errors and partial evidence are retained on failure. Each Unity phase retains its 30-minute limit; player execution has 180 seconds, with TERM/KILL confined to that newly created process group. Missing IL2CPP/module/SDK or a build lacking the chosen architecture blocks that attempt; installation, preference/security changes and alternate backends are outside this runner. The bounded result is DisposeWorld/pair/query AOT smoke, not full lifecycle or mobile coverage. Both stage summaries leave `s1a: INCOMPLETE`, original controls failed, mobile and S2 pending for separate review.
