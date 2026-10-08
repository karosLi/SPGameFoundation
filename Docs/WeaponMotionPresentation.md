# Continuous gameplay motion and held weapons

The [blade/sword choreography correction](BladeSwordChoreographyValidation.md) records why delivered `e0233871` motion was rejected, the unchanged-source red controls, and the bounded replacement. Numerical checks do not establish naturalness; new native 1× acceptance remains pending.

The [Horde armed-hero health-bar clearance](HordeWeaponHealthClearance.md) moves only that hero's bar below the feet to clear raised weapons. The retained overlap evidence, focused checks and pending native pixel gate are separate from motion acceptance.

The 2026-10-08 [humanoid arm and walking-cadence correction](HumanoidActionCadenceValidation.md) records the final-IK diagnosis, phase-authored lift and recovery, fixed-speed cadence measurements and new regressions. Its exact-source native video review remains pending; earlier checkpoints below do not validate that change.

The actual shared `GameplayCharacterPresenter` now consumes the authoritative `SPF.Contracts.Weapons.WeaponViewState` supplied by Brawler and Survivor. The four original outlined silhouettes are saber/刀 (slash, free supporting arm), sword/剑 (two-hand thrust), staff/法杖 (two grips and a cast socket), and bow/弓箭 (bow hand, string hand, live string and arrow release). These are presentation of the shared equipment/action runtime, not an independent decorative inventory or damage system.

## What changed

The old gameplay composition always aimed the near arm and then solved it again during contact correction; this erased its run swing. The far forearm retained its bind angle. The replacement keeps a relaxed two-arm gait until an aim/action layer requests control. Actual near/far foot phases drive weight transfer, pelvis support, chest opposition and elbow swing. Smoothed travel velocity adds distinct forward, backpedal and ground-depth lean; the head counter-rotates. Breathing is only a small relaxed layer. A bounded turn value moves shoulders through the turn and narrows the chest silhouette. Stable identity, explicit teleport reset, reachable world-space foot plants and bounded foot-cycle durations remain in place.

Body, foot, hand and weapon composition runs every render step. Quality-limited base/bind refreshes are still counted by `BasePoseRefreshes` (`PosesEvaluated` is a compatibility alias); that counter does **not** measure every continuous body/IK evaluation. Reduced quality cannot hold a bowstring or hand pose at 15 Hz while the actor moves. It also cannot change action phase or simulation.

Weapon trajectories use a narrowly scoped cubic Hermite sampler with shared tangents. The contact key is traversed with nonzero velocity rather than eased to a stop. `ContactPhase`, `ActiveEndPhase` and `ReleasePhase` come from the profile's fixed-tick timeline. There is no low-pass filter on action phase. The runtime adapter interpolates the authoritative previous/current action tick for rendering; hit windows remain integer simulation ticks.

The dominant hand is solved to the weapon target; the support hand follows the solved dominant hand. IK blends the *target position*, retains elbow bend direction and reserves 14 mm of extension to avoid a singular straight elbow. The bow string's draw point is distinct from its grip, stays taut through release, then recoils continuously. A small downward aiming crouch keeps directional release sockets reachable. Equip lowers/releases the old grip, then uses an outgoing-position/velocity Hermite transition for the new grip and angular velocity. This is a bounded hand-space transition, not full-body inertialization.

## Sockets, drawing and bounds

`TryReadWeapon(handle, out WeaponAttachmentSample)` is valid after `Evaluate`. It returns the actual FK dominant/support grips and weapon tip/muzzle/direction, visual ID, action pulse and sequenced authoritative cue flags. Particles consume these samples; damage must never read them. Collision and projectile origins remain the runtime's canonical profile sockets. Tests verify canonical release alignment for all sixteen aim directions, plus neutral contact at both facings and 30/60/120 Hz.

Weapon-enabled modes opt in at `GameplayCharacterPresenter(..., includeWeapons:true)`. Their art is created once with the original supersampled character atlas. The default unarmed presenter retains its existing 1 MiB atlas and 14-part stream capacity; it explicitly rejects a weapon-bearing input. The weapon-enabled atlas is 2 MiB. A new content ID can reuse an existing visual ID. Unregistered visual IDs explicitly fall back to their action family's authored silhouette and matching pivot/size. Explicit pixel pivots align the grip and tip/orb; bowstring segments are live batched geometry. An equipped actor uses 19 packed sprites instead of 14. The weapon is inserted within that actor's stream before the near arm/hand, and the support hand is drawn over its grip. Every part keeps the actor's ground depth. There is no global weapon overlay that can draw a back actor's weapon in front of a nearer actor.

The stream remains one fixed-capacity batch. The maximum adds five 32-byte instances per equipped actor; an unequipped actor still submits fourteen. Native socket output and sprite offsets are allocated once per capacity. There are no actor GameObjects, Animators, growing hot-path arrays or per-frame texture generation. Atlas memory is exposed by `ColorAtlasBytes`; the weapon opt-in doubles height from 256 to 512 at width 1024. This desktop implementation does not establish Android/iOS GPU or thermal performance.

## Evidence and how to review

`WeaponMotionTests` tests C1 contact velocity, four distinct profile sockets, both facings, canonical all-direction release, bow recoil, free-arm/chest motion, turn/aim wrap, velocity steps, equip/teleport continuity, extension elbow limits, quality-independent joints/sockets and calibrated zero warmed math allocations. Existing `GameplayCharacterTests` remain the foot/contact/identity baseline. These numerical tests prevent specific errors; they do not prove naturalness.

`WeaponCharacterGraphicsTests` renders the actual shared presenter through both production batch tiers: eight actors, all four weapons, both facings, hold/windup/contact/recovery poses, visible-pixel and shader-error checks, FK grips, and a separately calibrated synchronous warmed draw probe. Files go to `Artifacts/Screenshots/Weapons`. Those fixtures isolate silhouettes and grips; the Bw/Sv gameplay tests separately exercise real equipment, attacks and projectile releases. Each integrated revision requires the central Unity run and review of actual-game video; passing the pose fixture alone is insufficient.

The previous brawler and horde gameplay videos were inspected before this change. Their acquired cadence was around 10 Hz with recorded timestamps; they support the rigid-guard/forearm pose diagnosis, not conclusions about runtime frame pacing. New recordings must retain actual acquisition timestamps and encode after buffering. A fixed-step pose replay must be labeled separately from live game acquisition, and neither represents mobile-device performance.

## Industry sources and adaptation

- [Unity Two Bone IK](https://docs.unity.cn/Packages/com.unity.animation.rigging@1.3/manual/constraints/TwoBoneIKConstraint.html) documents explicit target/hint control, position/rotation weights and maintained offsets. Here the same target/bend/weight concepts are bounded value types and analytic 2D math.
- [Unity animation constraints sample](https://github.com/Unity-Technologies/Unity.Animation.Samples/blob/master/UnityAnimationHDRPExamples/Assets/Scenes/Advanced/Constraints/README.md) orders FK before the arm constraints and head aim. This implementation similarly composes body motion before hand correction.
- [Epic Aim Offset](https://dev.epicgames.com/documentation/unreal-engine/aim-offset-in-unreal-engine?lang=en-US) layers aim over an existing base pose and cautions that extreme aim poses can conflict with locomotion. The shared presenter therefore preserves a locomotion base and applies only requested upper-body control.

These are design references. The repository does not claim to implement Unity Animation Rigging, Unreal Lyra, Motion Matching, arbitrary rig retargeting or a commercial authored animation library.

## Role, locomotion and skill layers

`GameplayLocomotionClassifier` is a public value-only API usable by both articulated and sprite backends. `Step(speed, runEnter, runExit, grounded, enabled, idleEnter, idleExit)` returns Idle, Walk, Run or Air and retains `RunLatched`. The caller supplies consistent speed units. It has no bone, animation clock or gameplay dependency. Speed hysteresis chooses locomotion independently of upper-body attack state; an attack no longer removes the moving gait. Walk and run blend different stride/cycle bounds, foot lift, pelvis bob and arm arcs without resetting either foot's contact phase.

The default role profiles are hero (0), agile monster (1), and heavy monster (2). They vary thresholds, stride, support width, breathing, arm/chest opposition and hit recoil. They share the same rig, instance stream and identity slots. The heavy profile uses a longer minimum cycle, not faster legs to conceal reach errors. `MotionProfile` is an optional validated POD override; new role content does not require another actor enum.

The skill layer consumes `SkillPoseId`, `SkillPhase`, `SkillWeight` and `SkillPulse`. Its time comes from the game adapter. The built-in pose IDs are content examples:

| ID | Pose | Distinct contribution | Required authority |
|---|---|---|---|
| 100 | Kick | Raised guard, backward chest counterweight, released support hand | Existing kick phase and foot probe |
| 101 | Pulse | Chest gather, free hand raised outward, lowered carried weapon | Actual pulse activation/phase |
| 102 | Blink/dodge | Low compact body, tucked hands/weapon | Actual blink/dodge activation; teleport resets roots |
| 103 | Jump | Air tuck, opening for landing, balancing free arm | Authoritative height/velocity and jump phase |
| 104 | Knockdown | Whole-body fall/recovery layer | A game that actually reports knockdown |
| 105 | Heal | Hand to chest, raised free hand and bowed head | Actual heal activation/phase |

Idle/walk/run are lower-body states; aim/weapon/skill is an upper-body layer; hit/death are existing actor state responses. Unsupported states are not invented by the presenter. Games may provide a validated `SkillProfile` POD with a new content ID and its own body keys, hand anchors, support-hand release, weapon carry, foot tuck and fall contributions. Invalid overrides cause `Submit` to return false before batch work. Direct math calls defensively use the default role or empty skill. Profile changes and interrupted skills blend from the outgoing pose, while teleport/recycled identity resets them.

A skill can release the support hand while the dominant hand keeps carrying the weapon. The bowstring then returns toward its neutral nock instead of following a free gesturing hand. At an authoritative weapon contact/release, the weapon layer has priority over conflicting skill hand, chest, pelvis and carry-angle offsets, keeping projectile/collision alignment. Hit/death and unsupported combinations remain the adapter's responsibility; presentation never creates a second gameplay state machine.

`HitWeight` provides bounded additive recoil for games such as Survivor where taking damage intentionally does not cancel autoattack. Its default is zero; existing `State.Hit` callers retain their full response. Non-finite submitted values are rejected. Brawler keeps its authoritative hit interruption. Belt locomotion reads final ground displacement per fixed tick, including arena clamps and crowd separation, so pressing into a boundary cannot keep an otherwise stationary actor running. Survivor death instances retain the effective live radius and monster role, including elite scale.

Additional tests cover classifier hysteresis, idle→walk→run under moving attacks at 30/60/120 Hz, role distinctions, custom authored profiles, interrupt/teleport transitions, invalid-data rejection, zero warmed allocations, and all six skill peaks combined with all four weapons in eight aim directions while moving. Dominant and secondary contact errors remain below 3 mm in that matrix. The secondary shoulder correction also handles the nonzero inner reach radius of the unequal arm segments.

Local checkpoint: 46 focused .NET tests pass, zero skipped; compilation has zero warnings/errors. This is math, layering, contacts and harness allocation evidence. The updated actual-game role/skill captures and real Unity/Burst graphics verification are separate integration gates.


## Mixed-stream accounting regression

Weapon capacity has always been separate from per-frame output in this implementation. The existing sorted CPU pass computes one bounded output offset per input. The Burst job writes 14 body records for an unarmed actor and 19 for an equipped actor, preserving ground-depth ordering and hand/weapon layering. `PackedPayloadBytes` exposes useful records only; `ReadPart` is read-only diagnostic access to the sorted output.

For 192 unarmed actors plus one equipped actor:

- Compact output: 2,707 records, **86,624 packed bytes**.
- A hypothetical 19 records for all 193 actors: 3,667 records, 117,344 packed bytes.
- Difference: 960 records / **30,720 packed bytes**, already avoided by the compact stream.
- GpuDriven submits the exact compact instance count and uploads those records plus its indirect arguments. This describes API payload, not measured physical GPU traffic.
- DataTexture still selects the same prewarmed final prefix in this 193-actor example: a 2048 × 15 RGBA8 data texture, **122,880 upload bytes**. Its prefix mesh can still submit the zero-sized tail quads. Compact packing alone therefore does not establish a DataTexture upload or vertex-submission reduction in this case.

Three focused .NET stream tests pass with zero skips: mixed equipped/unarmed actor ordering, weapon removal and recycled-generation socket ownership; the exact 193-actor payload and maximum-capacity transitions with calibrated zero hot-path allocations; and the base-pose counter versus continuously corrected contacts. A separate Unity-only regression checks actual DataTexture upload accounting after the count shrinks; it remains pending the central graphics run. Numerical poses and rendering order were unchanged by this accounting/diagnostic follow-up.
