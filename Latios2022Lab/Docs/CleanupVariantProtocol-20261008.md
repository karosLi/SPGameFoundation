# Frozen Local cleanup experiment

Status, 2026-10-08: **matched unpatched Local control reproduced 45/49; patched Local Editor completed all five phases**. [The exact b6ef271 milestone](PatchedLocalEditorMilestone-20261008.md) retains archive/report hashes and the complete verification boundaries. The independent bootstrap correction and all frozen fixture bytes are common to the Local/Local comparison. The original [bda5bc7 attempt-2 control](BootstrapContractCorrection-20261008.md) remains failed. Repeat/player/mobile gates are pending; no product/S2 change is included.

## One experiment, two separately released arms

The only experiment is `latios-0.11.5-cleanup-query-v1`. Its arms are `unpatched-local-control` and `patched-local-variant`. Both use the same reviewed source commit, bootstrap, evidence tooling, Editor, package versions and unchanged test/probe/asmdef bytes. Each has a new workspace and an independently issued gate. They run serially on the reserved Mac, after the lead's higher-priority work. There is no automatic next-arm launch or retry.

The Local/Local comparison removes both the bootstrap and Git-to-Local-loading differences from the patch comparison. The original Git control stays preserved separately. A control failure is always FAILED. A successful unpatched EditMode/on instead records **UNEXPECTED_PASS**, fails the wrapper and leaves later phases NOT_RUN; non-reproduction needs review before releasing the patched arm. The lead must inspect the control's actual four cleanup failures and absence of bootstrap assertions, not merely its red workflow badge.

## Exact source and sole patch

Only the existing official `https://github.com/Dreaming381/Latios-Framework.git` is fetched, at commit `381a77dbf774ff603014d5695ef6c06abaa25d96`, Git tree `4790057a1964150f2ca815f89cc85498bc1cb43e`. The verified complete tree has **1,235 files / 23,986,607 bytes**. No fork, package upgrade, dependency addition, module trimming or new license terms are introduced. LICENSE and THIRD PARTY NOTICES retain their exact original bytes.

[The specification](../Variants/cleanup-query-v1.json), [complete source inventory](../Variants/cleanup-query-v1.source-inventory.json) and [exact patch](../Variants/cleanup-query-v1.patch) are checked against fixed module constants. The inventory contains every path, mode, Git blob ID, byte count and SHA256 and reconstructs the exact Git tree. Raw Git blobs are materialized without checkout filters/newline conversions; links, submodules, hard links, traversal, case/Unicode collisions and unexpected files are rejected. Inventories stay outside the package.

| Identity | SHA256 |
| --- | --- |
| Complete official inventory JSON | `da4d10946620e4b6d23cf5214c802d90ebdb1c7f5e77a7e923e442b1ac5a0106` |
| Original CollectionComponentOperations.cs | `5a72addfdfde3c358876267b1fe89a5e100c0f3d162ee3ec45861dc502f609dc` |
| Patched file | `087ddf701e638d56fb01b80ef4becc27ae7e0516c504fc3e4bdc09d2dc99ab6d` |
| UTF-8/LF three-context-line patch | `6736b6b8f1c0ca32151a44163afc9a75fcf133a70dbe15b4175a8d8f3ab994a0` |

Control must exactly equal the official inventory. Variant may differ only at `Core/Internal/CollectionComponentOperations.cs:175`, changing `RemoveComponent(context->addQuery, t)` to `RemoveComponent(context->removeQuery, t)`. The full postimage and exact diff must match, not just a text search. The original Git package/cache is never edited.

## Physical loading, genuine locks and truthful provenance

The fixed layout is `<new-workspace>/source/Latios2022Lab`, `<new-workspace>/upstream-source/com.latios.latiosframework`, and `<new-workspace>/variant-packages/com.latios.latiosframework`. Only the runtime project's copied manifest changes the Latios value to `file:` plus that arm's canonical absolute package path. The reviewed repository manifest stays at the original Git pin. Each arm has fresh Library/Temp/UPM caches and no starting generated lock; packages-lock.json is created only by Unity. [Unity 2022.3 documents these local paths](https://docs.unity3d.com/2022.3/Documentation/Manual/upm-localpath.html).

Local locks must show the exact canonical file path, local source, original package dependency map and **the entire unchanged non-Latios dependency graph** from the preserved real lock SHA256 `0226f8bb362698bb4dfab670ca76a49f260b08d248721bfe086c34ba9a0365e7`. This reference is a validator, never a manufactured Local lock. Unity's previously observed toolchain addition is accepted only with the matching genuine entry. Any other normalization, dependency change or package write is evidence to retain and investigate, not a reason to loosen pins.

PackageInfo records actual `source`, `packageId`, `resolvedPath`, version and experiment identity. The registered Latios package must be **Local** at the verified physical path; the original 0.11.5 package.json remains unchanged. The environment record identifies the sealed experiment-record SHA, whose contents bind upstream and patch/inventory identity. Every fixture's existing Verify entry additionally checks that Unity loaded the gated physical Local package. [source](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/PackageManager.PackageInfo-source.html) and [resolvedPath](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/PackageManager.PackageInfo-resolvedPath.html) describe the actual loader source, not the semantic release version. Logical compiler paths may still begin `Packages/com.latios.latiosframework`; those paths alone are not source provenance.

The milestone verifies actual Local lock/packageId formatting in this exact Editor/source run. Future runs still fail closed if it differs; the validator never relabels arbitrary bytes as the approved package. The Git-control path, when no experiment is selected, still requires the original Git pin and verifies registered Latios provenance.

## Gate and launch contract

Existing P0 evidence, clean source, exact branch, source allowlist, gate-only release, expiry and exclusive queue checks remain mandatory. The optional committed `experiment` object must exactly match `cleanup_variant.experiment_for_arm(arm)`: schema, fixed ID, arm, upstream commit/tree, approved patch SHA and official inventory SHA. This helper produces experiment metadata only, never a passing P0 attestation or a launch authorization. Unknown/partial fields or a null experiment fail closed.

CI prepares only the new run workspace after validation. It writes the runtime record outside the project, then binds its canonical path and actual bytes in `runtime-gate.json` as `experiment_record={path,sha256}`, alongside the release source commit. Each phase calls the normal launcher with `--experiment-record`; the launcher validates the record/gate/current-checkout match before allowing Local manifest handling. A record without its approved gate, a gate without its explicit record, or any player phase is rejected. Whole-package/source integrity is checked before and after each phase, including native failures. Original native errors remain primary when verification/capture also fails.

## Acceptance and evidence

The patched arm must complete import/on, EditMode/on, EditMode/off, PlayMode/on and PlayMode/off in order. Tests retain **exact 49/1 identities**, all Passed, zero skipped/inconclusive, actual query BurstDiscard on/off witnesses and safety checks. Original disposal checks retain sum 37265, witness [1,17], owner absence before teardown, no repeat disposal after another update, and final witness. Both domain-reloaded entry/exit cycles and the strict PlayMode logging assertion must finish. A passed patched Editor control does not prove original-control, player, mobile, clean-repeat-import or S2 completion. The empty default World's ownership remains source-backed unless separately observed; these fixtures do not establish comprehensive leak freedom.

Each arm preserves its original complete logs/XML, exact source/dirty delta, real lock/manifest, PackageInfo, compiler text, source and package snapshots, per-file inventories, exact patch and integrity reports. Snapshots are immutable. Unsafe or oversized drift is reported as incomplete evidence and blocks success. Early review includes bounded complete small metadata only, under the existing 16 MiB cap; full numbered evidence retains the package snapshots within the unchanged total archive limit. Artifact names include run, attempt and `git-control` or the exact arm, preventing same-name attempt confusion.

Static verification uses the regular Lab and CI unittest suites. Tiny Git fixtures exercise rejection cases without pretending to compile Unity. A separate full-official-tree materialization check verifies both arms locally without Unity. The source-review hashes track pending bootstrap/instrumentation changes and do not inherit the original bda native compilation pass.

## Local verification receipt

[The frozen local receipt](Validation/20261008-cleanup-variant-preparation/local-verification.json) records **63 Lab + 33 CI Python tests passing**, static preflight/source-hash checks, and full-official-package materialization. Control has zero changed files and 23,986,607 bytes; variant has one changed file and 23,986,610 bytes. All 1,235 files are verified in each. No gate, Unity process or real Local lock was created. The materialization module/spec/patch/inventory bytes match this candidate.

Package verification/capture is bounded at 2,048 files, 4,096 entries, 16 MiB per file and 64 MiB total per tree; the genuine largest file is below these limits. Exceeding them is an explicit failure/incomplete-capture report. Negative controls include foreign pin/patch, secondary changes, unsafe paths, manifest/dependency drift, false provenance, changed fixture hashes, equal-count wrong test identities and late gate/record changes. Independent review closed both observed error-precedence/binding defects with focused regression proofs.

The approved unified patch contains a required leading-space marker on one blank context line. Generic whitespace checking flags that data line; implementation/docs pass whitespace checks, while the unchanged patch is checked by its frozen SHA256 and `git apply --check` against the exact unpatched source.
