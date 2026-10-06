# Shared mobile skill HUD: opt-in examples

## Play the two examples

- Unity menu **SPF → Mobile Skill HUD → Create Landscape Brawler Scene**, or `BwGameBootstrap.CreateMobileCombat()`.
- Unity menu **SPF → Mobile Skill HUD → Create Portrait Horde Scene**, or `SvGameBootstrap.CreateMobileCombatExample()`.
- Both have a visible left joystick, vector skill icons, charge counts, recharge rings and fixed-tick cooldown text. WASD/arrows and J/K feed the same input frame.
- The menu names are orientation recommendations, not global mobile project settings. Brawler's lateral arena benefits from landscape; the horde's radial movement works well in portrait. Layouts handle either actual aspect and `Screen.safeArea`. No menu mutates PlayerSettings or `Screen.orientation`.

Brawler: hold **PUNCH / J** to repeat the existing jab after recovery; tap **KICK / K** for the existing kick, with two charges. Punch recharges in 20 playing ticks; kick charges replenish sequentially every 90 ticks. Brawler's existing simulation is **60 Hz**. Only an idle/walking living player can start an attack. Recovery rejects new casts without consuming a charge. Simultaneous requests choose kick first, matching the existing fighter update. Holding is retained through temporary recovery so repeat does not depend on UI timing. Existing classic unlimited queued-combo behavior is unchanged outside this opt-in example.

Horde: tap **PULSE / J** to damage targets in a radius-4 disk (28 base damage through the existing hit queue and resolver), with two charges and a 90-tick recharge. Drag **BLINK** to aim and release to move 3 units, clamped to the arena; dragging more than the cancel radius cancels. A short untargeted tap/K uses authoritative facing. Blink has two charges and a 120-tick recharge. Survivor is **30 Hz**. Blink is explicitly an instantaneous position change, not a swept attack or an invulnerability effect. Automatic weapons and guard rings remain independent examples. If pulse and blink are requested together, both may consume a charge: blink resolves first, then ordinary movement, then pulse collision uses the resulting hero position.

## Ownership and extension points

`SPF.Contracts.SkillSlotDefinition` holds stable skill/icon IDs, the configured gesture mode, integer recharge period and maximum charges. `SkillSlotSnapshot` carries current charges/recharge ticks and game-supplied eligibility. It contains no UI references.

`SPF.L2.Skills.SkillSlots` is a bounded main-thread gate with 1–4 slots and at most 16 charges per slot. Construction allocates its fixed arrays; advancing, gating and reading snapshots do not allocate. Call `AdvanceTick(playing)` once per authoritative tick, then `TryActivate(slot, eligible)` only for a requested skill. Successful activation consumes one charge once per slot per tick. Existing recharge is never restarted by spending another charge. The game owns target selection, effects, HP, movement and resource costs beyond charges.

This is separate from the existing projectile `SPF.L2.Skills.SkillDefinition`; that classic Snake contract and its seconds/mass rules are unchanged. There is no new skill inheritance hierarchy or global damage dispatcher.

`MobileCombatHud` consumes `IMobileCombatHudSource`:

- `SlotCount`, `TickRate`, `Playing`
- `ReadSlot(slot)` returns authoritative eligibility, charges, cooldown and icon ID
- `SlotLabel(slot)` supplies a presentation label
- Optional `IconResolver` maps icon IDs to project sprites; set it before `Build`. Missing art uses the supplied vector fallback icons. Add project art without extending the simulation or HUD switch statements.

A new game installs a `SkillSlots` resource and a small fixed-tick consumer, implements this read-only HUD source, builds the HUD under an existing canvas and adds `hud.Input` to its InputRouter. Bind `hud.Interrupted` to clear the game's latched input on global interruption. Bind `hud.SkillCanceled` to `SkillInput.CancelSlot` for local cancellation, preserving other slots and movement. Call `Session.Sync()` before adapters inspect native world state. UI never counts down cooldowns or applies damage. Swapping icons, resizing safe area, changing quality or hiding a renderer must not mutate skill state.

The adapter returns the existing `InputFrame`: slot indices map to button bits; hold supplies Held, tap supplies Pressed, aimed release supplies Pressed plus Aim. There is deliberately **one aimed-release direction per InputFrame**, sufficient for these examples. `SkillInput.Latch(stored, next, aimSlot)` preserves a release direction across frames without a tick and when another slot is pressed. A new press of that aimed slot replaces it, including zero aim. Extending to concurrent independently aimed skills would require a versioned command contract, not silently sharing this vector.

## Input interruption and layout

Each `SkillControl` and joystick owns one pointer. Duplicate downs and foreign drags/up events cannot steal or release it. Different controls accept different fingers. Short taps remain pending until one poll; aimed cast release direction persists until consumed. A local control cancel/disable clears only its own pending/latched slot and its aim. Application focus/pause, layout changes, whole-HUD disable, menu/death/restart and input-owner handoff flush all UI and latched commands. Manual controls are hidden while the priority Scripted source owns input, and while Survivor AutoPlay owns input; returning control requires a new press. Unity legacy touch cancellation is explicitly distinguished from an aimed release.

A hold is still an input intent while its snapshot is temporarily unavailable during attack recovery. The simulation rejects it until eligible. A depleted/disabled snapshot rejects new pointer-down events. Resuming after an interruption requires a new pointer press. UI cancellation cannot refund an action already consumed by a simulation tick.

The safe-area root contains controls and the example overlays. Canvas references adapt to actual portrait/landscape aspect. The HUD repositions a bounded two-column skill cluster and never requests orientation permissions or changes game configuration. The joystick ring is a fixed visual guide for the floating touch-area input; its knob shows direction and magnitude.

## Persistence and compatibility

New resources are installed only by the mobile examples:

- `Bw.MobileSkills.V1` (alongside the existing opt-in stable-handle Brawler combat resource)
- `Sv.MobileSkills.V1`

`SkillSlots` writes explicit magic, version, slot count, definition fingerprint, activated mask, charges and remaining integer recharge ticks. Restore validates IDs, modes, capacities and recharge invariants. Captures require the same authored definitions. Restart/level reset refills charges and clears activation state. No simulation-relevant state lives in a UI sidecar.

No classic FighterInfo, Survivor BulletInfo, InputFrame or classic Game snapshot payload changed. Classic snapshots cannot be loaded into these opt-in resource layouts, and vice versa. Existing classic snapshot fixtures remain a gate. No attempt is made to migrate legacy row-based hit histories here.

Pulse uses one grid query expanded by the largest target radius, then inclusive disk narrow phase. Each grid entry is visited once, so a target receives one hit. A full damage queue drops excess targets for that pulse without unbounded storage or retries; casting still spends its charge. Author event capacity accordingly. UI/update rates and visual effects never govern this damage.

## Verification boundary

Focused EditMode suites: `SkillSlotTests`, `MobileSkillControlTests`, `BwMobileSkillTests`, `SvMobileSkillTests`. They cover exact recharge boundaries, sequential charges, snapshot continuation/schema rejection, zero-allocation warmed gate calls, latched aim across no-tick frames, disabled/focus/pause/cancel/multitouch ownership, safe-area math, real Brawler damage and Survivor pulse/blink effects.

Graphics PlayMode suites `BwMobileHudPlayTests` and `SvMobileHudPlayTests` cover real bootstrap/control wiring in GPU-driven and data-texture tiers, independent fingers, interruptions, real skill effects, quality-independent snapshots and restart. The tests use ManualClock while exercising UI so automatic frame ticks cannot obscure the expected cooldown boundary.

.NET harness validates compilation, deterministic simulation and our managed code paths. It is not evidence for UGUI rendering, physical touch delivery, Burst/job safety or mobile device performance. Real Unity screenshots, PlayMode and target-device profiling remain separate integration gates.

### Isolated harness result (2026-10-06)

- 72 generated projects compiled serially with 0 warnings and 0 errors.
- Shared skill + pointer/safe-area tests: 16/16 passed; existing mobile input regressions: 19/19 passed.
- Full Brawler EditMode suite: 22/22 passed.
- Survivor non-performance EditMode suite: 44/44 passed, including the exact classic snapshot fixture and opt-in continuation.
- Warmed skill gate and snapshot reads measured 0 managed bytes. This is not a whole-frame or device claim.
- Added explicit full-queue, inclusive-edge and simultaneous blink/move/pulse origin assertions; all passed in the harness.
- Unity-only custom-sprite aim/large-cooldown text assertions and GraphicRaycaster/safe-root bounds/input-owner handoff PlayMode assertions are authored. These engine-dependent checks are not counted as harness passes.
- New graphics PlayMode suites compiled but were not run in the isolated worktree. Integration owns real Unity execution and controlled screenshots.

### CanvasRenderer integration correction

The first actual Unity HUD smoke run found that `CombatControlGraphic` lacked its own `CanvasRenderer`; the base UGUI Graphic only requires RectTransform. The custom class now explicitly requires CanvasRenderer, and the HUD factory constructs it before adding the Graphic. This fixes real rendering/raycast component ownership, not just log handling.

Engine-only `CombatControlGraphicTests` cover direct AddComponent for ring, disc and all four glyphs, actual submitted CanvasRenderer mesh vertices, and every graphic produced by a four-slot HUD factory. Both gameplay smokes now verify required components for all HUD graphics and submitted geometry for every active custom Graphic, alongside the real GraphicRaycaster hit-target/bounds assertions. These engine checks require a new Unity run and are not harness pass claims.

## Orientation/safe-area capture gate

`MobileCombatHud.SetPreviewViewport(width, height, safePixels)` supplies an explicit presentation viewport for camera/render-target previews. The caller must render the matching viewport. It uses a constant pixel scale for the preview instead of inheriting the editor desktop resolution; `ClearPreviewViewport()` restores normal screen-driven layout. It never changes PlayerSettings, Screen orientation or skill clocks. Like every layout change, it cancels pending gestures.

`BwMobileHudCaptureTests` renders the actual Brawler at 1280×720. `SvMobileHudCaptureTests` renders the actual horde at 720×1280, including a synthetic safe rectangle `(0,48,720,1168)` representing a 64-pixel top inset and 48-pixel home-indicator inset. Each fixture runs both sprite tiers, routes **all canvases belonging to its game** into the game camera, and saves unmodified RenderTexture PNG readbacks with projected bounds/raycast metadata. It does not rescale a desktop screenshot or paint a simulated result.

Output directory: `Artifacts/Screenshots/MobileHud/`.

- `brawler-landscape-ready-{gpu,datatex}.png`
- `brawler-landscape-cooldown-{gpu,datatex}.png`
- `survivor-portrait-ready-{gpu,datatex}.png`
- `survivor-portrait-cooldown-{gpu,datatex}.png`
- `survivor-portrait-safearea-{gpu,datatex}.png`
- `survivor-portrait-safearea-aim-{gpu,datatex}.png`
- `survivor-portrait-safearea-cancel-{gpu,datatex}.png`

The matching `.txt` sidecars identify the actual viewport, synthetic safe rectangle, every captured canvas, and camera-projected control bounds/first raycast results. Four Unity test cases produce fourteen PNGs. Asserted pixel counts guard against missing icons/status/telemetry canvases; actual camera-space GraphicRaycaster hits and bounds are checked in both orientations. Final visual inspection and a successful central Unity run are still required before these captures count as evidence.

### Inclusive annulus precision correction

A remote arm64 Unity run rejected the stored-float `4f + .3f` pulse tangent although Linux passed. The expanded `QueryCells` path does not contain a strict circle filter. A concrete mixed-precision hazard is reproducible: the stored coordinate is `4.300000190734863`, its squared value rounded to binary32 is `18.490001678466797`, and the promoted exact square is `18.49000164031986`. Comparing a rounded float dot against an extended scalar square can therefore reject the closed boundary. The precise original backend lowering is not proven by this arithmetic reproduction alone.

The shared annulus predicate now explicitly rounds its combined inner/outer bounds to binary32 and evaluates **both** squared sides in double. There is no epsilon or widened collision band. New tests verify immediately adjacent representable floats on both sides of inner/outer boundaries, positive/negative axes, managed and Strict/Fast Burst entry points, and the actual Survivor grid-to-damage path. Diagnostic output records operand bits and Burst outcomes; one float outside must still miss. Existing beam/sweep code is unchanged. Harness results are 10/10 focused shape/boundary tests and 50/50 Survivor non-performance tests; the new arm64 Unity rerun remains the final platform confirmation.
