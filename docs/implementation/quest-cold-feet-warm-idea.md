# Cinna Cirra: Cold Feet, Warm Idea

## Implemented behavior

The trial surface starts at campus (20,34), moves right through provisional virtual trap cells (21,34), (22,34), (23,34), (24,34), and stops at (24,34). Each successful GridWorld move increments authoritative `visitedTrapCount`; generic property mutation, frozen state, or goal position alone cannot manufacture proof. Both map puzzle metadata and the live monitor require four visits, frozen state, the goal, and no interruption. Goal recasts do nothing. Other spells cannot move or mutate this trial through SpellCaster.

Glaciate is taught only after the player advances beyond the accept/active terminal lesson, while Cold Feet is Active and Out of Alignment is Completed. Opening, acceptance alone, leaving, abandonment, premature direct lesson invocation, and completed-quest dialogue do not teach it. The saved spell flag remains compatible with existing progression. GameEventManager runtime replacement now notifies SpellCaster and QuestPuzzleMonitor; captured subscriptions are removed on disable and rebound on enable/replacement, so StartupMenu definition reload does not break teaching.

Trial Cinna keeps prerequisite visibility and offers exactly the three Cold Feet quest-state rules. Source NPC JSON, NpcCatalog and campus baked DynamicNpc definition agree. The campus source object, WorldArea asset and prefab registration carry the same lane metadata. The completion monitor opens one success dialogue after the one-time event succeeds, and does not replay it after saved completed state or enable/re-enable.

## Direction, interruption, reset and persistence policy

The lane is a one-row rightward path. Wrong-direction, diagonal, off-lane, generic movement and blocked attempts invalidate the sequence; the prop remains at the last valid cell and needs reset. Destinations use GridWorld bounds, blockers, mover occupancy and reservations across the registered object footprint. FreezeSlide applies up to four synchronous steps in a cast; this is a moving prop puzzle, not automatic player movement.

While the quest is Active, press R beside the surface to reset it to its authored start, clear visits/interruption and thaw it, then cast right again. Reset rejects an occupied or blocked start footprint without changing the current state. Player reset is unavailable after durable quest completion. WorldStateService exposes a low-level reset API for integration/tests; progression policy belongs to SpellCaster. No unrelated quest or map state is erased.

Valid mid-sequence snapshots persist cell, freeze state and visits and resume remaining ordered visits. An interrupted snapshot preserves its location and interruption and requires reset. Legacy frozen-goal saves lacking visit proof, negative/excessive counters, mismatched cells or changed saved tags cannot complete: they fall back to authored start with reset required. Authored lane/tags/footprint remain authoritative; snapshots have detached tag arrays. Restore rejects occupied saved placement and retains the previous placement with reset required. Map archives carry the same proof policy. Completed quest/event state remains durable independently of later world-state changes.

## Provisional content and pending acceptance

Four contiguous virtual trap cells, rightward cast direction and prop presentation are provisional. Lesson/objective text communicates coordinates and reset controls. No new final trap-marker/endstop meshes, approved trap art, automatic player slide or slide animation were authored. The mathematical lane endpoint is the stop. Trial objects remain grid-nonblocking; campus source row 35 provides a walking return path. Static source and baked GridObstacle inspection found no blocker footprint over x20–24 in rows34/35, but hierarchical physics and actual walking/visual clarity still need Play Mode acceptance.

No Unity Editor, Unity MCP/CLI, Play Mode, Unity test execution or player build was launched under the parallel-work contract. Visual, input-device, audio, platform, old slot integration and final content-design approval remain pending. Existing broad quest prose describing frozen-goal completion is superseded for this trial by this scoped document and the updated live objective/lesson. The known baseline pure-runtime Ampere lifecycle test incorrectly expects a caster-owned reward; First Light owns its correction during integration.

## Validation performed

- Offline Unity 6000.4.10f1 Roslyn compilation passed: GameEvents (10 source files), Runtime (25), GameEvents EditMode test assembly (7), World PlayMode test assembly (1). All imported Lumenfall.* and Assembly-CSharp references were excluded; current branch production outputs were used for tests. Shared references read only from `D:/Git Repositories/Lumenfall-FIT3161/Lumenfall/Library/Bee/artifacts/1900b0aE.dag`. Response files/output/log are in this worktree `Lumenfall/Temp/cold-feet-compile`. Existing obsolete API/serialized-field warnings remain; no compile errors.
- `ColdFeetTrialTests` adds 16 methods / 22 parameterized cases covering lesson timing/prerequisites and save, runtime replacement, real visits/end stop, forged goal/property mutations, directions, general movement, blocker interruption/retry, occupied reset, occupied/footprint destination, partial/map rebuild persistence, interrupted/legacy/malformed restore, tag isolation, completion idempotence and quest/spell guards. These Unity tests compiled but were not executed.
- `python scripts/validate_cold_feet.py`: PASS, scoped JSON/baked lane/target/NPC parity and walking source row checks.
- `python scripts/validate_runtime_world.py`: PASS, 8 maps / 7 NPCs.
- `python scripts/validate_npc_dialogue.py`: PASS, 6 quests / 16 dialogues / 9 events; narrow expected Cinna trial rule count updated to three.
- `python -m unittest discover -s scripts/tests -v`: 11 PASS, including seven Cold Feet content-invalid fixture tests and four baseline project-validator tests.
- `git diff --check origin/main...HEAD`: checked after final implementation commit; no whitespace errors.

Overall: implemented and offline/static validated; Unity gameplay acceptance remains unexecuted. Root owns worktree cleanup after remote verification.

## Merge overlaps

SpellCaster teaching/lifecycle/reset input, GameEventManager RuntimeChanged, QuestPuzzleMonitor evaluation/lifecycle/success dialogue, WorldStateService movement/reset/save/feedback, MapAndNpcDefinitions puzzle fields, campus asset/prefab, NpcCatalog and quest dialogue/monitor JSON overlap independent branches. Combine exact scope-owned hunks; preserve First Light captured-runtime lifecycle and terminal reward metadata, String exact live connection rules, alignment/Orio/Katrina rules, generic reset/feedback, save recovery and content validation. This branch grants only Glaciate's own new lesson; unrelated baseline spells retain their existing reward behavior. No merges or cherry-picks were performed.
- Full project integrity validator reports the same three known ThirdParty baseline faults: orphaned Outdoor Environment URP metadata, FantasyCastle ReadMe encoding, and Low Poly Tarvern Pack README conflict marker. No scoped new project-integrity fault was reported; those unrelated vendor files were left untouched.
