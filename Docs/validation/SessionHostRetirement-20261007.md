# B/C integration: retained Host cleanup ownership

2026-10-07. This is a narrow follow-up to the Stage B rollback commits `40cec34`, `9ebdd9a`, `a332580` and Stage C Host/View checkpoints `fa706a4`, `7c5e821`. Retirement checkpoint: `d9b737b`; the additional publication-boundary cases below build on it. It changes SessionHost ownership and its tests plus one internal SimSession disposal-completion predicate. Public State timing/guards, World/Pipeline implementation, authoritative state and snapshot layout remain unchanged.

## Reproduction and fix

Stage B deliberately keeps a SimSession retryable when Pipeline cleanup cannot establish completion. A deterministic reproduction is direct `session.Pipeline.Dispose()` calling a system's OnDestroy hook, which reenters `host.Initialize(...)`. The Pipeline is still inside its cleanup hooks, so SimSession refuses to release World and throws. Stage C's original detach-before-dispose code correctly unpublished the Session but discarded its only Host-held reference.

The Host now has one bounded private retirement slot. It disables scheduling and removes the active/public Session first, but retains ownership until Dispose returns or throws and the internal completion predicate confirms both Disposed state and no active Dispose call. The next Initialize or OnDestroy retries this slot before creating another Session. Disable/enable neither erases it nor resumes its launcher. A separate reentry flag prevents cleanup hooks from creating replacements during an outer Host retirement, including resource hooks after the Session has already entered Disposed.

Safe cleanup errors remain errors: system/resource cleanup finishes, the completed retirement slot clears, and original diagnostics (including secondary cleanup failures) propagate. A subsequent independent Initialize can retry successfully. No global cleanup manager, background retry loop, new public owner or snapshot field is added.

## Red-first evidence

The retirement part of `SessionHostRetirementTests` adds 8 cases:

- 4 direct-child replacement cases across both overlap modes and later replacement/destroy retry: the unpublished Session remains Host-owned; remaining system teardown still observes an undisposed resource; replacement declarations/notifications remain zero until safe retry; each resource/system cleans up once.
- 1 direct-child OnDestroy reentry case: incomplete teardown retains ownership; repeated OnDestroy after child completion drains it once.
- 2 Host-owned teardown reentry cases (system hook and resource hook): overlapping replacement creation is rejected; cleanup completes and later retry succeeds.
- 1 existing-good diagnostic control: original system exception and secondary resource exception survive; completed cleanup clears the retry slot and replacement succeeds later.

Before the fix: **7 failed, 1 passed**. After the fix: the combined focused set (`SessionHostTests`, `SessionHostRetirementTests`, `CompositionRollbackTests`, `MobileInputTests`, `MobileSkillControlTests`) passes **101/101**. The 8 new cases are additional to the prior B/C suite totals. Full integrated regression and actual Unity execution belong to the publishing parent's combined checkpoint and are not substituted by this focused result.

## SessionCreated publication failure boundary

A second red-first pass reproduced 4/4 additional failures: throwing SessionCreated handlers (with and without a secondary cleanup exception) left the newly created Session published/ticking; a one-shot reentrant Initialize replaced the outer binding without rejecting it; a handler could dispose the Session and Initialize still returned that disposed binding.

Initialize now rejects reentry until its creation/binding attempt finishes. The newly created Session is published for binding callbacks, but any subsequent failure unpublishes it, stops scheduling and uses the same retirement slot to clean up safely. B's CleanupErrors keeps the original binding exception primary, including its stack, and attaches any secondary teardown diagnostics. After handlers return, Initialize also verifies that its binding was not removed or disposed. Removing the failing handler permits the next explicit Initialize to succeed.

This protects only objects owned by the Host. It does not undo arbitrary side effects or destroy partial assets allocated by a subscriber; each binding handler remains responsible for its own partially created view/resources. A successfully invoked earlier handler is not given a fictitious transactional rollback promise.

After these 4 additional cases, the combined focused set above is **105/105 passed** (12 new retirement/publication cases in total). No authoritative content/layout/snapshot changes are involved. Integrated full-suite and actual Unity results remain the publishing checkpoint's responsibility.

## External Session disposal completion predicate

A final focused review found that public Disposed state alone is insufficient: direct external SimSession.Dispose enters that state before World resource hooks finish, and recursive Dispose returns immediately. A resource hook calling Host.Initialize could therefore admit a new Session while old World cleanup was still active; a hook calling Host.OnDestroy could erase the last Host owner too early. Both variants reproduced red.

The only core addition is internal `SimSession.IsDisposalComplete`, defined as `m_State == SessionState.Disposed && !m_Disposing`. Host retirement uses it for both success and finally-clear. Public State semantics remain unchanged. The two regressions verify rejected replacement, retained ownership while external cleanup is active, and once-only safe replacement/destruction retry after the outer Dispose unwinds. They inject no native job completion failure.

Final focused result: **107/107 passed**, including 14 new Host retirement/publication cases. All 503 Assets C# files compile against real Unity APIs with 0 errors (7 existing unused/serialized-field warnings). Full aggregate and native execution remain pending the parent's exact integrated checkpoint.

All 503 current Assets C# files, including the new Host cases, compile against the installed Unity 2022.3 APIs. This is a compile-only gate, not a native execution result.

## Explicit engine boundary

The test uses a real, deterministically reentrant Pipeline cleanup hook. It does **not** inject or claim to recover a native JobHandle.Complete failure. If the engine cannot establish completion, the Host keeps the retired Session, leaves Session null, stops the launcher and propagates the error; it cannot safely force-free the World or admit a replacement. Initialize/OnDestroy can retry when completion becomes possible. If destruction is final and no usable Host reference/callback remains, this bounded design cannot guarantee a future retry or eventual resource recovery; the failure must stay visible to the application. There is intentionally no process-wide orphan cleanup mechanism or false claim that unsafe native work has been stopped.
