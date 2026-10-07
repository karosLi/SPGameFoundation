# Stage D: bounded optional save-envelope validation

2026-10-07. Scope and binary contract: [VersionedSaveEnvelope](../VersionedSaveEnvelope.md). Initial source base `5cca0c7`; the implementation leaves all raw writer/reader bodies and old expected bytes unchanged. This report distinguishes .NET-stub logic, installed Unity API compile-only and actual native execution.

## Red-first boundary evidence

[Retained red probe](SaveEnvelopeRedBaseline.cs) was run against the unmodified raw API on `5cca0c7` before implementing the new entry point. Both assertions expected rejection and failed with `Expected: InvalidDataException; But was: null`:

1. Two equal-size integer columns swapped registration order: raw restore accepted them. New explicit ordered schema validation must reject mismatched envelope layouts before live mutation.
2. Extra bytes appended to a raw snapshot: raw restore accepted them. This is the established composable raw-stream boundary, **not a claim of a raw API defect**. The new standalone frame requires exact EOF; old raw behavior is intentionally preserved.

The first probe compilation had direct assignments into the value returned by World.Column, producing CS1612. The retained version stores the NativeArray local before assignment and then produced both runtime assertion failures. This compiler correction is not counted as a red behavior reproduction. To reproduce, use an isolated worktree at the base, place the retained probe in its SPF EditMode tests, generate the harness and run `FullyQualifiedName~SaveEnvelopeTests`; do not combine the probe with the final same-named fixture.

## Focused implementation results

Initial completed focused pass: **49/49** .NET-stub tests, zero failures: 38 `SaveEnvelopeTests`, 4 `BwWeaponSaveTests`, 7 `SvWeaponSaveTests`.

Coverage includes immutable descriptor inputs; complete explicit table/resource/system coverage; equal-size column reordering; semantic version/system identity/order changes; content, mode, contract, runtime, raw-compatibility, TickRate, seed and capacity mismatch; unknown versions; all payload truncation positions in the small fixture; fixed header truncation; negative/zero/oversize lengths; checksum corruption including a visual-only header digest; exact payload limits; chunked non-seekable streams; outer and checksummed inner suffixes; no implicit legacy fallback; bounded explicit import into a fresh internally-created temporary session; raw-reader restart behavior; exact pause/host/manual states; and no Sync on prevalidation rejection.

Both real weapon compositions restore/continue actual gameplay and preserve raw bytes. Brawler verifies same-tick interpolation/weapon revision invalidation, authored skeleton/fixed-grid content rejection and the retained visual raw gate. Survivor verifies Classic and actual Guard-weapon factory/import paths, exact arrow continuation/damage, every SvSettings scalar leaf and every noncolor EnemyDef scalar leaf, and visual-only color/name changes. Reflection exists only in the field-coverage tests, never in production schema/fingerprint generation.

An initial test helper incorrectly expected exactly three pending ticks across repeated rejection checks; the preserved queue correctly accumulated to six. The helper now records the actual pending count before each rejection. Test-only exception type assertions and a cursor class name were also corrected during development. No production threshold, fixture byte or existing expectation was weakened.

## Independent review

A separate read-only review identified three issues, all corrected and re-reviewed:

- Checksummed bytes inside the raw payload were initially left unread. A narrow internal partial-Session helper now rejects them, restarts and preserves the actual private state plus host suspension. This particular case increments TimelineRevision twice (raw restore + restart); regular raw parse rejection increments once.
- Brawler's fixed unsaved grid origin was missing from content. It is now included with a rejection test. Survivor's dynamic saved grid origin remains correctly excluded from content.
- TransformFinalBlock on a full payload unnecessarily copied that payload. Integrity hashing now streams through TransformBlock and finalizes an empty block; memory accounting is explicitly scoped to framing, with raw-reader/capture/import costs separate.

No further blocking finding was reported. Review is not a test execution result.

## Integration gates

The focused implementation is being rebased onto the parent's current P1 integration before the full harness, all-assets real Unity API compile-only and 19-composition cold comparison. Those results will be appended after execution; an earlier green checkpoint is not substituted. Actual native Unity/Burst/graphics execution belongs to the publishing parent's exact-commit CI. Physical Android/iOS remains an external validation gate; desktop/stub results do not establish mobile memory or load-time budgets.
