# Bounded blade grip close-up diagnostic

This adds visual evidence for reviewing blade/hand alignment, shaking and recovery. It does not replace the existing numeric weapon/movement fixtures or the existing normal-camera 1× recordings. No runtime, art, workflow or existing capture file is changed.

## Native cases and opt-in

`BrawlerFoundation.Tests.PlayMode.BwBladeGripCloseupCaptureTests.PreparedBladeGripSurvivesRepeatTurnEquipCancelAndReentry` has exactly two parameter cases: `GpuDriven` and `DataTexture`. Both require `SPF_WEAPON_GAMEPLAY_SEQUENCE=1`. Without the opt-in, this additional diagnostic is explicitly ignored; existing ordinary weapon acceptance remains independent. A real graphics device is required. GPU-driven additionally requires compute support.

The native acquisition uses the project's Built-in render pipeline. It fails clearly if a different pipeline is active, rather than writing stale frames. Native execution and visual review have **not** been performed for this new fixture in the local editing environment.

## What is recorded

- One 256×256 sequence per tier, 128 acquired source frames each. The raw buffer is exactly 33,554,432 bytes (32 MiB), used by one case at a time. Total saved image count is 256; output is lossless PNG. Encoded bytes are content-dependent and must be reported from actual native output.
- Automatic simulation, normal `Time.timeScale=1`, and public scripted input drive repeated blade attacks, right-to-left aim/movement reversal, an actual public `RequestEquip(Sword)` cancellation, and `RequestEquip(Blade)` reentry with repeated attacks. A stationary variant-zero enemy in another lane prevents wave transitions. Scene setup occurs before acquisition; no per-frame actor, pose, velocity or timeline state is authored.
- A separate diagnostic camera follows the live hero's rendered root at fixed orthographic size 2.2. This is explicitly an isolated close view, **not** a same-camera full-gameplay comparison. The original normal camera stays enabled and retains its target, transform, aspect, size and mask. Existing normal-camera fixture source and output names stay unchanged.
- After the normal camera finishes its ordinary render, the observer copies all 19 actual prepared hero records unchanged, using the exact stable handle and sorted actor offset. A prewarmed test-only `SpriteBatch` uses the presenter's borrowed atlas, original backend/shader, and matching depth/alpha settings. Only this batch is drawn on the diagnostic camera's layer. No trail, particle, other actor, terrain or HUD batch can cover the joint there. No extra `RenderFrame`, `Begin`, `Evaluate`, `Submit`, pose update or simulation `Step` occurs during acquisition.
- Positive prepared torso, blade and primary-hand presence, complete actor framing, original 19-record budget, exact source-record preservation, and unchanged authoritative snapshots are checked. Actual pixel presence is checked against an empty-camera negative control. Intended equip fade remains visible; source records are never brightened or retouched.
- Every sample preserves the Unity frame number, post-normal-render observation time, actual acquisition time, automatic simulation time/tick, dropped ticks, input, authority stage/timeline/pulse/equipment, skill clock, source record offset/count, hand/grip/tip and both cameras. No image encoding or file writes occur until capture ends. No interpolation, synthesized frames or asserted 60 fps cadence is used. Target acquisition is at most 30 samples/s; synchronous render/readback/snapshot checks can reduce actual cadence. This is not a device performance benchmark.

## Exact output paths

Under the Unity project root:

- `Artifacts/Screenshots/WeaponMotion/blade-grip-isolated-gpu/`
- `Artifacts/Screenshots/WeaponMotion/blade-grip-isolated-fallback/`

Each contains `frame-000.png` through `frame-127.png`, `acquisition.csv`, `acquisition.ffconcat`, `capture.json`, `README.txt`, `authority-and-camera.csv` and `scenario.txt`. The existing `BufferedFrameCapture` ffconcat preserves measured intervals and explicitly repeats only the final image to preserve its display duration. Use that timing for 1× review, never relabel the source 60 fps. Acquisition has a 30-second failure timeout if rendering stalls. Camera callbacks and owned buffers/materials/targets are released on success or failure; the borrowed presenter atlas and NativeArrays retain their original owner.

## Coverage limits and review

Review every frame around active/contact, follow-through, aim reversal, equipment cancellation and blade reentry. Judge the isolated grip together with the unchanged normal-camera gameplay clips. Numeric alignment alone cannot establish natural movement or lack of visible shaking.

This fixture does not exercise simultaneous weapon/skill overlap, hit/death, pause or arbitrary cancellation causes. Its skill clock trace must stay inactive. It does not force a skill state to imitate an unsupported combination. Existing ordinary fixtures remain responsible for their wider numeric/lifecycle scope. Android/iOS device performance and visual acceptance remain external gates.

## Local source/API checks

The focused Brawler PlayMode dependency graph was compiled against the current repository source and current engine stubs; no full aggregate or native runner was invoked. A separate C# 9 conditional-source audit compiles this unmodified native-only fixture and the actual unchanged `BufferedFrameCapture.cs`, with `UNITY_EDITOR`, `UNITY_INCLUDE_TESTS`, `UNITY_2022_3_OR_NEWER`, and **without** `SPF_DOTNET_HARNESS`.

That audit uses official `com.unity.ext.nunit` 1.0.6 (NUnit 3.5.0.0), official Unity Test Framework 1.1.33 `UnityTestAttribute`/combinatorial attribute sources, and the asmdef's direct references. Engine/Collections/Jobs/Burst/UI APIs remain stubs on .NET 8; scratch-only signature additions cover post-render callbacks, double realtime, camera copying/projection, material getters and unchanged readback helper APIs. These additions are not committed and do not simulate native behavior. The known host-NUnit-reference MSB3277 warning is disclosed; the explicit official NUnit DLL is selected and its copied SHA256 is checked. These are compile/API-domain checks, not Unity/Burst/shader/GPU execution or visual acceptance.

The callback and disabled-camera rendering pattern follow the official Unity 2022.3 [Camera.onPostRender](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Camera-onPostRender.html) and [Camera.Render](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Camera.Render.html) contracts. It renders only the separate camera from the callback; it never recursively renders the camera currently rendering.
