# Stella: First Light — implementation tracker

**Quest ID:** `stella_first_light`
**Monitor:** `stella_first_light_monitor` → `complete_stella_first_light`
**Assessment:** **Implemented core puzzle; partial end-to-end quest.** The five-relic board, matching active-only monitor target, dialogue reward, BFS objective hint, and return portal exist in code/data. Runtime/test compilation and the full Unity build passed; the headless GameEvents suite passed 29/29 tests. The batch command and local generated result paths are recorded in `docs/implementation-status.md`. Rendered board behavior, map traversal, and restore flow still need play-mode validation.

## Player flow and authored contract

Stella is registered on `B02.Ground` at `(12,4)` with placeholder `NPCs/Elara.prefab`. Her dialogue provides NotStarted, Active, and Completed branches. Accept starts `stella_first_light`; the accept and active lines explain five star-point relics, F interaction, and lighting all five. This guidance matches the custom implementation in `StellaStringThemTogetherPuzzle.cs`.

The dialogue metadata grants Ampere when the player finishes the dialogue while the quest is Active. That provides the spell before the puzzle, although this custom board itself uses F rather than Ampere. The generated relics are explicitly registered as not spell-compatible, so the Ampere lesson is not exercised by this specific board. Clarify whether Ampere should also power the separate authored `relic.stella.power` object or whether its progression role is tutorial-only here.

## Runtime completion and state

`StellaStringThemTogetherPuzzle.RegisterForMap` is called by `DataDrivenWorldLoader` for `B02.Ground`. It creates five numbered primitive relic views at authored cells `(19,19)`, `(26,14)`, `(23,6)`, `(15,6)`, `(12,14)`, and registers stable IDs `stella.string-together.relic.1` through `.5` with `WorldStateService`. Nearby activation uses F and a fixed toggle mask; the board starts unpowered. The puzzle directly triggers `complete_stella_first_light` only when all five registered relics are powered. The puzzle UI shows the nearby relic, F instruction, and 0–5 status. The quest hint calls `TryGetFirstLightStep`, which reads live powered bits and uses breadth-first search across the toggle masks to suggest a shortest next switch; the overlay then shows the target cell direction. World state is saved by the existing `spell-world-state` provider. These generated IDs are registered into `WorldStateService` during map load before readiness; authored JSON object entries are not needed for them.

`quest_puzzle_definitions.json` now defines the First Light monitor over `stella.string-together.relic.1` through `.5`, each required powered. `QuestPuzzleMonitor` and the board both trigger `complete_stella_first_light` only for an Active quest after all five are lit. The board also invokes that event directly on success; the monitor condition is now aligned with the board, so either path represents the same objective.

The custom puzzle only triggers completion while the quest is Active. `GameEventRuntime` owns quest status/event dispatch; the completion event is non-repeatable and calls `CompleteQuest`. The state service saves puzzle object powered state, so verify a restored board and the intended completion behavior after loading.

## Scene and data readiness

The resolved B02 map contains the stable `relic.stella.power` at local `(10,3)`, plus `circuit.stella.link` at `(15,3)`. The map's `coordinateOffset` is `(10,12)`. A return portal ID `P.B02.STELLA.CAMPUS` is authored, and `StellaReturnPortal` builds/rebinds it from the area portal content. It becomes visible only after First Light is Completed and dialogue flag `stella.first_light.debriefed` has been set by entering the return-portal dialogue node. This is implemented code/data; actual editor/play-mode travel remains unverified.

The five generated relics use primitive placeholder geometry and simple color/number feedback. The task hint can identify the next switch using current puzzle state; no evidence here confirms final art, classroom readability, map boundaries, player access to all five positions, or portal travel in Unity.

## Open gaps and acceptance checklist

- [ ] Decide whether Ampere should be used in this quest; align its teaching/action role with the F-operated, non-spell-compatible switch board.
- [ ] Verify quest acceptance, dialogue-end spell unlock, F input, five toggle masks, visible 0–5 feedback, and Active-only completion in Unity.
- [ ] Verify all five board cells are reachable and state survives save/load and map rebuild.
- [ ] Verify the post-completion dialogue sets `stella.first_light.debriefed`, reveals the return portal, and returns to the intended campus entrance.

Evidence: `Lumenfall/Assets/_Project/Systems/Quests/StellaStringThemTogetherPuzzle.cs`; `Lumenfall/Assets/_Project/Systems/Quests/StellaReturnPortal.cs`; `Lumenfall/Assets/_Project/Systems/DataDriven/DataDrivenWorldLoader.cs`; `Lumenfall/Assets/Resources/Maps/B02.Ground.json`; `Lumenfall/Assets/Resources/Quests/quest_puzzle_definitions.json`; `Lumenfall/Assets/Resources/GameEvents/quest_npc_dialogue.json`; `Lumenfall/Assets/Resources/NPCs/npcs.json`; `Lumenfall/Assets/_Project/Systems/Quests/QuestPuzzleMonitor.cs`; `Lumenfall/Assets/_Project/Systems/Spells/WorldStateService.cs`.
