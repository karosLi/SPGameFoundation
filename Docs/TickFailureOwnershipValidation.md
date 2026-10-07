# Bounded tick scheduling-failure ownership

This follow-on closes the specific `BeginTick` recovery gap left open by [composition rollback](CompositionRollbackValidation.md). Its runtime/test implementation is **`e27fb5eb01eb4ada369aa26f21408aa663f0cbbe`** (including `84cb48c`), based on `02e9b916ccbbe15bd82b1e59c92c9f46f70fee87`. It changes only `TickPipeline` and adds targeted tests. It does not introduce manifests, new phases, hot unload, a new save format, or gameplay rollback.

## Contract and limits

- A successful `OnTick` return transfers its returned `JobHandle` to the pipeline immediately. A temporary ownership slot covers failure during diagnostic `SerialProfiling` completion or dependency registration. Successful profiling still records the completed local handle, preserving the original serial dependency values.
- If scheduling, a barrier completion, profiling completion, or batch submission throws, the original exception object/stack remains primary. Recovery failures are attached through the existing `CleanupErrors.DataKey` (`SPF.CleanupFailures`) aggregate.
- Recovery publishes known handles as pending **before** attempting completion. If completion throws, `HasPendingTick` remains true, handles stay retained, no `World.Sync` notification runs, and `EndTick` or `Dispose` can retry. The existing Session owner refuses World cleanup until pipeline cleanup actually finishes.
- Only after every known handle completes are tracker state and ownership cleared. `World.Sync` is an after-completion notification. An `OnSync` failure cannot mask the scheduling cause and does not strand already-completed storage. Notification order stays unchanged and remains fail-fast: earlier callbacks may already have run, and later callbacks may not have run. Failed notifications are not automatically replayed.
- `BeginTick`, `EndTick`, reset, snapshot operations, and pipeline/Session disposal cannot reenter an active scheduling/recovery callback. Disposal during ordinary `OnSync` is also rejected until notification returns. This blocks owners from freeing World storage while a scheduler or sync callback is still using it.
- Begin/playback/system/sync profiler markers and the AccessGuard have balanced `finally` paths. Normal successful scheduling order, dependencies, barrier behavior, phase order, `LastTickTime`, snapshot bytes, defaults, and capacity semantics are unchanged. Failed scheduling does not count as a successful tick in pipeline statistics.
- **A throwing `OnTick` still owns any private handle it never returned.** It must complete that work before throwing; the pipeline cannot infer an unknown handle or make an arbitrary incorrect callback safe. One test demonstrates a system fulfilling this obligation with a scheduled `IJob` and `finally` completion.
- This is ownership recovery, **not a simulation transaction**. Clock advancement, command playback, prior systems' writes, queued events, and callback side effects already performed are not rolled back. Callers choose whether to recover, restart, or dispose after the exception.
- No hot-loop allocation was added. The ownership fields are value types; error aggregation/delegates are on cold failure/teardown paths. The completion-injection hook exists only under `SPF_DOTNET_HARNESS`; ordinary Unity Editor/player builds have no hook field, hook invocation, or test delegate branch.

## Red-first and focused evidence

The initial **21 real-callback cases** ran against unchanged `02e9b91` production sources: **16 failed / 5 passed**. They reproduced scheduling-cause masking by `OnSync` and unsafe reentry during scheduling/recovery. The fixed callback suite plus existing composition rollback cases passed **57/57**.

Four additional **harness-only simulated pre-completion rejection** cases cover retained pending ownership, repeated failed cleanup without World release, successful later completion/disposal, serial profiling, and barriers. A baseline run changed only the four `Complete` call sites to pass through the same harness hook, without changing the baseline's ownership/recovery logic: **3 failed / 1 passed**. These are deliberately not described as native `JobHandle.Complete` failure injection.

The final focused suite passes **28/28**. The original 21 callback cases and three completed-dependency positive controls are compiled for Unity too; returned work uses an actual `IJob` writing a small World `NativeArray`, and `OnSync` checks that result. In the .NET harness those jobs run eagerly, so the harness does not establish worker timing or Unity safety-handle behavior. The four simulated cases are excluded from ordinary native test builds.

Self-review caught a success-path compatibility detail before delivery: Unity's [2022.3 JobHandle binding](https://github.com/Unity-Technologies/UnityCsReference/blob/2022.3/Runtime/Jobs/ScriptBindings/JobHandle.bindings.cs) calls native completion through a ref receiver. A by-value helper would discard that receiver mutation. The final helper completes a temporary copy and assigns it back by ref only on success, retaining the original owned handle if completion throws. Profiling retains ownership in the temporary slot while preserving the old post-completion tracker registration value. Three native-compilable positive controls check direct completion clears a real scheduled handle, then check empty dependencies for an ordinary barrier, a profiled barrier and a profiled declared reader. The eager default-handle harness cannot distinguish this native receiver-mutation regression; these controls await native execution. The earlier 1,143-test full pass is not used as final acceptance.

## Frozen-source validation

Machine-readable results, input hashes, log hashes, and the separately reviewed Stage A source differences are in [TickFailureOwnership-20261007.json](validation/TickFailureOwnership-20261007.json).

- All **76** generated harness projects build with **zero warnings / zero errors**. The corrected final full run passes **1,146 tests / zero failures** (1,118 prior cases plus 28 new cases). The two existing explicit exports, `SearchShots` and `ExportFrames`, remain unrun.
- Real Unity 2022.3.62f2 API compilation passes **332 sources / 95 real Unity/package references**, with **seven existing field warnings / zero errors**. This includes 329 production sources, the two retained composition/initializer rollback test files, and the new tick-ownership test file. The harness-only hook and simulated tests are absent from this build. This is .NET-hosted API compilation, not native engine execution.
- Both cold composition probe runs match byte-for-byte and retain the Stage A SHA-256 **`a42c9f27d2bb8d277303eef24e5b4cdbd58f0035d8bca89c7e08089348dd5f30`**. All **19 compositions / 65 named fixture methods** match the retained inventory. Of 154 pinned sources, **136 are unchanged / 18 differ** from Stage A; the hashes remain recorded separately, with only `TickPipeline.cs` newly changed by this checkpoint. The historical inventory was not overwritten. A premature probe compile with reference builds disabled ran before the full harness had built Story and failed for that missing assembly; the later ordinary build and both actual comparisons passed.
- Independent review found no blocking ownership/reentry issue and checked the profiler/AccessGuard `finally` structure. Stub markers are no-ops, so profiler nesting is code-reviewed, not claimed as an instrumented native measurement.

## Explicit remaining validation

Native Unity EditMode execution, graphics PlayMode, Burst and physical Android/iOS remain separate gates. No native engine completion failure was induced. Native allocator/`CombineDependencies` failure and a failure inside `ScheduleBatchedJobs` are code-reviewed paths, not executed fault-injection results. Arbitrary concurrent use or direct caller disposal/mutation of the borrowed World is outside the pipeline ownership boundary. Existing test thresholds and snapshot fixtures were not weakened; no mobile performance or memory-safety claim follows from stub timing alone.

## Reproduce

Use the normal harness dependency setup, then:

```sh
python3 Tools/DotnetHarness/generate.py
dotnet build Tools/DotnetHarness/.gen/Harness.proj -m:1 -p:UseSharedCompilation=false -p:NuGetAudit=false
dotnet test Tools/DotnetHarness/.gen/Harness.proj --no-build -m:1
# Focused (28 cases in the harness; 24 native cases):
dotnet test Tools/DotnetHarness/.gen/SPF.Tests.EditMode/SPF.Tests.EditMode.csproj --no-build --filter FullyQualifiedName~TickFailureOwnershipTests
```

For the real API compile, use [GenerateCompositionApiCompile.py](validation/GenerateCompositionApiCompile.py) as documented in the composition checkpoint. Before building its temporary project, append a `Compile` item for `Assets/SinglePlayerFoundation/Tests/EditMode/TickFailureOwnershipTests.cs`; do **not** define `SPF_DOTNET_HARNESS`. Keep the normal harness run separately, because the combined API compile does not enforce asmdef boundaries.

For cold compatibility, follow [the retained probe instructions](validation/FoundationCompatibilityProbe-20261007.md). Compare actual composition fields and fixture names; record changed source hashes separately rather than accepting or replacing the Stage A source baseline silently.
