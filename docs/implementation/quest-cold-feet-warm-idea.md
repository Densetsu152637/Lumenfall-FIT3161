# Cinna Cirra: Cold Feet, Warm Idea

## Required scope and implementation plan

Teach Glaciate only after a completed terminal lesson while Cold Feet is Active and Out of Alignment is Completed. Preserve the trial surface at (20,34) and goal (24,34). Extend authoritative WorldStateService with a bounded rightward lane and real ordered visits to (21,34), (22,34), (23,34), (24,34). Frozen state or goal alone cannot complete it. Honor GridWorld blockers, occupancy, reservations and object footprints; reject off-lane or wrong-direction moves and record interruption. Provide active quest reset/retry and nonblocking walking exit via adjacent y35 row. Persist ordered visit proof and interruption; old saves without proof require reset. Synchronize source JSON, campus asset, prefab metadata and trial NPC catalogue/baked rules. Add behavior tests and offline compilation/validators.

## Provisional design and boundaries

The existing moving surface prop crosses traps; this implementation does not automatically slide the player. The contiguous four-cell ordering, rightward cast direction, walking return row y35, and prop presentation are provisional design. Trial props remain nonblocking. Unity Editor, Play Mode, visual/audio, player-build and target-device acceptance remain unexecuted under the parallel-work contract.

## Merge overlaps

SpellCaster lesson/lifecycle, QuestPuzzleMonitor evaluation/lifecycle/success dialogue, WorldStateService reset/save/feedback, MapAndNpcDefinitions puzzle fields, campus asset/prefab and quest dialogue/monitor JSON overlap other branches. Preserve First Light RuntimeChanged/captured runtime and terminal reward quest metadata, String exact live connection rules, and generic reset/feedback additions during eventual merge. No merges or cherry-picks are part of this branch.

## Validation

Pending substantive implementation and offline validation. Shared reference assemblies will be read-only, all imported Lumenfall references excluded, output under this worktree Temp. Static and offline compilation do not establish gameplay acceptance.
