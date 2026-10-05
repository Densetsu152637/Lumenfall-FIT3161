# Katrina: Something Borrowed — implementation tracker

**Quest ID:** `katrina_something_borrowed`
**Monitor:** `katrina_something_borrowed_monitor` → `complete_katrina_something_borrowed`
**Assessment:** **Partial.** Lost Echoes behavior, reveal-state persistence, keepsake target, NPC dialogue, and quest completion event are authored. Item recovery/inventory reward and trail/family details are not implemented by the documented runtime contract.

## Player flow and authored contract

Katrina is registered at `campus` cell `(40,68)`; dialogue placement metadata says C04. The record uses placeholder `NPCs/Elara.prefab`. She minimizes the missing item's importance, then accepts help. The accept and active guidance say to use Lost Echoes around the garden walk and follow revealed traces to an alcove. The completed line says the keepsake is returned and implies personal history, but does not identify the item's identity or family link.

Quest metadata offers LostEchoes and a `katrina-keepsake` key item. The dialogue metadata repeats the key-item reward. The runtime contracts establish spell reward handling, but the docs say inventory actions require a registered custom handler. No quest dialogue action in the current record gives the item, and the completion event only completes the quest. Treat the key item as metadata/design intent until a reward handler or explicit inventory action is evidenced.

## Runtime completion and state

The quest database targets `campus`; it requires `keepsake.katrina.hidden` to be revealed at `(42,60)`. `SpellCaster.LostEchoes` reveals the targeted object and also every object tagged `hidden` on the currently loaded map. It does not perform a pickup or transfer item ownership. The completion event is non-repeatable, Active-only and calls `CompleteQuest`.

`WorldStateService` persists object position and revealed state. The `WorldStateService` map-scoped object registry means only objects registered on the current map are reveal candidates. Completion checks exact cell and revealed flag but not inventory possession, narrative clue chain, interaction at the alcove, or return conversation.

The objective hint describes revealing the hidden keepsake with Lost Echoes and points toward its current cell. It reflects reveal-at-place behavior; it does not implement an item pickup or return interaction.

## Scene and data readiness

The resolved campus map places the keepsake at `(42,60)` with `hidden` and `lost-echoes` tags. The quest monitor now requires `(42,60)`, matching the map object cell; the map-level target checks revealed state only. The active `npcs.json` record places Katrina at `(40,68)`; `docs/npc-dialogue.md` still says `(23,40)` and alcove `(30,42)`. The active registry and map place the interaction in C04, while older prose gives different positions and an alcove coordinate. Align the hint to the current target cell `(42,60)` or update object and monitor to the approved alcove. Confirm a visible trail/pickup presentation.

## Open gaps and acceptance checklist

- [ ] Reconcile Katrina NPC `(40,68)` (registry; stale docs say `(23,40)`) and map/object cell `(42,60)` with the older prose hint to search near alcove `(30,42)`.
- [ ] Decide keepsake identity and personal/family history before finalizing dialogue.
- [ ] Decide whether reveal alone completes the quest or whether pickup/return interaction and inventory/key-item reward are required.
- [ ] Implement and verify the `katrina-keepsake` reward path or remove unsupported reward metadata.
- [ ] Add a Lost Echoes teaching/unlock path before the quest requires using it; current quest metadata grants it only at completion.
- [ ] Verify only intended traces/hidden objects reveal; test persistence, completion, and return dialogue in Unity.

Evidence: `quest_npc_dialogue.json`; `npcs.json`; `quest_puzzle_definitions.json`; `Maps/campus.json`; `SpellCaster.cs`; `WorldStateService.cs`; `QuestPuzzleMonitor.cs`; `docs/npc-dialogue.md`; `docs/spells.md`; `docs/runtime-data-contract.md`.
