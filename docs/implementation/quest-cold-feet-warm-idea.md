# Cinna Cirra: Cold Feet, Warm Idea implementation

Cold Feet, Warm Idea is now an ice-sliding puzzle in its own room, `C03.IceTrial`, built from the "ICE puzzle idea 2" top-down sketch. It replaces the earlier campus slide target `trial.cinna.ice`. The room follows the Katrina garden recess pattern: a runtime campus portal, a room component on the area prefab root, and the quest NPC spawned from their catalog record. Cinna Cirra teaches Glaciate inside the room.

## Player flow

1. The portal on the campus path at `(47,45)` leads to the ice trial. Like the recess entrance, it is open whenever campus is loaded, because Out of Alignment carries its relic in through it. Once that relic is on the pedestal and the player reports back, Cinna leaves the terrace behind a fade and waits in the lobby; the player walks back through this portal (see [Out of Alignment](quest-out-of-alignment.md)).
2. Cinna Cirra stands in the trial's lobby at `(8,1)`. Accepting the quest and finishing their lesson lends Glaciate (`spell.lesson.Glaciate`) while the quest is Active.
3. The pool is open water and blocks movement. Casting Glaciate on the frost relic `relic.cinna.frost` at `(3,1)` freezes it. This is the orientation relic carried in with Vis during Out of Alignment: the pedestal is tagged `vis-stand:relic.cinna.orientation`, and the relic only takes spells once `vis.delivered.relic.cinna.orientation` is set. It is tagged `anchored`, so it freezes in place rather than sliding. Casting Glaciate on it again thaws the pool back to water; a player standing in the pool is first moved out to the entrance, and the thaw is refused while the player is mid-step.
4. The player enters the pool through the gap in the south wall at `(5,2)` and slides between switches. The rules follow Pokémon Diamond and Pearl's Snowpoint Gym:
   - There is no steering mid-slide.
   - A slide stops on a switch, at the entrance or in front of a wall.
   - Only four directions are allowed on ice.
   - Holding a direction does not start another slide; the player presses again.
5. Landing on a switch toggles it: lit, then dimmed on the next landing. Landing on the seal `switch.cinna.ice.final` in the east-wall gap at `(11,8)` lights it only when all twelve switches are lit. The switches then hold until a reset.
6. `cinna_cold_feet_warm_idea_monitor` completes the quest when the twelve switches and the seal are powered. Cinna's success line `cinna_ice_trial_solved` plays, and the quest's `spellReward` teaches Glaciate permanently.
7. R resets the switches and the seal and returns the player to the entrance. The pool stays frozen. The landing stone at the spawn `(5,0)` returns to campus.

## Layout

The area is 12 x 14 cells. Sketch cell `(column, row from top)` maps to `(column + 1, 12 - row)`, so the 10 x 10 sketch pool is cells `(1,3)`–`(10,12)`. Rows 0–1 are the lobby, and the pool is enclosed by walls with two gaps: the entrance `(5,2)` and the seal `(11,8)`.

| Switch | Cell | Sketch | Switch | Cell | Sketch |
| --- | --- | --- | --- | --- | --- |
| `.01` | `(2,12)` | `(1,0)` | `.07` | `(3,6)` | `(2,6)` |
| `.02` | `(8,12)` | `(7,0)` | `.08` | `(6,6)` | `(5,6)` |
| `.03` | `(2,11)` | `(1,1)` | `.09` | `(3,5)` | `(2,7)` |
| `.04` | `(4,11)` | `(3,1)` | `.10` | `(7,5)` | `(6,7)` |
| `.05` | `(7,11)` | `(6,1)` | `.11` | `(3,3)` | `(2,9)` |
| `.06` | `(6,8)` | `(5,4)` | `.12` | `(8,3)` | `(7,9)` |

The bend in the sketch's lower-left line means "stop on the next switch immediately", so no extra stoppers were added. A breadth-first search over every (cell, switch-state) pair confirms the room is solvable. The shortest solution is the sketch's own route of 15 slides from the entrance: up, right, left, down, right, right, down, down, right, left, up, up, right, up, right.

## Runtime pieces

- `GridWorld.SetIce`/`IsIce` and the slide in `GridMover`, `GridPlayerInput` and `GridMoverPresentation` are general purpose; the [GridMovement README](../../Lumenfall/Assets/_Project/Systems/GridMovement/README.md) documents the rules.
- `CinnaIceTrialRoom` (on `Prefabs/C03.IceTrial.prefab`) builds placeholder geometry from `World/Areas/C03.IceTrial.asset`:
  - It builds the floor, walls, the pool (one `GridObstacle` for the open water), switch plates, the seal and the relic's pedestal (`StandHeight` 0.6). `CinnaAlignmentRelic` draws the relic itself on the pedestal once delivered, tinted icy when frozen.
  - It spawns Cinna from `cinna_cirra_trial` with the NPC_Chibi animator, as Katrina was once placed in the recess.
  - It builds the return portal on the landing stone.
  - It registers the relic, switches and seal with `WorldStateService` on every activation, so saved freeze and switch states restore. The relic registers as spell-compatible only once delivered (`RelicDelivered`), and becomes compatible as soon as the delivery lands while the room is loaded.
  - On `StateChanged` it disables the water obstacle and marks the pool cells (minus switches) as ice while the relic is frozen. On a thaw it moves a player standing in the pool (`poolArea`) to the entrance before the water obstacle comes back, and announces the thaw. It clears its ice in `OnDisable`, because the `GridWorld` is shared.
  - Its static `Land`, `ResetSwitches` and `DescribeStep` hold the trial rules and the hint text.
  - It draws an "Ice Trial" guide panel while the lesson or learned flag is set, plus short landing feedback.
- `CinnaIceTrialEntrance` builds `P.CAMPUS.ICETRIAL` at campus `(47,45)` at runtime, mirroring `KatrinaRecessEntrance`. The room's landing stone is `P.ICETRIAL.CAMPUS`. Neither portal is baked into a prefab, so the PlayMode baked-route count stays at 10.
- `SpellCaster.FreezeSlide` returns after freezing an object tagged `anchored`, and `ThawAnchored` unfreezes a frozen anchored object on a second Glaciate.
- `QuestHintOverlay` sends players on other maps to the portal and uses `DescribeStep` inside the trial.

## Data changes

- `NpcCatalog.asset` and `npcs.json`: `cinna_cirra_trial` moves to `C03.IceTrial` `(8,1)` with no layer symbol, and the catalog prefab is now `Cinna Cirra.fbx`. Their dialogue rules and Out of Alignment visibility gate are unchanged.
- `campus.prefab`: the baked trial Cinna instance and the `trial.cinna.ice` object are removed, with their parents' child references. `campus.asset` and the legacy `Maps/campus.json` drop the `trial.cinna.ice` object and its target. `campus.asset` lists the new campus portal asset, and SampleScene's `availableAreas` lists `C03.IceTrial`.
- `quest_npc_dialogue.json`: Cinna's dialogue gains `spellLesson` `Glaciate` and `spellRewardQuestId` metadata, plus lesson, reminder and completion text for the trial. The quest description and completion-event description are updated, and `cinna_ice_trial_solved` is added.
- `quest_puzzle_definitions.json`: the monitor uses `mapId` `C03.IceTrial` with two `requiredPowered` targets (the twelve switches, then the seal) and the success dialogue.
- `validate_out_of_alignment.py` checks that the trial Cinna is catalogued in `C03.IceTrial` and that campus bakes no copy.

## Verification

Executed:

- Offline Roslyn compiles against Unity 6000.4.10f1 references, with no errors:
  - runtime with `scripts/compile_runtime.ps1`;
  - EditMode tests with `scripts/compile_editmode_tests.ps1`;
  - the PlayMode test assembly, with an equivalent scratch response file.
- `validate_npc_dialogue.py` (6 quests, 17 dialogues, 10 events), `validate_runtime_world.py`, `validate_unity_project.py` and the Python unit tests pass.
- The Cinna-handoff section of `validate_out_of_alignment.py` passes. The script as a whole already failed at `f020fdd` on the alignment marker position, which this change does not touch.
- `validate_npc_layers.py`, `validate_garden_portals.py` and `validate_layouts.py` already fail on campus layer and map dimensions, which this change does not touch.

Written but not executed (the project was open in the Unity Editor):

- `CinnaIceTrialTests` (EditMode):
  - the pool is enclosed;
  - the sketch route lights every switch and ends on the seal;
  - no shorter solution exists;
  - the monitor, area target and catalog agree;
  - the lesson lends Glaciate and completion teaches it;
  - the anchored relic freezes in place;
  - the toggle, seal and reset rules hold.
- The updated Cold Feet case in `QuestHintOverlayTests`.
- `IceTrialPortalSlidesFollowTheSketchRouteAndCompleteColdFeet` (PlayMode): enters through the campus portal, freezes the relic, slides the full route, and checks every switch, the quest and the Glaciate flag.

Pending manual checks:

- Unity import of the new area, prefab and portal assets.
- Slide feel and speed, and camera follow during long slides.
- Cinna's model, animator and facing.
- Feedback and guide placement beside other HUD panels.
- Save and load mid-trial.
- R reset while standing on the seal.
- Placeholder primitive art is to be replaced.
