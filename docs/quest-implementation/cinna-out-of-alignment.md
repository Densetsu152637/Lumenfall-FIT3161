# Cinna Cirra: Out of Alignment — implementation tracker

**Quest ID:** `cinna_out_of_alignment`
**Completion:** Cinna's `report` choice → `CinnaTrialDeparture` → `complete_cinna_out_of_alignment` (no puzzle monitor)
**Assessment:** **Partial.** The Vis lift and carry, delivery to the ice-trial pedestal, the report, Cinna's departure fade, reset and saves are implemented. They pass EditMode tests and a PlayMode route test. Hand-played acceptance and production art are outstanding.

## Player flow and authored contract

Cinna Cirra's orientation NPC stands on `campus` at `(22,51)` (C02) and uses the shared placeholder `NPCs/Elara.prefab`. Accepting and finishing the lesson or reminder teaches Vis while the quest is Active.

The orientation relic `relic.cinna.orientation` rests at `(23,53)`, tagged `vis`, `vis-lift` and `vis-destination:C03.IceTrial`. The player lifts it with Vis and carries it through the trial portal at campus `(47,45)`. They then face the pedestal at `(3,1)` in `C03.IceTrial` and cast Vis again to set the relic on it.

On campus the relic can be set down only on open walkable ground that is not a portal. Entering any other area lets go of it, and it waits at its last safe placement. Delivery sets `vis.delivered.relic.cinna.orientation`; the relic leaves campus and becomes the frost relic for Cold Feet, Warm Idea.

Reporting back to Cinna on the terrace fades the screen to black while Cinna leaves for the trial. The quest completes and the screen fades back in with the player where they stood. They walk through the portal and find Cinna in the trial lobby at `(8,1)`.

## Runtime completion and state

`SpellCaster` holds the relic (`HeldRelic`) without changing world state. Placement on campus moves the relic through `WorldStateService.TryMoveObject`; delivery is a progression flag plus the stand's `aligned` state. `CinnaAlignmentRelic` registers the campus relic and draws it on every map. `CinnaTrialDeparture` handles the fade and completion while Cinna leaves for the trial. R near the relic resets it while the quest is Active on campus.

The hint overlay follows the steps: lift the relic beside Cinna; carry it to the portal; set it on the stand; return to Cinna.

## Scene and data readiness

`campus.json` and `campus.asset` agree on the relic, and the trial pedestal carries the stand tag. `scripts/validate_out_of_alignment.py` checks the relic, the stand, Cinna's report and the meet tile. The old inactive `orientation.cinna.align` marker GameObject remains in `campus.prefab`, but nothing registers or refers to it. The relic is a placeholder primitive.

## Open gaps and acceptance checklist

- [x] Teach Vis before it is needed.
- [x] Define the rule: lift, carry, place on open ground or the stand, and return to the last placement outside campus and the trial.
- [x] Complete only after the relic is on its stand and the player reports back.
- [x] PlayMode route test: lift on campus, carry into the trial, deliver, report, Cinna leaves, walk back in. The departure version has not yet been re-run in Unity.
- [ ] Hand-play the route and check the hover, the ghost tile against real props, the fade length and the camera during the cut.
- [ ] Check the save-slot round trip mid-carry and after delivery in a live game.

Evidence: `quest_npc_dialogue.json`; `Maps/campus.json`; `World/Areas/campus.asset`; `World/Areas/C03.IceTrial.asset`; `CinnaAlignmentRelic.cs`; `CinnaTrialDeparture.cs`; `SpellCaster.cs`; `WorldStateService.cs`; `DataDrivenWorldLoader.cs`; `OutOfAlignmentTests.cs`; `AuthoredWorldPlayModeTests.cs`; `scripts/validate_out_of_alignment.py`.
