# Stella: String Them Together — implementation tracker

**Quest ID:** `stella_string_them_together`
**Prerequisite:** `stella_first_light:Completed`
**Monitor:** `stella_string_them_together_monitor` → `complete_stella_string_them_together`
**Assessment:** **Partial.** Quest gating, Weave unlock, endpoint contract, connection persistence, and completion event are authored. The final map data is present; endpoint spacing makes the current Weave connection impossible under the runtime strength threshold. Play-mode feedback remains unverified.

## Player flow and authored contract

Stella is registered at W02 on `B02.Ground`, cell `(12,4)`, with placeholder `NPCs/Elara.prefab`. Weighted NPC dialogue selects this quest only after First Light is Completed; the dialogue's accept choice independently checks that prerequisite and that this quest is NotStarted. Acceptance starts the quest and sets `spell.unlocked.Weave` true. Stella asks for a connection from the powered relic to the circuit link and the active hint explains reconnecting a stray route.

Quest metadata lists Weave as the spell reward, but this quest grants Weave at acceptance in the dialogue action so it is available for the puzzle. Completion metadata is also processed by the spell progression handler; verify its idempotent behavior if progression wiring changes.

## Runtime completion and state

The puzzle monitor is scoped to `B02.Ground`. Its first target requires `relic.stella.power` to be powered; its second requires exact connection `relic.stella.power|circuit.stella.link`. The completion event is non-repeatable, requires quest Active, and completes the quest. `SpellCaster.Weave` looks for the target's `weave-with:` tag, then connects it to a powered object. The merged B02 map places the source at local cell `(10,3)` and link at `(15,3)`, five cells apart. `WorldStateService.AddConnection` calculates strength as `1 / (1 + distance)` and rejects strength below `0.2`; at this authored separation the strength is about `0.167`, so the current endpoints cannot connect through `AddConnection`. Connection state and strengths are included in persisted map snapshots.

The runtime interaction casts at the nearest compatible authored map object within two grid cells, so endpoint distance, reachability, target choice, and presentation matter. Runtime data-contract documentation describes live wire/power visuals, but a successful code path does not establish that required renderers and feedback are present in the current scene.

The objective overlay reads the active quest target and current map requirements to describe the powered-source and connection conditions and point toward the endpoint. The five-cell spacing issue remains a real runtime blocker, so guidance cannot make the current authored pair connect.

## Scene and data readiness

The merge is resolved. `B02.Ground.json` contains both stable endpoints and reciprocal `weave-with:` tags, with authored local cells `(10,3)` and `(15,3)`. The map target and quest database agree on the required exact connection. The `StellaStringThemTogetherPuzzle` class is present and implements First Light’s five-switch board, despite its name. No corresponding custom wiring presentation for this two-endpoint quest is evidenced in the audited sources. Dialogue is placed at W02; W03 is described as optional in `docs/npc-dialogue.md`.

## Open gaps and acceptance checklist

- [ ] Reduce source/link distance or adjust minimum connection strength; the five-cell gap yields strength ~0.167 against minimum 0.2, so `AddConnection` rejects it.
- [ ] Verify First Light must be completed before the dialogue becomes selectable and acceptance unlocks Weave.
- [ ] Verify a powered source can be connected to the intended link, exact connection evaluation passes, and wire/power feedback is readable.
- [ ] Verify stray/unintended connections do not satisfy the target; confirm any disconnect/retry interaction promised by dialogue exists.
- [ ] Verify quest completion and Weave/source/connection state persist across save/load and map rebuild.

Design details for final topology/board artwork and completion of production puzzle visuals remain open. Evidence: `quest_npc_dialogue.json`; `npcs.json`; `quest_puzzle_definitions.json`; `B02.Ground.json`; `campus.json`; `SpellCaster.cs`; `WorldStateService.cs`; `QuestPuzzleMonitor.cs`; `docs/spells.md`; `docs/implementation-status.md`.
