# Full game implementation plan and status

This document is the delivery guide for Lumenfall. It combines the playable game intent in the design notes with the runtime and data that are currently in the repository. Status is evidence based:

- **Implemented** means the behavior exists in first-party code or a shipped data asset and has automated coverage where that is available.
- **Partial** means a reusable foundation or data exists, but the player-facing feature still needs integration, content, presentation, or play-mode verification.
- **Planned** means the design is documented but has no corresponding production implementation.
- **Unknown** means the repository does not establish the decision or behavior; resolve it before production work depends on it.

The project is a Unity `6000.4.10f1` project. Open [`Lumenfall`](../Lumenfall) as the Unity project root. The authoritative first-party code lives under [`Lumenfall/Assets/_Project`](../Lumenfall/Assets/_Project); authored runtime data is under [`Lumenfall/Assets/Resources`](../Lumenfall/Assets/Resources). The existing design notes describe intended content and should be read alongside this status report.

## Product target and chapter loop

The opening chapter is a cozy 2.5D, tile-based academy mystery. The player arrives through the west forest, reaches the academy, learns that the dormitory key was supposedly passed to another student, and meets Stella, Cinna Cirra, Orio, and Katrina while helping with their practical spell puzzles. The receptionist later contradicts the earlier account and hands over the key. The group discusses the contradiction, then the player returns to the Sirius dormitory and sleeps; waking ends Chapter 1. See [`main-cast.md`](main-cast.md) and [`world.md`](world.md).

The intended loop is:

1. Explore the forest, campus, and approved interiors on a whole-tile grid.
2. Talk to a character through proximity interaction and data-driven dialogue.
3. Accept or advance a character quest.
4. Use the newly introduced spell and local puzzle objects to satisfy a target.
5. Let the puzzle monitor complete the quest and unlock the next conversation.
6. Return to the story route, save, and continue exploring until the chapter's dormitory transition.

The mystery, exact quest order, reward presentation, sleep/wake transition, and optional activity scope still need a product sign-off. The current data establishes six main quests and their prerequisite relationships, but it does not by itself make the complete Chapter 1 flow playable.

## What is in the repository now

### Implemented runtime foundations

| Area | Evidence | Status and limit |
| --- | --- | --- |
| Grid movement | [`Systems/GridMovement`](../Lumenfall/Assets/_Project/Systems/GridMovement), especially [`GridWorld.cs`](../Lumenfall/Assets/_Project/Systems/GridMovement/GridWorld.cs), [`GridMover.cs`](../Lumenfall/Assets/_Project/Systems/GridMovement/GridMover.cs), and [`GridPathfinder.cs`](../Lumenfall/Assets/_Project/Systems/GridMovement/GridPathfinder.cs) | **Implemented foundation.** Whole-tile movement, reservations, obstacles, A* NPC navigation, diagonal corner rules, and portals are coded. Sprint selects a faster duration; there is no stamina system. Play-mode scene coverage is still required. |
| Camera | [`CameraFollow2_5D.cs`](../Lumenfall/Assets/_Project/Systems/Camera/CameraFollow2_5D.cs) and [`Camera/README.md`](../Lumenfall/Assets/_Project/Systems/Camera/README.md) | **Implemented foundation.** Follow behavior exists; final framing, occlusion, interior transitions, and production camera tuning are unresolved. |
| Map loading | [`DataDrivenWorldLoader.cs`](../Lumenfall/Assets/_Project/Systems/DataDriven/DataDrivenWorldLoader.cs), [`runtime-data-contract.md`](runtime-data-contract.md), and `Assets/Resources/Maps` | **Implemented foundation.** JSON/text maps, regions, portals, buildings, object registration, NPC layers, and map state rebuilds are supported. Spawn, portal, collision, and scene transition behavior needs an editor/play-mode pass. |
| Dialogue and quest state | [`GameEvents`](../Lumenfall/Assets/_Project/Systems/GameEvents), [`quest_npc_dialogue.json`](../Lumenfall/Assets/Resources/GameEvents/quest_npc_dialogue.json), and [`DialogueOverlay.cs`](../Lumenfall/Assets/_Project/Systems/DataDriven/DialogueOverlay.cs) | **Implemented runtime/content foundation.** Six quests, 15 dialogue records, event conditions/actions, proximity dialogue, and quest status transitions are represented. Final dialogue writing, UX polish, lifecycle integration, and chapter-completion state are still needed. |
| Saves | [`GameSaveManager.cs`](../Lumenfall/Assets/_Project/Systems/GameEvents/GameSaveManager.cs), [`StartupMenu.cs`](../Lumenfall/Assets/_Project/Systems/Startup/StartupMenu.cs), and [`GameEvents/README.md`](../Lumenfall/Assets/_Project/Systems/GameEvents/README.md) | **Implemented foundation.** Three slots, startup load/new-game flow, provider snapshots, rollback on provider failure, map/player restore, and spell-world persistence are coded. The IMGUI overlays are sample-quality and need production UI, corruption/recovery UX, and play-mode validation. |
| Spells and puzzle state | [`SpellCaster.cs`](../Lumenfall/Assets/_Project/Systems/Spells/SpellCaster.cs), [`WorldStateService.cs`](../Lumenfall/Assets/_Project/Systems/Spells/WorldStateService.cs), and [`quest_puzzle_definitions.json`](../Lumenfall/Assets/Resources/Quests/quest_puzzle_definitions.json) | **Partial.** Seven spell IDs, object properties, connections, movement/sliding, reveal, save state, and six monitors are implemented. Tutorial unlocks, animations/VFX, input feedback, exact puzzle rules, reset/retry behavior, and scene wiring are incomplete. |
| Tests and build tooling | [`Systems/GameEvents/Tests/EditMode`](../Lumenfall/Assets/_Project/Systems/GameEvents/Tests/EditMode), [`compile_runtime.ps1`](../scripts/compile_runtime.ps1), and [`compile_editmode_tests.ps1`](../scripts/compile_editmode_tests.ps1) | **Compiled and headless-validated.** Runtime and EditMode test compilation passed; the full Unity Tundra build passed; the headless GameEvents suite passed 29/29. Live play-mode, rendered UI/startup save-flow, performance, accessibility, and platform acceptance remain unverified. |

### Maps and level data

`Assets/Resources/Maps/index.json` lists six maps: `campus`, `westForest`, `B01.Ground`, `B02.Ground`, `B03.Ground`, and `B04.Ground`. It contains 59 area records: 26 marked Chapter 1, 9 Optional, and 24 Reserved. `campus.json` contains six objects and six puzzle targets; `westForest.json` contains one object and one target; `B02.Ground.json` contains two objects and two targets. B01, B03, and B04 currently contain map/region/portal structure without puzzle objects.

The design layout identifies the playable route as F01-F04 forest, A01/S01-S03 arrival, C01-C05 campus, B02 teaching spaces, and D01/D02/D04/D07/D08 Sirius spaces. The proposed campus and room plan are useful blockout references, not proof that the corresponding 3D spaces are complete. See [`academy-map.md`](maps/academy-map.md), [`location-plan.md`](location-plan.md), and the tile layouts under [`docs/maps/layouts`](maps/layouts).

The current Unity scene set is [`OutdoorScene.unity`](../Lumenfall/Assets/_Project/Scenes/OutdoorScene.unity) and [`SampleScene.unity`](../Lumenfall/Assets/_Project/Scenes/SampleScene.unity). Building prefabs exist in [`Assets/Resources/Buildings`](../Lumenfall/Assets/Resources/Buildings), while the map system can load them by ID. Confirm which scene is the production entry point, whether interiors are loaded as separate scenes or generated into the outdoor scene, and whether both scenes are in Build Settings before level polish.

## System implementation contract

Keep the data and runtime boundaries already established:

1. `DataDrivenWorldLoader` owns map selection, map rebuild, tile/object instantiation, portals, and NPC placement. Stable map/object IDs are the join key for quests, spells, saves, and dialogue placement.
2. `GridWorld` is the movement authority. Obstacles, portals, and movers must register with it; puzzle code should request movement through the world rather than changing transforms directly.
3. `GameEventRuntime` owns flags, counters, quest status, event chaining, and dialogue sessions. Quest completion should use `CompleteQuest` through the runtime, with custom actions only for systems such as inventory or world objects.
4. `WorldStateService` owns mutable puzzle object state and the `spell-world-state` save provider. `QuestPuzzleMonitor` should remain the bridge from an authored `puzzleTargets` condition to a completion event.
5. `GameSaveManager` owns slot files and provider orchestration. Any new mutable subsystem must register one stable provider key and support rejected payloads without leaving a partial load.
6. UI, audio, and presentation subscribe to runtime events (`DialogueStarted`, quest/state changes, spell cast/effect events, save completion) instead of duplicating game state.

## Quest and narrative implementation

The quest registry currently contains these six main quests and monitor targets:

| Quest | Location/data target | Spell lesson | Remaining completion work |
| --- | --- | --- | --- |
| Stella: First Light | `B02.Ground`, generated IDs `stella.string-together.relic.1`–`.5` | Ampere | The loader registers five F-operated toggle relics in `WorldStateService`; direct puzzle completion and the active-only monitor both require all five powered. Dialogue completion unlocks Ampere before the board. Runtime hinting suggests the next switch from live state. Primitive presentation and editor/play-mode acceptance remain to be verified. |
| Stella: String Them Together | `B02.Ground`, `relic.stella.power` → `circuit.stella.link` | Weave | Acceptance unlocks Weave; completion requires a powered source and exact connection to the link. Current local cells `(10,3)` and `(15,3)` produce connection strength about `0.167`, below `WorldStateService.AddConnection`’s `0.2` minimum, so the authored endpoints cannot currently connect. Replace the endpoints or adjust the threshold, then validate presentation and travel in play mode. |
| Cinna Cirra: Out of Alignment | `campus` `relic.cinna.orientation` at `(23,53)`, carried to the `C03.IceTrial` stand `(3,1)` | Vis | **Partial.** Cinna's terminal lesson teaches Vis while the quest is Active. Vis lifts the relic, which is carried through the trial portal and set on the pedestal. Reporting back to Cinna completes the quest behind a fade while Cinna leaves for the trial (`CinnaTrialDeparture`); the player walks over. EditMode tests are in `OutOfAlignmentTests.cs`, with a PlayMode route test in `AuthoredWorldPlayModeTests.cs`. See [implementation notes](implementation/quest-out-of-alignment.md). |
| Cinna Cirra: Cold Feet, Warm Idea | `C03.IceTrial` (portal at campus `(47,45)`), `switch.cinna.ice.01`–`.12` and `switch.cinna.ice.final` | Glaciate | **Partial.** Cinna's lesson lends Glaciate; freezing `relic.cinna.frost` (the relic delivered in Out of Alignment, which takes spells only once delivered) turns the pool into ice and the player slides onto toggle switches. The monitor completes when all twelve and the seal are lit. EditMode coverage compiles; the EditMode runner, the PlayMode slide route and visual acceptance have not been run. See [implementation notes](implementation/quest-cold-feet-warm-idea.md). |
| Orio: Patch Notes | `westForest`, `mechanism.orio.rotate` → `90°` | Rotara | Runtime checks only the mechanism rotation; the described chain/receiver sequence is not modeled. Rotara is awarded on completion, so add an earlier teaching/unlock path. Validate initial orientation and feedback. |
| Katrina: Something Borrowed | `campus`, `keepsake.katrina.hidden` at `(42,60)` | Lost Echoes | The active target completes on reveal at the authored cell; no pickup or inventory reward action is authored despite key-item metadata. Lost Echoes is awarded only on completion. Align older dialogue location guidance and define keepsake history. |

`docs/quests.md`, `docs/spells.md`, and `docs/npc-dialogue.md` explicitly leave several of those details unresolved. Do not turn those prose concepts into hard requirements until the design decision is recorded. Dialogue choices start quests but do not complete them. `QuestPuzzleMonitor` completes data-target quests while they are Active; First Light’s custom board also emits the shared completion event once all five generated relic IDs are powered.

### Quest hints and settings

`QuestHintOverlay` provides an objective card from quest status, prerequisite metadata, the quest puzzle database, and current map targets. It prioritizes active quests on the current map, points toward targets, and reports the destination cell for position-based objectives. First Light uses the board solver over live powered states to suggest a shortest next switch. The in-game Settings pop-out, the journal's Settings tab and the startup menu all expose a persistent “Show quest hints” switch (`PlayerPrefs`); the pop-out and journal share the themed `OptionsPanel` (see `Systems/Startup/README.md`). Opening the pop-out suppresses the card and gates movement, casting, NPC interaction, dialogue input, and First Light F interaction. The overlay is prototype IMGUI and needs play-mode and usability validation.

The receptionist key mystery, acceptance of the key, dormitory lock, friend-guided movement, sleep interaction, and waking/chapter-complete transition have no corresponding production quest flow visible in the current resource files. They are the highest narrative integration gap after the puzzle loop. Add stable event/quest IDs, authored dialogue nodes, objective feedback, and a deterministic end-of-chapter save marker.

## Art, UI, audio, and asset pipeline

The project owns a small presentation base: Orio source/model files and preview, a player animator controller, terrain data/materials, and the folder contract in [`Art/README.md`](../Lumenfall/Assets/_Project/Art/README.md). Most environment and character content is in `Assets/ThirdParty`; six active NPC records deliberately use the shared placeholder `NPCs/Elara.prefab` (see [`npc-dialogue.md`](npc-dialogue.md)). Treat those records as data placement, not finished character art.

Complete presentation in this order:

1. Replace placeholder NPC visuals with approved player, Stella, Cinna Cirra, Orio, Katrina, receptionist, and ambient student prefabs. Verify scale, collision, grid anchor, animator parameters, facing, and portrait identity.
2. Build a coherent campus/forest/interior kit from approved source or generated assets. Add walls, gates, doors, signs, fountain, garden, teaching props, dormitory props, puzzle objects, and readable interaction markers. Keep generated/source files and license/provenance notes with the asset handoff.
3. Add spell cast, object state, quest completion, portal, dialogue, and sleep/wake feedback. Each authored puzzle object needs an idle, valid target, invalid target, solved, and restored-from-save presentation where applicable.
4. Replace sample IMGUI startup/save/dialogue presentation with production UI supporting keyboard/gamepad navigation, pause/input gating, readable text, choices, objective state, errors, and volume/settings controls. Retain the existing event and save APIs.
5. Add ambience, music states (forest, campus, interior, dialogue, mystery, sleep/wake), spell SFX, footsteps, UI feedback, and mix controls. Audio assets and settings belong under `Assets/_Project/Art/Audio` once the first approved assets exist.

Decide the visual style, target resolution/aspect ratios, portrait policy, font, color contrast, localization strategy, and whether external generation is permitted before commissioning a large asset batch. No asset generation is required to complete this document.

## Prioritized completion plan

### P0: prove a coherent playable slice

- Select the production entry scene and Build Settings entries; make a clean new game load `campus`/forest and return reliably through portals.
- Wire player, camera, GridWorld, loader, NPCs, dialogue overlay, spell caster, puzzle monitors, and save provider in a real scene.
- Implement the reception-to-first-quest route: arrival conversation, Stella quest start, Ampere lesson, target completion, and follow-up conversation.
- Replace or clearly label placeholder art and add interaction/error feedback sufficient for a tester to understand the loop.
- Run licensed Unity play-mode tests for movement, collision, portal arrival, dialogue choices, quest completion, map rebuild, save/load, and a fresh/new slot.

### P1: complete Chapter 1 content

- Author the six puzzle layouts and tune all target conditions, including resets and save restoration.
- Author final dialogue and quest prerequisites for Stella, Cinna Cirra, Orio, and Katrina; implement the receptionist/key contradiction and dormitory/sleep ending.
- Build and connect required B01/B02/B04 spaces and the forest/campus route; keep B03 and reserved rooms represented as doors or clear blockout until approved.
- Replace NPC placeholders, add core environment props, and deliver readable dialogue/objective/quest UI.
- Add audio pass, spell/quest feedback, accessibility settings, and a full chapter acceptance checklist.

### P2: production hardening and optional content

- Add optional dormitory power/recovery activities only after their scope, reset, reward, and replay rules are approved.
- Implement non-player schedules, ambient dialogue, additional interiors, waypoint travel, and expansion gates where the product plan requires them.
- Add automated validation for map references, duplicate IDs, broken portal destinations, missing assets, quest/dialogue references, and save migrations.
- Profile scene rebuilds, NPC navigation, object effects, memory, loading, and target platform performance; produce release builds.

Dependencies run from design decisions to data contracts, then map/puzzle blockout, then presentation and acceptance testing. Avoid polishing reserved rooms or optional minigames while the P0 route is not playable.

## Definition of done for the first release

The first release is ready when a new player can start a slot, traverse the complete Chapter 1 route, meet all four friends, accept and solve all six quests with clear spell teaching and feedback, receive the dormitory key through the intended contradiction, enter the player room, sleep, and see the waking/chapter-end state. A save made before and after each quest must restore the correct map, player cell, quest/dialogue state, puzzle object state, and unlocked route.

The acceptance run must also confirm that blocked cells remain blocked, diagonal movement does not cut corners, portals do not bounce, active dialogue/menu input cannot leak into movement, missing/invalid save files fail safely, quest completion is idempotent, and a solved puzzle remains solved after changing maps and loading a slot. Record the Unity version, platform, build identifier, test slot, and known limitations with each release candidate.

## Validation performed for this report

Runtime and EditMode test compilation passed, the full Unity Tundra build passed, and the headless GameEvents suite passed 29/29 tests (Editor exit code 0). The reproducible batch invocation is `Unity.exe -batchmode -nographics -projectPath <Lumenfall project path> -runTests -testPlatform EditMode -testFilter Lumenfall.Tests.GameEvents -testResults <results.xml> -logFile <editor.log>`. Raw result files are local generated artifacts, not repository evidence: `Lumenfall/TestResults/QuestHints-GameEvents.validated.xml` and `Lumenfall/TestResults/QuestHints-GameEvents.validated.log`. Repository inspection confirmed the six map IDs, 59 area records, Chapter 1/Optional/Reserved access counts (26/9/24), six puzzle monitors, seven NPC records, and six quests. These checks do not verify live rendered UI, startup-menu/save interaction, quest traversal and portal behavior, or the full play-mode acceptance checklist; keep those as manual validation items.
