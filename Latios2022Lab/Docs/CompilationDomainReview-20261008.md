# S1a owned compilation domain review

2026-10-08. The original review below was frozen against **11 C# files and four asmdefs at `34376351165a7e44e63305c6c47c520f7ddd21bf`**. Later [attempt 2 of bda5bc7](BootstrapContractCorrection-20261008.md) compiled all four, passed import and reached the original strict cleanup failure. The live [source/assembly hashes](Validation/20261008-compilation-domain/source-review.json) now use that native baseline and identify only the pending owned bootstrap correction; changed-source native validation is not inherited. The historical log evidence below remains tied to its original source.

## Four assemblies: actual evidence and direct closure

The complete, hash-verified log from [run 37744846280](https://github.com/karosLi/SPGameFoundation/actions/runs/37744846280/attempts/1), source `2edacdbe1315a6da682be39c1fa2cebae2e14bda`, has SHA-256 `edb24ceea2c749e0181680631d911c175995a524fffa11f5b225b77e7078c2a6`. Line references below refer to that original log, not an excerpt or a hypothetical compiler invocation.

| Assembly | Native log events | Relationship to reviewed source | Direct closure after correction |
| --- | --- | --- | --- |
| `Latios2022Lab.Runtime` | Csc 2467; ILPostProcess 2543; CopyFiles 2729 | All six C# files and asmdef hash-match native launch inputs | Latios Core/Transforms/Psyshock; Entities, Collections, Mathematics, Jobs, Burst |
| `Latios2022Lab.PlayMode` | Csc 2542; ILPostProcess 2591; CopyFiles 2755 | C# and asmdef hash-match native launch inputs | Runtime, Latios Core/Transforms/Psyshock; Entities, Collections, Mathematics, Jobs, Burst; both test runners and explicit NUnit |
| `Latios2022Lab.EditorTools` | Failed Csc 2923; expanded command/response text 2924–3309; DC0061/CS0104 at 3315–3316 | LabEnvironment.cs and asmdef changed in 3437635; LabPlayerBuild.cs unchanged | Runtime, Latios.Core, Burst, Entities and now explicit Collections; engine Editor references enabled |
| `Latios2022Lab.Editor` | Response-file write 2080 and metadata extraction 2261; **no Csc, ILPostProcess or CopyFiles completion** | Both C# files and asmdef hash-match native launch inputs, but this does not prove their compilation | Runtime + EditorTools; Latios Core/Transforms/Psyshock; Entities, Collections, Mathematics, Jobs, Burst; both test runners and explicit NUnit |

All four directly reference **Entities, Collections and Burst**. Entities 1.3.5's official `SystemGenerator.cs` checks direct Collections metadata and emits DC0061 if missing; a transitive dependency does not satisfy this check. Burst is directly present for relevant generated/job code. Runtime's `partial LabCollection` additionally requires the pinned Latios source generator to implement its public generated-interface contract. Both test asmdefs retain `UNITY_INCLUDE_TESTS`, explicit NUnit, explicit runner references, and no legacy optional runner duplication.

Runtime/PlayMode's recorded compilation is useful evidence for their identical reviewed inputs, but there was no complete domain load or test execution. EditorTools' corrected version and the downstream Editor assembly still need an actual Unity compile.

## What the actual response text proves

The log explicitly prints `Latios2022Lab.EditorTools.rsp`: 232 reference entries, 117 defines, 15 analyzers, C# 9.0 and the two owned EditorTools source files. It names both `.rsp` and `.rsp2` in the compiler command, but only prints a contents block for `.rsp`.

The printed references include `Unity.Entities.ref.dll`, `Latios.Core.ref.dll`, the owned Runtime reference and Burst; **`Unity.Collections.ref.dll` is absent**. A low-level Collections support DLL is present, but it is not the missing assembly. The generator set includes LatiosFramework.SourceGen and the Entities System/JobEntity/Aspect generators. Actual printed defines include `UNITY_EDITOR`, `UNITY_INCLUDE_TESTS`, `ENABLE_UNITY_COLLECTIONS_CHECKS`, `ENTITY_STORE_V1` and `UNITY_BURST_EXPERIMENTAL_ATOMIC_INTRINSICS`.

These are actual compiler inputs reported by Unity. They are not a successful runtime safety/Burst witness. The extracted response text is labelled as text printed in the verified log; its original standalone file bytes were not recovered. Editor response contents must not be inferred from EditorTools contents merely because the assemblies share references.

The restored 29-file archive contains **zero original `.rsp`/`.rsp2` files, zero Unity/Lab DLLs and zero generated `.cs` files**. The local workspace has no usable UnityEngine.CoreModule, UnityEditor.CoreModule, Entities or Lab DLL set. Therefore an exact-reference local C# compilation was unavailable. No shim, unrelated NuGet assembly or package upgrade was used to manufacture one.

## Every owned C# file and its API/type review

Fixed sources: UnityCsReference **2022.3.62f2**; Latios **381a77dbf774ff603014d5695ef6c06abaa25d96**; official registry Entities **1.3.5**, Collections **2.5.1**, Mathematics **1.3.2**, Burst **1.8.18**, Test Framework **1.4.5**, and Unity Custom NUnit **2.0.3**. The runtime source signatures below were checked alongside the real compilation evidence. Editor tests received an independent source/metadata review because they have not compiled.

| Owned file | Checked external contract / type issue | Conclusion and remaining boundary |
| --- | --- | --- |
| Runtime/LabWorld.cs | [LatiosWorld constructor/group/scene-blackboard APIs](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/Core/Framework/LatiosWorld.cs); [QVVS installer](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/Transforms/CachedQvvs/TransformsBootstrap.cs) accepts World, ComponentSystemGroup and optional bool; Entities ICustomBootstrap returns bool | Signatures match. Worlds are reference types; owned creation/disposal unchanged. Recorded Runtime compilation, no world execution gate |
| Runtime/LabCollection.cs | [ICollectionComponent.TryDispose(JobHandle)](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/Core/Components/IManagedStructComponent.cs); generated interface; [Unity IJob scheduling](https://github.com/Unity-Technologies/UnityCsReference/blob/2022.3.62f2/Runtime/Jobs/Managed/IJob.cs); [NativeArray disposal](https://github.com/Unity-Technologies/UnityCsReference/blob/2022.3.62f2/Runtime/Export/NativeArray/NativeArray.cs) | Public partial unmanaged struct and mutable job fields satisfy reviewed constraints. Generator-emitted source was not retained; native Runtime compile is the available generation evidence |
| Runtime/CollectionProbe.cs | [LatiosWorldUnmanaged Add/Get/Has/Remove/MainThreadAccess](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/Core/Framework/LatiosWorldUnmanaged.cs); Entities World/GetOrCreateSystemManaged, EntityManager Create/Destroy/Exists, group Add/Sort/Update; [parallel scheduling](https://github.com/Unity-Technologies/UnityCsReference/blob/2022.3.62f2/Runtime/Jobs/Managed/IJobParallelFor.cs) | Generic constraints and receiver types match. Property-returned native managers use shared implementation data. Strict disposal/cleanup oracle unchanged and not executed |
| Runtime/PsyshockProbe.cs | [NativeArray layer builder and allocator overloads](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/PsyshockPhysics/Physics/Spatial/Builders/Physics.BuildCollisionLayer.cs); [FindPairs processor/schedulers](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/PsyshockPhysics/Physics/Spatial/Queries/Physics.FindPairs.cs); [ray](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/PsyshockPhysics/Physics/Spatial/Queries/Physics.Raycast.cs) / [point distance](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/PsyshockPhysics/Physics/Spatial/Queries/Physics.DistanceBetween.cs); Collections NativeList/ParallelWriter | Physics alias is unambiguous; allocator implicit conversion exists. Mutable bodies try/finally correction compiled in recorded Runtime. Pair/query oracles and Burst execution remain unrun |
| Runtime/LabRunPolicy.cs | Official Burst 1.8.18 `BurstCompiler.IsEnabled`, `Options`, `BurstCompilerOptions.EnableBurstSafetyChecks`; Unity version and compile-time symbols | Public getters exist. Only getters are used; no preference mutation. Recorded Runtime compilation does not prove the checks executed |
| Runtime/LabPlayerSmoke.cs | Unity MonoBehaviour/Application/JsonUtility/log APIs; the public owned probes | No name/visibility mismatch; source is in the compiled Runtime assembly. No player build, AOT or player execution occurred |
| EditorTools/LabEnvironment.cs | [PackageManager inventory](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/PackageManager.PackageInfo.GetAllRegisteredPackages.html), package name/version/source/packageId; [PlayerSettings API compatibility](https://github.com/Unity-Technologies/UnityCsReference/blob/2022.3.62f2/Editor/Mono/PlayerSettings.bindings.cs); [Editor reload settings](https://github.com/Unity-Technologies/UnityCsReference/blob/2022.3.62f2/Editor/Mono/EditorSettings.bindings.cs); compilation inventory | The ambiguous PackageInfo is fully qualified in 3437635. Inventory structure unchanged; corrected assembly still uncompiled |
| EditorTools/LabPlayerBuild.cs | [BuildPipeline target/group/build APIs](https://github.com/Unity-Technologies/UnityCsReference/blob/2022.3.62f2/Editor/Mono/BuildPipeline.bindings.cs); [PlayerSettings backend/IL2CPP setters](https://github.com/Unity-Technologies/UnityCsReference/blob/2022.3.62f2/Editor/Mono/PlayerSettings.bindings.cs); [scene creation](https://github.com/Unity-Technologies/UnityCsReference/blob/2022.3.62f2/Editor/Mono/EditorSceneManager.bindings.cs) and public SaveScene overload | Existing NamedBuildTarget and Il2CppCodeGeneration qualification is retained. No further concrete signature mismatch; file participated in the failed EditorTools invocation, not a passing build |
| Editor/CoreTests.cs | World SequenceNumber/Update/Dispose, managed-system generic constraint, native-array read semantics, collection removal constraint, exact NUnit assertions/attributes, LogAssert Regex overload, reload settings, Enter/ExitPlayMode | Independent review found matching signatures and direct references. No Editor Csc completion; see detailed checks below |
| Editor/PsyshockTests.cs | Exact NUnit Test/Combinatorial/Values attributes; public owned fixture enums and probe signatures | Static combinations remain 36 pairs + 1 query. No new mismatch, but actual discovery and compilation remain unverified |
| PlayMode/PlayModeTests.cs | TF145 UnityTest and LogAssert.NoUnexpectedReceived; IEnumerator; owned mode/probe APIs | Correct public signatures and runner references. Recorded assembly compiled/copied; the one coroutine test never ran |

The six remaining using-declared native containers are read or passed by value; their writes occur through mutable job fields. No direct assignment or ref/out use through those read-only locals was found. `NativeList.AsParallelWriter()` copies the shared list pointer and safety handle, so the reviewed use has no harmful value-copy update. `NativeArray<T>` itself is an engine type in **UnityEngine.CoreModule**, not the Collections package; the separate direct Collections requirement comes from Entities' generator/API closure.

## Additional checks for the uncompiled Editor tests

| API/use | Exact reviewed declaration | Assembly/type conclusion |
| --- | --- | --- |
| Test, Combinatorial, Values | Unity Custom NUnit 2.0.3 DLL metadata contains required public constructors, including the object-array Values constructor | Explicit `nunit.framework.dll` is present; no unrelated NUnit 3.13.3 substitute |
| AreEqual/AreNotEqual/IsTrue/IsFalse | Exact custom NUnit metadata includes object/object comparisons and bool/string/params assertions used here | Boxed int/ulong comparisons are supported |
| World identity and writer construction | Entities 1.3.5 World.cs exposes ulong SequenceNumber and GetOrCreateSystemManaged constrained to ComponentSystemBase | LabWriterSystem derives through Latios SubSystem/SystemBase, satisfying the constraint |
| Collection removal | Pinned RemoveCollectionComponentAndDispose requires unmanaged, ICollectionComponent and the public generated interface | Public partial LabCollection plus its generator are required; no assertion or generated-interface constraint was removed |
| Expected logged exception | TF145 LogAssert.cs has the LogType/Regex overload | Engine LogType and BCL Regex are unambiguous |
| Play-mode entry/exit | TF145 EnterPlayMode accepts optional bool; ExitPlayMode has a public zero-argument constructor | Both classes use namespace UnityEngine.TestTools but are declared in **UnityEditor.TestRunner**; that reference is explicitly present |
| Reentry settings | Exact EditorSettings properties and DisableDomainReload flag match the expression | Domain reload requirement unchanged |

The exact NUnit archive was verified against registry SHA-1 `e9d0d64aaa514a9ed29680de4c7e3c323717bbac`; the inspected framework DLL has SHA-256 `d3fe5ce66fbad51644d8fa2add8015cc0c1af95774fb3778f725e05785257d3f`. Metadata inspection does not execute the DLL or establish an Editor-domain compile. The declared native total remains **49 EditMode + 1 PlayMode**, each with Burst on/off.

## Reproduce the frozen static/log audit

From the repository root after restoring the genuine run evidence:

```sh
PYTHONDONTWRITEBYTECODE=1 python3 Latios2022Lab/Tools/audit_domain.py \
  --unity-log /restored/Artifacts/native/20261008T074135Z-import-on-da7fc92f/unity.log \
  --artifact-root /restored/Artifacts \
  --output /local/output/compilation-domain-audit.json
PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s Latios2022Lab/Tools -p 'test_*.py' -v
PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s Tools/ci -p test_latios_lab_ci.py -v
```

The audit checks every frozen C# and asmdef hash, direct Entities/Collections closure, test-assembly contracts, real log stage lines and source/asmdef equality against launch.json. It distinguishes response generation from compilation, labels logged response text separately from original files, lists retained compiler files and verifies the log against the restored hash manifest. It does not invent missing arguments, invoke Csc or run Unity. **29 Lab and 23 CI tests pass**, including negative review-freshness and stage-classification checks.

## Next-run compiler text retention

The collector now captures only the four exact Lab assembly names' `.rsp`, `.rsp2` and `.UnityAdditionalFile.txt` files under `Library/Bee/artifacts/<dag>/`, plus existing `.cs` files under `Temp/GeneratedCode/<exact-assembly>/`. It does not follow paths listed inside a response file or copy DLLs, PackageCache, other assemblies, the entire Library or environment/credential state.

Each native phase receives an immutable snapshot and index. Text is bounded at **128 files, 2 MiB per file and 8 MiB total per snapshot**; excess discovery is explicitly reported. Symlinks and non-UTF8 text are refused. Missing response patterns and zero emitted-source counts remain explicit. Full copied bytes and hashes enter both early evidence and the original bounded archive; the existing 16 MiB early cap remains enforced.

Entities 1.3.5's `SourceOutputHelpers.cs` writes generated files only when `DOTS_OUTPUT_SOURCEGEN_FILES` is enabled. It was absent from the actual printed defines and is **not enabled by this change**. Therefore generated `.cs` may remain unavailable even when source generation succeeds. Absence cannot be reported as either failure to generate or proof of no generated code.

If capture also fails after Unity fails, the original native failure and exit code remain primary; capture trouble is recorded separately. Original logs are retained first, and hashes are refreshed after the final summary mutation. Combined-failure regression verifies both precedence and summary/hash consistency. No new gate, native run, remote write, package/settings/define change or fixture-semantic change is part of this review. A genuine complete four-assembly import is still required next.
