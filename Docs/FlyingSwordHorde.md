# Flying-sword horde: opt-in Survivor configuration

## Playable entry point

- Menu: **SPF → Survivor → Create Flying Sword Horde Scene**, then Play and START.
- Code: `SvGameBootstrap.CreateFlyingSwordExample()`; configuration: `SvConfig.CreateFlyingSwordExample()`.
- One reusable Survivor module, with its existing enemy table, spatial grid, Burst separation, gem pool, hit/death resolution, progression, camera, restart flow and shared mobile HUD. It is not a second horde engine.
- Left stick / WASD moves; PULSE / J clears space; drag BLINK / K to aim and release (drag outside the cancel radius to cancel). Auto-targeted swords orbit, launch, home and return. Collect gems and pick an upgrade. Survive 75 seconds of waves, then clear every remaining enemy to win. Death and victory both offer restart/menu.
- Sword Swarm grows 10 → 14 → 18 → 22 → 24 swords. Keen Edges grows damage/contact radius; Sword Tempo reduces orbit recharge; Piercing Flight grows the unique-target limit per sortie. Speed, vitality, magnet and might reuse the existing passive rules. Legacy bolts/nova/spiral/orbit damage are disabled only for this opt-in sword configuration.

## Authority and bounded work

`SvFlyingSwordState` is a fixed-capacity native resource with one `SvSword` and `HitHistory` slice per sword. Its jobs own positions, phase, target handle, action timeline and damage. Rendering only reads it.

- Orbit, outgoing homing and return durations/speeds, turn rate, targeting radius, damage, capacity and unique-target history are authorable session constants.
- Damage uses relative-motion `CombatSweep.Circles`, including a moving target's previous/current positions. The authoritative shape is the configured circle around the blade center, not the full decorative sprite.
- Broadphase uses the existing enemy grid expanded by the maximum enemy displacement for the current tick. A single O(enemy-count) pass computes that conservative bound. Nearest-target queries use grid cells, and homing resolves the generation-checked `EntityLookup` in O(1).
- Contacts are ordered by entering time, then stable handle index/generation. Row sorting cannot change the tie winner. A target can take damage only once from each sword sortie, across outgoing and return motion. Recycled generations are different targets.
- All contacts fit into one preallocated enemy-capacity scratch array reused serially by the sword job. Sortie history/pierce exhaustion rejects further damage without eviction. Queue failure does not consume history. Diagnostics use O(1) saturating addition.
- Maximum authored sword capacity is 64, history per sword 128. The runnable preset uses 24/12, 1024 enemy slots and a 768 live spawn limit. This is a capacity contract, not a mobile-device frame-time claim.
- Target loss switches the sword to returning. Return timeout repositioning is non-damaging. The trail view also clears discontinuities rather than drawing false attack beams.

## Snapshots and compatibility

The sword resource has an opt-in V1 schema and a fingerprint over every sword rule (including damage, speeds and phase timings), plus exact capacity/history validation. All sword positions, phases, clocks, targets, histories and authoritative diagnostics are saved. Scratch contact buffers and visual trails/numbers are not.

Enabling swords also opts into the existing game-clock extension even when used on the Classic variant. Disabled Classic retains the existing exact payload, resource layout and fixture. Existing guard/mobile skill behavior remains isolated.

## Read-only presentation

The same GPU-driven and data-texture SpriteBatch backends draw the smooth outlined swords. The opt-in natural-character adapter draws the hero/nearby enemies; remaining enemies retain cheap smooth sprites.

- Blue/gold trails: fixed eight samples per sword; quality changes only the number of drawn segments (7/4/2).
- Actual applied damage is aggregated per target per resolve tick, sent through the existing bounded feedback queue and drawn using the shared SpriteEffects/SpriteFont batches. The number pool is 64, admission is capped at 16/8/4 per rendered frame, and each number lasts .62 seconds. No GameObject, Text or string is created for each hit.
- Sprite batches and upload buffers are prewarmed. Number/trail drops never change hit policy or damage.
- The real shared portrait HUD retains safe-area layout, joystick, skill charges/cooldown, aim/cancel and input-owner cancellation. A small non-interactive telemetry backplate protects three status lines from crowd bars.

## Verification

`SvFlyingSwordTests` covers orbit→launch→return, one-hit-per-sortie, freeze during level-up, high-speed and relative-motion sweeps, TOI/stable tie ordering, reorder/generation recycle, full queue/history rejection, snapshot exact continuation, changed-config rejection, enabled-on-classic clock preservation, upgrade/win/death/restart/menu and a dense workload.

The dense report-only workload uses **1024 live moving targets, 24 swords, 45 warmup + 120 measured ticks**, and recycles 32 handles before measurement. It reports collision-grid candidates, swept contacts, accepted queued hits and overflow counters only for the measured window. A calibrated ManagedAllocationProbe measures the prepared tick delegate with retained-array and empty controls before and after. It is a .NET/Unity-stub logic measurement; it does not measure Burst workers, GPU cost or device performance.

`SvFlyingSwordPlayTests` provides central Unity execution entry points:

1. `CaptureDenseSwordSwarmWithRealPortraitHudAndQualityInvariant`, parameterized for both sprite tiers: 160 enemies, 18 swords, real 720×1280 Canvas/HUD, synthetic top/home insets, hit-number admission, visible trails, HUD hit targets, low-VFX screenshot and exact snapshot equality across render-quality changes. It also calibrates 60 synchronous calls to the real sword presentation; this excludes a whole Unity frame.
2. `PlayableSwordLoopKillsLevelsChoosesWinsDiesAndRestarts`: actual enemy kill → gem pickup → upgrade-button choice → victory → restart → death → restart → menu.

Capture filenames: `survivor-sword-horde-portrait-{gpu,datatex}` and `survivor-sword-horde-low-vfx-{gpu,datatex}`. The worker compiled these tests against the harness but did not launch Unity; actual render/capture results must be recorded by central validation.

Snapshot compatibility includes the host `SvVariant` as well as every sword rule. Identical sword parameters cannot restore across Classic/Guard/FlyingSwordHorde because spawning and terminal flow differ. The ordinary same-configuration exact continuation and actual legacy snapshot fixtures remain unchanged. This opt-in fingerprint does not claim to migrate or validate every historical Survivor authoring field.
