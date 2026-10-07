# Stage B: optional composition preflight

This bounded follow-on to [composition rollback](CompositionRollbackValidation.md) implements the optional declaration slice of [Stage B](SharedFoundationSemanticExtensionPlan.md#p1-阶段-b-组合预检和安装事务). It starts from `02e9b916ccbbe15bd82b1e59c92c9f46f70fee87`; the [Stage A inventory](FoundationCompatibilityMatrix.md) remains pinned to its original source and is not regenerated.

## Contract and actual consumers

- [ModuleManifest](../Assets/SinglePlayerFoundation/Runtime/Composition/ModuleManifest.cs) adds one optional interface, `ICompositionManifestProvider`. `IGameplayModule`, `GameplayModuleAsset`, `ModeDefinition` and `SimSession` APIs are unchanged. Returning null opts out. The metadata callback must not acquire native resources or mutate configuration. Arrays are copied into read-only collections; declarations, report records and versioned capabilities are immutable.
- [CompositionPreflight](../Assets/SinglePlayerFoundation/Runtime/Composition/CompositionPreflight.cs) checks settings, module slots/IDs, stable manifest IDs and positive metadata schema versions. Exact-version Provides/Requires detect missing/duplicate providers, invalid/repeated requirements and cycles between modules. A self-provided requirement needs no inter-module installation edge. Requirements describe declared presence, not OnCreate ordering: later-listed providers are allowed. No module/system sorting, resolver or runtime lookup is introduced.
- Typed table/resource declarations detect duplicate names with distinct runtime key objects, duplicate declarations, incompatible declared schema, level scope or pooled storage, absent/duplicate table owners, duplicate resource registrations, invalid capacities/sources and unsupported reset/snapshot hook claims. Table extensions retain maximum requested capacity. An optional positive maximum budget rejects an excessive declared merge; zero preserves the unbounded legacy merge policy. The report lists the effective declared maximum and owner; each original declaration retains its request and source.
- `WorldComposer.BuildWorld` invokes preflight before any `DeclareData` when an optional provider is present. Compositions containing no optional-provider interface retain their previous validation and rollback timing. Classic Survivor/Brawler now implement that interface and return null, so they still pay the empty metadata-report cost and receive early slot checks; their valid declared layouts remain unchanged. Settings are still checked first. `BuildPipeline` alone remains the existing borrowed-world operation and does not perform a separate composition preflight.
- [Survivor weapon mode](../Assets/SurvivorFoundation/Runtime/SvModule.cs) describes its three actual tables and weapon/pose/skill resource slice. [Brawler weapon belt](../Assets/BrawlerFoundation/Runtime/BwModule.cs) describes its fighter table and corresponding three resources. Both use stable manifest IDs/schema 1. Their table budgets match the configured bounds used by their grids/queues/scratch storage; primary resource bounds are 32 projectiles, one pose clock and four skill slots. Other configurations return null. Description of an unconfigured Survivor does not invoke its lazy configuration factory.
- Existing `DeclareData`/`RegisterSystems` bodies in both consumers are byte-for-byte unchanged. Thus this is a pure managed metadata pass before the old allocationful declarations, not a conversion of those declarations to a separate allocator. Default compositions, explicit module order, Phase/Order/registration order, actual capacities, resource order and snapshot writers remain unchanged.

## Ownership and intentional limits

A report describes the intended module registrar. It does not own a resource object and must not be used as a cleanup list. Ownership transfers only when the existing `WorldLayout.Resource` accepts an object, then once to a successfully built `SimWorld`. Rejected objects and unregistered partial allocations remain the caller's responsibility. Existing rollback and aliased-object disposal are unchanged.

This is a deliberately partial contract, not configuration completeness or a save envelope:

- Undeclared legacy data, implicit destroy queues, columns, systems and unrelated resources are not inferred. A declared table extension needs a declared owner; the validator does not guess one from legacy modules. Resource primary capacities are descriptive and do not account for all native buffers, cue/history limits or bytes.
- The checker does not execute or inspect `DeclareData`, freeze external mutable configuration, or prove a declaration matches later code. The two consumer tests compare the described slice with actual world registrations. A nonparticipating extension or an inaccurate manifest can still change undeclared layout; existing runtime checks/rollback remain necessary.
- Metadata schema and capability versions are explicit caller-authored contracts, distinct from raw snapshot versions. There is no column/struct layout fingerprint, content hash, migration, portable persistent format or hostile-input envelope. Manifest schema 1 does not label WeaponRuntime's existing raw snapshot v2 as v1.
- `SnapshotHook` verifies only that the key's declared resource type implements `ISnapshotResource`. It cannot prove full state coverage, instance-specific saved flags, correct write/read implementations or replay continuation. `Unspecified` makes no save claim. Reset scope similarly requires `IResettableResource` on the declared type. These closed generic type/interface checks are cold metadata checks, not reflective service discovery.
- Arbitrary module/resource constructors still must clean their own partial failures. The pre-existing `ModeDefinition.Create` temporary-asset leak if its module enumeration throws remains outside this slice; that factory is untouched. Previously documented job-scheduling recovery and native/device gates remain open.

## Red-first and regression evidence

The new core API was first introduced as an inert report scaffold with the existing WorldComposer unchanged: **18 failed / 1 passed**. These were executed tests, not missing-type compilation failures. The immutable-array test already passed; validation/report/pre-allocation rejection cases failed. After implementing validation, all 19 passed; four additional adversarial cases bring the core suite to 23. The original 36 rollback cases also pass unchanged.

Consumer tests ran before either module implemented the optional interface: **6 Survivor failures + 7 Brawler failures**, all at the expected missing-interface assertions. The same 13 cases pass after wiring. They verify legacy opt-out, the real declared registration slice/capacities, unmodified lazy configuration and early resource/capacity-conflict rejection before either module can declare data. The isolated core example verifies declaration before acquisition and exactly-once release through actual accepted-resource ownership.

Source checkpoints: `c9c657bf294390d1a593e5b6acf5a121b9a18e69` core contracts and `91916b012e1194889712525e7e14abda20c1f3f2` two consumers. Final reviewed production/test source is the latter commit (tree `f8dd3a722fced2f908fe4c46630391bf227a715c`). On that exact source:

- All 76 generated harness projects build, **zero warnings/errors**. The aggregate run executes 11 EditMode assemblies: **1,154 passed / zero failed**, including all 36 new cases. Existing allocation thresholds and snapshot fixtures were not changed. The two pre-existing explicit diagnostics `SearchShots`/`ExportFrames` remain unrun. Fourteen PlayMode assemblies provide no runnable tests to the .NET adapter (its verbose log reports failed-load/no-tests); none are counted as native/graphics passes.
- The real Unity 2022.3.62f2 API-only compiler passes **337 sources / 95 references**, zero errors and seven existing unused/unassigned field warnings. This includes the three new test files plus the retained rollback tests. The normal harness separately enforces the unchanged asmdef graph. No asmdef, legacy composition API signature or existing declaration/system-registration method body changed.
- The unchanged cold probe runs all **19** compositions twice. Both outputs exactly match Stage A SHA-256 `a42c9f27d2bb8d277303eef24e5b4cdbd58f0035d8bca89c7e08089348dd5f30`; the full table/resource/system/settings inventory and all **65** named fixture methods match. Twenty pinned source files differ from the historical Stage A inventory because of the intervening rollback/lifecycle work plus this slice; those changes are recorded separately rather than replacing the baseline. This slice itself changes three existing production files and adds three metadata files/three test files.
- Independent read-only review of `02e9b91..91916b0` found **no blocking findings**. Its nonblocking observation that null-manifest consumers still pay metadata cost is disclosed and measured below. The reviewer did not independently execute native or mobile tests.
- Actual Unity EditMode/graphics PlayMode and physical Android/iOS execution are **not run for this source here**. They remain separate integration/device gates. No earlier native result is relabeled as a result for this commit.

Aggregate build-log SHA-256: `f5638a9c119eed7751f6eccfb481011700bf465bbbeef5bea125d2e73cc4b3b0`; aggregate test-log SHA-256: `8724f92f8592c2ead058547e1e464a8908e62782c56cde368c47d650691feaf6`. Build/test durations were 136.75 / 409.04 seconds in this executor. Initial tool launch issues (missing dotnet PATH, uncached restore wait and one worker IPC launch restriction) were resolved before successful runs; they are not test passes.

## Cold-path and mobile cost boundary

This feature runs only during explicit preflight/World construction. It adds no code to Tick, jobs, input, presentation or snapshot paths, and does not allocate native/GPU storage itself. Its managed working set is linear in declared data/capabilities plus an M × M boolean dependency matrix for M manifested modules. The two real consumers each have M=1 and 6/4 data declarations. Resources are still allocated once by their existing declaration path.

A Linux x64/.NET 8.0.425 diagnostic warmed 128 metadata calls, retained 1,024 measured reports with the repository's `ManagedAllocationProbe`, and calibrated immediately before/after. All positive controls measured 33,536 B and empty controls 0 B. The measured region excludes module/config creation, actual world construction and report formatting. Allocations are expected cold-path overhead, not a zero-allocation claim:

| Metadata pass | Mean managed bytes/report | Gen-0 collections during 1,024 reports | p50 / p95 / worst microseconds/report |
| --- | ---: | ---: | ---: |
| Classic Brawler (null manifest) | 752 | 0 | 0.738 / 1.227 / 5.487 |
| Survivor weapon mode | 3,728.20 | 1 | 4.130 / 6.502 / 19.520 |
| Brawler weapon belt | 3,008 | 0 | 4.294 / 5.769 / 57.816 |

Timing used 128 batches × 32 calls, reported as per-call batch averages, with `Stopwatch.GetTimestamp`; it is a warmed execution diagnostic of cold-only code, not first-load/JIT/startup latency. Concurrent aggregate validation was running. The noninteger allocation average includes runtime measurement effects rather than implying fractional objects. Collections are independent process counters, not an allocation metric. No threshold was adjusted from these values.

Android/iOS cold start, Mono/IL2CPP metadata cost, native memory, GC pauses, sustained frame time, thermal/battery behavior and actual graphics/touch execution remain device/native gates. The desktop diagnostic cannot establish their budgets. At this slice's small M/D, the managed allocations are short-lived construction/report work; callers who retain reports retain their corresponding metadata.

## Reproduction

Generate and run the existing harness, preserving its asmdef boundaries:

```sh
python3 Tools/DotnetHarness/generate.py
dotnet build Tools/DotnetHarness/.gen/Harness.proj -m:1 -p:UseSharedCompilation=false -p:NuGetAudit=false
dotnet test Tools/DotnetHarness/.gen/Harness.proj --no-build -m:1
```

Focused cases are [CompositionContractTests](../Assets/SinglePlayerFoundation/Tests/EditMode/CompositionContractTests.cs), [SvCompositionManifestTests](../Assets/SurvivorFoundation/Tests/EditMode/SvCompositionManifestTests.cs) and [BwCompositionManifestTests](../Assets/BrawlerFoundation/Tests/EditMode/BwCompositionManifestTests.cs), in their respective EditMode projects. The unchanged `CompositionRollbackTests` exercises the retained cleanup path.

Use [the retained cold probe](validation/FoundationCompatibilityProbe-20261007.md) twice and compare its 19 compositions against the pinned inventory. Inspect source hash differences independently rather than overwriting the baseline. For API-only compilation, use [GenerateCompositionApiCompile.py](validation/GenerateCompositionApiCompile.py), then add the three new test files above to the generated temporary project; compile against installed Unity 2022.3.62f2 and NUnit. This does not execute Unity or replace native tests.

### Reproducing the cold-only allocation/timing diagnostic

Use a temporary .NET console project referencing generated `SPF.Runtime`, `SPF.Testing`, `SurvivorFoundation.Runtime` and `BrawlerFoundation.Runtime` projects; set `ConcurrentGarbageCollection=false`, matching the harness. Construct the modules once outside measurement: `SvConfig.CreateDefault()` with `WeaponCombat=true` passed to `SvModule.Create`, `BwModule.CreateWeaponBelt(BwBeltConfig.Default)`, and `BwModule.Create()` for the opted-out case. Destroy the borrowed module/config assets afterward. Run this body for each one-element `IGameplayModule[]`, with the usual namespaces imported:

```csharp
var settings = SessionSettings.Default;
for (int i = 0; i < 128; i++)
    CompositionPreflight.Validate(modules, settings).ThrowIfInvalid();
var reports = new CompositionReport[1024];
Action collect = () => {
    for (int i = 0; i < reports.Length; i++)
        reports[i] = CompositionPreflight.Validate(modules, settings);
};
using var probe = new SPF.Testing.ManagedAllocationProbe();
var before = probe.Calibrate();
var measured = probe.Measure(collect);
var after = probe.Calibrate();
var times = new double[128];
for (int sample = 0; sample < times.Length; sample++) {
    long start = System.Diagnostics.Stopwatch.GetTimestamp();
    for (int i = 0; i < 32; i++)
        reports[i] = CompositionPreflight.Validate(modules, settings);
    times[sample] = (System.Diagnostics.Stopwatch.GetTimestamp() - start)
        * 1000000d / System.Diagnostics.Stopwatch.Frequency / 32;
}
Array.Sort(times);
// Format after measurement: measured.Value / 1024d; measured.Collections;
// times[64], times[121], times[127]; before/after control values.
GC.KeepAlive(reports);
```
