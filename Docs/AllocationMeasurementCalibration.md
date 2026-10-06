# Allocation measurement correction — 2026-10-06

A retained-allocation positive control found that `GC.GetAllocatedBytesForCurrentThread()` is ineffective in the tested Unity 2022.3.62f2 Linux Editor runtime. This corrects the interpretation of earlier synchronous zero-byte observations; it does not change gameplay or relax any allocation budget.

## Observed controls

The probe retained 32 distinct byte arrays with 1,024-byte payloads before and after each ABBA experiment. Unity's current-thread `GC.Alloc` Recorder detected exactly 32 samples in each control, and zero in an empty control. The raw current-thread byte API returned **0 in both allocating controls**. A zero from that uncalibrated API is therefore **unavailable evidence**, not proof of no allocations.

An independently executed .NET 8 control reported 33,536 bytes for the same retained arrays and zero for the empty window. Do not assume .NET behavior carries into Unity, or that this Linux result proves the API state on every other runtime. Calibrate the actual execution environment.

The Unity recorder procedure follows the installed Test Framework 1.1.33 `AllocatingGCMemoryConstraint`: `Recorder.Get("GC.Alloc")`, filter to the current thread, enable around the synchronous body, stop/flush, then inspect `sampleBlockCount`. Samples are allocation events, **not bytes or collections**. This does not enable a production profiler by default.

## Corrected interpretation of previous reports

- Previous Unity `Prepare`, sprite warmup, simulation/math-helper and BufferText synchronous tests using only the raw thread-byte API did pass their then-existing assertions, but those observations do **not** establish allocation-free execution in this environment.
- In particular, the weighted BAT CPU/GPU `Prepare` statement of “0 main-thread bytes” and the BufferText ABBA “0 B” statement must be read as invalidated measurement evidence, pending calibrated revalidation.
- Actual shader/vertex/pixel comparisons, lifecycle tests, deterministic snapshots and allocation-independent math tests remain valid.
- The previously exported stamped-profiler `GC.Alloc` callstacks/bytes, valid whole-frame `GC Allocated In Frame` counters and independently reported `GC.CollectionCount(0)` deltas use different mechanisms. They are not invalidated by this thread-byte API finding.
- The 374/532 harness and 382/540 EditMode totals are historical runner results. Their raw-byte allocation assertions must not be promoted to valid Unity zero-allocation proof merely because the suite was green.

## New bounded evidence and remaining work

A separate TowerDefense HUD experiment, not a full-checkout A/B, adds the removed eager label expressions to the same production refresh. Using calibrated current-thread Recorder windows, 256 tower-label refreshes observed 1,024 allocation samples in each former-expression window and 0 after caching; 256 build-label refreshes observed 1,536 versus 0. The retained positive/empty controls passed before and after; completed generation-0 collection deltas were 0. This establishes the narrow formatting-path improvement, not attribution of the remote Mac 328-byte frame-counter residual.

A shared test-only calibrated probe and migration of affected assertions are being validated in the next checkpoint. They must fail an ineffective positive control or nonzero empty control, preserve explicit warmup, keep units visible, and never turn unavailable measurement into zero or loosen existing whole-frame thresholds. Precise Unity byte attribution continues to use stamped profiler/callstack capture.

This document is an evidence correction appended to the second checkpoint. Its code remains the already tested second-stage code; the separate HUD remediation/measurement-tool implementation is not silently included.
