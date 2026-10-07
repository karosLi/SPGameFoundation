# Approved Mac CI Burst activation (2026-10-07)

The owner explicitly approved enabling Burst Compilation on the existing Mac CI runner after its read-only diagnostic showed persisted `BurstCompilation=false`, disabled compiler/job flags, independent native controls `0/0/0`, and managed physics execution.

## Scope and guards

The separate `unity-burst-setup-approved.yml` workflow runs only on creation of `dot/ci-burst-setup-approved-20261007`. It has no scheduled, manual, pull-request, or ordinary-branch trigger. The existing general Unity workflow excludes this branch to avoid duplicate suites. Later pushes to ordinary gameplay branches never invoke setup. The workflow uses read-only repository permissions and the existing Mac runner and licensed Unity install; it installs no software and changes no security settings.

`CiBurstSetup.EnableApproved` requires batch mode and the exact `--spf-enable-approved-ci-burst-20261007` flag. Its transaction captures presence/value for all five preferences written by Burst 1.8's option-change callback, plus session-only `BurstSafetyChecks`. It writes `BurstCompilation=true` and, when needed, uses the supported public `BurstCompiler.Options.EnableBurstCompilation` setter. It restores the other four persisted keys and the session value, deleting keys that were originally absent. On activation error it restores the original option followed by all five preferences and the session snapshot; rollback errors are reported explicitly. It uses no private fields, event changes, or private compiler calls.

Before activation, the script checks the resolved Burst package is the project's pinned 1.8.27 and verifies its preference-writing surface. All source evidence, before/activation/fresh-process reports, logs, and the exact checked-out SHA/tree are uploaded. The source check fails closed if that surface changes.

## Verification

Three independent Editor processes perform: read-only inspection, guarded activation, then read-only verification. The last requires both persisted enablement and effective compiler/job flags, independent direct/Run/Schedule controls of `0/1/1`, and the actual physics job's native witness. The shell compares all four non-target persisted preferences across the first and last processes. Session state is preserved within the activation process; it is not expected to survive process exit.

The original full EditMode and PlayMode suites then run unchanged. The final evidence gate rejects Burst-disabled fallback artifacts, missing results, failed suites, missing native controls, and anything other than 60/60 native warmup and 300/300 native measured physics steps. The existing benchmark remains 600 bodies, 60 warmup steps, 300 measured steps, and a mean below 4 ms. No performance limit, allocation limit, or native assertion is relaxed. A later test failure does not silently switch off an otherwise successfully activated, approved preference.

Pure fake-store tests cover all 729 absent/false/true combinations for five preferences and one session value, denied invocation before any store access, option-setter failure after side effects, backend-verification failure, complete rollback, and an already enabled option. These tests never read or write the machine's EditorPrefs. The .NET harness checks these simulated transactions; real preference and native execution evidence requires the Mac CI run.

Public API: [Burst compiler options](https://docs.unity3d.com/Packages/com.unity.burst@1.8/api/Unity.Burst.BurstCompilerOptions.html). This setup is editor/runner enablement evidence; it does not establish Android or iOS device performance.
