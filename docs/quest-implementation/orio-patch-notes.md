# Orio: Patch Notes — implementation tracker

**Quest ID:** `orio_patch_notes`
**Monitor:** `orio_patch_notes_monitor` → `complete_orio_patch_notes`
**Assessment:** **Partial.** Rotara behavior, quest definition, event threshold, target object, and reward metadata exist. The authored requirement is only a quarter-turn value; the promised sequence/receiver puzzle is not modeled by the monitor.

## Player flow and authored contract

Orio's NPC record is on `westForest` cell `(8,23)` and uses `NPCs/Elara.prefab` placeholder art. Dialogue metadata places the conversation at F04. The accept choice starts the quest; the active branch offers a hint, and the completed branch closes the loop. Orio describes a mechanism where components pass output along like a Spellio turn, asks the player to turn the central mechanism and observe the response, then points it to a marked quarter-turn. The active hint says to work backwards from the receiver and avoid breaking the chain.

Quest metadata rewards both Rotara and Gravitate when completed. The acceptance dialogue does not grant either at quest start, so runtime awards Rotara and Gravitate only after meeting the monitor threshold. The puzzle specifically directs use of Rotara; a fresh game has no evidenced pre-quest unlock path, making the current objective inaccessible.

## Runtime completion and state

The quest database targets map `westForest`; `mechanism.orio.rotate` must have `rotation=90`. The forest map places the stable object ID at `(8,23)` tagged `rotara`; the map-level target independently requires 90 degrees. No initial `rotation` is authored in this map object, so the runtime starting orientation depends on instantiated object defaults. `SpellCaster.Rotara` advances rotation by 90 degrees modulo 360 per successful cast. The monitor does not check receiver state, multiple component sequence, output chain, or any other mechanism object; any route that leaves this object's rotation at 90 satisfies the target.

The completion event is non-repeatable and Active-only. World object rotation is held by `WorldStateService` and included in `spell-world-state` snapshots. The NPC and mechanism share coordinates in data, so validate interaction overlap, character/object placement and nearest-target selection in the scene.

The objective hint identifies the required rotation (90 degrees) and points toward the target on the relevant map. It does not evaluate the output chain described by Orio.

## Scene and data readiness

`westForest` contains F04 and the mechanism object/target. Static data confirms the ID and target value, while runtime scene presentation, authored chain components, receiver feedback, blockers and portal route have not been verified. The outdoor scene wiring and new-game entry remain unresolved in the implementation status. The map does not author an initial rotation value. Inspect the instantiated prefab/default state to determine how many casts reach 90 degrees.

## Open gaps and acceptance checklist

- [ ] Add a Rotara teaching/unlock path before the quest requires casting it; test progression from a fresh save.
- [ ] Author and validate final mechanism configuration, receiver/chain conditions, and feedback, or revise dialogue to match the single-object rotation target.
- [ ] Confirm the instantiated initial rotation and that casts advance exactly 90 degrees to the intended state.
- [ ] Verify `westForest` object registration, F04 region access, Orio interaction, cast reach and collision footprint in Unity.
- [ ] Verify completion grants Rotara and Gravitate, persists quest/object state, and exposes the completed dialogue.

Evidence: `quest_npc_dialogue.json`; `npcs.json`; `quest_puzzle_definitions.json`; `Maps/westForest.json`; `SpellCaster.cs`; `WorldStateService.cs`; `QuestPuzzleMonitor.cs`; `docs/quests.md`; `docs/spells.md`; `docs/implementation-status.md`.
