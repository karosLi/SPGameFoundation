# Source and backport ledger

## Control group

- Upstream: [Dreaming381/Latios-Framework 0.11.5](https://github.com/Dreaming381/Latios-Framework/tree/381a77dbf774ff603014d5695ef6c06abaa25d96), exact `381a77dbf774ff603014d5695ef6c06abaa25d96`.
- Source host metadata: [package.json](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/package.json) specifies Entities 1.3.5, Graphics 1.4.2, Burst 1.8.18 and DSPGraph 0.1.0-preview.22; minimum Editor 2022.3.36f1. Exact target 2022.3.62f2 reached a first import failure; corrected-source compilation and execution remain pending.
- Documentation snapshot: [bc3be505](https://github.com/Dreaming381/Latios-Framework-Documentation/tree/bc3be50530180ad6ee8dd10fa7388a7854131f6b).
- Local preparation baseline: SPF `9fc4e2b7e20a17e5586a68f756e92f8102f99339`. No SPF root package/settings changes.
- Upstream code modifications: **none**. Vendored implementation/binaries: **none**. Independent lab tests/tools reference the package API; only the two exact notices are copied.
- S1b patch: **none selected or applied**. Do not use preparation or 0.11.5 success to claim newer API compatibility.

## API evidence used to write the fixtures

All source links below are the pinned old version, not 0.16 API guesses.

| Fixture/decision | Exact upstream evidence |
| --- | --- |
| Owned world and mandatory define | [LatiosWorld.cs](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/Core/Framework/LatiosWorld.cs): constructor, group properties, `ENTITY_STORE_V1` compile guard, scene-blackboard method |
| Generated `partial` collection and disposal chain | [IManagedStructComponent.cs](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/Core/Components/IManagedStructComponent.cs): `ICollectionComponent.TryDispose(JobHandle)`; [historical collection guide](https://github.com/Dreaming381/Latios-Framework-Documentation/blob/bc3be50530180ad6ee8dd10fa7388a7854131f6b/Core/Collection%20and%20Managed%20Struct%20Components.md) explains `partial` and source-generated companion types |
| Tracked writer/read system and explicit setup acknowledgement | [SubSystem.cs](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/Core/Framework/SubSystem.cs); [LatiosWorldUnmanaged.cs](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/Core/Framework/LatiosWorldUnmanaged.cs#L294-L532): add/get/remove APIs, read-only flag, manual main-thread access, tracked dependencies |
| Destroy/reactive cleanup and whole-world disposal | [LatiosWorldSystemGroups.cs](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/Core/Systems/_Essentials/LatiosWorldSystemGroups.cs) installs collection reactive cleanup; [LatiosWorldUnmanagedSystem.cs](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/Core/Systems/_Essentials/LatiosWorldUnmanagedSystem.cs) owns final storage disposal |
| QVVS only when requested | [cached QVVS installer](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/Transforms/CachedQvvs/TransformsBootstrap.cs): `InstallTransforms(world, group)` |
| Array builder and actual scheduler choices | [Physics.BuildCollisionLayer.cs](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/PsyshockPhysics/Physics/Spatial/Builders/Physics.BuildCollisionLayer.cs): NativeArray entry at 163, settings, immediate/single/parallel methods; no public batch-size argument |
| Processor signature, stable array identities, alias safety | [Physics.FindPairs.cs](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/PsyshockPhysics/Physics/Spatial/Queries/Physics.FindPairs.cs): `Execute(in FindPairsResult)`, sourceIndexA/B, safe parallel schedule. No `WithoutEntityAliasingChecks` or unsafe scheduling is used. |
| Sphere body and transforms | [ColliderBody.cs](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/PsyshockPhysics/Physics/Types/ColliderBody.cs), [SphereColliderPsyshock.cs](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/PsyshockPhysics/Physics/Types/Colliders/SphereColliderPsyshock.cs), [TransformQvvs.cs](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/Transforms/TransformQvvs.cs) |
| Independent inclusive-AABB oracle boundary | [FindPairsSweepMethods.cs](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/PsyshockPhysics/Physics/Internal/Queries/Layers/FindPairsSweepMethods.cs): inclusive X bound and strict-separation Y/Z rejection. Oracle is independently expressed on fixture centers/radii, not delegated back to the implementation. |
| Known finite ray / signed-distance geometry | [Physics.Raycast.cs](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/PsyshockPhysics/Physics/Spatial/Queries/Physics.Raycast.cs), [Physics.DistanceBetween.cs](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/PsyshockPhysics/Physics/Spatial/Queries/Physics.DistanceBetween.cs), [QueryResults.cs](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/PsyshockPhysics/Physics/Types/QueryResults.cs) |
| Exact 2022 API settings and IL2CPP setter | [Unity 2022.3.62f2 PlayerSettings](https://github.com/Unity-Technologies/UnityCsReference/blob/2022.3.62f2/Editor/Mono/PlayerSettings.bindings.cs): `NET_Standard` value 6, `SetIl2CppCodeGeneration(NamedBuildTarget, ...)` |

The bootstrap is deliberately smaller than the upstream full template: it suppresses broad default system injection. Its separate empty standard default World satisfies Unity's bootstrap contract and belongs to registered Unity shutdown; fixtures retain explicit ownership of their separate Latios worlds. Fixture-owned worlds use the Core constructor essentials, explicit real BeginInitialization ECB and empty SceneSystemGroup ordering dependencies, and optional QVVS. `CoreBootstrap.InstallSceneManager` remains optional and unused; no SceneSystem/streaming children are injected. Psyshock arrays use library algorithms directly, not a nonexistent general physics runtime installer.

## Licence and distribution scope

The exact old [LICENSE.md](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/LICENSE.md) states **Unity Companion License for Unity-dependent projects**. Its unmodified text is preserved in [UpstreamNotices/Latios-0.11.5-LICENSE.md](UpstreamNotices/Latios-0.11.5-LICENSE.md).

The exact old [THIRD PARTY NOTICES.md](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/THIRD%20PARTY%20NOTICES.md) is retained as [the matching notice snapshot](UpstreamNotices/Latios-0.11.5-THIRD-PARTY-NOTICES.md). It lists SquirrelNoise5 (CC-BY-3.0 US), ACL (MIT, including a binary wrapper-source reference), arraylayout (CC BY 4.0) and unity-guid (MIT). These notices apply to included upstream material according to their original scope; they do not make the full package MIT. Newer-version notices are not substituted.

UPM will fetch the whole original package and its notices later. A player/package distribution must inventory the actual copied/generated code, assets and binaries and preserve all required licences/notices. This preparation neither vendors the package nor authorizes a public upstream fork, relicensing or non-Unity use. Licence links are provenance, not a legal conclusion about a proposed commercial distribution.

## Review ledger for future changes

For each future compatibility fix or new capability, record upstream commit/file, concrete product need, failure/old oracle, new oracle, dependency closure, modified files, preserved notices, native/Burst/AOT result and platform limits. Keep compatibility fixes separate from feature backports.

### S1a host import correction, 2026-10-08

The [first run and source-based diagnosis](ImportCorrection-20261008.md) isolate owned duplicate test-runner references as the compilation setup blocker. Both Lab test asmdefs now use explicit runners and NUnit, without legacy `optionalUnityReferences`. Host Test Framework 1.4.5 and URP 14.0.12 declarations reflect verified dependency/registered-package evidence. Launcher checks retain the unchanged original Latios/Entities/Graphics/Burst graph, reject unreviewed normalization, and compare registered versions with the new genuine lock. The first failed-run lock is preserved verbatim outside active `Packages`.

No upstream implementation, package patch, experimental Properties dependency, feature backport, test semantics or threshold was changed. The Properties orphan text is informational output in the exact Editor source, not proof of an additional fatal error. Original notices are unchanged. Static validation passes; native import, 49+1 test cases in both Burst modes, repeat clean control, AOT/player and physical-device results for this corrected candidate remain **NOT RUN**. This is a host correction, not S1a completion or S1b.

### S1a owned NativeArray compilation correction, 2026-10-08

The [second native import](NativeArrayOwnershipCorrection-20261008.md) passed the duplicate-reference stage and failed on CS1654 at `PsyshockProbe.cs:46`: the fixture wrote through a using-declared NativeArray struct. It produced a matching host lock and a fully restored 29-file evidence archive, with no test execution. `RunPairs` now gives the mutable bodies array an explicit outer try/finally, preserving cleanup ordering and every query/identity/oracle detail. All 11 Lab C# files were reviewed for related receiver constraints. No upstream patch or test waiver is involved; the suspected reactive-cleanup defect remains unexecuted and unmodified. Static checks and an explicitly non-Unity C# language probe pass; native compilation and all downstream acceptance remain pending.

### S1a owned EditorTools import correction, 2026-10-08

The [third import](EditorToolsImportCorrection-20261008.md) compiled Runtime and PlayMode, but EditorTools failed with missing Collections reference DC0061 and ambiguous PackageInfo CS0104. The two local changes add the required direct asmdef reference and fully qualify the intended PackageManager API. The preflight now rejects the missing Entities/Collections relationship; 26 Lab and 19 CI checks pass. Original package versions and all fixture semantics remain unchanged. The complete 29-file native archive is verified; there is still no native test/safety/Burst witness, environment inventory or reproduced upstream cleanup failure. These corrections remain unexecuted in Unity.

### Whole owned-domain review and bounded text evidence, 2026-10-08

[CompilationDomainReview](CompilationDomainReview-20261008.md) freezes all 11 C# and four asmdef hashes at the corrected source, separates actual Runtime/PlayMode compilation from failed EditorTools and uncompiled Editor tests, and records the exact pinned public API/type/generator review. No new C# or package change was needed. The audit parser and bounded per-phase collection retain only owned compiler text and report missing/unemitted files without inventing a pass. Original native errors remain primary if collection also fails; the final hash manifest matches the final summary. Static checks are 29 Lab + 23 CI. Exact local Unity-reference compilation was unavailable; full native import/tests remain pending.

### Fourth-run inventory and initialization composition correction, 2026-10-08

[WorldInventoryCorrection](WorldInventoryCorrection-20261008.md) records all four owned assemblies compiling on original `8bca004`, the 42-file verified archive and all 12 real compiler inputs. Import then failed because environment capture used LINQ on World.Systems; pinned Entities deliberately throws from its interface enumerator. The owned correction uses Count/indexer and the same sorted name inventory. Three original ordering warnings exposed omitted real Unity targets in the smaller Lab construction; the factory now adds the standard initialization ECB and empty SceneSystemGroup before sorting, with a direct Runtime Unity.Scenes reference. This is an owned construction correction with the documented ECB allocator/singleton cost, not a dummy group, optional scene-manager install or warning waiver. The strict logging and cleanup assertions remain unchanged. Current source guards are 31 Lab + 23 CI; changed-source native import/tests remain pending.

### Original cleanup failure and owned bootstrap contract, 2026-10-08

[BootstrapContractCorrection](BootstrapContractCorrection-20261008.md) records run 37761086135 attempt 2: full import success, 45/49 EditMode/on passing, and four retained-owner failures. The earlier inventory/ordering fixes are now native-proven in this scope. Two separate original log assertions show our bootstrap returned true without a default World. The pending owned fix assigns a real empty standard Game World and returns true; fixed Entities shutdown owns its exit/reload disposal. It creates no systems, performs no broad injection and does not append that World to PlayerLoop. No defines, packages or assertions change. Static checks are 32 Lab + 23 CI; this new Runtime change is not native-validated.

## Original control-group cleanup finding (native reproduced)

At pinned [CollectionComponentOperations.cs, SyncQueries](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/Core/Internal/CollectionComponentOperations.cs#L114-L176), the removal path processes removeQuery and completes disposal handles but then passes **addQuery** when removing the cleanup component. [CollectionComponentsReactiveSystem](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/Core/Systems/_Essentials/ManagedStructStorageCleanupSystems.cs) invokes this operation without another removal. A destroyed owner matches removeQuery, not addQuery.

The original attempt now fails exactly at `EntityManager.Exists(owner)` after the preceding output=37265 and witness=[1,17] checks passed. This reproduces a retained cleanup entity after disposal, consistent with the source defect. Native artifacts do not contain a per-entity component dump; marker identity is inferred from the pinned code. The second-update and final normal-path assertions were not reached in the failing cases.

The original package and strict 49+1 tests remain unchanged. Do not manually remove the entity in the fixture or weaken the oracle. A separate compatibility candidate may evaluate the one-argument removeQuery correction only with its own provenance/ledger and full regression; no such patch is included here.
