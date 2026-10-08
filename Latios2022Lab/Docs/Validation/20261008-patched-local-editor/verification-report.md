# Patched Latios Local experiment verification

[Run 37813671446, attempt 1](https://github.com/karosLi/SPGameFoundation/actions/runs/37813671446/attempts/1) is verified as a **passing single patched Local Editor experiment**. Import passed, EditMode passed 49/49 with Burst on and 49/49 with Burst off, and standalone PlayMode passed 1/1 in each mode. No native test failures, skips or inconclusive results. The workflow completed successfully at 2026-10-08 17:16:46 UTC.

This result applies to source `b6ef271fc8eded93c6e79c0dfcf90917b72b7945`, tree `dbda0866da350caa91029ce2d1cae855c61ce06b`, with approved common source `4f3910bb338080390071db493dd63c2cdc920b7c`. The clean local precursor `33a92bc3c8cc3d23bbf99c34190491d9e2722184` has the same tree. Relative to the matched unpatched Local release, the sole changed path is `Latios2022Lab/ci-request.json`, changing only the experiment arm and expiry. Source, fixture identities, budgets and oracle hashes were not relaxed.

## Artifact integrity and reproducibility

All six official GitHub artifact wrappers were downloaded. Their SHA-256 digests and byte sizes match the official artifact API, and every wrapper passes CRC checks. The five full-part manifests are byte-identical. All five inner parts match their manifest digests and sizes. The full archive was assembled in memory to avoid another large disk copy, then checked for exact size/hash, CRC, safe regular-file paths, unique members and manifest coverage before restoration.

- Full archive: 82,893,355 bytes, SHA-256 `b1fb3ba53fb4b43eeeb581c264eee41fa1d05444e8126ad3022be6d7b246661d`
- Manifest: SHA-256 `057131583ed2d701e0c5910af6df6b065d16a8ce4e661ef7b84ba70e5e23710b`
- Restored: 327 files, 623,181,044 bytes; every member size and SHA-256 verified
- Producer's separate original hash map: all 326 originals verified, with exact coverage excluding the hash map itself
- Five native hash maps: every entry rechecked against the restored original; all native exit codes are zero and post-run integrity records have no secondary errors
- Early artifact 11567110654: wrapper SHA-256 `05dc498ea9778e044292692cf91e960b68c21cf5f2afca87f25a5cadaf045652`; its 271 complete original files are byte-identical to full evidence, and all 10 excerpts exactly reproduce the labelled head/tail and original hash

The official wrappers are retained in this directory. No unique evidence was deleted. The restored files are under `restored/`, with the archive's leading `Artifacts/` removed. Independent scripts and all verification records are retained beside them. `verification-summary.json` and `report-sha256.json` index exact hashes. The prepared snapshot verifier was read before execution, archived unchanged, and supplemented with independent wrapper/restoration, original/early hash and semantic audits; its pass flag alone was not treated as sufficient.

## Package, source and dependency isolation

Actual Unity PackageInfo reports `com.latios.latiosframework`, version 0.11.5, source `Local`, resolved to `/Users/karosli/Documents/Study/Unity/SPGame/actions-runner/_work/_temp/latios-s1a-37813671446-1/variant-packages/com.latios.latiosframework`. Its packageId and manifest file: URL resolve to that same run-specific path. This is a modified Local package, not an unmodified Git package.

Twelve snapshots cover before-native, before/after each of the five phases, and final state. Every one of their 24 uncompressed tars was fully read: 1,235 members each, 29,640 member checks total, with exact path, mode, byte size, SHA-256 and Git blob verification. Every pristine source snapshot reconstructs official upstream tree `4790057a1964150f2ca815f89cc85498bc1cb43e` for commit `381a77dbf774ff603014d5695ef6c06abaa25d96`; the official GitHub commit endpoint independently confirms this mapping. The pristine payload is 23,986,607 bytes; the patched payload is 23,986,610 bytes and reconstructs tree `033755a5271452395e145555e78f34b0eeb20565`.

The only source difference is `Core/Internal/CollectionComponentOperations.cs`, line 175: `RemoveComponent(context->addQuery, t)` becomes `RemoveComponent(context->removeQuery, t)`. Each snapshot's byte-derived unified diff exactly equals the approved patch. Patch SHA-256: `6736b6b8f1c0ca32151a44163afc9a75fcf133a70dbe15b4175a8d8f3ab994a0`. Target SHA-256 changes from `5a72addfdfde3c358876267b1fe89a5e100c0f3d162ee3ec45861dc502f609dc` to `087ddf701e638d56fb01b80ef4becc27ae7e0516c504fc3e4bdc09d2dc99ab6d`.

All original files, LICENSE and third-party notices are retained; the repository notice copies match the tar bytes. All 44 frozen owned-file hashes match the reviewed source and the matched unpatched Local control. All five launch records retain the 41 launch-visible owned files and all 26 fixture/oracle hashes; the other three frozen entries are the separately verified variant spec, patch and inventory. Runtime-gate/source identity and sealed experiment record bindings agree throughout. Each of the ten per-native before/after inventory records matches the verified tar inventories. The CI's post-phase own-source checks report no errors; hashes are recorded at each launch, not a claimed second post-run copy of every owned source file.

All 12 actual generated lock copies contain the exact 39-entry graph: one run-specific Local Latios entry and all 38 unchanged non-Latios entries. No Git provenance was forged into a Local lock. Unity's normalized lab-only settings and generated URP asset are retained separately. Root product input fingerprints match the reviewed source and each native post-run record confirms no root input mutation.

## Native results and their boundaries

Unity 2022.3.62f2 ran natively as arm64 on Apple M5 Pro / macOS 26.4, target StandaloneOSX. Resolved dependencies include Entities 1.3.5, Entities Graphics 1.4.2, Collections 2.5.1, Burst 1.8.18, Mathematics 1.3.2 and URP 14.0.12. This environment is an Editor validation host.

The exact test identities and assertions were checked against the frozen source. The patch fixes all three DestroyEntity cases at writer batch sizes 1/8/64, including the strict owner-absence check before World teardown, stable disposal count after another initialization update, and final assertions. Both EditMode modes complete two domain-reloaded entry/exit cycles. Source and full log samples agree on that completed path.

The unchanged conditions imply sum 37265 and disposal witness [1,17] at the relevant passing assertions; these were not separately dumped as raw values. The post-schedule expected-exception fixture also passes its immediate post-removal disposal witness. Standalone PlayMode runs three owned-world iterations using DisposeWorld only, with pair and query checks. It does not add standalone PlayMode DestroyEntity or Remove coverage.

The native BurstDiscard marker directly witnesses the scheduled sphere QueryJob: it must stay 1 with Burst on and become 0 with Burst off. Both modes pass in EditMode and PlayMode. This is direct backend evidence for that job, not a per-job trace of every pair, collection or optional-module algorithm.

Psyshock coverage is 36 pair cases per EditMode mode (four finite sphere fixtures × immediate/single/parallel × subdivisions 1/2/4), normalized pair sets and duplicate rejection against an independent inclusive AABB oracle, plus known sphere ray and distance queries. Empty QVVS installation/update and small Core collection ownership paths are exercised. Pair visitation order, general shape/solver correctness and performance are not established.

All five compiler snapshots independently verify their 12 original file hashes and sizes (60 checks); each totals 138,660 bytes. Response files retain direct dependencies and Editor, safety, store and atomic defines. All four owned and 13 Latios assemblies have compiler/postprocessing/copy events. The 77-assembly and 14-managed-system inventories are compilation/installation evidence. Optional renderer/audio/module algorithms, native plugins, populated QVVS hierarchies and scene streaming are not established. No generated C# was retained for inspection. Nonfatal Xcode Info.plist warnings remain visible; this pass does not validate player tooling.

See [source-semantics-audit.md](source-semantics-audit.md) and its JSON for fixture identities, source/log locations, expected exception handling, precise disposal/reentry reasoning, and compiler evidence.

## Original failures remain failures

Original Git control `bda5bc7`, run 37761086135 attempt 2, and matched unpatched Local control `fe946e2`, run 37785324983 attempt 1, both remain 45/49 with the same four failures: DestroyEntity at batches 1/8/64 and domain-reload reentry. The original XML and failure evidence were preserved and independently reread.

Each original failure reaches `CollectionProbe.cs:79` because the destroyed owner still exists after sum 37265 and witness [1,17] have passed. The exact remaining component list was not dumped. Those failures stop before subsequent second-update and final normal-path assertions. Passing the patched Local variant does not retroactively pass either original control or establish an unmodified-upstream result.

## Remaining gates

Repeat clean import and fresh-cache repeat controls, desktop IL2CPP build and actual player execution, physical Android/iOS validation, product/SPF integration and S2 remain pending. S1a remains incomplete. The observed disposal/reentry checks and shutdown messages do not prove comprehensive native/managed leak freedom. No native runs, reruns, source edits, ref changes, publication or Library uploads were performed by this verifier.
