# Cinna Cirra: Cold Feet, Warm Idea — implementation tracker

**Quest ID:** `cinna_cold_feet_warm_idea`
**Prerequisite:** `cinna_out_of_alignment:Completed`
**Monitor:** `cinna_cold_feet_warm_idea_monitor` → `complete_cinna_cold_feet_warm_idea` (success line `cinna_ice_trial_solved`)
**Assessment:** **Partial.** The ice trial room, lesson, slide rules, toggle switches, seal, reset, persistence and completion are implemented in data and runtime code. Unity Play Mode acceptance and production art are outstanding.

## Player flow and authored contract

The trial lives in its own area, `C03.IceTrial`, reached through the portal on the campus path at `(47,45)`. During Out of Alignment the player carries the orientation relic in and sets it on the pedestal at `(3,1)`. Reporting back to Cinna on the terrace sends Cinna ahead behind a fade; Cinna Cirra (`cinna_cirra_trial`) then waits in the trial lobby at `(8,1)`, and the player walks over through the portal. The trial Cinna appears once Out of Alignment is completed. Their accept choice starts the quest. Finishing the lesson while the quest is Active lends Glaciate through `spellLesson` metadata.

Casting Glaciate on the delivered relic (`relic.cinna.frost`, anchored, spell-compatible only once delivered) freezes the pool, and a second Glaciate thaws it again (moving the player out of the pool first). The player slides Snowpoint Gym style: no steering mid-slide, stopping on a switch, at the entrance or in front of a wall. Each landing toggles the switch underneath. With all nine lit, landing on the seal on the east ledge opens it and completes the quest, which teaches Glaciate permanently. R resets the switches and returns the player to the entrance.

The layout, rules, runtime pieces and verification are in [the implementation notes](../implementation/quest-cold-feet-warm-idea.md).

## Open gaps and acceptance checklist

- [x] Run `CinnaIceTrialTests` (5/5), the Cold Feet hint test and the PlayMode slide-route test (passes when run on its own, after closing the startup menu) in Unity.
- [ ] Check slide speed, camera follow and the idle slide pose in Play Mode.
- [ ] Check Cinna's model, animation and facing in the lobby.
- [ ] Check save and load mid-trial, and leaving and re-entering the room, for frozen and switch state.
- [ ] Replace placeholder primitives with production art and an ice/water material.
- [x] The portal stays open before Out of Alignment completes, because the relic is carried in through it.

Evidence: `CinnaIceTrialRoom.cs`; `CinnaIceTrialEntrance.cs`; `GridMover.cs`; `GridWorld.cs`; `C03.IceTrial.asset`; `quest_npc_dialogue.json`; `quest_puzzle_definitions.json`; `NpcCatalog.asset`; `CinnaIceTrialTests.cs`; `AuthoredWorldPlayModeTests.cs`.
