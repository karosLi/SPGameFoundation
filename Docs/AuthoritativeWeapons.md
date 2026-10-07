# Authoritative handheld weapons

Opt-in gameplay entries:

- SPF → Weapons → Create Landscape Weapon Brawler Scene
- SPF → Weapons → Create Portrait Weapon Horde Scene

The belt player uses J / ATTACK, Q / SWITCH, with the existing kick, jump, heal and independent joystick. The portrait horde automatically attacks targets within the equipped weapon's range; F / ATTACK can attack without a nearby target, Q / SWITCH cycles equipment, and the existing J / PULSE and K / BLINK retain separate pointer ownership. Both HUDs display current and pending equipment. These factories enable actual gameplay and natural character presentation, rather than a showcase animation.

## Content and dependencies

`WeaponProfile` is authored data with stable ContentId and VisualId, finite action family, tick durations, commitment/cancel windows, damage, reach, projectile speed/lifetime, and grip/support/muzzle metadata. Default IDs1001–1004 select blade, sword, staff and bow. New IDs do not require a common weapon enum. `SvConfig.WeaponProfiles` and `BwModule.CreateWeaponBelt(config, profiles)` accept alternate content. Runtime clones and validates the catalog, and snapshots fingerprint every behavior/attachment parameter. A label change is cosmetic.

`SPF.Contracts.Weapons.WeaponViewState` and `WeaponCue` contain only read-only POD metadata. `SPF.L2.Weapons.WeaponRuntime` has no game or presentation dependency. It reuses ActionTimeline, TickInputBuffer and HitHistory. The game adapters preserve the existing tables, spatial grids, SkillSlots, pooled enemies and damage resolvers. Classic modes never install this resource or change their saved schemas.

## Tick and hit contract

A new action captures aim once. Consecutive held/automatic actions capture their next aim at the new pulse boundary; the current action cannot turn halfway through its contact or release. Presses use a short fixed-tick buffer. Switching cancels windup before contact, waits through the committed interval, then runs the destination weapon's equip duration. Repeated switch input can update the pending item. Death/interruption clears buffered input, pending equipment, contact history and live owned projectiles. Level-up/session pause does not advance the weapon clock.

Melee uses one explicit committed contact sample: `ContactWindow = [Active.From, Active.From + 1)`. Its canonical grip-to-tip segment is queried once through the host grid; stable full entity handles deduplicate overlapping cells. `Active` also describes visible follow-through and the switch lock. Targets entering later during follow-through are not damaged. This is a deliberate one-contact melee model, not a continuously swept blade volume. A future multi-tick cutting mechanic needs an authoritative shared trajectory.

Staff and bow release exactly once at their authored marker. Bow has a longer draw and can be canceled before release. The bounded projectile pool advances in fixed ticks and uses CombatSweep for high-speed contact. Earliest contact wins with stable identity tie breaking; accepted queue insertion precedes hit-history recording. Pool exhaustion rejects the newest release once without a later retry. Damage-queue rejection consumes an encountered projectile deterministically. A newborn projectile starts at the muzzle on the release tick and begins travel on the next tick; interpolation shows the nocked arrow until the release marker and the projectile afterward, with no double arrow. Projectile expiration runs after its final collision segment. Survivor damage retains its existing Might multiplier.

Profile offsets use (forward distance along aim, visual height) before actor scale. Collision uses ground-space forward distance and a separate height; belt ground depth is projected only in the adapter. Presentation uses the same offset definition. The pose hits the canonical socket at contact/release, then follows through. `View(alpha)` samples the previous/current authoritative ticks for continuous pose motion, with phase and markers derived from the same action. No rendered bone position controls damage.

## Bounded effects and persistence

Each runtime owns32 projectile slots by default, one history slice per contact/projectile scope, and32 retained cue entries. Retained cues are oldest to newest, monotonically sequenced and independent of render frequency. They describe actual begin, release, accepted hit, cancellation and equip events. A full cosmetic ring retires its oldest event; gameplay remains unchanged. Cue backlog and diagnostic overwrite counts are intentionally unsaved. Restore never replays old effects.

Renderer adapters project cue/shot positions, sample actual held-weapon sockets, and pass them to WeaponParticlePresenter. Repeated render calls do not duplicate sequenced bursts; actual arrow trails connect successive presented positions, keyed by projectile pool slot and saved SpawnTick. Runtime rewind/menu clears presentation state. GPU-compute particles and their CPU/Burst fallback are described in [BoundedWeaponParticles](BoundedWeaponParticles.md). The GPU tier binds a fixed1024-particle budget; the DataTexture fallback binds256. Adaptive actor quality changes do not resize these already-warmed effect pools. These presentation budgets never alter simulation capacities or saved bytes.

Snapshots include equipment, pending switch, buffered input, action tick/pulse/release state, projectile slots and stable hit histories. They validate content fingerprint/capacity, finite geometry, normalized directions, owner/pulse linkage and buffered timestamps. A different opt-in content/layout cannot silently load an old snapshot.

## Verification scope

Focused EditMode regressions cover all four profiles in both real game adapters, one-time release, range/damage differences, windup cancellation, active switch delay, late-contact rejection, stable history capacity, full damage queues, pause/death/restart, in-flight restore and changed-content rejection. Calibrated warmed allocation probes retain the existing zero-allocation threshold and report their current-thread scope.

`BwWeaponGameplayTests` and `SvWeaponGameplayTests` exercise the actual UI/game loops and capture all four weapons facing both ways, with socket/damage assertions. `SPF_WEAPON_GAMEPLAY_SEQUENCE=1` additionally captures normal automatic-clock gameplay in a bounded raw-frame buffer, with PNG/file encoding after acquisition. CSV and ffconcat retain actual acquisition times; the30Hz target is not relabeled as a measurement. Real Unity compilation, graphics/Burst and artifact review must be established by the central licensed runner; the .NET harness alone does not establish them.

The weapon snapshot header is version2. SpawnTick preserves a full authored travel lifetime after the stationary muzzle birth tick, including a one-tick lifetime. Gameplay graphics sequences warm the target with ordinary rendered frames before their first buffered readback.
