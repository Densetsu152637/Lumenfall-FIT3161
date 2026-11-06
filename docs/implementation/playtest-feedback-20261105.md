# Playtest feedback: 5–6 November 2026

Changes made for the November playtest round. Items already covered by the [October round](beta-feedback-debugging-20261018.md) (Rowan's facing and pose, the gyroscope, First Light readiness) and the deliberate design calls recorded there (spell key 3, Weave on dark runes) were checked and left as they are.

## Bugs

| Report | Cause and change |
| --- | --- |
| Could not move after alt-tabbing while reading the Library sign; loading saves did not help, New Game did | Five systems shared `GridMover`'s single boolean movement lock (cutscenes, cutscene walks, the ice-trial fall, Cinna's departure fade, the north-gate dog joke), so one could release or strand another's lock. `AnnoyingDogGate` never released its lock if any other lock was already held. All of them now hold owner-based locks; locks held by destroyed objects no longer count; loading a save or starting a new game clears every lock and re-enables movement input (`StartupMenu.ResetPlayerControl`); held directions are dropped when the window regains focus. The tester's exact sequence was not reproduced. |
| Ampere "works" on the ice-trial relic | It already did nothing (the relic only takes Glaciate), but the feedback was wrong: input casts only look for objects the selected spell can target, so casting beside the relic said "No spell target within reach". When something nearby takes a different spell, the cast now says so and names it: "Ampere cannot change this object. Try Glaciate instead." |
| Spell keys skip 3 | Lost Echoes held slot 3 but is usually learned last, so the wheel showed 1, 2, 4, 5, 6 for most of the game. Keys are now numbered by position among the spells the player has, in wheel order (`SpellCaster.KeyActionFor(slot)`), so they always run 1, 2, 3 with no gap. This reverses the October "stable hotkey" decision (#48): learning a spell can shift the keys of the spells after it on the wheel, and a custom binding now follows the slot rather than the spell. |

## Wayfinding

- **Quest arrow** (`QuestGuidanceHud`, fed by `QuestHintOverlay.CurrentWaypoint`): a gold marker over the person to talk to, or an arrow at the screen edge. A person or quest area elsewhere routes to the door that leads there through the areas' portal links. Puzzle pieces are deliberately never marked. The off-screen direction comes from the camera's ground-plane axes, since projection flips for targets behind the camera.
- **Minimap and map:** a north-up minimap in the bottom-right corner; M opens the whole area with every door labelled by its destination.
- **Directions:** the Ice Trial is on the east edge of campus, not the "lower edge" (which sent testers to the Library). Kit's tour and the dormitory hint no longer use "above"/"below", which change meaning with the camera angle.
- **Kit's tour pans to each building** as it is named: dialogue node metadata `cameraFocus` (`area:<map id>` or `npc:<npc id>`), handled in `DialogueOverlay.FrameCamera`. Reduced Motion keeps the camera still.

## Quests, rewards and hand-holding

- The hint card shows who a quest is for and lists the other quests in progress; clicking one tracks it.
- Room-key progress ("3 of 6 favours done") on the explore card and in a new quest-complete banner, which also names the spell learned.
- One-time tips (saved `tip.seen.*` flags): the first arrow, the first quest and the first spell.

## Feel

- Holding a diagonal into a wall slides along it (not on ice).
- Camera follow lag 0.15 s → 0.07 s.
- Vegetation reveal: the original look, slightly smaller (radius 0.34, feather 0.16; see [player-occlusion-reveal.md](../player-occlusion-reveal.md)).
- Rotara dais spread to twice its spacing ([quest-orio-patch-notes.md](quest-orio-patch-notes.md)).
- West Forest: `ForestTreeSpacingTool` removed the 35 painted trees whose canopies reached over the trails and thinned 23 more crowding them (949 kept).
- Campus plaza: `CampusPlazaGroundTool` (Lumenfall > World > Rebuild Campus Plaza Collision) replaces the hand-placed decoration, tree and fountain tiles around the Library and fountain (cells x 19..50, z 37..56), several of which sat a tile away from the lamp or bench they stood for, with blockers taken from the props themselves: lamps, benches, tables, signs and the fountain block what they cover, trees only their trunk. Buildings, the outer wall and NPC blockers are kept, and it refuses to apply if a portal or reachable cell would be cut off. Run requested through `Library/lumenfall-campus-plaza.request`.
- West Forest off-trail walking: `WestForestOpenGroundTool` (Lumenfall > World > Open West Forest Undergrowth, with a Preview item) deactivates the invisible trail fences and blocks only tree and rock trunk cells, the stream and the academy wall, gate, pillars and bench. Trail, portal, spawn and NPC cells are never blocked, and it refuses to apply if any trail cell would be cut off. A run is requested through `Library/lumenfall-open-west-forest.request` (Library, because the Editor empties Temp on start-up, which dropped the first request); it applies on the next script reload in edit mode.

## Art

The AI-generated dialogue frames and spell icons were replaced by art drawn in code (`DialogueFrameArt`, `SpellIconArt`, on `ProceduralArt.cs`); see [dialogue-art-provenance.md](../dialogue-art-provenance.md). After two rejected designs (flat plum-and-gold plates, then rune stones), the startup-menu buttons became astrolabe plaques (`MenuButtonArt`), chosen from mock-ups to match the menu's night-glass panels and Weave threads. Labels are live text.

## Time scale

`StartupMenu` pauses the game by setting `Time.timeScale` to 0 and restores the value it found on opening. In the Editor the time scale survives between play sessions, so a menu interrupted while paused (here by a script reload during play) left 0 behind, and the next menu restored it: the player could start a step but never finish one. A menu that opens on a stopped clock now restores 1.

## Tests

Added for this round and for `e0aa5d0` (the West Forest expansion and camera realignment, which shipped with one layout test and none for the camera):

- New EditMode fixtures: `PlaytestFeedbackTests` (movement locks, the north-gate lock bug, the menu's time scale, door routing, quest banners, tips), `CameraFollowTests` (due-south view, follow lag, peek limits and return), `WestForestExpansionTests` (negative-cell saves, Orio's clearing) and `ProceduralArtTests` (shapes, frames, icons, menu buttons).
- Extended: `QuestHintOverlayTests` (waypoints never on puzzle pieces, side quests, room-key progress), `SpellWorldStateTests` (feedback naming the right spell, gap-free spell keys), `LayOfTheLandTests` (the tour's camera focus) and `AuthoredLayoutTests` (open forest ground replaces the fenced-trail check, dais socket spacing, plaza lamps and campus doors).
- New PlayMode fixture `WallSlidePlayModeTests`.
- The six tests broken by the spell-tag gate now tag their relics; the relay case asserts the gate itself.
- `StartupMenu.TimeScaleToRestore` was extracted so the time-scale rule can be tested; `AssemblyInfo.cs` grants the EditMode and World PlayMode test assemblies internal access.
- Totals: 398 EditMode and 24 batch PlayMode cases in `tests/gameplay_suites.json`, `tests/README.md` and the Python harness, whose hard-coded 372 had been stale since `e0aa5d0`.

The plaza tool was revised after its first run blocked 50 extra cells (bounding boxes of angled benches, a static character model, two puzzle objects): props now block only the cells under their lowest metre of geometry, and only named scenery counts. The rerun opened 14 cells and blocked 8.

## Verification

A real Unity 6000.4.10f1 batch run compiled every script without errors, and the forest tool ran there. The new art was rendered outside Unity and inspected.

EditMode (`-testCategory !RequiresGraphics`, with a temporary company/product identity and the original ProjectSettings bytes restored afterwards): **367 of 373 passed**. The six failures come from the spell-tag gate added in `8e47823` ("Reported bug fixing"), after the October validation: `Cast` now requires the spell's tag on the target, and these tests register relics without it (`SpellWorldStateTests` Ampere, Glaciate, Weave endpoint and repeated-Weave feedback; `QuestHintOverlayTests.SettingsRemainReachable…`; `StringThemTogetherTests.BoardRelicsTakePower…`). They fail before reaching anything changed here, but a clean `main` run was not made to confirm it. Either the tests need their relics tagged or the gate needs revisiting.

PlayMode and graphics suites were not run. Nothing has been play-tested: the arrow, map, banner and tip layout; the dialogue frames in the live UI; the tour pans; the dais and forest visuals; and the tester's alt-tab route all need a manual pass.
