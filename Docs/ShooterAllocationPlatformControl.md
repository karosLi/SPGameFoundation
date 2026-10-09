# Shooter allocation platform control: fixed A/B experiment

This is a default-off, ordinary-counter platform control. Its success means the
observations and retained-allocation controls are valid. **It does not pass the
Shooter product allocation gate, subtract background bytes, or identify a callsite.**
The original Shooter 150-frame warmup, 180-frame window and maximum two allocating
frames are unchanged, as are its retained failures.

## Why this experiment

The original ordinary run `37802878017`, remote `ef332695fc19ee4ddc0f3b26556b70d3b077b2fb`,
observed three 41-byte GPU samples, 12 rendered frames apart; DataTexture observed
zero. The first intrusive capture attributed 180 × 40 bytes to Test Framework log
checking but left 1,230 bytes per tier unrecorded. The Editor-inclusive attempt
`37825087420` failed every mapping/control: its 300 retained Editor records covered
only 31–39 ms, and positive controls were absent from the governor's reads. Neither
result attributes the original residual; neither permits background subtraction.
See [the preserved attribution investigation](ShooterAllocationDiagnostic.md).

Editor-wide profiling is not used here. The prior attempt's sampled peak RSS was
9,402 MB; the separate Editor-profiler allocator high-water report of 28.21 GB is
not process RSS. This control does not enable recording, callstacks or deep profiling,
set Editor profiling, change frame history, increase profiler memory, or write
EditorPrefs. It refuses incompatible profiler state instead of correcting it.

## Preregistered scope

One `Explicit` UnityTest executes A then B once, in that order, in one native Unity
process. Each condition has exactly 150 warmup yields, 180 observations and 24
post-window control yields: 708 scheduled measurement/warmup/control yields total,
plus one teardown yield after each condition. There is no third condition, repeat,
retry, Burst-disabled fallback or automatic rerun of Shooter.

- **A, raw recorder:** no SPF/Shooter gameplay components; a minimal camera, test
  reader and control probe remain, together with the project's loaded Editor/UTF
  environment. A is not an empty Unity project or proof that no SPF static code ran.
- **B, FrameGovernor:** the same minimal scene adds the real shared FrameGovernor,
  including thermal/battery polling, frame-rate and adaptive-quality policy.
  Settings match Shooter Playing: active/idle 60/30 FPS, idle throttling enabled,
  and `KeepAwake` before the governor's Update. No Shooter renderer/event subscriber
  is added; an effect unique to B could still be an engine response to its state.

The observer enumerates existing scene components, including inactive ones, before
each condition. Only transforms, the five named Test Framework 1.1.33 runner
components, and the exact engine type/assembly pair
`UnityEngine.Rendering.DebugUpdater | Unity.RenderPipelines.Core.Runtime` are allowed.
The latter is Core RP 14.0.12 engine background observed in the native inventory;
it remains enabled and is not removed, mutated or blamed for the residual allocation.
No namespace-wide or assembly-wide exemption is added. An unexpected camera,
renderer, Canvas, collider, script or other component still invalidates the empty
scene without deleting unrelated objects.
It records that inventory and destroys only its own objects. A runs first to avoid
prior governor/gameplay component lifetime, but
fixed order and process age remain confounds. A negative finding cannot exclude a
source, particularly an intermittent source seen only three times in an older run.

Both conditions request 60 FPS, preserve vSync, render interval and time scale, and
record actual frame timestamps/durations, resolution and rendering/API settings.
Requested FPS is not achieved FPS, and the empty camera does not reproduce Shooter's
GPU, audio or simulation workload. Unity documents that [vSync and Editor behavior
affect frame pacing](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Application-targetFrameRate.html).
No temporal or cross-mode byte matching is inferred solely from equal byte sizes.

## Observation phase and controls

A uses `ProfilerRecorder.StartNew(Memory, "GC Allocated In Frame")` with the same
default capacity/options as FrameGovernor. Its Update runs at order 950 and caches
LastValue. B uses the governor's own recorder and cached result; a one-time cold
reflection lookup borrows the existing handle for readiness and equality checks.
It never starts, resets or disposes that borrowed handle. The owning component
disposes it on disable. Unity's [2022.3 recorder implementation](https://github.com/Unity-Technologies/UnityCsReference/blob/2022.3/Runtime/Profiler/ScriptBindings/ProfilerRecorder.bindings.cs)
stores a native handle, so copying the struct does not create a second recorder.

Each coroutine iteration stores its source frame/time, yields null, then stores the
read frame/time and cached previous-frame value. The 180th row freezes the endpoint.
All file formatting and I/O wait until after the controls. Arrays and read paths are
allocated/warmed before the 150 warmup yields. No per-frame assertion, formatting,
collection growth, scene change or log write is added to the observation window.

Six four-frame blocks append empty and retained-array controls in Update, LateUpdate
and the coroutine. Retained payloads are 4,096, 8,192 and 16,384 bytes. Requested frame,
actual emission frame/phase, emission count and object retention are checked. Each
positive must identify exactly one read at lag 1 in the bounded neighboring-read
search. A missing, stale, ambiguous or wrongly phased positive invalidates the
condition. The reported bytes are whole-frame counter bytes, not exact array sizes.
Empty frames may contain the background allocation under investigation; their
nonzero bytes are retained without a zero requirement or subtraction.

Every row checks Valid, IsRunning and initialization through Count/ WrappedAround,
nonnegative bytes, the original capacity, frame sequence, and cumulative totals.
[Count can be a wrapping index](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Unity.Profiling.ProfilerRecorder.Count.html),
so capacity-one Count=0 is not treated as an unready recorder when WrappedAround is
true. Positive phase controls establish bounded responsiveness; capacity one cannot
independently prove freshness of each intervening zero sample. B's borrowed current
LastValue must equal its cached governor value, and the governor's frame/allocation
counts must match the rows. B has no public Update-frame stamp; its CSV uses -1 in
the raw-reader-only stamp column rather than inventing one.

## Execution and evidence

The isolated diagnostic branch workflow now selects only:

```sh
Tools/ci/local-unity-tests.sh --shooter-platform-control
```

The filter is the single explicit test
`ShooterFoundation.Tests.PlayMode.ShooterPlatformControlTests.MatchedEmptyPlatformWindows`.
The launcher sets `SPF_SHOOTER_PLATFORM_CONTROL=1`, forces stack/Editor capture
environment switches off, and retains native Apple Silicon selection. Normal local
invocations force both diagnostic modes off. The ordinary native workflows continue
to exclude the isolated diagnostic branch; the historical manual allocation-stack
entry remains separate and is not used by the current workflow.

The external launcher samples the exact Unity PID's RSS every second. It terminates
that process at a sampled RSS above 4,096 MiB, unavailable RSS or a 600-second process
timeout, first requesting termination and then killing after two seconds if needed.
It never retries. The RSS boundary is a monitored stop threshold, not an OS-enforced
hard cap or whole-process-tree measurement; overshoot may occur. The 4 GiB threshold
is over twice the original ordinary run's sampled PlayMode peak of 1,913 MB. The
monitor, limits and observations are recorded. No profiler preferences need restoring
after a kill because this mode never writes them; ordinary process-local frame-rate
requests are restored on cooperative completion only.

`Artifacts/GC/platform-control/{A-raw-recorder,B-frame-governor}/` contains settings,
initial components, `window.csv`, `window-endpoint.json`, all observations, controls
and `summary.json`. Preflight state is retained at the parent directory, including
when it refuses a profiler configuration. Run provenance, XML/logs and resident
samples remain under Artifacts. Partial data are exported in cleanup where possible;
the launcher rejects missing conditions, incomplete windows, skipped tests or failed
calibration even if Unity returns zero. The existing bounded packaging retains every
output and verifies all parts/hashes.

Interpretation is also written in each machine-readable summary:

1. A reproduction shows the pattern can occur without SPF/Shooter gameplay
   components; it does not identify Unity, UTF, Editor packages or the observer as
   the allocating callsite.
2. B-only reproduction narrows the candidate environment to the governor or induced
   state; it does not prove a specific governor allocation.
3. Failure to reproduce excludes no source. Differences in actual pacing, thermal
   state, process age or invalid controls must stay visible.
4. All bytes remain raw. The original Shooter gate is independently reported and
   unchanged; this experiment cannot turn an earlier failure into a pass.

## Local verification boundary

Native-domain compilation uses Editor/test defines and Unity's pinned NUnit 3.5
reference. Engine APIs remain signature stubs; the known transitive harness NUnit
version warning is retained. Forty pure checks exercise counter readiness,
stale/wrong phase, totals, pacing, missing/duplicate controls, ambiguity, retention,
all three positive phases and the component allowlist. A synthetic three-41-byte result remains a valid
platform observation without being relabelled as product acceptance. Fifteen
launcher checks cover routing, single invocation, missing/bad outputs, default-off
behavior and RSS-stop failures. None of these checks runs Unity or proves native
counter correctness. One native experiment remains pending review/publication.


## Inventory correction after the first native attempt

Run [37832732235](https://github.com/karosLi/SPGameFoundation/actions/runs/37832732235)
on `925ea6d5bcf04d95eb86c1237ee0155826f09027` stopped in A's initial component
inventory, before creating the recorder or collecting any measurement. Its inventory
contained the engine DebugUpdater above and a Transform. All six preflight profiler
flags were false. This failure yields no A/B allocation result.

The correction recognizes only that exact type and assembly as retained engine
background. Unity's [Core RP DebugUpdater source](https://github.com/Unity-Technologies/Graphics/blob/2022.3/staging/Packages/com.unity.render-pipelines.core/Runtime/Debugging/DebugUpdater.cs)
initializes it after scene load in the Editor. This test does not change that engine
lifecycle. The raw observations, positive controls, profiler-off preflight, process
limits and Shooter's original 150/180/maximum-two product gate remain unchanged.
The corrected native run is pending; earlier local pure-check counts are historical
and do not constitute native validation of this inventory correction.
