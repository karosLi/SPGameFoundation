# Blade and sword choreography correction

The delivered `e0233871a47ef5dc0ef505fe911acc5da2d4cc7d` footage was rejected by the user as unnatural. Its passing numerical/native checks did not establish visual acceptance. This bounded presentation correction starts from the equivalent local `b2013a8` tree `dd6c05eff7b633bc82410a0ff168bb9c23ba9a56`. **Naturalness remains unaccepted pending fresh native, same-camera 1× review and the user's judgment.**

## Actual baseline and red controls

Inspection used the restored actual GPU/fallback JPEG95 frames and their final-FK trace, not generated reference art. In the belt sword sequence, f071→074 drops the hand about 0.672 model units while advancing only 0.090. The shoulder advances about 0.19 and the elbow remains deeply bent (about 104°→99°). The blade's f045 grip `(0.432,1.008)`, angle −71.5° and full grip-to-tip length 1.07 reconstruct a tip at about −0.007 relative to ground. The held-attack recording supplies complete repeated blade cycles; the belt clip alone interrupts recovery with equip.

Sword windup also saturates the ±0.85-radian wrist limit, leaving an approximately 8° hand/weapon orientation mismatch. Its generic secondary socket maps to sword-art pixel `(1,39)` outside the drawn handle, whose centerline is y=48 and rear handle starts near x=13. No inspected elbow-branch flip, wrist wrap or changing blade length was found. Torso motion and locomotion were already present.

Nine family-specific tests were run against unchanged baseline production sources before editing. All nine failed (three controls at 30/60/120 Hz):

| Control | Exact baseline math result | Required behavior |
|---|---:|---|
| Sword chamber | 0.120 forward from idle | Rearward at least 0.12 |
| Sword chamber→contact | 0.100 forward, 0.620 vertical; elbow closes 0.0255–0.0270 rad | Forward ≥0.32, vertical <0.16, elbow opens >0.28 rad |
| Blade full-cycle tip | Minimum −0.00944; follow angle −1.25593 rad | Tip >0.30 above ground; diagonal follow-through −0.72…−0.20 rad |
| Sword support palm | Art pixel approximately `(1,39)` | Rear handle x=20…34, centerline y=43…53 |

The full baseline failure log/TRX is retained in the integration evidence bundle as `blade-sword-choreography/baseline-red.*`. These are final-FK probes of the exact source, distinct from the approximate actual-frame measurements above.

## Bounded change and ownership

`WeaponMotion.Sample` authors three connected phases for each family using the existing C1 Hermite trajectory and the unchanged authoritative markers:

- Blade: chest loads before the high hand key; hand and blade cut diagonally through canonical contact, finish slightly forward with a shallower angle, then return to guard. Follow-through is retained. Its joint grip/angle authoring, rather than a lifted grip alone, keeps the full blade clear of the boots and ground.
- Sword: rearward chamber near the existing thrust line, forward elbow extension, small forward settle, then retraction. The chest loads earlier than the hand. The former shoulder-height lift criterion is inappropriate for this thrust.
- During an actual blade/sword action, the generic punch chest lean no longer adds a second attack rotation on top of the weapon body keys. An idle held weapon still preserves generic kick/punch body ownership. This leaves the weapon family responsible for its own chest sequence; locomotion, support/pelvis corrections, skill priority and hit/death composition remain in the existing pipeline.
- The sword visual's rear hand anchor is `(24,48)` relative to primary pivot `(46,48)` and 198-pixel grip-to-tip length. It scales/rotates with the actual sword art. Authoritative profile offsets, hit/release sockets and saved content fingerprints are unchanged. Staff's handle and bow/string mappings are unchanged; blade retains its free balancing arm.

The wrist limit and final attachment path are retained. Reauthoring the melee trajectories keeps the final wrist aligned with the blade during the reviewed horizontal attacks instead of silently accepting an angle left over after wrist clamping. This is not a claim that arbitrary extreme aims or custom weapon proportions can always achieve exact anatomical wrist alignment.

No authoritative weapon profiles, action timing, cooldown, damage, collision, release geometry, SmoothAttackV1 movement or raw save layout are modified. No rig/atlas redesign, extra pose state, buffer growth, allocation or new animation system is introduced.

## Local verification and native gate

The universal high-hand test is replaced for melee by `BladeSwordChoreographyTests`. Staff/bow retain their existing lift checks. New controls inspect the final FK, normalizing root, scale and facing; sword extension compares the hand and shoulder and measures the actual elbow joint. Blade clearance uses `WeaponAttachmentSample.Tip`, including the complete blade length and its final rendered direction, and checks wrist/blade alignment. Sword support is projected back into actual art pixels.

The focused suite passed **104 tests, zero failures/skips**, including default 30/60 Hz tick profiles, three roles, both facings, stationary/moving attacks and 30/60/120 Hz render steps. Candidate ranges:

| Measurement | Candidate |
|---|---:|
| Sword rearward chamber | 0.20187–0.22000 model units |
| Sword chamber→contact forward travel | 0.43615–0.44000 |
| Sword chamber→contact vertical travel | 0.04170–0.05000 |
| Sword elbow opening | 0.52091–0.67053 rad |
| Blade minimum full-cycle tip clearance | 0.58528–0.58552 |
| Blade shallow follow angle | −0.59376 rad |
| Both melee wrist/blade alignment | <0.01 rad throughout tested horizontal cycles |

A separate 18-case probe against the frozen DLLs checks blade/sword × 30/60/120 Hz × Recovery→Idle, Windup→Idle and Windup→Hit. `Attack` is directly sampled rather than faded. Every boundary produces zero restored generic punch lean and zero discontinuity in weapon body contribution; Idle/cancel torso deltas are zero. Hit deltas are only the existing hit response (0.10829/0.06220/0.03343 rad). The idle-equipped Kick regressions retain the original generic body contribution. No source change followed this probe.

Existing contact/socket, continuity, contact velocity, support, interrupt/equip, skill overlap, extreme aim and quality independence gates retain their thresholds. The calibrated warmed zero-allocation test now covers each of blade, sword, staff and bow rather than bow alone. After source freeze, one complete aggregate built all 78 projects with zero warnings/errors and ran 1,625 passing tests, zero failures. The 27 original TRX files also contain six pre-existing Explicit `NotExecuted` diagnostics and fifteen empty PlayMode harness files. Those empty files provide no native coverage. SDK 8.0.425 used the pinned cached dependencies (NuGet vulnerability audit disabled for this build); no dependency version changed. The [machine-readable verification record](validation/BladeSwordChoreography-20261008.json) preserves source hashes, outcomes and separate acceptance status. These are .NET-stub results, not Unity/Burst/graphics or visual acceptance.

Native acceptance must keep the existing input schedules, root speed, camera, resolution, tiers and measured acquisition timestamps. Use `e0233871` as the actual before capture, including the complete held-attack blade cycles. Acquire the corrected exact commit through the same eight matched scenarios and four full-input movement scenarios. Encode after acquisition with no generated/interpolated frames. Compare at normal 1× first; separately label any slowed diagnostic replay. Review shoulder→elbow→wrist coordination, the full tip arc, hilt contact, compact recovery, walking support and repeated/equipped/interrupted transitions, especially the rear support hand during blade→sword equip and immediate attack. Preserve failures and acquisition limits. Historical e023387 native success does not cover the correction. Unity/Burst/graphics, human normal-speed judgment and physical Android/iOS remain separate gates.
