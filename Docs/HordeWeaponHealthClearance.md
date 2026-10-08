# Horde armed-hero health-bar clearance

2026-10-08; local presentation correction based on `84b6fc23cb3721597ea8794ed27b9c7c32314216`. Native execution and visual acceptance of this change are **pending**. Blade/sword motion acceptance remains a separate gate.

## Verified problem and bounded change

The recovered `e023387` native Horde sequences, `Artifacts/Screenshots/WeaponMotion/weapon-horde-live-{gpu,fallback}/frame-025..042.jpg`, show the later health batch cutting across the raised staff silhouette. Frames 050–089 provide the bow/turn continuation. The 360×640 scene has a roughly 40-pixel hero, so moving the existing overhead bar farther upward would consume space above the action and closer to the top HUD. These original JPEG review pixels are retained; they are not lossless pixel-oracle files.

`SvRenderer` now anchors the existing 1.1-unit hero bar at a fixed 0.45 units below the root only when natural characters and the weapon runtime are both present. Its upper frame edge is 0.35 units below the ground root. The gap preserves the planted feet and shadow area; the native test must confirm the result at the actual small viewport. The anchor does not follow pose, facing, weapon phase, equip progress, quality or HP. It adds no sprite, batch, runtime allocation, per-frame collection or layout state.

The existing hero damage-number reservation expands downward to include that actual bar. It still uses one reservation slot. Non-natural/unarmed hero bars, enemy bars and beacon bars retain their existing anchors. HP fractions, frame/track colors, controls, simulation, save data, weapon geometry and render tiers are unchanged.

## Local checks and retained counterexample

The frozen focused `SvViewLifecycleTests` run passed **20/20** cases. The complete affected `SurvivorFoundation.Tests.EditMode` assembly then passed **168/168**, with zero failed or not-executed tests, against the same frozen production/test DLLs and source hashes. This did not repeat the all-project aggregate. The final native-only syntax compilation completed with zero errors and the one disclosed NUnit-version warning. `git diff --check` passed. Native execution remains pending for this isolated correction.

`SvViewLifecycleTests` checks the real renderer's prepared health and character batches:

- Legacy hero/beacon anchors and three-sprite bar counts.
- Whole rotated sprite-quads, including weapon art and planted feet, across continuous settle/turn/equip/attack/recovery for four weapons, both facings, movement and quality changes.
- Full, 40% and empty HP, visible frame/track, pause and unchanged authoritative snapshots.
- The actual disable/reenable path with simulation advancing while presentation is hidden; first 12 visible frames and pause/resume frames.
- An elapsed long-frame presentation step after skipped simulation/render samples, then the following 12 visible frames, plus pause/resume.

During development a diagnostic stepped full settle/equip intervals without rendering, then supplied only 1/30 second to the first visible sample. The next sword sample intersected the lower bar with both an axis-aligned and rotated-quad oracle. This counterexample is retained separately. The diagnostic had not actually settled the presentation and did not supply the elapsed long-frame delta. It is not reported as a passing case, and no motion/IK code was changed to erase it. Continuous presentation and the real lifecycle/elapsed-delta paths are separately tested; this is not a general guarantee about arbitrary externally supplied visual clocks.

Local evidence is retained under `integration-validation/resume-20261008/horde-health-clearance/` in the integration workspace: `original-pixels.json` identifies 12 copied source JPEGs with SHA-256 hashes, `fixture-failures.txt` retains the diagnostic failures, and the focused test/compile logs identify what actually ran. This local check does not substitute for Unity, Burst, GPU or device execution.

## Native regression and acceptance still required

`SvHeroHealthClearanceTests.ArmedHeroHealthRemainsVisibleOutsideWeaponAndFootMotion` is an actual graphics PlayMode test for GPU and DataTexture tiers. It uses the real gameplay weapon timeline, checks prepared geometry on every sampled action tick, and saves 360×640 full-scene captures for idle, windup, active and recovery, four weapons and both facings. Full and 40% HP must produce positive green pixels inside the projected fill footprint; the bar must remain within the viewport and clear the real header/menu/status/control rectangles.

An isolated camera copies the game's prepared arm/hand/weapon instances, excluding head, ground, HUD, VFX and bars. Its empty black control must contain no art, then the real arm/weapon batch must produce visible pixels. The original overhead staff-bar footprint must intersect actual pixels somewhere in the sampled cycle, while the new bar footprint must contain none. This proves the original class of occlusion rather than only comparing a chosen constant. Bow counterfactual pixel counts are reported without claiming that every bow pose intersects the old anchor.

The test saves full scenes in `Artifacts/Screenshots/MobileHud/health-horde-*.png` and isolated art in `Artifacts/Screenshots/HealthClearance/health-horde-*-arms.png`. Its native-only C# surface was compiled with the official Unity NUnit 3.5 assembly and UnityTest attribute source plus explicit engine API stubs. The expected NUnit 3.5/3.13 harness-reference conflict is disclosed; no engine rendering behavior was executed by that compilation.

After integration, run the focused native regression plus affected gameplay/lifecycle tests on the exact accepted commit, inspect the existing continuous staff/bow sequence at normal speed, and review both tiers' actual pixels before accepting the anchor. Verify the first visible recovery windows in native as well as the logic probes. The implementation worker did not start a native job or publish. Integration publishes this correction on its own branch from the verified `d2cc1f976a517bba3d0b9fb05f81412d8c84f996` tree. The ordinary Unity suites run the new pixel test without enabling another full weapon-video capture set; unchanged motion videos retain their exact d2cc1f9 provenance. Physical Android/iOS rendering, touch, sustained frame time, heat and power remain external gates.
