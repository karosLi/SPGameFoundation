# Mobile presentation, skill HUD and calibrated allocation checkpoint

This report tracks the third integration stage. `CurrentWipStatus.md` is the preserved publication-freeze record; its “not yet run” statements describe that earlier snapshot. The source now contains the later verification and fixes below. Local verification is complete for this implementation; remote Mac verification remains the promotion gate. No unfinished later gameplay variants are included in this checkpoint.

## Scope

- Shared fixed-tick skill slots and mobile HUD, used by Brawler and Survivor: joystick, click/hold/aim-release/cancel, authoritative cooldown/charges, input ownership and interruption, portrait/landscape/safe-area layouts.
- Shared bounded layered combat effects and polished original aircraft/guard art. Critical feedback admission is protected from lower-priority floods; quality affects presentation only.
- Natural-motion character showcase: original 14-bone hero/monster cutouts, planted feet, smoothed IK, bounded pose cadence, sampled silhouette shadows and explicit dynamic-IK blob fallback. Actual gameplay character adapters are a separate remaining item.
- TowerDefense unchanged-price label caching and calibrated, explicit-unit allocation tests.

## Correct measurement units

The installed Unity runtime returned zero from `GC.GetAllocatedBytesForCurrentThread` even for 32 retained 1 KiB arrays. That byte counter cannot support a zero-allocation claim here. The testing-only `ManagedAllocationProbe` uses current-thread `GC.Alloc` recorder samples in Unity and calibrated managed bytes in .NET. Both have positive and empty controls before/after; process-wide collection counts are independent. Unavailable calibration fails rather than returning zero.

The migrated warmed synchronous paths include Brawler ticks, Shooter ticks, combat queries/history, FX emit/merge/pack, blob appending, natural-pose math, shadow selection, skill slots, data-texture uploads, BAT instance updates and crossed-blade ticks. Their scope excludes deferred canvas work, other threads, native memory and full-frame allocation. Whole-frame budgets remain unchanged.

The explicit BufferText ABBA diagnostic, run on 2026-10-06 19:06 UTC, passed its retained/empty controls at 32/0 samples before and after. Each 1,000-call single-line/multiline window and isolated newline-request window recorded zero current-thread allocation samples, zero font rebuild callbacks and zero independent gen-0 collections. This tests direct warmed mesh population only; it does not settle deferred canvas or whole-frame behavior.

## Verified before the final regression

Local Unity 2022.3.62f2, Linux, OpenGLCore/Mesa llvmpipe:

- 624 EditMode cases: 621 passed, 3 explicit diagnostic skips, no failures.
- 18 targeted PlayMode cases: all passed (BAT, natural motion, mobile HUD, TowerDefense label behavior/probes).
- Refined natural-motion recording: 30 actual rendered frames, 10 fps, 3 seconds. No interpolation or synthetic animation. Both sprite tiers and portrait aspect-fit output inspected.
- The first real mobile HUD smoke exposed missing CanvasRenderer ownership. The requirement/factory was fixed and real submitted geometry/raycast tests added. A later remote compile exposed use of the wrong `GetMesh` overload; the Unity 2022 parameterless API is now used without destroying its borrowed mesh.

Exact remote WIP 93937656ccfb5d09b0ab04b46e48f8fea9624823 (equivalent local 772e42d), Mac/Metal:

- .NET passed.
- PlayMode: 90 passed, 1 explicit skip, no failures.
- EditMode: 618 passed, 4 skipped, 2 failures. Survivor exact pulse tangency and the unchanged physics benchmark are under repair; this is not a green overall result.
- TowerDefense original whole-frame budgets passed both tiers: 0/180 allocating frames, 0 bytes, 0 independent gen-0 collections. Earlier failing windows remain evidence; a passing rerun does not establish the cause of the prior residual.
- Narrow TowerDefense label comparison reproduced: former tower/build expressions recorded 1,024/1,536 allocation samples per 256 refreshes, fixed windows zero; positive/empty controls 32/0 before and after. These event counts are not bytes, and this comparison is not a full-checkout A/B.

## Remaining integration gates

The four real HUD capture cases now exercise 1280×720 Brawler and 720×1280 Survivor in both sprite tiers, including synthetic notch/home-indicator safe areas. They capture all game-owned canvases and assert real camera-space raycasts, control bounds, visible icons/status and actual skill effects. The full local run completed at 19:13 UTC: **94 passed, 1 explicit allocation-capture skip, 0 failures**. All four new HUD capture cases passed and produced fourteen PNGs; landscape cooldown and portrait aim/safe-area pixels were inspected. The explicit warmed BufferText diagnostic passed separately.

The full-run frame evidence still contains small Survivor allocations: GPU auto-play 12/180 allocating frames totaling 1,968 bytes (9 next to level-up/flow and 3 classified steady); data-texture 17/180 totaling 2,624 bytes (13 near flow and 4 steady). Both windows recorded **zero process-wide gen-0 collections**. These are allocating frames, not collections. The existing budgets passed without modification, but this is not a whole-game zero-allocation claim or proof of the remaining callers. Earlier stamped callstack investigation and its unresolved attribution remain documented separately. TowerDefense and Shooter both recorded 0/180 allocating frames in each tier in this full run.

The finite remaining feature list is in `RequestedCapabilityChecklist.md`. Physical Android/iOS touch, native graphics, sustained thermal/battery and frame-time validation are blocked on device availability. Linux software rendering and desktop Mac CI are not mobile performance guarantees.

## CI remediation and final local checks

- The physics source/workload/settings were byte-identical between the earlier 0.449 ms Mac pass and the later 15.958 ms failure. That older failure lacked an execution witness. Synchronous first compilation now prevents unbounded managed warmup; a BurstDiscard witness records the actual execution path without changing snapshots or allocating. Local full EditMode passed 622/625 with three explicit skips; native benchmark mean **1.039 ms**, worst 4.274 ms, 60/60 warmup and 300/300 measured native steps. The existing assertion is mean <4 ms; it is unchanged. This supports readiness correction, not a claimed solver algorithm speedup or retrospective proof of the old failure cause.
- Inclusive annulus boundaries now round combined bounds explicitly to binary32 and compare both squared sides in double precision. There is no epsilon expansion: the immediately adjacent representable outside value misses. Managed and synchronous Strict/Fast Burst tests cover inner/outer edges on four axes, several radii, large finite coordinates and actual grid candidates. The previous exact Mac lowering remains unproven until remote diagnostics.
- After both runtime fixes: **634 EditMode passed, 3 explicit skips, no failures**; **6 affected Survivor PlayMode cases passed**, including actual pulse/blink/rings/restart and portrait safe-area captures in both sprite tiers. The earlier complete all-game run remains **94 passed, 1 explicit skip**. The final CanvasCapture cleanup restores scale with and without a CanvasScaler; its focused rerun plus both Sling physics gameplay tiers passed **5/5**. The final full EditMode physics sample was mean **1.685 ms**, worst 25.522 ms while other local builds were active, with native witness 60/60 and 300/300; no device throughput conclusion follows from this desktop timing.
- Thresholds, seeds, body counts, gameplay allocation windows and normal HUD/audio paths were not weakened.

Final serial .NET aggregate: all 75 generated assemblies built with zero warnings/errors; **618 passed**, two Explicit helpers (`ExportFrames`, `SearchShots`) not executed, zero failures. Twenty-four TRX files retain the exact test results. Code review of the precision, execution-witness and capture-restoration fixes was separate from their author. Source is frozen for this publication checkpoint; later sword/belt/natural-gameplay/compute changes remain in isolated branches.
