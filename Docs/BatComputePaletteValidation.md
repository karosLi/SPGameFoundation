# Optional bounded BAT compute palette

## Scope and selection

`new BatCharacterBatch(asset, capacity, preferCompute: true)` requests the optional `GpuComputePalette` backend. The existing four-argument constructor calls keep their behavior: vertex BAT by default, with the real weighted CPU fallback. `forceCpu: true` always wins. The scene's Inspector **Prefer Compute** checkbox and `G` key request compute; `C` still forces CPU. The startup/recreation log and `ActiveBackend` report the selected backend, including fallback.

This remains the original bounded three-bone, two-influence asset and static-parent two-bone IK, not arbitrary rig import, generalized GPU animation or gameplay-authoritative IK. All placement, mirror, clip-frame blend and IK inputs remain presentation-only. No mobile speedup or device support is inferred from the existence of the compute code.

Compute selection independently requires:

- The original explicit API allowlist, graphics device, instancing, shader level ≥45, exact accepted sampled BAT format and texture dimension limits
- At least two vertex structured-buffer inputs (instance data and computed palette), two compute buffer inputs, and a supported 64-thread workgroup
- A present `BatPalette` compute asset, `BuildPalette` kernel, exact 64×1×1 kernel dimensions, and `ComputeShader.IsSupported(kernel)`
- A supported `BatComputed` render shader and sufficient maximum graphics-buffer size for both bounded allocations

Missing compute gates fall back to vertex BAT when its independent shader/buffer gates permit, then to CPU. GLES and null devices preserve the existing lower-tier behavior. No runtime readback is used to choose the tier. An actual allocation failure still fails construction and disposes owned resources; it is not disguised as a supported-device fallback.

## Work and ownership

One compute thread handles one active actor. It samples/interpolates three model-space skin matrices from the existing bone animation texture; if IK is enabled, it computes the upper/lower override once for that actor. The output is three consecutive `BatRows` entries (two float4 rows each), **96 bytes per actor**, in a structured GPU buffer. A 64-thread group size needs 1–4 groups for 1–256 actors. Rounded-up threads exit before reading/writing any actor data.

The production vertex shader reads the two weighted bone matrices from that buffer and performs skinning/placement. `BatCommon.hlsl` owns the shared IK, transform and weighted placement equations; `BatSampling.hlsl` owns BAT sampling. The original vertex backend and compute backend reuse those functions. CPU `SamplePalette` provides an independent C# reference using the existing CPU IK math; CPU weighted skinning remains its own per-vertex path.

Each batch owns its compute-shader instance and buffer bindings, so two batches/assets do not overwrite each other's uniforms or outputs. Dispatch and draw use Unity's graphics queue, with the engine managing the compute-write to vertex-read dependency. This implementation does not use async compute or manual fences. A clean batch reuses the already-built palette without uploading or dispatching. Empty batches upload, dispatch and draw nothing. Dirty reuse overwrites every active row; inactive capacity is never drawn.

Normal `Prepare`/`Draw` uses preallocated arrays, meshes, materials and GPU buffers. `ReadbackComputedPaletteForValidation` is explicitly test-only, synchronous and never called by normal rendering. Test artifacts and captures are outside shipping code paths. Batches dispose their own resources idempotently and never own the shared clip set.

## Honest counters and bounded storage

At capacity 256:

- Instance GPU storage: **16,384 bytes**, reported by `InstanceBufferBytes`
- Computed palette GPU storage: **24,576 bytes**, reported by `ComputedPaletteBytes`
- Existing shared float BAT: **11,520 bytes** for three bones ×120 frames, reported by `PaletteBytes`; compact accepted half fixtures use **5,760 bytes**
- Fully dirty instance upload: **16,384 bytes**, reported by `BytesUploaded`. Computed palette output is GPU-written, not CPU-uploaded.
- Fully dirty compute output: **24,576 logical bytes**, `PaletteBytesWritten`; one `DispatchCalls`, four `DispatchGroups`, one production `DrawCalls`

Upload/output/dispatch/draw counters describe the most recent `Prepare`/`Draw`/`Record` operation. Calling `Prepare` again on clean data resets upload/output/dispatch to zero. Storage counters are capacity-based and exclude engine, driver, mesh, shared asset and staging overhead. Logical GPU-written bytes are not measured bandwidth. Dispatch counts are submissions, not GPU elapsed-time measurements.

## Tests and evidence boundary

EditMode / .NET:

- `SPF.Tests.EditMode.BatComputeTests`: ABI/offsets; per-instance palette versus existing independent per-vertex weighted skinning; both precisions; cross-clip and seam frame interpolation; mirror/non-unit scale; zero, reachable and unreachable targets; both bend signs; each missing compute gate; shared gates; forced CPU; exact resource bounds
- `SPF.Tests.EditMode.BatCharacterTests`: unchanged reference asset, baking, precision, IK and original selector contracts

Graphics-enabled PlayMode filter: `SPF.Characters.Tests.PlayMode.BatGraphicsTests`.

- `ComputeBufferReadback_ThreeMatricesMatchCpuReferenceAcrossAllCases`: actual GPU output coefficients for float/accepted half, 65 actors and rounded dispatch tail
- `VertexStageReadback_MatchesWeightedCpuAndBoundedIk`: original and computed vertex paths, float/half probes against CPU positions
- `ProductionShader_CpuGpuPixelParity_AndEmptyReuse`: original/computed production materials against actual CPU weighted meshes
- `ProductionDraw_ComputePaletteMatchesCpuWithClipBlendMirrorScaleAndBothIkBends`: real `Graphics.RenderMeshPrimitives`, CPU `Graphics.DrawMesh`, cross-clip matrix blends, mirror/non-unit scales, reachable/unreachable targets, both bends, nonempty actor regions and empty clearing
- `ActualDemo_ComputePaletteRenderMeshPrimitives_IKAndRecreate`: real demo, independent phases/facings, visible IK arm changes, disable/recreate
- `ComputeCapacityCounters_EmptyReuseAndDispose`: 1/63/64/65/255/256 capacities, final actor readback, counters, empty and single-actor reuse, idempotent disposal
- `ComputeBatchesOwnBindingsAndForceCpuStillOverridesPreference`: independent shader state across distinct assets and explicit forced CPU
- `WarmedPrepareDoesNotAllocateManagedMemory` and `WarmedComputeDrawHasCalibratedZeroCurrentThreadManagedAllocation`: 64 warmed/64 measured dirty iterations with retained-array/empty calibration before and after; only current-thread managed allocation during calls, not full-frame/native/driver memory

PNG captures are saved under `Artifacts/Bat`. Graphics capability skips are explicitly **unverified**, not passes. The .NET harness cannot execute shaders, validate Unity rendering or measure Unity allocation. Central Unity execution and visual inspection are required before recording those results as passed.

Unity 2022.3 API choices were checked against the installed 2022.3.62f2 `UnityEngine.CoreModule.xml` and compiled against its actual DLLs, including `ComputeShader.HasKernel`, `IsSupported`, `GetKernelThreadGroupSizes`, `SetBuffer(GraphicsBuffer)`, `Dispatch`, `GraphicsBuffer.GetData`, and `SystemInfo.maxComputeWorkGroupSize(X)`/buffer limits. This is API verification only; it does not establish GPU runtime support.

### Local pre-integration checks (2026-10-06)

- Serial .NET harness: **75 assemblies built, 0 warnings, 0 errors**; focused BAT reference plus compute contracts: **146 passed, 0 failed, 0 skipped** (126 existing +20 new). This is C# reference/selection evidence only.
- Source compilation against installed Unity **2022.3.62f2** reference DLLs: presentation runtime/demo and graphics tests compiled with **0 warnings, 0 errors**.
- GPU execution, captured pixels, actual production draw and calibrated Unity allocation tests were **not run by the implementation worker**. They are provided for the central graphics-enabled Unity run; no GPU success or zero-allocation result is claimed here.
