# Source and backport ledger

## Control group

- Upstream: [Dreaming381/Latios-Framework 0.11.5](https://github.com/Dreaming381/Latios-Framework/tree/381a77dbf774ff603014d5695ef6c06abaa25d96), exact `381a77dbf774ff603014d5695ef6c06abaa25d96`.
- Source host metadata: [package.json](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/package.json) specifies Entities 1.3.5, Graphics 1.4.2, Burst 1.8.18 and DSPGraph 0.1.0-preview.22; minimum Editor 2022.3.36f1. Exact target 2022.3.62f2 still needs execution.
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

The bootstrap is deliberately smaller than the upstream full template: it suppresses default runtime creation, while fixture-owned worlds install only Core constructor essentials and optional QVVS. `CoreBootstrap.InstallSceneManager` is optional and is not needed for these owned-world fixtures. Psyshock arrays use library algorithms directly, not a nonexistent general physics runtime installer.

## Licence and distribution scope

The exact old [LICENSE.md](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/LICENSE.md) states **Unity Companion License for Unity-dependent projects**. Its unmodified text is preserved in [UpstreamNotices/Latios-0.11.5-LICENSE.md](UpstreamNotices/Latios-0.11.5-LICENSE.md).

The exact old [THIRD PARTY NOTICES.md](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/THIRD%20PARTY%20NOTICES.md) is retained as [the matching notice snapshot](UpstreamNotices/Latios-0.11.5-THIRD-PARTY-NOTICES.md). It lists SquirrelNoise5 (CC-BY-3.0 US), ACL (MIT, including a binary wrapper-source reference), arraylayout (CC BY 4.0) and unity-guid (MIT). These notices apply to included upstream material according to their original scope; they do not make the full package MIT. Newer-version notices are not substituted.

UPM will fetch the whole original package and its notices later. A player/package distribution must inventory the actual copied/generated code, assets and binaries and preserve all required licences/notices. This preparation neither vendors the package nor authorizes a public upstream fork, relicensing or non-Unity use. Licence links are provenance, not a legal conclusion about a proposed commercial distribution.

## Review ledger for future changes

For each future compatibility fix or new capability, record upstream commit/file, concrete product need, failure/old oracle, new oracle, dependency closure, modified files, preserved notices, native/Burst/AOT result and platform limits. Keep compatibility fixes separate from feature backports. The first entry remains **pending real S1a import results**.
