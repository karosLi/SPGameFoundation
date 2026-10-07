# Collision broad-phase comparison

## Decision and scope

Keep the production `SpatialGrid.Query` traversal and its strict circle-overlap policy. Its ordinary cell visitation order and entry-overlap predicate are unchanged; query bounds now match the builder at reciprocal-rounded upper edges. The bounded quadtree is a **test-only comparator**, not a new default or a global collision-backend abstraction. `QueryPruned` is an explicit experimental alternative; no gameplay caller enables it. Initial managed measurements reject enabling that pruning globally. Native measurements must be collected on the exact integrated revision before making a native-performance claim.

Confirmed correctness/accounting fixes are independent of that decision:

- Coarse large-radius cells can extend beyond the fine window when dimensions are not divisible by the coarse scale. Build acceptance now uses the same half-open world window as `GridReader.Covers`, for both layers.
- Accepted upper-edge centers that reciprocal multiplication rounds to an out-of-range cell are kept in the last cell. The query lower bound applies the same correction only while inside the canonical window, so wholly outside rectangles remain outside. The large-layer scatter also follows the counting pass classification.
- Requested staging entries above capacity are included in `DroppedLastBuild`, together with out-of-window entries. `EntryCount` comes from the actual built layer totals. Negative requested counts are treated as empty. No arrays resize, visitor ordering changes, or snapshot fields are added.

The grid also rejects non-finite/non-positive cell sizes and negative capacities before allocation. Packed `GridEntry` Owner/Data remain 16-bit and its layout remains 16 bytes.

## Existing consumers, not a blanket migration

| Consumer | Existing path | Relevant regression |
| --- | --- | --- |
| Snake | Incremental four-entry block grids for body/items, moving fine window, coarse head grid | `SnakeWorldTests`, `SnakePerformanceTests`, gameplay/snapshot tests |
| RPG | Rebuilt actor grid plus tile collision | `RpgCombatTests`, `RpgGameTests`, `RpgSnapshotTests` |
| Survivor | Rebuilt enemy grid, separation capped at eight overlaps | `SvGameTests`, `SvMobileSkillTests` |
| Guard / flying swords / weapons | Same grid, inclusive shapes, relative-motion sweeps and stable hit history | `SvGuardTests`, `SvFlyingSwordTests`, `SvWeaponTests` |
| Mobile shooter | Swept proxy circles, relative-motion narrow phase, stable first-hit ties | `ShooterTests`, `ShooterCollisionBenchmark` |
| Landscape belt | Two grid builds/tick, ground/height filtering, ordered separation | `BwBeltScrollerTests`, `BwBeltWorkloadTests`, `BwWeaponTests` |
| Classic Brawler | Bounded pair loops and bone probes | `BwTests`, shared-combat tests |
| Sling | Persistent sort-and-sweep rigid-body broad phase | `PhysicsTests`, `SlTests` |
| Platformer | Swept tile AABB motor and one-way platforms | `PlatformerTests`, `PlTests` |
| Puzzle / Story | No moving-object broad-phase workload | Existing complete game and save/UI regressions |

A deterministic tree order is not the same as the grid's order. Survivor's first eight overlap choices and RPG/belt floating-point accumulation can change when traversal changes. Equal hit sets alone therefore do not authorize a gameplay migration. Existing snapshots and classic narrow-phase policies remain separate compatibility requirements.

## Reproducible comparison

`SpatialBroadphaseComparisonTests.PairedBuildAndQueryReport` compares four backends:

1. Existing single-layer grid.
2. Opt-in conservative cell pruning, preserving accepted entries and their order.
3. Existing large-radius two-layer grid (threshold .75, coarse scale 4).
4. `BoundedQuadtreeReference`, fixed native arrays, bucket size 16, maximum depth 12.

The tree uses stable center partitioning, contiguous leaf ranges, subtree maximum radii and threaded stackless traversal. Every accepted entry is stored once. A node/depth/precision limit leaves an oversized leaf queryable; saturation never silently drops entries. Its node budget is reported and included in memory accounting. Build includes the entry copy and stable-partition scratch work. This is a rebuilt bucket quadtree, not a loose incremental tree.

Each case uses identical immutable entries, queries and previous/current target positions, seed **721**, six warmups and 24 samples. Backend order rotates each sample. Each backend uses a single sequential `IJob` for the query batch; this controls execution topology but does not measure the production Shooter parallel-for scaling. The full total includes build scheduling, dependent query scheduling and final completion; separately measured build/query each include their own scheduling/completion, so their sum need not equal the pipelined total. Input generation and initial staging writes are excluded equally. Each rebuild resets its count/state; there is no backend-specific evolving-world advantage.

| Distribution | Entries / queries | Construction |
| --- | --- | --- |
| Uniform | 1024/4096 and 4096/4096 | 64×64, radius .3; cell 2 |
| Clustered | 1024/4096 | 90% in four 4×4 clusters, 10% uniform |
| Sparse huge map | 320/1280 | 7500×7500, production-like head cell 125; radius-150/300 queries |
| Mixed sizes | 1024/4096 | Most radius .3, some .75, 1% radius 4 |
| Dense worst case | 1024/128 | Coincident or millimeter-spaced centers; true overlap cannot be pruned |
| High speed | 1024/4096 | 10/25-unit target movement per 60 Hz tick, opposing projectile motion, stationary and axis/diagonal sweeps |
| Belt spread/clustered | 32/128, 64/256, 128/512 | Existing workload's spread and tight-cluster formulas within 20×8 |

A separate sensitivity fixture reports bucket sizes 8/16/32 × depth limits 8/12/16 on all six generic distributions, with 512 entries/512 queries, seed 1234, four warmups and 16 rotated samples. These smaller-case results expose tuning effects; do not compare their absolute times to the primary larger matrix or retroactively pick a winner from it.

Half the queries are near entries and half sample the whole domain. These are distribution/proxy-query microbenchmarks, not measurements of the complete real gameplay loops. In particular, the sparse case compares a production-like **coarse** grid, not an artificially enormous fine grid. Incremental Snake maintenance, actual guard/sword cadence and whole-game decisions still require their existing workloads.

### Measurement outputs

`Artifacts/perf-spatial-unity-native-editmode-<distribution>-<entries>.txt`, or `/tmp/spf-artifacts/perf-spatial-dotnet-stubs-...` in the harness, reports:

- Uninstrumented total/build/query p50, p95 and worst time, in milliseconds.
- A separate counted pass: cells/nodes visited/pruned, **raw entries examined**, visitor calls, exact narrow-phase hits.
- Native element-payload bytes, including staging and tree scratch, excluding allocator headers and shared fixture arrays.
- Tree node/depth/saturation diagnostics and rejected entries.
- Actual query execution Burst sentinel, and a tree-build sentinel. Grid construction remains the existing Burst-annotated job and is labeled not independently instrumented.
- Separate warmed allocation windows using the calibrated `ManagedAllocationProbe` and independent process Gen0 counts.

A visitor count is not the number of entries the broad phase inspected: `GridReader.Query` filters circles before calling the visitor. The old Shooter `CandidateTests` reports the latter. Do not compare those metrics as though they measure the same work.

No wall-clock thresholds gate correctness. On native Unity with Burst enabled, execution sentinels must report 1; a zero means managed execution and must not be labeled Burst performance. The .NET harness always reports 0 and establishes logic/allocation accounting only. Neither desktop runtime establishes Android/iOS frame budget, thermal behavior or battery use.

## Correctness gates

- Exact per-target brute-force oracles for all distributions and seeds 721/1234/9173; no duplicate targets.
- Earliest relative-motion contact fraction and stable identity tie-break across backends, including reversed staging of equal-time hits.
- Baseline/pruned visit-order identity, first-eight early termination, measured/unmeasured parity, strict tangency and inclusive cell traversal.
- Negative/offset/scaled coordinates, cell and split boundaries, adjacent representable float values, mixed radii, finite coarse coverage, reciprocal rounding, large-origin cancellation, and capacity accounting.
- Bounded tree node/depth saturation, coincident entries, empty rebuilds and zero capacity; old readers reference the new completed build.
- Native strict/fast Burst probes with matching-mode brute-force oracles, native per-target swept-contact/earliest-hit parity, and warmed zero-allocation windows.

Grid circle queries remain strict (`<`). Cell queries remain broad conservative cell traversal. Inclusive rings/beams/sword contacts retain their explicit padding and narrow-phase rules. This change does not convert legacy projectile policies or introduce a generic epsilon into narrow phases.

## Managed evidence on the tested patch

The final harness run found a useful negative result: pruning reduced examined entries for uniform 1024-target data from **121,448 to 104,166**, but total p50 increased from **14.1433 ms to 71.8446 ms**. The added per-cell calculations cost more than the eliminated entry tests in that runtime, so the candidate remains opt-in.

Mixed sizes demonstrated the value of testing the already-existing second layer: examined entries fell from **285,261 to 140,977**, with unchanged **75,371** visitor/hit results. Total p50 was **27.3553 ms** single-layer versus **17.5129 ms** two-layer. The bounded tree examined **203,221** entries and took **30.1051 ms**. These are **unoptimized managed-stub measurements**, not native recommendations. Individual reports retain p95/worst, memory and counters; reruns can differ.

The dense case examined the same 67,584 real overlaps in every backend. Its bounded tree reached depth 12 and kept four oversized leaves without lost entries. Changing the spatial index does not eliminate the cost of real pairwise overlaps.

Do not enable the second layer everywhere based on that one fixture: it changes traversal across layers and has extra storage/build cost. Collect native evidence, check target consumers' order/history contracts, and adopt a workload-specific choice only when full-tick measurements improve.

## Validation and commands

The final local .NET aggregate build compiled 76 generated projects with zero warnings/errors. The full harness executed **908 tests, all passed**; two existing explicit on-demand cases (`SearchShots` tuning and `ExportFrames` preview export) remain unexecuted. Foundation tests contributed 613 passing cases. PlayMode coroutine/graphics tests are not executed by the .NET stubs and still require native Unity CI on the integrated commit. All 13 primary reports have zero warmed current-thread managed bytes and correctly report Burst sentinels as 0 in stubs. Six sensitivity reports preserve all nine configuration rows, including slower choices.

Normal repository entry point:

```sh
Tools/DotnetHarness/run.sh
```

Targeted rerun after harness generation/build:

```sh
dotnet test Tools/DotnetHarness/.gen/SPF.Tests.EditMode/SPF.Tests.EditMode.csproj --no-build --filter "FullyQualifiedName~L1GridTests|FullyQualifiedName~BoundedQuadtreeTests|FullyQualifiedName~SpatialBroadphaseComparisonTests|FullyQualifiedName~SpatialSweepParityTests"
```

Run the same fixtures in native Unity EditMode with Burst enabled, inspect the actual sentinels and exact-contact masks, and retain complete old-game EditMode/graphics PlayMode regressions. No existing budget or measurement window was relaxed.

## Source guidance

- [NVIDIA: Thinking Parallel, Part I](https://developer.nvidia.com/blog/thinking-parallel-part-i-collision-detection-gpu/) explains uniform-grid assumptions, mixed-size/sparse-world tradeoffs and divergence. It is algorithm guidance, not a benchmark of this CPU/Burst implementation.
- [Samet, Sankaranarayanan and Auerbach, SIGMOD 2013](https://dl.acm.org/doi/10.1145/2463676.2465332) studies loose-quadtree motion updates. This test-only rebuilt bucket tree does not claim those incremental-update benefits.
- [Box2D dynamic-tree documentation](https://box2d.org/documentation/group__tree.html) distinguishes node and leaf visits; its dynamic AABB tree is not a quadtree.
- [Unity Burst type support](https://docs.unity3d.com/Packages/com.unity.burst@1.8/manual/csharp-type-support.html) motivates native flat arrays and value-type visitors. The repository pins Burst 1.8.27; docs for the 1.8 line can track newer patch releases.
