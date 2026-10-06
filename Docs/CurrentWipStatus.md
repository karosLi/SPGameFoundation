Historical freeze record: this describes ee3d75a at publication time. Later verification and fixes are recorded in [MobilePresentationAndHudCheckpoint.md](MobilePresentationAndHudCheckpoint.md).

# Current work in progress — 2026-10-06 freeze

The user requested that existing changes be committed and pushed before more development. This branch is a coherent source snapshot, **not a fully validated release**. The separate `dot/mobile-foundation-validated` branch retains the completed second checkpoint and its measurement correction.

## Existing work included

- Shared mobile skill HUD: visible joystick, skill icons, authoritative fixed-tick cooldown/charges, click/hold/drag-aim/cancel, safe-area layouts; opt-in landscape Brawler and portrait-first Survivor adapters.
- Bounded layered combat FX and refined original Shooter/Guard sprites; protected critical-effect admission, explicit sprite/area budgets, stable entity-handle animation phases.
- Separate 14-bone CPU/Burst cutout natural-motion showcase, planted feet, smoothed IK, sampled silhouette shadows with blob/none fallbacks, landscape layout and aspect-fit controls. It is not the separate weighted BAT backend.
- TowerDefense price-label caching and calibrated allocation diagnostics.
- Shared testing-only allocation probe and migration of the remaining synchronous byte-only assertions to positive/empty-calibrated measurement with explicit units.

## What has and has not been verified

- FX before the final allocation-test migration: 14 Unity EditMode and 7 graphics/gameplay cases passed. Actual updated Shooter/Guard pixels were inspected.
- Initial natural-motion version: 28 Unity EditMode and 2 actual scene cases passed; both sprite tiers and 20 PNGs inspected. Later layout/neck/sole refinements have compile-only checks; refreshed graphics and the implemented optional 30-frame recording have **not** run.
- Skill rules/control tests: 27 targeted real Unity EditMode cases passed. The first four real UI cases failed because a custom Graphic lacked CanvasRenderer. The requirement/factory and seven geometry regressions are now added, but the corrected Unity UI cases are **not yet run**. Do not assume a working touch HUD solely from harness tests.
- TowerDefense cache behavior and calibrated focused label probes passed. Retained-array controls produced 32 Unity GC.Alloc samples while the raw thread-byte API returned 0. Former/fixed label windows observed 1024→0 (tower) and1536→0 (build) samples per256 refreshes. That narrow result does not attribute the remote Mac328-byte residual.
- A fresh Editor run of the original unchanged TowerDefense budget passed both tiers with0/180 allocating frames and0 completed gen-0 collections. An earlier combined run failed DataTexture7/180 frames/287B. That failure remains evidence; its cause is not proven.
- The shared probe's own .NET tests passed5/5 and Unity-reference compilation passed. Its dedicated Unity tests and the final12-file assertion migration have **not run**. The final migration received static diff/asmdef checks only: no compile, .NET run or Unity run after that last commit.
- No physical Android/iOS, thermal or battery validation. Cloud graphics use Mesa llvmpipe. Desktop Mac CI is a separate platform gate.

## Known prior CI issue

The first published checkpoint had one Mac/Metal TowerDefense budget failure:4/180 allocating frames,328B, against the unchanged allowance of2. This is not4 collections; that old window did not record collections. Do not call the issue resolved until a matching remote rerun confirms the remediation.

## Next validation work, deliberately paused for publication

1. Compile and run calibrated allocation assertions; treat unavailable calibration as a failure, never aszero.
2. Rerun real joystick/skill raycasts, geometry, lifecycle, landscape/portrait and safe-area captures.
3. Refresh natural-motion frames and optional recording after the placement refinements.
4. Run the complete regression, then the remote Mac budget, without raising thresholds.
5. Integrate natural characters into gameplay and continue ground-depth/belt-scroller capability only in a later reviewed change. The current Brawler remains X-axis gameplay; the character lab is a presentation showcase.

No new features or tests were started after this freeze. Source history and earlier failed evidence are preserved; this WIP snapshot is intentionally published separately from validated code.
