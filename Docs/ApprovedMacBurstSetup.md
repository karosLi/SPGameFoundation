# Approved Mac CI Burst activation (2026-10-07)

The owner explicitly approved enabling Burst Compilation on the existing Mac CI runner after its read-only diagnostic showed persisted `BurstCompilation=false`, disabled compiler/job flags, independent native controls `0/0/0`, and managed physics execution.

## Scope and guards

The separate `unity-burst-setup-approved.yml` workflow runs only on creation of `dot/ci-burst-persist-approved-20261007`. It has no scheduled, manual, pull-request, or ordinary-branch trigger. The existing general Unity workflow excludes this and the preceding setup branch to avoid duplicate suites. Later pushes to ordinary gameplay branches never invoke setup. The workflow uses read-only repository permissions and the existing Mac runner and licensed Unity install; it installs no software and changes no security settings.

`CiBurstSetup.EnableApproved` requires batch mode and the exact `--spf-enable-approved-ci-burst-20261007` flag. Its transaction captures presence/value for all five preferences written by Burst 1.8's option-change callback, plus session-only `BurstSafetyChecks`. It writes `BurstCompilation=true` and, when needed, uses the supported public `BurstCompiler.Options.EnableBurstCompilation` setter. It restores the other four persisted keys and the session value, deleting keys that were originally absent. On activation error it restores the original option followed by all five preferences and the session snapshot; rollback errors are reported explicitly. It uses no private fields, event changes, or private compiler calls.

Before activation, the script checks the resolved Burst package is the project's pinned 1.8.27 and verifies its preference-writing surface. All source evidence, before/activation/fresh-process reports, logs, and the exact checked-out SHA/tree are uploaded. The source check fails closed if that surface changes.

## Verification

Three independent Editor processes perform: read-only inspection, guarded activation, then read-only verification. The last requires both persisted enablement and effective compiler/job flags, independent direct/Run/Schedule controls of `0/1/1`, and the actual physics job's native witness. The shell compares all four non-target persisted preferences across the first and last processes. Session state is preserved within the activation process; it is not expected to survive process exit.

The original full EditMode and PlayMode suites then run unchanged. The final evidence gate rejects Burst-disabled fallback artifacts, missing results, failed suites, missing native controls, and anything other than 60/60 native warmup and 300/300 native measured physics steps. The existing benchmark remains 600 bodies, 60 warmup steps, 300 measured steps, and a mean below 4 ms. No performance limit, allocation limit, or native assertion is relaxed. A later test failure does not silently switch off an otherwise successfully activated, approved preference.

Pure fake-store tests cover all 729 absent/false/true combinations for five preferences and one session value, denied invocation before any store access, option-setter failure after side effects, backend-verification failure, complete rollback, and an already enabled option. These tests never read or write the machine's EditorPrefs. The .NET harness checks these simulated transactions; real preference and native execution evidence requires the Mac CI run.

Public API: [Burst compiler options](https://docs.unity3d.com/Packages/com.unity.burst@1.8/api/Unity.Burst.BurstCompilerOptions.html). This setup is editor/runner enablement evidence; it does not establish Android or iOS device performance.

## Initial execution and persistence correction

Remote `011b00005b9f7e1b39ce1dada1e9fbd84f7db366` passed all 708 .NET tests with zero build warnings/errors. Its Mac activation reported the target preference and compiler/backend enabled, preserving all non-target preferences and session absence. The next fresh Editor read the target as false, so verification failed and full Unity suites did not run. Earlier full-suite files retained in that first job's artifact belong to previous executions and are not results for this commit.

The bounded retry uses the public `EditorApplication.Exit(0)` path only after successful activation and report writing. This tests the macOS batch-quit persistence behavior [reported by a Unity developer](https://discussions.unity.com/t/editorprefs-set-when-using-batchmode-quit-under-macosx/153728); that historical report is a hypothesis, not proof of the present cause. Read-only macOS `defaults read` snapshots of the five known keys surround activation and fresh verification. Generated artifact directories are cleared before the run to prevent stale suite evidence. The transaction, other-preference restoration, fresh native controls, full test workloads, and performance limits remain unchanged.

## Persisted activation and read-only final verification

At remote `d5a4f03ee278e514fe56eb1b1aa71ec347b58a15`, the OS readback changed `BurstCompilation` from 0 to 1 and remained 1 after the independent fresh Editor. Its controls were `0/1/1`, the actual physics first step was native, all four other preferences were unchanged, and the activation session's absent safety key remained absent. The full suites passed: EditMode **722 passed, 0 failed, 4 existing skips**; PlayMode **122 passed, 0 failed, 1 existing skip**. The unchanged 600-body workload recorded **60/60 native warmup steps, 300/300 native measured steps, mean 0.418 ms, worst 0.618 ms**, below the original 4 ms mean limit. Exact-head .NET passed **708 tests**, with zero build warnings/errors.

The initial evidence parser incorrectly rejected the EditMode root `result="Skipped:Ignored"`, although all 722 executed cases passed and all four skips were already expected. The corrected parser inspects every case, reconciles counters, requires the complete baseline case counts, permits only the named existing optional/graphics skips, rejects all failures/inconclusive cases and managed fallbacks, and rechecks the original physics workload and budget. Regression tests include the ignored-root format and negative cases for failures, missing tests/reports, unknown skips, inconsistent counters, managed witnesses/steps, and over-budget/nonfinite means.

The dedicated `dot/ci-burst-verify-20261007` branch-creation workflow performs **read-only preference/native verification and full suites**. It never invokes the activation method or supplies its approval flag. It records the exact SHA/tree and compares the same five known OS preference keys before and after verification. The earlier approved setup is not replayed merely to correct evidence parsing.
