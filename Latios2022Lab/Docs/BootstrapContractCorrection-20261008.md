# Native cleanup failure and owned bootstrap correction

[Run 37761086135, attempt 2](https://github.com/karosLi/SPGameFoundation/actions/runs/37761086135/attempts/2), source `bda5bc72bf03d99d0b2ff96f6af1388130b165e3`, passed import and executed **49 EditMode/Burst-on cases: 45 passed, 4 failed**. All later test phases are **NOT_RUN**. [Original provenance](Validation/20261008-fifth-control/provenance.json) records the complete verified 65-file archive and native evidence. The change below only corrects our bootstrap contract; it does not repair or weaken the original Latios cleanup failure.

## Original control result

Attempt 1 failed on a DSPGraph connection timeout before compilation. One authorized retry used the identical source. Attempt 2 resolved the real original package graph, with lock SHA256 `0226f8bb362698bb4dfab670ca76a49f260b08d248721bfe086c34ba9a0365e7`, identical to the previously preserved complete lock. The incomplete attempt-1 lock remains separate failure evidence.

All four owned assemblies completed Csc, ILPostProcess and CopyFiles. All 11 C# and four asmdef hashes match the launch. Both phase snapshots retain all 12 actual compiler inputs, 138,336 bytes each. Import completed successfully, including inventory and teardown. Its 14 sorted managed systems contain the real initialization ECB and empty SceneSystemGroup. Both complete native logs have no old invalid-ordering warning or NoAllocReadOnlyCollection exception. Thus the inventory and ordering corrections now have native evidence.

All 36 Psyshock array/oracle combinations and the scheduled geometry/Burst-on query passed. Six Remove/DisposeWorld combinations, the two-owned-World case and the explicitly expected scheduling-exception case also passed. The query's BurstDiscard witness establishes actual Burst execution for that scheduled query; off-mode and standalone PlayMode remain unrun.

The four failures are DestroyEntity batches **1, 8, 64**, and the second PlayMode entry of the domain-reload test. Each reaches `CollectionProbe.cs:79`: **Destroyed collection owner retained a cleanup entity after the reactive update.** Reaching that line proves the preceding sum **37265** and disposal witness **[1,17]** checks passed before World teardown. The owner nevertheless still exists. The next reactive update and normal-path final assertions are not reached in these failing paths. The original evidence has no per-entity component dump; the exact remaining marker is inferred from pinned source, not measured as a component list.

The pinned [CollectionComponentOperations.cs](https://github.com/Dreaming381/Latios-Framework/blob/381a77dbf774ff603014d5695ef6c06abaa25d96/Core/Internal/CollectionComponentOperations.cs#L114-L176) defines addQuery as Exist without Cleanup and removeQuery as Cleanup without Exist. It removes/disposes storage for removeQuery and completes those jobs, then removes the cleanup component through **addQuery** at line 175. The destroyed owner matches removeQuery. This source-backed explanation fits the native failure. No upstream change is applied here; a potential one-argument correction needs a separate compatibility candidate and ledger, keeping this original failing control intact.

## Independent bootstrap violation

EditMode's original unity.log lines **586** and **698** report:

> ICustomBootstrap.Initialize() implementation failed to set World.DefaultGameObjectInjectionWorld, despite returning true (indicating the World has been properly initialized)

These are two PlayMode-entry assertions, separate from the four XML cleanup failure messages. Our old bootstrap returned true while leaving the default reference null. Returning false would select broad default system discovery and injection, which does not fit this Lab's isolated scope.

The corrected bootstrap creates exactly one **standard empty `World(defaultWorldName, WorldFlags.Game)`**, assigns it to `World.DefaultGameObjectInjectionWorld`, and then returns true. It does not create systems, install Latios modules, stream scenes or append this World to the player loop. Fixtures continue to create and dispose their own separate Latios worlds; the empty default World is not a fixture owner or an alternative cleanup path. Its real ECS storage, query and allocator costs are part of this correction, not a zero-allocation claim.

## Fixed Entities 1.3.5 lifecycle proof

These contracts were checked in the actual official registry package source:

| Source | Contract |
| --- | --- |
| `DefaultWorldInitialization.cs:121` | Registers unload/PlayMode shutdown before invoking the custom bootstrap. |
| `DefaultWorldInitialization.cs:134–140` | true requires a non-null default World and returns it immediately. |
| `DefaultWorldInitialization.cs:147–149` | Broad default-system injection/player-loop append occurs only after the custom branch, so our true return bypasses it. |
| `World.cs:208–243` | Constructor creates ECS backing state/query and registers World.All; it does not create systems. |
| `DefaultWorldInitializationProxy.cs:25–28` | Its active OnDisable invokes the registered shutdown. |
| `DefaultWorldInitialization.cs:80–106` | Shutdown removes loop registrations and calls World.DisposeAllWorlds, even for worlds not individually appended. |
| `World.cs:315–333` | Dispose removes World.All membership, clears the matching default reference, and releases backing state; DisposeAllWorlds processes all registered worlds. |

Normal automatic startup invokes this contract once per PlayMode entry. Official shutdown covers exit/domain reload and clears the reference before a fresh entry. No static field, manual event subscription, early Dispose, reusable cached World or speculative existing-world fallback is added. No defines or package choices change. Official initialization still registers its existing RuntimeApplication infrastructure callback; only this empty World has no player-loop entry. A lazily recreated Editor World after exit is legitimate, so exit validation must track the prior Game World rather than demand a permanently null default reference.

## Verification and boundaries

An independent source reviewer confirmed the real constructor, empty-system behavior, early-return path and shutdown ownership. One narrow source guard checks assignment before true and rejects broad injection, loop appends or premature disposal. **32 Lab static tests and 23 CI tests pass**, with preflight and updated frozen source/asmdef checks. These checks are not Unity execution.

The live source-review record uses native baseline **bda5bc7 attempt 2** and identifies this pending `LabWorld.cs` change separately. The original native audit still describes the tested source. The previous request is removed from the prepared source; no new gate or run is included.

A later authorized run must compile this Runtime change, verify the empty default World is assigned on entry and disposed by official shutdown, and show both bootstrap assertions absent on reentry. The original cleanup assertion is expected to remain a blocker until separately handled; this commit cannot make the Editor control green by itself. The original **49+1**, all thresholds/logging expectations, upstream pin, lock provenance, safety settings and root product are unchanged.
