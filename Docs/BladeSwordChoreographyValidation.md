# Blade and sword choreography correction

The delivered `e0233871a47ef5dc0ef505fe911acc5da2d4cc7d` footage was rejected by the user as unnatural. Its passing numerical/native checks did not establish visual acceptance. This bounded presentation correction starts from the equivalent local `b2013a8` tree `dd6c05eff7b633bc82410a0ff168bb9c23ba9a56`. **Fresh native execution and chronological frame review are complete on d2cc1f9. After viewing the new footage, the user accepted the sword thrust and rejected the blade's downward cut/contact-to-recovery coordination. The blade remains unaccepted.**

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

The native acceptance protocol keeps the existing input schedules, root speed, camera, resolution, tiers and measured acquisition timestamps. Use `e0233871` as the actual before capture, including the complete held-attack blade cycles. Acquire the corrected exact commit through the same eight matched scenarios and four full-input movement scenarios. Encode after acquisition with no generated/interpolated frames. Compare at normal 1× first; separately label any slowed diagnostic replay. Review shoulder→elbow→wrist coordination, the full tip arc, hilt contact, compact recovery, walking support and repeated/equipped/interrupted transitions, especially the rear support hand during blade→sword equip and immediate attack. Preserve failures and acquisition limits. Historical e023387 native success does not cover the correction. The new native result below supplies correction-specific Unity/Burst/graphics evidence; human normal-speed judgment and physical Android/iOS remain separate gates.


## Exact native result and actual-frame review — 2026-10-08

Published source `d2cc1f976a517bba3d0b9fb05f81412d8c84f996`, tree `2dda7c646e7a212169d27f58a8f362e829d87083`, passed the [Mac Unity run 37741504324](https://github.com/karosLi/SPGameFoundation/actions/runs/37741504324) and [.NET run 37741504133](https://github.com/karosLi/SPGameFoundation/actions/runs/37741504133), both attempt 1. Native EditMode passed **1,664**, failed **0**, skipped **5**; PlayMode passed **171**, failed **0**, skipped **1**. The five EditMode skips are the three existing explicit diagnostics and two graphics-only cases in the no-graphics process. The one PlayMode skip is the existing explicit allocation-callstack diagnostic. The .NET TRX files contain **1,625 passes**, zero failures and six existing Explicit `NotExecuted` diagnostics; the harness console's aggregate skipped count does not include those six.

Both Editor processes exited 0 with no crash retry or Burst-disabled fallback. The actual Burst control remains managed/Run/Schedule **0/1/1**, with physics executing natively for all 60 warmup and 300 measured steps. Physics mean **0.396 ms**, worst **0.661 ms**, retains its existing mean <4 ms desktop gate. Moving-attack logic and all four warmed weapon/FK probes pass their unchanged allocation gates with retained positive/empty controls. These current-thread probes do not measure all native/driver/GPU work.

This is not a universal zero-allocation claim. Survivor DataTexture autoplay records **492 steady bytes in three frames**, within the unchanged ≤1,024 bytes/3 seconds budget, plus **1,476 bytes** near existing level-up/flow UI. GPU autoplay has zero steady bytes, with 820 UI-near bytes. The separate 240-frame render-only stress windows have zero managed allocation bytes in both tiers; the original Story window records 0/30 allocating frames. These scopes remain separate.

All **31 parts**, **2,823 archive members** and **509,695,261 archive bytes** were restored and verified against wrapper, part, ZIP and member hashes. Archive SHA-256: `121a56f81cc44ccfc99b4a4ed1ceca4a9ae5f4ad83aa94f86a6b27b2af11dd55`. All twelve requested capture directories are complete: eight directories contain 1,000 JPEG95 frames and four movement directories contain 200 PNG frames. Use [CI evidence recovery](CiEvidence.md) for the run's original bounded artifacts.

The actual GPU and DataTexture `weapon-belt-live-*` action/switch windows and all 50 frames of each `belt-movement-held-attack-*` sequence were inspected in chronological order:

- The new sword chambers backward and extends forward. GPU f074→077 moves the hand about **0.469** model units forward and **0.057** vertically, with shoulder travel about **0.044** and elbow bend opening **126.9°→84.5°**. The prior e023 f071→074 window mainly dropped 0.672. These are phase-near sampled windows, not exactly synchronized contact instants.
- The blade's sampled minimum tip clearance is about **0.591** GPU / **0.618** DataTexture model units, versus approximately −0.007 in the old sequence. The complete held-attack cycles retain anticipation, diagonal cut, follow-through and return while stepping continues; the blade no longer hangs by the boots.
- Blade→sword equip and immediate attack show no gross detachment, sampled elbow flip or obvious single-frame weapon teleport. Tiny overlapping palms, hilt, torso and hit flashes prevent a precise finger-contact or subpixel-sliding verdict. The mathematical grip controls are distinct evidence.

Normal 1× deliverables preserve source timestamps without interpolation or dropped source frames; one repeated last frame preserves its final hold. Separately labelled 0.25× diagnostic versions multiply those times by four. Normal PTS rounding error is at most 0.0005 ms. The new weapon sequences were acquired at approximately **22.44/22.80 Hz** and held-attack at **21.47/21.69 Hz**, GPU/DataTexture respectively, at 640×360. This is Mac Unity capture, not a 60 fps or physical-phone claim. Source frames and metadata remain under `Artifacts/Screenshots/WeaponMotion/` in the verified run.

The finding is a visible correction of the two reported trajectory defects. Sword still uses the gameplay-defined waist-height thrust, and both attacks retain a regular two-dimensional cutout rhythm with limited chest/hip/footwork variation. Chronological pixel inspection and validated 1× files do not constitute human live-play or subjective normal-speed acceptance. After receiving the actual new clips, the user said the forward thrust is fine and should be retained, but the downward blade cut remains uncoordinated and appears as if the weapon may drop after contact. This is a new explicit blade rejection despite the corrected ground clearance. Keep the sword unchanged; research primary-source motion/rig guidance and inspect the actual contact-to-follow-through frames before further blade authoring. That research and redesign, unexamined moving/reversed/arbitrary-aim edge cases, and physical Android/iOS remain open.
