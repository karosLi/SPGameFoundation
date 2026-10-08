# CellListGrid allocation tails and native Snake isolation

## Failure and diagnosis

Native run `37708607373`, source `35af6dfad4bdafbc06d0d5db31a9f1eebac68bff`, reported one EditMode failure:
`SnakeConfigIsolationTests.SourceEditsAfterSessionCreationDoNotChangeRuntimeOrReplay`.
All direct runtime/config-content assertions passed. The final complete snapshots both held
6,588,656 bytes; the first mismatch was byte 2,306,311 (expected 0, actual 24).

A .NET capture of the same seed/configuration/16 ticks has the same length. Parsing the unchanged
writer layout locates `Snake.BodyGrid` at payload offset 12,045 and its full `SlotKey` array at
2,305,861 (131,072 int32 elements). Offset 2,306,311 is `SlotKey[112]`, byte 2. The captured grid's
block high-water mark is 28, or 112 slots: index 112 is the first never-allocated slot, outside
every live cell chain and absent from `KeySlot`. This mapping uses the .NET capture; the native
failure XML provides the length/offset/values, not a complete native snapshot artifact.

`CellListGrid` allocated five full-snapshot buffers with `UninitializedMemory`, while construction's
`Clear()` only assigned cell metadata, key lookup and statistics. The writer preserved the entire
five buffers, including never-written tails. The .NET stub always zeroed allocations, even when
`UninitializedMemory` was requested, masking this native-only difference.

## Narrow fix and compatibility

Construct `Slots`, `SlotKey`, `BlockNext`, `BlockPrev` and `FreeBlocks` with the default
`ClearMemory` option. This is one-time allocation initialization. Capacities, snapshot layout,
writer/reader, insertion/removal order, and the hot `Clear()` path are unchanged. The original
Snake isolation test, including its complete final byte comparison, is unchanged.

At the tested default Snake capacities, the newly initialized payload is 92 bytes per block:
3,014,656 bytes for the body grid plus 2,071,104 for the item grid (5,085,760 total). This is a
byte count, not a native/mobile timing or memory-bandwidth measurement.

New tests compare complete snapshots of independently allocated empty and edited grids; a
positive control changes an unused slot and requires raw inequality. A separate compatibility
test places nonzero values in all five unused buffers, restores and rewrites the raw snapshot
exactly, then checks future edits, query order and complete bytes. Existing persisted tail bytes
are preserved; this change does not rewrite legacy snapshots or claim that Snake's unsaved
game/population state now supports complete mid-game restoration.

## Validation

- Standard .NET grid/world-snapshot focus: 22 passed.
- Controlled diagnostic: temporarily changed the .NET stub to fill only allocations requesting
  `UninitializedMemory` with a varying nonzero pattern; restored the normal stub afterward.
- With the original constructor, both new independent-grid cases failed, as did the unchanged
  Snake source-edit test; the legacy restore/rewrite case passed.
- With the fixed constructor under the same allocation diagnostic, all four cases passed.
  The diagnostic changes neither assertions nor snapshot data and is not part of the shipped code.
- Full .NET and exact-candidate native Unity verification are recorded separately by integration.
  The old native failure is not a passed result for this change. Physical Android/iOS cold
  construction cost and gameplay performance remain unmeasured here.
