# Cinna Cirra: Cold Feet, Warm Idea — implementation tracker

**Quest ID:** `cinna_cold_feet_warm_idea`
**Prerequisite:** `cinna_out_of_alignment:Completed`
**Monitor:** `cinna_cold_feet_warm_idea_monitor` → `complete_cinna_cold_feet_warm_idea`
**Assessment:** **Partial.** The active quest gate, Glaciate spell behavior, target coordinates, persistence, and completion event are data/runtime-backed. The documented trap sequence has no represented runtime conditions, and Glaciate unlocks only when this quest completes.

## Player flow and authored contract

Cinna Cirra is registered in `campus`; dialogue metadata places this quest at C03. The accept choice is shown only after Out of Alignment is completed and this quest is NotStarted. It starts the quest. The accept/active dialogue describes an ordered trap trial, freezing the surface, starting from a safe edge, sliding toward a marked stopping point, and leaving a clear route back. Glaciate is listed as the quest's spell reward, which runtime grants when the quest completes. The dialogue does not unlock Glaciate at acceptance, so the objective is unavailable from a fresh game unless another route grants it first.

## Runtime completion and state

The quest puzzle database, campus object tag, and map-level target now agree that `trial.cinna.ice` must be frozen at `(34,67)`. The object starts at `(34,71)` and carries tag `slide-goal:34,67`. `SpellCaster.Glaciate` freezes the target and moves it repeatedly in one chosen direction, stopping at a `slide-goal:x,y` tag if present, otherwise when movement is blocked or reaches map boundary. The monitor does not check trap IDs, order, player path, safe-edge start, or route back; only frozen state and the exact database position determine success.

The completion event is non-repeatable and requires Active status. `WorldStateService` persists cell and frozen state. Because the spell action can freeze and slide directly when cast, confirm the intended command direction and goal tag make the target settle at the required monitor coordinate.

The objective hint uses the active C03 target and points to the object; after it is frozen it gives the required destination cell `(34,67)`. It is guidance for the current target condition, not a trap-order evaluator.

## Scene and data readiness

The relevant region is C03, “Orientation trial strip,” on campus. The resolved map object starts at `(34,71)`, while its `slide-goal` tag and map-level target agree on `(34,67)`; `quest_puzzle_definitions.json` has been aligned to `(34,67)`. Verify blockers, collision footprint, target compatibility, and safe exit in the playable scene. Reset behavior remains unresolved.

## Open gaps and acceptance checklist

- [ ] Verify the resolved `(34,67)` target with a valid slide direction and route clearance in Unity.
- [ ] Add a Glaciate teaching/unlock path before the slide objective; the current reward occurs only after quest completion.
- [ ] Implement/confirm trap order and decide whether traps are actual puzzle conditions or dialogue-only framing.
- [ ] Verify slide stops at the intended point from a defined starting direction; test blocked path and safe exit.
- [ ] Define reset/retry behavior after wrong movement or save/load and ensure quest completion is Active-only.
- [ ] Verify C03 runtime wiring, feedback, collision and save restoration in Unity.

Evidence: `quest_npc_dialogue.json`; `npcs.json`; `quest_puzzle_definitions.json`; `Maps/campus.json`; `SpellCaster.cs`; `WorldStateService.cs`; `QuestPuzzleMonitor.cs`; `docs/quests.md`; `docs/spells.md`; `docs/implementation-status.md`.
