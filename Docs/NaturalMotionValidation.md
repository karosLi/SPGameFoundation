# Natural motion and bounded silhouette shadows

## Run the showcase

Unity 2022.3: **SPF → Characters → Create Natural Motion Showcase**, then Play.
The menu creates `Assets/SinglePlayerFoundation/MotionValidation/Scenes/NaturalMotion.unity`.
The scene is deliberately composed for landscape. The camera fits its entire 18 × 10.2 world-unit envelope at any aspect; fixed landscape labels and controls use the same centered fit region. Portrait is contained and intentionally letterboxed, not a redesigned portrait UX. It never changes global PlayerSettings or another game's orientation.

- Two original articulated characters: an armored teal/gold explorer and a horned coral/moss creature. Art is code-authored, 3× supersampled, straight-alpha/bilinear, with explicit back-to-front parts.
- Left pair: anchored stance, smooth lifted swing, small pelvis/head motion. Green ground marks mean stance; amber means swing. Ground ticks make sliding easy to inspect.
- Right pair: eased anticipation, strike/contact, and recovery; gold requested targets and green solved tips. Their sparks are a bounded presentation cue, not evidence of a gameplay hit.
- Upper library: four smaller fixed-contact poses with 15 Hz secondary-motion evaluation and genuine sampled silhouette shadows. Dynamic lower actors intentionally use contact blobs.
- Buttons and keys: Pause clock / Space, Shadow / Q, Light / L, Reach / U, Tier / T. Tap-drag inside the lower stage adjusts the aiming targets, including while the gait clock is paused. Reach switches between reachable and deliberately unreachable requests.

This is **CPU/Burst cutout animation**, drawn through the existing indirect-sprite or data-texture-sprite backend. It is not GPU skinning and does not extend or replace the separate, independently validated three-bone weighted BAT ABI or scene.

## Reusable motion contracts

`Presentation/Animation/NaturalMotion.cs` provides blittable foot and aim state, exact exponential held-target smoothing, smoothstep-five swing interpolation, world/model conversion, aspect fit, and a canonical straight-line gait sampler. `NaturalCharacterRig` defines the explicit 14-bone demo rig and composes secondary motion before existing `Skeleton2D` two-bone IK and FK. `NaturalPoseJob` evaluates and packs fixed output ranges in Burst.

- Feet store a **world-space plant** during stance (62% of a 0.92-second cycle). Only lift-off chooses a bounded predicted landing; a moving target is not chased during swing. Event-root interpolation reduces update-rate dependence. The swing arc has zero height velocity at lift/contact.
- Call `InitializeFoot` after teleporting or discontinuously changing the ground/path. Ground height is a caller-provided contact sample. This bounded controller is not a terrain raycaster, obstacle planner, root-motion controller, locomotion state machine, or general rig importer.
- `StepFoot` caps one call at 0.25 seconds; the demo caps presentation updates at 0.1 seconds. Long pauses intentionally do not fast-forward the character. Typical 30/60/120 Hz updates are numerically checked.
- Aim receives a world-space target, presentation root/facing/scale, and explicit bend sign. It converts into the existing solver's unmirrored frame; reach limits remain the solver's responsibility. Smoothing changes only visual target state, never hitboxes or simulation input.
- Both sides mirror through existing `BoneWorld` semantics. Feet are solved after pelvis bob and then held level. Torso/head offsets remain small so secondary motion does not create a rubber-body effect.
- Warmed math calls contain no allocations; fixed native pose/output arrays and actor states are allocated at initialization. Startup, quality-tier resource recreation, atlas baking, and UI status changes intentionally allocate.

## Silhouette baker and honest fallback

`PoseSilhouetteShadow` rasterizes the **actual alpha masks of transformed cutout parts** into a fixed-light flat-ground projection. It is neither a shadow map nor a capsule stand-in. The fixed projection is `(x + 0.42y, -0.24y)`; it supports neither moving lights nor occlusion onto walls/other actors.

The explicit library is 2 kinds × 2 facings × 8 canonical phases = **32 samples**. Each mask is 96 × 64 with 2×2 coverage sampling and a fixed 3×3 loading-time feather. Four internal gutter texels plus two atlas padding texels prevent clipped/bleeding edges. Per-frame bounds are derived from every transformed attachment quad corner, including horns and feet.

Selection requires matching kind/facing and checks every current attachment corner against the frozen sampled geometry. Maximum deviation is **0.12 model units**; this is a declared approximate pose envelope, not an exact dynamic shadow. Larger deviations, arbitrary dynamic IK, or missing samples **must fall back to Blob**. The demo proactively marks all four stateful gait/arm-IK actors dynamic, so they never claim a matching baked silhouette. Quality `Blob` skips sample selection; quality `None` submits no shadows. No quality option mutates gameplay, clock authority, RNG, saved state, or a source rig/clip.

The baker validates finite positive part geometry and bounds sample/part/bone counts. Bake/load allocates; selection and appending sprites do not. It retains its own immutable sample geometry, so subsequent caller edits cannot invalidate the envelope.

## Explicit budget

- Exactly **8 actors × 14 bones and 14 parts = 112 character sprites**. There is one bootstrap, one camera, and a fixed UI; no per-actor GameObjects or Animator components.
- Four foreground poses update at presentation rate. Four background poses are actually skipped until their 15 Hz pose tick changes, reusing their previously packed sprites. Their complete sprite buffer is still uploaded each frame; this is CPU pose LOD, not a sparse-upload claim.
- Fixed capacities: 112 actor parts, 128 stage sprites, 8 silhouette shadows, 8 blobs, 64 effect slots. At most ten strike effect sprites plus ten diagnostic sprites are used in the current composition.
- Color atlas: 1024 × 256 RGBA32 = **1 MiB**. Optional normal atlas: same size = **1 MiB**. Pose shadow atlas: 512 × 512 RGBA32 = **1 MiB**. No mipmaps. These exclude UI font, engine overhead and geometry/buffers. Color and silhouette atlases also retain **2 MiB of readable CPU texture data** in total; the optional normal atlas releases its CPU texture copy. The 16 source part canvases retain **589,824 bytes (0.5625 MiB)** of managed RGBA pixels for reuse/baking. Load-time supersampling/baking scratch and engine/driver allocation overhead are additional, and have not been peak-memory profiled on a device.
- Actor packed upload is **112 × 32 = 3,584 bytes** plus indirect arguments on the indirect tier; the data-texture actor page is 1,024 RGBA8 texels = **4,096 bytes**. Stage instances are reused without uploads. Shadows/effects have separate small uploads.
- Two normal-map lights maximum in this scene; they can be disabled. No full-screen post-processing, runtime blur, shadow render targets, or per-actor lights.

## Validation and evidence

New focused tests:

- `SPF.Tests.EditMode.NaturalMotionTests`
- `SPF.Tests.EditMode.PoseSilhouetteShadowTests`
- `SPF.Motion.Tests.PlayMode.NaturalMotionGraphicsTests`

At the initial implementation checkpoint:

- Focused .NET reference suite: **28/28 passed, zero skipped**. Coverage: exact stance anchors; 30/60/120 Hz landing/phase parity; seam/contact position and velocity continuity; swing clearance; smoothed target parity; both facing/bend signs; near/far unreachable targets; exact planted FK tips; zero managed allocations in warmed math; landscape/portrait/wide fit; antialiased original art; shadow coverage, padding, bounds, source independence, quality fallback and the 1 MiB maximum.
- All **74 harness assemblies** compile successfully with **zero warnings / zero errors**.
- Compile-only check against installed Unity 2022.3.62f2 assemblies (including real runtime/demo/editor/graphics-test APIs): **passed, zero code errors**. This is not a Unity run and does not establish Burst/render correctness.
- Central Unity 2022.3.62f2 run: **28/28 EditMode and 2/2 PlayMode passed, zero skipped**. Both tests render the actual `NaturalMotionDemo`, not a separate CommandBuffer fixture. Six real time-separated frames per tier plus unreachable-target, low-quality, recreate and portrait images are in `Artifacts/NaturalMotion/`. Both tiers were visually inspected: articulation, distinct original silhouettes, contact/strike poses, supported silhouette shadows, and reach clamping are visible.
- Inspection identified a thumbnail-caption overlap and a ground-tick-caption overlap; the follow-on patch moves the labels and aligns portrait UI with the fitted stage. A small overlapping neck avoids a cutout seam under head counter-rotation; explicit authored sole offsets align visible boot contact with the platforms while preserving the solver's ankle anchors. **Those visual refinements and the optional video capture require a fresh central run.**
- Physical Android/iOS performance, heat, battery and driver verification: **not run**. Desktop software graphics can establish correctness only.

### Optional real animation sequence

Set `SPF_MOTION_CAPTURE_SEQUENCE=1` only for an evidence run of `NaturalMotion_DataTexture_SequenceAndFallback`. It captures **30 actual rendered PNGs at 10 fps for 3 seconds**, including a facing reversal, from the same demo. Filenames: `motion-datatex-sequence-000.png` through `029.png`. The default regression remains six frames per tier. The capture freezes paused input smoothing and deterministically steps the presentation state to each time; no generated, interpolated or duplicated fake frames are used. Camera readback, assertion pixel copies, PNG encoding and filenames deliberately allocate only in this test path and must never be mixed into a performance window.

Example local encoding (requires ffmpeg):

```sh
ffmpeg -y -framerate 10 -i Artifacts/NaturalMotion/motion-datatex-sequence-%03d.png \
  -c:v libx264 -pix_fmt yuv420p -movflags +faststart Artifacts/NaturalMotion/motion-datatex-3s.mp4
```

The existing weighted BAT evidence remains separate and unchanged.
