# Portrait shooter integration

## Run

- Unity 2022.3 LTS: **SPF → Shooter → Create Or Update Scene**, open `Assets/ShooterFoundation/Scenes/Shooter.unity`, Play.
- For a portrait mobile build, explicitly select **SPF → Shooter → Apply Portrait Mobile Settings**. Scene creation does not silently change global settings: this shared multi-mode project may still be configured for landscape until that separate menu is selected. Native device orientation has not been validated.
- Or add `ShooterFoundation.Game.ShooterGameBootstrap` to an empty GameObject.
- Code: `ShooterGameBootstrap.Create(RenderTier.DataTexture)` or `.Create(RenderTier.GpuDriven)`; omit tier for hardware detection. Optional `ShooterConfig` and seed follow the tier argument.
- Touch/mouse drag anywhere in the play field, or WASD/arrows. Fire is automatic. Choose one of three upgrades before launch and between waves. Five waves lead to victory; zero hull leads to defeat. Both results support restart and menu.
- `AutoPlay` is a validation/attract-mode helper, not the authoritative AI.

## Reference interpretation and original art

The supplied first three mobile references were inspected as actual image pixels. This integration adopts their portrait forward-shooting flow, small wing partner, incoming enemy waves, health and collectible rewards, three-way upgrade choice, and nearest-target continuous ray. It does not reproduce their characters, aircraft silhouettes, logos, UI wording or third-party assets.

`ShooterArt` creates original clean outlined aircraft, coral drones, armored enemies, cyan bolts, gold salvage and mint repair tokens. Shapes are rasterized at 4× resolution and alpha-aware downsampled, then packed with bilinear filtering and two-pixel extruded gutters. These are smooth, non-pixel placeholder sprites. They can be replaced through the shared imported-image atlas APIs without changing simulation.

All soft sprite edges use premultiplied translucency with explicit render queues. Stable Y/ID sorting is used inside the aircraft batch. Background → clouds → blob shadows → pickups → aircraft → ray → bullets → impact rings is explicit, identical on both rendering tiers. Merely turning on bilinear filtering in a cutout batch is intentionally avoided.

## Simulation and mobile design

- `ShooterMode`/`SessionHost` retain the foundation's fixed 30 Hz simulation; CPU state is authoritative.
- Enemy, bullet and pickup data live in fixed-capacity pooled SoA tables. Default limits: 512 enemies, 4,096 projectiles and 512 pickups. Spawn overflow is counted; commands use a fixed 16-entry ring and count rejected writes.
- A bounded `ApplyCommands` transaction owns structural changes. Burst parallel jobs move enemies and bullets. A shared `SpatialGrid` is rebuilt by counting sort. Each projectile writes only its own earliest-hit result; the main thread reduces hits in row order after completion.
- The broad phase uses swept enemy bounds and projectile segment bounds. Narrow phase uses relative-motion circle sweeps, including initial overlap/tangency. Fast movers therefore do not rely on a discrete overlap at the end of a tick.
- The ray queries nearby grid cells, excludes dead enemies, selects nearest center inside range, and breaks ties by stable spawn ID. Enemy sine motion and aimed fire, wave generation, upgrade choices, pickups and weapon cooldowns all live in the simulation.
- Snapshots include run state, pending commands, tables and grid; scratch hit buffers are derived and rewritten each tick.
- No per-enemy GameObjects, physics colliders, particle systems or material instances. Sprite batches and data-texture pages are warmed once. Gameplay hot loops create no managed collections or delegates.
- `FrameGovernor.LevelChanged` only reduces cloud/shadow/impact and beam-glow presentation budgets. Simulation population, collisions, damage, targeting and tick rate are unchanged.
- Blob shadows are artistic contact cues, not occlusion/shadow mapping. This slice does not add weighted mesh or GPU skeletal skinning; see the shared integration notes for the honest skeletal boundary.

## Validation and evidence

EditMode tests cover three unique choices and paused upgrade time, duplicate/invalid choices, earliest swept hit, moving-enemy sweep, tangent/zero-length sweep, nearest-target tie-breaking, pickups/HP clamping, bounded pools/command overflow, cancel/non-finite input/bounds, win/death/restart/menu, same-seed determinism, exact snapshot continuation, and zero managed bytes over 180 warmed simulation ticks.

`ShooterCollisionBenchmark.DenseSpatialSweepCandidateAndTimingReport` runs 1,024 enemies and 4,096 swept queries, records actual narrow-phase candidate counts against 4,194,304 Cartesian pairs, and reports p50/p95 after warmup. The microbenchmark excludes grid rebuild, rendering and upload; its desktop/harness timing is not a mobile frame-rate or battery claim. Report: `Artifacts/perf-shooter.txt` in Unity, `/tmp/spf-artifacts/perf-shooter.txt` under the .NET harness.

PlayMode tests exercise both `GpuDriven` and `DataTexture`: actual UI launch/choice, two-pointer rejection, cancel/disable stale-input prevention (including native canceled-release routing, foreign-pointer rejection, and preserved final movement on normal release), kills and victory, restart/death/menu, renderer activity, and a separate 150-frame warmup + 180-frame allocation window with the existing strict ≤2 allocating-frame budget. Quality changes are checked against an unchanged simulation snapshot.

The playing screenshot uses an explicit **test-only fixture**: three visible enemies (one durable nearest ray target), a repair token and a salvage token. The normal simulation and bot must still kill these enemies and finish the full win/death/restart flow; production waves are unchanged. Screenshot assertions separately check actual white telemetry glyph pixels and the golden ray region, beyond a generic non-empty-frame check.

PlayMode screenshot output:

- `Artifacts/Screenshots/shooter-upgrade-gpu.png`
- `Artifacts/Screenshots/shooter-upgrade-datatex.png`
- `Artifacts/Screenshots/shooter-playing-gpu.png`
- `Artifacts/Screenshots/shooter-playing-datatex.png`

### Current verification status

2026-10-06: Unity **2022.3.62f2, Linux x86_64** EditMode execution passed all **16 Shooter cases**. Evidence: `Artifacts/editmode-results.xml` (run ended 16:06:33 UTC), `Artifacts/editmode.log`. This includes actual Editor asset create/save/import/load, exact snapshot continuation, and zero managed bytes over 180 warmed simulation ticks. All six Shooter assemblies also compiled in the .NET harness; that aggregate build was still blocked by an unrelated assembly at this checkpoint.

The Unity collision microbenchmark recorded **4,290 narrow-phase candidates / 4,194,304 Cartesian pairs (0.102%)**, 3,036 hits, **0.519 ms p50 / 0.569 ms p95** over 30 samples after 10 warmups. Evidence: `Artifacts/perf-shooter.txt`. This is desktop Editor collision-only timing, not a mobile full-frame or battery measurement.

The first complete PlayMode run passed all four Shooter cases: GPU-driven steady play recorded 0/180 allocating frames (0 bytes), data-texture steady play 1/180 (158 bytes), within the unchanged ≤2-frame budget. However, actual screenshot inspection caught missing telemetry glyphs despite these initial passes. A focused probe proved that shared `BufferText` lacked a `CanvasRenderer`; requiring that component restored 1,151 white telemetry pixels and 116 mesh vertices before any manual rebind. Rebinding did not change the result, so the font-atlas-rebind hypothesis was not confirmed. Evidence is retained in `Artifacts/font-probe-missing-canvas-results.xml` and the parent-owned successful probe outputs.

The permanent regional glyph/ray checks and controlled playing screenshot fixture are now in place; final full regression and final screenshot inspection are pending at this checkpoint. The graphics environment uses Mesa llvmpipe software rendering, so tier coverage does not establish hardware GPU or GLES-device performance. No on-device touch, thermal or battery result is claimed.

Final aggregate verification (2026-10-06): Unity EditMode 382 passed / 3 explicit skipped; Unity PlayMode 67 passed / 1 explicit skipped; .NET 374 passed / 2 explicit unexecuted. Final Shooter steady windows were 0/180 allocating frames and 0 bytes on each tier. Both playing and upgrade screenshots were visually inspected after the CanvasRenderer fix. See [MobileGameplayCheckpoint](MobileGameplayCheckpoint.md) for scope and evidence.

## Retained steady-allocation failure and frame evidence (2026-10-08)

On exact `67a22c1a64f029aed992ba9f508315b0e60fdc1b`, [native run 37747724041](https://github.com/karosLi/SPGameFoundation/actions/runs/37747724041) failed `WarmSteadyFrameAndPresentationOnlyQuality(GpuDriven)`: **3 of 180** frames allocated, **123 bytes total**, against the unchanged maximum of **2** allocating frames. DataTexture reported 0/180 and 0 bytes. The original test XML and `Artifacts/perf-gc.txt` are preserved. That run did not retain the three individual frame indices, their byte split or allocation callstacks, so the source cannot be attributed and three equal allocations cannot be inferred. The complete run remains failed even though the unrelated new Horde health-bar pixel cases passed.

The fixture now preallocates 180 plain-value records before the same 150-frame warmup. During the unchanged measured window it copies the existing governor's previous-frame allocation sample/cumulative bytes, observed Unity frame, governor frame index and current tick/flow/version/kills/wave. All string formatting and file IO happen after the window; `Artifacts/shooter-steady-frame-samples-{GpuDriven,DataTexture}.csv` is saved before the unchanged assertion, including on a failing run. These observations do not add a profiler, alter gameplay, filter frames or attribute allocation bytes to the current simulation tick. No window or threshold is relaxed.

The modified complete C# fixture compiles against its direct assembly references, official Unity NUnit 3.5 and native test-runner attribute sources, with zero errors and the existing disclosed indirect NUnit-version warning. Engine APIs in this cloud check remain stubs; it is not real Unity compilation or native measurement. Fresh native samples are pending the next planned integrated run. This instrumentation is a diagnosis aid, not a fix or a claim that the original failure was harmless.
