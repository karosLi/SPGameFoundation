# Latios 2022 S1a laboratory

Status: **original control imports; EditMode/Burst-on executes 49 cases, with 45 passing and 4 strict cleanup failures**. [Run 37761086135 attempt 2](https://github.com/karosLi/SPGameFoundation/actions/runs/37761086135/attempts/2) confirms the inventory and initialization-ordering corrections. [Original cleanup evidence and independent owned bootstrap correction](Docs/BootstrapContractCorrection-20261008.md) preserve both the upstream-query finding and two PlayMode-entry bootstrap assertions. The bootstrap correction is statically reviewed but not yet run; all later on/off phases remain NOT_RUN. This is the isolated historical control group from [the integration blueprint](../Docs/LatiosUnity2022IntegrationBlueprint.md#7-轨道二-建立真实的-latios-2022-兼容线), not an SPF backend or S1a completion.

[The owned-domain review](Docs/CompilationDomainReview-20261008.md) covers all 11 C# files and four asmdefs. The original attempt compiled all four and retains two complete bounded compiler snapshots. Current frozen hashes identify the pending bootstrap change separately from that native evidence.

- Unity: exactly **2022.3.62f2**
- Latios: **0.11.5**, commit `381a77dbf774ff603014d5695ef6c06abaa25d96`
- Declared control dependencies: Entities 1.3.5, Entities Graphics 1.4.2, Burst 1.8.18, DSPGraph 0.1.0-preview.22
- Host-project choices: URP 14.0.12, matching this Editor's actual registered built-in package, and Test Framework 1.4.5, required by Collections 2.5.1 and observed in the first lock; required standard engine modules remain explicit
- Collections/Mathematics/Serialization: dependency graph remains 2.5.1/1.3.2/3.1.1, confirmed in the first failed import. The launcher checks those versions without manufacturing a lock for the corrected input.
- The initial PlayerSettings file is a small authored input, not an Editor-produced settings dump. Unity must normalize it, and the native environment check must confirm the actual defines and .NET Standard API.

The entire upstream package remains a Git dependency. Optional runtime installers are not called. This still brings its other assemblies, generators, native plug-ins, Graphics and DSPGraph dependencies into package import/compilation. No renderer, audio, text or animation compatibility is inferred from Core/Psyshock tests. [Module/platform matrix](Docs/CompatibilityMatrix.md).

## Safe preparation now

From the repository root:

```sh
python3 Latios2022Lab/Tools/lab.py preflight
python3 -m unittest discover -s Latios2022Lab/Tools -p 'test_*.py' -v
```

Neither command imports packages or starts Unity. The [deliberate CI launcher](../Docs/Latios2022LabCI.md) requires a separately reviewed release request on its exact lab-only branch; ordinary stage pushes do not run the lab. The existing root `Assets`, `Packages`, `ProjectSettings`, `Library` and test launcher are not used by this lab.

The first genuine lock is preserved verbatim under [Docs/Validation](Docs/Validation/20261008-first-import/provenance.json), outside active `Packages`. It belongs to the failed source and is not promoted to a successful control. Active `Packages/packages-lock.json` remains absent until Unity resolves the corrected manifest. Never copy a sample lock, manufacture one from this README, or report manifest pins as resolved versions. Review the next real lock and actual registered versions before committing it separately with exact environment evidence.

After import, the launcher accepts only the observed `com.unity.toolchain.macos-arm64-linux-x86_64` 2.0.5 manifest addition, and only with a matching registry entry in the actual lock. This keeps later phases usable after the Editor's known normalization; it does not add the toolchain to authored input or authorize installation. Other additions, pin changes and manifest configuration changes fail closed.

## After P0 and runner release

The coordinator first verifies the precise SPF P0 native acceptance, obtains exclusive runner time, and writes a short-lived JSON gate outside tracked source. This is an operator attestation, not automated verification of the linked CI result. Required keys:

- `schema`: 1
- `project_path`: exact absolute path ending in this checkout's `/Latios2022Lab`
- `p0_commit`: the actually verified full 40-character commit
- `p0_native_status`: `passed`, only after inspecting results
- `p0_evidence_url`: the inspected HTTPS run/artifact reference
- `runner_reserved`: true; `coordinator`: the responsible coordinator
- `expires_utc`: real, timezone-qualified future expiry for that reservation
- `allowed_phases`: the explicitly released subset of `import`, `editmode`, `playmode`, `player-build`
- For `player-build` only: `s1a_editor_status`: `passed`, after both Burst controls and reentry checks actually pass

No ready-made passing gate is shipped. Do not forge one to bypass an unresolved gate. Revalidate the reservation between commands. The per-project exclusive launch directory does not replace the coordinator's shared-runner ownership.

First inspect the plan (no Editor launched):

```sh
python3 Latios2022Lab/Tools/lab.py import --editor /Applications/Unity/Hub/Editor/2022.3.62f2/Unity.app/Contents/MacOS/Unity --target StandaloneOSX
```

When released, add `--execute --gate /absolute/path/to/released-gate.json`. Run sequentially:

1. `import --burst on`: actual import, environment inventory and genuine lock capture. Verify every compile/UPM/generator error; preserve failures.
2. `editmode --burst on`, then `editmode --burst off`: all 49 expected cases, no failure/skip/inconclusive result. The off flag is per-process; no persistent Burst preference is changed.
3. `playmode --burst on`, then `playmode --burst off`: repeated ownership/query smoke with actual frames. Do not use `-nographics` for reentry or PlayMode.
4. Repeat clean import and those gates in a new dedicated worktree/lab with its own Library and UPM cache. Review the two real locks and environment inventories for drift. Do not delete/reuse the product Library to obtain a "clean" result.
5. Only after Editor gates, `player-build --burst on` builds the selected **desktop IL2CPP development** smoke. Install the matching IL2CPP module through an authorized setup if missing; stop rather than switch backend. This command builds only. Launch the resulting player separately on that actual host, retain its exit code/log and `latios-s1a-smoke.json` from `Application.persistentDataPath`, and verify `passed: true`. Hash the actual player artifact. A build success is not an execution pass.

Use `--target StandaloneLinux64` or `StandaloneWindows64` for those approved desktop controls with the matching installed Editor/IL2CPP modules; the default Mac target is explicit and never inferred to prove another platform. On macOS the launcher reads physical ARM64 capability and uses `arch -arm64` even if its runner is under Rosetta. It does not install software, alter signing/security, kill existing Unity processes, retry without Burst, or change Mac/Editor preferences.

After checking the external gate, the launcher first runs the selected binary with `-version` and no project path, rejecting anything except 2022.3.62f2 before it can import/normalize the lab. Every Editor test phase also checks the actual `Application.unityVersion`. The launcher fixes `-projectPath`, working directory and all UPM caches under this lab, checks symlinks/project locks, records source commit, dirty state, input hashes, command and gate, and checks root product manifests/settings stayed unchanged. Run evidence is unique under ignored `Artifacts/`. A native nonzero exit stays a failure; it is not retried under a weaker configuration. Review generated lab ProjectSettings before committing them.

## Fixtures and limits

- Core: 257 values; tracked writer and reader systems; batch sizes 1/8/64; removal, entity-destroy cleanup and World disposal; final-read/disposal-count witnesses; an expected post-schedule exception; two QVVS worlds; two PlayMode entries with domain reload.
- Psyshock: empty and single inputs, overlaps/separation, tangency, negative and outside-bounds coordinates, a large cross-cell body, Z separation, coincident zero-radius points and adjacent representable float positions. Compare normalized **source-index** pairs with independent inclusive AABB comparisons, reject duplicates, and allocate the maximum possible unordered-pair capacity with `AddNoResize`. Real distinct ECS entities preserve upstream alias checking.
- Modes: immediate/single/parallel builds and queries, subdivisions 1/2/4. The 0.11.5 public layer scheduler has no arbitrary batch-size parameter; subdivisions vary spatial partitioning, not batch size. The Core writer independently varies its true `IJobParallelFor` batch size.
- Known sphere ray hit/miss, signed inside/outside distances and threshold rejection run in an actual scheduled job. A `BurstDiscard` execution witness must match the requested mode. This witness proves that query job's backend; it does not alone prove every upstream scheduled job was Bursted.
- Array query tests do not install a Psyshock simulation/solver. A minimal QVVS install is exercised separately. The own-world bootstrap suppresses broad default runtime injection, while explicitly adding the real initialization ECB and empty SceneSystemGroup required by the pinned Core ordering attributes. Each fixture owns disposal; no scene-streaming children or optional Latios SceneManager are installed. A separate empty standard default World satisfies Unity's bootstrap contract and is owned by Unity's registered shutdown, without systems or a World player-loop entry.

These are small correctness/lifecycle fixtures, not gameplay, performance, GPU, broad platform or full-module acceptance. Non-finite/negative-radius inputs, arbitrary shape/mesh/compound/baking behavior, hot swapping, SPF bridge semantics, solver behavior and S1b backports are not promised. No raw SPF state/EntityHandle is reinterpreted as an ECS Entity.

## Stop conditions and remaining gates

Source review found a possible historical reactive-cleanup defect in the pinned package: its removal branch targets the add-query when removing the cleanup component. The destroy fixture requires the owner entity to cease existing and the disposal witness to complete before whole-world teardown. This deliberately remains a strict native gate; no upstream fix or waived assertion is included. See the ledger for exact source.

Classify a failure as UPM/dependency, package/internal API, generator, native safety/lifecycle, query oracle or build/AOT. Preserve the exact failing input and log. A fix may target a specifically diagnosed compatibility issue within the approved scope. Stop and reassess before upgrading Editor, editing Entities internals/layout, disabling safety, broad package splitting, or rewriting renderer/audio/generators.

Physical Android ARM64 Vulkan/GLES fallback and iOS ARM64 Metal/IL2CPP remain external gates: plug-in architectures, stripping/registration, touch/background/recovery, native memory, sustained frame times, thermal/battery and actual audio/graphics. Installing old package metadata proves none of these. Desktop smoke does not change SPF product acceptance. No S1b capability selection or S2 bridge implementation until genuine S1a gates close.

Source/API details and licences: [BackportLedger](Docs/BackportLedger.md). This lab is a local compatibility control, not an upstream-supported LTS fork or a claim that the entire package is MIT.
