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
