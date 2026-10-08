# Shooter native allocation diagnostic

The current isolated workflow runs the [ordinary-counter A/B platform control](ShooterAllocationPlatformControl.md).
The allocation-stack and Editor-inclusive captures below are retained historical
experiments; the long Editor-inclusive route is not being repeated.

This is an intrusive allocation-attribution run, **not the full native gate** and not
a production optimization. It investigates the retained Shooter steady-window
failure: remote `ef332695fc19ee4ddc0f3b26556b70d3b077b2fb`, native run `37802878017`,
reported three 41-byte GPU observations (123 bytes), exceeding the existing two-frame
limit. DataTexture reported zero. That evidence has no allocation callstack, so source
ownership remains unknown. The diagnostic starts from the identical local tree at
`373ecd933d71cde15293c83ab2acb26006cee87f` (`79edca80f06beffe903dab6b42a0e90d3699315e`). A passing diagnostic
does not close that failure or replace full, uninstrumented validation on the final head.

## Historical isolated allocation-stack execution

The allocation-stack version of `.github/workflows/unity-shooter-native-allocation-diagnostic.yml` responded only to a
push to the exact branch `diagnostic/shooter-native-allocation-20261008`. It uses the
existing licensed `[self-hosted, unity]` runner and requires the existing
`UNITY_SELF_HOSTED=true` repository variable. `UNITY_EDITOR_PATH` remains optional.
It does not install dependencies, alter runner permissions, publish screenshots, or
push repository changes; its token has `contents: read`.

The ordinary self-hosted workflow ignores this branch's pushes and rejects its
pull-request head or manual-dispatch ref. The Docker Unity workflow also rejects
that ref/head. Other branches, including `main`, retain the ordinary full EditMode
and PlayMode path. Both ordinary workflows explicitly set `SPF_SHOOTER_GC_CAPTURE=0`;
the ordinary local launcher also forces it off, and Docker explicitly passes zero
into its container. An inherited runner variable cannot enable this capture in the
normal gate. Existing Story manual diagnostics remain separately controlled.

On an already provisioned native runner, the explicit local command is:

```sh
Tools/ci/local-unity-tests.sh --shooter-gc-diagnostic
```

The launcher accepts only no arguments (the normal full gate) or this exact flag.
It exposes no arbitrary filter parameter. The diagnostic sets capture to one and
uses this fixed `-testFilter`:

```text
ShooterFoundation.Tests.PlayMode.ShooterPlayTests.WarmSteadyFrameAndPresentationOnlyQuality
```

That selects the existing `GpuDriven` and `DataTexture` cases in one graphics-enabled
PlayMode editor invocation. It reuses local editor discovery, native arm64 launch on
Apple Silicon, project-process cleanup, Burst-cache preparation, bounded log tails,
and result reporting. Apple Silicon without an arm64 editor fails before launch.
The diagnostic deliberately bypasses the normal launcher's crash-retry function:
there is **one invocation, no retry, and no Burst-disabled fallback**. Burst's actual
state belongs to the per-tier capture settings/precondition; an absent disable flag
alone is not evidence that Burst ran. Missing, extra, unrelated, or skipped test cases
make result-scope validation fail, even if Unity returns zero.

The original 150-frame warmup, 180-frame measurement, two-allocating-frame maximum,
gameplay, and presentation checks remain unchanged. With capture enabled, the test
appends retained marker calibration controls after the original window; control
allocations are labelled separately and do not replace or reduce the original budget.
Profiler overhead makes this a diagnostic result rather than ordinary acceptance.

## Frame mapping and controls

The governor reads Unity's [last completed frame value](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Unity.Profiling.ProfilerRecorder.LastValue.html).
Each of the original 180 iterations stamps the current Unity frame before its existing
yield, then records the governor after that yield. Native metadata maps that source
frame to a profiler frame; the exporter does not assign profiler indices by arithmetic.
Coroutine-time tick, timers, feedback, audio counters, effects, HUD rebuilds, HP/coins,
upload counts and quality are context only. A full profiler frame includes work before
and after that coroutine boundary.

All observer/control methods and storage are warmed before the existing 150 warmup
yields. After observation 180, counter validity, frames, allocating frames, bytes and
process-wide generation-0 collections are frozen. The original sample CSV and GC report
are written immediately, so a later diagnostic exception cannot erase them. Only those
saved totals feed the unchanged allocating-frame assertion.

Six calibration blocks then run for four frames each: an empty marker and a retained
array allocation in Update, LateUpdate and the coroutine. Payloads are 4,096, 8,192 and
16,384 bytes respectively; actual managed object bytes remain separately measured.
For each positive marker, the exporter requires a single marker, allocation samples,
recorded callstacks, enough retained bytes and exactly one matching governor observation
within the bounded neighboring-read search. All three phases must establish the
expected one-frame lag. Empty markers require no allocation inside that scope; unrelated
all-thread allocations in the same frame remain visible. Ambiguity, missing frames,
duplicate metadata, invalid/negative counters, inconsistent cumulative totals or a
changed endpoint fail diagnostic completeness.

The capture records CPU and Memory with allocation callstacks, deep profiling off,
a 300-frame history, a 64 MiB buffer, 204 stamped source frames and three receiver-drain
yields. These are fixed bounds. Raw history also contains retained surrounding frames;
CSV attribution is restricted to uniquely mapped source frames. The profiler's existing
enablement, area selection, history preference, memory mode/budget, Editor inclusion,
deep profiling and binary-log settings are restored in bounded independent steps.
Scene teardown still runs if restoration reports an error.

`mappingAndCalibrationComplete` means those mechanical checks passed. It does not claim
complete attribution: missing stacks and unresolved addresses are counted, observer
bytes are included, and omitted EditorLoop work cannot be excluded by elimination.
[Allocation callstack recording adds overhead](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Profiling.Profiler-enableAllocationCallstacks.html).
The optional `SPF_SHOOTER_GC_INCLUDE_EDITOR=1` enabled the reviewed follow-on capture
described below. The current dedicated workflow forces it off for the platform
control; ordinary tests also force capture off.

## First native capture and Editor-inclusive follow-on

Native [run `37820933270`, attempt 1](https://github.com/karosLi/SPGameFoundation/actions/runs/37820933270)
captured remote `a0fac55b88a68b0452acf12fe1040bc0daa75b42` (local
`3c961dcb46e3bcde3f61341ea256abda6f41fc6c`, tree
`99cfbce6666caaf0c58caa50e4e0d513c90e789f`). Unity 2022.3.62f2 on macOS/Metal
reported Burst enabled and `includeEditor=False`. Both targeted cases failed the
unchanged two-allocating-frame gate: each governor reported **180/180 frames and
8,430 bytes**, with zero process-wide generation-0 collections. This is retained
intrusive diagnostic failure evidence, not an ordinary acceptance result.

All 204 source frames mapped uniquely in each tier; empty and retained controls in
Update, LateUpdate and the coroutine passed and established a one-frame governor
lag. In each original 180-frame window, the allocation CSV contains exactly
**180 samples of 40 bytes = 7,200 bytes**, all with the same resolved managed path:
`LogScope.EvaluateLogScope` (Test Framework 1.1.33, `LogScope.cs:179`) through
`UnityLogCheckDelegatingCommand.CheckFailingLogs` (`:104`). There are no missing
callstacks in those 180 samples; each also contains one unresolved native address.
The capture observer's named scopes contain zero allocation bytes. This establishes
the owner of those recorded 40-byte samples only.

The governor exceeds the recorded samples by **1,230 bytes per tier**, equivalent
to thirty 41-byte amounts. GPU differences are 82 bytes at source ordinals
7, 19, 31, 43, 55, 67, 79, 91, 103, 115, 127, 139, 151, 163 and 175. DataTexture
has eight 82-byte differences and fourteen 41-byte differences, with some pairs
split across adjacent frames. No 41-byte allocation sample exists in either gate
export. This cadence resembles the original ordinary failure but does not identify
its allocation source. The larger all-phase stack totals also include post-gate CSV
writing and positive controls; they must not be assigned to the steady window.

Unity's [Profiler marker documentation](https://docs.unity3d.com/2022.3/Documentation/Manual/profiler-markers.html#main-thread)
states that Play-mode profiling omits detailed EditorLoop samples and that targeting
the Editor exposes them, with PlayerLoop nested inside EditorLoop. Omitted Editor
work is therefore a hypothesis for this measured gap, not a proven attribution.
The follow-on changes only the dedicated workflow's existing Editor-inclusion switch.
It retains the 150/180 schedule, gate, six controls, 204 stamps, 300-frame history,
64 MiB buffer, three drain frames, single invocation and no retry/fallback policy.
The helper saves `ProfilerDriver.profileEditor` before setup and restores it in its
independent cleanup steps, including when another restoration step fails. Expanded
capture may consume more history or memory; missing frames or failed controls remain
incomplete evidence and cannot support a source or zero-allocation conclusion.

Both original artifact ZIPs, IDs `11569815015` and `11569705618`, were downloaded and
their published hashes checked. Matching manifests, both binary parts, the restored
archive and all 22 file hashes passed `evidence_parts.py restore`. The archive has
18,885,016 bytes and SHA-256
`53c84085432480c506cb20aa367c3c1bad6fd66e9e73926b62c6d1dd202ed70c`.
The original XML, log, raw captures and CSVs remain preserved. No product-source
change or allocation subtraction is justified by this first capture; the original
uninstrumented failure remains open until attributed, fixed if necessary, and
checked by the full ordinary native gate.

## Evidence and failures

The launcher clears only the dedicated `Artifacts/` directory after argument
validation and records run provenance before checking editor prerequisites. The
workflow packages evidence even after test failure. Evidence includes:

- `shooter-diagnostic-run.txt`: exact checked-out head, GitHub SHA/ref/run/attempt,
  UTC start, selected editor/version/host, fixed scope, unchanged budget, and exit codes
- `playmode-shooter-gc-diagnostic-results.xml` and `.log`
- `shooter-steady-frame-samples-{GpuDriven,DataTexture}.csv` and `perf-gc.txt`
- `GC/shooter-<tier>-<UTC>/steady-window.raw`, `-settings.txt`, `-summary.txt`,
  `-frames.csv`, `-observations.csv`, `-allocations.csv`, `-stacks.csv`, and `-controls.csv`

Startup failures may have only run provenance and available logs. Missing raw data,
failed mapping/control checks, or unresolved stacks remain explicit evidence gaps.
Do not infer allocation ownership or harmlessness from a green rerun, missing samples,
or the absence of an application stack.

Artifacts are named `shooter-native-allocation-diagnostic-part00` through `part39`,
with only nonempty parts uploaded. The unchanged [bounded evidence format](CiEvidence.md)
retains every file, 16 MiB binary parts, per-file/part/archive hashes, and the existing
640 MiB compressed maximum; overflow fails packaging instead of dropping files.
Download all parts from the same run attempt and restore them with
`Tools/ci/evidence_parts.py restore`. The job summary shows only bounded metadata and
summary excerpts; full XML/log/raw files remain in the artifacts.

## Local isolation checks

```sh
bash -n Tools/ci/local-unity-tests.sh Tools/ci/unity-tests.sh
python3 -m unittest discover -s Tools/ci -p 'test_local_unity_tests.py' -v
```

These standard-library checks use temporary fake editors and mocked process/architecture
commands. They cover normal full-suite routing/capture off, exact diagnostic filtering,
single invocation, failed/signal exit preservation, invalid arguments, incomplete XML,
arm64 selection, branch guards, and bounded evidence upload wiring. They do not start
Unity, exercise native Burst, measure allocations, or validate the Mac runner. Native
capture and final full uninstrumented acceptance must be reported against their own
exact commit and run evidence.

The implementation was additionally compiled with native Editor/test defines, Unity's
pinned NUnit 3.5 binary and the actual Unity Test Framework attribute/combination sources.
Engine and profiler APIs were compile-only stubs, not native execution. The transitive
harness NUnit 3.13 reference produces the expected version warning; the primary NUnit
binding is 3.5. Synthetic profiler data exercised the real extraction code in 21 positive
and negative cases, and three restoration cases verified saved settings including an
injected history-setter failure. These checks preserve a synthetic failed three-frame
budget rather than converting it to success. They establish exporter behavior only;
the first native diagnostic's results are recorded above. The Editor-inclusive
follow-on failed completeness as recorded below; ordinary acceptance is separate.

For the Editor-inclusive workflow change, shell syntax and all 10 launch-isolation
checks passed. Two additional fake-editor checks verified the switch reaches the
single diagnostic invocation and cannot enable capture in an ordinary invocation.
The unchanged extraction helper passed 22 synthetic cases, including controls nested
under EditorLoop/PlayerLoop and all prior negative cases. Sixteen restoration cases
covered both original/requested Editor states, both history-preference states and
an injected history-restoration failure; each preserved the fixed capture bounds
and restored the original Editor state. These are compile/stub checks, not native
proof that the expanded capture fits its buffer or identifies the missing bytes.

## Editor-inclusive native result

Run [`37825087420`](https://github.com/karosLi/SPGameFoundation/actions/runs/37825087420),
remote `ddf56e9db526c27a8a136209247098cb4719bdbb`, completed with both cases failing
diagnostic mapping/calibration. Each had 204 source observations but zero mapped
frames and zero captured control markers. The governor's 0/180 readings are invalid
evidence because retained positive controls were also absent. No product or engine
allocation source was established.

Both raw files retain 300 consecutive records, spanning only 31.21 ms (GPU) and
38.89 ms (DataTexture), rather than the approximately 3.4-second source/control
window. All requested metadata GUIDs and probe marker names are absent. Binary
timing fields were cross-checked against all 408 native frame-duration rows from
the earlier valid capture. The loss timing and counter-mode semantics remain
distinct uncertainties; retaining more history alone would not validate the counter.

The launcher sampled peak RSS of 9,402 MB once per second. The separate shutdown
`ALLOC_PROFILER_EDITOR` report shows 28.21 GB allocator high-water usage, not process
resident memory. The 64 MiB stream parameter does not cap whole-process RSS.
Further long Editor captures and history/memory expansion are stopped.

Artifact `11570673620` and all 22 members verified. Archive: 2,124,283 bytes,
SHA-256 `f75005ba1b3963c70592efdfc0b8897d12cb3bc2d13d90420fa2aefb058b2c93`.
The next bounded investigation is the default-off [platform control](ShooterAllocationPlatformControl.md);
its results cannot substitute for the unchanged original Shooter allocation gate.
