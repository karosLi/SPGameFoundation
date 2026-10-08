# Blade close-up native review and recovery gate

## Exact source and valid close-up evidence

This records `5d1db3e27b5db45658febbd4d4a122a1d8e40328` / tree `b2d5e2fc751baede4496c0a722db2685f4ed7bb9`, [native run 37776649150, attempt 1](https://github.com/karosLi/SPGameFoundation/actions/runs/37776649150/attempts/1). Its 567 production/resource/tool/workflow paths match `dc30b164`; only the capture repair and documentation differ. [Capture contract and identity proof](BladeGripCloseupDiagnostic.md).

Both `PreparedBladeGripSurvivesRepeatTurnEquipCancelAndReentry` GPU/DataTexture cases passed. Each produced 128 sequential 256×256 PNGs and six metadata files in `Artifacts/Screenshots/WeaponMotion/blade-grip-isolated-{gpu,fallback}`. All 256 images decode and match their official manifest hashes. Each actual sample records 19 prepared actor records, HP 120, no active skill, and unchanged dropped-tick count 16→16. The scripts verify copied source records and authoritative snapshots remain unchanged within their measured post-Sync span; the later observation-window defect and its trace limits are described below. The diagnostic camera is separate from normal gameplay; its documented temporary layer-mask exclusion affects only this fixture.

GPU acquisition spans 4.799179625 s, DataTexture 4.8748805 s: about 26.46/26.05 samples per second. Those are readback sample rates, not device FPS. Traces cover both facings, public equip cancellation, sword change and blade reentry. Videos retain all 128 source frames plus the documented final hold, without interpolation; slow diagnostics are explicitly labelled 0.25×.

## Actual visual result: acceptance withheld

All 256 frames were inspected chronologically. Ordinary blade cuts reach near-horizontal contact and recover upward while the palm and hilt stay together; post-equip repeated blade attacks also show controlled recovery. These observations support the earlier normal-camera improvement, but do not establish overall naturalness.

A specific remaining defect is visible during right-to-left reversal in **scenario 1, pulse 4 windup, before equip**. GPU f055 at 2.089 s swings the blade down toward the legs; DataTexture f053 at 2.082 s shows the same detour. GPU tip Y across f054–057 is approximately **1.897 → 0.192 → 1.435 → 2.210**. The hand/hilt remain attached, so the defect concerns the whole wrist/weapon path, not an independent sliding pivot. This evidence was acquired before the later storage failures and cannot be attributed to disk capacity.

The frozen source investigation identifies an unstable 180° angular tie, new-facing mirroring while smoothed aim still points toward the previous direction, and a fixed arm-bend branch during that mirror. The implementation continuation must reproduce the entire real held-input/new-pulse reversal, including final world-space arm and blade trajectories at 30/60/120 Hz and irregular samples. A correction must retain ordinary contact/recovery, canonical attack geometry/timing, rigid grip, bounded wrist and accepted sword behavior. Numeric grip equality alone is insufficient. A new production change requires fresh actual recordings; these films remain the failed reversal baseline.

## Whole run remains failed

| Scope | Result |
| --- | --- |
| .NET harness | 1,670 passed, zero failed, six retained Explicit cases; 78 projects, zero warnings/errors |
| Native EditMode | 1,710 passed, zero failed, five retained skips |
| Native PlayMode | 151 passed, 22 failed, three skips; both new close-up cases passed |
| Failure categories | 13 explicit disk-I/O failures, four audio-log failures, three object-identity failures, two allocation failures |
| Evidence archive | 16 parts, 1,376 members, 260,545,948 bytes; every wrapper/part/archive/member size, SHA and CRC verified |

Archive SHA256: `e8cd6b6c07d43af7407e7ddd5aa0f4a542f00518702b47a9daea2a7b69a634d1`. Twelve complete capture sets contain 860 frames. Four required Survivor sets are absent after failures; this differs from the twelve intentionally disabled large weapon sets reused from the identical dc30 production. Twelve zero-byte PNGs are preserved as failed outputs, not counted as valid images.

The first failed case is `SvComposedPulseGameplayTests.ComposedPulseUsesRealHudReleasePoseAndResolver(GpuDriven,True)`, bounded by XML times **12:35:55–12:36:00 UTC**. `Directory.CreateDirectory → BufferedFrameCapture.Write` reports `No space left on device`. The next disk failure names `Artifacts/Screenshots/MobileHud/ability-horde-repulse-fallback.png`. The timestamped job summary prints the first error at 12:37:39; that is summary emission, not the syscall time. No contemporaneous free-space measurement exists. Every other failed case follows this first error, but temporal order alone does not prove the nine non-I/O failures are secondary.

Actual Burst witness remains 0/1/1, with 60/60 warmup and 300/300 measured physics steps using Burst; measured mean/worst are 0.417/0.715 ms on this Mac. Shooter reports zero allocating frames/bytes across its original 180-frame window on each backend. Survivor autoplay fails its unchanged 1,024-byte gate with 20,204/17,706 bytes. RenderStress passes workload assertions but observes 135/240 allocating frames and 22,034 bytes on GPU, 125/240 and 20,250 bytes on DataTexture; it is not a zero-GC assertion. No gate or sample window was relaxed.

## Storage recovery and unchanged-source control

The user confirmed storage was freed and authorized continuation. A single [read-only capacity job 37781117374](https://github.com/karosLi/SPGameFoundation/actions/runs/37781117374) on the same `karoslideMacBook-Pro` measured **70,110,187,520 available bytes** for both approved filesystems at **13:01:26 UTC**. It read capacity only: no checkout, Unity execution, directory scan, content read or cleanup. Normal CI jobs were deliberately excluded only on that diagnostic branch. This snapshot is not a guarantee of future capacity.

After confirming the original run was terminal and the queue empty, one failed-job retry of the exact unchanged `5d1db3e` was released at 13:03 UTC. Attempt 2 and its artifacts are kept separate from attempt 1. Attempt 2 is now fully verified: EditMode 1,710 passed, zero failed, five retained skips; PlayMode 171 passed, two failed, three skips. Every one of the original 22 failures now passes on unchanged source and budgets. This is evidence of recovery under the changed environment, not a proof of the precise cause of each earlier non-I/O failure. The two new failures are current/prepared visual-ID mismatches in the close-up observer (GPU 1002 versus1001; DataTexture1001 versus1002). No new close-up outputs were written. All24 new artifact parts and1,753 members are verified; ZIP386,406,092bytes SHA256 `cb04d0df16627ee758630f13a1f47e505b788dff05055edfa280b3384568de0f`. Original attempt1 evidence is unchanged.

Source diagnosis confirms the test driver and SessionTickLauncher both used LateUpdate32000. Depending on their equal-order scheduling, observer Sync can complete a new tick after renderer500 prepared the displayed actor. The earlier snapshot equality covered only the post-Sync interval, and old mutable authority trace can lead the actual prepared image by a tick. The corrected observer uses1000, asserts no pending tick before reading, does not Sync into another tick, preserves the original ID assertion and checks no clock/tick mutation through readback. The original images and visible reversal defect remain valid, while exact rendered-phase inference from those old authority rows is excluded. [Observer correction](BladeGripCloseupDiagnostic.md#completed-tick-observation-window-correction).

Native remains failed until the integrated observer and [blade reversal correction](BladeTurnTrajectory.md) complete a new full run. Neither earlier failed attempt is retroactively marked passed, and normal or close-up recordings from old production cannot establish the new turn behavior.

These are Mac Unity/Metal observations. Physical Android/iOS behavior, sustained device performance and user naturalness acceptance remain open.
