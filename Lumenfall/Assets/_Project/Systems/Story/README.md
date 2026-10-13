# Story

Scripted story scenes: what starts them, and any characters that only exist for them. The scenes are cutscenes (see [the Cutscenes README](../Cutscenes/README.md)). Their data lives in this folder's `Resources/Cutscenes` and `Resources/GameEvents`.

## Late to orientation (forest to campus)

`LateToOrientationScene` runs the walk from the forest to the academy after the opening.

- **Kepler waits at the gate.** Until the scene has played, Prof. Kepler stands in `westForest` at `(19,46)`, beside the gate to campus (portal `P.F00` at `(17,48)`). Kepler is the `prof_kepler` record in `NpcCatalog.asset`, with the `Teacher_01` prefab and no dialogue of their own. They are not baked into the area prefab. The scene spawns them through `StoryNpc` (see below).
- **Trigger.** The first time the player stands on row y 22 of the forest or beyond it (around `(18,22)`), with no cutscene, dialogue or menu open, the cutscene `late_to_orientation` plays. The step onto the row counts as reaching it, so the scene starts at once rather than when the player stops. Every cutscene locks movement the moment it starts, so the player halts on that cell. It is play-once and unskippable. The gate is further on, so the row cannot be walked around.
- **The scene:**
  1. Kepler shouts from the gate (speaker "Distant voice", since Kepler is far off and the two have not met): orientation started ages ago, and the player should come over quickly. The player's inner voice wonders whether the afterlife has an orientation, and decides it is probably best to do what they say.
  2. The game walks the player to two cells in front of Kepler (`(19,44)`).
  3. Kepler says they are already late to orientation, introduces themselves and asks for the player's name. The name prompt opens: type a name, or leave it blank for Sol (see [the Player README](../Player/README.md)).
  4. Kepler marks them down by name ("{player}. There, you're on the list.") and tells them to get in quickly. The inner voice agrees: through the gate it is.
  5. The flag `story.orientation.kepler_met` is set. The game walks the player onto the gate. The screen fades to black, they go through to campus (arriving at `P.C00`), and it fades back in. Control returns on campus.
- **Afterwards.** Once the flag is set, this Kepler is removed as soon as the cutscene ends and is never spawned again. Kepler is meant to return elsewhere later with their own placement.
- **Saves.** Autosave waits during the scene, and the flag is saved with the GameEvents state. A save made in the forest beyond row 22 before this scene existed plays it as soon as it loads. Kepler and their blocker are removed while the startup menu is open, because saves load then, and a blocker on a saved player cell made the world provider reject the whole save. If a save leaves the player on Kepler's cell, Kepler appears once the player steps off it.

The shout has no portrait, because Kepler is far away when it plays. Once the player reaches Kepler and they introduce themselves, the speaker is "Prof. Kepler". That matches the catalog name, so the dialogue box shows Kepler's portrait.

## Lay of the land (arriving on campus)

`LayOfTheLandScene` runs the arrival on campus, straight after Kepler's scene.

- **Kit.** Kit is the student at the campus entrance: `campus_student`, renamed from "Campus student" in the catalog, the campus prefab, `npcs.json` and the journal. Kit stands at `(24,13)` in the campus prefab; the catalog's `(32,21)` is out of date, so the scene uses Kit's live position.
- **The call.** On campus, once `story.orientation.kepler_met` is set and `story.orientation.kit_called` is not, with no cutscene, dialogue or menu open, the cutscene `kit_call` plays. In practice that is the moment the player comes through the forest gate. Kit calls the player over, the player's inner voice notes that at least this stranger is smiling, and the flag `story.orientation.kit_called` is set. Control returns, and the player walks over themselves. Nothing walks them.
- **The tour.** Once the call has played, the first time the player talks to Kit (the talk key, `E` by default, within Kit's usual talk range), the cutscene `kit_lay_of_the_land` plays instead of Kit's usual conversation. The scene runs before NPC input, so it takes that press first. After the tour, talking to Kit opens Kit's usual choice-based courtyard conversation again. Both cutscenes are play-once and unskippable.
- **The tour's content:**
  1. Kit introduces themselves and gives the lay of the land: the courtyard with its fountain in the middle (the place to head back to if lost), the Main Academy Building (reception, the main portal, the staff), the Arcane Workshop (relics and machines), the Dormitory (student rooms and common spaces), the Library, building signs by every door, and the forest back through the gate. The directions are landmarks rather than left and right, because the camera's diagonal makes screen directions differ from the map, and older map data disagree about which side each building is on.
  2. Kit says the player missed the orientation ceremony, and that the headmistress, Professor Bann, is still by the fountain: go and speak to her right away. The flag `story.orientation.kit_met` is set and control returns. There is no hint card or marker.
- **Prof. Bann** is the `prof_bann` record (`Teacher_03`, campus `(26,42)`, by the fountain). She is spawned through `StoryNpc` from the moment `story.orientation.kepler_met` is set, and the player talks to her with E.
  - Her conversation `bann_orientation`: the player has missed orientation entirely, and she will organise a student guide to go over it with them tomorrow. For now they should report to their dorm and get their room sorted. Today is for exploring and speaking to fellow students, so she suggests the room first, then whatever they please. The player's inner voice finds the afterlife weirdly organised. Her last line sets `story.orientation.bann_met`. She does not say where the dorm is; there is no hint card or marker.
  - Talking to her again gives the short `bann_reminder` ("Your dorm first, {player}…") instead. Her first node triggers the repeatable event `bann_remind_if_met`, which swaps in the reminder once the flag is set.
- **Kit's everyday conversation** (`ambient_courtyard_student`, after the tour): "Hey, {player}! Settling in all right?", with two choices. "Can you remind me of the campus buildings and areas?" repeats the landmark tour: the fountain, the Main Academy Building, the Arcane Workshop, the Dormitory and Library, the forest back through the gate, and the building signs. "Bye!" gets "See you around!". Kit's two one-line courtyard chats (`_2`, `_3`) are unchanged, apart from the speaker now being Kit.

## Ambient cast

`AmbientCast` keeps background students and staff on the maps, so the academy feels lived in until the dorm and later story content exist. Each is an NPC catalog record, spawned through `StoryNpc` whenever the player is on their map, with a short placeholder conversation in `Resources/GameEvents/ambient_cast.json` to be rewritten later.

| Character | Catalog id | Prefab | Map and cell |
| --- | --- | --- | --- |
| Prof. Voltaire | `prof_voltaire` | `Teacher_02` | Library, `B04.Ground` `(7,17)` |
| Vex | `vex` | `Student_04` | Library, `B04.Ground` `(2,11)` |
| Luna | `luna` | `Student_01` | Campus, `(22,77)` |
| Juno | `juno` | `Student_03` | Campus, `(43,42)` |
| Rowan | `rowan` | `Student_02` | Training Centre, `B01.Ground` `(6,4)` |
| Moss | `moss` | `Student_05` | West forest, `(25,32)` |

Cells were set by hand on 2026-10-14. Marigold and Thistle (`Receptionist_01`, `_02`) and Odette (`Student_06`) are kept back for later. The older baked forest student (`forest_student`, at around `(16,39)` in the West Forest prefab) is now called Grace, in the catalog, the prefab, `npcs.json`, the journal and their three forest conversations. Moss stands beyond the forest's gate-scene trigger row, so the player meets Moss on a return visit. Each cast member has a journal entry.

## Story-only characters

`StoryNpc` spawns a character from their NPC catalog record (prefab, map and cell) instead of baking them into an area prefab, in the way the ice trial spawns its Cinna. Each frame, the owning scene says whether the story wants them present.

- They appear on their map as a child of its area object, facing south, with a `DynamicNpc` and a one-cell `GridObstacle`. They switch off with the area, and are removed when they leave their map or are no longer wanted.
- They are removed while the startup menu is open, because saves load then, and a blocker on a saved player cell made the world provider reject the save.
- If the player stands on their cell, they wait until the player steps off.
- If their authored cell is blocked (a tree, a wall, the fountain) or is a doorway, they stand on the nearest free cell within four cells instead. The interior and forest layouts only exist in the area prefabs, so authored cells cannot all be checked ahead of time.

## Verification

`LateToOrientationTests`, `LayOfTheLandTests` and `AmbientCastTests` (EditMode) check the cast's prefabs, maps and conversations, Kit's and Bann's catalog records, when Kit's scene plays, its step order and its conversations. `LateToOrientationTests` also check the trigger row, Kepler's catalog record, the scene's step order (call, walk beside Kepler, ask for a name, mark down, flag, walk onto the gate, enter it) and the three conversations. They compile but have not yet been run in Unity.

Manual validation (not yet executed): check that the shout plays at row 22 and the walk finds a way through the real forest. A walk that is blocked for three seconds, or has no path, places the player at its destination instead. Also check that Kepler's model, facing and portrait look right; that the name prompt works with the keyboard; that the player arrives on campus; and that Kepler is gone on returning to the forest.
