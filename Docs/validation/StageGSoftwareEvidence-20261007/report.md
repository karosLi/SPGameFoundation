# Stage G bounded mobile-budget evidence

Commit `0ffa2ee356bdda740bbcf8f2b91c63c4eb7fae9f`, tree `b15d04e4fae26242060b5c245ed32e4a8c5ba767`.

Android: **Pending**. iOS: **Pending**. Measurements below are simulation-only software probes.
Profiles are configurable goals. No result below demonstrates a physical-device budget.

## external.courier-test-only / parcel8-rule-only
- Session: external.courier-test-only/seed-123; seed 123; 30 Hz; orientation intent none (no rendered viewport)
- Actual backend: simulation-only/no-renderer; runtime: dotnet-unity-stubs; platform: Unix 6.18.44.0
- Timing: Stopwatch around fixed input + SimSession.Step; input and sync included; no render or frame pacing; ticks [16, 80); warmup 16
- Fixed-step timing p50/p95/worst: 0.000671/0.000741/0.001573 ms (64 samples, nearest-rank; not frames)
- PipelineStats end EMA: schedule 0.000377, sync wait 0.000028, wall 0.000563 ms. PipelineStats, EMA alpha 0.1 since construction; ordinary pipelined execution, not worker CPU time
- Allocation: 0 ManagedBytes, thread 14, ticks [80, 144); process gen0 collections 0
- Retained/empty controls before: 33536/0; after: 33536/0; controls excluded from measured window
- Create failures 0; destroy overflow 0. Cumulative since factory construction, read after timing and allocation windows; peak_after_tick is timing window only, not sub-tick pool high-water
- Default factory capacities match: None; custom fixture capacity must not be presented as default-load evidence.
  - ExternalCourier.Parcel: capacity 8, completed-tick peak 1, final 1
- Quality scope: Unsupported: test-only rule module has no renderer or quality controls; it is not a polished playable demo.
- Views: Unsupported: no views or asynchronous assets are created by this test-only module.

## platformer.classic / PlMode-default
- Session: platformer.classic/seed-123; seed 123; 60 Hz; orientation intent landscape (no rendered viewport)
- Actual backend: simulation-only/no-renderer; runtime: dotnet-unity-stubs; platform: Unix 6.18.44.0
- Timing: Stopwatch around fixed input + SimSession.Step; input and sync included; no render or frame pacing; ticks [17, 81); warmup 16
- Fixed-step timing p50/p95/worst: 0.006249/0.007621/0.013140 ms (64 samples, nearest-rank; not frames)
- PipelineStats end EMA: schedule 0.008071, sync wait 0.000028, wall 0.008275 ms. PipelineStats, EMA alpha 0.1 since construction; ordinary pipelined execution, not worker CPU time
- Allocation: 0 ManagedBytes, thread 14, ticks [81, 145); process gen0 collections 0
- Retained/empty controls before: 33536/0; after: 33536/0; controls excluded from measured window
- Create failures 0; destroy overflow 0. Cumulative since factory construction, read after timing and allocation windows; peak_after_tick is timing window only, not sub-tick pool high-water
- Default factory capacities match: True; custom fixture capacity must not be presented as default-load evidence.
  - Pl.Walker: capacity 128, completed-tick peak 2, final 2
  - Pl.Platform: capacity 64, completed-tick peak 1, final 1
  - Pl.Coin: capacity 512, completed-tick peak 11, final 11
- Quality scope: Unsupported: PlRenderer has no per-mode quality setter; renderer/backend and locomotion rebind are separate existing graphics PlayMode tests.
- Views: This EditMode adapter creates no views; PlPlayTests.LocomotionTransitionsPauseAndRebind owns real view rebind coverage.

## shooter.default / enemy8-bullet32-pickup8-no-waves
- Session: shooter.default/seed-123; seed 123; 30 Hz; orientation intent portrait (no rendered viewport)
- Actual backend: simulation-only/no-renderer; runtime: dotnet-unity-stubs; platform: Unix 6.18.44.0
- Timing: Stopwatch around fixed input + SimSession.Step; input and sync included; no render or frame pacing; ticks [18, 82); warmup 16
- Fixed-step timing p50/p95/worst: 0.029434/0.035403/0.058256 ms (64 samples, nearest-rank; not frames)
- PipelineStats end EMA: schedule 0.030148, sync wait 0.000037, wall 0.030523 ms. PipelineStats, EMA alpha 0.1 since construction; ordinary pipelined execution, not worker CPU time
- Allocation: 0 ManagedBytes, thread 14, ticks [82, 146); process gen0 collections 0
- Retained/empty controls before: 33536/0; after: 33536/0; controls excluded from measured window
- Create failures 0; destroy overflow 0. Cumulative since factory construction, read after timing and allocation windows; peak_after_tick is timing window only, not sub-tick pool high-water
- Default factory capacities match: False; custom fixture capacity must not be presented as default-load evidence.
  - Shooter.Enemy: capacity 8, completed-tick peak 0, final 0
  - Shooter.Bullet: capacity 32, completed-tick peak 12, final 9
  - Shooter.Pickup: capacity 8, completed-tick peak 0, final 0
- Quality scope: Actual CombatVfxPool admission/overflow under qualities 0 and 3; no renderer pixels or GPU execution.
- Views: This EditMode adapter creates no views. Real multi-view/host retirement is covered by SessionHostRetirementTests and graphics PlayMode suites.

## Unknowns (not zeros)
- cpu_worker_ms: PipelineStats main-thread schedule/wait is not worker CPU time.
- gpu_ms: No renderer/GPU profiler is active in this simulation-only fixture.
- managed_live_bytes: Allocation deltas are not live managed memory.
- native_live_bytes: No native-memory counter sampled.
- gpu_live_bytes: No GPU memory counter sampled.
- upload_payload_bytes: No presentation submission in this fixture. PackedPayloadBytes may be supplied by separate graphics evidence.
- upload_api_bytes: No draw/upload occurs in this fixture. SpriteBatch/ParticleRenderer/BatCharacterBatch.BytesUploaded are separate API accounting.
- physical_gpu_traffic_bytes: API payload/upload counts never measure physical bus/GPU traffic.
- pool_subtick_high_water: Completed-tick table maxima do not capture transient sub-tick peaks.
- input_rejection_count: No common input rejection counter; game-specific input regressions are separate.
- allocation_frame_count: Synchronous tick allocation probe has no rendered-frame sample.
- overdraw: VfxBudget.ScreenArea is a transparent-quad coverage budget, not measured GPU overdraw.
- thermal: No physical Android/iOS device capture.
- battery: No physical Android/iOS battery measurement.

## Per-mode profile goals
Each mode has the four declared quality levels and Android/iOS candidate APIs. All physical gates remain Pending.
- snake.classic: landscape, 60 FPS goal; capacities {"Food": 24000, "Projectile": 512, "Prop": 512, "Snake": 320}. FrameGovernor render-scale goals only. This mode has no common per-game decoration-quality contract; validate actual renderer mapping separately.
  - Compatibility exception: Classic Snake AI quality throttling is recorded replay input; this legacy mode cannot claim pure presentation-only quality.
- rpg.classic: landscape, 60 FPS goal; capacities {"Rpg.Actor": 512, "Rpg.Item": 512, "Rpg.Projectile": 256, "Rpg.Prop": 256}. FrameGovernor render-scale goals only. This mode has no common per-game decoration-quality contract; validate actual renderer mapping separately.
- survivor.classic: portrait, 60 FPS goal; capacities {"Sv.Bullet": 32768, "Sv.Enemy": 4096, "Sv.Gem": 8192}. Actual game renderer quality hooks and optional CombatVfxPool use bounded presentation budgets. Four tiers preserve authoritative table capacities; input/hit/danger readability needs graphics and device evidence.
- platformer.classic: landscape, 60 FPS goal; capacities {"Pl.Coin": 512, "Pl.Platform": 64, "Pl.Walker": 128}. FrameGovernor render-scale goals only. This mode has no common per-game decoration-quality contract; validate actual renderer mapping separately.
- defense.classic: landscape, 60 FPS goal; capacities {"Td.Enemy": 1024, "Td.Shot": 2048, "Td.Tower": 256}. FrameGovernor render-scale goals only. This mode has no common per-game decoration-quality contract; validate actual renderer mapping separately.
- puzzle.classic: portrait, 60 FPS goal; capacities {}. FrameGovernor render-scale goals only. This mode has no common per-game decoration-quality contract; validate actual renderer mapping separately.
- sling.classic: landscape, 60 FPS goal; capacities {}. FrameGovernor render-scale goals only. This mode has no common per-game decoration-quality contract; validate actual renderer mapping separately.
- brawler.classic: landscape, 60 FPS goal; capacities {"Bw.Fighter": 64}. Actual game renderer quality hooks and optional CombatVfxPool use bounded presentation budgets. Four tiers preserve authoritative table capacities; input/hit/danger readability needs graphics and device evidence.
- story.classic: landscape, 60 FPS goal; capacities {}. FrameGovernor render-scale goals only. This mode has no common per-game decoration-quality contract; validate actual renderer mapping separately.
- shooter.default: portrait, 60 FPS goal; capacities {"Shooter.Bullet": 4096, "Shooter.Enemy": 512, "Shooter.Pickup": 512}. Actual game renderer quality hooks and optional CombatVfxPool use bounded presentation budgets. Four tiers preserve authoritative table capacities; input/hit/danger readability needs graphics and device evidence.
- survivor.guard: portrait, 60 FPS goal; capacities {"Sv.Bullet": 4096, "Sv.Enemy": 1024, "Sv.Gem": 2048}. Actual game renderer quality hooks and optional CombatVfxPool use bounded presentation budgets. Four tiers preserve authoritative table capacities; input/hit/danger readability needs graphics and device evidence.
- survivor.flying_sword: portrait, 60 FPS goal; capacities {"Sv.Bullet": 1024, "Sv.Enemy": 1024, "Sv.Gem": 2048}. Actual game renderer quality hooks and optional CombatVfxPool use bounded presentation budgets. Four tiers preserve authoritative table capacities; input/hit/danger readability needs graphics and device evidence.
- brawler.belt: landscape, 60 FPS goal; capacities {"Bw.Fighter": 64}. Actual game renderer quality hooks and optional CombatVfxPool use bounded presentation budgets. Four tiers preserve authoritative table capacities; input/hit/danger readability needs graphics and device evidence.
- survivor.crossed_blades: portrait, 60 FPS goal; capacities {"Sv.Bullet": 32768, "Sv.Enemy": 4096, "Sv.Gem": 8192}. Actual game renderer quality hooks and optional CombatVfxPool use bounded presentation budgets. Four tiers preserve authoritative table capacities; input/hit/danger readability needs graphics and device evidence.
- survivor.mobile: portrait, 60 FPS goal; capacities {"Sv.Bullet": 4096, "Sv.Enemy": 1024, "Sv.Gem": 2048}. Actual game renderer quality hooks and optional CombatVfxPool use bounded presentation budgets. Four tiers preserve authoritative table capacities; input/hit/danger readability needs graphics and device evidence.
- survivor.weapons: portrait, 60 FPS goal; capacities {"Sv.Bullet": 4096, "Sv.Enemy": 1024, "Sv.Gem": 2048}. Actual game renderer quality hooks and optional CombatVfxPool use bounded presentation budgets. Four tiers preserve authoritative table capacities; input/hit/danger readability needs graphics and device evidence.
- brawler.shared_combat: landscape, 60 FPS goal; capacities {"Bw.Fighter": 64}. Actual game renderer quality hooks and optional CombatVfxPool use bounded presentation budgets. Four tiers preserve authoritative table capacities; input/hit/danger readability needs graphics and device evidence.
- brawler.mobile: landscape, 60 FPS goal; capacities {"Bw.Fighter": 64}. Actual game renderer quality hooks and optional CombatVfxPool use bounded presentation budgets. Four tiers preserve authoritative table capacities; input/hit/danger readability needs graphics and device evidence.
- brawler.weapon_belt: landscape, 60 FPS goal; capacities {"Bw.Fighter": 64}. Actual game renderer quality hooks and optional CombatVfxPool use bounded presentation budgets. Four tiers preserve authoritative table capacities; input/hit/danger readability needs graphics and device evidence.

Unmeasured modes: snake.classic, rpg.classic, survivor.classic, defense.classic, puzzle.classic, sling.classic, brawler.classic, story.classic, survivor.guard, survivor.flying_sword, brawler.belt, survivor.crossed_blades, survivor.mobile, survivor.weapons, brawler.shared_combat, brawler.mobile, brawler.weapon_belt

## Limits
- Not a full game regression summary. Existing NUnit/Unity results remain separately required.
- No Android/iOS touch, IL2CPP/Burst build, fallback, thermal, battery or sustained frame acceptance.
- No physical GPU traffic can be inferred from payload, API upload or allocation counts.
- Goals do not override runtime capacities, GC gates, fixture windows or existing thresholds.
