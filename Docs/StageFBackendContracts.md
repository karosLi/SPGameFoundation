# Stage F: bounded backend conformance contracts

2026-10-07. Source base: `40a4323`. This checkpoint implements the bounded contract/test portion of [Stage F](SharedFoundationSemanticExtensionPlan.md#p2-阶段-f-查询和表现后端的可替换契约). It does not introduce a production backend, switch defaults, migrate snapshots, or change the simulation/composition loop.

## Audit and retained contracts

The audit preceded edits and compared the actual spatial, AI and presentation implementations with their existing tests and reports. These contracts were already adequate and are **retained**, rather than replaced by a universal backend interface:

| Boundary | Existing contract retained | Actual implementations/consumers |
|---|---|---|
| Spatial circle broad phase | `GridEntry`, generic struct `IGridVisitor`, `GridReader`, `GridQueryStats` | Production single/two-layer grid; opt-in `QueryPruned`; test-only `BoundedQuadtreeReference` |
| Reactive AI selection | `DecisionNode`, `DecisionResult`, validated bounded `DecisionTree` | RPG tactical selection and Belt intent selection; direct branches remain default |
| Presentation selection | `RenderCapabilities`/`RenderTier`, backend-specific capability structs and constructors | Sprite GPU/data-texture drawing, weighted BAT vertex/compute/CPU, particles compute/CPU with independently selected sprite tier |
| Presentation input/lifetime | Existing character streams, `WeaponViewState`, authoritative projectile/socket data, binding revisions | Actual Brawler and Survivor character/weapon renderers |

There is no new production POD/interface, per-entity virtual call, managed delegate, reflection, third renderer, or generic registry. The new dispatcher and managed delegates live exclusively in testing. `SPF.Testing` adds a dependency on the existing `SPF.L2Gameplay` contract; RPG's test assembly adds its missing `SPF.Testing` reference. Production asmdefs and production C# remain unchanged.

## Implemented: reusable spatial conformance

[SpatialBackendFixture](../Assets/SinglePlayerFoundation/Tests/EditMode/SpatialBackendFixture.cs) owns and invokes the real grid, pruned grid, layered grid and test-only tree. It is reused by:

- Existing `SpatialBroadphaseComparisonTests.AllBackendsMatchEveryBruteForceHitWithoutDuplicates`: same eight distributions, three seeds, 192 entries, 64 queries, exact per-target membership and duplicate checks. Workload/timing code is unchanged.
- New [SpatialBackendContractTests](../Assets/SinglePlayerFoundation/Tests/EditMode/SpatialBackendContractTests.cs): shared boundary/capacity tests, caller filtering and bounded outputs, explicit order/reduction differences, and order-independent nearest selection with a stable tie.

The precise contract is:

1. Coordinates are a two-dimensional world/ground plane. Circles use strict overlap (`distance² < (query radius + entry radius)²`). Center acceptance is a half-open window: minimum included, maximum excluded, even when an outside circle overlaps the window. Exact tangent and immediately inward representable points are tested.
2. Source/staging and query results are independently bounded. A build drops requested slots beyond source capacity and centers outside the window; negative counts rebuild empty. Zero-capacity behavior is explicit. A saturated tree leaf retains entries; its existing node/depth/precision saturation tests remain intact.
3. Owner/Data are opaque packed 16-bit fields. Self/team/data filters belong to the visitor. They are not entity-generation validation or built-in height filters. Narrow-phase sweeps, ground height, stale handles, deduplication and contact priority remain the consuming rule's responsibility. Non-finite/negative circles, arbitrary 3D/rays, and a shared sweep API are not added or promised.
4. The common fixture's **test visitor** writes the first N eligible entries. With full counting it counts every omitted eligible result. With first-overflow early-out it stops at the first extra eligible result and reports one observed overflow, a lower bound on hidden results. A full output alone does not mean overflow. Empty output, exact fit, excess capacity and filtered entries before/after saturation are tested. This visitor does not rewrite the distinct existing consumer policies (for example Survivor's stop-at-eight overlap rule).
5. Early-out forbids subsequent visitor calls. `QueryMeasured` must preserve the same ordered prefix, filtered overflow and stop behavior; its `VisitorCalls` includes callbacks subsequently rejected by caller filtering. `EntriesExamined` is distinct from accepted hits. Grid's public void query and tree's boolean return stay unchanged; the fixture makes no new common completion-return promise.
6. Grid order is row-major cells with stable source order inside cells, small layer before large. Pruning preserves this exact order. Tree order is stable partitioning with depth-first quadrant traversal. A fixed source explicitly produces `0,1,2,3` for grid/pruned, `0,2,3,1` for layered, and `0,2,1,3` for tree. All four return the same set. The first-two reduction using unchanged `GroundCombatQueries.Separation` differs for tree/layered; pruned and baseline remain raw-bit identical. This is a deliberately bounded reduction witness, not a benchmark or evidence that arbitrary unlimited sums are equal.
7. Full filtered nearest selection compares squared distance then the caller-provided stable owner key. Reversing staging preserves the selected equal-distance target. Existing moving-target earliest-fraction/reversed-tie tests and per-target swept parity remain in place. A packed row owner is not automatically a persistent stable ID; the caller must choose and resolve its key correctly.

Existing adversarial boundary, strict/fast native jobs, high-speed sweep, tree saturation, full distribution benchmark and ordered-separation arithmetic fixtures remain unchanged. Equality is scoped to the same runtime/float mode. The managed Float32/ExtendedScalar policy and production ordered separation are not modified; see [the retained precision evidence](SeparationArithmeticParity.md).

## Implemented: reusable reactive-selection conformance

[ReactiveDecisionConformance](../Assets/SinglePlayerFoundation/Testing/ReactiveDecisionConformance.cs) is a cold-path fixture, used by the actual [RPG](../Assets/RpgFoundation/Tests/EditMode/RpgAiDecisionTests.cs) and [Belt](../Assets/BrawlerFoundation/Tests/EditMode/BwBeltAiTests.cs) policy tests. The existing independent frozen direct-policy oracles remain the expected actions. The helper does not generate expected actions by running the candidate tree.

- All 32 RPG / 8 Belt fact combinations run ascending, descending and ascending again.
- Selected action, successful status, real leaf identity and bounded visits are checked; every leaf must be exercised.
- An exact visit budget reproduces the result; one fewer visit must return `BudgetExceeded`, no action and no leaf. Unknown input fact bits must not change either policy's result/trace.
- Repeated inputs must have the same trace and shared program instructions must remain unchanged.
- A fixture self-test rejects a wrong direct oracle and fabricated visit counts.

The existing perception thresholds, invalid-program safe Hold, Hit/KO/Attack commitment locks, scheduled native Burst witnesses, allocation calibration and actual full-Tick frozen-reference/snapshot tests are retained. This does not turn the reactive selector into a behavior tree, resumable task, perception scheduler, movement executor, RNG owner or persistent action state machine. No AI enum or production selection logic changes.

## Presentation contracts retained; lifecycle evidence added

Capability, preference and quality remain distinct. `RenderCapabilities.Override` is a requested tier and can deliberately bypass coarse device detection for tests. It is not evidence of shader execution. `SpriteBatch` expects a viable selected shader and does not itself implement a universal fallback factory. Particle capability selection can choose CPU simulation while keeping healthy GPU sprite drawing, or choose DataTexture if GPU drawing is unavailable. Weighted BAT separately selects vertex/compute/CPU and accepted half/float format. No new generalized fallback claim is made.

The existing real fixtures remain the proof boundary:

| Existing fixture | What it actually checks |
|---|---|
| `WeaponParticleTests.EveryMissingCapabilitySelectsHonestCpuFallback` / `UnusableGpuDrawSelectsDataTextureWhileComputeFailurePreservesHealthyDraw` | Pure capability-selection gates; not GPU execution |
| `WeaponParticleGraphicsTests.MissingComputeAndWrongKernelsFallbackAndSeparateBatchesKeepTheirResources` / `MissingGpuShaderStillDrawsThroughDataTextureFallback` | Real assets, unavailable/wrong compute kernels, ownership and real fallback draw |
| `WeaponParticleGraphicsTests.ActualComputeStateMatchesCpuForSpawnMotionDragGravityLifetimeAndSockets` / `ProductionDrawMatchesCpuPackedSpritesAndDataTextureFallback` | Actual compute readback and production GPU/CPU pixels; original tolerances unchanged |
| `BatCharacterTests.HalfPrecisionAcceptanceIsDecidedByTheMeasuredPixelBudget` / `BackendSelectionRequiresTheExactAcceptedSampleFormat`, `BatComputeTests` | Asset precision budget, format/capability/resource gates; not proof of device execution |
| `BatGraphicsTests.VertexStageReadback_MatchesWeightedCpuAndBoundedIk` / `ComputeBufferReadback_ThreeMatricesMatchCpuReferenceAcrossAllCases` | Actual vertex/compute probes with accepted format, including the original compute `<1e-4` coefficient tolerance; missing capability explicitly leaves that path unverified |
| `BatGraphicsTests.ProductionDraw_ComputePaletteMatchesCpuWithClipBlendMirrorScaleAndBothIkBends` | Real compute/CPU draw; existing pixel/edge budgets retained |
| Existing `BwViewLifecycleTests` / `SvViewLifecycleTests`, actual weapon gameplay tests | Rebuild, same-tick restore, session/owner identity, shared/private ownership, quality and authoritative snapshot isolation |

New images are acquired **inside** `BwWeaponGameplayTests.FourWeaponsUseActualBeltLoopAndSockets` and `SvWeaponGameplayTests.FourWeaponsDrivePortraitHordeAndKeepSkillHud`, for both requested tiers. Each captures before disable, after reenable and after same-tick restore, before the later live-motion scenarios reset their worlds. These are three point images per game/tier, not a continuous transition video.

- Uses the existing `CanvasCapture` real camera/all-canvases GPU readback. The expected 12 PNGs and their `.txt` sidecars are under `Artifacts/Screenshots/MobileHud`, named `weapon-{belt|horde}-lifecycle-point-{before-disable|after-reenable|after-same-tick-restore}-{gpu|fallback}`.
- Each sidecar records requested tier, renderer-reported bound world tier, active particle draw tier and simulation backend, graphics API, next simulation tick, actual readback acquisition time and reserved particle count. The GPU filename suffix is a requested-world-tier label, not a compute-execution witness.
- DataTexture requires DataTexture particle drawing and CpuBurst selection. GPU world-tier requests still permit an honestly reported particle draw/simulation fallback; the world renderer itself must report the requested tier. Existing dedicated compute probes remain necessary for proof of compute execution.
- Manual clock, tick and full snapshot invariance surround each yielded capture. Hidden authoritative Staff releases are real and must remain suppressed across captured reenable/restore frames. Existing original motion/allocation/assertion gates are preserved.
- These images will provide inspectable visual states only after a native graphics run and artifact retrieval. Compilation and test stubs cannot establish that the images exist or look correct.

## Existing performance decision and evidence provenance

[CollisionBroadphaseBenchmarks](CollisionBroadphaseBenchmarks.md), [AiDecisionValidation](AiDecisionValidation.md), [SeparationArithmeticParity](SeparationArithmeticParity.md) and [MobileFoundationFollowupValidation](MobileFoundationFollowupValidation.md) retain the primary paired build/query/complete, ABBA full-Tick, exact arithmetic, allocation and graphics reports. Their `4af9b87` / `e86ee87` native evidence remains attributed to those exact sources. It is not relabeled as Stage F evidence. Current P1 native runs likewise do not execute these later edits.

Grid and direct AI remain defaults. The tree is a test comparator, pruning an opt-in candidate, and parallel separation remains unadopted. No new performance workload or claimed speedup is necessary for this test-only checkpoint. Device budgets, samples, GC counters and all existing numeric/physics thresholds remain unchanged. Physical Android/iOS validation is still external.

## This checkpoint's verification

Final-source checks and exact source hashes are recorded in [the verification record](validation/StageFBackendContractsValidation.json). The following remain separate kinds of evidence:

- Final focused .NET conformance: **42 passed / 0 failed** (SPF 30, RPG 5, Belt 7). Full asmdef-separated .NET harness: **1,210 passed / 0 failed**, 76 projects, **0 build warnings/errors**. The existing explicit `SearchShots` and `ExportFrames` tools remain unrun; native PlayMode is excluded by the harness. These results establish test logic and managed-stub behavior.
- All **513 Assets C# files** compiled against installed real Unity 2022.3.62f2, package and test-runner DLLs: **0 errors / 7 existing serialized/unused-field warnings**. This establishes API compatibility only.
- Two independent cold-19 composition exports match each other and the committed compatibility baseline exactly: SHA-256 `a42c9f27d2bb8d277303eef24e5b4cdbd58f0035d8bca89c7e08089348dd5f30`. No runtime/module/save layout changed.
- Independent source/doc review found no source defects; two documentation wording nits were corrected. Local links and `git diff --check` pass.
- Native EditMode/Burst, real graphics execution, capture retrieval/pixel review and unchanged original allocation windows for **this exact new source** remain pending. Prior green native runs do not discharge them.

The worktree's first build omitted its untracked Mathematics checkout and failed with missing-type errors. It was repaired by copying the already-cached exact pinned `f110c8c230d253654afed153569030a587cc7557` source and regenerating projects; no dependency version changed.

## Pending and deliberately not added

- Execute/review the new native lifecycle stills and retained native gates on the integrated exact commit. Point images cannot prove continuity between transitions; original live-motion clips cover their own later reset scenarios only.
- No third backend/factory extraction, generic scheduler, new spatial-height dimension, automatic performance-based backend switching or full behavior tree is warranted by these two-consumer tests.
- No runtime device-health dispatch/readback probe is added. Existing capability/asset/precision gates and native validation probes remain as implemented; a device claiming support can still require platform-specific investigation.
- Save envelopes/content compilation (Stage D), generalized gameplay-rule decomposition (Stage E), mobile hardware acceptance, and broader Stage G integration are outside this bounded Stage F checkpoint.
