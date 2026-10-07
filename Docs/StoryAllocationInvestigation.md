# Story typewriter allocation investigation

## Current evidence

The native macOS Unity run for remote commit `b873034` (local equivalent `f52fb53`), run `37563011844`, failed `StPlayTests.TapThroughChooseSwitchLanguageUndoAndSave`: 3 allocating frames versus the existing maximum of 1. This is a whole-frame allocation counter, not three garbage collections. The available job log does not contain per-frame byte counts or allocation stacks. No source attribution or performance fix is established by that failure alone.

The fixture warms the first narration line, advances to Mira's second line, waits for the runner's Choice state and two frames, then measures at most 30 frames while the dialogue is typing. All normal scene and UI work remains active. `DialogueBox.Refresh` changes `BufferText.MaxVisible`; when typing finishes, that same method presents choice buttons. A slow Editor frame can therefore bring first-choice presentation into the original window. This is a hypothesis to inspect, not grounds to exclude that frame or relax the budget.

Source inspection also leaves font/Canvas work, late line-snapshot work, render-policy changes, other frame activity and the test framework as candidates. `BufferText` requests/layouts the complete text before revealing glyphs. Its first-line buffers and shared mesh capacity are warmed, but this alone does not prove every deferred or font-related path is allocation-free.

## Capture the actual fixture

Set `SPF_STORY_GC_CAPTURE=1` and run the existing PlayMode test:

`StoryFoundation.Tests.PlayMode.StPlayTests.TapThroughChooseSwitchLanguageUndoAndSave`

The test keeps its first-line warmup, two settling frames, maximum 30-frame window and `allocatingFrames <= 1` assertion. The diagnostic is inactive by default. It does not mute audio, hide UI, change the story, alter frame pacing, disable quality adaptation, subtract samples or add an event exclusion window.

Setup and one observer warmup happen before the original measurement. Each measured frame uses a fixed profiler marker and preallocated metadata/snapshot buffers. At the original end boundary, the fixture saves the governor result before three diagnostic-only drain frames let the final profiler frame arrive. Export then runs before the original assertion so evidence survives a budget failure. The saved result, not the governor's later value, is asserted. Profiler settings and the persistent metadata allocation are restored/disposed in the test iterator's `finally` path.

Files appear under `Artifacts/GC/story-<UTC timestamp>/second-line`:

- `.raw`: native Unity profiler capture, also retaining nearby warmup/drain frames for inspection
- `-frames.csv`: exact metadata mapping, real time, reveal length/visibility, Typing, choices, body mesh rebuilds, sprite count, quality level, start/end governor values and generation counts
- `-allocations.csv`: each GC.Alloc sample on every recorded thread in the stamped frames, including byte amount, sample ancestry, callstack addresses, resolved symbols, missing stacks and observer classification
- `-stacks.csv`: grouped allocation totals without removing observer/test-runner/UI/Editor samples
- `-summary.txt`: mapping completeness, original governor totals, captured allocation totals and process-wide collection deltas
- `-settings.txt`: environment, selected profiler settings and interpretation limits

Metadata maps the actual `Time.frameCount` to profiler frames, with missing/duplicate mappings and frames containing multiple distinct ordinals rejected. The end snapshot allows the final measured iteration's reveal/choice transition and governor update to be inspected. Zero observed frames is an incomplete diagnostic, even if the pre-existing budget would otherwise trivially pass.

## Interpretation limits and next step

CPU and Memory profiler areas are enabled; allocation callstacks are on, deep profiling is off, history is 300 frames and stream memory is bounded to 64 MiB. `SPF_STORY_GC_INCLUDE_EDITOR=1` additionally enables EditorLoop profiling when specifically needed. The default PlayerLoop capture cannot explain unrecorded EditorLoop work merely by elimination. The governor's previous-frame counter snapshots and coroutine-boundary collection deltas are labelled separately from complete stamped profiler-frame allocation totals; no guessed frame offset makes them equivalent.

Callstack capture is intrusive. A diagnostic run can change timing and measured allocation counts; it is evidence for attribution, not a substitute for the normal uninstrumented budget test. Observer-scope allocations remain in exported totals. Unresolved addresses are retained, not declared engine-owned. Native source lines may identify method definitions rather than the exact allocation statement.

Inspect allocating-frame stacks together with Typing/Choices transitions and body rebuild/quality changes. Fix a confirmed application source, then validate the same uninstrumented fixture with its original budget and normal presentation. If the stacks only demonstrate external one-time work, report that evidence and its limits; do not invent a production optimization or treat a passing rerun as attribution.
