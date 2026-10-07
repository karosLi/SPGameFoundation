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

## Final integrated local result

Exact runtime/test head **`56a13dea32579afb4b76eb0f27406122380056b9`**, based on P1 integration `40a4323`. [Machine-readable result and raw-body comparisons](SaveEnvelope-20261007.json).

- Full .NET harness: **1,245 passed / 0 failed / 0 skipped**, 11 EditMode assemblies. Build: 0 warnings / 0 errors. These are Unity stubs, not native Jobs/Burst or graphics execution.
- All **519 Assets C# sources** compiled against installed real Unity 2022.3.62f2/package APIs: **0 errors, 7 existing warnings**.
- The same all-assets API project also compiled targeting **.NET Standard 2.1**, rather than net8.0: **0 errors, the same 7 existing warnings**. This checks the declared Unity-era BCL API surface; it does not execute Mono/IL2CPP/Unity.
- Two independent cold inventory processes are byte-identical and all **19 compositions** match the retained table/column/resource/system order, capacities, scopes and settings. Output SHA-256: `a42c9f27d2bb8d277303eef24e5b4cdbd58f0035d8bca89c7e08089348dd5f30`, identical to the Stage A baseline. Original baseline source hashes are not rewritten or claimed unchanged.
- Six entire raw/fixture files and eight raw writer/reader bodies were compared directly with `40a4323` and are unchanged; exact hashes are in the machine result. The full suite includes the original fixed Survivor raw fixtures and existing weapon/skill corruption/replay tests.
- The final consumer tests keep one session **uninterrupted**, comparing its fixed-input continuation against the envelope-restored session. The known-legacy Guard-weapon import also compares subsequent fixed-input continuation against an untouched original. Same-tick view invalidation is verified on the restored side separately.
- `git diff --check` and all added document relative links pass. No remote write was performed by this task.

The independent reviewer’s final test-coverage observation was that the first continuation tests restored both sessions. Commit `56a13de` corrects that by keeping untouched controls; it is included in the full result above. Framing-memory wording was also narrowed to avoid claiming that downstream raw readers allocate nothing.

### Reproduction

Use the normal `Tools/DotnetHarness/generate.py`, then `dotnet build` and `dotnet test Tools/DotnetHarness/.gen/Harness.proj` with `--no-build` only after the build succeeds. Run [GenerateCompositionApiCompile.py](GenerateCompositionApiCompile.py) with `--all-assets` and the installed Unity Data/NUnit paths; build its generated project. To check the BCL boundary, copy the generated project to a separate directory, change only TargetFramework from `net8.0` to `netstandard2.1`, and build again with its installed reference pack. Neither project is a Unity runner.

Repeat the existing [cold-probe recipe](FoundationCompatibilityProbe-20261007.md), comparing its observed compositions to the retained inventory without regenerating expected data. The raw-contract comparison lists the exact unchanged files/methods and baseline in the JSON. Preserve the external cache/dependency directories outside the commit.

### Remaining execution gates

Actual native Unity/Burst/graphics execution belongs to the publishing parent’s exact-commit CI and is **not claimed here**. Physical Android/iOS remains an external validation gate; desktop/stub/compile-only results do not establish device memory, IL2CPP runtime, load-time or sustained-frame budgets.
