# Humanoid arm motion and walking cadence

This isolated presentation change starts from local `c0c2cc6e3ec7f41595ba390c98e753552e389d14` (the frozen integration source). It responds to the 2026-10-08 report that attacking arms do not lift and walking looks too slow. Native evidence for the new source is **pending**; numerical results do not approve naturalness.

## Confirmed cause and scope

The live character path is `GameplayCharacterPresenter` → `GameplayCharacterMotion.CorrectContacts` → `WeaponMotion.ApplyArms` → final FK. It is the 14-bone procedural cutout rig, not the independent three-bone weighted BAT demo. The final two-bone IK owns the equipped primary hand. Changing an earlier arm angle alone is consequently ineffective: the final hand target must carry the authored attack movement.

A baseline diagnostic compiled the three motion files directly from the starting commit, warmed the pose at 120 Hz, then sampled a one-second normalized action at 240 Hz with unit scale and horizontal aim. The following are final FK coordinates, after arm IK:

| Weapon family | Idle hand Y → wind-up hand Y | Idle elbow Y → wind-up elbow Y |
|---|---:|---:|
| Blade / Slash | 1.330 → 1.610 | 1.256 → 1.415 |
| Sword / Thrust | 1.340 → 1.410 | 1.252 → 1.312 |
| Staff / Cast | 1.240 → 1.260 | 1.265 → 1.292 |
| Bow / Draw | 1.455 → 1.434 | 1.250 → 1.231 |

The idle shoulder was at Y 1.666. Sword/staff hand elevation was small, and the bow hand lowered slightly. The blade already had an arc; the report must not be generalized to “every weapon had no arm movement.” The hand bone also inherited the forearm rotation without a separate wrist/grip correction.

The repair authors hand-space wind-up, contact, follow-through and recovery, which the final IK now follows. The blade cuts from a high guard; the sword chambers for a forward thrust; the staff gathers and directs a two-hand cast; the bow raises then settles for release. The existing Hermite curves preserve continuous velocity through contact. The blade reaches its preparation key earlier, leaving enough of its unchanged short action for the larger downswing. Grip rotation is corrected by a bounded ±0.85-radian wrist articulation; an extreme weapon angle can retain residual grip-angle error rather than breaking the wrist limit. Correction eases toward neutral near an impossible rear-facing grip instead of jumping between opposite wrist limits. The primary grip remains attached, and secondary IK follows the solved hand. Bounded side arcs and chest/scapular motion keep projected vertical aims away from shoulder singularities; each arc is zero at contact/release. The bow drawing hand follows through at its rearward anchor and recovers independently of the already-released string. Existing canonical release heights are preserved; this remains a stylized 2D pose, not a claim of full anatomical archery reproduction. Ranged alignment uses the release marker for both staff and bow, including content where contact and release differ.

Unarmed action anticipation raises the working hand with a continuous action envelope rather than an absolute strike value that falls to zero between preparation and impact; the free arm guards/counterbalances, with bounded hit protection and death compression in pelvis, chest, head and hands. Skill and two-hand ownership remain layered after the shared locomotion. No new enemy attack is fabricated: Survivor enemies still have no authoritative attack timeline.

## Walking speed audit

The isolated animation commit leaves authoritative speeds unchanged: Belt player speed is 3.2 world units/second and the base Horde hero speed is 5. Belt and Horde adapters read actual final displacement; that commit changes visual stride and cycle period only. A subsequent runtime audit found that the old Belt Attack path generally read no stick input and therefore multiplied zero by 0.22; that constant was not a measured continuous attack speed. The separately authorized [SmoothAttackV1 gameplay change](BeltAttackMobility.md) fixes that lock and deliberately changes committed player travel and the replay content domain. Run thresholds, run strides/periods, attack durations, hit windows, cooldowns, damage, projectile rules and raw saved field layout remain unchanged.

| Role | Old walk stride / maximum period | New walk stride / maximum period |
|---|---:|---:|
| Hero | 1.40 / 1.08 s | 1.10 / 0.82 s |
| Agile | 1.25 / 0.95 s | 0.98 / 0.72 s |
| Heavy | 1.65 / 1.18 s | 1.18 / 0.94 s |

The existing reach constraint can further shorten a period. The 0.44-second walk floor is preserved; it does **not** explain the old low-input Belt walk. That capture uses input (0.27, 0.18), approximately (0.864, 0.3168) projected world velocity and scale 0.9, hence about 1.0225 model units/second. Its former 1.08-second maximum period yields 0.926 cycles/second (1.852 steps/second); the new 0.82-second period yields 1.220 cycles/second (2.439 steps/second). The displacement is the same. Full-stick Belt horizontal movement normally enters Run and is a separate case.

World-space plants, alternating support, walk double support, lift envelope, movement classifier and run flight ownership are retained. Faster cycles shorten the landing lead and stride at unchanged root speed. Existing 30/60/120 Hz contact/start/stop/turn/reversal/ground-depth tests remain strict. Heavy/agile motion keeps its wider/narrower stance, different support shift, arm swing, body sway, recoil and cadence.

## Evidence and review gates

The native `weapon-belt-live-*` and `weapon-horde-live-*` sequences keep their original input schedules, cameras, dimensions and target 30 Hz acquisition cadence. The Belt fixture now explicitly selects Legacy mobility to preserve its historical root-speed control; the user-facing bootstrap selects SmoothAttackV1 and its actual movement is separately captured by [the full-input movement fixture](BeltMovementSpeedCapture.md). A bounded, preallocated readback annotation now records final working shoulder/elbow/hand, joint angles and weapon phase in `gait.csv`. Encoding still happens after capture, and `acquisition.csv`/`acquisition.ffconcat` preserve measured timing. The existing `grounded-*-live-*` footage likewise retains its schedule. No frames are generated or interpolated.

Before/after comparison must identify both source commits and confirm unchanged capture/movement source. Play each at normal 1× using measured acquisition intervals, with the same camera and root speed. Historical baseline video is acceptable only for those unchanged inputs/capture paths; a historical green result is not a new-source native pass.

| Role/state | Required review | Current status |
|---|---|---|
| Hero, agile, heavy idle/walk/run | Alternating contact, start/stop/reverse/turn, depth travel, arm opposition, pelvis/head stability | Automated regressions; native candidate review pending |
| Hero blade/sword/staff/bow | Visible shoulder/elbow/wrist anticipation, contact/release alignment, distinct direction, follow-through/recovery | Final-FK regressions; native candidate review pending |
| Belt enemy punch/kick | Free-arm balance, authoritative target, planted support or real jump | Shared change; actual gameplay review pending |
| Hit/death/recovery | No rigid arms during recoil/fall, no weapon/skill ownership regression | Shared change; actual gameplay review pending |
| Equip/cancel/interrupt, moving attack, skill overlap | No pop, detached support grip, stale action or foot sliding | Existing regressions retained; native candidate review pending |

Focused numerical verification passed 92 cases: 23 new regressions, 66 existing motion/gait/profile cases, and three external diagnostic audits. The normal full harness excludes those three audits. Final horizontal anticipation FK hand/elbow heights (model units) are blade 1.83039/1.46365, sword 1.87027/1.46436, staff 1.87997/1.49091 and bow 1.73432/1.36354. Unlike the old low hand curves, all four include measurable upper-arm elevation; within the new action, blade/sword/staff elbows rise over 0.20 model units and bow over 0.10 from their corresponding idle pose. These are within-action rises, not increments over each old peak.

Across real default action durations, both facings, and horizontal/±45°/±90° aim, maximum per-render joint displacement at 30/60/120 Hz was 0.361795/0.182973/0.096724 model units; maximum angular change was 0.812704/0.422140/0.219424 radians. No continuity threshold was relaxed. These are numerical bounds, not a human assessment of the footage.

At model speed 0.5, measured cycles/second changed hero 0.92594→1.21950, agile 1.05263→1.38887 and heavy 0.84745→1.06384. At model speed 1.5, they changed 1.16249→1.36364, 1.20002→1.53064 and 1.16249→1.27120 respectively. Step rate is twice cycle rate. Actual landing counts also increased along identical root routes, with alternating support and zero double-air walking samples. Faster-walk gains depend on speed and role; they are not one global multiplier.

The initial normal .NET harness reported **1,590 executed/passed tests and zero failures** across all 12 discovered EditMode test assemblies. Its console aggregate printed zero skipped; that display does not establish the absence of explicit opt-in cases (see the later TRX reconciliation below). All 78 generated projects built successfully using SDK 8.0.425 with serial MSBuild. The 132 NU1900 warnings concern an unavailable NuGet vulnerability index; dependency versions were not changed. An initial parallel aggregate restore exited without a diagnostic; the serial build and full test run then completed successfully. No test or allocation threshold was relaxed, and the direct release-marker tests use valid ranged active windows.

The harness build uses Unity stubs, and native PlayMode bodies remain excluded there. This is not a Unity/Burst/graphics pass. Native exact-commit capture, 1× before/after review, and final role/state/weapon acceptance remain pending. Physical Android/iOS thermal, sustained frame-time, GPU and touch validation remain external gates. No mobile performance improvement is inferred from these desktop math checks.

## Technique references and bounded adaptation

- [Unity Two Bone IK](https://docs.unity3d.com/Packages/com.unity.animation.rigging@1.2/manual/constraints/TwoBoneIKConstraint.html): end-effector targets, joint hint direction, position/rotation and overall weights have separate responsibilities. Here this informs the existing analytic solver and phase-authored target; it does not introduce Animation Rigging, Animator instances or a new graph.
- [Unity blend trees](https://docs.unity3d.com/2022.3/Documentation/Manual/class-BlendTree.html): blend compatible motion phases. Here the existing coupled ground contacts remain the clock for body and arm opposition.

The implementation is fixed-capacity math over the existing pose buffers, with one additional visual free-guard weight per motion slot to retain cancellation continuity. New temporary collections, per-character objects, runtime state machines, physics features and authoritative clocks are out of scope.

## First native candidate result

Exact candidate `b07364ff6357c57f5edaf9c9f8d55cb4f1bc5f36`, [run 37720722788](https://github.com/karosLi/SPGameFoundation/actions/runs/37720722788), stopped at script compilation in both Editor invocations: the new final-FK annotation in `SvWeaponGameplayTests` referenced `WeaponViewState` without importing `SPF.Contracts.Weapons`. The bounded artifact contains both complete logs, whose archive and member hashes were verified; there is no test XML or new footage from this run. Adding the missing namespace is a test-source correction only. It does not change runtime motion, input, capture schedules or assertions. The corrected exact head still requires native execution.

The correction was checked across all five new/modified test source files in four compile groups matching their actual asmdef direct references, with C# 9, `UNITY_INCLUDE_TESTS`/`UNITY_EDITOR`, and without `SPF_DOTNET_HARNESS`. The PlayMode groups included UTF 1.1.33's original `UnityTestAttribute` and combinatorial-strategy sources. They explicitly referenced the official `com.unity.ext.nunit` 1.0.6 NUnit 3.5 DLL; all four groups compiled with zero errors. Each group reported an MSB3277 warning because the existing harness stubs indirectly name host NUnit 3.13; the explicit official 3.5 DLL was selected and its copied hash was verified. UnityEngine/UnityEditor reference DLLs are unavailable in this recovery environment, so this is a bounded source/API check using engine/package stubs, with explicit clock, buffered-capture and camera-projection shims. It is not real Unity assembly compilation or a native test pass.

The same `b07364ff` hosted .NET run [37720722816](https://github.com/karosLi/SPGameFoundation/actions/runs/37720722816) succeeded with **1,613 Passed, zero Failed and six explicit NotExecuted** entries in the original TRX, for 1,619 inventoried cases. These six opt-in diagnostics are separately printed by the runner even though the VSTest aggregate says zero skipped. Fifteen PlayMode harness TRX files contain zero tests; they do not provide native coverage. All 27 TRX members were recovered and checked. A scratch-only negative compilation check removing the corrected using reproduced the original CS0246; current source passed.
