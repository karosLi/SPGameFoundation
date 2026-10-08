# Authoritative handheld weapons

Opt-in gameplay entries:

- SPF → Weapons → Create Landscape Weapon Brawler Scene
- SPF → Weapons → Create Portrait Weapon Horde Scene

The belt player uses J / ATTACK, Q / SWITCH, with the existing kick, jump, heal and independent joystick. The portrait horde automatically attacks targets within the equipped weapon's range; F / ATTACK can attack without a nearby target, Q / SWITCH cycles equipment, and the existing J / PULSE and K / BLINK retain separate pointer ownership. Both HUDs display current and pending equipment. These factories enable actual gameplay and natural character presentation, rather than a showcase animation.

## Content and dependencies

`WeaponProfile` is authored data with stable ContentId and VisualId, finite action family, tick durations, commitment/cancel windows, damage, reach, projectile speed/lifetime, and grip/support/muzzle metadata. Default IDs1001–1004 select blade, sword, staff and bow. New IDs do not require a common weapon enum. `SvConfig.WeaponProfiles` and `BwModule.CreateWeaponBelt(config, profiles)` accept alternate content. Runtime clones and validates the catalog, and snapshots fingerprint every behavior/attachment parameter. A label change is cosmetic.

`SPF.Contracts.Weapons.WeaponViewState` and `WeaponCue` contain only read-only POD metadata. `SPF.L2.Weapons.WeaponRuntime` has no game or presentation dependency. It reuses ActionTimeline, TickInputBuffer and HitHistory. The game adapters preserve the existing tables, spatial grids, SkillSlots, pooled enemies and damage resolvers. Unextended default/classic factories never install either the weapon resource or the optional skill pose clock, and their fixture bytes stay unchanged.

## Tick and hit contract

The player-facing Belt weapon/ability examples select the versioned [SmoothAttackV1 mobility policy](BeltAttackMobility.md): the stick moves during windup/contact/recovery while committed attack aim stays fixed. Low-level legacy configs and speed-matched historical recordings retain the old policy explicitly; snapshot layout equality does not imply replay compatibility across these policies.

A new action captures aim once. Consecutive held/automatic actions capture their next aim at the new pulse boundary; the current action cannot turn halfway through its contact or release. Presses use a short fixed-tick buffer. Switching cancels windup before contact, waits through the committed interval, then runs the destination weapon's equip duration. Repeated switch input can update the pending item. Death/interruption clears buffered input, pending equipment, contact history and live owned projectiles. Level-up/session pause does not advance the weapon clock.

Melee uses one explicit committed contact sample: `ContactWindow = [Active.From, Active.From + 1)`. Its canonical grip-to-tip segment is queried once through the host grid; stable full entity handles deduplicate overlapping cells. `Active` also describes visible follow-through and the switch lock. Targets entering later during follow-through are not damaged. This is a deliberate one-contact melee model, not a continuously swept blade volume. A future multi-tick cutting mechanic needs an authoritative shared trajectory.

Staff and bow release exactly once at their authored marker. Bow has a longer draw and can be canceled before release. The bounded projectile pool advances in fixed ticks and uses CombatSweep for high-speed contact. Earliest contact wins with stable identity tie breaking; accepted queue insertion precedes hit-history recording. Pool exhaustion rejects the newest release once without a later retry. Damage-queue rejection consumes an encountered projectile deterministically. A newborn projectile starts at the muzzle on the release tick and begins travel on the next tick; interpolation shows the nocked arrow until the release marker and the projectile afterward, with no double arrow. Projectile expiration runs after its final collision segment. Survivor damage retains its existing Might multiplier.

Profile offsets use (forward distance along aim, visual height) before actor scale. Collision uses ground-space forward distance and a separate height; belt ground depth is projected only in the adapter. Presentation uses the same offset definition. The pose hits the canonical socket at contact/release, then follows through. `View(alpha)` samples the previous/current authoritative ticks for continuous pose motion, with phase and markers derived from the same action. No rendered bone position controls damage.

Each renderer resolves one monotonic interpolation fraction for the current fixed tick, shared by bodies, camera follow, weapons, projectiles, trails and cue gating. After a paused frame presents alpha1, resuming at raw alpha0.3 keeps1 until the next tick begins its new interval. Session restore/restart exposes an unsaved timeline revision; level/resource replacement also resets the view cursor. This policy never rebases the simulation clock or changes snapshot bytes.

## Bounded effects and persistence

Each runtime owns32 projectile slots by default, one history slice per contact/projectile scope, and32 retained cue entries. Retained cues are oldest to newest, monotonically sequenced and independent of render frequency. They describe actual begin, release, accepted hit, cancellation and equip events. A full cosmetic ring retires its oldest event; gameplay remains unchanged. Cue backlog and diagnostic overwrite counts are intentionally unsaved. Restore never replays old effects.

Renderer adapters project cue/shot positions, sample actual held-weapon sockets, and pass them to WeaponParticlePresenter. Repeated render calls do not duplicate sequenced bursts; actual arrow trails connect successive presented positions, keyed by projectile pool slot and saved SpawnTick. Runtime rewind/menu clears presentation state. GPU-compute particles and their CPU/Burst fallback are described in [BoundedWeaponParticles](BoundedWeaponParticles.md). The GPU tier binds a fixed1024-particle budget; the DataTexture fallback binds256. Adaptive actor quality changes do not resize these already-warmed effect pools. These presentation budgets never alter simulation capacities or saved bytes.

Snapshots include equipment, pending switch, buffered input, action tick/pulse/release state, projectile slots and stable hit histories. They validate content fingerprint/capacity, finite geometry, normalized directions, owner/pulse linkage and buffered timestamps. A different opt-in content/layout cannot silently load an old snapshot.

## Verification scope

Focused EditMode regressions cover all four profiles in both real game adapters, one-time release, range/damage differences, windup cancellation, active switch delay, late-contact rejection, stable history capacity, full damage queues, pause/death/restart, in-flight restore and changed-content rejection. Calibrated warmed allocation probes retain the existing zero-allocation threshold and report their current-thread scope.

`BwWeaponGameplayTests` and `SvWeaponGameplayTests` exercise the actual UI/game loops and capture all four weapons facing both ways, with socket/damage assertions. `SPF_WEAPON_GAMEPLAY_SEQUENCE=1` additionally captures normal automatic-clock gameplay in a bounded raw-frame buffer, with PNG/file encoding after acquisition. CSV and ffconcat retain actual acquisition times; the30Hz target is not relabeled as a measurement. Real Unity compilation, graphics/Burst and artifact review must be established by the central licensed runner; the .NET harness alone does not establish them.

The weapon snapshot header is version2. SpawnTick preserves a full authored travel lifetime after the stationary muzzle birth tick, including a one-tick lifetime. Gameplay graphics sequences warm the target with ordinary rendered frames before their first buffered readback.

## Skill and role animation adapters

The staged BeltScroller and MobileSkills opt-ins, including the weapon variants, now install a small optional ActionPoseClock, built on the existing ActionTimeline. It stores an authored pose content ID, duration (at least three ticks for a visible interpolated interval) and pulse, with fixed-tick pause/death/reset/restore behavior. It is a visual response clock for an already-activated instant skill, and does not activate or apply the skill. Brawler maps actual kick100, jump103 and heal105; Survivor maps pulse101 and blink102. Simultaneous pulse/blink executes both gameplay skills, with blink taking visual priority. Enemy kicks use their existing fighter action duration. Hero/agile/heavy motion roles come from the existing fighter variant or enemy radius.

The view receives SkillPoseId, SkillPhase, SkillPulse and SkillWeight plus a role profile ID, allowing finite authored pose families without a new global enum entry per future skill. Unextended classic modes retain their original resource layout. These staged opt-in modes intentionally evolve their save layout with the additional versioned resource; earlier opt-in snapshots fail the world resource/layout check clearly, rather than being silently reinterpreted. This is a prototype schema boundary, not a migration or a change to the original classic fixtures. Particle trails now connect successive rendered projectile positions, keyed by fixed pool slot and saved SpawnTick, so they cannot lead an interpolated arrow or bridge a reused projectile slot. Newborn projectiles preserve their full authored travel lifetime, including a one-tick lifetime, and the weapon snapshot header is version2.
