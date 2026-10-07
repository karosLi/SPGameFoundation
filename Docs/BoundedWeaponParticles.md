# Bounded weapon particles

This is a genuine compute simulation backend, separate from the older CPU-updated `SpriteEffects` pool. It targets the repository's existing built-in 2D pipeline. It does not install VFX Graph, change a renderer pipeline, or mutate weapon simulation, damage, input, saves, or gameplay RNG.

## Production API and ownership

`SPF.Presentation.Particles.WeaponParticlePresenter` owns one `ParticleRenderer` and a fixed 32-entry emitter table. The weapon gameplay adapters call:

1. `BeginFrame(dt, viewRect)` with the current orthographic screen rectangle `(minX,minY,maxX,maxY)`.
2. `UpdateEmitter(owner, state, tip, muzzle, direction, depth, hero)` after `GameplayCharacterPresenter.Evaluate` / `TryReadWeapon`; positions are the actual evaluated weapon sockets in render space. The complete `EntityHandle`, including generation, identifies the owner.
3. `SubmitCue(projectedCue, depth, hero)` in increasing retained-ring sequence order. Release and impact are authoritative runtime cues. Only accepted damage produces an impact. `WeaponCue.Position` must already be projected into the same world space; the particle layer never guesses a ground/height projection. `Clear()` on session/reset/rewind before replaying earlier sequences.
4. `ProjectileTrail(previous, current, depth, visualSeed, hero)` once per simulation-tick advance for actual projectile positions.
5. `EndFrame(bounds, layer)` after actor evaluation/submission. It retires missing attached emitters, simulates, and submits the production draw.

A caller must not call `Simulate` more than once between `BeginFrame` calls. A charge emitter is local to the current socket basis; its existing particles follow that moving socket. Generation changes, equipped-content changes, missing owners, cancel/equip cues, and muzzle teleports larger than 2.5 world units invalidate attached state and previous blade samples. World bursts detach and finish their short lifetimes. A caller can explicitly call `ResetOwner` on death or an intentional smaller teleport.

Visual recipes use presentation-only integer hashes. Knife/thrust actions have compact directional streaks. Sword/slash actions leave a short tapered segment between successive actual blade tips. Confirmed impacts have a narrow fast spark phase, compact core pop and slower embers. Staff windup has inward socket-local motes and a rotating broken rune, followed by an outward release burst. Bow windup has warm inward motes; release has a short cross-string snap and forward sparks; moving arrows use the projectile trail API. A swung blade never invents a confirmed-hit flash.

## Simulation and rendering are distinct

The capability gate chooses `GpuCompute` only for a supported graphics API, compute support, both supported 64-thread kernels, SSBO/instancing/shader limits, buffer limits, and a sampleable RGBA8 atlas. A requested data-texture tier, forced CPU mode, missing compute resource/kernel, or missing capability chooses `CpuBurst`. No GLES compute support is claimed here. The lower tier is CPU/Burst simulation with the existing `SpriteBatch` data-texture rendering; forced CPU on a capable GPU renderer keeps its existing structured sprite draw.

GPU mode owns a clone of the compute shader and its buffer bindings. Persistent state contains position, velocity, age, lifetime, drag, gravity, rotation, shape, tint and socket generation. A scatter kernel applies bounded spawn commands; the simulation kernel advances and retires particles and writes the same 32-byte packed sprite representation as CPU. CPU uploads spawn commands and socket records, never evolving particle positions/velocities. There is no production GPU readback. The explicit `ReadbackForValidation` method exists only for tests/tools.

A fixed-capacity indirect draw reuses arguments created during loading; dead particles generate zero-area quads. This deliberately trades bounded vertex work for avoiding append counters, atomic contention and count readback. Compute still dispatches the bounded pool; GPU work is not proportional only to visible particles. CPU fallback uses a Burst parallel job for the same integrator and builds the existing packed sprite output after completing it.

## Budgets and counters

- High / low capacity: 1024 / 256 particles; 32 sockets; at most 64 spawn-or-retirement commands per frame. Each spawn record is 112 bytes and each socket is 32 bytes. Maximum production compute buffer upload payload is 8,192 bytes per frame (64 × 112 + 32 × 32), excluding initialization/clear, shader parameters, driver overhead and physical bus effects.
- 16 command slots, 64 particle slots, and 25% of admission area are reserved from decorative/trail effects for important release/impact cues. Higher priorities replace lower priorities; among equal low priorities, farther admitted particles are shed first. If no allowed victim fits, the new visual particle is dropped. No unbounded queue exists.
- Conservative summed maximum quad area is limited to 12% (high) / 7% (low) of the current orthographic view. A single quad cannot exceed 0.4% of that view on admission. The sum is an admission estimate, not a GPU-visible count or measured overdraw. A camera zoom rescales all submitted quads to preserve the bound. Lifetime reservations include a small conservative margin; actual live GPU counts can be lower.
- `ReservedCount`, `ReservedCoverageFraction`, `CoverageFraction`, `Dropped`, and `Replaced` describe CPU admission bookkeeping. `CoverageFraction` includes the current zoom scale. They are not readback counters. `BytesUploaded` is logical API buffer payload; fallback sprite texture padding is included by `SpriteBatch`. `DispatchCalls` and `DrawCalls` count submissions.
- One generated 128×32 padded atlas (four original glyphs), one additive blend group, no depth writes, lights, shadows, soft-depth sampling, distortion, or broad glow layers. Compute does not remove transparent fill cost.
- Frame delta is bounded to 50 ms and lifetime to 1.5 s. This is cosmetic time, independent from fixed-tick gameplay. All buffers and lower-tier sprite pages are allocated/warmed at construction; disposal is idempotent.

## Verification and honest limits

`WeaponParticleTests` exercises ABI, seeded RNG, independent integration values, lifetime, overflow/priority admission, unique scatter targets, conservative area and camera zoom, attached-generation invalidation, capability fallback, cue deduplication, owner lifecycle, and randomized mixed admission/socket/zoom retirement invariants. The focused .NET harness run passed all 17 cases on 2026-10-07. It verifies those CPU/logical behaviors, but cannot execute Burst, compile compute/shaders, or prove graphics.

`WeaponParticleGraphicsTests` is for the central real-Unity graphics run. It reads back actual production compute state and compares it with the CPU reference across spawn, drag/gravity, lifetimes and socket invalidation (coefficient error < 1e-4). It compares actual production pixels with CPU packed-buffer and data-texture draws (>150 occupied pixels; per-channel tolerance 8/255, at most 2.5% occupied-union mismatches), checks resource isolation, clear/dispose/recreate and fallback with a missing asset or wrong kernels, and measures calibrated warmed current-thread managed allocation around simulation/draw submissions. Those allocation scopes exclude rendering-frame, driver/native and other-thread allocations. It saves production particle comparison PNGs under `Artifacts`.

The coordinator separately captures integrated weapon gameplay after the adapters are connected. Desktop Metal results are correctness evidence only. No mobile GPU speedup, frame budget, sustained thermal behavior, or device timing is established by these tests. The selected starting bounds must still be profiled on intended Android/iOS hardware.
