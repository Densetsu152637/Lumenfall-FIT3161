# Story

Scripted story scenes: what starts them, and any characters that only exist for them. The scenes are cutscenes (see [the Cutscenes README](../Cutscenes/README.md)). Their data lives in this folder's `Resources/Cutscenes` and `Resources/GameEvents`.

## Late to orientation (forest to campus)

`LateToOrientationScene` runs the walk from the forest to the academy after the opening.

- **Kepler waits at the gate.** Until the scene has played, Prof. Kepler stands in `westForest` at `(19,46)`, beside the gate to campus (portal `P.F00` at `(17,48)`). He is the `prof_kepler` record in `NpcCatalog.asset`, with the `Teacher_01` prefab and no dialogue of his own. He is not baked into the area prefab. The scene spawns him as a child of the forest's area object, with a one-cell `GridObstacle`, so he and his blocker switch off whenever another area loads.
- **Trigger.** The first time the player stands on row y 22 of the forest or beyond it (around `(18,22)`), with no cutscene, dialogue or menu open, the cutscene `late_to_orientation` plays. It is play-once and unskippable. The gate is further on, so the row cannot be walked around.
- **The scene:**
  1. Kepler shouts from the gate (speaker "Distant voice", since he is far off and they have not met): orientation started ages ago, and the player should come over quickly. The player's inner voice wonders whether the afterlife has an orientation, and decides it is probably best to do what they say.
  2. The game walks the player to `(19,44)`, two cells in front of Kepler.
  3. Kepler says they are already late to orientation, introduces himself and asks for their name. The name prompt opens: type a name, or leave it blank for Sol (see [the Player README](../Player/README.md)).
  4. Kepler marks them down by name ("{player}. There, you're on the list.") and tells them to get in quickly. The inner voice agrees: through the gate it is.
  5. The flag `story.orientation.kepler_met` is set. The game walks the player onto the gate. The screen fades to black, they go through to campus (arriving at `P.C00`), and it fades back in. Control returns on campus.
- **Afterwards.** Once the flag is set, this Kepler is removed as soon as the cutscene ends and is never spawned again. Kepler is meant to return elsewhere later with his own placement.
- **Saves.** Autosave waits during the scene, and the flag is saved with the GameEvents state. A save made in the forest beyond row 22 before this scene existed plays it as soon as it loads. Kepler and his blocker are removed while the startup menu is open, because saves load then, and a blocker on a saved player cell made the world provider reject the whole save. If a save leaves the player on Kepler's cell, he appears once they step off it.

The shout has no portrait, because Kepler is far away when it plays. Once the player reaches him and he introduces himself, his speaker is "Prof. Kepler". That matches his catalog name, so the dialogue box shows his portrait.

## Verification

`LateToOrientationTests` (EditMode) check the trigger row, Kepler's catalog record, the scene's step order (call, walk beside Kepler, ask for a name, mark down, flag, walk onto the gate, enter it) and the three conversations. They compile but have not yet been run in Unity.

Manual validation (not yet executed): check that the shout plays at row 22 and the walk finds a way through the real forest. A walk that is blocked for three seconds, or has no path, places the player at its destination instead. Also check that Kepler's model, facing and portrait look right; that the name prompt works with the keyboard; that the player arrives on campus; and that Kepler is gone on returning to the forest.
