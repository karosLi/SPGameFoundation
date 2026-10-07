# Mobile action, projectile and feedback coordination audit

Audit started 2026-10-07 from `c345f2a4716b98e133a11acc636c1b32e1d4d80f`. This is a mobile-targeted acceptance record, not a statement that every combination is implemented or visually approved. The previous fully validated checkpoint is [52c1ec8](WeaponMotionStage5Validation.md). Results from that checkpoint do not validate subsequent source changes.

## 1. Research and decisions before extending the implementation

Primary references were retrieved on 2026-10-07:

| Reference | Relevant practice | Bounded adaptation here |
|---|---|---|
| [Unity 2022.3 particle main module](https://docs.unity3d.com/2022.3/Documentation/Manual/PartSysMainModule.html) | Explicit local/world simulation space and scaled versus unscaled time | Charge is socket-local; release, contact sparks and trails are detached world-space records. Pause supplies zero cosmetic delta. No per-actor ParticleSystem is introduced. |
| [Epic animation notifies](https://dev.epicgames.com/documentation/en-us/unreal-engine/animation-notifies-in-unreal-engine) | One-shot events versus state windows; attached effects can follow a socket while detached effects retain the event location | Keep the existing fixed-tick cue ring and action phase. Scope attachments to full owner identity and action pulse; consume old events without resetting a newer action. Do not make damage depend on an animation notify or rendered socket. |
| [Epic blend nodes](https://dev.epicgames.com/documentation/en-us/unreal-engine/animation-blueprint-blend-nodes-in-unreal-engine) | Additive poses and per-bone layers compose with a base pose | Retain the shared locomotion base, role parameters, upper-body skill/weapon weighting and final analytic IK. No full animation-graph framework or motion-matching database is added. |
| [Unity 2022.3 particle GPU instancing](https://docs.unity3d.com/2022.3/Documentation/Manual/PartSysInstancing.html) | Structured instance data has shader/backend requirements | Preserve explicit capability-gated compute plus independent draw fallback. A desktop compute pass is not proof of a mobile speedup. |
| [Unity Rendering Profiler](https://docs.unity3d.com/2022.3/Documentation/Manual/ProfilerRendering.html) | Batches, SetPass, vertices, triangles and resource cost need measurement | Reuse existing atlas/batch for projectile art; keep one quad per shot and report padding/allocation rather than claiming source pixels equal GPU allocation. Profile the complete game on the target device. |

Decision: repair confirmed action-ownership errors and improve the existing ranged silhouettes. Do not add gravity, homing, embedding, piercing, obstacles, ragdolls, weapon-bearing monsters, a new hit system or a second animation authority under the label of visual polish. Those require their own gameplay contract, content and tests.

## 2. Role × state × weapon × skill coverage

Legend: **implemented** means present in the source; **logic/math verified** means targeted automated behavior; **native historical** refers only to the exact earlier checkpoint; **visual pending** means new exact-head continuous gameplay capture/review is still required. Synthetic pose combinations are not proof that a game exposes those actions.

| State/transition | Belt hero | Belt agile/heavy monsters | Horde hero | Horde agile/heavy monsters | Evidence and outstanding gate |
|---|---|---|---|---|---|
| Idle, start, walk/run, stop, reverse, turn, lateral and ground-depth travel | Shared classifier from final ground displacement; normal and moving attacks | Shared classifier, distinct agile/heavy profile | Same base under automatic attacks | Same base from actual enemy displacement and effective radius | Existing `GameplayCharacterTests`, `WeaponMotionTests`, `GameplayMotionProfileTests`; separate grounded-gait correction and new live mobile-shaped footage required. Old weapon captures freeze enemy speeds. |
| Jump → ascent → fall → land | Authoritative belt height/vertical speed, jump skill 103 | Actual kick launch/height can produce air state; no invented enemy jump command | No jump/fall gameplay | No jump/fall gameplay | Belt runtime and PlayMode tests cover height, gravity and landing. Shared air tuck follows root minus ground; exact-head full-body clips pending. |
| Hit → recover | Hit interrupts equipped action; kick also cancels primary weapon action | Existing hit/stagger clears attack state/buffer | Damage deliberately keeps autoattack; additive `HitWeight` must not move release socket | Flash/hit recoil while existing movement continues | `BwWeaponTests`, `SvWeaponTests`, profile/socket regressions. Sustained repeated-hit footage for every role remains pending. |
| Death/KO → restart | Runtime cancels input/pending equip/projectiles; presenter falls with held silhouette | Existing KO and fall | Runtime death cancels weapon; hero death pose | At most 16 presentation-only fall/fade identities retain effective radius/role | Stable generation, cleanup and restart tests exist. Those death IDs do not reconstruct the original entity's full animation history. No ragdoll or arbitrary resurrection blend is claimed. |
| Equip → hold; switch during windup/commit/recovery | All four profiles | No held-weapon gameplay | All four profiles | No held-weapon gameplay | Shared runtime cancel windows and 12-switch adapter tests. New retained-cue regressions ensure an old stop cannot invalidate the new action's charge. |
| Charge → contact/release → follow-through → recovery | Blade/slash, sword/thrust, staff/cast, bow/draw | Existing unarmed jab/cross/kick only | Four equipped families | Horde has no authored enemy attack timeline in this adapter | Canonical neutral/all-direction sockets, C1 contact trajectory, two-hand/string grip and release tests. A monster walking into contact is not a weapon attack animation. |
| Continuous held/automatic attack → next pulse | Fixed-tick held primary repeats | Existing AI commits/recovery timing | Automatic in-range or held primary repeats | Existing simulation only | Every pulse captures aim anew. No new combo graph: equipped primary repeats its profile, whereas the unarmed belt combo remains jab→cross→kick. Trails never connect two action pulses. |
| Skill overlap / cancel / pause / rebind | Kick 100, jump 103, heal 105 from real skill clock | Existing kick pose uses authoritative fighter phase | Pulse 101, blink 102; blink is a teleport | No hero skill or equipment layer | `ActionPoseClock`, adapter tests and weapon/skill contact matrix. Shared knockdown 104 is authored pose data, not a newly implemented gameplay state. All six shared pose IDs can be tested mathematically without claiming all six in both games. |

The reusable role/skill/weapon/aim matrix includes role IDs 0/1/2, skill IDs 100–105, the four default profiles, eight aim directions, 30/60/120 Hz presentation steps and moving roots: 576 combinations at each rate, 60,480 sampled poses total. Hero cases include additive hit recoil. The three rate cases pass, checking finite bone output and the existing <3 mm muzzle/support constraints; maximum observed errors were 3.999e-7 and 6.035e-7 world units. It deliberately does **not** assert that an unsupported monster weapon/hero skill combination exists in gameplay, or that numerical contacts establish natural motion. Grounded locomotion is reviewed separately in `Docs/GroundedLocomotionValidation.md` when that correction is integrated.

### Required transition review sequence

For each supported role and weapon: idle → low-speed walk → run → stop → reverse → lateral/depth turn; then standing and moving windup → contact/release → follow-through → recovery → next held pulse. Interrupt before commitment, request equip during commitment, then interrupt recovery. Add available jump/landing, hit, death/restart, pause/resume, same-tick restore, renderer disable/rebind and lower-quality transitions. Review the torso, both shoulders/elbows/hands, grip pivot, weapon tip/muzzle, support release and bow string together, at normal 1× playback. A state that the host never reports is recorded as unsupported, not silently substituted by a decorative animation.

## 3. Confirmed particle-transition defects and repair

Local commit `69487dc3711c5297c7bd3e6221a2f4dd62b15ec1` fixes three related ownership failures without changing simulation:

1. The renderer publishes the current socket before consuming a retained cue backlog. An older Cancel or Equip previously cleared that newer action's attached charge. Stop cues now invalidate only the matching content/action, while their sequence is still consumed.
2. A render interval can skip the old release/recovery and observe a new charge directly. Attachments previously retained the old token because equipment was unchanged. A changed action pulse now retires the previous attachment/token and blade sample.
3. A retained Release previously borrowed the current muzzle and family. A new action, changed weapon or later recovery could move/recolor the old burst. Detached release now uses the projected event position; the adapter resolves the cue's original content family from the existing immutable catalog.

Four reproduction cases failed on `c345f2a` before the repair (prior Cancel, prior Equip, charge-to-charge, old release origin). Seven new cases additionally cover current-stop cleanup, retained family after switching, zero-delta pause and deduplication. An older synthetic charge fixture was corrected to supply its actual action pulse/content in the stop cue, matching the production contract; its cleanup assertion was retained.

At `69487dc`, 86 focused .NET cases passed with zero skips: weapon runtime, weapon motion, motion profiles and particles. This is not native compute or frame-rendering evidence. Existing owner-generation wrap, sequence wrap, missing-owner, pool priority/overflow, scatter uniqueness, camera zoom and fallback tests stay in place.

## 4. Ranged presentation and collision contract

### Examples and visual decisions

| Example | Implemented simulation | Visual and feedback contract | Explicit exclusions |
|---|---|---|---|
| Shooter bullet | Existing fixed-tick straight bullet motion and relative circle sweep | Existing Shooter bullet/impact art remains unchanged; shared new catalog includes an optional compact bullet specimen for future consumers | No new weapon-fire family or behavior is added merely by adding a mask |
| Equipped bow arrow | Fixed-tick straight motion, one release marker, first accepted collision/despawn | Original narrow shaft, distinct head and fletching replace the unfeatured bar; one existing-batch quad aligned with projected direction; bounded world-space arrow trail | No gravity, homing, wall embedding, piercing or physical rigidbody |
| Equipped staff spell | Fixed-tick straight motion, one release marker, first collision/despawn | Compact blue shell and bright core replace the solid blue square; inward attached charge → detached release → confirmed impact/core/embers | No broad multi-layer glow, light, distortion, area damage or target seeking |

Local commit `8508eebe9fc10a14c89ffb82f8fee0a662917443` adds `WeaponProjectileArt`: three small original canvases at load time. Only modes with `BwWeapons.Key` / `SvWeapons.Key` append the catalog to their existing atlas; ordinary art factories default to disabled. Its initial arrow quad was centred on the authoritative projectile point; the follow-up correction below anchors the authored tip to the projected collision boundary. Spell cores stay centred. Collision remains the configured circle at the simulation centre, not a pixel-perfect mesh. View interpolation and art width cannot create or extend a hit. Source-only previews: [bullet specimen](preview/projectiles/projectile-0.png), [arrow](preview/projectiles/projectile-1.png), [spell](preview/projectiles/projectile-2.png).

The new art needs its own exact-head GPU/DataTexture screenshots and continuous release/contact review. Source-canvas alpha/shape tests prove the mask is no longer a rectangle; they do not prove small-screen readability after blending, camera scaling and compression.

### Movement bodies, hurt volumes and attack queries

| Layer | Belt | Horde / Shooter | Authority and filtering |
|---|---|---|---|
| Movement body | Ground X/depth bounds and local separation; independent height | Existing enemy spacing/arena or flight bounds | Motion/separation volume is not automatically the damage volume or visible character outline |
| Hurt volume | Ground footprint plus a separate hurt-height interval | Horde enemy radius; Shooter's own radius data | Uses simulation geometry, not rendered bone/sprite bounds |
| Melee attack | Canonical grip-to-tip segment inflated by profile radius, sampled once on `ContactWindow` | Horde same one-contact ground-space model | Stable complete entity handles deduplicate contact. Targets entering during later visual follow-through do not receive retroactive damage |
| Ranged query | Projectile previous/current centre and target previous/current ground position; interpolated target height at TOI | Horde and Shooter relative-motion circle sweeps | Earliest TOI wins. Current equipped Bw/Sv tie policy compares stable handle index; current live generation is retained by history. Projectile impact is not a limb-collider query |
| Damage permission | Opposing team, non-KO target; current classic/belt rule semantics preserved | Dead targets excluded; queue admission before horde hit-history record; hero invulnerability follows its existing resolver | No new blanket invulnerability/occluder policy is inferred from visual flash. Do not silently change classic filters |
| Hit identity / lifetime | Owner handle + action pulse; separate projectile-slot scope and saved spawn tick | Same shared weapon contract; Shooter has its own stable spawn IDs | One-contact melee and no-pierce equipped shots; full projectile pool rejects newest release once. A rejected horde damage insertion does not record a successful hit; encountered shot is still consumed deterministically |

Equipped Bw/Sv shots use 32 default projectile slots and per-scope hit histories. They expire only after the final segment has been queried. Actual impact cues are created by accepted authoritative hit handling; swinging through empty space can produce a trail but never a confirmed-hit flash or damage. There is no obstacle/wall occluder query in this shared weapon slice. The separate collision-policy/overlay work must document supported shapes, exact boundary/tie semantics and snapshot compatibility before adding any such behavior.

## 5. Mobile budgets and lifecycle

- Existing effect limits stay 1024/256 particles, 32 emitter sockets, 128 cue-owner cursors, 64 spawn/retirement commands per frame. Admission reserves 16 commands, 64 particles and 25% area for important cues. The 12%/7% conservative summed quad-area limits are not measured overdraw.
- New projectile source canvases contain 4096 RGBA pixels total (16 KiB before atlas gutters/padding). Atlas packing may increase a power-of-two allocation; report actual host atlas dimensions. It is not correct to claim exactly 16 KiB GPU growth. The readable atlas CPU copy, generated canvases, native buffers and driver memory are separate.
- Measured atlas dimensions in source tests: weapon Brawler smooth stays 512×128 RGBA32 (256 KiB), pixel opt-in grows 256×32→256×64 (32→64 KiB), both four-enemy Survivor samples keep their previous allocations (pixel 1024×128=512 KiB; smooth 1024×256=1 MiB). The Brawler opt-in shelf width is explicitly bounded; an initial unrestricted pack doubled the smooth atlas width, so that unnecessary padding growth was removed before delivery. Default factory dimensions and UVs remain identical with the opt-in disabled. Configurations with different enemy catalogs must measure their own packing.
- Each live projectile retains one 32-byte packed sprite record. At the default 32-slot bound this is at most 1024 useful packed bytes; DataTexture prefix padding and all other effects are additional. Arrow quads are taller for head/fletching visibility, increasing potential fill area relative to the old bar even though transparent corners remain clear. No extra draw/material group, per-shot GameObject or per-frame texture work is added.
- Spawn, retirement, cue owners, visual RNG and charge state stay bounded. Changing render quality cannot change damage, cooldown, action pulse, collision, simulation RNG or saved bytes. All GPU resources remain presenter-owned and disposable; no shared atlas is destroyed by an individual effect.
- Device acceptance must measure Android/iOS CPU/GPU frame p50/p95/worst, native/managed memory, atlas/upload/draw cost, overdraw under dense impacts, sustained heat and battery, graphics fallback, pause/background and touch/safe-area behavior. No desktop test supplies those measurements.

## 6. Evidence provenance and current limits

Historical continuous source was `0b0dd6d`, recovered by the bounded evidence flow under `Artifacts/Screenshots/WeaponMotion/`. Each of the four clips contains 90 real acquired frames; the encoded file contains 91 frames because the final source image is repeated once to preserve its hold. No motion interpolation was synthesized. Acquisition is affected by synchronous readback and is not a runtime frame-pacing measurement.

| Clip suffix (`weapon-…-0b0dd6d-verified.mp4`) | Source duration | Mean acquisition | SHA-256 |
|---|---:|---:|---|
| belt-live-gpu | 4.236357375 s | 21.0086 Hz | `847de5758ac3ef52bb2b7016ce727d5dde6587ae96d75f0caa25574fd20c8afc` |
| belt-live-fallback | 4.185560125 s | 21.2636 Hz | `447b20d3b16618039b9ad6ff4dc968e21152b1a13e0fb5257fd572f1dba3e1ba` |
| horde-live-gpu | 4.238231708 s | 20.9993 Hz | `d26c1312e9e755bc08bc93aa0d43831a0f32b56ead36d2964cf1f768bf538e07` |
| horde-live-fallback | 4.038145125 s | 22.0398 Hz | `6aeb701fa6e462610ee7ec627b2cff2d24f8b60fdb8fe1b9aa53e693624db816` |

This audit inspected the existing contact sheets and individual source pixels at belt GPU frame 37, belt fallback frame 55, horde GPU frame 27 and horde fallback frame 68. They show old blade/sword grip/contact silhouettes and staff/bow travel, and substantiate the old solid-square/bar ranged art. They do not show this repair or new art. Frozen enemy speeds in that recording are an explicit limitation; no monster locomotion approval is derived from it. Full normal-speed new clips, dense hit readability and all supported state transitions remain an integration gate.

The earlier exact native checkpoint passed 878 EditMode / 141 PlayMode and the .NET harness passed 848 tests; its skips and source tree are listed in [WeaponMotionStage5Validation](WeaponMotionStage5Validation.md). Seven earlier real particle graphics cases independently validated production compute against CPU on desktop Metal. Neither historical result proves the current edits or physical mobile performance.

Related implementation contracts: [held motion](WeaponMotionPresentation.md), [authoritative weapons](AuthoritativeWeapons.md), [bounded particles](BoundedWeaponParticles.md), [natural characters](GameplayNaturalCharacters.md), [belt](LandscapeBeltScroller.md), [Shooter](ShooterValidation.md).

### Reproducing the current checks

- `WeaponParticleTransitionTests`: 7 cases, including the four red-before/green-after regressions above.
- `WeaponProjectileArtTests`: 4 cases for padded alpha, arrow head/shaft/fletching, non-rectangular masks and three-record catalog. Optional `SPF_PROJECTILE_ART_PREVIEW` exports raw RGBA source canvases for inspection; these are not gameplay screenshots. All three source masks were visually inspected.
- `BwProjectileArtTests` / `SvProjectileArtTests`: 4 cases for explicit opt-in, unchanged default UVs and actual atlas dimensions. Affected weapon adapters plus art tests pass 17 Brawler and 13 Survivor cases, zero skips.
- `MobileActionCoordinationTests`: 3 rate cases for the 1,728 contact combinations above; no thresholds relaxed.
- `WeaponProjectileGraphicsTests`: two real-backend silhouette fixtures save `Artifacts/Screenshots/Weapons/projectile-catalog-{gpu,fallback}.png`; implemented, **native execution pending**.
- `WeaponParticleBacklogGraphicsTests`: two actual GPU/CPU state-readback cases for skipped charge transition, stale stop, retained release origin and cleanup; implemented, **native execution pending**. A no-graphics or unsupported-compute skip remains unverified.

At source `f17758e8ced0bf64eb8fce2c1b1a174ba8319615`, the final local harness compiled all 76 assembly projects with zero warnings/errors and passed **867 tests, zero failed/skipped, across 11 EditMode assemblies**. The harness excludes native PlayMode execution; an empty PlayMode assembly is not a graphics pass. The actual Unity 2022.3.62f2 API compile of changed code, source tests and both new graphics fixture classes passed with zero errors (four pre-existing serialized-field warnings). [Machine-readable source, suite totals and hashes](validation/MobileActionCoordination-20261007.json).

A separate integration worktree combines this audit with the grounded-gait correction `b504c05` at local `1c33fde`. Its test launch failed before .NET startup twice due to a synthetic sandbox mount setup error, so the combined revision is **not verified by this worker**. The coordinator must rerun the contact/weapon/particle/grounded suites on the final integrated head. Final native exact-head status also remains separate; neither a successful API compile nor a .NET stub run executes Burst, shaders or a graphics device.

## 7. Render-only arrow tip alignment follow-up

The collision review found that centring a long arrow quad on the projectile centre makes its head visually reach a target before collision. In the 64×32 authored mask, the apex is (60,16). For the belt's .66-unit-wide quad, that is .28875 world units forward of the quad centre, whereas the default arrow collision radius at scale .9 is .09 ground units. This is a visual anchor defect; increasing damage radius would hide it by changing gameplay.

Research reference: [Unity 2022.3 Sprite.Create](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Sprite.Create.html) distinguishes a normalized graphic-rectangle pivot from its texture rectangle and optional generated physics shape. The adaptation here uses one constant authored pivot in the existing packed quad; it creates no SpriteRenderer, Unity collider or new object.

`WeaponProjectileArt.ArrowTipU` is 60/64. `ArrowCentre` positions the quad so that this authored point coincides with a caller-supplied projected forward collision boundary:

- Belt: `BwBeltRules.Project(interpolatedGround + shot.Direction * profile.Radius * shot.Scale, shot.Height)`. Normalizing the projected direction is used only to orient/offset the art; it must not be used to turn a ground-space radius into a screen-space radius. Pure depth travel therefore correctly applies the 0.55 projection factor.
- Horde: `interpolatedGround + (0, shot.Height) + shot.Direction * profile.Radius * shot.Scale`.
- Spell: keep the original centre and compact core. No radius, speed, hit filter, damage, release timing, pool capacity, snapshot or trail-authority change.

Four production-batch regressions (two games × two draw tiers) failed on the previous code. Their first sampled errors at scale .4 were .2488184 belt / .1787500 horde world units. The corrected regressions directly invoke each renderer's existing projectile draw method, then independently reconstruct the authored tip from its packed centre, half-precision width/rotation and the source pixel coordinate. They cover 16 headings, three scales, three interpolation fractions and both arrow/spell profiles; every presentation call must preserve byte-identical weapon snapshots and one packed record per shot. The allowed .0005-unit packed-coordinate error accounts for the existing half-precision angle/size encoding, not a wider collision radius.

The alpha-mask regression also verifies that the last covered source-pixel column ends at the authored pivot, so a future art edit cannot silently detach the metadata from its silhouette. Two additional real graphics cases inspect opposing arrows through GPU/DataTexture rendering and require the leading alpha coverage to end within two pixels of the boundary; their screenshots are named `arrow-tip-pivot-{gpu,fallback}.png`. Those native pixel cases and normal-speed in-game release/contact/overlay review remain **pending** until actual execution and inspection.

No texture, mask, atlas allocation, quad size, material, batch capacity or particle budget changes in this correction. Only the arrow quad's centre moves backward. The collision-policy/overlay work is documented separately in `Docs/CollisionPoliciesAndDebugging.md` when integrated.

Follow-up verification in the preserved audit worktree (base `e7bd1e4`, before the coordinator's newer gait/collision integration): **871 .NET tests passed, zero failed/skipped**, and all 76 harness projects compiled with zero warnings/errors. Maximum packed tip errors were .0002876261 belt / .00021171573 horde world units, identical across the two tier submissions. The four batch tests covered 1,152 arrow/spell presentation samples and preserved snapshot bytes on every call. Actual Unity 2022.3 API compilation of the final source and new pixel tests passed with zero errors and four existing serialized-field warnings. [Source hashes and complete suite totals](validation/ArrowTipAlignment-20261007.json). These results do not replace an integrated-head rerun or the pending real GPU/DataTexture pixel and continuous gameplay review.

## 8. Integrated native evidence on 4af9b87

The original new projectile silhouette and particle-backlog graphics fixtures listed as pending in the source-worktree section have now executed successfully on **both tiers**, exact remote `4af9b87cc32fcd1a5f10a4bd2817848044ad4649`. Full graphics PlayMode is 148 passed / one explicit diagnostic skip; all four weapon/socket gameplay cases also pass. The separate arrow-pivot changes in section 7 came later and remain pending their own native tests.

All 29 artifact parts and 1,398 extracted files were hash-verified (archive SHA-256 `2a62d8ad4776279f77b9a170ab4a4725c1134909d00c0eb1e3305557b6b6232d`). Actual adjacent Belt frames 41–48 show contact, target flash and a hit burst appearing/expanding/fading. Staff footage contains the detached cyan projectile; subsequent bow footage shows draw/release, and the high-resolution left-facing bow contact PNG visibly aligns the hand/arrow. Maximum gameplay contact/release socket error is `2.38418579e-7`. Small 360×640 review frames limit fine silhouette judgment, so the dedicated pixel tests remain separate evidence.

Production particle compute/CPU state error is at most `1.907349e-6`; 2,295 occupied production-draw pixels have zero mismatches above 8/255. Both warmed GPU and CPU/Burst paths report 0 current-thread AllocationSamples over 64 submissions, retained/empty controls 32/0 before/after and process Gen0=0. These results exclude native/driver/other-thread allocations and physical mobile performance. The full native run still has two test-only dense separation precision failures. [Integrated scope, continuous-video review and remaining gates](MobileFoundationFollowupValidation.md).
