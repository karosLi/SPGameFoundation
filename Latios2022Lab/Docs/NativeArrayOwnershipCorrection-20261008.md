# S1a second import: mutable NativeArray ownership

2026-10-08. **The second import failed at an owned C# compilation error. The correction below has not run in Unity.** All four native test phases remain NOT_RUN and S1a remains incomplete.

## Verified failure and retained evidence

[Run 37723875083, attempt 1](https://github.com/karosLi/SPGameFoundation/actions/runs/37723875083/attempts/1), native job `113137530365`, ran source `bc10273e8fd78cd4047cb0c52a39c823c4dbcf9e`, tree `de348dba5bbbf91735611cec2306d210bfa26439`. Local equivalent `50865ccdba0be3c8d151ef1359ab2c83802ae4ed` has the same tree. All 40 launch-input hashes match that source; the native checkout started clean. The host/P0/static gate passed before the native job.

The full 304,854-byte Unity log identifies one unique C# error, repeated three times: `PsyshockProbe.cs(46,17)`, **CS1654**, because `bodies[i]` assigns through a `using var` local of the `NativeArray<ColliderBody>` value type. Unity exited 1; the phase launcher exited 2. This happens before any EditMode/PlayMode test, environment capture or Core cleanup oracle. The previous duplicate runner diagnostics are absent. The documented upstream Properties asmref informational output remains, with no new evidence making it a fatal error.

Both official artifact wrappers passed their API SHA-256 checks. The single numbered part restored **29 files**, including the evidence manifest; all **28 original-file hashes** passed. Full normalized ProjectSettings are now present both in early evidence and the complete archive. There is no remaining transfer gap for this run. Exact artifact IDs, wrapper/Unity-log/archive hashes and verbatim [manifest](Validation/20261008-second-import/manifest.json) / [lock](Validation/20261008-second-import/packages-lock.json) are recorded in [provenance.json](Validation/20261008-second-import/provenance.json). The [first failed run](ImportCorrection-20261008.md) and its evidence remain unchanged.

The real second lock resolves Test Framework **1.4.5** and URP **14.0.12**, as requested by the previous host correction. Collections 2.5.1, Mathematics 1.3.2, Serialization 3.1.1, Entities 1.3.5, Graphics 1.4.2, Burst 1.8.18 and original Latios Git commit `381a77dbf774ff603014d5695ef6c06abaa25d96` are retained. The real post-import manifest/lock/settings pass the existing static preflight, including the exact observed toolchain addition, all three required define groups and .NET Standard API value 6. This is static validation of actual retained inputs, not a successful native environment inventory. The failed lock stays outside active `Packages`.

## Bounded code correction

The only runtime source edit is in `PsyshockProbe.RunPairs`:

1. Allocate `bodies` as a normal mutable local.
2. Enter an outer `try` before allocating the pair list or creating fixture entities.
3. Preserve the existing pair capacity, real entity identities, layer build/query modes, scheduled dependency, normalized pair validation and independent AABB oracle.
4. Dispose `bodies` in the outer `finally`.

This retains cleanup order: complete pending jobs and dispose the layer; dispose pairs; dispose bodies; dispose the owned world. Failure during pair allocation, fixture initialization, layer construction or oracle validation still exits through the applicable ownership scopes. The change does not introduce an alias workaround, change `NativeArray` to a reference type or move disposal ahead of dependent work. [Microsoft's CS1654 documentation](https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs1654) explains why a value-type member assignment through a read-only local is rejected.

## Receiver audit and verification limits

A separate read-only review covered all **11 Lab-owned C# files**. The six remaining using-declared native containers are only read or passed by value; their writes occur through mutable job fields. No other direct index/property assignment or `ref`/`out` use of a using-declared struct was found. Collections 2.5.1's `NativeList.AsParallelWriter` copies the shared list pointer/safety handle, preserving shared pair writes and length. Pinned LatiosWorld is a class; LatiosWorldUnmanaged and EntityManager operations access shared implementation data. No additional receiver-related compile blocker was identified.

An isolated C# 9 language probe used a real disposable struct with a settable indexer: its negative `using var` form reproduced CS1654; the mutable-local/try-finally form compiled and verified exactly-once disposal on normal and setup-exception paths. It does **not** compile actual Unity NativeArray, Latios, jobs or the Lab assemblies and is not presented as native acceptance. Static preflight, **25 Lab tests and 19 CI tests** pass; normalized native inputs also pass static preflight.

No test fixture data, case declaration, threshold, strict assertion, Burst/safety control, package pin, upstream implementation or root product file changed. Expected discovery remains **49 EditMode + 1 PlayMode**, each with Burst on/off. The original suspected upstream reactive-cleanup defect was not reached and is unmodified. Runtime recompile, downstream Editor/PlayMode compilation, generated code, native safety, cleanup/query behavior, repeat clean controls and IL2CPP/device gates remain unverified. The prior gate is removed; publication and the next reserved native slot remain with the execution lead.
