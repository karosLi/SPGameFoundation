# .NET harness allocation accounting — 2026-10-06

## Failure and scope

The stage-four GitHub harness failed the unchanged natural-motion allocation test twice at remote commit `0b72f08c9f758c648d5e441e49d0e6bd90d90d86`: **3,312 bytes**, then **2,408 bytes**. Both runs measured 1,000 calls after 100 warm-up calls, with retained-array/empty controls of 33,536/0 before and after and a reported generation-0 collection delta of zero. The test's surrounding managed-footprint log fell from 15 MiB to 5 MiB.

This is an allocation-counter accounting investigation, not a gameplay allocation optimization. The pose, foot, aim, skeletal math and `ManagedAllocationProbe` measurement implementation are unchanged. The zero-byte assertion, positive/empty controls, 100-call warm-up and 1,000 measured calls are unchanged. No result is retried until it passes, discounted, rounded, or permitted a nonzero threshold.

## Independent Linux x64 reproduction

Local baseline: SDK 8.0.425, CoreCLR 8.0.31, Linux x64, Debug, workstation GC. Ten isolated natural-motion test processes, ten entire natural-motion fixture processes, and five complete SPF EditMode suite processes passed without imposed pressure. This alone did not establish reliability under CI's collection timing.

The checked-in diagnostic compares the actual pose/foot/aim body with an independent managed integer busy loop. That sentinel contains only loop control, integer arithmetic and static integer field access. Its measured body constructs no objects and calls no framework or gameplay methods. A separate worker retains an object graph, rotates large arrays and requests full collections. Storage, delegate binding, logging and assertions stay outside the measured windows. The exact production workload remains 1,000 calls after the original 100-call warm-up.

A matched eight-condition diagnostic recorded 256 windows per condition:

| Workload | Collection requests | Runtime concurrent GC | Nonzero windows |
| --- | --- | --- | ---: |
| Integer sentinel | nonblocking | enabled | 78 / 256 |
| Integer sentinel | nonblocking | disabled | 0 / 256 |
| Integer sentinel | blocking | enabled | 0 / 256 |
| Integer sentinel | blocking | disabled | 0 / 256 |
| Pose/foot/aim | nonblocking | enabled | 78 / 256 |
| Pose/foot/aim | nonblocking | disabled | 0 / 256 |
| Pose/foot/aim | blocking | enabled | 0 / 256 |
| Pose/foot/aim | blocking | disabled | 0 / 256 |

Without collection pressure, both workloads had 0/256 nonzero windows. A preceding independent event-capture pass observed 79/256 sentinel and 91/256 pose nonzero windows under background pressure, also 0/256 without pressure. Counts vary with scheduling; neither a particular byte count nor a particular incidence is required to reproduce the defect.

The runtime EventListener captured GC suspension/completion events in failing windows, including windows with a zero generation-0 delta. In the matched pass, 71/78 sentinel failures and 25/78 pose failures had a zero generation-0 delta. A collection can begin before a window and retire its allocation context during the window, so that delta alone is not a reliable exclusion of background-GC interference.

The same pre-bound workload delegate is used throughout all 256 windows. Nonzero readings continue well after its first invocation; warming only that first invocation is therefore not a repair for this failure mode.

There were no sampled allocation ticks on the measuring OS thread inside the recorded windows. Observed byte-array allocation ticks belonged to the pressure worker. **Ticks are sampled**, so their absence alone does not prove the absence of a small allocation or attribute the exact bytes from the original remote failures. The independent no-allocation sentinel, repeated workload controls and blocking/background contrast establish a runtime-counter failure mode without attributing it to NUnit, first delegate invocation or gameplay code. Positive controls all exceeded the required 32,768 payload bytes; some background-GC controls themselves overcounted. With blocking GC, every recorded retained/empty control was exactly 33,536/0.

This is consistent with the runtime's [background allocation-context accounting defect](https://github.com/dotnet/runtime/pull/134855). That upstream report originally concerns another runtime/architecture; the Linux x64 evidence above was independently obtained on our .NET 8 runtime. The original remote tests did not collect allocation stacks, so their individual 3,312/2,408 bytes cannot retrospectively be assigned an allocation caller with certainty.

## Bounded repair

Generated **.NET test projects only** set the supported MSBuild property `ConcurrentGarbageCollection=false`, which produces `System.GC.Concurrent=false` in their testhost runtime configurations. Collections still run, synchronously; real managed allocations remain visible to the unchanged current-thread byte counter. This changes neither the game assemblies' runtime configuration nor Unity/player/machine settings.

A harness-only regression requires the effective `Batch` GC latency mode, measures a nonallocating managed busy loop while another thread requests collections, keeps the strict zero-byte assertion, and separately proves that a retained managed array is still detected. Existing calibrated allocation tests still run. The harness now prints `dotnet --info` so future CI logs identify installed SDK/runtime versions.

The resulting claim is deliberately limited: **managed allocation behavior of the stubbed .NET logic paths under blocking GC**. Harness collection counts and timing do not represent production background-GC frequency, pause behavior, Unity or mobile performance. Real Unity recorder, profiler, whole-frame and device evidence retain their separate scopes.

Official configuration reference: [.NET background-GC settings](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/garbage-collector#background-gc). Latency-mode reference: [.NET latency modes](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/latency).

## Reproduce

From the repository root, with .NET 8 and the ordinary harness dependencies available:

```sh
Tools/DotnetHarness/run.sh
Tools/DotnetHarness/diagnose-allocation-accounting.sh
CONFIGURATION=Release Tools/DotnetHarness/diagnose-allocation-accounting.sh
```

The Linux-only diagnostic generates an independent console host under `.gen/AllocationDiagnostics`, leaving its GC mode selectable by process environment. For each workload it runs a concurrent-on/off/off/on ABBA sequence and an explicit blocking-request control. It writes every sample, retained/empty controls, gen0 deltas, effective runtime/latency mode, and timestamp-correlated GC/allocation-event thread/type evidence under `.gen/allocation-diagnostics-Debug` or `-Release`. A failed calibration is explicitly reported as unavailable, excluded from the valid-window denominator, and never retried or converted to zero. This diagnostic is not a pass-until-green test and is not run by the ordinary harness.

## Verification after repair

- Full Debug harness: **700 executed tests passed, zero failed**, including the new harness-only regression. Five additional fresh-process focused runs each passed all 28 natural-motion/probe tests; the pose window remained 0 bytes with unchanged 33,536/0 controls. The full Debug console log preserves its assembly totals; a fixed-name local TRX output was overwritten between assemblies and is not a complete machine-readable record.
- Full Release harness: **700 executed tests passed, zero failed**. All 24 test-project TRX files were retained with unique names; their aggregate counters are 702 total, 700 executed, 700 passed and zero failed. Both Debug and Release full builds reported zero warnings/errors.
- Debug diagnostic ABBA (concurrent on/off/off/on), nonzero windows out of 256: integer sentinel **82 / 0 / 0 / 79**; pose **84 / 0 / 0 / 92**. Explicit blocking requests: 0/256 for both.
- Completed Release diagnostic ABBA, nonzero windows out of 256: integer sentinel **75 / 0 / 0 / 63**; pose **33 / 0 / 0 / 26**. Explicit blocking requests: 0/256 for both; no unavailable windows in that completed matrix. A preceding concurrent-on Release condition aborted because its empty calibration itself read 6,512 bytes. That failure is preserved separately and prompted the explicit unavailable reporting described above; it was not counted as zero or a successful condition.
- Negative configuration control: restoring concurrent GC in a copied testhost runtime configuration makes the new regression fail (`Batch` expected, `Interactive` observed).
- Generated-project inspection: all **24 test projects** select blocking GC; all **51 non-test projects** lack that setting. A retained 1,024-byte payload allocation is still detected by the regression. No Unity/player validation is claimed for this .NET-only configuration change.
