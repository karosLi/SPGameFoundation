# Shared combat stage 2: bounded, opt-in primitives

## Scope and compatibility

This stage adds three small reusable L2 combat helpers and two simulation consumers. It does not add a skill inheritance tree, a damage framework, another simulation loop, or presentation-driven rules.

- `HitHistory`: bounded, allocation-free full `EntityHandle` deduplication over caller-owned `NativeArray` slices.
- `ActionTimeline`, `ActionWindow`, `TickInputBuffer`: integer fixed-tick active windows, explicit interruption/cancel rules, action/pulse IDs, and a one-command input buffer.
- `CombatSweep`: shared closed-circle time of impact, including relative target motion. `ShooterMath.Sweep` forwards to the same arithmetic, and Survivor guard contact uses the shared helper.

Classic Survivor's `BulletInfo`, classic Brawler's `FighterInfo`, and their existing table/resource layouts are unchanged. The existing classic 97a2b34 snapshot fixture remains the byte-for-byte compatibility gate. The annular skill still uses its single-query OR predicate; it already deduplicates overlapping rings and does not need a history.

**Classic Survivor/RPG persistent row-based projectile `LastHit` and classic Brawler row-mask identity bugs remain.** Only the new shared Brawler path fixes the reproduced attack-lifetime swap-back defect. This is an explicit opt-in boundary, not a silent save migration. No claim is made that an already-corrupted V1 row can be mapped back to its original entity.

## Hit-history contract

The caller owns storage, offset, capacity, and `HitHistoryState`. The state contains the full source owner, pulse ID, and active count. A null owner is allowed for a level-owned singleton ability, but null/invalid target handles are rejected. Handles are world-local and include both index and generation; they are not globally unique IDs. History equality does not check aliveness: the caller obtains candidates from the current table or resolves their handles.

- `Begin(owner, pulse)` is idempotent for the same owner and nonzero pulse, so multiple shape queries share their accepted targets.
- A changed owner or pulse clears the old active prefix. Source-generation reuse cannot inherit previous hits.
- `Release` clears the prefix and invalidates the scope. The owning resource must also reset on level/session reset.
- `Check` returns `Added` for an eligible candidate without mutation. `TryRecord` commits it.
- `Full` rejects new damage; it never evicts an existing target or silently grows. A duplicate still returns `Duplicate` when full.
- Zero capacity is a valid scope that always rejects new targets. Invalid offsets/counts/slices are explicit `InvalidScope` results.
- Exactly one writer owns a scope. Parallel independent scopes may use disjoint slices; sharing a scope across concurrent jobs is unsupported.
- Queued damage uses **check → successful enqueue → commit**, all under that writer. Queue failure must not consume a history slot or pierce budget. Immediate Brawler damage records successfully before mutating HP.

The primitive has no resource pool or per-projectile heap object. Do not allocate a multi-target history for every horde bullet. A legacy-compatible consecutive-target projectile would need just one full last-target handle in a separately versioned layout; that integration is deferred.

## Timeline contract

`Begin()` starts at tick 0 and advances the nonzero pulse ID (wraps from `uint.MaxValue` to 1). `Advance(n)` visits `(previous, current]`. `Crossed([from, until))` detects an active window even if a caller advances over the whole window. Repeated shape queries may inspect the same span, but a zero step does not visit a new tick. Consumers are responsible for sampling geometry at the appropriate pose for their action; this helper does not reconstruct animation during skipped ticks.

Cancellation is allowed only when the **current** tick lies inside the cancel window; it is not retroactively allowed by crossing an earlier window. `Stop()` represents an unconditional external interruption. A new action requires `Begin()`. Negative steps and integer overflow fail without mutation.

The input buffer is latest-input-wins, one command, on absolute monotonic simulation ticks. A command is valid in `[pressedAt, pressedAt + lifetime)`, remains queued when consumption is temporarily ineligible, expires at the boundary, and is consumed once. Reset it on owner reuse or interruption when the game's rules require that. It is a reusable primitive tested independently; the current Brawler's existing unlimited queued-punch policy is deliberately unchanged in this stage.

## Survivor integration

`SvConfig.CreateCrossedBladeExample()` enables two periodic crossed flying-blade paths (horizontal and vertical capsules) using the existing enemy grid and hit queue/resolver. It is a **simulation/configuration example**, with no new visuals or audio supplied here.

Defaults: one pulse every 12 playing simulation ticks, reach 5, half-width 0.35, damage 16 per pulse, history capacity 256. The origin is the current hero position. At world creation, history capacity clamps to 1 through the authored enemy-table capacity; intervals clamp to 1 through `int.MaxValue - 1` ticks. `SvSettings.CrossedBlades` exposes these values. Rendering quality has no effect on collision, cadence, or damage.

Each pulse runs two capsule queries, sharing one history. A target intersecting both takes one accepted hit; the next pulse may hit again. Cells are expanded by the grid's largest enemy radius, followed by inclusive capsule narrow phase, so exact tangency is retained. Rows stay transient in the queued `SvHit`; only history stores full handles.

The singleton `SvCrossedBladeState` is registered only when enabled. It owns the bounded target storage, pulse history, timeline, and saturating history/queue rejection counters. The simulation pauses its timeline while not Playing. Level reset clears the whole state. The scope's owner is null because the hero is not an entity-table row; resource lifetime supplies its ownership boundary.

## Brawler integration

Use `BwMode.CreateSharedCombat(BwSharedCombatConfig.Default, out module)` for the new path. The classic `BwMode.Create(out module)` remains unchanged. The existing bootstrap still selects classic, and the existing renderer still displays at most 64 fighters. Selecting the new factory is a simulation opt-in; more than 64 visible fighters needs a separate presentation change.

The shared path reuses the existing fighter update, skeleton probe, combat loop, damage, knockback, and flow. One bounded scope belongs to each active source handle; source lookup never assumes that registry indices equal rows or fit the fighter capacity. Expired/interrupted/deleted sources are pruned using the entity registry before scope acquisition. Same-swing history survives target/source swap-back, sorting, and recycled target generations. New swings/combos reset history through their timeline pulse IDs.

Active seconds from the authored attacks are converted into closed fixed-tick ranges: `[ceil(from / dt), floor(to / dt)]`. This avoids accumulated float drift on the opt-in path. Animation/movement and the classic path still follow the original rules.

Authored capacities are explicitly bounded to **1–128 fighters and 1–128 targets per attack**. The new path can hit row 65 and does not perform bit shifts for hit identity. Brawler still uses pairwise body separation and bounded linear combat/scope lookup. This is **not a scalable dense-combat implementation**; 65+ support is a correctness capability, not a mobile performance claim. Further scaling requires a measured candidate-query design. Other modules extending the table beyond the authored source-scope capacity can encounter `RejectedScopes`, which rejects attacks safely instead of allocating.

At default 64×64, target payload alone is 32 KiB (8 bytes per handle). This arithmetic excludes source state, native-container overhead, and other gameplay data; it is not a measured memory footprint.

## Snapshot contract

New opt-in resources have named schema keys and explicit magic/version/capacity checks:

- `Sv.CrossedBlades.V1`
- `Bw.SharedCombat.V1`

They persist active owners/targets/counts, timelines/pulse IDs, scope ownership, and deterministic rejection counters. Inactive prefixes are cleared when released; construction/reset clears all storage. Restore validates capacities, bounds, identities, timeline state, and version. Snapshot sessions require the same configuration as capture, following the foundation's existing immutable-configuration contract. A restore failure leaves the world partially restored, following `SimWorld`'s existing reset-before-use rule.

There is no V1 import converter. A classic snapshot is rejected by an opt-in resource layout, and an opt-in snapshot is rejected by classic resource ordering. No non-snapshot sidecar holds simulation-relevant hit memory.

## Verification

New focused suites:

- `SharedCombatTests`: actual world swap-back/sort/recycle, distinct generations, exact/full/zero capacity, disjoint scopes, source reuse, pulse reset, saved history/timeline, skipped windows, cancel and input expiry, relative sweep tangency/endpoints/moving targets, Burst-job entry point, warmed allocation-free primitive calls.
- `BwSharedCombatTests`: normal old-corpse expiry during a jab, target and source row movement, sort, replaced target generations, full-history fail-closed damage, row 65, mid-hit snapshot continuation, repeated swings, level reset, and invalid snapshot bounds/capacity.
- `SvCrossedBladeTests`: overlapping and independent paths, end tangency, periodic pause/resume, full-history damage policy, full-queue retry without consumed slots, sort/destroy/recycle snapshot continuation, opt-in boundary, reset, and invalid snapshot capacity/bounds.

The .NET harness checks source compilation, logic, exact classic fixtures, deterministic continuation, and measured managed allocations in the primitive calls. Its job API is a stub: it does **not** prove actual Burst compilation, real Unity job safety, rendering, or Android/iOS performance. Real Unity EditMode/PlayMode and target-device profiling are separate gates.

### Local validation result (2026-10-06)

- All 69 generated harness assemblies compiled with zero warnings/errors (serial aggregate build).
- Focused shared primitives: 7/7 passed.
- Complete Brawler EditMode logic suite: 19/19 passed.
- Survivor non-performance EditMode suite: 37/37 passed, including actual classic 97a2b34 byte fixture and continuation.
- Complete Shooter EditMode logic suite: 14/14 passed after sweep extraction.
- Warmed primitive calls, crossed-blade fixed ticks, and repeated shared Brawler attack ticks each measured zero managed bytes in the harness. These are scoped CPU simulation assertions, not whole-frame or device-performance claims.
- Unity/Burst/PlayMode validation has not been run in this isolated worktree; the integration owner runs that gate separately.
