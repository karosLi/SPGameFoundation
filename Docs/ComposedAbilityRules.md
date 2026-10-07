# Opt-in composed ability rules (Stage E)

This adds two local rule compositions on the existing weapon games. It does not add a weapon/action-family content enum, a generic ability engine, an object-payload event bus, a second game loop, or a new art pipeline. Existing factories, classic modes, weapon families and raw fixtures remain controls. Native graphics and physical-device acceptance must be reported separately from the .NET tests below.

## Source-grounded decision

The existing source already supplies almost the whole chain:

| Responsibility | Survivor | Brawler |
| --- | --- | --- |
| Intent | Shared `MobileCombatHud` → latched `InputFrame` → `MobileSkillInputSystem` | Shared HUD → `InputFrame` → `BeltFighterSystem` |
| Eligibility/charge | Playing/alive, `SkillSlots.AdvanceTick/TryActivate` | Fighting/alive, fighter free/recovery/ground conditions, same `SkillSlots` |
| Action | New local pulse `ActionTimeline`, existing `ActionPoseClock` pose 101 | Existing kick state/bone clip + pose 100; existing heal pose 105 |
| Query/history | Existing `SpatialGrid` + closed annulus test, bounded stable-handle `HitHistory` | Existing belt grid + `BwProbe.Tip`, ground depth/height hurt box, `BwSharedCombatState` |
| Authoritative request/fact | Existing bounded `SvHit` request queue → `ResolveSystem` owns HP, radius-scaled knock, deaths/loot | Existing direct `BeltCombatSystem` owns accepted damage, hit reaction, launch, score and drops; settled kick grants local credit |
| Visual cue | Existing unsaved `SvFeedback.Nova` describes a release; existing hit/death presentation describes resolver output | Existing kick/heal coordinated pose and existing hit/KO feedback; credit is authoritative state, not a particle message |

The genuinely repeated piece is the read-only admission decision: requested, playing, alive, free, and charge available. `AbilityAdmission.Evaluate` returns a small value result/reason; it neither spends a charge nor starts an action. Both opted-in games use it before their unchanged `SkillSlots.TryActivate` gate. It is not applied retroactively to default modes.

The games have different settlement semantics, so no common damage resolver or effect queue was extracted. `SvPulseReleaseResult` is deliberately game-local: accepted *requests* are not settled HP changes. Brawler's `SettledKickHits` counts actual direct damage accepted through its existing history. The two weapon adapters already share `WeaponRuntime`/timeline/socket/history mechanisms, so Stage E adds no second abstraction over them.

The design follows the previously pinned source survey's explicit composition and input/query/visual separation, without importing source or dependencies: [shared foundation survey](OpenSourceSharedFoundationSurvey.md), [semantic plan Stage E](SharedFoundationSemanticExtensionPlan.md#p2-阶段-e-把战斗扩展点整理成可组合规则). This is a local extraction justified by the two real consumers, not a claim that the surveyed frameworks were integrated or benchmarked here.

## Playable opt-in content

### Portrait Survivor

- `SvConfig.CreateComposedPulseExample(false)`: wide-pulse data variant, radius 5, damage 20 × existing Might, two charges, sequential 120-tick recharge at 30 Hz, default stable history 128.
- `SvConfig.CreateComposedPulseExample(true)` (default): the same data plus independent `SvRepulseRule` outward knock of 0.9 through `SvHit.Knock`. Existing resolver divides this by `max(targetRadius × 2, 0.5)`; neither the renderer nor a particle moves targets.
- `SvGameBootstrap.CreateComposedPulseExample(config)` starts the ordinary portrait weapon horde with shared pulse/blink/attack/switch HUD, joystick, run/death/win/restart/menu, original natural characters and existing weapon/VFX fallbacks.
- Pulse is a committed nine-tick action. Charge is spent once at admission, damage is attempted only on tick 3, the first fixed tick at/after existing pose-101 contact 0.28 × 9. Nothing hits during windup. Radius query is centered on the authoritative post-movement hero at release; it is not a weapon-tip strike.
- Existing auto-weapon action is canceled/suppressed while that pulse runs. Busy pulse taps are rejected, not buffered. An accepted blink/equip intent cancels the pulse; damage interrupt or death also cancels it. Equipment requested directly through `WeaponRuntime` is covered. Cancel/whiff/queue exhaustion does not refund an already admitted action. Rejected admission spends nothing.
- Session pause freezes the accepted action; level-up flow also freezes its timeline/charge recharge. Resume continues once. Menu/restart clears level-scoped action, charge and history state. Existing legacy simultaneous pulse/blink behavior is unchanged; the new composition gives blink/equip priority.

### Landscape Brawler

- `BwComposedAbilityConfig.DataVariant`: content 2101, kick damage 22, heal base 18, kick recharge 84 ticks and heal recharge 360 ticks at 60 Hz; no earned bonus.
- `BwComposedAbilityConfig.Default`: content 2102, same data, plus independent local credit rule: each actual accepted unique player-kick hit earns 6 heal credit, capped at 18. Whiffs, duplicate/history-full contacts, weapon hits and enemy hits earn none.
- `BwMode.CreateComposedAbilityBelt(beltConfig, abilityConfig, out module)` / `BwGameBootstrap.CreateComposedAbilityBelt()` keep the ordinary belt waves/loot/win/loss/restart/menu and four mobile controls.
- Kick uses the existing attack clip, contact window, bone tip, depth and height checks. Damage tuning changes the local accepted damage amount, not reach or visual hitboxes.
- Heal admission spends one skill charge, then waits for pose 105 tick 9/18 (first fixed tick at/after its 0.45 contact). Existing direct damage resolves before the new Resolve/10 heal system; a same-tick hit can interrupt healing before settlement. Only the actual heal release consumes the earned bank. Canceled heal keeps the bank but does not refund its charge. Death/owner replacement/reset clears it.
- Equip, hit, KO, non-fighting flow and mismatched/canceled pose invalidate pending actions. Pause freezes the session; snapshot restore retains the saved owner/pulse rather than comparing an unsaved view revision. No saved healing result is replayed after its marker.

Both defaults target Android/iOS and use the existing orientation-specific safe-area HUD without changing global PlayerSettings. Keyboard mappings remain the existing per-game mappings. New factories are explicit alternatives; ordinary `CreateWeaponCombatExample` / `CreateWeaponBelt` are controls.

## Capacity, scheduling and ownership

- Survivor history is fixed `Targets × sizeof(EntityHandle)` (default 128 × 8 = 1,024 payload bytes), one singleton scope. The existing `Sv.Hits` queue capacity remains `Events × 2`; no new damage queue or overflow list exists. History/queue-full rejects newest candidates, records bounded per-release counts, and never retries the release on another tick.
- Survivor runs one synchronous grid traversal only on the release tick, after completing declared dependencies. The one writer does `HitHistory.Check` → successful `Sv.Hits.TryAdd` → `HitHistory.TryRecord`, without an intervening writer. This ordering is not a concurrent transaction. No Native history is mutated by presentation or a parallel job.
- Brawler keeps its existing fixed history capacity per fighter attack and its direct settlement. No queue is inserted merely for symmetry. Its additional resource is one fixed managed scalar state; credit cannot exceed the authored cap and counters saturate.
- World owns the level-scoped states. Survivor disposes its Native history once. Reset clears action/history/credit. All added hot-path state is preallocated and contains no growing collection, reflection, LINQ, callback payload or per-contact allocation.
- Old system registration/phase/order and access declarations remain unchanged on old paths. New Survivor input uses its explicit serial barrier; pulse replaces only the opt-in Collision/7 pulse implementation. New Brawler appends only Resolve/10 heal settlement. No changes to Session, TickPipeline or WorldComposer are part of E.
- No CPU/GPU or mobile power improvement is claimed. Added admission costs are bounded; current-thread warmed allocation is tested with retained-array and empty controls. Native whole-frame allocation and physical phone sustained CPU/GPU/thermal/memory budgets remain acceptance gates under the existing thresholds.

## Save identity

The new states implement reset and explicit v1 snapshot readers with exact frozen content checks. Their raw snapshots are only same-configuration/same-runtime checkpoints, not portable long-term save files.

`SvComposedPulseSave` and `BwComposedAbilitySave` each list every ordered table/column/resource/system through the Stage D schema API, with distinct mode/contract IDs. Changed input/hero/reward/fighter/combat semantics are explicitly versioned. Content identity uses actual frozen runtime values, capacities, slot definitions, weapon rules and ability parameters; visual identity is separate. Existing `SvWeaponSave` and `BwWeaponSave` still reject the extra authoritative resource, rather than relabeling an old descriptor.

Only per-game cold content/visual/raw-compatibility writers were extracted into internal methods. The old recipes keep their exact schema arrays and strict coverage. Before-extraction baseline bytes are captured using the independent unchanged D `56a13de` assemblies; after-extraction output is compared directly and has frozen fingerprint/hash tests. This avoids a circular test where both sides call the new helper.

## Acceptance and reproducibility

New EditMode fixtures:

- `AbilityAdmissionTests`: pure reason/result mapping
- `SvComposedPulseTests`: exact release/damage/knock, no windup damage, bounded queue/history, full queue does not record, whiff/busy/spend, blink/equip/hurt/death/menu, pause/level-up/restore/reset, old factory and allocation controls
- `BwComposedAbilityTests`: actual kick/heal/credit, capacity/duplicate/whiff, interruption/equip/death/owner/reset/pause/restore and old factory controls
- `SvComposedPulseSaveTests` / `BwComposedAbilitySaveTests`: own complete recipe, old-recipe rejection, in-flight action continuation against uninterrupted control, incompatible content rejects before mutation
- `SvWeaponSaveWriterCompatibilityTests` / `BwWeaponSaveWriterCompatibilityTests`: .NET-only frozen D descriptor/envelope byte identity; not an assertion of native ABI equivalence

Native-only fixtures are separate from Stage F's existing weapon fixtures:

- `SvComposedPulseGameplayTests`: four data/rule × render-tier cases, actual mobile HUD pointer and simulation release, HP/knock/history assertions, existing coordinated pulse pose, rigid held-weapon socket invariant, read-only rendering/same-tick restore, actual selected particle fallback
- `BwComposedAbilityGameplayTests`: four data/rule × render-tier cases, actual HUD → kick bone contact → earned credit → heal pose marker, exact HP, equipment socket after recovery, read-only/snapshot controls
- Both native workflows enable `SPF_ABILITY_GAMEPLAY_SEQUENCE=1` (the Docker wrapper forwards it); local runs can set the same variable. This produces two Survivor and two Brawler bounded automatic-clock sequences. `BufferedFrameCapture` records real acquisition timestamps; raw acquisition precedes JPEG95 review encoding. `ability.csv` records action/charge/effect state. Use `Tools/ci/encode_capture.py CAPTURE_DIRECTORY NEW_OUTPUT.mp4` for verified microsecond VFR timestamps and inspect at normal 1×; do not impose a fabricated constant acquisition rate, interpolate frames or treat stills as continuous proof.
- Expected still prefixes: `ability-horde-{wide|repulse}-{gpu|fallback}`, `ability-belt-{kick-contact|heal-marker|weapon-contact}-{data|credit}-{gpu|fallback}`. Sequence prefixes: `ability-horde-live-repulse-{gpu|fallback}`, `ability-belt-live-credit-{gpu|fallback}`.

Status is intentionally not inferred from the presence of these tests. See the [exact-source validation record](validation/ComposedAbilityRules-20261007.md) for passed, failed and not-run stages. Native compilation is not Unity/Burst/graphics execution; desktop graphics is not physical Android/iOS touch, backend, heating, battery or sustained-frame-time validation.

### Evidence budget

The additional continuous evidence is bounded to 420 real frames total: 2 × 90 portrait 360×640 and 2 × 120 landscape 640×360. Each capture is disposed before the next: raw readback storage peaks at 110,592,000 bytes for a Brawler sequence, below the existing 128MiB per-buffer limit. Image encoding happens afterwards. Sixteen expected new PNG stills and their metadata are separate. The planning allowance for additional stored JPEG95/still/CSV evidence is 64MiB (four 16MiB parts); actual compressed sizes and acquisition intervals remain pending native capture and must be recorded, not inferred from pixel counts. Existing 16MiB-part/max32 packaging limits, whole-suite captures and allocation thresholds are unchanged; packaging must fail rather than drop evidence if the 512MiB total is exceeded. Review must restore every hashed part and check all four sequences at measured normal 1× timing.
