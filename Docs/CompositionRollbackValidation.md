# Stage B: bounded composition rollback

This checkpoint implements the first failure boundary from [the semantic extension plan](SharedFoundationSemanticExtensionPlan.md), against the reviewed `9bc7c41` compatibility baseline. It does not implement manifests, capability graphs, automatic ordering, hot unload, or a new save format.

## Ownership and failure contract

- Session settings are checked before module callbacks or native allocation. Runtime validation accepts every positive TickRate/MaxTicksPerFrame and every nonnegative destroy-queue capacity, including zero. Inspector ranges are not new runtime limits. Defaults remain 30 / 3 / 4096.
- A successful `WorldLayout.Resource` registration transfers that object to the temporary layout. A rejected value remains the caller's responsibility. Scope metadata is not changed by a rejected duplicate. Modules must release any allocation they have not yet registered, including partial resource constructors; borrowed modules/configuration assets are not destroyed by composition.
- A layout owns its accepted resources until `SimWorld` completes. World construction cleans already allocated tables/columns, registry and implicit queue on failure, then cleans the temporary layout. Successful construction transfers ownership once; disposing that layout afterward is harmless. An owned-resource layout cannot be reused to share state across worlds. A resource-free, table-only layout remains reusable and allocates independent table storage, preserving the prior direct-builder contract.
- Successful system `OnCreate` calls are recorded in sorted execution order. A later failure destroys only that completed prefix, in reverse order, then the Session releases its World. RegisterSystems/Declare must not acquire untracked native resources. A failing OnCreate self-cleans its partial allocations and completes any private work; the pipeline does not blindly invoke its normal OnDestroy. A separately constructed pipeline borrows its World and never disposes it. It cannot reverse arbitrary state mutations performed by an initializer; it guarantees cleanup of completed systems only. Fully constructed SimSession is the owned atomic publication boundary, not a transaction over arbitrary writes to a caller-owned World.
- Normal teardown retains reverse system order followed by registration-order resource disposal and storage release. Aliases of the same resource object are disposed once. Each owner gets one cleanup attempt even if a preceding owner throws. Repeated teardown is harmless; failed job completion leaves ownership retryable rather than releasing possibly in-use storage.
- The original exception object and stack remain primary. Additional cleanup exceptions are retained as an AggregateException in `Exception.Data[CleanupErrors.DataKey]` (`SPF.CleanupFailures`). An external disposer that throws may still retain its own private allocation; the framework can attempt all owners, not guarantee recovery inside arbitrary user code.
- EndTick distinguishes completed jobs from a failed OnSync notification. Disposal still releases completed work after a notification failure. New ticks during cleanup are rejected, and a direct Pipeline teardown cannot prematurely release World by reentering Session.Dispose.

OnCreate is synchronous: it must complete initializer-private JobHandles before returning or throwing, before rollback invokes another system's OnDestroy. There is no initializer job-registration API in the current World/Pipeline contract. ISyncResource.OnSync is explicitly an after-job-completion notification, not a JobHandle ownership registry; calling it cannot substitute for completing private work. During ordinary disposal, pipeline-tracked OnTick handles are completed before system cleanup and World storage release. Unreturned handles or untracked work cannot be inferred or made safe by this checkpoint.

No successful phase/order/registration ordering, table/column order, capacity merge policy, seeded behavior, native data layout, or snapshot-writing code is changed.

## Red-first evidence and regression coverage

`CompositionRollbackTests` was first run against unchanged `9bc7c41` production sources: **15 failures / 6 passes**. The failing cases showed accepted declaration resources leaking, rejected-resource ownership paths leaking the accepted predecessor, a partially allocated table surviving a later column factory failure, middle/last initializer cleanup missing, settings failing only after allocation, and teardown stopping or masking the cause.

The first fix passed all 21. Adversarial expansion then reproduced another failure: OnSync throwing during disposal abandoned an already completed World (**1 failure / 29 passes**). The final reviewed focused suite currently passes **35/35**, covering first/middle/last initialization failure, self-cleaning partial initializer, cleanup failures, direct borrowed-world ownership, resource aliases, scope rejection, repeated failed-then-successful construction, unchanged successful initialization order, normal disposal, reentrant scheduling and upward owner disposal.

Column failure injection adds a throwing factory after a real small NativeArray column allocation through cold test reflection. It does not allocate huge arrays or claim an actual operating-system/native allocator exhaustion test. Core multi-allocation constructors and managed ownership-list insertion failures are exception-safe by code review; allocator OOM injection is not executed.

Validation so far:

- All 76 generated .NET harness projects build with zero warnings/errors. Full regression is in progress and will be recorded for the final checkpoint; earlier focused success is not a full pass.
- Real Unity API compile includes the new test and changed core: 213 sources / 96 real Unity/package references. Four pre-existing serialized-field warnings, zero errors. This checks C#9/API compilation, not native execution.
- The retained cold probe runs all 19 compositions twice. Both outputs are byte-identical to the Stage A output, SHA-256 `a42c9f27d2bb8d277303eef24e5b4cdbd58f0035d8bca89c7e08089348dd5f30`. All composition fields and 65 named existing fixture methods match. Changed source hashes are recorded separately; the Stage A inventory is not overwritten.
- Native Unity EditMode/graphics PlayMode and physical Android/iOS remain separate validation gates. No old test or budget was weakened.

## Explicit remaining scope

A separate follow-on now makes the five reviewed multi-array OnCreate implementations exception-safe: Snake Population/Resolve/BodyGrid, RPG Combat and BeltCombat. Each uses its own private buffer cleanup on failure, attempts every acquired buffer, and preserves the original cause. BodyGrid registers its managed change log only after native buffers succeed. Two real Resolve initializer cases omit Prop or Snake after earlier small NativeArrays were allocated: **2 red before the fix, 2 green after**, including a successful retry of the same system. The other native allocation-failure branches are code-reviewed, not claimed as allocator fault-injection tests. Arbitrary module/resource constructors retain their own partial-allocation obligation.

Existing BeginTick *scheduling-failure recovery* is outside this construction checkpoint: a thrown recovery Complete/OnSync can mask the scheduling exception, and recovery-completion ownership needs a separate hardening test. This document does not claim all exceptional job-scheduling paths are fixed.

The [Stage A compatibility matrix](FoundationCompatibilityMatrix.md) remains a pinned description of that baseline, including its historical ownership exceptions. This document records the specifically implemented changes; other proposed Stage B checks remain proposals.
