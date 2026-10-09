# Continuous authored blade arc

This change targets the user-rejected tucked-elbow blade slash only. The accepted sword thrust, simulation hit clock, content IDs, damage and fixed grip geometry remain unchanged.

## Motion design

The working upper arm and forearm use continuous authored angular curves. Forward kinematics of the fixed .43/.42-m links derives the free-space hand path; the loaded upper-arm key is above horizontal so the elbow opens away from the ribs. Torso loading shares the action clock. The final solve accounts for the actual moving body and preserves the same elbow branch. The obsolete .22-m horizontal shoulder retreat is removed from the horizontal cut; vertical aim retains its existing bounded clearance correction.

The exact canonical contact and the existing no-downward-droop recovery contract remain. There is no contact hold. Outgoing action/cancellation and repeated pulses use a single root-relative residual transition clock, continuing toward the live pose rather than freezing a world-space hand target. Final palm position and orientation still own the rigid blade attachment.

No per-character Animator, managed allocation, dynamic collection or gameplay mutation is introduced. Both rendering tiers consume the same current final chain. Physical Android/iOS performance and touch interruption validation remain unverified.

## Validation order

First run focused blade/weapon EditMode checks and the existing normal-speed native closeup sequence, which already covers repeated attack, movement, turn, equipment and cancellation on GPU/DataTexture. This is a visual checkpoint, not full regression. Preserve acquisition timestamps; encode only after capture. Inspect the actual video before declaring naturalness fixed. Then remove the branch-only SPF_BLADE_REVIEW workflow opt-in and run full native/harness suites on the final source.

Existing strict hand/contact, wrist, no-droop, continuity and other-weapon tests remain. Added tests require the final elbow, not just the hand target, to rise visibly above the shoulder with fixed limb lengths. Results are pending on this working revision. Prior ef332695 results do not validate it.

Reference: Meshy right-hand sword slash preview, https://www.meshy.ai/zh/animation-library/fighting/punching/right-hand-sword-slash . The inspected preview demonstrates whole-arm lift and torso participation but contains no visible sword, so it cannot establish grip stability. No claim of Mixamo viewing or imported motion assets is made.

## First visual checkpoint status

Local .NET compilation passed. Latest focused turn/hit/transition selection: 27 tests, 17 passed and 10 failed. All seven new transition cases passed, including whole-chain zero-time cancellation/restart and live-root ownership. Remaining failures are existing 15 m/s hand-speed / 20 m/s elbow-speed gates during the central fold of a loaded 180-degree reversal (observed final hand about 18–24 m/s). Those thresholds are unchanged. The intermediate authored joint keys remove a long hand detour; the turn-fold mapping still needs correction. This checkpoint is intentionally incomplete and is published only for early native visual inspection. No acceptance or merge is claimed.

## Corrected trajectory checkpoint

The central-fold spike was caused by composing two eased clocks over a moving arm-ownership target. Ownership now travels across the actual remaining anticipation with a continuous near-constant-speed middle and bounded endpoint ramps. The outgoing pose/velocity remains the start; exact canonical contact remains the end. No new elbow branch, global slowdown or hit-time change is used.

The larger arm needed coordinated forearm timing rather than the old hand-only braking splice. The forearm now starts its descent earlier, while the upper arm uses continuous-acceleration speed ramps. Loaded upper-arm key is .515 rad; final elbow height is tested above the shoulder, not inferred from the key. A .035-m maximum continuous loading weight shift follows the already-bridged torso pose and the normal planted-leg solve. Steep projected aims blend toward their existing compact directional arc; the side-on blade retains the complete large arc. The full rigid-palm attachment and .85-rad wrist bound remain unchanged.

Local exact-source focused check: **126 passed, zero failed/skipped** using .NET 8.0.425 and the repository Unity stubs, including all blade, weapon-motion and humanoid-action tests. Tested schedules include 30/60/120 Hz and irregular presentation steps. Maximum logged held-turn hand speed was 13.973758 m/s against the unchanged <15 gate; maximum logged elbow speed was 11.595576 m/s against <20. All three moving-action foot-plant/joint-speed cases passed, as did the raised-elbow silhouette, nine new transition cases, no-droop/contact/wrist constraints and warmed allocation checks. This is not native/Burst or physical-device certification; a fresh native checkpoint and the final full suites remain pending.

Continuity scope: authored curve joins and the added transition endpoint tests establish the tested C0/C1 conditions. Outgoing velocities are bounded finite differences, not a proof of analytic final-FK C1 for arbitrary phase discontinuities. Event frames retain the existing last-pose convention; clamped extreme turn momentum and arbitrary near-contact phase jumps remain outside a universal low-speed guarantee. The real normal-speed footage, not these metrics alone, determines naturalness acceptance.

## Native corrected checkpoint: 3f9e6f7

[Native run 37881878799](https://github.com/karosLi/SPGameFoundation/actions/runs/37881878799) completed successfully on Unity 2022.3.62f2/Mac: **127 EditMode and 2 graphics PlayMode tests passed, zero failed/skipped**. The full .NET harness run [37881878736](https://github.com/karosLi/SPGameFoundation/actions/runs/37881878736) also completed successfully. These results cover exactly `3f9e6f75d1e91b94782112b19bbdc675a8749e27`.

Both 128-frame, 256×256 isolated GPU/DataTexture sequences were restored with manifest/hash verification and encoded at their measured acquisition timing: 4.8281 / 4.9384 seconds, plus the encoder's documented final display hold. No interpolated frames or invented cadence were used. Source-frame review confirms shoulder/elbow lift, an extended working arm and a turn across the front without a floorward detour. Grip error remains exactly zero in both captures. During pre-equip attack recovery, minimum tip height was 1.35204 / 1.35101 above root. Low newly equipped idle poses are equipment lowering, not post-contact attack droop.

Compared with the incomplete first checkpoint, the previously failing 120-Hz reversal hand speed fell from 24.22389 to 13.74106 m/s at the same <15 threshold. Across all tested held-turn combinations the maximum was 13.97376 m/s, elbow 11.59559 m/s. The large arc is retained; the peak hand height is slightly lower than the over-fast first draft, approximately 2.2 rather than 2.27 model units. This is not a claim of identical frame phase between independently sampled recordings.

Review limitation: source frames/contact sheets were inspected; continuous playback was not reviewed by the assistant. Naturalness is not declared accepted on test results alone. Equipment lowering is intentional, and physical Android/iOS remain untested.

The branch-only narrow-test workflow opt-in is now removed. Production animation and test sources remain byte-identical to the corrected checkpoint for the ensuing full native regression. Its terminal result is recorded by that commit's GitHub Actions checks; no full-native result is claimed in advance here.
