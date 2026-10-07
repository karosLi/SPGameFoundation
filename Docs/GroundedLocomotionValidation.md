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
