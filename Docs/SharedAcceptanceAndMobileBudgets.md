# Stage G: shared acceptance and bounded mobile-budget reporting

This is the test/tooling slice of [Stage G](SharedFoundationSemanticExtensionPlan.md#p3-阶段-g-公共接入测试和移动预算报告). It adds no runtime monitor, changes no public gameplay enum, and does not modify game production code, FrameGovernor, PerfHud, allocation probes or performance thresholds. **Android and iOS physical-device acceptance remain Pending.** A desktop software proof is not a thermal, battery, touch, GPU or sustained mobile-performance proof.

## Shared contracts, actual consumers and limits

`SPF.Testing/GameplayAcceptance.cs` is a cold adapter and assertion helper, invoked by existing NUnit infrastructure. It uses the actual `SimSession`, `SimWorld`, `DestroyQueue`, registry generations and snapshots. It does not use reflection, per-entity managed adapters or a second engine/test framework. Each check takes a factory and owns fresh sessions, so destructive capacity probes cannot contaminate gameplay evidence. The factory supplies fixed input, state/cancel callbacks, optional save/restore and optional presentation work. A module without save supplies a state reader plus an explicit unsupported-save reason; the replay helper refuses to run for that declaration.

- Shooter: actual `ShooterMode.Create`, seed 123, eight enemies / 32 bullets / eight pickups, waves disabled. This bounded fixture is deliberately **not default-load or dense-load performance evidence**. Existing Shooter gameplay, dense collision, UI and graphics suites remain necessary.
- Platformer: actual default `PlMode.Create`, seed 123, 128 walkers / 64 platforms / 512 coins. Uses its actual start command and latched `InputFrame`; direct input reset matches the input resource contract, not a proof of native touch delivery.
- External Courier: an independent test-only `IGameplayModule` under `Assets/FoundationAcceptance/Tests/EditMode`, with its own keys, eight bounded parcels, delivery rule, reset and snapshot resource. It depends only on foundation assemblies and passes the same applicable fixtures. It is an external rule-integration proof, **not a polished playable demo** and not a new public game mode.

Checks include factory create/start/pause/resume/dispose/repeated disposal, restart clock/request cleanup, full handle-table rejection/counter, deferred and duplicate destruction, stale generation rejection/reuse, level clear, pooled-table full/reject/compaction, equal handles in separate worlds, permuted duplicate queue arrivals, double-session alternating execution/isolation, snapshot restore/replay, cancellation during pause/resume/restart, and quality-isolated checkpoint equality where supported. Deliberately different execution order is exercised using actual sessions/queues; this does not claim arbitrary cross-platform bitwise floating-point determinism.

| Capability | Shooter | Platformer | External Courier |
| --- | --- | --- | --- |
| Lifecycle, deferred structure, Level clear, save/replay, input interruption, double session | Shared fixture | Shared fixture | Shared fixture |
| Handle generation/reuse, duplicate destroy-queue arrivals | Not applicable: all Shooter tables are pooled; stable spawn IDs/compaction/reuse tested instead | Shared fixture | Shared fixture |
| Pooled capacity/compaction | Actual enemy/bullet/pickup tables | Actual coin table | Not applicable: only a handle table |
| Quality invariance | Actual `CombatVfxPool` admission/overflow at levels 0/3; no pixels/GPU | Not applicable to this adapter: `PlRenderer` has no per-mode quality setter | Not applicable: no presentation |
| Multiple views / asset retirement | Not created here; retain SessionHost and existing Shooter graphics tests | Not created here; retain `PlPlayTests.LocomotionTransitionsPauseAndRebind` | Not applicable: no views/assets |
| Native worker scheduling / shuffled Job execution | Existing harness shuffled scheduler plus separate Unity suites; not inferred from this synchronous fixture | Same | Courier is synchronous; does not claim a Job |
| Physical input / mobile suspend / render fallback | Pending physical device; existing native/graphics tests remain separate | Same | No player integration |

The test-only `SPF.Testing` asmdef now references `SPF.Contracts`, `SPF.Runtime.Core`, `SPF.Runtime`. The reference graph remains acyclic: production Runtime/Presentation/Core do not reference Testing. Platformer's existing test asmdef adds Testing. No production asmdef or release build gains a testing dependency.

The nine classic games and four variants retain their existing regression suites, with the six opt-in compositions documented in the [compatibility matrix](FoundationCompatibilityMatrix.md). Shared fixtures for two consumers do not automatically confer fixture coverage on every mode. Classic Snake retains its explicitly documented AI-quality/replay-input exception; it is not relabelled presentation-only.

## Configurable profile goals

[mobile_profiles.json](../Tools/Acceptance/mobile_profiles.json) contains all 19 actual factory inventory labels. These labels are audit labels, not new runtime ModeIds or save schemas. Each profile names the real factory/source, orientation **goal**, safe-area plan, active FPS goal, exact default table capacities and budget axes. The four quality levels reuse FrameGovernor's 1/.9/.8/.7 render-scale goals. Some games have no per-mode decoration-quality hook; their profiles say so. A URP render-scale goal does not mean a URP path or FrameGovernor is active in a capture.

- The 60 FPS goals come from existing bootstrap target frame rates. They are neither observed throughput nor new pass thresholds.
- CPU main/worker goals each provisionally allocate 50% of a 60 FPS frame; GPU provisionally allocates 80%. These are configurable, explicitly unvalidated engineering goals, not historical measurements or modifications of existing gates. These times may overlap and must not be added.
- Managed/Native/GPU memory, API-upload and overdraw goals remain null with reasons until a concrete device, content and counter scope are selected. This is an incomplete device budget, not a zero-byte target or mobile sign-off.
- Android Vulkan/OpenGLES3 and iOS Metal are candidate APIs. Device models and OS versions are unresolved. Selecting a target never turns Pending into Passed.
- Capacity goals are copied from the Stage A inventory and protected by a schema test. Low quality never shrinks authoritative capacity. Existing `VfxBudget.ForQuality` preserves core feedback before decoration; critical hit/danger readability still requires actual graphics/device observation. `ScreenArea` is a coverage estimate, not measured GPU overdraw.
- The comparison reports whether measured factory capacities match the default inventory, so a small fixture cannot impersonate a default/dense workload. No benchmark threshold or prior allocation/physics/action gate is relaxed.

## Measured evidence and counter provenance

`SPF.Testing/AcceptanceEvidence.cs` records a fixed 16-tick warmup followed by 64 timing ticks, then a separate 64-tick synchronous allocation window. A running scenario may have setup ticks before warmup; exact beginning/end-exclusive ticks are recorded. Tables are sampled after each timing tick. Serialization/file IO occurs after all measurement. There is no release-frame formatting or global monitor.

| Field | Actual source/window | What it does not establish |
| --- | --- | --- |
| Fixed-step p50/p95/worst | Raw `Stopwatch` samples around fixed input + `SimSession.Step`; 64 samples; nearest-rank | Rendered frame pacing, worker CPU/GPU time, or sustained phone performance |
| Schedule / SyncWait / TickWall | Existing `PipelineStats`, final EMA alpha .1, history since construction; completed tick count recorded | These are not percentiles, a reset measurement-window mean, or exclusive CPU main/worker attribution |
| Table capacity / sampled peak | Actual `SimTable.Capacity` and maximum Count after each timing tick | Transient sub-tick pool high-water or byte memory size |
| Create failures / destroy overflow | `SimWorld.CreateFailures` and `DestroyQueue.TotalOverflow`, cumulative since factory construction, read after both windows | Every game-specific queue/particle/input refusal or pending uncommitted queue overflow |
| Allocation | Existing `ManagedAllocationProbe`, current creating thread, 64 synchronous fixed-input/Step ticks, retained-array + empty controls before and after | All-thread/native/driver allocation, live managed memory, allocation-frame count |
| GC collections | Independent process-wide generation-0 delta inside allocation window | Number of allocations, pauses, or exclusive attribution to this session |

The .NET probe reports **managed bytes**, the native Unity probe reports **GC.Alloc samples**. Units are never converted or conflated. Before/after retained arrays must meet the original probe minimum (32×1024 bytes or 32 samples), and empty windows must equal zero. Invalid/unavailable/negative counters fail validation; they never produce verified zero-allocation claims. Initialization allocations are outside the steady-state window and are not covered by its result. Existing gameplay allocation gates remain unchanged; these reports collect evidence rather than introduce a looser threshold.

Simulation fixtures create no renderer. Actual backend is `simulation-only/no-renderer`, even in native EditMode. `orientation_intent` names the fixture target; there is no measured viewport/orientation. Worker CPU, GPU time, live memory, rendered allocation frames, uploads, physical GPU traffic, overdraw, thermal and battery remain **Unknown/null** with explicit reasons. Existing available graphics counters belong to separate graphics evidence:

- `GameplayCharacterPresenter.PackedPayloadBytes`: meaningful packed sprite bytes.
- `SpriteBatch.BytesUploaded`: accounted API upload, including DataTexture padding and indirect arguments as appropriate.
- `ParticleRenderer.BytesUploaded`: spawn/socket/batch API accounting; backend source must accompany it.
- `BatCharacterBatch.BytesUploaded` / `PaletteBytesWritten`: API payload and compute writes have different scopes.
- `CombatVfxPool.Stats` and weapon particle diagnostics: admission/drops/expiry/capacity, not global memory or GPU traffic.
- `FrameGovernor.GcCounterValid`, per-frame byte/frame counters and process collections: preserve Valid gating and exact rendered-frame/reset window. These are not the synchronous tick probe.

A graphics/device extension must cite actual counter, validity, backend and window, never fill unknowns with guessed zeros. Neither packed payload nor API-accounted bytes are physical GPU/bus traffic.

## Reproduce the bounded export

Prerequisites: repository's .NET 8 harness dependencies, Python 3, NUnit packages; no new Python packages/frameworks. Use the existing harness dependency acquisition (`Tools/DotnetHarness/fetch-deps.sh`) if needed. Keep SDK/cache directories writable according to the environment. Do not change device or runner settings.

```bash
# Full ordinary regression first; do not replace it with the focused export.
Tools/DotnetHarness/run.sh
python3 -m unittest discover -s Tools/Acceptance -p 'test_*.py' -v
# Commit executable/config inputs first, then choose a new/empty output directory.
Tools/Acceptance/run-report.sh /tmp/spf-stage-g-evidence
```

The script refuses changed/untracked Assets/Tools inputs, rebuilds the ordinary asmdef-based harness, runs all three new NUnit adapter classes, retains TRX/logs/raw probes, validates profiles and emits `report.json` plus `report.md`. It verifies unchanged Assets/Tools and the pinned, clean ignored Unity.Mathematics source (including unexpected ignored C# files) both before and after execution, retains dependency source hashes and `dotnet --info`, records exact commit/tree supplied to each probe, and refuses stale/mixed/duplicate evidence identities. The raw capture environment variables are `SPF_ACCEPTANCE_OUT`, `SPF_ACCEPTANCE_COMMIT`, `SPF_ACCEPTANCE_TREE`. To use a native Unity test executor, supply those to the test process using existing authorized tooling; do not infer a Unity run from an API compile.

To re-render already captured evidence:

```bash
python3 Tools/Acceptance/mobile_acceptance_report.py \
  --profiles Tools/Acceptance/mobile_profiles.json \
  --evidence /tmp/spf-stage-g-evidence/raw \
  --toolchain /tmp/spf-stage-g-evidence/toolchain.json \
  --output /tmp/spf-stage-g-evidence/report
```

The strict version-1 schema is implemented and negative-tested by the standard-library validator. It rejects unknown/missing fields, duplicate JSON keys, nonfinite/negative/bool numeric counters, calibration failures, false backend claims, mismatched timing windows, invalid capacities, mixed commit/trees and unknown modes. Bounds: 2 MiB/input, 256 probes, exactly 16 warmup/64 timing/64 allocation ticks in schema v1, 128 tables/probe. Input hashes are retained. Synthetic Python unit-test data is labelled synthetic and is not retained as real measurements.

## Completion gates

A software checkpoint needs exact code/tree, full harness, real Unity API and netstandard2.1 compatibility compile, unchanged cold-19 composition, independent review, and separately run affected native EditMode/graphics suites. API compile alone proves no native execution. A historical green result is not a new checkpoint.

A physical acceptance run must preselect device model/OS/API, exact binary/build settings (including IL2CPP/Burst), resolution/orientation/safe area, workload/seed/capacities, warmup and sustained window, intended quality and fallback; report actual backend/valid counters and p50/p95/worst rendered frame times, touch/interruption/restart, memory, upload, thermal and battery. The short fixed-tick desktop report deliberately cannot issue Passed for Android or iOS. Missing device access leaves those gates Pending.

Exact local verification and integration status are recorded separately in [Stage G validation](validation/SharedAcceptanceStageG-20261007.md).
