# Patched Local Editor milestone and conditional player preparation

2026-10-08: [run 37813671446, attempt 1](https://github.com/karosLi/SPGameFoundation/actions/runs/37813671446/attempts/1) is verified as a **passing patched Local Editor experiment**: clean import, 49/49 EditMode with Burst on, 49/49 off, 1/1 standalone PlayMode on and 1/1 off; no skips or inconclusive cases. It completed at 17:16:46 UTC. S1a remains incomplete.

The native source is `b6ef271fc8eded93c6e79c0dfcf90917b72b7945`, tree `dbda0866da350caa91029ce2d1cae855c61ce06b`, approved common source `4f3910bb338080390071db493dd63c2cdc920b7c`. Local precursor `33a92bc3c8cc3d23bbf99c34190491d9e2722184` has the same tree; it is not substituted for the published run's commit identity. The patched/unpatched Local release differs only in the gate's arm and expiry. Fixtures, budgets, assertion identities and all 26 frozen oracle/meta hashes are unchanged.

The package is [official Latios 0.11.5 at 381a77db](https://github.com/Dreaming381/Latios-Framework/tree/381a77dbf774ff603014d5695ef6c06abaa25d96), loaded as a Local copy with the approved single cleanup-query correction. It is not an unmodified Git result. [The frozen protocol](CleanupVariantProtocol-20261008.md) retains the original upstream tree, complete inventory, patch and licences.

## Preserved evidence

[Original verification report](Validation/20261008-patched-local-editor/verification-report.md), [machine summary](Validation/20261008-patched-local-editor/verification-summary.json), [report hash index](Validation/20261008-patched-local-editor/report-sha256.json) and [source-semantics audit](Validation/20261008-patched-local-editor/source-semantics-audit.md) are copied without changing bytes. Their references to `restored/`, wrappers, scripts and other audit files describe the verifier's full archive, not files all copied into this repository. Large raw tars remain in the original run's bounded artifacts and retained verification workspace. The run link identifies all six artifact wrappers; five full parts restore the original evidence via `Tools/ci/evidence_parts.py`.

| Receipt | Exact value |
| --- | --- |
| Full ZIP | 82,893,355 bytes; SHA-256 `b1fb3ba53fb4b43eeeb581c264eee41fa1d05444e8126ad3022be6d7b246661d` |
| Archive manifest | SHA-256 `057131583ed2d701e0c5910af6df6b065d16a8ce4e661ef7b84ba70e5e23710b` |
| Verification report | SHA-256 `fde933fb9b75445015fe0e676df8235fb8a81446beb300425d7ba0b38f441b85` |
| Verification summary | SHA-256 `a7fdcdd6518254e9034559f543aae9963024cc6961774e2fc572b4b5cee0ea43` |
| Complete restoration | 327 files / 623,181,044 bytes, every member verified; 326 original producer hashes verified |
| Package snapshots | 12 snapshots / 24 fully read tars / 1,235 members per tar / 29,640 member checks |
| Actual generated locks | 12 exact 39-entry graphs, including 38 unchanged non-Latios entries |
| Compiler evidence | Five snapshots, 12 files each, 60 original hash/size checks; four owned and 13 Latios assemblies compiled |

The sole package change remains `Core/Internal/CollectionComponentOperations.cs:175`, `addQuery` to `removeQuery` in the cleanup removal call, patch SHA-256 `6736b6b8f1c0ca32151a44163afc9a75fcf133a70dbe15b4175a8d8f3ab994a0`. Pristine snapshots reconstruct upstream tree `4790057a1964150f2ca815f89cc85498bc1cb43e`; patched snapshots reconstruct `033755a5271452395e145555e78f34b0eeb20565`. Every licence/notice byte is retained.

## What passed, and what did not

On Unity 2022.3.62f2 / arm64 Apple M5 Pro / macOS 26.4, both Editor modes pass the strict DestroyEntity owner-absence checks at batch sizes 1/8/64, Remove/DisposeWorld and expected-exception checks, and two domain-reloaded entry/exit cycles. Psyshock has 36 pair/oracle cases plus scheduled sphere ray/distance checks per EditMode mode. The BurstDiscard witness directly proves the query job's on/off execution mode. Standalone PlayMode exercises three DisposeWorld iterations and pair/query checks; it does not add standalone Remove/DestroyEntity coverage. Empty QVVS installation/update is covered; optional module algorithms, populated transform hierarchies, general shapes/solver behavior, pair order, performance and comprehensive leak freedom are not established.

[Original Git run 37761086135 attempt 2](https://github.com/karosLi/SPGameFoundation/actions/runs/37761086135/attempts/2) and [unpatched Local run 37785324983 attempt 1](https://github.com/karosLi/SPGameFoundation/actions/runs/37785324983/attempts/1) remain **45/49, four failures**: three DestroyEntity batch cases and domain-reload reentry. Both stop at the retained owner check. The patched pass never retroactively changes those results.

## Next conditional gates

[The separate follow-up launcher](../Tools/followup.py) is preparation only. It preserves every C# file, all frozen fixture hashes, the experiment profile and the published Editor CI workflow/schema. Its tool commit and native source commit are explicit separate fields: new Python orchestration does not rewrite historical source identity, nor require a replacement fixture suite. A release must still bind the precise successful native source/tree and all five original Editor phases, original evidence hashes and human review, a future reservation expiry, live P0/Editor run state and an empty shared queue.

1. `repeat-clean-import`: materialize exactly that native source in a physically new detached worktree; prepare the same official patched Local arm; use a new Library/Temp/UPM cache and no starting generated lock. Pass import, package/real-lock/environment/source integrity and retain original output. This proves a second clean import only; it does not claim a second complete test-suite run.
2. `mac-il2cpp-smoke`: requires separately reviewed repeat evidence for that same source and a separate fresh gate/workspace. Import again, then build and actually launch the unchanged `LabPlayerSmoke`. Its bounded acceptance is three DisposeWorld collection cycles, four parallel pair fixtures and the scheduled Burst query witness. It does not claim full player lifecycle or mobile validation.

Only the disposable normalized player's company/product names become run-unique. The launcher preserves before/configured/after-build settings, their SHA-256 hashes and both textual deltas, including the existing build helper's IL2CPP configuration. Native compiled source remains frozen; normalized/build settings are explicitly different evidence. Using Unity's [documented macOS persistent-data paths](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Application-persistentDataPath.html), the launcher precreates a new company/product directory and refuses any existing current or legacy path. It never rewrites `HOME`, global preferences, signing/security or module installations.

Build success alone is insufficient. The selected `arm64` or `x86_64` architecture must exist in the Mach-O executable; launch uses that explicit `arch` selector. A mismatched default build is a failure, not an automatic architecture/backend substitution. The launcher checks the report and actual normalized IL2CPP setting, fresh output, process exit/timeout, exact frozen result content, matching log JSON, unchanged application inventory, source/tree and run identity. Source/run/result/app/log hashes are bound in an external manifest because the frozen result does not embed a nonce. No new player assertions are implied. Missing IL2CPP/SDK support is preserved as a genuine blocker.

No conditional gate, native process, CI queue request or publication was produced by this preparation. Repeat import, IL2CPP build/execution, physical Android/iOS, S1a closure and S2/product adoption remain pending. Ordinary product/default backends are unchanged.

Local preparation checks: **79 Lab Python tests + 33 CI Python tests passed**, including 16 new conditional-gate cases. Static preflight and all 26 frozen fixture/meta hashes pass; all C#/asmdefs, variant files, original launchers and workflows have no diff. The actual retained b6 archive's 326 producer hashes and all five original phase commands/source/package records were reread successfully with the new prerequisite parser. Independent review's two findings were corrected and independently rechecked: only the three exact generated scene/meta paths are excluded from source freezing, and every historical phase must bind its complete command and common sealed record. [Bounded receipt and source hashes](Validation/20261008-patched-local-editor/local-preparation-verification.json) distinguish synthetic tests and read-only archive checking from native execution.
