# Grounded locomotion correction

## Status and scope

This change addresses the shared hero/enemy gait in the actual Brawler and Survivor presenters. It changes presentation only: world movement, fixed-tick damage, skill clocks, weapon release sockets, art meshes/sprites and cameras are unchanged.

Controller commit `b504c05` passed the full 76-project .NET build (zero warnings/errors), all 857 .NET tests, and a separate 54-test focused contact/role/weapon/skill pass. The capture-fixture regression also passed independently. Native Unity execution and continuous-video acceptance must be reported separately for the final integrated commit. Numeric tests alone do not establish that the movement looks natural.

## Confirmed baseline failure

The pre-fix controller at `faa0014` limited stance to `min(period * 0.62, 0.52 / speed)`. That can be less than half a cycle even while the locomotion classifier reports Walk. Each foot could independently release early or restart its swing. Their phases could drift permanently, producing synchronized hops after changes in speed/direction.

The pelvis solver also used the minimum reach height of both feet, including a swing foot. Because screen Y combines projected ground depth and height, the trailing depth target could pull the body into a deep crouch every step. The old clamp permitted a 0.52-model-unit drop. Merely reducing the decorative bob would not repair either cause.

The original 1× videos were real automatic-clock game captures, but the portrait weapon fixture deliberately froze enemy movement. It was therefore not evidence of monster walking. The added dedicated sequences move actual agile and heavy enemies.

## Controller changes

- One bounded support-transfer controller owns both feet. Walking alternates support and includes double support. A foot cannot independently abandon the sole walking support.
- Running releases the supporting foot only in the short final part of its partner's swing. This controlled locomotion flight does not set the authoritative airborne/jump flag or modify the root.
- Travel speed changes cadence while contact span stays within the existing rig's reach. Landing footprints account for projected depth rather than requiring the entire body to follow a low trailing swing foot.
- Left/right footprint offsets mirror with the character. Slow movement also reduces foot clearance, avoiding high steps at crawling speed.
- Acceleration and reversal can shorten the partner's remaining landing time. In-flight retargeting preserves the current trajectory position and does not restart a full swing every frame, which previously could cause indefinite hovering.
- Support recovery has a brief 40 ms toe-off reach constraint and bounded upward relaxation. Normal swing feet do not drive pelvis height. Exceptional reversal compression cannot snap the newly lifted FK foot away from its previous contact.
- The pair uses at most 12 fixed-size presentation substeps for the existing maximum 0.1 s input step. It uses no dynamic collections or per-frame allocations. No physics/GC budget was relaxed.

## Matched numerical evidence

The baseline and corrected exports use identical inputs: 30/60/120 Hz; hero, agile and heavy profiles; seven speeds from 0.15 to 7.58 model units/s; horizontal, diagonal and depth travel. Each case runs five seconds, with statistics over seconds 1–5. This is 189 scenarios and 52,920 steady frames per revision.

| Measurement | Baseline | Corrected candidate |
| --- | ---: | ---: |
| Walking frames with neither foot in stance | 4,883 / 32,760 | 0 / 32,760 |
| Largest steady-walk pelvis height range | 0.264884 | 0.008255 |
| Largest steady-run pelvis height range | 0.431191 | 0.013844 |
| Worst scenario's run-flight fraction | 71.67% | 12.50% |
| Largest per-frame pelvis height change | 0.179565 | 0.008503 |
| Largest head-height range | 0.437070 | 0.027331 |

Heights are relative to the authoritative ground/root and expressed in model units. These are deterministic presentation traces, not measured device performance or video-derived estimates.

The regression suite is broader than the export: 576 steady role/speed/heading/frame-rate combinations, including both signs of both axes and 0.04-unit/s movement, plus 36 eight-second start/stop/reversal/turn paths. It checks:

- No unintended walking double-air interval and genuine double support
- Both feet actually stepping, rather than satisfying contact assertions by hovering
- Stable pelvis/head height, bounded run flight, finite IK and unchanged jump authority
- Exact rendered FK contacts and no sliding of a continuous plant
- Existing extreme 5-world-unit/s depth reversal and 30/60/120 Hz turn/reach limits
- Existing weapon grip/release sockets, concurrent skill layers, stable identity and warmed zero-allocation probes

Export with `SPF_GAIT_TRACE=<output-directory>` and run `GroundedGaitTests.ExportGroundedGaitTrace`. Each CSV retains per-frame root, ground-depth velocity, locomotion, foot phases/contact flags/positions, pelvis/head height and authoritative jump height. Keep baseline and candidate exports in separate directories.

## Native evidence protocol

Capture-only commit `d1a240c` adds instrumentation without the gait fix. Its initial horde fixture incorrectly used a zero-based enemy ID; baseline fix `85b36cd` corrects the calls to the spawner’s one-based IDs. The fixture now asserts the count, IDs and agile/heavy radius roles, and a separate .NET regression executes the actual spawns and confirms all three enemies move. `SPF_WEAPON_GAMEPLAY_SEQUENCE=1` retains the original weapon clips and adds:

- `grounded-belt-live-{gpu,fallback}`: real hero, agile and heavy enemies
- `grounded-horde-live-{gpu,fallback}`: real hero plus moving agile, standard and heavy enemies

Each sequence uses 160 raw buffered images, the same input schedule/camera before and after, normal automatic simulation, 568×320 landscape or 320×568 portrait, and the existing measured acquisition timestamps. It includes walk, run, stop, reversal and depth walking. PNG encoding/CSV serialization happens after acquisition. No frame interpolation or fixed-FPS relabeling is permitted.

Each sequence also writes `gait.csv` beside `acquisition.csv`. Actor state and bones are readback-time annotations of the rendered presenter, not a replacement clock or a manufactured animation. Inspect the 1× continuous video and individual transitions along with the trace. Report actual acquisition cadence and exact source commit/backend.

Both capture fixtures and 201 dependency sources were compiled against the installed Unity 2022.3.62f2/package DLLs with no Unity stubs: 203 sources, 96 native references, zero errors. This verifies API compatibility only; it is not a Unity graphics execution result.

Physical Android/iOS frame pacing, touch behavior, thermal behavior and sustained performance still require physical-device testing. Desktop native captures cannot substitute for that gate.

## Native interruption follow-up (2026-10-07)

The first integrated native candidate passed its graphics tests, but actual continuous footage still exposed defects outside the original steady-motion tests. Those observations are retained; that candidate is not claimed to satisfy all-state visual acceptance.

1. In the belt GPU recording, a one-frame stop during a far-foot swing was followed by knockback/restart. Restart always lifted the near foot, although it was the sole remaining support. The regression now reproduces this at 30/60/120 Hz with short 1–3-frame stops, three roles and both ground axes. Restart preserves an unfinished transfer and only starts a new placement when both feet are planted.
2. A horizontal weapon facing reversal temporarily rotates the smoothed drawing direction through downward Y. The old pelvis term treated that intermediate angle as a real downward aim and produced a 0.163–0.164-model-unit crouch without vertical aim. Pelvis accommodation now has its own response to the authored aim direction. Genuine downward idle hold, moving aim, recovery and active release remain supported; all four weapon families retain the unchanged 3 mm release-socket bound. Hit recoil is preserved.
3. In the isolated portrait depth-turn sequence, the controller lifted the forward contact immediately after the rear foot landed, leaving the rear contact to support a newly reversed velocity. The body then compressed and recovered. A reconstructed boundary from frames 135–136 fails the old controller (0.341-model-unit pelvis excursion at 60 Hz and contact failures at 30/120 Hz). Double support now considers which contact will lose reach first, while preserving alternation when both are viable. A freshly landed foot that never bore weight does not acquire a toe-off support hold. The corrected boundary stays at approximately 1.045–1.053 model-unit pelvis height at all three rates.

### Stronger capture attribution and role coverage

The existing CSV columns remain in order. Appended fields identify the entity, current-frame viewport visibility, source gameplay state/action/time/HP, scripted scenario phase, hit/attack weights, facing/turn, support and airborne ages, toe-off support flags, support ceiling/transfer delay, weapon aim/accommodation and skill pelvis contribution. Capture reads only current-frame submitted actors; a retained offscreen identity no longer counts as visible evidence. Serialization remains after acquisition and sample capacity remains 160 × 4.

The original heavy horde spawn overlapped the beacon exclusion, became stationary, and later left the camera. That was a capture-fixture gap, not evidence of heavy walking. The revised portrait fixture keeps the camera unchanged and uses clear, separated spawns plus a bounded hero route. NPC content speeds explicitly change from slow to fast and back through the real automatic-clock simulation. These are labelled scripted test inputs, not fabricated poses. Agile/standard/heavy spawns are (3.5,4.2), (0,6.5) and (-3.5,7). Their slow speeds are .4/.45/.3 world units/s; frames 32–55 use 1.4/1.9/3.7 before returning to slow motion. The real simulation/production-adapter preflight passes at 20/21/22/23/30 Hz and at alternating 20–23 Hz intervals, with every role retaining grounded walking support. Each recorded role must have at least 120 visible samples, 100 moving samples, 60 Walk samples, 16 Run samples and two world units of cumulative travel. The revised portrait fixture is fresh coverage, not an alleged matching pair with the invalid old portrait fixture; the belt comparison retains its original scenario.

A fresh native run and 1× inspection of both backends are still required before accepting this follow-up visually. Local API compilation and numeric regressions are necessary but do not replace that gate.

## Reference-informed coordinated Walk correction (2026-10-07)

### Diagnosis and scope

The user-supplied gameplay reference and exact `2066211` Belt footage were inspected as decoded frame sequences. The existing Belt Walk has alternating support, but still reads as high knees below a relatively rigid guarded upper body. The reference supports articulated, offset body/limb silhouettes; its small, occluded humans do **not** establish reliable human contact duty, foot clearance or cadence. Its larger dragon is hovering/attacking, so that motion is not copied into grounded humanoids. No reference art, font, texture or character asset is incorporated.

This bounded follow-up preserves the coupled contact controller, cadence/reach bounds, 14-bone cutout rig, Run flight distinction and independent three-bone BAT ABI. It does not change Runtime, damage, snapshots, camera, input schedules or rendering tiers.

- Walk swing lift is lower than Run and smoothly responds to speed, including slowing/stopping while a foot is airborne. Creeping retains very small lift rather than a full-height step.
- Landing prediction previously combined forward projected-depth travel with a full upward depth bias. The new bias uses up to 0.12 model units of the existing knee-flexion reserve before raising the footprint. The remaining bias is retained for large/deep strides; a swing still cannot pull the pelvis down.
- The pelvis now rotates a restrained amount with actual foot transfer, with opposing chest rotation, speed/acceleration-dependent lean and head counter-rotation. Leg support-height calculations use the rotated hip anchors and feet remain level.
- A relaxed weapon guard follows transfer by a few degrees. The contribution is multiplied by the existing relaxed/action weight and disappears exactly at canonical contact/release. Dominant/support IK and reach accommodation use the full parent rotation. Moving attacks retain the same lower-body contacts and return continuously to the guard.
- Hero/agile/heavy profiles retain distinct cadence/support widths and now separate their actual clearance/arm arcs more clearly. All three are grounded humanoid profiles; no flying or quadruped capability is implied.

### Measured quantities are different

“Ankle elevation” below means rendered ankle Y minus root/contact-plane Y, divided by the rendered head height above that plane. It includes the ankle rest offset and projected ground depth. “Path clearance” subtracts the interpolated projected start/end contact trajectory from the swing ankle Y, then divides by stature; it measures the additional swing arc only. Neither is a sole-to-physical-terrain measurement, because this rig has a projected 2D ground plane.

The reference review's 4–8% suggestion was a tuning hypothesis, not a universal gate. Tests use bounded regressions for the identified recorded input and check support, continuity and articulation separately. They cannot certify naturalness.

### Matched numeric comparison

The baseline compiles the exact three presentation files from `14652d5`, with the same new probe and remaining dependencies as the candidate. Inputs match the observed Belt hero's scale 0.9 and X velocity 0.864; projected depth is 0 or ±0.3168 world units/s. Both revisions run at 30/60/120 Hz for five seconds, measuring seconds 1–5. This is a deterministic math comparison, not new native footage.

| Measurement | Baseline | Candidate |
| --- | ---: | ---: |
| Horizontal ankle elevation / stature | 10.46% | 6.01% |
| Positive-depth ankle elevation / stature | 19.90–20.05% | 11.52% |
| Negative-depth ankle elevation / stature | 19.81–19.83% | 11.43–11.45% |
| Swing path clearance / stature, all three paths | 6.47–6.48% | 2.02–2.15% |
| Pelvis rotation excursion | 0 | 0.0455–0.0485 rad |
| Chest rotation excursion | 0.1555–0.1658 rad | 0.1863–0.1986 rad |
| Relaxed guard rotation excursion | 0 | 0.0855–0.0912 rad |

The unchanged free-elbow phase remains active; the free upper-arm range is role-specific. At the same one-model-unit/s horizontal speed over the seven-second steady window, hero/agile/heavy actual free-arm arcs are 0.648/0.771/0.374 rad, additional swing clearances are 0.0396/0.0500/0.0292 model units, and landing counts are 13/15/12. These are measured output differences, not a claim that three numerical profiles alone establish three convincing performances.

### Focused verification and pending native acceptance

`CoordinatedLocomotionTests` adds 16 cases: matched horizontal/positive-depth/negative-depth Walk at three frame rates; creeping/start/stop/Run/reversal/depth continuity across all roles; measured role output separation; and all four weapon families at both facings under moving action/recovery. It checks exact planted FK, absence of unsupported Walk, smooth lift response, idle settling, unchanged lower-body positions under the action overlay, guard transition continuity and the existing 3 mm canonical socket limit. The nine captured-path regressions fail against the exact baseline. No existing bound was loosened.

The focused candidate suite includes the new cases plus all existing `GameplayCharacterTests`, `GameplayMotionProfileTests`, `GroundedGaitTests`, `GroundedGaitInterruptionTests` and `WeaponMotionTests`. Their existing zero-allocation, identity, support, run-flight, weapon, skill and reach tests remain required. **Candidate result: 83/83 focused tests passed, zero skipped; focused project build had zero warnings/errors.** [Machine-readable source hashes and scope](validation/CoordinatedLocomotion-20261007.json) identify the tested files. The real-API compile passed 235 source files with zero errors and four existing serialized-field CS0649 warnings. An initial reuse of an old compile-only source list missed newer composition/audio dependencies; rebuilding the actual dependency closure fixed that validation recipe. A separate compile-only check covers both modified capture fixtures and their production dependencies against installed real Unity 2022.3 APIs; this does not execute graphics or Burst. The main integration owner runs the combined aggregate and native CI for the exact integrated commit.

Both native `gait.csv` fixtures only append columns: pelvis/chest/head world angles, free-elbow relative angle, guard angle, head X and each foot's path clearance. Existing columns/order, frame count, timing, camera, speed schedule, actor selection and 1× encoding rules remain unchanged. The six extra captured floats occupy a bounded 15,360 bytes for each 160×4 test sample buffer; clearance is derived after acquisition. Production sprite counts and buffers are unchanged.

Visual acceptance remains **pending** centrally queued, matched real-game GPU and fallback clips at recorded 1× PTS. Inspect full phone-size view and labeled close-ups of low-speed Walk, Run, start/stop/reverse/depth, roles, all weapons, attack/recovery and interruption. Check feet/support, pelvis/chest/head and held/free hands together. A numerical pass, lower elevation or static pose does not close this gate. Physical Android/iOS performance and sustained thermal/input acceptance remain separate.
