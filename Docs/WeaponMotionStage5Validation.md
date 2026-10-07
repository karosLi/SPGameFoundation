# Weapon and motion checkpoint: exact-head native validation

## Verified source and runs

The validated source is remote commit `52c1ec8b6f7827336371fd3f415c6627539b6d5a` on `dot/weapon-motion-stage5`, equivalent to local `1c9731a8410915effc824a40eedf5764d56aa3b3`. Both trees are `1c50014859c78ecace08e25197b11e4975080250`. The remote history was published without force or changes to the user's original branch.

- [Native Mac Unity run 37568026818](https://github.com/karosLi/SPGameFoundation/actions/runs/37568026818): EditMode 878 passed, 0 failed, 5 skipped; PlayMode 141 passed, 0 failed, 1 skipped. All workflow steps, including bounded evidence upload, succeeded.
- [NET harness run 37568026798](https://github.com/karosLi/SPGameFoundation/actions/runs/37568026798): 848 logic tests passed, no failures. Stub results are not native engine or graphics evidence.
- Burst 1.8.27 was enabled on the Mac runner. The diagnostic control was managed=0/Run=1/Schedule=1; the actual physics job reported Burst in 60/60 warmup and 300/300 measured steps. No performance or allocation threshold was relaxed.

The four native artifact parts restore 710 files; the reconstructed ZIP SHA-256 is `081c648c1fae96c633b810204bd56a5904ca8ac9f0d7a99b24451b211149b390`. Use the checked-in bounded evidence restore tool described in [CiEvidence.md](CiEvidence.md).

## Included behavior

The full regression includes authoritative blade, sword, staff and bow actions; fixed-tick equip/charge/release; real Brawler and Survivor gameplay integration; hand/socket constraints; continuous locomotion and upper-body weapon/skill layers; role-dependent hit/death presentation; compact mixed actor streams; real GPU compute particle state and CPU/data-texture fallback; HUD click/hold/aim/cancel, pause/rebind and native rendering checks.

The formerly failing belt rebind fixtures now pass on both production tiers. Fresh held input is delivered after the resume boundary and new release/damage evidence is required, rather than accepting old cues. The built-in weighted BAT backend remains a separate three-bone/two-influence example; the gameplay cutout rig has fourteen bones. This checkpoint does not claim an arbitrary rig importer.

## Story allocation evidence

The original, uninstrumented Story test passed with **0/30 allocating frames and 0 recorded bytes**, keeping its original frame window and allowance of at most one allocating frame. No Story production optimization or test-window change was applied.

The earlier diagnostic run `0b0dd6d` captured 40 bytes per measured frame in the Unity Test Framework log observer. Its PlayerLoop capture did not explain the complete governor count. This was evidence of observer overhead, not attribution of a production defect. Intrusive allocation profiling is now a manual workflow option, default false; it must not be used as the ordinary budget validation. The older uninstrumented three-frame failure is retained as historical evidence, not erased or explained by this successful rerun. See [StoryAllocationInvestigation.md](StoryAllocationInvestigation.md).

## Actual continuous video evidence

Four clips were recovered from exact source `0b0dd6d`: live belt GPU/fallback and live horde GPU/fallback. Each contains 90 genuinely acquired image frames over approximately 4.0–4.24 seconds, acquired at 21.0–22.0 Hz. Encoding occurred after acquisition; there is no interpolated or synthesized motion, and one repeated final image preserves its display duration. Encoding timestamps match source timestamps within 0.5 microseconds.

Actual pixels were inspected: belt demonstrates blade→sword, approach/contact/recovery and hit response; horde demonstrates staff→bow, directional travel and live ranged attacks. These clips still show the previous placeholder art. The later art redesign requires its own exact-head native screenshots. Numerical pose tests and passing graphics checks do not alone prove aesthetic naturalness.

## Remaining requested work and evidence limits

- Collision: paired completed-build-plus-query comparison against a bounded quadtree; order-preserving runtime improvements only if correctness and representative measurement justify them.
- AI: bounded reusable decision selection over existing action FSMs, with actual RPG/belt integration, classic behavior/replay preservation, and separate runtime-cost measurements.
- Original scene, character, monster and UI art: source integration plus actual new native captures and visual inspection.
- Physical Android/iOS touch, graphics, thermal, battery and native-memory measurements remain unavailable until target hardware/access is supplied. Mac desktop timings are not substituted for those measurements.
