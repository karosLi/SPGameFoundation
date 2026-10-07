# Stage G local verification record (2026-10-07 UTC)

## Exact checkpoint and scope

Implementation: `0ffa2ee356bdda740bbcf8f2b91c63c4eb7fae9f`, tree `b15d04e4fae26242060b5c245ed32e4a8c5ba767`, based on `3a09484`. This documentation/evidence follow-up changes no executable code. Stage D/F and E integration remain the parent checkpoint's separate responsibility; this record does not claim their later combined tree was tested here.

[Shared fixtures and bounded reporting](../SharedAcceptanceAndMobileBudgets.md) add testing/tooling only. No production runtime/game/presentation source, public gameplay enum, runner/device setting, CI workflow, old fixture or GC/physics/action/performance threshold was changed. The external Courier is test-only integration evidence, not a finished playable demo.

## Verified final results

- Full ordinary .NET harness: **1,221 executed / passed, zero failed**, including all **25 new acceptance cases**. Parsed all 26 TRX files: 12 nonempty EditMode records and 14 empty PlayMode records. Two pre-existing explicit diagnostics are `NotExecuted`; VSTest summary counter `notExecuted=0` is not used to erase those actual result entries. See the exact names and outcomes in [verification.json](StageGSoftwareEvidence-20261007/verification.json).
- Focused committed export: external Courier 9, Platformer 8 and Shooter 8 cases passed, zero failures/skips. All **24 Python schema/provenance tests passed**.
- Real Unity API compatibility: **515 sources / 95 actual managed/package references**, Unity 2022.3.62f2, C# 9, both `net8.0` and `netstandard2.1`, **zero errors**; seven existing unused/unassigned-field warnings in each compile. The monolithic API compile checks API availability, while the ordinary harness preserves asmdef boundaries. Exact source/reference SHA-256s and flags are in [native-api-inputs.json](StageGSoftwareEvidence-20261007/native-api-inputs.json). **Neither compile is native execution.**
- Cold factory inventory: all **19 compositions unchanged**; two independent process outputs match each other and the Stage A baseline byte-for-byte, SHA-256 `a42c9f27d2bb8d277303eef24e5b4cdbd58f0035d8bca89c7e08089348dd5f30`. No baseline regeneration.
- Dependency/toolchain: ignored Unity.Mathematics source is clean at pinned `f110c8c230d253654afed153569030a587cc7557`, source digest `6f57c93d503b28c940ecad8d8a9b2cf0496ae3a23ea4a7176ffcbc8fdc610b9e`; before/after context identical. SDK 8.0.425. The exporter verifies tracked/untracked/ignored compiled-source inputs and retains [`dotnet --info`](StageGSoftwareEvidence-20261007/toolchain.json).
- Independent code review checked all 75 asmdefs are acyclic and all 19 factory paths exist. Four P2 findings were corrected: pooled identity collision against every survivor, genuinely reversed distinct destroy order plus handle recycling, fixed 16/64/64 windows, and ignored dependency provenance. Independent committed-output review then verified focused TRX counts, Python results, all raw hashes/context, default-vs-custom capacities, and Unknown/Pending semantics. No remaining review blocker.
- JSON and Markdown re-render byte-identically from retained inputs. JSON SHA-256 `2484041391b049cb15e21f3dd9022ef07710b4da1c423c9c4d13c0823c648903`; Markdown SHA-256 `c917397dfd2936e83042fae7c8d8bf5728fe9a27e8d3f87ae4895aa19d6fe187`.

The whole final harness was rerun after the last reviewed C# changes, from the exact implementation commit. A previous green 1,221-case run preceded two final assertion-strengthening edits and is not substituted for this final result.

## Retained bounded evidence

- [Human-readable measured report](StageGSoftwareEvidence-20261007/report.md)
- [Machine-readable measured report + provisional profile goals](StageGSoftwareEvidence-20261007/report.json)
- Raw probes: [Shooter](StageGSoftwareEvidence-20261007/raw/shooter.default.json), [Platformer](StageGSoftwareEvidence-20261007/raw/platformer.classic.json), [external Courier](StageGSoftwareEvidence-20261007/raw/external.courier-test-only.json)
- [Verification/TRX/log manifest](StageGSoftwareEvidence-20261007/verification.json), [toolchain](StageGSoftwareEvidence-20261007/toolchain.json), [all retained file hashes](StageGSoftwareEvidence-20261007/SHA256.json)

Only three software probes were measured. The other 17 inventory modes have configurable goals and their ordinary regression evidence, not new per-mode budget measurements. Shooter's small fixture has zero active enemies/pickups during the measurement and must not be cited as a dense/default workload. Platformer uses default factory capacities. All three synchronous current-thread allocation windows measured zero managed bytes with 33,536-byte retained controls and zero empty controls both before and after; that is not all-thread/native/driver or rendered-frame allocation evidence.

Actual backend is `simulation-only/no-renderer`. GPU time, worker CPU, live memory, upload, physical GPU traffic, transient pool high-water, rendered allocation frames, overdraw, thermal and battery remain Unknown/null with reasons. The short fixed-step timings are neither rendered-frame pacing nor sustained mobile performance.

## Commands and observed development failures

Using writable SDK/cache directories and the repository's pinned dependencies:

```bash
python3 Tools/DotnetHarness/generate.py
dotnet build Tools/DotnetHarness/.gen/Harness.proj -nologo -v q -m:1
dotnet test Tools/DotnetHarness/.gen/Harness.proj --no-build -nologo -v normal -m:1 \
  --logger 'console;verbosity=normal' --logger trx --results-directory <results>
Tools/Acceptance/run-report.sh <new-empty-output-directory>
```

The final export wrapper rebuilds and runs all three focused test adapters plus Python checks. [The recipe](../SharedAcceptanceAndMobileBudgets.md#reproduce-the-bounded-export) covers re-rendering retained JSON/Markdown and the existing cold-probe document covers inventory reproduction. API compatibility was compiled against the real SDK references listed in `native-api-inputs.json`; do not present that as a Unity Test Runner result.

Development attempts exposed and fixed a missing Platformer test assembly reference, direct mutation of a NativeArray-valued property, and the incorrect initial assumption that Shooter used handle tables. Shooter now tests its actual pooled stable-ID contract, with handle destroy-queue cases explicitly inapplicable. An initial parallel aggregate test invocation hit the sandbox's named-pipe permission limit before executing tests; the unchanged aggregate suite ran serially with `-m:1`. Initial dependency audit-cache warnings were not test failures. Local cached verification used `NuGetAudit=false`; this is not a dependency vulnerability audit. The final ordinary harness build emitted zero compiler warnings/errors. No test window or acceptance threshold was weakened to obtain these results.

## Remaining gates

- New native Unity EditMode/graphics execution: **Pending parent integration/runner verification**. Historical graphics results and empty harness PlayMode records cannot close this gate.
- Android physical-device acceptance: **Pending**.
- iOS physical-device acceptance: **Pending**.

Device touch/suspend, IL2CPP/Burst player behavior, actual graphics fallback, sustained frame-time percentiles, memory, thermal and battery require a selected physical device and bounded capture plan. No physical device was supplied or measured, and this report does not sign a mobile-performance pass.
