# S1a third import: EditorTools qualification and dependency

2026-10-08. **Import failed; the two owned EditorTools corrections below have not run in Unity.** No test passed or ran in this control, and S1a remains incomplete.

## Exact run and complete evidence

[Run 37744846280, attempt 1](https://github.com/karosLi/SPGameFoundation/actions/runs/37744846280/attempts/1), native job `113203846666`, ended **failure** on source `2edacdbe1315a6da682be39c1fa2cebae2e14bda`, tree `5972a414a4d3d4f2ef2e9fd54f3442ac8fb67a10`. Local release equivalent `881d7322928abb8a6eadc9a602f372b8486b3304` has that same tree. The hosted P0/source/static release gate passed. The separate old-product [.NET run 37744846276](https://github.com/karosLi/SPGameFoundation/actions/runs/37744846276) ended success; it does not compile or test this isolated Lab.

Both artifact wrappers match their official API sizes and SHA-256 values. One numbered part restored **29 files**; all **28 original-file hashes** and **40 launch-input/source hashes** verify. The initial source was clean. The complete Unity log is 316,962 bytes, SHA-256 `edb24ceea2c749e0181680631d911c175995a524fffa11f5b225b77e7078c2a6`. [Provenance](Validation/20261008-third-import/provenance.json) records exact artifact IDs, archive hashes and both coded compiler diagnostics. There is no missing-file or upload gap.

The third real manifest and lock are byte-identical to the preserved [second manifest](Validation/20261008-second-import/manifest.json) and [second lock](Validation/20261008-second-import/packages-lock.json). This equality was checked against the restored original bytes; no new lock was invented or promoted to active Packages. Unity is still 2022.3.62f2 (`7670c08855a9`), native ARM64 on macOS 26.4. No package pin or Editor setting is changed by the correction.

## Two actual errors, both in owned EditorTools

The full log contains two distinct coded errors, each repeated three times:

1. **DC0061**, `LabEnvironment.cs(1,1)`: `Latios2022Lab.EditorTools` references Entities without a direct Collections reference. The Entities generator requires Collections' `AllocatorHandle` type.
2. **CS0104**, `LabEnvironment.cs(38,28)`: the unqualified `PackageInfo` name is ambiguous between `UnityEditor.PackageInfo` and `UnityEditor.PackageManager.PackageInfo`.

The log shows successful Csc, ILPostProcess and CopyFiles steps for **Latios2022Lab.Runtime** and **Latios2022Lab.PlayMode**. This is progress beyond the previous NativeArray compiler failure, not test execution. EditorTools fails and prevents the complete Editor import and dependent Editor test assembly from becoming usable. The import process exits 1; the launcher exits 2. EditMode on/off and PlayMode on/off all remain **NOT_RUN**. No test XML or `environment.json` exists; runtime safety/Burst witnesses, reentry, Core cleanup and Psyshock assertions were not reached.

## Narrow correction and primary support

- Fully qualify `UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()` and remove the now-unused namespace import. Unity's [2022.3 API reference](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/PackageManager.PackageInfo.GetAllRegisteredPackages.html) identifies this exact package inventory method. The record fields, sorting and inventory format are unchanged.
- Add `Unity.Collections` to the owned EditorTools asmdef. The inspected official [Entities 1.3.5 archive](https://download.packages.unity.com/com.unity.entities/-/com.unity.entities-1.3.5.tgz), `Unity.Entities/SourceGenerators/Source~/SystemGenerator/SystemGenerator.cs`, checks direct Collections references and emits DC0061 at lines 174–175. `SystemGenerator.Common/SystemGeneratorErrors.cs` defines the matching diagnostic. Its archive hash remains the one preserved in the [first import investigation](ImportCorrection-20261008.md).
- Add a static preflight dependency check for Lab assemblies referencing Entities, and correct the existing direct-dependency test's EditorTools requirement. A negative test removes Collections from the corrected assembly description and verifies rejection; an assembly with neither Entities nor Collections remains allowed.

A separate review inspected all **11 Lab C# files and four asmdefs**. It found DC0061 in the full log in addition to CS0104 and no further concrete name-resolution issue. Diagnostic review includes generator codes, not only `CS` codes. The existing Physics alias and previously qualified IL2CPP enum remain intact.

## Verification and remaining gates

The updated direct-dependency test failed against the old EditorTools asmdef, then passed after its correction. Static preflight, **26 Lab tests and 19 CI tests**, and whitespace checks pass. These are not Unity compilation results. No additional native launch, remote write, fixture edit, test-count/threshold relaxation or upstream patch was performed. The prior release request is removed for a separately reviewed gate-only child.

Root product files, original Latios/Entities dependencies, the NativeArray ownership fix, strict **49 EditMode + 1 PlayMode** cases, Burst/safety controls and original cleanup assertion are unchanged. The suspected upstream reactive-cleanup defect remains unexecuted and unmodified. Fresh native import and all Editor tests still need to run before any repeat-clean, IL2CPP, device or S1b claim.
