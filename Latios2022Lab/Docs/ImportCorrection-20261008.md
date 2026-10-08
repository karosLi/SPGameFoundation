# First S1a import: diagnosis and host correction

2026-10-08. **First import failed; corrected source has not run in Unity.** No test result, source-generation result or S1a acceptance follows from this correction.

## Actual failed control

[Run 37715538586, attempt 1](https://github.com/karosLi/SPGameFoundation/actions/runs/37715538586/attempts/1) ran exact source `83c3b065e69a704d9018ad78d1ccfaefbbdcb5a9`, tree `7740635acd6275938308f9b4661ea6392e41ae36`, in a fresh isolated Lab. The actual Editor was Unity **2022.3.62f2**, revision `7670c08855a9`, on macOS 26.4 ARM64. Unity exited 1 and the Lab launcher exited 2 during the first Burst-on import. Both EditMode phases and both PlayMode phases remained **NOT_RUN**. There is no test XML or environment capture; Core cleanup, Psyshock, QVVS, safety/Burst witnesses and reentry were not reached.

The complete 29,641-byte Unity log and 1,325-byte launcher log were recovered from early-artifact heads and verified against their original SHA-256 values. The first real [lock](Validation/20261008-first-import/packages-lock.json), post-import [manifest](Validation/20261008-first-import/manifest.json), exact source/run IDs and hashes are retained in [provenance.json](Validation/20261008-first-import/provenance.json). These are verbatim failed-run snapshots outside active `Packages`, not a new or accepted resolution.

The separate full part00 upload failed after a CreateArtifact timeout and `read EADDRNOTAVAIL`. No numbered part was finalized. Normalized ProjectSettings `.asset` bytes were omitted from the old early archive and have not been retrieved or reviewed. This candidate therefore includes complete retained ProjectSettings files in future early archives, under the existing 16 MiB hard cap. Full bounded parts remain unchanged; this does not retroactively recover the missing files.

## Fatal owned error versus upstream diagnostic

The two Lab test asmdefs combined explicit runner references with legacy `optionalUnityReferences: [TestAssemblies]`. The Editor diagnosed duplicated engine/editor runner references in the EditMode assembly and a duplicated engine runner in PlayMode. Exact Unity [LoadingAssemblyDefinition.cs](https://github.com/Unity-Technologies/UnityCsReference/blob/2022.3.62f2/Editor/Mono/Scripting/ScriptCompilation/LoadingAssemblyDefinition.cs#L159) throws `AssemblyDefinitionException` for duplicate references and marks `CompilationSetupErrors.LoadError`. This is the observed compilation setup blocker.

The correction follows the existing product test-assembly pattern: explicit `UnityEngine.TestRunner` / `UnityEditor.TestRunner`, `overrideReferences: true`, precompiled `nunit.framework.dll`, `autoReferenced: false`, and the existing `UNITY_INCLUDE_TESTS` constraint. Legacy optional references are removed. Test Framework 1.4.5's exact [official package](https://download.packages.unity.com/com.unity.test-framework/-/com.unity.test-framework-1.4.5.tgz), `Documentation~/workflow-create-test-assembly.md`, identifies NUnit references as the test-assembly marker; the matching [Unity documentation](https://docs.unity3d.com/Packages/com.unity.test-framework@1.4/manual/workflow-create-test-assembly.html) describes explicit runner/NUnit references. No C# fixture, test count, strict assertion or threshold changes.

The accompanying Entities message about `Unity.Properties.Internals.asmref` is **not an independently established fatal error**. Exact Unity [AssemblyGraphBuilder.cs](https://github.com/Unity-Technologies/UnityCsReference/blob/2022.3.62f2/Editor/Mono/Scripting/ScriptCompilation/AssemblyGraphBuilder.cs#L125) prints that precise text with `Console.WriteLine`; that branch neither throws nor sets a compilation setup error. The separate [missing-reference validation](https://github.com/Unity-Technologies/UnityCsReference/blob/2022.3.62f2/Editor/Mono/Scripting/ScriptCompilation/EditorCompilationInterface.cs#L166) reports a warning.

Primary package source explains the stale target:

- Official Entities 1.3.5, `Unity.Scenes.Editor/Internals/Properties/Unity.Properties.Internals.asmref`, targets GUID `807be9350277eff4182cd3600de41d3a`.
- The official Properties 2.1.0-exp.7 package's `Runtime/Unity.Properties.asmdef.meta` matches that GUID. Its assembly disables itself from Unity 2022.2.0a18 through `USE_PROPERTIES_MODULE` / `!USE_PROPERTIES_MODULE`, because Properties became an [engine runtime module in Unity 2022.2](https://docs.unity3d.com/2022.3/Documentation/Manual/WhatsNew20222.html).
- Serialization 3.1.1 declares only Burst and Collections. Its [3.0 migration changelog](https://docs.unity3d.com/Packages/com.unity.serialization@3.1/changelog/CHANGELOG.html) records removal of the Properties dependency when moving to Unity 2022.2. The missing Properties package entry is consistent with the declared graph.
- Official registry metadata has no Properties 2.1.1. The experimental 2.1.0-exp.7 package includes a source generator and a Performance Testing dependency, so adding it cannot be characterized as a metadata-only repair.
- The next published Entities version, 1.3.8, has identical dependencies and all 47 `.asmdef`/`.asmref` files are byte-identical to 1.3.5. A bump would retain this diagnostic.

The stale-package explanation is a source-based inference, not an upstream issue acknowledgement. Entities remains **1.3.5**, no Properties package is added, and no package source is patched. Re-observe the diagnostic in the next import; investigate any later C# or runtime failure on its own evidence.

## Host dependency and normalization corrections

| Input/check | First-run evidence | Corrected expectation |
| --- | --- | --- |
| Test Framework | Manifest requested 1.1.33; Collections 2.5.1 requires 1.4.5; real lock and registered package are 1.4.5 | Declare and check 1.4.5 |
| URP | Manifest/lock said 14.0.11; actual registered built-in package was 14.0.12, as were Core RP and ShaderGraph | Declare 14.0.12; require the new real lock and actual inventory to agree |
| Collections / Mathematics / Serialization | Real lock resolved 2.5.1 / 1.3.2 / 3.1.1 | Check those exact transitive versions |
| Editor-added toolchain | Post-import manifest gained `com.unity.toolchain.macos-arm64-linux-x86_64` 2.0.5 | Permit only this exact addition with a matching registry lock entry; reject other additions/configuration |

Collections 2.5.1's exact [official package](https://download.packages.unity.com/com.unity.collections/-/com.unity.collections-2.5.1.tgz), `package.json`, independently confirms the 1.4.5 Test Framework requirement. This changes the host's inaccurate declaration; it does not change the test framework version actually selected by the first import. URP is aligned with this exact Editor's observed built-in version, not inferred from a newer Editor or registry default. Native resolution of the corrected input is still required.

Latios stays at original `381a77dbf774ff603014d5695ef6c06abaa25d96`; Entities 1.3.5, Graphics 1.4.2, Burst 1.8.18 and DSPGraph 0.1.0-preview.22 remain unchanged. No toolchain is added to authored manifest or installed by these tools. Root product `Assets`, `Packages`, `ProjectSettings`, engine version, Burst/safety controls and upstream notices are unchanged. The old short-lived CI request is removed; the lead must create a new gate-only child after review and queue reservation.

## Reproducible primary-source hashes

Official tarball bytes were checked against the registry's SHA-1 before inspection; SHA-256 values below identify the inspected content. Registry metadata is available at `https://packages.unity.com/<package-name>`; the archive links identify exact versions.

| Official archive | Registry SHA-1 | Inspected SHA-256 |
| --- | --- | --- |
| [Entities 1.3.5](https://download.packages.unity.com/com.unity.entities/-/com.unity.entities-1.3.5.tgz) | `0afb1656b1455d5957ffc882660091a5176e6a73` | `93eb2305939557afa1157e13c3a81826d47d7f65a5bf655bc5ba829ce8de0271` |
| [Entities 1.3.8 comparison only](https://download.packages.unity.com/com.unity.entities/-/com.unity.entities-1.3.8.tgz) | `6a355619334f7ed62ba31c1c9894af096312544d` | `04a78a7e23151df9bd957cb0f45297cc8dc6d1f94d5643bffea1b138b845444d` |
| [Collections 2.5.1](https://download.packages.unity.com/com.unity.collections/-/com.unity.collections-2.5.1.tgz) | `56bff8827a7ef6d44fcee4f36e558a74da89c1a0` | `f6c134f7cfe747df76d863463f7bea5a7b44c19dff35039c109118a9ba087c81` |
| [Test Framework 1.4.5](https://download.packages.unity.com/com.unity.test-framework/-/com.unity.test-framework-1.4.5.tgz) | `0a21eb82d95cd331643a1e0ce4e8e9a5f18954c8` | `79c1e4efe3ed98f031573e19519b991a9aa056c91e5563c825e1b22f1c9f1b77` |
| [Serialization 3.1.1](https://download.packages.unity.com/com.unity.serialization/-/com.unity.serialization-3.1.1.tgz) | `a31a78f949b0b327339dbb19be2b93a421251113` | `f394f0ccecbbb5ade8ba4d989c82ec8966e1db5ef006ae45d2b542fc1da28fb8` |
| [Properties comparison only](https://download.packages.unity.com/com.unity.properties/-/com.unity.properties-2.1.0-exp.7.tgz) | `9b1ad9956a7624a4a674cfb84b7bed23c00b3a5b` | `fa3cdf60e7291837919394ea24b2fb682aa96c45e42ae0d9c337ae9fc9b6824f` |

Exact UnityCsReference tag `2022.3.62f2` file SHA-256: AssemblyGraphBuilder `a1787491c997e37124f33c0c4e32d88e65ce34986a6c83dce44e50989056a5ea`; LoadingAssemblyDefinition `f2ca5bc525a0bb58b6255c421bd50a94ba19765e38321d408b770bbac319a0bf`; EditorCompilationInterface `55037fecf5d6f58cbc977c9378b6b2d356669bf6e4cf115de6e1205176bc6a82`.

## Verification and remaining native gates

Static preflight, **25 Lab tests and 19 CI tests** pass. The regression checks reject the actual old duplicate-reference shape, missing NUnit, mismatched registered URP/lock, transitive drift, unknown manifest additions and toolchain version/source drift. The early-settings test first failed against the old packager because the complete `.asset` entry was absent, then passed after the inclusion change; oversized early evidence still fails without discarding full parts. No .NET product-harness result is used as Lab compilation evidence.

Next authorized control must establish clean import, actual package inventory/lock agreement, 49 EditMode + 1 PlayMode cases with both Burst modes, unchanged strict cleanup/query assertions and actual safety/execution witnesses. The suspected upstream `RemoveComponent(addQuery, ...)` cleanup issue was not reached and remains unmodified. Repeat clean import, desktop IL2CPP build and player run, plus physical Android/iOS acceptance remain separate open gates. The original failed record is preserved even if a later candidate succeeds.
