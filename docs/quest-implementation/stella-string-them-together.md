# Stella: String Them Together — implementation tracker

**Quest ID:** `stella_string_them_together`
**Prerequisite:** `stella_first_light:Completed`
**Monitor:** `stella_string_them_together_monitor` → `complete_stella_string_them_together`
**Assessment:** **Partial.** Quest gating, Weave unlock, the woven board circuit, relay ring, connection persistence, completion event, hints and placeholder cable visuals are authored. EditMode tests compile but have not been executed, and Play Mode verification has not been executed.

## Player flow and authored contract

Stella is registered at W02 on `B02.Ground`, cell `(12,4)`, with placeholder `NPCs/Elara.prefab`. Weighted NPC dialogue selects this quest only after First Light is Completed; the dialogue's accept choice independently checks that prerequisite and that this quest is NotStarted. Acceptance starts the quest and sets `spell.unlocked.Weave` true. Stella explains that the five First Light board relics have gone dark and only take power through Weave cables, and the active line repeats how to hold, tie and cut a thread.

Quest metadata lists Weave as the spell reward, but this quest grants Weave at acceptance in the dialogue action so it is available for the puzzle. Completion metadata is also processed by the spell progression handler; verify its idempotent behavior if progression wiring changes.

## Runtime completion and state

The puzzle monitor is scoped to `B02.Ground` and has one target: all five board relics `stella.string-together.relic.1`–`5` powered. When this quest leaves NotStarted, `StellaStringThemTogetherPuzzle` retags those relics `weave`, `circuit` and `conductor`, makes them spell-compatible and shows ten relay pylons `stella.circuit.relay.1`–`10`, also conductors. Conductors cannot be powered by Ampere or the interact key; they light only while woven cables lead back to `relic.stella.power` (tags `ampere`, `weave`, `circuit`) after the player powers it with Ampere. `circuit.stella.link` is now a conductor that bridges the source to relic 4. The relays form a ring of hops between 2 and 3.6 tiles, so the four-tile cable limit still applies and the player chooses how to wire the ring. The completion event is non-repeatable, requires quest Active, and completes the quest. The B02 area places the source at local cell `(11,3)` and the link at `(14,3)` on the W02 workbench, three cells apart. `WorldStateService.AddConnection` calculates strength as `1 / (1 + distance)` and rejects strength below `0.2`, so the authored pair connects at `0.25`. They were previously at `(10,3)` and `(15,3)` (strength about `0.167`), which made the quest impossible; `scripts/validate_runtime_world.py` fails any `weave-with:` pair in the map JSON or area prefabs that is too far apart to connect; the relay ring is checked by the EditMode test `AuthoredRingCanBeWovenToEveryRelicByFollowingTheHint`. Connection state and strengths are included in persisted map snapshots, so a save that already visited B02 restores the old cells; start a new game to test the moved endpoints.

The runtime interaction casts at the nearest compatible authored map object within two grid cells, so endpoint distance, reachability, target choice, and presentation matter. `WeaveCableView` (in `Systems/Spells`) presents this state: each connection is drawn as a sagging cable, a live cable (effective power at least `0.2`) pulses cyan with a spark travelling from the powered end, a connected but dark cable is drawn amber, and objects tagged `ampere` or `weave` glow with a point light while powered. Once Weave is learned, each unconnected `weave-with:` pair shows a faint guide thread. These are placeholder visuals pending approved cable art.

The objective overlay asks for Ampere on Stella's relic while it is dark, then uses `StellaStringThemTogetherPuzzle.TryGetCircuitStep` to name and point at the next cable on a shortest route from the lit network to a dark relic. While the quest is active the board panel shows which relics are lit and whether a thread is held.

## Scene and data readiness

The merge is resolved. `B02.Ground.json` contains both stable endpoints and reciprocal `weave-with:` tags, with authored local cells `(10,3)` and `(15,3)`. The map target and quest database agree on the required exact connection. The `StellaStringThemTogetherPuzzle` class is present and implements First Light’s five-switch board, despite its name; it now also owns this quest's circuit mode, relays and status panel. Cable presentation comes from the shared `WeaveCableView`. Dialogue is placed at W02; W03 is described as optional in `docs/npc-dialogue.md`.

## Open gaps and acceptance checklist

- [x] Reduce source/link distance: endpoints moved to `(11,3)`/`(14,3)` (strength `0.25`) and guarded by the runtime world validator.
- [ ] Verify First Light must be completed before the dialogue becomes selectable and acceptance unlocks Weave.
- [ ] Play-test the woven circuit: board relics go dark when the quest starts, Ampere is refused on them, threads tie and cut as described, and cable/glow feedback is readable.
- [ ] Run the EditMode tests in Unity (`StringThemTogetherTests`, `SpellWorldStateTests`).
- [ ] Verify quest completion and Weave/source/connection state persist across save/load and map rebuild.

Design details for final topology/board artwork and completion of production puzzle visuals remain open. Evidence: `quest_npc_dialogue.json`; `npcs.json`; `quest_puzzle_definitions.json`; `B02.Ground.json`; `campus.json`; `SpellCaster.cs`; `WorldStateService.cs`; `QuestPuzzleMonitor.cs`; `docs/spells.md`; `docs/implementation-status.md`.
