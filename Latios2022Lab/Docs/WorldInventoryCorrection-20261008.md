# Fourth S1a import: compiled domain, inventory and world ordering

2026-10-08. **All four owned assemblies compiled in the original fourth run. Import then failed in the owned environment capture; no test ran.** The corrections below are source-reviewed and statically checked, but have not run in Unity.

## Actual progress and failure

[Run 37750770157, attempt 1](https://github.com/karosLi/SPGameFoundation/actions/runs/37750770157/attempts/1), source `8bca004c57ba1fe89cc7858372e233c3738982e6`, tree `6812a8aadb35594cc315123ae9ddef5cdaa8e6b1`, ended failure. Local gate equivalent `2d71a819ed77445096910400ceee6ec21e88e68b` has the same tree. The original [provenance record](Validation/20261008-fourth-import/provenance.json) retains the outcome and exact evidence.

| Assembly | Csc line | ILPostProcess line | CopyFiles line |
| --- | --- | --- | --- |
| Runtime | 2573 | 2631 | 2768 |
| PlayMode | 2598 | 2655 | 2797 |
| EditorTools | 2599 | 2943 | 2974 |
| Editor | 2612 | 2679 | 2836 |

Those lines are from the complete 2,848,390-byte native log, SHA-256 `eb006223e45d06088d0385c7ebb0245d69a48fee0039b4ac31863e18d771e161`. It has no coded compiler errors. The collector retained all **12 actual `.rsp`/`.rsp2`/additional-file inputs**, 138,273 bytes, with no missing pattern or collection issue. All four response files directly include Entities/Collections references and the required Editor, native-collection-safety, entity-store and Burst-atomic defines. Generated `.cs` counts remain zero because emission was not enabled; that is not a source-generation failure or evidence of no generated code.

Both official wrappers verify. One part restores **42 files**, including **41 original-file hashes**; all **42 launch-input hashes** match the prepared source and initial status was clean. The real manifest/lock are again byte-identical to the preserved second-run snapshots. No package version changed. The paired [.NET run 37750769992](https://github.com/karosLi/SPGameFoundation/actions/runs/37750769992) succeeded separately and is not Lab evidence.

The first terminal exception is at log line 18817: `NotSupportedException: To avoid boxing, do not cast NoAllocReadOnlyCollection to IEnumerable<T>.` Its stack enters Entities 1.3.5 `World.cs:1425`, LINQ's Select/OrderBy/ToArray and `LabEnvironment.CaptureEnvironment`. The exception occurs while assembling `installedManagedSystems`, before any environment JSON is written. Unity exits 1; the launcher exits 2. EditMode on/off and PlayMode on/off are all **NOT_RUN**, with no test XML. The original reactive-cleanup assertion is **NOT_REACHED**.

The source and later exception location show that the initial `Verify()` call returned and the empty owned world update was reached. This supports a narrow inference that the requested on-mode/configuration checks did not throw. There is no persisted environment inventory, no query `BurstDiscard` execution witness, and no off-mode evidence; none is claimed as passing.

## Inventory correction preserves the data contract

The official Entities 1.3.5 `Unity.Entities/World.cs` defines `NoAllocReadOnlyCollection<T>` with real `Count` and indexer getters and a concrete enumerator. Both explicit `IEnumerable` enumerators deliberately throw. A public interface declaration therefore does not make the old LINQ call valid.

Environment capture now copies each managed system's full type name using that collection's **Count/indexer**, into one array of the exact count, then calls `Array.Sort`. The same default string comparer, same `installedManagedSystems` field and same managed-system-only meaning are retained. The other two LINQ calls consume actual arrays returned by PackageInfo and CompilationPipeline; they do not touch this native collection. A review of all 11 owned C# files found no other `.Systems` use.

## Three original warnings and the missing construction dependencies

Before the exception, the original log contains these three warnings from `LabWorld.Create:27`, the explicit initialization-group `SortSystems()` call made by environment capture:

| Original line | Exact warning first line |
| --- | --- |
| 18763 | Ignoring invalid [Unity.Entities.UpdateBeforeAttribute] attribute on Latios.Systems.PreSyncPointGroup targeting Unity.Entities.BeginInitializationEntityCommandBufferSystem. |
| 18781 | Ignoring invalid [Unity.Entities.UpdateBeforeAttribute] attribute on Latios.Systems.SyncPointPlaybackSystemDispatch targeting Unity.Entities.BeginInitializationEntityCommandBufferSystem. |
| 18799 | Ignoring invalid [Unity.Entities.UpdateAfterAttribute] attribute on Latios.Systems.LatiosWorldSyncGroup targeting Unity.Scenes.SceneSystemGroup. |

Each warning continues with the same explanation that ordering requires both systems to be in the same ComponentSystemGroup instance. Their complete original message text and call sites are in [provenance.json](Validation/20261008-fourth-import/provenance.json); the original full stacks remain in the archived log. These are not the terminal exception, but leaving them unresolved risks failing the existing PlayMode `LogAssert.NoUnexpectedReceived()`: Test Framework 1.4.5 `LogScope.cs:234–244` checks **all** unhandled logs, including warnings. PlayMode has not yet run, so this is a source-backed risk, not a fabricated native failure.

Pinned-source review identifies an incomplete **owned Lab composition**:

- [LatiosWorld constructor](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/Core/Framework/LatiosWorld.cs#L117) installs its initialization rate manager.
- [LatiosWorldSystemGroups](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/Core/Systems/_Essentials/LatiosWorldSystemGroups.cs#L39) inserts the Latios essentials but not the two Unity ordering targets. Its declarations and [SyncPointPlaybackSystem](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/Core/Systems/_Essentials/SyncPointPlaybackSystem.cs#L19) contain the observed ordering constraints.
- Both pinned [standard explicit](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/Core/Editor/ScriptTemplates/StandardExplicitBootstrap.txt#L49) and [standard injection](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/Core/Editor/ScriptTemplates/StandardInjectionBootstrap.txt#L48) bootstraps inject Unity systems before sorting. The smaller owned Lab omitted these needed targets while retaining the ordering attributes.
- Official Entities 1.3.5 `DefaultWorld.cs:11–13` places BeginInitialization ECB in the initialization OrderFirst bucket. `Unity.Scenes/SceneSystemGroup.cs:9–14` places the real empty SceneSystemGroup in its normal bucket. These match the respective Latios constraints. `ComponentSystemSorter.cs:456–476` warns specifically when the target is absent from that group's update list.

The corrected owned factory explicitly creates and inserts **BeginInitializationEntityCommandBufferSystem** and **Unity.Scenes.SceneSystemGroup** in that same initialization group before sorting. Runtime receives a direct `Unity.Scenes` asmdef reference. These are real pinned Unity types, not dummy warning anchors. The ECB owns its standard singleton, pending-buffer list and initial **16 KiB rewindable allocator**, released by normal World ownership. The empty scene group has no custom OnCreate/OnUpdate and does not inject SceneSystem or scene-streaming children.

Optional [Latios SceneManager installation](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/Core/Framework/CoreBootstrap.cs#L16) remains unused. The pinned World explicitly supports [manual scene-blackboard creation when that manager is absent](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/Core/Framework/LatiosWorld.cs#L131), which the Lab retains. Broad Unity injection and renderer/audio/text installers remain absent. No warning is ignored, expected away or suppressed; the original logging assertion is unchanged.

## Verification and next native requirements

Independent read-only review confirmed the concrete collection APIs, unchanged inventory ordering, real target types/buckets, direct assembly reference and existing disposal guard. Narrow source guards cover the inventory route and both ordering targets before sorting, while keeping the logging assertion present. **31 Lab static tests and 23 CI tests pass**, as do preflight, frozen-source checks and whitespace checks. These are not engine-shim execution or native test passes.

The live [source review hashes](Validation/20261008-compilation-domain/source-review.json) identify the pending changes against native baseline `8bca004`; they do not reuse the old exact-source review as proof for modified files. The four-assembly success table above remains attached to the original failed run. The next authorized run must compile the changed Runtime/EditorTools, confirm only the intended two systems were added, finish inventory capture, show these three warnings absent, and execute the untouched **49 EditMode + 1 PlayMode** cases in both Burst modes. No upstream cleanup patch, expectation change, package/lock/Editor/define mutation, product change, new gate or native launch is included here.
