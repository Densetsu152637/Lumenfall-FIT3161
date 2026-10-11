# Cinna Cirra: Out of Alignment

Branch `codex/cinna-orientation-quests`, renamed on 2026-10-10 from `codex/quest-out-of-alignment` and started at `origin/main` baseline `8ceebfa`. It also carries Cold Feet, Warm Idea (see [its implementation notes](quest-cold-feet-warm-idea.md)), brought in from the local branch `codex/quest-cold-feet-ice-trial` (`f5c5821`). Out of Alignment teaches Vis as a lifting spell. The player carries an orientation relic from the C02 terrace into the ice trial and sets it on the pedestal there, where it becomes the frost relic that Glaciate freezes in the next quest. They then report back to Cinna Cirra, who goes ahead to the trial and asks the player to meet them there; a fade to black hides Cinna leaving, and the player walks over themselves.

## History

- **First version:** a rotation marker, `orientation.cinna.align`, had to be turned to ninety degrees at campus `(40,22)`. After the campus rework the baked marker sat inactive on Cinna's own tile in `campus.prefab`, so it never registered and the quest could not be finished in game. The inactive GameObject is still in the prefab, untouched to avoid reserialising it; no data or code refers to it.
- **Interim version (2026-10-10):** a relic was pushed one tile into a socket at `(23,52)`. It was replaced the same day by the lift-and-carry design below. Some of its animation work carries over: the anticipation dip, the shudder and bump on refusal, the idle wobble and the shockwave ring.

## Player flow

1. Talk to Cinna Cirra on the C02 terrace at `(22,51)` and accept. Finishing the accept or reminder node teaches Vis while the quest is Active.
2. The relic `relic.cinna.orientation` rests at `(23,53)` and blocks its tile. Stand within two tiles, select Vis and right-click (or press F) to lift it. It rises to hover in front of the player at chest height and follows them, swinging round to stay in front whichever way they face. Lifting, setting down and delivering show no text; the relic's animation is the feedback.
3. Carry it along the campus path to the ice-trial portal at `(47,45)`. To set it down on the way, face an open tile and cast Vis again. A ghost tile shows where it would land: green when allowed, red when not. On campus it may only land on walkable ground that is not a portal tile.
4. Entering any other area (a building, the forest) lets go of it, and it settles back at its last safe placement on campus. This happens because world state never moves while it is carried.
5. In the trial, face the empty pedestal at `(3,1)` and cast Vis. The relic floats onto it, the stand pulses and a shockwave ring spreads, and it locks in place. Anywhere else in the trial, placement is refused with a shudder.
6. Return to Cinna on the terrace and choose "The relic is on the trial stand." Cinna says they will go ahead and asks to meet at the ice trial. When the dialogue ends, the screen fades to black. While black, the quest completes, which hides the terrace Cinna and shows the trial Cinna in the trial lobby at `(8,1)`. The screen fades back in with the player where they stood. They walk through the portal at `(47,45)` and start Cold Feet, Warm Idea by talking to Cinna there.

While the quest is Active on campus, R near the relic lets go of a held relic and returns it to `(23,53)`. There is no on-screen reset box; Cinna's lines mention R.

## Runtime pieces

- **`SpellCaster`**
  - Vis on an object tagged `vis-lift` sets `HeldRelic` and records its home map and its `vis-destination:` map; world state is unchanged.
  - F (interact) also casts Vis and Glaciate, but only at objects tagged `vis` or `glaciate`, so it never acts on the board relics or gear that use F on their own maps.
  - With a relic held, right-click or F with Vis calls `TryPlaceHeldRelic` on the faced tile (`TryGetPlacementCell`; a diagonal facing keeps its horizontal part).
  - An object tagged `vis-stand:<relic id>` on that tile accepts the relic on any map. It sets the progression flag `vis.delivered.<relic id>` and marks the stand `aligned`.
  - Otherwise, only on the home map, the relic moves there through `WorldStateService.TryMoveObject`, after a portal check. That covers bounds, obstacles and the player.
  - `CanPlaceHeldRelicAt` makes the same checks for the preview.
  - `ReleaseHeldRelicOutOfReach` lets go of the relic when the current map is neither the home map nor the destination. A loaded save or a new game also lets go (`OnProgressionRestored`).
  - The events `VisLifted`, `VisPlaced`, `VisPlaceRefused` and `VisHoldReleased` drive the presentation.
  - Objects without `vis-lift` keep the in-place `vis-target:` turn.
  - Lifting, setting down and delivering show no text; refusals and an out-of-reach release still do.
  - The departure fade blocks casting (`InputBlockedByOverlay`).
- **`CinnaAlignmentRelic` (Systems/Quests)**
  - The loader calls `RegisterForMap` before `EndMapRebuild`. On campus it registers the relic from the area definition, unless it has been delivered, in which case it neither registers nor blocks. On a map with the relic's stand it records the stand tile.
  - It draws one relic visual on every map:
    - At rest, the relic wobbles and its notch glows.
    - When lifted, it dips and then rises to a hover 0.85 tiles in front of the player (their `SpellCaster.FacingDirection`) and 0.55 up, at chest height and clear of the character's hat, bobbing and turning slowly. Turning swings it round the player (`Mathf.LerpAngle`) rather than through them. Its position tracks the player exactly, without easing, so it stays ahead of a walking player instead of trailing back into the model's face.
    - While held, a dim mark shows the tile it still occupies, and the preview shows where it would land.
    - Placing it floats it down along a small arc.
    - A refused placement makes it shudder (wrong place) or bump toward the blocked tile.
    - Delivery floats it onto the pedestal (`CinnaIceTrialRoom.StandHeight`), then the relic pulses and the ring spreads.
    - On the pedestal it rests, tinted icy once frozen.
  - Loads, saves and releases snap without animating.
  - Its static `DescribeStep` feeds `QuestHintOverlay`.
- **`CinnaTrialDeparture` (Systems/Quests)**
  - Cinna's `report` node sets `cinna.trial.departure`. When that dialogue completes with the quest Active, it fades out over 0.6 s, holds black for 0.35 s and fades in over 0.6 s, locking movement throughout.
  - At black it clears the flag and triggers `complete_cinna_out_of_alignment`, so the terrace Cinna hides and the trial Cinna appears in the trial lobby. The player is not moved.
  - Closing the dialogue early, or any other dialogue ending, does not start it.
- **`CinnaIceTrialRoom`**
  - It builds only the pedestal, and registers `relic.cinna.frost` as spell-compatible once the relic is delivered. If the delivery lands while the room is loaded, it switches compatibility on straight away.
  - The relic look comes from `CinnaAlignmentRelic`.
- **`WorldStateService`:** `authoredPoses` keeps each object's authored cell and rotation, so `ResetAlignmentObject` can return a moved relic. Reset is refused while someone stands on the start tile.
- **Data**
  - `campus.json` and `campus.asset`: the relic has the tags `vis`, `vis-lift` and `vis-destination:C03.IceTrial`, rotation 0, and it blocks its tile. The campus target and the quest monitor for Out of Alignment were removed.
  - `C03.IceTrial.asset`: the pedestal gains `vis-stand:relic.cinna.orientation`.
  - `quest_npc_dialogue.json`: new quest text and lesson lines. The `report` choice (needs Active plus the delivered flag) and the `active` choice (needs the flag unset) are split, and a `report` node sets the departure flag. The Cold Feet text now refers to the carried relic.
- **Saves:** there is no schema change. Holding is never saved. The relic's last placement is campus world state, delivery is a progression flag, and completion is the quest state.

## Validation performed

On 2026-10-10:

- `scripts/compile_runtime.ps1` and `scripts/compile_editmode_tests.ps1` pass.
- The repository checks pass: `validate_out_of_alignment.py` (rewritten for the relic, its stand, Cinna's report and the meet tile), `validate_npc_dialogue.py` (6 quests, 17 dialogues, 10 events), `validate_runtime_world.py` and the 18 script unit tests.

A headless Unity `6000.4.10f1` EditMode run passed 102/115:

- all 33 `OutOfAlignmentTests`: lesson gating; authored data and stand; campus registration until delivery; lifting; open-ground placement; travel limits and the last-placement return; stand-only placement in the trial; save release; locked and overlay input; reset; report gating; the departure timing; and the relic animations;
- all 5 `CinnaIceTrialTests`;
- the updated Cold Feet hint test.

The 13 failures are the same tests that failed before this work, in `KatrinaBorrowedWorldTests`, `OrioPatchNotesTests`, `QuestDialogueLifecycleTests` (Gravitate), `QuestHintOverlayTests` and `StringThemTogetherTests`. They assert behaviour that has since changed elsewhere, and none of them touch Vis or the ice trial.

PlayMode runs were headless, with one test per Unity session.

**`VisCarriesTheRelicToTheTrialAndCinnaGoesAheadToMeetThePlayer`** covers the route in the real scene. Its earlier version, where the player was moved to the trial, passed (5.0 s). On 2026-10-11 the departure, hover and text changes were made, and this version compiles but has not been re-run in Unity, because the editor was open. It checks:

- the relic rests at `(23,53)` and blocks its tile;
- the player stands at `(23,54)` and lifts it;
- the campus portal carries it into the trial;
- the player sets it on the pedestal;
- the relic is gone from campus;
- the player reports to Cinna and the departure fades;
- the quest completes, the player stays where they stood on campus and the terrace Cinna is gone;
- walking through the portal finds the trial Cinna waiting.

**`IceTrialPortalSlidesFollowTheSketchRouteAndCompleteColdFeet` passes (7.3 s).** It first failed for two reasons:

- `StartupMenu` sets `Time.timeScale` to 0 until Start is pressed, so slides never advanced.
- The menu's backdrop save could start the room already frozen.

Both tests now close the menu (`DismissStartupMenu`). The ice test also resets event and world state, as the route test does.

**Known issues:**

- Running the whole `AuthoredWorldPlayModeTests` class in one session fails every test after the first. The cause is a logged error that predates this work: on the second `SampleScene` load, the new loader registers the `data-driven-world` save provider before the old one is destroyed.
- These PlayMode tests use the real save folder (`Application.persistentDataPath`), and the autosave writes the current slot.

## Pending acceptance

Not yet checked by hand in Play Mode:

- the feel of the hover, the fade length and the camera during the cut;
- placement previews against real props;
- controller input;
- a live save slot.

Other gaps:

- The portal rule (no placement on portal tiles) has no automated test.
- The relic is a placeholder primitive.
