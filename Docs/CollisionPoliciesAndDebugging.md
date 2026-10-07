# Weapon contact policies and authoritative debug views

This bounded slice fixes two confirmed weapon-adapter problems and exposes the existing collision rules. It does not replace the game pipelines, migrate classic saves, or introduce general rigid-body, wall, homing, gravity, or piercing behavior.

## Confirmed fixes

### Belt: ground entry is not always the first valid 3D contact

The former weapon adapter found the first ground-circle TOI and tested target height only at that instant. A falling target could be too high at ground entry, enter the vertical hurt interval later while the ground circles still overlapped, and incorrectly receive no hit.

`CombatSweep.CircleContactInterval` now returns a closed relative-motion ground interval in double precision. `GroundCombatQueries.SweepProjectile` intersects it with the interval in which projectile height overlaps the target's moving hurt interval. Only the final joint TOI is converted to float. There is no contact epsilon or inflated hurtbox. This also rejects cases where ground and height overlap at different, non-overlapping times.

The focused reproduction enters the ground overlap at t=.25 and the valid combined volume at t=.375. The actual belt adapter test gives a target an existing falling state, puts a live staff projectile inside its ground footprint, advances the normal session, and verifies damage at a later fraction. A target that stays too high remains unharmed and does not consume the projectile. Original `Circles`, `PointCircle`, and `PointCircleLegacy` implementations remain unchanged.

### Horde: use measured motion once per weapon collision pass

The former adapter recalculated a bound from authored enemy speeds/radii inside every projectile loop. The weapon adapter now scans the current live target rows once when at least one projectile is active and records the largest previous-to-current displacement. This conservatively includes actual separation, knockback and beacon correction rather than assuming only authored locomotion.

A regression makes a target cross a stationary projectile from x=-6 to x=6, beyond the old authored bound, then invokes the real weapon collision system and verifies the accepted damage command and TOI. Separate 1/32-projectile tests verify that 16 target rows are examined once in either case.

This is an explicit O(targets) pass; it is not automatically cheaper than scanning a short enemy-definition list for a single projectile. It can tighten the broad phase and removes repeated per-projectile definition work. Full-tick native measurements, candidate density and projectile count determine the performance tradeoff. No unsupported speedup or mobile-budget claim is made.

## Rules preserved and made explicit

| Path | Geometry / boundary | Ordering and filtering | Lifetime / overflow |
| --- | --- | --- | --- |
| Shared belt projectile | Relative ground circles intersect moving vertical hurt interval; closed tangencies/endpoints; initial overlap allowed | Earliest joint TOI, then stable handle index/generation; owner, faction, KO and per-scope history | First eligible contact consumes projectile; fixed pool and tick lifetime |
| Shared horde projectile | Relative ground-circle sweep; closed contacts; visual height does not affect damage | Earliest TOI, stable handle; owner/dead/history filters | First eligible contact consumes projectile, **including damage-queue rejection** |
| Shared belt melee | Existing capsule/point-circle ground test plus belt hurt height; closed contacts | Existing traversal and per-attack stable history, one authoritative contact tick | No damage during visual follow-through |
| Shared horde melee | Existing ground capsule; closed contacts | Existing traversal and stable per-attack history | Queue admission precedes recording a target |
| Shooter | Existing legacy relative-circle path and tiny-motion cutoff | Earliest TOI then stable spawn ID; team and hero invulnerability rules | Existing hostile/friendly/lifetime behavior |
| Flying swords | Existing relative swept contacts | TOI then handle index/generation, per-sortie history | Existing bounded pierce/return behavior |
| Classic RPG / Survivor | Existing static-target/row-memory policies | Existing filters and replay behavior | Not migrated by this slice |

`ProjectileCollisionProfile` is a small derived shape value, not a new serialized weapon content schema. Its belt geometry uses the existing projectile radius, body ground radius, hurt bottom/top and a closed boundary. `CombatCollisionPolicy` shares filter/tie/history-reason helpers; host damage, feedback, queues and state transitions remain in their original adapters. Enemies in the two shared weapon examples do not currently have a separate invulnerability flag; those adapters pass false explicitly. The reusable filter supports invulnerability where a host actually supplies it.

Grid `Query` remains strict circle overlap, while `QueryCells` plus inclusive weapon narrow phases preserves tangency. Broad-phase motion/radius padding is not a narrow-phase tolerance. Shared weapon defaults do not pierce, home or fall under gravity. No wall/occluder geometry exists in the belt/horde examples. Classic RPG retains its tile line-of-sight checks; this slice does not claim a new continuous earliest-wall TOI API.

## Contact is not necessarily applied damage

The optional trace separates `Reason` and `DamageOutcome`:

- `Candidate` / `None`: geometry and filters admitted a candidate; it may lose earliest-contact selection.
- `Accepted` / `Applied`: the belt adapter applied its damage transition directly.
- `Accepted` / `Queued`: the horde adapter successfully queued damage and recorded history. This alone does not certify the later resolver's HP change.
- `QueueFull` / `None`: a physical eligible contact occurred but the horde damage queue rejected the command. The projectile is still consumed, as before; no accepted hit-history record or damaging impact cue is invented.
- Other rejection reasons identify self/friendly/dead/invulnerable, ground or height miss, duplicate, full history, invalid target or inactive scope.

`SvProjectilePolicyTests.FullDamageQueueConsumesContactWithoutRecordingOrInventingAcceptedImpact` fills the queue's actual capacity (the module allocates twice `Capacity.Events`), then verifies consume-on-contact, no accepted record, unchanged queue occupancy and an explicit `QueueFull` trace. Queued and directly applied damage are also labeled in screenshot CSVs. Green means an admitted damage operation, with queued/applied status in the trace, rather than a claim that every contact changed HP.

## Body, hurt and attack shapes

Belt diagnostics show cyan ground body circles (also the weapon target ground circles), magenta rectangular ground footprints for bone-probe hurt rules, and the moving vertical hurt interval. The rectangle does not replace the projectile target circle. Ground depth is projected with the existing .55 factor, independently of jump height. A body footprint is not an attack capsule, and a sprite silhouette is not a hurtbox. The existing separation height gate and ground-depth rules remain authoritative.

Horde body/hurt ground circles coincide by design: magenta circles and cyan center markers make that identity explicit. The blue line from a projectile ground center to its displayed height shows the distinction between gameplay plane and rendered weapon height. It does not add a vertical horde collision rule.

Amber capsules are actual previous/current weapon sweeps. Green crosses mark accepted points, red crosses mark rejected-target witnesses or rejected queue contacts. Rejected geometry has no physical contact point, so its marker is the candidate target center; the CSV reason states the rejection. Accepted points are the actual points used for the weapon impact cue.

The arrow artwork's separate render-only anchor work belongs to the projectile-art slice: the authored arrow tip should align with the collider's forward boundary, center + ground direction × radius × scale, while the spell core stays centered. Belt overlays use `BwBeltRules.Project` for both points; horde overlays show the ground and raised display anchors separately. Blue authority-center markers and amber forward-boundary lines expose that convention. This does not inflate damage shapes or make arrow art authoritative.

## Enable and budget the debug view

Weapon example bootstraps attach `BwCollisionDebugOverlay` / `SvCollisionDebugOverlay` with `ShowCollisionDebug=false`. Toggle that property on `game.CollisionOverlay` or in the component inspector. Existing renderer/particle methods are not modified. Classic modes do not attach these components.

The common view uses the existing sprite backend on both GPU-driven and DataTexture tiers:

- 256 shape descriptors, 4096 line-sprite segments, 16 segments per circle
- At most 48 actors and 48 current trace records submitted per host frame
- 256 trace records per weapon runtime; newest diagnostics are rejected when full, with an explicit count
- Last accepted diagnostic retained for at most 12 simulation ticks, cleared on toggle-off, reset, cancel and snapshot restore
- No per-shape GameObjects or growing lists; GPU/texture/mesh resources are created on first enable, warmed and disposed with the component
- The trace is preallocated and unsaved; it never controls collision acceptance, lifetime, queue admission or damage

Disabled views submit no overlay sprites. Enabled diagnostics add main-thread geometry and upload work; their cost is not part of a claimed normal-gameplay mobile budget. Capacity overflow drops diagnostics only. The existing `WeaponRuntime` snapshot fields, version and content fingerprint remain unchanged.

## Named verification and evidence boundary

EditMode coverage:

- `GroundProjectileSweepTests`: 29 interval/height cases, original defect, disjoint intervals, tangencies, zero motion, moving bodies, negative and extreme scales, matching-mode Strict/Fast Burst sentinels, calibrated allocation window
- `BwProjectilePolicyTests`: actual falling-target damage, height rejection without despawn, unsaved trace/restore behavior
- `SvProjectilePolicyTests`: measured crossing-target bound, projectile-count-independent scan, full-queue semantics
- `CombatCollisionPolicyTests`: filter/tie policies, fixed trace budget, derived profile, zero-allocation trace recording and bounded debug geometry
- Existing weapon, classic game, save/restore and allocation tests remain required

Native capture fixtures are separate from gait/art tests:

- `BwCollisionDebugTests.WeaponBeltOverlayShowsActualContactsWithoutChangingGameplay`
- `SvCollisionDebugTests.WeaponHordeOverlayShowsActualContactsWithoutChangingGameplay`

Each runs both tiers, proves real damage and a geometric rejection, compares enabled/disabled GPU readbacks for diagnostic colors, records projectile TOI, exports contact/rejection/damage-outcome CSVs, checks read-only snapshots and lifecycle clearing, and captures 45 frames under the normal automatic clock at a requested 30 Hz with actual acquisition timestamps. They do not manufacture animation frames or change Player settings. Screenshots/CSVs are written under `Artifacts/Screenshots/MobileHud`; raw timestamped sequences use `Artifacts/Screenshots/WeaponMotion`.

The guarded native sources compile against real Unity 2022.3 engine/package/TestRunner DLLs. That is API compatibility evidence, not a native run. Actual GPU pixels, recordings, feel review and native Burst remain pending the lead's exact integrated-head Mac CI and visual inspection. Static outlines and mathematical correctness alone do not prove gameplay quality. Physical Android/iOS touch, sustained frame times, thermal/battery and native-memory validation remain external device gates.

## Local verification for this commit

All 76 generated assemblies build with zero warnings/errors. The complete .NET harness passed **949 executed tests, zero failures**, with the same two explicit on-demand tooling cases unexecuted (`SearchShots`, `ExportFrames`). This includes 647 foundation, 78 Brawler and 91 Survivor tests on this branch. Calibrated trace and geometry hot windows report zero current-thread managed bytes. The separate actual-Unity-DLL API build reports zero errors and four pre-existing serialized-field warnings. No native graphics run is implied by these results; the four new PlayMode backend cases and their recordings still require the exact integrated-head native run.
