# Tower-defense HUD allocation investigation

## Observed failure and attribution limits

The published stage-1 commit `c78ba88` (tree-equivalent local checkpoint `f6a7df9`) failed
`TdPlayTests.BuildThroughTheUiAndDefend(GpuDriven)` in the
[2026-10-06 self-hosted Unity run](https://github.com/karosLi/SPGameFoundation/actions/runs/37505180428/job/112411906539).
Its `perf-gc.txt` records **4 allocating frames out of 180, totaling 328 managed bytes**.
The unchanged allowance is at most 2 allocating frames. DataTexture recorded **0/180, 0 bytes**.
These are allocation measurements; that run did **not** separately measure tower-defense GC collections.
It therefore does not establish either four collections or zero collections.

Reported runner: Unity 2022.3.62f2, native arm64 (Rosetta NO), macOS 26.4 / Darwin 25.4,
Apple M5 Pro, Metal, 48 GB RAM, Burst 1.8.27 loaded and not disabled.
Local Linux/llvmpipe stage-2 PlayMode passes do not prove that this Mac failure is fixed.
The investigation/remediation branch starts from local stage-2 `f04f14a`.

The CI artifacts do not contain allocation callstacks for those four tower-defense frames.
The HUD path below is a concrete app-owned allocation source, but **has not been matched to the
328 bytes**. Byte counts alone do not identify a caller, and the tier difference is not proof of
an allocation in GPU rendering.

## Confirmed source path and bounded change

`WaveSystem` increments `TdGameState.Version` when it consumes rewards and leaks. `TdHud.Update`
uses that version, together with selection, to refresh controls. Previously, a selected tower
formatted `UPGRADE {cost}` and `SELL {refund}` on every such refresh, even if both labels were
unchanged. An empty-cell selection similarly formatted all three build-price strings.
UGUI `Text.text` can reject an equal string only after interpolation has already created it.

`TdHud` now checks the displayed numeric values and MAX state before formatting:

- Build labels track their three current costs, rather than assuming the initial rules never change.
- Upgrade labels track upgrade cost and MAX state, including switching between different towers.
- Sell labels track the refund from the selected tower's actual investment.
- Gold/lives/timer text and button affordability still refresh normally. Cache checks do not
  bypass reward/leak updates, selection changes, build validation, or session synchronization.

The existing Text components, fonts, sizes, colors, alignment, hierarchy, interactions and sound
paths remain intact. Formatting still occurs when a displayed price actually changes. No snapshot,
simulation, upgrade/refund rule, shared text renderer, or warm-up interval in the whole-frame test
was changed.

## Regression and measurement design

`TdHudTests.RewardsSelectionUpgradeSellAndRuleChangesKeepLabelsCurrent` drives real UI buttons
and simulation ticks. It checks:

- Actual reward-queue consumption updates gold, kills, version and affordability; leaks update lives.
- Unchanged labels retain their content through those refreshes, while stats reflect new values.
- Building, both upgrades, MAX, switching tower kinds, sale/refund and restart produce correct labels.
- A customized tower price is reflected after refresh without corrupting the existing-tower refund.
- Build labels retain their original font size, alignment and non-raycast configuration.

`TdHudTests.WarmGoldOnlyRefreshDoesNotAllocate(false/true)` measures a narrow synchronous path
for the selected-tower/build panel respectively. A closed delegate is bound once to the actual
private `TdHud.Update`; there is no `MethodInfo.Invoke` or new public production test seam.
Each iteration increments gold and version, matching reward invalidation without including
simulation or coroutine-runner work in this isolated measurement. The separate behavioral test
above uses the real reward queue.

After 32 calls of each path, four 256-call windows run in former/fixed/fixed/former order. The
former-expression control adds the removed expressions to the same warmed production refresh,
on the same live HUD, rather than substituting a fake Text implementation.

The initial central Unity run exposed an important measurement failure: both former-expression
controls and both fixed windows returned zero from `GC.GetAllocatedBytesForCurrentThread`.
The positive-control assertions failed correctly; **none of those zero-byte readings establishes
zero allocation**. A calibration is now required before interpreting this byte API: 32 retained
1,024-byte arrays are allocated before and after the windows. If their payload is not observed,
the API is explicitly reported as unavailable for this environment, rather than as zero.

The allocation assertions now use the testing-only `SPF.Testing.ManagedAllocationProbe`. In Unity
it wraps `UnityEngine.Profiling.Recorder.Get("GC.Alloc")`, current-thread filtering and stop/flush
sample counts, following the installed Unity Test Framework 1.1.33
`AllocatingGCMemoryConstraint` implementation. In the .NET harness its explicitly different metric
is calibrated current-thread managed bytes. Each result carries its unit, and the helper throws
rather than returning a zero when calibration is unavailable. Operation warm-up stays explicit at
the call site. Its own tests cover positive/empty controls, exception recovery, wrong-thread,
nested, uncalibrated, null and disposed use. Both retained-array controls must observe at least
32 allocation samples, an empty window must observe zero, both former-expression windows must
allocate, and both fixed windows must observe **zero GC.Alloc samples**. This does not substitute
allocation sample counts for bytes or collection counts. The recorder is stopped and returned to
all-thread filtering in `finally`; no production profiler is enabled by this change.

Gen-0 collection counts are recorded independently as process-wide deltas. Logs and assertions
run after the windows. This is an isolated comparison of the removed label work, **not** a
full-checkout A/B, a render benchmark or a whole-frame zero-allocation claim. Deferred canvas work,
other threads and native allocations are outside the synchronous current-thread probe. HUD and
audio remain on.

The existing `TdPlayTests.BuildThroughTheUiAndDefend` is retained for both render tiers with its
original **180-frame window and <=2 allocating-frame allowance**, beginning after three kills.
It additionally reports a process-wide generation-0 collection delta through the existing
`GcReport` optional field; that count is independent of allocating frames. No windows are
excluded and no allowances are relaxed. A preallocated 180-element array now records every
sample's previous-frame byte value, governor frame count and current observation frame/tick/flow/
version/kills/gold. It writes `Artifacts/defense-frame-samples-{tier}.csv` after measurement and
before the budget assertion, including all zero and nonzero rows. These are observation-context
values, not exact callstack attribution or a claim that previous-frame bytes belong to the current
tick. There is no formatting, logging or collection growth inside the window.

## Validation status

- .NET harness DefenseFoundation EditMode logic tests: **8/8 passed**. These do not execute the
  Unity-only HUD tests, native Jobs/Burst, canvas, rendering or sound.
- Offline compile of the changed Game and PlayMode assemblies against actual Unity 2022.3.62f2
  references and its C# compiler: passed. This does not execute Unity or validate runtime behavior.
- Initial central Unity 2022.3.62f2 Linux/OpenGLCore run: behavioral HUD case passed; both initial
  byte-only probe controls failed with zero readings, so their zero-allocation claims are invalid.
  Whole-frame GpuDriven passed at 0/180, 0 bytes; DataTexture failed at 7/180, 287 bytes. Both
  separately recorded zero process-wide gen-0 collections. The cache does not resolve that residual.
- Calibrated central Unity rerun (`Artifacts/Remediation/Calibration/playmode.xml`): **3/3 passed**.
  Before and after each probe, the retained arrays produced **32 GC.Alloc samples and 0 raw
  current-thread bytes**. That establishes the byte API is ineffective in this Unity/Linux
  environment; it does not establish that all Unity runtimes behave identically.
  - Tower labels: each former window recorded **1,024 allocation samples / 256 refreshes**;
    each fixed window recorded **0 samples / 256 refreshes**.
  - Build labels: each former window recorded **1,536 allocation samples / 256 refreshes**;
    each fixed window recorded **0 samples / 256 refreshes**.
  - All these probe windows recorded **0 independent process-wide gen-0 collections**.
  These are allocation-event counts, not bytes. The old-expression controls establish the eager
  formatting cost, but do not attribute the remote 328 bytes.
- Any earlier Unity current-thread-zero claim based solely on the ineffective byte API needs a
  calibrated rerun. This finding does not invalidate independently sampled frame-allocation
  counters, collection counters, stamped profiler callstacks, or calibrated .NET byte results.
- Shared-helper tests in the .NET harness: **5/5 passed**, including calibrated 33,536-byte
  retained-array controls, zero-byte empty controls, and exception/thread/nesting checks. The
  helper, helper tests and migrated HUD tests compile against the actual Unity references.
  Their central Unity runtime reruns are still required. The original
  `TdPlayTests`-only fresh-Editor run and matched Mac rerun remain pending. No remote fix is claimed.
- Test ordering can share static font/UGUI/runner state. The initial focused run put the new HUD
  cases before the whole-frame cases; a fresh-Editor run of the original `TdPlayTests` alone is
  needed to distinguish test-order effects. No deferred-canvas cause is established by that order,
  and gameplay warm-up/window boundaries have not been changed to hide the failure.

For central real-Unity verification, run the normal PlayMode filter
`DefenseFoundation.Tests.PlayMode`, which includes the three new HUD cases and the two unchanged
whole-frame tier cases. Keep its XML, editor log and `Artifacts/perf-gc.txt`; the focused probe
writes its scope, Unity/OS/graphics metadata, calibrated allocation samples, byte-counter validity
and separate collection deltas to test output.

## If residual allocation remains

Do not infer a caller from allocation size or add warm-up to make the budget pass. Reuse the
methodology in `SvAllocationCaptureTests`: allocation callstacks, preallocated per-frame metadata,
exact measured-frame mapping, separate collection counters, raw profiler data, post-capture
extraction, and explicit missing-stack/unresolved-address counts. Preserve normal tower-defense
HUD, sound, selection, reward/leak handling, three-kill start condition and both render tiers.
Record platform/architecture/Burst state with each capture and compare on the failing runner.

If a reusable helper becomes necessary, extract only the existing profiler settings save/restore
and raw allocation/stack extraction into an Editor-only SPF.Testing utility. Keep game-specific
setup, stamp fields, scenario boundaries and assertions in each test. Coordinate that shared
change separately; this patch does not duplicate the large Survivor capture implementation.

## Later calibrated and remote verification

The shared probe and migrated assertions subsequently passed the central real-Unity suite. A fresh original-TdPlayTests-only run passed both tiers at 0/180 allocating frames, zero bytes and zero independent gen-0 collections. The complete local PlayMode run later repeated those same two zero windows. Exact remote WIP 93937656 on Mac/Metal also passed both unchanged windows, and reproduced the label ABBA event counts (former tower/build 1,024/1,536 per 256 refreshes; fixed zero; retained/empty controls 32/0 before and after). These passed reruns do not retrospectively attribute the earlier 328-byte Mac or 287-byte Linux residuals. The older failed windows remain part of the evidence.
