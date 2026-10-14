# Cinna Cirra: Cold Feet, Warm Idea implementation

Cold Feet, Warm Idea is now an ice-sliding puzzle in its own room, `C03.IceTrial`. It started from the "ICE puzzle idea 2" top-down sketch and was redesigned on 2026-10-14 with open edges, crystal pillars, snow patches and nine switches. It replaces the earlier campus slide target `trial.cinna.ice`. The room follows the Katrina garden recess pattern: a runtime campus portal, a room component on the area prefab root, and the quest NPC spawned from their catalog record. Cinna Cirra teaches Glaciate inside the room.

## Player flow

1. The portal on the campus path at `(47,45)` leads to the ice trial. Like the recess entrance, it is open whenever campus is loaded, because Out of Alignment carries its relic in through it. Once that relic is on the pedestal and the player reports back, Cinna leaves the terrace behind a fade and waits in the lobby; the player walks back through this portal (see [Out of Alignment](quest-out-of-alignment.md)).
2. Cinna Cirra stands in the trial's lobby at `(8,1)`. Accepting the quest and finishing their lesson lends Glaciate (`spell.lesson.Glaciate`) while the quest is Active.
3. The pool is open water and blocks movement. Casting Glaciate on the frost relic `relic.cinna.frost` at `(3,1)` freezes it. This is the orientation relic carried in with Vis during Out of Alignment: the pedestal is tagged `vis-stand:relic.cinna.orientation`, and the relic only takes spells once `vis.delivered.relic.cinna.orientation` is set. It is tagged `anchored`, so it freezes in place rather than sliding. Casting Glaciate on it again thaws the pool back to water; a player standing in the pool is first moved out to the entrance, and the thaw is refused while the player is mid-step.
4. The player enters the pool through the gap in the south wall at `(5,2)` and slides between switches. The rules follow Pokémon Diamond and Pearl's Snowpoint Gym:
   - There is no steering mid-slide.
   - A slide stops on a switch, on a snow patch, at the entrance, or in front of a crystal pillar or the south wall.
   - Only four directions are allowed on ice.
   - Holding a direction does not start another slide; the player presses again.
5. The west, north and east edges of the pool are open over a chasm. A slide that carries the player off one is a fall: the player drops with the screen fading to black, reappears on the entrance tile, and every switch goes dark ("You slip into the chasm. Every switch goes dark."). After the seal has opened, a fall only sends the player back.
6. Landing on a switch toggles it: lit, then dimmed on the next landing. Landing on the seal `switch.cinna.ice.final` on the east ledge at `(11,8)` lights it only when all nine switches are lit. The switches then hold until a reset.
7. `cinna_cold_feet_warm_idea_monitor` completes the quest when the nine switches and the seal are powered. Cinna's success line `cinna_ice_trial_solved` plays, and the quest's `spellReward` teaches Glaciate permanently.
8. R resets the switches and the seal and returns the player to the entrance. The pool stays frozen. The landing stone at the spawn `(5,0)` returns to campus.

## Layout

The area is 12 x 14 cells. Rows 0–1 are the lobby, and the 10 x 10 pool is cells `(1,3)`–`(10,12)`. The south wall at row 2 has the entrance gap `(5,2)`. The other three sides are `ice-drop` strips (`drop.cinna.ice.west`, `.north`, `.east-south`, `.east-north`) with the seal ledge `(11,8)` between the east ones.

```
13
12  ........#.     # crystal pillar (blocks)
11  ......7...     o snow patch (stop, no toggle)
10  .8....o.6.     1-9 switches, numbered in the order the route below lights them
 9  ..........     > seal on the east ledge
 8  ......9...>    E entrance, = south wall
 7  .#..5....#     blank: open edge over the chasm
 6  .4.oo....1
 5  ..........
 4  ..........
 3  .3.......2
 2 =====E======
   012345678901
```

| Kind | Ids and cells |
| --- | --- |
| Switches | `.01` `(10,6)`, `.02` `(10,3)`, `.03` `(2,3)`, `.04` `(2,6)`, `.05` `(5,7)`, `.06` `(9,10)`, `.07` `(7,11)`, `.08` `(2,10)`, `.09` `(7,8)` |
| Pillars (`ice-pillar`) | `pillar.cinna.ice.01` `(2,7)`, `.02` `(9,12)`, `.03` `(10,7)` |
| Snow patches (`ice-stop`) | `stop.cinna.ice.01` `(4,6)`, `.02` `(5,6)`, `.03` `(7,10)` |

The layout was chosen by search over candidate placements, then checked by a breadth-first search over every (cell, switch-state) pair that ignores falls, since a fall only resets progress:
- The shortest solution is 17 slides, and there are 10 of that length. Within two extra slides there are 12 distinct orders of switch landings.
- The first slide from the entrance stops safely on the snow patch at `(5,6)`.
- Removing the snow patch at `(5,6)` or the pillar at `(10,7)` makes the room unsolvable; removing the pillar at `(2,7)`, the patch at `(4,6)` or the patch at `(7,10)` changes the shortest length.

One shortest route, which lights the switches in their numbered order: up, right, down, left, up, right, right, up, right, up, up, left, down, left, right, down, right.

## Runtime pieces

- `GridWorld.SetIce`/`IsIce` and the slide in `GridMover`, `GridPlayerInput` and `GridMoverPresentation` are general purpose; the [GridMovement README](../../Lumenfall/Assets/_Project/Systems/GridMovement/README.md) documents the rules.
- `CinnaIceTrialRoom` (on `Prefabs/C03.IceTrial.prefab`) builds placeholder geometry from `World/Areas/C03.IceTrial.asset`:
  - It builds the floor, the south walls, the pool (one `GridObstacle` for the open water), the crystal pillars (each with its own `GridObstacle`), the snow patches, switch plates, the seal and the relic's pedestal (`StandHeight` 0.6). Pillar and snow-patch cells are left out of the ice; `ice-drop` cells are walkable and have no floor. `CinnaAlignmentRelic` draws the relic itself on the pedestal once delivered, tinted icy when frozen.
  - It spawns Cinna from `cinna_cirra_trial` with the NPC_Chibi animator, as Katrina was once placed in the recess.
  - It builds the return portal on the landing stone.
  - It registers the relic, switches and seal with `WorldStateService` on every activation, so saved freeze and switch states restore. The relic registers as spell-compatible only once delivered (`RelicDelivered`), and becomes compatible as soon as the delivery lands while the room is loaded.
  - On `StateChanged` it disables the water obstacle and marks the pool cells (minus switches) as ice while the relic is frozen. On a thaw it moves a player standing in the pool (`poolArea`) to the entrance before the water obstacle comes back, and announces the thaw. It clears its ice in `OnDisable`, because the `GridWorld` is shared.
  - When the player completes a move onto a drop cell, it starts the fall:
    - it locks movement and drops the player 3.2 units over 0.55 s while an IMGUI black overlay fades in;
    - at black it resets the switches (unless the seal is open) and teleports the player to the entrance;
    - it holds for 0.25 s and fades back in over 0.45 s.

    A player found idle on a drop cell, for example after loading a save made mid-fall, falls at once. Leaving the room mid-fall finishes the fall immediately.
  - Its static `Land`, `ResetSwitches` and `DescribeStep` hold the trial rules and the hint text.
  - It draws an "Ice Trial" guide panel while the lesson or learned flag is set, plus short landing feedback.
- `CinnaIceTrialEntrance` builds `P.CAMPUS.ICETRIAL` at campus `(47,45)` at runtime, mirroring `KatrinaRecessEntrance`. The room's landing stone is `P.ICETRIAL.CAMPUS`. Neither portal is baked into a prefab, so the PlayMode baked-route count stays at 10.
- `SpellCaster.FreezeSlide` returns after freezing an object tagged `anchored`, and `ThawAnchored` unfreezes a frozen anchored object on a second Glaciate.
- `QuestHintOverlay` sends players on other maps to the portal and uses `DescribeStep` inside the trial.

## Art

The room, the relic and the campus entrance read optional art from `Systems/Quests/Resources/CinnaIceTrialArt.asset` (`CinnaIceTrialArt`). Any slot left empty falls back to the coloured primitives described above, so EditMode tests and the rules do not depend on it.

- **Sources:** CC0 models from Poly Pizza (iPoly3D crystals; Quaternius Big Crystal, Rock Snow, Pedestal, Column and Column Round) in `Assets/ThirdParty/PolyPizza`, and CC0 ambientCG textures (Ice002, Ice003, Snow013) in `Assets/ThirdParty/ambientCG`. Both folders have a README listing each file's source and licence.
- **Build tool:** `Lumenfall/World/Build C03 Ice Trial Art` (`Editor/C03IceTrialArtTool.cs`):
  - It creates the materials, prefabs and switch rune texture under `Art/Environment/Puzzle Elements/Ice Trial`. The thawed stone uses the FantasyCastle `ground_2` textures.
  - It fills `CinnaIceTrialArt`.
  - It rebuilds `[Ice Trial Dressing]` in `C03.IceTrial.prefab`. The dressing holds columns on wall cells, braziers on the four corner columns, crystal banks and snowy rocks outside the walls, cold lights, snowfall and pool mist. Nothing in it sits on a walkable cell.
- **Room shape:**
  - The pool sits in a basin 0.4 below the floor, with dark stone sides. The water drifts while thawed, and the ice sheet sits just above it.
  - The walls reach down into the basin and carry a stone coping.
  - The room stands on dark cave ground, ringed by cliffs (Polytope `PT_Generic_Rock_01`) and pooled mist on the west, north and east.
  - The exit back to campus (return portal `P.I.00` on the spawn tile `(5,0)`) follows the other rooms' exits: a low stone wall runs along the lobby's south edge with a one-tile gap at the portal, and a runner rug leads from the pool entrance to the gap.
  - The south wall is a low parapet (0.4) with short posts, so the camera can see the pool row behind it. Beyond its west end are a desk with books, a scroll and a candle that stays lit; beyond its east end are a bookcase and a chest. All of this is flat or outside the map, so no walkable cell changes.
- **Thawed and frozen looks (`CinnaIceTrialAmbience`):**
  - Until the relic is frozen, the room is a stone hall with open water, and only the brazier flames and warm lights are on.
  - A live Glaciate sends a frost front out from the pedestal at 9 cells per second. As it passes:
    - the ice sheet and a frost dusting over the floor are revealed by the `Lumenfall/Ice Trial Frost` shader (`IceTrialFrost.shader`), with a ragged, glowing edge and world-space texture coordinates, so there are no seams;
    - ice climbs the walls, crystal spikes grow along their tops and icicles hang from their faces;
    - the crystal banks and rocks grow in;
    - the braziers go out, frost crowns grow in their place, and warm lights give way to cold ones;
    - snowfall, pool mist, ice glints and floating motes start;
    - a cold colour grade (`VP_IceTrial_Frozen`: saturation, white balance, bloom, vignette) fades in through a global volume in the area.
  - When the front finishes, the `Ice Reflections` probe re-renders, so the ice reflects the current look.
  - A thaw runs the same front in reverse. Loads, save restores and returning to the room snap to the end state.
  - The project's quality levels turn real-time reflection probes off. The room turns them on while it is active and restores the setting when it is not; everything else lives in the area.
- **Effects (`CinnaIceTrialEffects`):**
  - The Glaciate impact is a flash of light at the relic, a frost ring along the floor and a burst of frost. A freeze also gives a short camera shake, applied only while the camera renders.
  - The player throws frost spray while sliding and a puff where the slide stops.
  - While the room is frozen, the player's breath mists every few seconds.
- **Switches (`CinnaIceTrialSwitchVisuals`):**
  - Each switch is a stone disc with a cut-out rune ring: frosted grey when off, glowing cyan when lit.
  - A live toggle bounces the rune and throws a frost burst. A lit switch grows a beam of light to the crystal outside the seal gap at `(12,8)`, and that crystal brightens a step for every lit switch.
  - When the seal opens live, the crystal flares with a gold light and sparks, the beams pulse, and gold runs back from the seal across every switch and beam. A restored open seal is simply gold.
- **Other runtime art:**
  - The pedestal is a column cut to `StandHeight`.
  - The relic is a stone base with a crystal. Only the crystal takes `CinnaAlignmentRelic`'s state colours, as tint and emission.
  - The campus entrance gains a frost patch and four small crystals inside its own tile.
- **Not yet verified:** none of this has been checked in Play Mode by hand. `CinnaIceTrialTests` cover the rules only, not the visuals.

## Data changes

- `NpcCatalog.asset` and `npcs.json`: `cinna_cirra_trial` moves to `C03.IceTrial` `(8,1)` with no layer symbol, and the catalog prefab is now `Cinna Cirra.fbx`. Their dialogue rules and Out of Alignment visibility gate are unchanged.
- `campus.prefab`: the baked trial Cinna instance and the `trial.cinna.ice` object are removed, with their parents' child references. `campus.asset` and the legacy `Maps/campus.json` drop the `trial.cinna.ice` object and its target. `campus.asset` lists the new campus portal asset, and SampleScene's `availableAreas` lists `C03.IceTrial`.
- `quest_npc_dialogue.json`: Cinna's dialogue gains `spellLesson` `Glaciate` and `spellRewardQuestId` metadata, plus lesson, reminder and completion text for the trial. The quest description and completion-event description are updated, and `cinna_ice_trial_solved` is added.
- `quest_puzzle_definitions.json`: the monitor uses `mapId` `C03.IceTrial` with two `requiredPowered` targets (the nine switches, then the seal) and the success dialogue.
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
- `IceTrialPortalSlidesFollowASolutionRouteAndCompleteColdFeet` (PlayMode): enters through the campus portal, freezes the relic, slides the full route, and checks every switch, the quest and the Glaciate flag.

Pending manual checks:

- Unity import of the new area, prefab and portal assets.
- Slide feel and speed, and camera follow during long slides.
- Cinna's model, animator and facing.
- Feedback and guide placement beside other HUD panels.
- Save and load mid-trial.
- R reset while standing on the seal.
- Placeholder primitive art is to be replaced.
