# Continuous gameplay motion and held weapons

The actual shared `GameplayCharacterPresenter` now consumes the authoritative `SPF.Contracts.Weapons.WeaponViewState` supplied by Brawler and Survivor. The four original outlined silhouettes are saber/刀 (slash, free supporting arm), sword/剑 (two-hand thrust), staff/法杖 (two grips and a cast socket), and bow/弓箭 (bow hand, string hand, live string and arrow release). These are presentation of the shared equipment/action runtime, not an independent decorative inventory or damage system.

## What changed

The old gameplay composition always aimed the near arm and then solved it again during contact correction; this erased its run swing. The far forearm retained its bind angle. The replacement keeps a relaxed two-arm gait until an aim/action layer requests control. Actual near/far foot phases drive weight transfer, pelvis support, chest opposition and elbow swing. Smoothed travel velocity adds distinct forward, backpedal and ground-depth lean; the head counter-rotates. Breathing is only a small relaxed layer. A bounded turn value moves shoulders through the turn and narrows the chest silhouette. Stable identity, explicit teleport reset, reachable world-space foot plants and bounded foot-cycle durations remain in place.

Body, foot, hand and weapon composition runs every render step. Quality-limited base/bind refreshes are still counted by `PosesEvaluated`; that counter does **not** measure every continuous body/IK evaluation. Reduced quality cannot hold a bowstring or hand pose at 15 Hz while the actor moves. It also cannot change action phase or simulation.

Weapon trajectories use a narrowly scoped cubic Hermite sampler with shared tangents. The contact key is traversed with nonzero velocity rather than eased to a stop. `ContactPhase`, `ActiveEndPhase` and `ReleasePhase` come from the profile's fixed-tick timeline. There is no low-pass filter on action phase. The runtime adapter interpolates the authoritative previous/current action tick for rendering; hit windows remain integer simulation ticks.

The dominant hand is solved to the weapon target; the support hand follows the solved dominant hand. IK blends the *target position*, retains elbow bend direction and reserves 14 mm of extension to avoid a singular straight elbow. The bow string's draw point is distinct from its grip, stays taut through release, then recoils continuously. A small downward aiming crouch keeps directional release sockets reachable. Equip lowers/releases the old grip, then uses an outgoing-position/velocity Hermite transition for the new grip and angular velocity. This is a bounded hand-space transition, not full-body inertialization.

## Sockets, drawing and bounds

`TryReadWeapon(handle, out WeaponAttachmentSample)` is valid after `Evaluate`. It returns the actual FK dominant/support grips and weapon tip/muzzle/direction, visual ID, action pulse and sequenced authoritative cue flags. Particles consume these samples; damage must never read them. Collision and projectile origins remain the runtime's canonical profile sockets. Tests verify canonical release alignment for all sixteen aim directions, plus neutral contact at both facings and 30/60/120 Hz.

Weapon-enabled modes opt in at `GameplayCharacterPresenter(..., includeWeapons:true)`. Their art is created once with the original supersampled character atlas. The default unarmed presenter retains its existing 1 MiB atlas and 14-part stream capacity; it explicitly rejects a weapon-bearing input. The weapon-enabled atlas is 2 MiB. A new content ID can reuse an existing visual ID. Unregistered visual IDs explicitly fall back to their action family's authored silhouette and matching pivot/size. Explicit pixel pivots align the grip and tip/orb; bowstring segments are live batched geometry. An equipped actor uses 19 packed sprites instead of 14. The weapon is inserted within that actor's stream before the near arm/hand, and the support hand is drawn over its grip. Every part keeps the actor's ground depth. There is no global weapon overlay that can draw a back actor's weapon in front of a nearer actor.

The stream remains one fixed-capacity batch. The maximum adds five 32-byte instances per equipped actor; an unequipped actor still submits fourteen. Native socket output and sprite offsets are allocated once per capacity. There are no actor GameObjects, Animators, growing hot-path arrays or per-frame texture generation. Atlas memory is exposed by `ColorAtlasBytes`; the weapon opt-in doubles height from 256 to 512 at width 1024. This desktop implementation does not establish Android/iOS GPU or thermal performance.

## Evidence and how to review

`WeaponMotionTests` tests C1 contact velocity, four distinct profile sockets, both facings, canonical all-direction release, bow recoil, free-arm/chest motion, turn/aim wrap, velocity steps, equip/teleport continuity, extension elbow limits, quality-independent joints/sockets and calibrated zero warmed math allocations. Existing `GameplayCharacterTests` remain the foot/contact/identity baseline. These numerical tests prevent specific errors; they do not prove naturalness.

`WeaponCharacterGraphicsTests` renders the actual shared presenter through both production batch tiers: eight actors, all four weapons, both facings, hold/windup/contact/recovery poses, visible-pixel and shader-error checks, FK grips, and a separately calibrated synchronous warmed draw probe. Files go to `Artifacts/Screenshots/Weapons`. Those fixtures isolate silhouettes and grips; the Bw/Sv gameplay tests separately exercise real equipment, attacks and projectile releases. Unity graphics execution and actual-game video review remain required after integration.

The previous brawler and horde gameplay videos were inspected before this change. Their acquired cadence was around 10 Hz with recorded timestamps; they support the rigid-guard/forearm pose diagnosis, not conclusions about runtime frame pacing. New recordings must retain actual acquisition timestamps and encode after buffering. A fixed-step pose replay must be labeled separately from live game acquisition, and neither represents mobile-device performance.

## Industry sources and adaptation

- [Unity Two Bone IK](https://docs.unity.cn/Packages/com.unity.animation.rigging@1.3/manual/constraints/TwoBoneIKConstraint.html) documents explicit target/hint control, position/rotation weights and maintained offsets. Here the same target/bend/weight concepts are bounded value types and analytic 2D math.
- [Unity animation constraints sample](https://github.com/Unity-Technologies/Unity.Animation.Samples/blob/master/UnityAnimationHDRPExamples/Assets/Scenes/Advanced/Constraints/README.md) orders FK before the arm constraints and head aim. This implementation similarly composes body motion before hand correction.
- [Epic Aim Offset](https://dev.epicgames.com/documentation/unreal-engine/aim-offset-in-unreal-engine?lang=en-US) layers aim over an existing base pose and cautions that extreme aim poses can conflict with locomotion. The shared presenter therefore preserves a locomotion base and applies only requested upper-body control.

These are design references. The repository does not claim to implement Unity Animation Rigging, Unreal Lyra, Motion Matching, arbitrary rig retargeting or a commercial authored animation library.
