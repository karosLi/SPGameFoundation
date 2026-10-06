# Belt-scroller complete-logic workload report

## Scope and method

`BwBeltWorkloadTests.FullBeltLogicWorkloadReport` runs six cases: 32, 64 and 128 fighters, each initially spread across the arena or tightly clustered near its center. Enemies use real pursuit/attack rules and the player receives deterministic movement/skill input; high HP prevents a death from shrinking the measured population. One extra spawn attempt in setup intentionally verifies rejection at capacity.

Each case warms 90 full session ticks, resets to its authored density outside measurement, then times 120 complete evolving ticks. Actors are not teleported back into a cluster each tick. A separate 120-tick allocation window starts from the same reset density. Exact snapshot replay and equal query counts are asserted against a second world driven by the same input. Calibration runs before and after allocation sampling. Report formatting, snapshots, timing arrays and file output are outside measured windows.

The timed scope includes flow, input/skills, every fighter's AI/movement, spatial separation, hit resolution, loot scanning, session synchronization, both completed grid builds, and one feedback-queue clear per tick. It excludes rendering, GPU upload, full-frame/other-thread/native allocations and sustained device behavior. Queue totals include drained and pending overflow.

This is **report-only performance evidence**, with no arbitrary timing or allocation threshold. Population bounds, deterministic replay, covered-grid correctness and allocation-probe calibration remain assertions.

## Standalone harness sample, 2026-10-06

All **55/55 Brawler tests passed**, including all six workload cases. These measurements are from .NET 8 stubs on the shared cloud execution host. They do not establish native Unity, Burst or mobile performance. Host scheduling can vary p95/worst readings even when snapshots and candidate counts remain identical.

| Fighters | Initial layout | Mean ms | p50 ms | p95 ms | Worst ms | Separation total / peak | Hit total / peak |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 32 | spread | 0.0854 | 0.0837 | 0.1032 | 0.1550 | 3,326 / 72 | 274 / 20 |
| 32 | clustered | 0.1208 | 0.1088 | 0.1910 | 0.2976 | 13,862 / 988 | 549 / 23 |
| 64 | spread | 0.2005 | 0.1945 | 0.2823 | 0.3961 | 10,584 / 178 | 773 / 43 |
| 64 | clustered | 0.3172 | 0.2384 | 0.9629 | 1.5653 | 40,104 / 4,002 | 209 / 30 |
| 128 | spread | 0.4258 | 0.4189 | 0.5186 | 0.6344 | 41,658 / 472 | 692 / 64 |
| 128 | clustered | 0.7609 | 0.5644 | 1.8235 | 4.0452 | 126,142 / 16,002 | 1,564 / 80 |

All six cases reported:

- Exact snapshot replay: pass, both timed and allocation windows
- Grid drops, feedback overflow, rejected drops, rejected hits and rejected scopes: zero
- Rejected spawns: one, from the deliberate setup overflow request
- Feedback peak: 3–9 pending events out of 128 slots
- Allocation window: zero current-thread managed bytes, zero process-wide gen0 collections
- Retained-array/empty calibration: 33,536/0 bytes before and after

## Important boundary

The grid builds are scheduled Burst-capable work. **Fighter decisions, separation visitors and hit loops currently execute on the main thread.** This report does not imply that the whole belt simulation runs as Burst jobs.

At 128 tightly clustered fighters, the separation peak was **16,002 directed neighbor candidates**, nearly the 16,256 all-pairs maximum. A spatial grid removes distant-pair work; it cannot remove genuine local overlap. Dense worst-case work is still quadratic, and this slice is deliberately bounded at 128 simulation fighters. The measurement did not expose warmed managed allocation in the standalone logic, but it does expose the density scaling limit. No renderer quality change is permitted to reduce these simulation queries.

## Native central run

The same fixture is compiled without `SPF_DOTNET_HARNESS` into the Unity EditMode test assembly. Run filter `BrawlerFoundation.Tests.BwBeltWorkloadTests` centrally. It executes native session/job paths, uses the calibrated current-thread Unity GC.Alloc sample metric, and writes six `Artifacts/perf-belt-unity-native-editmode-{count}-{layout}.txt` reports. Positive allocations must be reported rather than relabeled as zero; a failed calibration is unavailable evidence.

Harness reports use `perf-belt-dotnet-stubs-{count}-{layout}.txt` in the temporary `spf-artifacts` directory. Native output must be retained separately from these harness numbers. Native Unity execution and target-device validation were not run by the implementation worker.
