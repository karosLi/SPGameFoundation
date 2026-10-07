# Stage A: source-accurate compatibility baseline

This closes P0 / Stage A of [the semantic extension plan](SharedFoundationSemanticExtensionPlan.md). It changes documentation and retained diagnostic artifacts only. No runtime, asmdef, existing test assertion, default configuration or old snapshot was changed.

## Deliverables

- [FoundationCompatibilityMatrix](FoundationCompatibilityMatrix.md): 19 actual compositions, covering nine classic games, four mobile variants and six additional opt-ins. It distinguishes real factory/module/system order, capacities, resource/table scope, pooled/handle identity, input/render paths and save/replay support.
- [Machine-readable inventory](validation/FoundationCompatibilityInventory-20261007.json): 154 pinned source hashes and 65 exact existing test methods. These are composition audit labels, not new runtime ModeId/schema identifiers.
- [Cold probe source and reproduction](validation/FoundationCompatibilityProbe-20261007.md): each composition was created/disposed twice in independent .NET processes with identical inventories; retained-source reproduction matched the original output hash. It does not start ticks or claim native leak, GPU or mobile performance proof.
- [Architecture](Architecture.md) and [new-game recipe](NewGameplayIntegrationRecipe.md): actual APIs, ownership, dependencies, identity, queues, configuration and save boundaries replace obsolete proposal-as-implementation descriptions. Useful rendering designs and historical benchmark context remain labeled by scope.

## Validation

The documentation worker built all 76 generated projects and passed 1,050 .NET tests. Its dependency vulnerability-cache NU1900 warnings are recorded; there were no C# build errors. It compiled six independent snippets and executed DriftSmoke 60 ticks, SkillSlots and extension-column composition. Static checks covered 150 relative-link occurrences, 120 distinct targets, 11 real menus, 83 table rows and four contextual API excerpts. [Recorded validation and log hashes](validation/FoundationSemanticDocs-20261007.json).

After integration, the lead reran the checked-in validator without external link-resolution roots, checked every one of the 154 pinned source hashes, and rebuilt/executed the generated examples: zero warnings/errors and the same passing example output. The initial fresh snippet project needed an ordinary restore before building; its final restored build is the result above.

Production source remains the fully verified `e86ee87` / local `62c5b7f` baseline: 1,050 .NET, 1,084 native EditMode and 154 graphics PlayMode passes, with 5/1 documented native skips. The docs-only commit uses explicit `[skip ci]`; it does not pretend to rerun native Unity or physical Android/iOS. [Exact native closure](MobileFoundationFollowupValidation.md).

## Important preserved boundaries

- Classic Snake replay is implemented; complete Session snapshot coverage is incomplete/unverified and no dedicated save/load UI was found. Unmarked mutable resources, including the job-used Signals mailbox, are not all detected by SnapshotGaps.
- Classic Snake's adaptive AI cadence affects simulation and is recorded in replay. This is a named legacy exception, not evidence that all current quality settings are presentation-only.
- Puzzle/Story manual-clock behavior is wired by Bootstrap, not the Mode factory. Shooter pooled rows, resource-owned heroes and synthetic view keys are not registry entities.
- Current raw snapshots have bounded shape/version checks, but lack a universal stable semantic schema/content envelope. Equal-size field/column meaning changes need explicit future compatibility handling.
- Some runtime configs retain shallow references. Five logical layers are not a claim that current asmdefs already enforce every semantic read-only boundary.

The next work is Stage B's red-first composition failure/rollback contract, followed in parallel by Stage C's real Session/View lifecycle tests. New abstractions require demonstrated shared need; existing formats and classic behavior remain the baseline.
