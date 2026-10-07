# Deterministic lifecycle probe clock

2026-10-07. Exact native b75d423 attempt2 produced 1,164 EditMode passes, two failures and five skips; all154 graphics PlayMode cases passed with one explicit diagnostic skipped. The two failures were BwViewLifecycleTests.SameTickRestoreClearsOldTransientFeedback(false/true), at the **pre-restore positive control**, not the restore assertion. See [exact run](https://github.com/karosLi/SPGameFoundation/actions/runs/37600770862). Counts come from individually hash-verified XML; full archive reconciliation is separately retained.

## Diagnosis and bounded fix

Brawler drains a real Hit into0.25s Puff/0.12s flare, then immediately ages them using Unity Time.deltaTime. A sufficiently large native EditMode delta expires them before Active is counted. The .NET stub supplies1/60s. The native run did not log deltaTime, so the specific elapsed value/cause is a source-supported explanation, not a measured retrospective value. Survivor's analogous0.6s effect also needed a survival control to prevent expiry from masking broken clearing.

Both real renderers now have an explicit visual-timestep overload for synchronous probes. The existing no-argument RenderFrame and LateUpdate continue passing Unity's **unmodified** delta. The argument flows to the same FX, character and particle update paths with the same pause/flow gates; it never advances simulation. Invalid explicit values are rejected. No clip lifetime, runtime delta clamp, capacity, threshold or allocation budget changed.

The four same-tick probes use zero visual time, require Active>0, require it remain>0 on another unchanged-timeline render, restore the same numeric tick, then require Active==0 and exact simulation snapshot bytes. Four additional large-step cases verify that a1s visual update still expires these real effects without a restore; this preserves normal expiry instead of hiding it. The probes log ambient delta for the next native run.

## Counterexample and verification

- Fixed source:26/26 focused lifecycle cases pass (Bw12/Sv14).
- Mutation: omit **only** the two timeline-driven ClearTransientState calls. All four same-tick cases fail at the post-restore zero assertion, retaining two Brawler effects/one Survivor effect. Positive and no-restore survival controls had passed. This shows expiry cannot make a broken reset pass.
- Restore: fixed renderer bytes match their pre-mutation SHA-256; rebuild and rerun again pass26/26. The mutant is not committed.
- Independent source review confirms the same delta consumers/flow gates and unchanged production lifetimes. Full final integration and exact-head native execution remain separate gates; this record does not turn the failed old native run green.

[Machine record and log hashes](LifecycleProbeClock-20261007.json). Reproduce through the ordinary harness Brawler/Survivor EditMode projects with FullyQualifiedName~ViewLifecycleTests. Mutation control uses the same tests and removes only the named clearing calls temporarily, then restores the exact fixed source and rebuilds.
