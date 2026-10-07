# Mobile foundation follow-up validation

This page tracks the finite collision/AI, art, gait and projectile follow-up to the green [weapon checkpoint](WeaponMotionStage5Validation.md). It does not replace the detailed implementation contracts or claim physical Android/iOS validation.

## Closed native checkpoint: e86ee87

Exact remote `e86ee87a7691282ae7e96713c315dbec5998c414`, local `62c5b7f5765a879aaf8ec9fa69bc7601ae3fa969`, tree `04435658d8b1c2e95e286b093405141b5fd6040b`: **both CI jobs are terminal success**. [.NET](https://github.com/karosLi/SPGameFoundation/actions/runs/37585283320): **1,050 passed**. [Native Unity](https://github.com/karosLi/SPGameFoundation/actions/runs/37585283378): **1,084 EditMode passed / 0 failed / 5 skipped**, **154 graphics PlayMode passed / 0 failed / 1 explicit diagnostic skip**.

All **19 parts / 1,674 files / 306,025,159 archive bytes** are fully restored and hash-verified; archive SHA-256 `958641b10b3aa0efb7f6b2ef48c78a1a2c3d37ac27a86974f1821582ae0b6652`. [Machine-readable closure, all arithmetic CSV results and source-equivalence hashes](validation/NativePrecisionClosure-20261007.json).

The two dense precision failures are resolved without changing production or relaxing equality. The Mac selected **ExtendedScalar**; actual candidate Burst backend is **1** and the independent managed reference is **0**. All 17 diagnostic CSVs verify **4,096 exact pair cases, 736 exact row pairs and 59,598 exact contribution-stage pairs**, with zero differences and instrumented/uninstrumented identity. [Diagnosis and contract](SeparationArithmeticParity.md). The experimental parallel candidate remains unadopted: exact arithmetic is necessary, but does not prove whole-Tick or mobile performance benefit.

All 408 production asset files, 60 PlayMode fixtures, CI capture tools, packages and settings are byte-identical to the reviewed `3f6362f` source. Its actual 1× gait/weapon/collision videos and compact-HUD/arrow-tip review remain explicitly labeled `3f6362f`, rather than being relabeled as newly viewed pixels. Native graphics regressions also pass on this new head. Current physics evidence retains the unchanged gate: mean **0.395 ms**, worst **0.504 ms**, with actual Burst in 60/60 warmup and 300/300 measured steps on this Mac; this is not a universal device budget.

This closes the requested software/native collision/AI/art/action follow-up checkpoint. Existing visibility limits and physical Android/iOS touch, sustained frame pacing, memory, thermal and battery gates remain explicit. The user subsequently authorized execution of the [layered semantic extension plan](SharedFoundationSemanticExtensionPlan.md); that is the next staged work, beginning with its compatibility baseline, not an unreviewed replacement of the current foundation.

## Earlier runtime checkpoint: 3f6362f

Exact remote `3f6362f659cc5bc72955e019cca14010744784e2`, local `677611a85aab71eb33561aeb479f032c7188c113`, tree `1cb7401216d679de2d091a5e1a998755eede574d`. [Native run](https://github.com/karosLi/SPGameFoundation/actions/runs/37581211270): **1,077 EditMode passed / 2 failed / 5 skipped**, **154 graphics PlayMode passed / 0 failed / 1 explicit diagnostic skip**. [.NET CI](https://github.com/karosLi/SPGameFoundation/actions/runs/37581211300) passes **1,045 tests**, zero build errors. This runtime checkpoint is **not fully green**.

The same dense test-only separation comparisons still fail under Strict/High, with actual Burst backend=1: count32 row0 managed `uint2(3189493657,3175785598)` versus Burst `uint2(3189493657,3175785599)`; count128 row2 managed `(1025170858,3189639816)` versus Burst `(1025170860,3189639816)`. The precision-flag hypothesis did not resolve the mismatch. Production remains on the original ordered path; the exact equality gate is retained. A subsequent arithmetic investigation reproduced both managed results in bundled Mono with float32 optimization disabled; that investigation is not a completed native repair.

All **19 parts / 1,655 files** are fully restored and verified: archive **303,898,026 bytes**, SHA-256 `fbbc6d5e19b6aff3104647b34db67758dab42e116e9098aff6533228ed3c45f2`. The 1,002 JPEG95 review files total 76,890,593 bytes; the full archive also adds new collision evidence, so its size is not a same-workload compression benchmark. Raw acquisition/pixel assertions and dedicated lossless PNG tests remain separate.

Targeted acceptance now supported by actual execution and reviewed pixels:

- All four real gait captures have zero visible grounded-Walk samples with both feet unsupported. The former Belt hit/restart exception and deep reversal/restart crouches are absent in the inspected windows; actual hit flashes/recoil remain. All Horde roles genuinely move, including the formerly pinned heavy. `current_frame_visible` means camera-frustum/current submission, not HUD-occlusion detection: the heavy is partly under the top panel early and fully clear from about frame48.
- Compact HUD is visually accepted for its bounded change: both tiers, normal and720×1600 tall safe-area layouts,110px two-row and134px armed panels. Status/health labels, Menu and skills remain readable. This is not a blanket final-art claim for all dense crowds.
- Arrow-tip pixel cases, all four collision-debug graphics cases and JPEG export/raw-preservation checks pass. Actual overlay sequences show projectile flight, accepted contact, off-lane rejection and separate body/hurt/attack shapes.
- New same-camera Belt videos retain160 source frames plus one final hold: GPU7.218990 seconds, DataTexture7.427300 seconds, maximum PTS error0.0005ms, normal1× playback. They use declared JPEG95 review sources. Late Belt movement is constrained by nearby enemies; early enemies partly overlap right-side controls.

The subsequent shared-foundation survey, semantic plan and AGENTS links are **documentation only**. Their publication uses an explicit `[skip ci]` marker to avoid repeating unchanged rendering/capture work; it does not clear these two runtime failures or claim new runtime validation. The actual precision-repair commit must run full CI without that marker.

## Exact recovered checkpoint: 4af9b87

- Remote: `4af9b87cc32fcd1a5f10a4bd2817848044ad4649`; equivalent local: `cf45385ba0f3e13d2576542d1a35fbc4e50151ef`; identical tree: `c0173dec0f2cbda34c5734b496d0915f5bd38d1e`.
- [Native run](https://github.com/karosLi/SPGameFoundation/actions/runs/37574715913): Unity 2022.3.62f2, Apple M5 Pro. EditMode **1,012 passed / 2 failed / 5 skipped**; graphics PlayMode **148 passed / 0 failed / 1 explicit diagnostic skip**. Exact-head [.NET run](https://github.com/karosLi/SPGameFoundation/actions/runs/37574715924): **980 passed**, across 11 EditMode assemblies. Stub PlayMode projects are not native graphics execution.
- Complete recovery: 29 parts, 1,398 files, 478,596,105 archive bytes, SHA-256 `2a62d8ad4776279f77b9a170ab4a4725c1134909d00c0eb1e3305557b6b6232d`. Each part, archive and extracted file matched the manifest. [Recovery/encoding recipe](CiEvidence.md).
- The only failures are `BwBeltSeparationCandidateTests.ImmutableGridParallelCandidateMatchesOrderedReferenceAndReportsCompletedCost(32,True)` and `(128,True)`: approximately one-ULP differences in exact ordered float accumulation. The parallel candidate is test-only; production ordered separation remains unchanged. The next candidate explicitly requests Strict/High Burst arithmetic and preserves exact equality, dense cases and all thresholds. The precise generated-instruction cause is not established; a local pass is not native proof of that repair.
- EditMode skips: two original explicit export/tuning tools, an opt-in allocation diagnostic, and two GPU-resource checks without a graphics device. The corresponding GPU upload-accounting behavior has separate executed graphics PlayMode tests. PlayMode's one skip is the explicit Survivor allocation-callstack diagnostic. These are not counted as passes.

## Verified and pending acceptance

| Area | Evidence on 4af9b87 | Follow-up gate |
| --- | --- | --- |
| Shared mobile rules | Root [AGENTS.md](../AGENTS.md) requires mobile budgets, research before new capabilities, grounded motion, full playable loops, bounded FX/collisions and exact source evidence. | Apply this to every later demo and change, rather than treating the document itself as runtime acceptance. |
| Grid / quadtree | All primary distribution, tuning, brute-force, ordering, overflow and swept-hit cases pass; actual Burst sentinels are 1. [Unmodified reports](Benchmarks/Native-4af9b87/source.json). | Retain grid default; no blanket migration based on a query microbenchmark. [Comparison](CollisionBroadphaseBenchmarks.md). |
| AI | Direct/tree policy and snapshot tests pass; measured tree selection is slower; direct remains default. Stable projectile/destroy queue fixes are active. | Dense test-only separation precision remains red on this head. [AI evidence](AiDecisionValidation.md). |
| Original art | Actual two-tier shooter, guard, sword-horde and belt screenshots contain the painted resources and ivory/coral actors. Terrace join and GPU/DataTexture consistency were inspected. | Dense horde overlap and the large status card prevent a blanket polished-art sign-off. Compact HUD is implemented after this head and needs its new screenshots. [Art status](SanctuaryArtDirection.md). |
| Native UI defects | Both portrait cancel cases and `CancelKeepsHighContrastText` pass. Actual fallback image has readable ivory CANCEL with coral warning art. Both delayed-floor and new-game RPG camera tests pass; the hero is visible in the run capture. | Do not transfer those results automatically to a later modified source. |
| Grounded walk | Matched camera/input Belt captures show substantial support improvement. Native counts are below. | Residual restart, weapon-turn and depth-reversal pelvis defects require the next runtime candidate and actual clips; Horde fixture must prove sustained, visible movement for each role. |
| Weapon/FX coordination | Both games and both tiers pass four-weapon/socket gameplay, retained-event/current-charge backlog, and projectile silhouette tests. Actual adjacent Belt frames show blade contact, target flash and a burst appearing, expanding and fading; staff and bow releases are visible. | New arrow-tip pivot and joint ground/height collision fixes plus debug overlays are later changes; require exact-head pixel tests and normal-speed gameplay review. [Audit](MobileActionCoordinationAudit.md), [hit policies](CollisionPoliciesAndDebugging.md). |
| GPU / CPU particles | Actual compute state max error `1.907349e-6`; 2,295 occupied production-draw pixels, zero mismatches above 8/255, including buffer/texture and missing-shader fallback. Both warmed GPU and CPU/Burst paths have 0 current-thread AllocationSamples over 64 submissions, with retained/empty controls 32/0 before/after and process Gen0=0. | This scope excludes native/driver/other-thread allocation, mobile GPU behavior, thermal and battery costs. |
| Mobile hardware | Android/iOS is the delivery target; controls, orientation, safety areas, quality tiers and fallback contracts are implemented/tested in desktop environments. | Physical touch delivery, device graphics, sustained frame pacing, memory, thermal and battery evidence remain external gates until hardware/access is supplied. |

## Actual continuous motion review

Each Belt before/after recording has 160 acquired frames plus one documented final display hold in its encoded video. Encoding uses each clip's measured timestamps at normal 1× speed, without interpolation or retiming. The paired capture source, camera and input schedule are unchanged; separate runs are not frame-synchronized simulations. Acquisition cadence is about 20–23 Hz because synchronous Editor readback affects it, not a mobile frame-rate claim.

Walk samples with both feet marked out of stance, ordered **hero / agile / heavy**:

| Backend | Baseline 54477736 | After 4af9b87 |
| --- | --- | --- |
| GPU | 22/111, 17/85, 20/66 | 1/128, 0/122, 0/103 |
| DataTexture | 12/85, 14/79, 19/71 | 0/127, 0/115, 0/103 |

The remaining GPU sample follows a visible hit/HP loss and restart, so it was investigated rather than hidden or relabeled as a clean walk. Larger pelvis dips also appear without fresh hit flashes in fallback Belt frames 89–90 and 157–158, and isolated Horde GPU frames 136–144. The old CSV lacks explicit Hit/Attack state, so it cannot attribute every dip. Targeted software reproductions subsequently identified restart support loss, an intermediate downward smoothed weapon heading during horizontal turns, and wrong support-foot choice after a depth-direction change. A subsequent fix needs fresh native verification.

The new Horde recordings are **not** a matched old/new pair. On this head the heavy fixture mostly idles after beacon separation and later leaves view; agile/standard actors move but have no Walk samples. They cannot establish complete per-role naturalness. The next fixture must assert current-frame actor submission, viewport inclusion, real displacement and sustained Walk/Run samples. Scripted movement phases must be labeled as controlled live-gameplay diagnostics, not autonomous AI behavior.

Representative delivered pair:

- `grounded-belt-baseline-54477736-datatex.mp4`: 7.358870 seconds of measured acquisition.
- `grounded-belt-after-4af9b87-datatex.mp4`: 7.344218 seconds; SHA-256 `c6371854b23ce0378caf2b65fca1dab81e764f13002ca41cfffa4286d2071e77`.

Both retain the same camera without explanatory image overlays. Source-frame hashes and encoded PTS reports accompany the videos. Maximum encoded timestamp discrepancy across all eight 4af9b87 videos is 0.0005 ms. The raw artifact names remain `Screenshots/WeaponMotion/{grounded-belt-live,grounded-horde-live,weapon-belt-live,weapon-horde-live}-{gpu,fallback}`.

## Remaining bounded validation

1. Compile/run the combined follow-up, preserving the old test budgets and exact parity checks.
2. Publish exact trees on the existing feature branch and run native EditMode/graphics PlayMode with actual Burst.
3. Inspect compact normal/tall safe-area HUD, opposing arrow tips, joint ground/height contact/rejection overlays, weapon impact timing, and normal-speed gait with explicit state traces.
4. Record exact results, failures and source links here and in the relevant implementation docs. A later green test result still requires actual visual review. Do not generalize desktop proof to physical devices.

## Combined follow-up local gate

The combined runtime/capture source `7e9419155827a40dfe9a1cea19546e053f9f7c47`, tree `7f8514b3f1471da793670c4ea8590aaa5383c047`, now passes **1,045/1,045 .NET logic tests**, all 76 generated assemblies and zero build errors. The tooling suite passes 30 Python tests. A separate actual Unity-DLL compile covers 211 current sources against 96 Unity/package references: zero errors and four existing serialized-field warnings. [Machine-readable scope and log hash](validation/MobileFoundationFollowup-local-20261007.json).

This combines compact HUD, forward arrow-tip alignment, joint ground/height collision and overlays, unchanged exact separation parity with Strict/High, three reproduced gait fixes, richer action/support traces, visible per-role Horde cadence checks and deferred JPEG95 review capture. The conflicting Horde capture line was resolved to retain both the honest scripted-speed description/role assertions and JPEG95 output. No test threshold or scenario was removed.

The independent gait source aggregate also passes 1,045 cases; focused motion/action/socket tests pass 70 and controlled-Horde cadence preflights pass six (20/21/22/23/30 Hz and alternating20–23 Hz). This establishes regression/compilation readiness, **not final visual acceptance**. The next exact native run must resolve the two prior dense parity failures and supply the new real screenshots and normal-speed footage. Physical Android/iOS remains unmeasured.
