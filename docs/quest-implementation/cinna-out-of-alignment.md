# Cinna Cirra: Out of Alignment — implementation tracker

**Quest ID:** `cinna_out_of_alignment`
**Monitor:** `cinna_out_of_alignment_monitor` → `complete_cinna_out_of_alignment`
**Assessment:** **Partial.** The Vis state flag and quest event threshold are implemented in data/runtime. The design description and player-facing rule are under-specified, while the map provides a single generic orientation target.

## Player flow and authored contract

Cinna Cirra's orientation NPC record is on `campus`, cell `(22,51)`, and uses the shared placeholder `NPCs/Elara.prefab`. Dialogue placement metadata says C02. The accept choice starts this quest; active and completed dialogue branches are provided. The accept line says to compare the object's face to the terrace mark, then use Vis. The active hint refers to the C02 terrace and suggests comparing alignment visually.

The quest description says “align the orientation objects and learn Vis.” Quest metadata offers Vis on completion. Runtime spell reward code grants spell rewards when a quest status becomes Completed. There is no acceptance-time Vis unlock action in this dialogue, so the authored instruction presumes access through another progression path. The runtime reward handler grants Vis only when this quest completes; without an external unlock, the quest cannot be solved from a fresh game. Provide a pre-puzzle teaching/unlock path.

## Runtime completion and state

The quest database targets `campus` and requires `orientation.cinna.align` to have `aligned=true`. The completion event is non-repeatable and Active-only. `SpellCaster.Vis` sets `aligned=true` and rounds the object's rotation to the nearest quarter-turn. It does not compare the object with a separately authored marker in the visible action implementation. Therefore, one successful Vis cast on the target satisfies the current registry rule regardless of its starting orientation.

`WorldStateService` owns the target state and saves `aligned` and rotation. `QuestPuzzleMonitor` only invokes the event on the matching map when the quest is Active and targets are registered after map rebuild.

The contextual hint describes alignment with Vis and points toward the authored campus target. It does not add a face-to-marker comparison or failure rule beyond the current `aligned` state condition.

## Scene and data readiness

The resolved `campus.json` describes `orientation.cinna.align` at `(22,51)` with `vis` tag and rotation 90, matching the C02 placement of Cinna’s orientation NPC. A target ID in static map JSON does not establish a visible marker, mesh, readable mismatch state, or usable spell prompt. `docs/quests.md` originally left object type and exact rules open; the registry now supplies the generic aligned flag but does not settle an educational rule or success/failure feedback.

## Open gaps and acceptance checklist

- [ ] Add a Vis teaching/unlock path before the target requires casting it; the current reward occurs only after quest completion.
- [ ] Define the alignment rule (including whether there is a distinct target marker), starting state, failure response, and whether multiple objects are required.
- [ ] Verify final campus C02 object registration, visual marker, player reach, Vis cast and feedback.
- [ ] Verify monitor waits until Active and completes only at the intended alignment state.
- [ ] Verify completion unlocks Cold Feet, Warm Idea and Vis progression behaves as designed; verify save/load restoration.

Evidence: `quest_npc_dialogue.json`; `npcs.json`; `quest_puzzle_definitions.json`; `Maps/campus.json`; `SpellCaster.cs`; `WorldStateService.cs`; `QuestPuzzleMonitor.cs`; `docs/quests.md`; `docs/spells.md`.
