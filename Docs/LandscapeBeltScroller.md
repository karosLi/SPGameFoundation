# Landscape belt-scroller vertical slice

## Run the real game

Use **SPF → Mobile Gameplay → Create Landscape Belt Scroller Scene**, open the saved scene and press Play, then FIGHT. Programmatic entry: `BwGameBootstrap.CreateBeltScroller()`.

- Left stick / WASD / arrows: normalized movement on both ground axes.
- COMBO / J: hold to chain jab → cross → kick; a tap during recovery buffers the next basic attack.
- KICK / K: two separately recharging charges; knocks back and launches a target.
- JUMP / Space: independent vertical height, gravity and landing. Ground depth continues moving while airborne.
- HEAL / L: restores 24 HP, eight-second cooldown; disabled at full health or while unable to act.
- Defeated enemies leave coins; every second defeat also drops healing. Nearby loot attracts toward the player and is collected close to the ground.
- Three increasing waves, a short movement-only collection interval, victory/loss, and AGAIN reset the same session safely.

This is an opt-in landscape layout and gameplay schema. It does not change global PlayerSettings or force other games into landscape. `Create()`, `CreateSharedCombat()` and `CreateMobileCombat()` keep their previous rules, controls, capacities and save layouts. `CreateNaturalCombat()` selects the natural character presenter with the existing mobile brawler rules, without adding the belt schema.

## Bounded foundation reuse

The variant uses the existing `BwModule`, `SimSession`, fighter table/registry, `FighterInfo`, skeletal attack definitions, `ActionTimeline`, stable-handle `HitHistory`, `SkillSlots`, `MobileCombatHud`, `SpatialGrid` and common character presenter. It adds three table columns and one versioned, level-scoped resource; it is not another gameplay engine.

- `BwBeltKeys.Ground` / `PreviousGround`: authoritative `(x, groundDepth)`.
- `BwBeltKeys.Motion`: independent height/vertical velocity, visual ground velocity, combo grace and fixed-tick input buffer.
- `BwKeys.Position` / `Prev`: compatible projected position, `(ground.x, ground.y * 0.55 + height)`.
- Ground limits: X ±9, depth ±2.4. Jump gravity and landing are simulation-owned.
- Shared `GroundCombatQueries`: a probe against X/depth footprint and a separate height interval. Closed tangencies count; screen-space Y is never collision authority.
- The existing `BwRig` action timing and striking-bone tip supply melee probes. The natural art adapter consumes authoritative state and normalized phase; visual motion/cadence is not collision authority.
- Enemy pursuit uses stable lane offsets and local spatial separation. Separation is calculated from immutable ground entries into a preallocated output array, then applied together. Combat uses local cell queries and stable target handles, including generation, rather than row masks.
- Grid and query counters are derived diagnostics. The grid is rebuilt before use from authoritative columns, including immediately after restore. It does not need snapshot storage.
- Default bounds: 64 fighters, 64 targets per concurrent swing, 32 loot slots. Configured simulation maximum: 128 fighters/targets and 256 loot slots. Dense worst-case overlap can still visit many neighbors; this is a bounded belt arena, not a claim of unbounded horde scalability.
- Full fighter capacity rejects a spawn and increments `RejectedSpawns`. Full loot capacity rejects the newest drop without evicting an existing reward; `RejectedDrops` records it. Shared target history retains its reject-newest policy and `RejectedHits` counter. Grid capacity matches fighter capacity and the grid covers all clamped ground coordinates.
- Skill priority on simultaneous input is charged kick, buffered/held combo, jump, heal. Charges are consumed only when the corresponding action starts. Stagger/death clear the attack buffer. During the 2.2-second wave-clear collection interval the joystick/keyboard can still move; all four skills are disabled, action bits cannot carry into the next wave, and skill recharge is paused. Focus interruption releases the movement pointer and requires a new drag.
- The renderer consumes ground roots for depth sorting/shadows and height for body placement. FrameGovernor quality changes affect presentation cadence only and never remove collisions, change AI, skip skill ticks or alter loot.

## Save and restart boundaries

`Bw.BeltScroller.V1` writes a magic/version header, exact capacities and wave configuration, counters and fixed loot storage. Extra fighter columns and the four-slot skill definition give belt saves a distinct layout from classic and two-slot mobile saves. Restore rejects mismatched configuration instead of guessing a migration. No classic resource serializer or `FighterInfo` field was changed.

Restart clears level-scoped entities, shared hit scopes, buffered attacks, airborne state, coins, loot and cooldowns. Registry generations prevent old renderer/attack references from becoming the new player. Terminal states freeze gameplay until an explicit restart/menu command.

## Verification

The .NET harness passes **49/49 Brawler EditMode cases**, including **27 new belt cases** at the implementation checkpoint. Coverage includes depth hit/miss, height miss, exact tangency, normalized ground motion, independent jump/landing, combo/recovery buffer, pursuit/stagger, local separation, row-sort/recycled-generation hit identity, loot/healing, reject-newest overflow, 128-fighter capacity, overflow-safe wave configuration validation, movement-only collection/recharge isolation, snapshot continuation with airborne/buffer/loot state, same-input determinism, terminal/restart, schema isolation and classic/shared/mobile regressions.

A warmed 90-tick belt-logic allocation probe reported **0 current-thread managed bytes** in the standalone .NET harness with positive retained-array calibration. This excludes Unity engine/native allocations, renderer, upload cost and whole-frame collection. It is not mobile-device performance evidence.

`BwBeltScrollerPlayTests.LandscapeBeltDepthJumpLootRestartAndBudgetIsolation` is an actual game/canvas/camera test for both GPU-driven and data-texture backends. It exercises real skill raycasts, four-slot safe-area layout, depth miss/hit, hit-stagger, charged kick hit/launch, joystick+jump, ground shadow placement, healing/loot, loss/restart and snapshot invariance while changing presentation quality. It records unmodified 1280×720 readbacks in `Artifacts/Screenshots/MobileHud/belt-landscape-*.png`.

`BwBeltScrollerPlayTests.CollectionIntervalKeepsJoystickAndReachesDistantLootAfterPause` separately exercises the actual input sink/HUD across wave clear, focus interruption/resume and victory. Its pickup starts over four ground units from the player, outside attraction range; the joystick must move to collect it. The tests stage enemy HP/KO and final-wave state to isolate mechanics and collection-to-terminal UI; they are not evidence of a naturally played complete multiwave victory.

Real Unity execution and visual review are centralized separately; the presence or harness compilation of these PlayMode assertions does not mean they have run. Android/iOS touch, safe area, thermal behavior and frame times still need target-device validation. The included art is original outlined placeholder character/environment art, not copied artwork from the supplied references.

## Dense logic evidence

See [BeltLogicWorkload](BeltLogicWorkload.md) for the six 32/64/128-fighter spread/clustered complete-tick reports, deterministic replay, calibrated allocation and native-run instructions. The final Brawler harness suite including those cases passes 55/55. The report explicitly preserves the main-thread and dense worst-case quadratic limits.
