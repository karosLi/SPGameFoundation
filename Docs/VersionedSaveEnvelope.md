# Opt-in same-runtime save envelopes

Stage D of the [semantic extension plan](SharedFoundationSemanticExtensionPlan.md), implemented as an additional entry point. This is bounded compatibility framing for the existing raw session payload, **not** field serialization, authentication, arbitrary historical migration, crash-atomic disk persistence, or cross-platform saves. The existing raw API, SessionSnapshotSave v1, ProfileStore, bootstraps and fixture bytes retain their original behavior.

## Evidence and design choice

The source audit starts from the [compatibility matrix](FoundationCompatibilityMatrix.md): NativeIO writes count/size/native bytes; table snapshots lack column identities; system snapshots use names; authoritative configuration generally lives outside the payload. Equal-sized columns can exchange meanings without a raw-reader error. Raw readers also intentionally compose into larger streams and therefore do not require EOF. Neither fact justifies changing their established byte protocol.

The pinned Unity [ImageGeneratorAuthoring baker](https://github.com/Unity-Technologies/EntityComponentSystemSamples/blob/6786a741ee1f118ed14cecfa02beae8e926937b0/EntitiesSamples/Assets/Baking/BakingDependencies/ImageGeneratorAuthoring.cs) explicitly declares its configuration/asset dependencies and keeps baking-only data out of runtime. Retrieved source blob `1e2780e1064e9e0515b9f4a2338010ae5697693d` was inspected for this implementation. We adopt the local pattern of explicit dependencies and already-baked definitions, not Entities/Baker, Unity 6, or sample source. No third-party code, assets or dependencies are copied; the source's Unity Companion License does not license a new imported dependency here. The [pinned survey](OpenSourceSharedFoundationSurvey.md) remains the broader alternatives record.

Options considered:

- Change NativeIO/table headers: rejected because it would break every raw fixture and existing reader.
- Infer semantics from `sizeof` or reflected fields: rejected; equal-size changes of meaning still need an explicit version.
- Introduce a universal config base or serialize every object: rejected; the two games already have baked runtime definitions and WeaponRuntime/SkillSlots already clone and validate their inputs.
- Add cold explicit descriptors and a fixed bounded envelope: adopted. No Tick, scheduling, input, rendering or combat path is changed.

## Identities and complete local schemas

[SaveCompatibilityDescriptor](../Assets/SinglePlayerFoundation/Runtime/Session/SaveCompatibilityDescriptor.cs) hashes its bounded input immediately and exposes no mutable digest buffer. It has seven separate SHA-256 identities:

1. Mode ID
2. Callable contract ID/version
3. Ordered save schema, with explicit semantic IDs/versions for module, table, column, resource and system, plus the existing raw-session/registry/handle framing
4. Authoritative content: actual baked rules, ordered definitions, seed, TickRate, MaxTicksPerFrame, manual-clock policy, capacities and ordering/rule revisions
5. Visual definitions, separate from authoritative rules
6. Existing raw-reader compatibility gates, which can include legacy cosmetic fields
7. Caller-supplied runtime compatibility domain: **the application's build, scripting backend, architecture and ABI**

The application must supply an accurate runtime ID. Equal IDs are a compatibility assertion by the application, not automatic proof of identical runtimes. Change the explicit schema/rule/system version when source meaning changes, even when size/type names do not. Current sizes supplement the explicit column schema; they never replace it. The outer SHA-256 values detect differences and accidental corruption, not who wrote a file or whether a maliciously recomputed payload is safe.

[SnapshotSchema](../Assets/SinglePlayerFoundation/Runtime/World/SnapshotSchema.cs) checks every actual ordered table/column/resource against the explicit recipe, including unsaved resources and level/pooled scope. The pooled hidden dead flag and handle semantics are included. It rejects uncovered members, duplicate stable names, and actual equal-size column reordering. System checking covers every actual execution slot, concrete identity, phase/order and explicit version, including systems with no snapshot hook. Process-local AccessKey.Id is never persisted.

Resource policy describes current behavior, rather than silently changing it: authoritative snapshot state; static definitions covered by content; snapshot state with derived caches; discarded output-only events. Main-thread game flow, commands, skill slots and weapon state are explicitly listed. The partial Stage B preflight manifest is neither imported nor treated as a complete save schema.

## Two actual consumers

- [BwWeaponSave](../Assets/BrawlerFoundation/Runtime/BwWeaponSave.cs): weapon-belt Brawler, including configuration, skills/weapons, attack definitions, authoritative skeleton/clip data, decision program, capacities and the fixed **unsaved** belt-grid origin.
- [SvWeaponSave](../Assets/SurvivorFoundation/Runtime/SvWeaponSave.cs): Classic/Guard Survivor **with weapons installed**, including every SvSettings field, ordered baked enemy/pattern definitions, skills/weapons, capacities and actual raw GameState extension selection. Flying-sword/crossed-blade composites are deliberately unsupported by this first recipe; they need their own complete schema.

Both adapters derive the descriptor from the live session's already-baked definitions on every capture/restore. They do not assume mutable ScriptableObject authoring still describes that session. WeaponRuntime's existing cloned profile order/validation and raw-v2 fingerprint are reused; SkillSlots definitions are read through its existing immutable snapshots. Raw writers remain unchanged.

Enemy colors/names can change while preserving Survivor authoritative compatibility. Weapon VisualId/secondary grip and SkillSlots IconId remain subject to their old raw reader gates: these can reject before mutation even when the separate authoritative hash matches. Weapon grip/muzzle geometry and Brawler skeletons affect actual collision and are authoritative, even though also used for drawing.

## Binary frame and resource limits

[SaveEnvelope](../Assets/SinglePlayerFoundation/Runtime/Session/SaveEnvelope.cs) v1 is one standalone EOF-terminated frame, including for a chunked non-seekable stream:

| Offset | Field | Encoding |
| --- | --- | --- |
| 0 | magic `0x45504653` | little-endian int32 |
| 4 | envelope version = 1 | int32 |
| 8 | payload kind = 1, same-runtime raw | int32 |
| 12 | raw payload length | positive int32 |
| 16 | seven identities in the order above | 7 × 32 bytes |
| 240 | integrity hash of header bytes 0..239 followed by payload | SHA-256, 32 bytes |
| 272 | unchanged raw session snapshot | declared length |

Unknown magic/version/payload kind, incompatible nonvisual identities, length outside the configured limit, truncation, trailing outer bytes and bad integrity reject before any live Sync/restore. No untrusted strings or collections are decoded by the frame parser. Identity inputs are bounded to 64 KiB each; IDs are at most 128 characters. Payload defaults to 16 MiB, and callers may select a positive bound up to the hard 64 MiB ceiling. Choose a smaller measured bound for a shipping mode; limits are cold-path safety ceilings, not mobile memory budgets.

Restore’s framing layer uses one payload-sized managed buffer plus small fixed framing/hash buffers; the downstream raw readers can additionally allocate strings, queues and their existing scratch state. Capture uses a capped growable raw buffer, one exact raw copy, and the final envelope, so simultaneous retained byte buffers can approach **3 × payload bound + header** (plus transient old growth buffers until GC). Import also retains the bounded original raw input and a temporary session; its peak can approach **4 × bound plus both sessions' allocations**, again excluding transient GC/growth overhead. No compression or hot-path work is introduced. Actual Android/iOS peak memory, load time, thermal behavior and IL2CPP execution remain unmeasured.

All methods leave supplied streams open. `ReadPayload` consumes exactly one entire stream frame; it is not a multi-envelope stream iterator. `Write` first captures a complete frame, but a later destination-write failure may leave a partial destination. It makes no atomic-file-replacement promise.

## Failure and timeline semantics

- **Envelope prevalidation rejection:** live raw bytes, effective/private pause state, pending tick requests, timeline revision and any outstanding tick are untouched. The frame parser does not call Sync.
- **Successful restore, including the same tick:** existing raw restore semantics apply; timeline revision increments once, pending ticks clear, and the weapon presentation revision/cues invalidate through the existing resource reader.
- **Structurally invalid but correctly checksummed raw data:** raw restore can mutate before discovering the error. SimSession restarts, preserves its private running/paused state and host suspension, clears pending ticks and rethrows. This is the existing safe restart policy, not transactional recovery of the previous live game.
- **Extra bytes inside an otherwise valid checksummed payload:** an internal envelope-only Session helper discovers this after raw restore. It restarts and rethrows while preserving Created/running/manual pause/host suspension. TimelineRevision changes **twice**: once for the successful raw restore, once for restart. No old raw stream reader is changed.

No claim is made that every malicious raw value is semantically validated. The legacy readers remain the final bounded-payload parsers; an integrity hash can be recomputed by an attacker.

## Explicit known-legacy import

`ImportKnownLegacy` requires a named `KnownLegacySaveDescriptor`, a target ModeDefinition/seed and a function that describes the internally created temporary session. It bounds the raw input, creates/owns a fresh session, requires descriptor compatibility, restores and checks complete raw consumption there, then captures the resulting envelope. It never accepts a live session to mutate and never writes/replaces a file. On failure, original bytes and live sessions remain with their owners.

This supports wrapping a **known same-runtime layout**; no arbitrary historical discovery, field converter, or vN→vN+1 semantic migration is implemented. Unknown legacy layouts must be rejected. Callers keep the original file until their separate verified/durable storage policy has committed the new one. The classic Survivor `97a2b34` raw fixtures remain raw-reader regression tests, not an assertion that this weapon adapter can import them.

## Example opt-in use

```csharp
// Runtime ID is an application-maintained, verified compatibility domain.
const string runtimeId = "mygame.release-17.unity2022.3.mono.x64";
byte[] bytes = BwWeaponSave.Capture(session, runtimeId, maxPayloadBytes: 1024 * 1024);
using (var stream = new MemoryStream(bytes, writable: false))
    BwWeaponSave.Restore(stream, session, runtimeId, maxPayloadBytes: 1024 * 1024);
// Survivor weapon sessions use SvWeaponSave in the same way.
```

Do not pass an unrelated/stale descriptor to the generic SaveEnvelope API. Prefer the game adapters, which re-describe actual content. Default games/UI continue using their old raw APIs unless separately opted in.

## Validation

See [Stage D validation](validation/SaveEnvelope-20261007.md). .NET stubs, real-Unity API compile-only, actual Unity execution and physical-device validation are separate claims.
