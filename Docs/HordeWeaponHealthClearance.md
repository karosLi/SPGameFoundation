# Horde armed-hero health-bar clearance

2026-10-08; local presentation correction based on `84b6fc23cb3721597ea8794ed27b9c7c32314216`. The health-specific native geometry/pixel checks and sampled visual review **passed on 67a22c1**. The overall native run **failed an existing Shooter allocation-frame gate**. Blade/sword motion acceptance remains separate.

## Verified problem and bounded change

The recovered `e023387` native Horde sequences, `Artifacts/Screenshots/WeaponMotion/weapon-horde-live-{gpu,fallback}/frame-025..042.jpg`, show the later health batch cutting across the raised staff silhouette. Frames 050–089 provide the bow/turn continuation. The 360×640 scene has a roughly 40-pixel hero, so moving the existing overhead bar farther upward would consume space above the action and closer to the top HUD. These original JPEG review pixels are retained; they are not lossless pixel-oracle files.

`SvRenderer` now anchors the existing 1.1-unit hero bar at a fixed 0.45 units below the root only when natural characters and the weapon runtime are both present. Its upper frame edge is 0.35 units below the ground root. The gap preserves the planted feet and shadow area; the native test must confirm the result at the actual small viewport. The anchor does not follow pose, facing, weapon phase, equip progress, quality or HP. It adds no sprite, batch, runtime allocation, per-frame collection or layout state.

The existing hero damage-number reservation expands downward to include that actual bar. It still uses one reservation slot. Non-natural/unarmed hero bars, enemy bars and beacon bars retain their existing anchors. HP fractions, frame/track colors, controls, simulation, save data, weapon geometry and render tiers are unchanged.

## Local checks and retained counterexample

The frozen focused `SvViewLifecycleTests` run passed **20/20** cases. The complete affected `SurvivorFoundation.Tests.EditMode` assembly then passed **168/168**, with zero failed or not-executed tests, against the same frozen production/test DLLs and source hashes. This did not repeat the all-project aggregate. The final native-only syntax compilation completed with zero errors and the one disclosed NUnit-version warning. `git diff --check` passed. The native outcome is recorded below; local compilation alone was not treated as native execution.

`SvViewLifecycleTests` checks the real renderer's prepared health and character batches:

- Legacy hero/beacon anchors and three-sprite bar counts.
- Whole rotated sprite-quads, including weapon art and planted feet, across continuous settle/turn/equip/attack/recovery for four weapons, both facings, movement and quality changes.
- Full, 40% and empty HP, visible frame/track, pause and unchanged authoritative snapshots.
- The actual disable/reenable path with simulation advancing while presentation is hidden; first 12 visible frames and pause/resume frames.
- An elapsed long-frame presentation step after skipped simulation/render samples, then the following 12 visible frames, plus pause/resume.

During development a diagnostic stepped full settle/equip intervals without rendering, then supplied only 1/30 second to the first visible sample. The next sword sample intersected the lower bar with both an axis-aligned and rotated-quad oracle. This counterexample is retained separately. The diagnostic had not actually settled the presentation and did not supply the elapsed long-frame delta. It is not reported as a passing case, and no motion/IK code was changed to erase it. Continuous presentation and the real lifecycle/elapsed-delta paths are separately tested; this is not a general guarantee about arbitrary externally supplied visual clocks.

Local evidence is retained under `integration-validation/resume-20261008/horde-health-clearance/` in the integration workspace: `original-pixels.json` identifies 12 copied source JPEGs with SHA-256 hashes, `fixture-failures.txt` retains the diagnostic failures, and the focused test/compile logs identify what actually ran. This local check does not substitute for Unity, Burst, GPU or device execution.

## Native regression and acceptance scope

`SvHeroHealthClearanceTests.ArmedHeroHealthRemainsVisibleOutsideWeaponAndFootMotion` is an actual graphics PlayMode test for GPU and DataTexture tiers. It uses the real gameplay weapon timeline, checks prepared geometry on every sampled action tick, and saves 360×640 full-scene captures for idle, windup, active and recovery, four weapons and both facings. Full and 40% HP must produce positive green pixels inside the projected fill footprint; the bar must remain within the viewport and clear the real header/menu/status/control rectangles.

An isolated camera copies the game's prepared arm/hand/weapon instances, excluding head, ground, HUD, VFX and bars. Its empty black control must contain no art, then the real arm/weapon batch must produce visible pixels. The original overhead staff-bar footprint must intersect actual pixels somewhere in the sampled cycle, while the new bar footprint must contain none. This proves the original class of occlusion rather than only comparing a chosen constant. Bow counterfactual pixel counts are reported without claiming that every bow pose intersects the old anchor.

The test saves full scenes in `Artifacts/Screenshots/MobileHud/health-horde-*.png` and isolated art in `Artifacts/Screenshots/HealthClearance/health-horde-*-arms.png`. Its native-only C# surface was compiled with the official Unity NUnit 3.5 assembly and UnityTest attribute source plus explicit engine API stubs. The expected NUnit 3.5/3.13 harness-reference conflict is disclosed; no engine rendering behavior was executed by that compilation.

After integration, run the focused native regression plus affected gameplay/lifecycle tests on the exact accepted commit, inspect the existing continuous staff/bow sequence at normal speed, and review both tiers' actual pixels before accepting the anchor. Verify the first visible recovery windows in native as well as the logic probes. The implementation worker did not start a native job or publish. Integration publishes this correction on its own branch from the verified `d2cc1f976a517bba3d0b9fb05f81412d8c84f996` tree. The ordinary Unity suites run the new pixel test without enabling another full weapon-video capture set; unchanged motion videos retain their exact d2cc1f9 provenance. Physical Android/iOS rendering, touch, sustained frame time, heat and power remain external gates.


## Exact native result and scoped visual review

On published `67a22c1a64f029aed992ba9f508315b0e60fdc1b`, tree `613e06e6d95f3d2652c1630f96c11bff1ab474a7`, [Mac Unity run 37747724041](https://github.com/karosLi/SPGameFoundation/actions/runs/37747724041) completed with **1,670 EditMode passes, zero failures, five original skips**, and **170 PlayMode passes, one failure, three skips**. The two additional PlayMode skips are the opt-in full-input movement recordings disabled on this independent health branch; those exact unchanged motion sources remain covered by d2cc1f9. The original explicit allocation-callstack diagnostic is also skipped. [.NET run 37747724035](https://github.com/karosLi/SPGameFoundation/actions/runs/37747724035) passed 1,631 tests with zero failures and six retained explicit diagnostics; build warnings/errors are zero.

All six new native EditMode health/lifecycle cases and both graphics cases passed. The complete 64 full-scene PNGs and 64 isolated arm/weapon PNGs were inspected at 360×640, covering four weapons, both facings and Idle/Windup/Active/Recovery. Full and 40% fill remain readable below the feet. The original overhead footprint intersects staff pixels **9 times GPU / 10 DataTexture**; bow counts are zero, so no bow counterfactual overlap is claimed. The new footprint has zero arm/weapon pixels in both tiers. An independent connected-component check of the stored images finds visible fill in all 64 scenes with no matching isolated-arm pixels over it.

The sole suite failure is `ShooterPlayTests.WarmSteadyFrameAndPresentationOnlyQuality(GpuDriven)`: 3/180 allocating frames and 123 bytes against the unchanged maximum of 2 allocating frames. Its original log lacks individual frame indices/callstacks; allocation origin and byte distribution remain unknown. The health feature's scoped success does not make the complete run green. A separate test-only evidence change records the next unchanged Shooter window before its assertion; no budget is relaxed.

Both Unity processes compiled without C#/Burst/shader errors or warnings; exits were 0/2 for EditMode/PlayMode. No crash retry or Burst-disabled fallback occurred. Burst controls remain managed/Run/Schedule 0/1/1; physics executed 60/60 warmup and 300/300 measured steps, mean 0.400 ms, worst 1.289 ms, under the unchanged desktop mean <4 ms gate. Both Survivor autoplay steady windows and 240-frame render-only stress windows reported zero allocated frames/bytes. These are desktop measurements, not phone budgets.

All 24 official wrappers/parts, 1,751 members and 386,430,840 archive bytes were restored and hash/size/CRC verified. Archive SHA-256: `0ed5437c54eafb495ed4bd405d88d613feac2ad2efd042035552a6d29f312479`. See [CI evidence recovery](CiEvidence.md) and the original run artifacts. The record establishes sampled phase pixels and native geometry through interruption/rebind; it does not supply fresh continuous video or interruption-window screenshots for this anchor. Physical Android/iOS remains pending.
