# Confirmed capability acceptance checklist

This is a finite implementation/verification checklist for the user's supplied reference archetypes. Examples prove reusable foundation interfaces; they are not independent copied engines. Status is scoped to the evidence named here, not a claim that a display demo is integrated gameplay.

| Item | Concrete implementation / acceptance | Current status and evidence |
| --- | --- | --- |
| Portrait aircraft shooter | Touch drag, wingman, wave/pickup/three-choice loop, nearest beam, terminal/restart flow | Implemented in ShooterFoundation; two-tier gameplay captures and regressions passed. |
| Nonpixel horde guard | Finite waves, beacon target/HP, actual annular damage, batched bars/shadows | Implemented in Survivor guard configuration; two-tier gameplay tests/captures passed. |
| Flying-sword horde | Configurable sword movement/targeting, swept hits, stable pulse identity, shared skill HUD, dense deterministic scenario | Not yet implemented as a playable dedicated variant. Crossed-blade simulation alone does not close this item. |
| Landscape belt-scroller | Ground-depth movement/height contract, depth-aware hits, grid queries/AI, combo/stagger/recovery, basic drops/heal, terminal/restart | Not yet implemented. Current Brawler remains X-axis combat, including its opt-in shared-hit mode. |
| Shared combat integration | ActionTimeline/HitHistory/CombatSweep used by multiple rule sets, bounded queues, snapshot/reset, generation-safe identity | Implemented; shared Brawler/Survivor opt-ins and exact classic fixture tests passed. Classic projectile row-memory migration remains explicitly separate. |
| Skill-driven mobile HUD | Authoritative slots/cooldown/charges, joystick, click/hold/aim/cancel, ownership and safe-area/orientation | Functional raycast/mesh/lifecycle tests and four two-tier portrait/landscape/synthetic-inset capture cases passed. Fourteen actual PNGs produced and representative pixels inspected. |
| Natural characters in gameplay | Shared presentation inputs consumed by Brawler and Survivor, gait/attack/hit/death, stable identities and LOD | Separate natural character showcase is implemented and recorded; actual gameplay adapters remain open. |
| Natural motion / shadows | Planted feet, smoothed bounded IK, mirrored actors, sampled silhouette atlas and honest dynamic-IK blob fallback | 28 math cases and two actual scene cases passed; refreshed 30-frame recording produced. CPU/Burst cutout, not weighted mesh. |
| Weighted BAT / GPU / fallback | Real mixed weights, baked matrix texture, numeric/pixel parity, real production draw, CPU fallback and capability/precision gates | Vertex GPU skinning/limited IK and CPU weighted fallback implemented and tested. Optional compute-palette backend still open. |
| Layered mobile FX | Profiles, bounded pools, priorities/dedup/merge, sprite/coverage caps, quality independent of simulation | Implemented and tested; updated actual Shooter/Guard captures inspected. |
| Orientation and safe areas | Portrait aircraft/horde, landscape action/showcase, stable aspect fit, synthetic notch and multitouch tests | Actual 1280×720 action and 720×1280 horde HUD captures passed both tiers, including synthetic notch/home insets and real raycasts. No global orientation forcing. New variant captures remain pending. |
| Calibrated allocation evidence | Retained positive+empty controls, explicit samples/bytes, independent collections, unchanged frame budgets | Shared probe and migrated assertions passed actual Unity 621 Edit/18 targeted Play; complete local PlayMode passed 94 with 1 explicit skip; final EditMode passed 634 with 3 explicit skips and affected gameplay reruns passed 6 + 5; remote precision/native-readiness verification remains pending. |
| Dense collision / AI evidence | Candidate counts and deterministic outcomes at declared capacities, overflow counters, high-speed/tangent cases | Shooter 1024-target/4096-query evidence exists. New sword/belt scenarios need their own declared workload evidence. |
| New-game recipe | Data/system/view ownership, composing shared slots/query/FX/character adapters without common enum changes | Base integration checklist exists; final minimal example/recipe will be updated after both open variants. |
| Remote verified milestones | Commit/push each completed stage, exact trees and CI artifacts, WIP clearly separated | Stage 2 validated branch is green. WIP 93937656 passed .NET and 90 PlayMode cases but failed physics/tangency EditMode checks. Both fixes now pass locally and await exact remote CI before promotion. |
| Android/iOS device evidence | Physical touch delivery, graphics backend behavior, thermal/battery/native-memory measurements | External blocker: no physical mobile device supplied. Desktop Linux/Mac results are not substituted for this item. |

## Remaining bounded order

1. Close current HUD capture, calibrated/full regression, and remote CI; preserve each failed attempt and publish the verified milestone.
2. In isolated changes, implement the sword-horde and landscape belt-scroller using shared combat/skill primitives.
3. Connect the same natural-character presentation contract to both actual games; keep visual quality switches independent of authoritative state.
4. Add the optional bounded compute-palette path with real GPU/CPU parity and fallback tests, without pretending device speedups from software rendering.
5. Execute the two new variants' full interaction/restart/replay/capacity/quality matrix, update the integration recipe and publish.

Completion means all implementable rows closed with files/tests/evidence. External mobile-device validation stays explicitly blocked until hardware is available; no claim of complete device certification follows from desktop CI.
