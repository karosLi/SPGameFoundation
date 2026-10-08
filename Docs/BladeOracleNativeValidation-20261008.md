# Same-runtime art oracle: exact native closure

The test-only source `33bdb4adce849376be56d688071938cb8bd47371`, tree `3e6e3031a6d6700b7d729bfe646ee7d91192aa91`, passed [native run 37758432513](https://github.com/karosLi/SPGameFoundation/actions/runs/37758432513) and [harness run 37758432370](https://github.com/karosLi/SPGameFoundation/actions/runs/37758432370). This closes the two invalid cross-runtime hash expectations. It does **not** accept the later-requested no-jitter/no-droop blade behavior, which is a separate runtime revision requiring fresh captures.

## Exact outcome

- EditMode: 1,691 passed, zero failed, five unchanged skips.
- Graphics PlayMode: 171 passed, zero failed, three skips. Two are the explicitly optional movement recordings reused from production-identical da28905; the third is the existing explicit allocation-callstack diagnostic. They are not reported as freshly executed.
- Harness: 1,651 passed, zero failed, six existing Explicit diagnostics. All 78 projects built without warnings/errors; fifteen empty harness PlayMode TRX files are not native coverage.
- The two corrected art cases each compare all 20 original canvases with independent frozen f46c765 generators and each detect 80 single-channel mutations. The independent native texel/extrusion test also passes. No test names were added or removed.
- Both Editor exits are 0, attempt 1; no retry, Burst-disabled fallback, C#/Burst/shader compiler error or warning. Burst controls are managed/Run/Schedule 0/1/1 and physics executes all 60 warmup plus 300 measured steps. Original thresholds remain unchanged.

The [oracle correction and precision diagnosis](BladeControlledRecovery.md#native-hash-oracle-correction-test-only-2026-10-08) preserve the original failed da28905 XML. All 567 tracked non-document/non-test paths, modes and blobs remain identical to da28905; [the reuse proof](validation/BladeOracleRecordingReuse-20261008.json) identifies its complete recordings and original successful movement-capture cases. The twelve large recording sets are absent from this test-only run as intended; ordinary graphics and health pixel captures ran.

## Allocation boundaries and retained failure

Fresh Shooter CSVs contain 180 sequential samples per backend, zero allocating frames and zero bytes under the original maximum-two-frame assertion. The earlier 67a22c1 result of 3/180 allocating frames and 123 bytes remains preserved and unexplained; two subsequent successful windows do not identify its cause.

The separate Survivor autoplay steady samples record 492 bytes GPU and 656 bytes DataTexture, each within the unchanged 1,024-byte/180-frame gate, with UI-near frames reported separately. DataTexture render stress records 3/240 allocating frames and 366 bytes; that fixture asserts the visible bullet storm, not zero GC. Current-thread warmed motion/FK probes and full-frame observations are different scopes.

## Artifact integrity and remaining gates

All 24 official wrappers/parts, 1,753 archive members and 386,422,150 ZIP bytes passed size/SHA/CRC checks and complete restoration. ZIP SHA-256: `fab8494edc24f8c78cbf7e33ec0854814550b3d77264654e2bc1aff7861774ee`. [Restore the original evidence](CiEvidence.md) from this exact run; do not substitute this report for the logs/XML.

The user subsequently required the blade to remain firm with no visible shake or continued drop. The existing 14–15° authored low finish and its aesthetic lower-bound test do not satisfy that new condition. Its revision, fresh continuous native footage, user judgment and physical Android/iOS remain open. The accepted forward sword thrust is preserved.
