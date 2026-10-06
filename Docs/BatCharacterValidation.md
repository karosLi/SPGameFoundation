# Bounded weighted BAT character validation

## What is implemented

A separate presentation-only backend in `SPF.Presentation.Characters`; existing cutout gameplay rendering is unchanged. It contains an original 95-vertex/88-triangle smooth geometric character, exactly three bones, two authored one-second clips sampled at 60 frames each, genuine two-influence arm vertices (including 50/50), a shared immutable bone animation texture, vertex-stage weighted skinning, bounded analytic GPU IK, and a real Burst CPU weighted-mesh fallback. This is runnable code, not a compute placeholder.

Open **SPF → Characters → Create Or Update Weighted BAT Scene**, then Play. The generated scene is `Assets/SinglePlayerFoundation/CharacterValidation/Scenes/WeightedBat.unity`. It creates a camera if needed. Controls:

- `1`, `2`, `3`: 1 / 64 / 256 characters
- `C`: recreate the batch with forced CPU fallback (a loading-time allocation)
- `I`: selected first character's IK on/off
- Hold pointer: selected character's unmirrored model-space target
- Inspector: bend sign, target, actor count, contact shadows
- Gold marker: requested target; green marker: independently evaluated source-pose CPU tip

The default is 64 independently phased, tinted and alternately mirrored actors. Existing `BlobShadow` footprints use the data-texture sprite tier. They are contact cues, not physically cast shadows or occlusion. No screenshot or GPU result feeds gameplay, hitboxes, actor identity, RNG, saves or replay. Presentation state has an explicit source-array/Animator2D immutability test.

## Contracts and restrictions

- Exactly static identity root → upper → lower, parent-before-child. Zero bind rotations; upper/lower positive lengths ≤2; lower bind offset equals upper length. No extra descendants, root motion or animated translation offsets.
- At most two clips, 60 samples per clip, 128 vertices, 768 triangle indices and 256 instances per batch. Two finite nonnegative influences sum to one. Invalid input fails before allocation/upload.
- Bind pose is independently evaluated from `BoneDef`, never assumed to be the first animation frame. Each matrix is posed-model × inverse(bind-model).
- Loop samples exclude a duplicated endpoint, with explicit last-to-first interpolation. Non-loop clips include both endpoints. Single-frame clips work. Matrix interpolation can shrink/shear rotation and is not authoritative pose interpolation.
- IK is full override on bones 1/2 for one static-parent two-bone chain. Both bend signs, zero target direction and min/max reach clamping match the CPU solver. It does not solve arbitrary hierarchies, blend IK weights, retarget, propagate descendants or compute root motion.
- IK runs in the vertex stage, deliberately repeating small bounded trigonometry. No compute skinning/palette pass is fabricated. A later compute pass needs benchmark evidence that its dispatch, barriers and output reads beat this baseline.
- Shaders target the built-in rendering pipeline tested by this project. No URP/mobile correctness claim is made without those runs.

### GPU layout

`BatInstance` is exactly 64 bytes: four float4s at offsets 0/16/32/48: placement xy/scale/facing, absolute frame A/B/blend/z, tint RGBA, model target xy/IK enable/bend.

`BatRows` is exactly 32 bytes: two float4 rows `(m00,m01,tx,0)` and `(m10,m11,ty,0)`. Do not upload the existing 24-byte `Affine2D` directly.

BAT width is `2 * bones`; height is the sum of clip frame counts. Texture columns `2*b` and `2*b+1` store the rows. Linear RGBAHalf or RGBAFloat, point/clamp, no mips/compression/sRGB. `Texture2D.Load` reads explicit integer coordinates; shader lerps matrices and applies two weighted transforms, then facing/scale/placement. CPU fallback reads an independent dequantized palette with the exact selected precision. The shared texture is uploaded once and its Unity CPU copy released.

### Precision gate and capabilities

Baking measures full-float versus half-dequantized weighted vertex positions at every sampled endpoint and midpoint, including clip seams. Because both skinning and matrix interpolation are linear, the endpoint maximum bounds intermediate coefficient-quantization error. The accepted budget is ≤0.25 pixel at the declared maximum 4× instance scale and 256 pixels/world-unit. IK is float computation and is independent of BAT coefficient quantization.

For the original asset, the .NET reference measured max model error **0.0012308495**, or **1.2603899 pixels** at that deliberately conservative envelope. **Half is rejected, so the original sample chooses float.** A 0.1-scale companion fixture tests the actual half GPU path only if it meets the same budget. The budget is not weakened to force a half result. Half draws require an explicit orthographic camera within 256 pixels/world-unit; use `allowHalf:false` for another camera/zoom envelope. The test-only command recording API controls its own target/projection and must not be used to bypass production camera validation.

The independent selector requires a non-null device, explicitly admitted API (D3D11/12, Metal, Vulkan or OpenGLCore), shader support, shader level ≥45, instancing, at least one vertex-stage structured-buffer input, texture dimensions and buffer-size bounds, and support for the exact sampled floating-point format. Compute support alone is not evidence for vertex SSBO support. GLES currently uses the CPU weighted fallback. Null/headless devices issue no draw. GPU test skips explicitly mean unverified, not passed.

## Memory, upload and lifecycle

All valid warmed updates use owned preallocated native arrays, buffers, meshes and material property blocks. No per-frame palette upload or runtime GPU readback. Batches do not own their shared clip set; dispose batches before the clip set. Dispose is idempotent. Count grows only through validated `Add`, stops at capacity, and `Clear` prevents stale actors. A clean `Prepare` uploads zero bytes.

At 3 bones ×120 frames:

- BAT GPU storage/upload once: **11,520 bytes float**, or **5,760 half** when accepted
- Two retained dequantized CPU palettes: **23,040 bytes total** (both precisions retained explicitly)
- Bind CPU vertex data: 95 ×48 = **4,560 bytes**; triangles: 264 ×4 = **1,056 bytes**, plus small clip metadata
- GPU instance storage: 64 ×capacity; 256 actors upload **16,384 bytes/frame** when dirty
- CPU fallback uses pages of ≤64 actors, static UV/UInt16 index streams, dynamic Float32 position + Float32 RGBA stream (**28 bytes/vertex**). At 256 ×95 vertices, dynamic staging and each full GPU position/color allocation are **680,960 bytes**; a fully dirty frame uploads that payload. Static UVs add 194,560 bytes and static UInt16 indices 135,168 bytes across pages, excluding engine/driver overhead.
- Shader upper bound: two influences ×two frames ×two row texels = eight BAT texels/vertex before cache effects. This is a logical fetch count, not measured bandwidth.
- Shadow and marker allocations/uploads are separate and use existing sprite counters. Backend toggles and scene initialization intentionally allocate.

Bounds include sampled poses, matrix-lerp endpoints and full IK rotation envelopes (including each influenced vertex's distance from its bind origin), then instance placement, scale and facing. No bind-only culling box is used.

## Verification and current evidence

Focused test names:

- EditMode / .NET: `SPF.Tests.EditMode.BatCharacterTests` (**126 cases**)
- Graphics-enabled PlayMode: `SPF.Characters.Tests.PlayMode.BatGraphicsTests` (**9 cases**, including two actual demo draw cases)

EditMode covers ABI, exact source-pose sampled palette parity, independent bind inverse, actual mixed weights, endpoints/negative time/seams, one-frame/one-shot clips, matrix interpolation, both IK bend/facing signs, zero/unreachable targets, precision acceptance/rejection, malformed input, capability gates, ownership/disposal and source/snapshot-independent state.

PlayMode performs actual vertex-stage world-position readback into a linear RGBAFloat target (full-size float and compact half fixtures; tolerance 1e-4), production weighted-mesh CPU/GPU pixel comparison with a one-pixel edge band, nonempty per-actor regions, no magenta output, empty-count/stale-instance checks, 256→0→1 reuse, logical upload accounting and warmed main-thread managed-allocation checks. Test readback is synchronous and test-only. Numeric readback uses the production HLSL include; visible parity independently exercises the production material. PNGs go to `Artifacts/Bat/bat-gpu.png` and `bat-cpu.png`.

At the first integrated Unity checkpoint (2026-10-06):

- Installed Unity 2022.3.62f2 DLL compilation of runtime, demo, editor and graphics tests: **0 warnings / 0 errors**
- Final focused .NET run: **126/126 passed, 0 skipped**; 234 ms test execution (restore emitted NU1900 vulnerability-cache warnings because the home cache is read-only)
- Unity 2022.3.62f2 initial graphics-enabled run: **126 EditMode / 7 PlayMode passed, 0 skipped** on OpenGLCore/Mesa llvmpipe. Actual vertex-readback error: float **1.274202e-5**, half **1.095919e-6**. Production-material image: **24,807 occupied pixels**, **0 mismatches** beyond the one-pixel edge band. Warmed CPU/GPU Prepare allocation gates passed.
- Added two further actual-demo tests: GPU uses `Graphics.RenderMeshPrimitives`; forced CPU independently uses `Graphics.DrawMesh` without requiring GPU support. Each captures 64 actors, per-actor torso regions, both facings, independent phases, visible IK change, disabled clearing, and enable/recreate. **These two tests remain pending** until the next coordinated run; the first seven tests use a deterministic CommandBuffer draw and do not alone prove the separate production draw API.
- Physical iOS Metal / Android Vulkan / GLES fallback correctness, performance, thermal and battery validation: **not run**

Do not run the graphics suite with `-nographics`. Mesa llvmpipe can establish correctness only; it cannot establish mobile GPU throughput, power or thermals. Do not claim zero managed allocations from code inspection or a .NET stub run. Update this evidence section after the final integrated Unity run, recording backend, maximum numeric error, pixel differences and exact passed/skipped counts.
