# Patched Local experiment: source and execution semantics audit

Run [37813671446, attempt 1](https://github.com/karosLi/SPGameFoundation/actions/runs/37813671446/attempts/1), source `b6ef271fc8eded93c6e79c0dfcf90917b72b7945`, tree `dbda0866da350caa91029ce2d1cae855c61ce06b`.

The restored native results support a passing **single patched Local Editor experiment**: import/on, EditMode/on 49/49, EditMode/off 49/49, PlayMode/on 1/1, PlayMode/off 1/1. All case identities match the source-derived Cartesian products; zero failures, skips or inconclusive results. This audit did not start Unity, modify source or refs, publish anything, or queue another run.

## Exact source and retained controls

The reviewed clean worktree is the known publication precursor `33a92bc3c8cc3d23bbf99c34190491d9e2722184`; its tree matches the run tree. All 44 frozen owned-file hashes match that worktree and the unpatched Local control's frozen inventory. Every patched launch matches the 26 frozen oracle hashes and the 41 frozen inputs directly included in launch.inputs_sha256. The other three entries are the variant spec, patch and upstream inventory, retained separately with package snapshots. All 11 C# files and four assembly definitions are therefore bound to the reviewed native inputs.

Both original controls remain failed. Original Git control `bda5bc7`, run 37761086135 attempt 2, and unpatched Local control `fe946e2`, run 37785324983 attempt 1, each retain 45/49 with the same four failed identities:

- TrackedWriterReaderAndDisposal(1,DestroyEntity)
- TrackedWriterReaderAndDisposal(8,DestroyEntity)
- TrackedWriterReaderAndDisposal(64,DestroyEntity)
- ReenterPlayModeTwiceWithDomainReload

Every failure reaches CollectionProbe.cs:79, after the first reactive update's sum 37265 and disposal witness [1,17] checks passed. The failure is that the destroyed owner still exists before World teardown. The exact remaining component inventory was not dumped. The second reactive update and normal final checks were not reached in these failures. The patched results do not overwrite or relabel these original failures.

The retained patched target has SHA256 `087ddf701e638d56fb01b80ef4becc27ae7e0516c504fc3e4bdc09d2dc99ab6d`. At upstream `381a77dbf774ff603014d5695ef6c06abaa25d96`, addQuery selects the exist marker without the cleanup marker, while removeQuery selects cleanup without exist. SyncQueries removes storage and completes disposal handles for removeQuery before line 175. The sole approved line-175 addQuery→removeQuery change removes the cleanup marker from that same removal set.

## What the passing fixtures establish

Core tests schedule a 257-element writer through tracked Latios systems, then a tracked reader, with real writer batch sizes 1/8/64. Disposal chains a final-read/count witness before disposing the owned Values array. No explicit fixture job completion before removal substitutes for framework ownership.

The DestroyEntity cases establish sum 37265, witness [1,17], owner absence before whole-World teardown, unchanged disposal count after a second initialization update, and final checks after teardown. These values are inferred from the unchanged strict conditions and Passed outcomes; they are not separate raw-value logs. Ordinary Remove checks component absence before teardown and final sum/witness afterward. The expected post-schedule-exception test separately checks witness [1,17] immediately after explicit removal, before its World using-scope ends.

Each EditMode control completes two domain-reloaded PlayMode entry/exit cycles: DisposeWorld first, DestroyEntity second. Full logs independently record two EnterPlayMode and two ExitPlayMode samples in both modes. Standalone PlayMode runs three iterations of DisposeWorld, fixture-2 parallel pairs at subdivision 2, and known sphere queries, yielding a frame each iteration and ending with LogAssert.NoUnexpectedReceived. **Standalone PlayMode does not exercise DestroyEntity or explicit Remove.**

The query job's BurstDiscard marker starts at 1 and becomes 0 only in managed execution. Passing conditions require 1 in on mode and 0 in off mode. Both the dedicated EditMode query case and PlayMode query calls pass. Off launches/logs show Burst disabled; the import environment records Burst enabled and Burst safety enabled; all five compiler snapshots retain safety defines. This directly witnesses the QueryJob backend, not every upstream pair or collection job. BurstCompile annotations and IL postprocessing are not complete per-job execution traces.

Psyshock runs 36 pair cases per Editor mode: four finite sphere fixtures × immediate/single/parallel × subdivisions 1/2/4. Normalized source-index pairs reject duplicates and match an independent inclusive AABB-set oracle, including empty/single inputs, tangency, negative/outside bounds, a large cross-cell body, depth separation, coincident zero-radius points and adjacent floats. Real distinct ECS entities preserve alias checks. Known scheduled sphere ray hit/miss, signed distance and threshold-rejection queries also pass. Set equivalence does not establish visit order, solver behavior, arbitrary-shape correctness or performance.

## Compilation versus runtime scope

Five retained compiler snapshots each contain 12 original files totaling 138,660 bytes. All 60 file hashes and sizes verify. Every assembly response file directly references Entities, Collections and Burst and contains UNITY_EDITOR, ENABLE_UNITY_COLLECTIONS_CHECKS, ENTITY_STORE_V1 and UNITY_BURST_EXPERIMENTAL_ATOMIC_INTRINSICS. The pinned Latios source-generator path is present. No generated C# files were retained, so generated text was not inspected.

The import log has Csc, ILPostProcess and CopyFiles events for all four owned assemblies and all 13 Latios assemblies. This includes optional modules in the full package. It does not mean their runtime algorithms or native plugins were exercised. Runtime tests cover Core collections, small Psyshock array/query paths and empty QVVS installation/update. Two QVVS worlds have distinct sequence numbers and update/dispose, but no populated hierarchy is tested. The managed-system inventory is not an unmanaged-system execution trace.

No optional Kinemation, Myri, Calligraphics or Mimic installer, scene-streaming pipeline, renderer/audio workload, gameplay or SPF bridge acceptance is established. The empty default World's lifetime has a source-backed ownership design and the observed entries have no bootstrap assertion. The suite and shutdown 'no leaked weakptrs' lines do not establish comprehensive managed/native leak freedom. Nonfatal Xcode Info.plist warnings remain in logs; the Editor pass does not verify player-build tooling.

## Remaining gates

Repeat clean import and the Editor controls in a separate fresh project/cache remain pending. Desktop IL2CPP build and actual player execution, physical Android/iOS validation, product integration and S2 remain pending. No optional-module runtime, broad leak or performance claim should be derived from this experiment.

`source-semantics-audit.json` records the exact original XML paths and hashes, every expected/result identity, original failure stacks, source hashes, compiler-file verification, log line references, environment and coverage limits. Original restored evidence was read without alteration.
