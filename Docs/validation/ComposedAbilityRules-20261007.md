# Stage E exact-scope validation — 2026-10-07

Implementation/design: [Composed ability rules](../ComposedAbilityRules.md). This is a local software/source checkpoint. Native Unity/Burst/graphics execution, actual new screenshots/clips, frame-by-frame 1× visual review and physical Android/iOS acceptance are **pending**. Preparing or API-compiling a fixture is not native execution.

## Source and scope

- Starting gameplay/core checkpoint: `40a4323`.
- Frozen Stage D dependency: original `15c10ff` + `56a13de` (equivalent local adopted commits `2b56e49` + `e1fdbaf`). No Stage D public envelope/core/schema API is changed by E.
- Stage E changes only its new admission helper, two game-local opt-in rule states/factories/HUD wiring, complete distinct save recipes, new tests, narrow cold canonical-writer extraction, capture flag forwarding and related docs.
- Existing `BwWeaponGameplayTests` / `SvWeaponGameplayTests` are not edited here; Stage F owns those independent lifecycle captures. Existing default rules/weapon families/raw fixtures and capacities remain controls.
- Final changed-source manifest and log hashes are in [the machine-readable record](ComposedAbilityRules-20261007.json). All 31 changed code/workflow files were unchanged throughout the final run; digest `0c251673aa7b22f2400432cf143cc379c8e808bbe9146c92e8e7bac5182bd9f8`. Documentation-only record updates do not change these tested sources.

## Verification ledger

| Check | Verified status |
| --- | --- |
| Initial aggregate .NET | 1,300 passed, zero failed, 11 EditMode assemblies; no PlayMode execution in the harness |
| Initial real Unity API compile | net8.0 and netstandard2.1, zero errors, seven existing warnings; compile only |
| Initial cold composition inventory | 19 entries unchanged against recorded inventory; repeated output byte-identical; SHA-256 `a42c9f27d2bb8d277303eef24e5b4cdbd58f0035d8bca89c7e08089348dd5f30` |
| Initial independent old-D byte control | Brawler and Survivor before/after full envelope files and descriptor fingerprint output compare byte-for-byte |
| CI Python utilities | 30 passed, unchanged evidence packaging/encoder tests |
| Final exact-source .NET/API/cold/byte-control rerun | **1,304 .NET passes, zero failures**; both real API targets zero errors/seven existing warnings; cold 19 unchanged/repeat byte-identical; independent old-D bytes identical; all 30 CI utility tests pass |
| Independent runtime/native-fixture review | Source/API-level review: no blocking findings. It explicitly does not claim a rendered result |
| Independent save review | Ordered coverage/content/visual/raw identity and frozen old writer sequences reviewed. One mutable history-capacity guard gap was corrected with two regressions; no remaining blockers |
| Native new EditMode / graphics | Not executed for E yet; CI runner unavailable at parent checkpoint |
| New sixteen stills / four clips / 420 frames | Fixtures and CI flags prepared; files and actual stored sizes/timestamps not yet acquired |
| Physical Android/iOS | Not run; touch, backend fidelity, heat/battery, sustained frame time/native memory remain external gates |

## Preserved before/after writer control

[AbilityLegacyByteProbe.cs](AbilityLegacyByteProbe.cs) was first compiled against the independent unchanged D `56a13de` harness assemblies. It creates the ordinary Brawler weapon belt and ordinary Survivor weapon example with seed 7, sends Start, executes 13 completed ticks, uses runtime domain `stage-e-byte-control`, and writes full envelope bytes plus every descriptor fingerprint. The candidate executable is independently rebuilt against E's assemblies and its output files are compared directly with the retained D files. The new `*WeaponSaveWriterCompatibilityTests` also contain frozen D fingerprint and envelope SHA-256 values; both sides do not call a newly extracted helper.

- Brawler full envelope SHA-256: `65e420610391875f34ed6fdeeb5d0df4204ad669aa804e816f5744dece759579`
- Survivor full envelope SHA-256: `11663a75e3a0f4e5081c5fab175ffe1b053a7aa349ebc4ebdb9b69c629050b77`

These controls are explicitly .NET same-runtime/ABI checks. They do not demand that native Unity emits the same raw bytes as the .NET stub harness. Native tests instead compare restore/replay against their uninterrupted control on that same backend.

## Commands and expected evidence

- Full logic/layer suite: `Tools/DotnetHarness/run.sh` with .NET 8 SDK; the final local toolchain is 8.0.425. It compiles every asmdef using project references, then runs all EditMode logic/performance tests.
- Real API check: `Docs/validation/GenerateCompositionApiCompile.py --repo <checkout> --unity-data <Unity 2022.3.62f2/Editor/Data> --nunit <nunit.framework.dll> --out <outside-output> --all-assets`, followed by C#9 builds for net8.0 and netstandard2.1. Native guards are enabled; this is an API/type check only.
- Cold compatibility: build/run `FoundationCompatibilityProbe.cs`, compare all 19 entries with `FoundationCompatibilityInventory-20261007.json`, then repeat for byte identity.
- Native suites: `SPF_ABILITY_GAMEPLAY_SEQUENCE=1 Tools/ci/local-unity-tests.sh` with licensed Unity 2022.3.62f2 and a graphics-capable display. Both native workflow environments now enable the flag; the Docker wrapper forwards it. This E change preserves all existing capture flags/thresholds.
- New native filters: `SurvivorFoundation.Tests.PlayMode.SvComposedPulseGameplayTests` and `BrawlerFoundation.Tests.PlayMode.BwComposedAbilityGameplayTests` (four parameterized cases each).
- Still prefixes and sequence names are listed in [the design](../ComposedAbilityRules.md#acceptance-and-reproducibility). All new continuous captures use actual automatic-clock gameplay, actual shared HUD intents and existing art; `ability.csv` records authority alongside `acquisition.csv`.
- Encode each recovered capture after acquisition with `Tools/ci/encode_capture.py CAPTURE_DIRECTORY NEW_OUTPUT.mp4`. Keep its JSON report/hashes and exact measured microsecond VFR PTS. Inspect 1× continuously and at contact/settlement transitions; do not infer smoothness from a point capture or manufacture intermediate frames.
- Raw capture memory remains below existing 128 MiB per-buffer limit; planned extra stored evidence allowance 64 MiB. Exact new bytes, cadence, four video hashes and 1× review outcomes remain pending. Existing 16 MiB/max 32 artifact part contract remains enforced with no drops/threshold relaxation.

## Findings and corrections preserved

1. The initial new pause test incorrectly called the deliberate replay/test `SimSession.Step()` bypass while paused. Source inspection confirmed Step ignores the clock by contract; the test now uses `Session.Update`, proving pause is a true no-op. No Session behavior changed.
2. Survivor cancellation based only on start HP could miss an accepted injury after a level-up raised HP. The existing authoritative RewardSystem now cancels the optional pulse when it accepts hero damage. Its old HP/invulnerability/death arithmetic is unchanged; a real `HeroDamage` queue regression covers the case.
3. Save review found that a manually replaced/disposed public pulse history could leave the authored capacity identity unchanged. The new recipe now rejects an uncreated or wrong-length history before capture/restore preflight, with two tests. The normal factory was already fixed-capacity.

The user-visible completion statement must retain the native/video/device pending gates until exact-source evidence is actually recovered and reviewed.

Both new active gameplay paths retained the existing zero-current-thread-allocation threshold: Survivor 240 measured ticks and Brawler 840 measured ticks after full-path warm-up each report 0 B, with retained-array/empty calibration 33536/0 before and after. These are calibrated harness observations, not native/render/all-thread/mobile claims.
