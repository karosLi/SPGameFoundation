# Fourth checkpoint: playable variants and reusable character presentation

This checkpoint completes the implementable reference-capability list in `RequestedCapabilityChecklist.md`. It is a locally verified source checkpoint, not a claim of Android/iOS certification. Remote promotion is separately blocked by the Mac runner's saved Burst preference. The first published stage-four head also exposed a .NET allocation-accounting failure investigated below; no threshold is weakened to bypass either gate.

## What can be run

- `SvGameBootstrap.CreateFlyingSwordExample()`: portrait-first sword horde, fixed-capacity orbit/launch/homing/return swords, moving-target swept hits, stable per-sortie history, upgraded weapons, finite waves, UI upgrade/restart flow, shared pulse/blink controls, bounded blue/gold trails and aggregated damage numbers.
- `BwGameBootstrap.CreateBeltScroller()`: landscape-first ground-depth movement and independent height, depth/height-aware skeletal strikes, pursuit/separation grid, buffered three-hit combo, charged kick/launch, jump, healing and coin/health drops. Wave-clear collection retains movement while skills/recharge remain paused. Natural character presentation is selected by default.
- Both games consume `GameplayCharacterPresenter`: one original 14-bone hero/monster cutout rig, stable-handle visual state, bounded selection/sorting, walk/run/attack/hit/death/recovery, finite IK, contact recovery, airborne poses, and quality-independent simulation. Gameplay articulation is CPU/Burst; it is not being relabeled GPU skinning.
- The separate weighted BAT backend now has real optional compute-palette dispatch, sampled matrix texture interpolation, bounded two-bone IK and production draw, with GPU-vertex and CPU-weighted fallbacks. Its supported asset remains three bones, two normalized influences, two clips, at most256 actors. It is not an arbitrary skeleton/mesh importer.
- The previous portrait aircraft and guard examples, mobile HUD, layered FX, sampled silhouette-shadow showcase and blob/none fallbacks remain part of the cumulative foundation. Dynamic gameplay IK uses blob contact shadows; supported frozen pose samples can use the separate silhouette atlas.

Menu paths and a compiled-source-grounded minimal new-game recipe are in `NewGameplayIntegrationRecipe.md`. Ordinary defaults, classic tables and the actual97a2b34 Survivor snapshot fixture remain regression gates.

## Final local verification

Frozen code under test before this documentation commit: `cfbfbac67aaa6eb2365d989cdd468d53334745ea`.

| Gate | Result | Scope |
| --- | --- | --- |
| Serial .NET aggregate | **699 passed**,2 Explicit helpers not executed;75 generated assemblies,0 warnings/errors | Managed logic/API stubs;24 TRX files |
| Unity2022.3.62f2 full EditMode | **714 passed**,4 skipped,0 failed | CI-matching `-batchmode -nographics`;3 explicit helpers plus GPU-only upload accounting unavailable on Null graphics |
| Unity full graphics PlayMode | **122 passed**,1 explicit callstack-capture skip,0 failed | Original examples, both render tiers, new variants, HUD, natural characters, float/half BAT/compute, lifecycle/restart |
| Focused actual compute graphics |25/25 initially, then float/accepted-half production draw2/2 | Real dispatch, GPU coefficient readback, actual production draw, CPU/pixel parity, resource isolation/reuse |
| Focused final motion/gameplay |16 motion EditMode and10 actual gameplay cases passed | Both tiers and real automatic-clock recording; later included in full regression |
| Native belt workload |6/6 passed |32/64/128 fighters, spread/clustered, exact replay, counters and calibrated allocation |

The remaining normal skips are `SearchShots`, `ExportFrames`, explicit BufferText allocation diagnostic, GPU upload accounting in headless EditMode, and the explicit six-window Survivor callstack capture in PlayMode. BufferText was run explicitly and calibrated successfully in the prior cumulative checkpoint. These skips are not counted as passes.

The cloud graphics backend is OpenGLCore/Mesa llvmpipe on a desktop AMD EPYC environment. Native Unity jobs and shaders are exercised, but software rendering, desktop CPU timing and synthetic pointer events do not establish mobile driver support, physical touch delivery, battery or thermal behavior.

## Scoped performance and allocation evidence

- Native belt full-tick p95, spread32/64/128: **0.1102/0.1777/0.3283 ms** in the focused run. Clustered: **0.1594/0.3094/1.2900 ms**; clustered128 worst2.9481 ms. Main-thread game decisions/separation/hit loops and two completed grid builds are included. Dense peak separation16,002 candidates demonstrates the near-quadratic worst case; it is not hidden by grid terminology. These report-only desktop timings are not device budgets.
- Each native belt120-tick allocation window reported **0 current-thread GC.Alloc samples**, positive/empty controls32/0 before and after,0 independent gen-0 collections. Unexpected grid/feedback/hit/drop/scope overflow was0; one extra spawn rejection was deliberately introduced outside measurement.
- Sword workload:1,024 enemies/24 swords,120 measured ticks after45 warmup ticks, stable generation reuse. Initial native run58.48ms total,123,542 candidates,8,676 swept contacts,1,728 queued hits,5,438 history/pierce rejects,0 queue rejects,576 launches, calibrated0 allocation samples. Its old output prefix incorrectly said .NET/stub even in Unity; the final source now reports the actual runtime and explicit collection count. Timing includes probe overhead and is report-only.
- Full natural horde renderer at192 articulated enemies: **1.332/1.309 ms per synchronous call** in the final GPU/data-texture cases; calibrated0 current-thread allocation samples. This excludes full-frame/native/driver allocation and is not a GPU-time claim. Quality reductions retain cheap sprites for every remaining visible enemy and preserve simulation snapshots.
- Actual compute output: maximum coefficient error6.31e-5 for float and8.59e-6 for accepted half in the initial GPU readback. Production float draw matched26,018 occupied pixels with0 interior mismatches; an independent PNG comparison found exact pixel equality. Accepted-half production draw also passed.64 warmed dirty upload/dispatch/draw submissions recorded0 current-thread allocation samples and0 independent collections, with32/0 controls on both sides.
- At256 weighted actors, computed palette capacity is24,576 bytes, instance capacity16,384 bytes, and one dirty update submits four64-thread groups. GPU-written logical bytes are not CPU upload traffic or measured memory-bus bandwidth.

Whole-frame GC budgets remain unchanged. Final TowerDefense and Shooter windows recorded0/180 allocating frames in both tiers. Survivor auto-play still had small allocation bursts: GPU12/180 frames totaling1,968 bytes (9 near flow screens,3 steady); DataTexture17/180 totaling2,542 bytes (13 near flow,4 steady). Both recorded0 process-wide gen-0 collections. These are allocating frames, not collections. Passing the existing budgets does not establish zero whole-game allocation or identify all residual callers; the earlier stamped callstack investigation and unresolved attribution are retained.

## Exact published-head observations

Equivalent remote commit `0b72f08c9f758c648d5e441e49d0e6bd90d90d86` has the same tree as local `b8e6fbf0edcf833e72c33ad1db21b76e4dc85b00`. Its first Mac run passed **122 PlayMode tests**, with 1 explicit skip; EditMode passed **712**, skipped 4, and failed only the two actual-Burst-execution/physics-budget checks. The saved disabled Burst preference was again reported. This is not a fully green remote head.

The exact remote .NET run passed **698**, failed the natural-motion current-thread-byte assertion, and did not execute 2 Explicit helpers. A single unchanged-head .NET-only retry reproduced that failure: 3,312 bytes initially, then 2,408 bytes, with 33,536/0 positive/empty controls. A standalone integer-loop control independently reproduced nonzero byte accounting under background-GC pressure. The bounded test-host repair, reproducible controls and limitations are documented in [HarnessAllocationAccounting.md](HarnessAllocationAccounting.md); production motion code, strict zero assertions and the original measurement windows are unchanged. The prior local 699-test result above remains a correctly scoped historical result, not evidence that this remote failure did not occur.

The follow-on harness-only repair passed all **700 executed tests** in both Debug and Release locally; the added test verifies nonallocating windows under collection pressure and still detects a retained allocation. All 24 generated test runtime configurations select non-concurrent GC, while all 51 non-test projects remain unchanged. Independent review repeated 7 focused cases and the deliberately failing concurrent-enabled configuration control. Exact-head remote verification of this repair remains pending at this documentation commit.

The remote Survivor whole-frame windows also differ from the local run above. GPU measured 17/180 allocating frames totaling 2,501 bytes, 4 steady-state allocation frames, and **1 process-wide generation-0 collection**. DataTexture measured 8/180 frames totaling 1,312 bytes, no steady-state allocation frames, and 0 collections. The separate no-feedback diagnostic also observed 1 process-wide collection. TowerDefense and Shooter remained 0/180 allocating frames in both tiers. These test-runner/Editor-inclusive observations neither identify a specific allocation caller nor establish player/device collection frequency; they must not be summarized as globally zero GC.

## Review findings fixed before this checkpoint

- Prevented waveform count overflow from admitting billions of belt spawn attempts.
- Kept actual joystick/keyboard movement available during collection, masked action commands, and tested distant pickup after pause/resume.
- Included host variant in the sword snapshot contract, preventing Classic/Guard/FlyingSword terminal-flow divergence on restore.
- Synchronized direct sword presentation calls before reading pending native job data; the real in-flight-tick regression passed.
- Allocated a valid classic Survivor mask for native job safety, preserving opt-out rendering.
- Corrected cached-pose foot drift, vertical ground travel and reversal/turn recovery; bounded gait cadence instead of accelerating legs to hide unreachable plants.
- Replaced quadratic actor submission ordering with bounded heapsort and pre-sized identity containers.
- Preserved real video acquisition timing and separated readable motion capture from the dense stress fixture.

Independent motion checks covered96 movement cases at30/60/120Hz, four production speed/scale profiles and both facings;3,000 identity/reorder frames and38,873 submissions passed. Two of41,736 grounded samples retained a2.089mm miss because their target lies inside the unequal leg lengths' minimum IK radius. The solver correctly clamps; the documentation does not promise impossible exact contact there. The final-version stop/jump/landing rerun covered22,181 contacts with maximum1.034343e-6 error and no samples above0.0002. The earlier22,282/max1.33e-6 observation was immediately before the final hip/margin refinement and is retained only as historical evidence. Actual final frames were separately inspected; math tests alone are not the visual acceptance gate.

Controlled flow fixtures deliberately shorten/configure runs and sometimes stage KO/final-wave state. They prove UI and transition wiring, not that a human naturally completed the default multiwave difficulty. The readable horde video uses six representative enemies at authored speeds, suppressing enemy projectiles after the separate224-enemy visibility/allocation test. Its sidecar records this setup.

## Evidence and remaining external gates

- `Artifacts/Stage4/Final/`: final Unity XML and .NET TRX.
- `Artifacts/Screenshots/MobileHud/`: unmodified game-camera/all-canvas PNGs, bounds/raycast sidecars, actual acquisition CSV and ffconcat.
- `Artifacts/Bat/`: actual weighted/compute captures.
- `Artifacts/perf-belt-unity-native-editmode-*.txt`, `perf-physics-backend.txt`: scoped measurements.
- Two delivered gameplay MP4s preserve the30 acquisition times within0.5ms, with a final repeated frame to retain display duration; they are not interpolated imagery or device frame-pacing measurements.

Remote stage3 diagnostic0a2209be proved the Mac runner's persistent `BurstCompilation` preference is false. Options/Jobs flags are false; independent direct/Run/Schedule controls are0/0/0 despite an initialized compiler service. That explains the managed physics benchmark on that run. The preference-changing setup is held separately pending explicit owner approval; this checkpoint does not silently activate it. No local green result is substituted for that remote gate, and the validated branch stays on its last green head until the exact published head passes CI.

Physical Android/iOS testing requires hardware/access not supplied in this task. Other future work (arbitrary weighted-rig importing, dynamic terrain/occlusion shadows, full editor authoring tools, legacy projectile-row-history migration) is outside this finite implemented slice and remains explicitly distinct from completion of the confirmed capability checklist.
