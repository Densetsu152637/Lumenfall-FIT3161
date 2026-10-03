# Chapter 1 arrival, key mystery, dormitory and sleep/wake

Branch: `codex/feature-chapter-one-story`, independent baseline `8ceebfa`.

## Required scope

Implement the opening chapter from `docs/main-cast.md`: the receptionist first says another student has the key; meet the four friends and finish the six required quests; return together for the contradictory account and one-time residential key; discuss the contradiction in Sirius; unlock the residential door; accept a friend's guidance to the player's room; sleep; wake with a deterministic saved Chapter 1 completion marker. Preserve the unresolved contradiction and existing quest identities.

All story transitions must reject premature/direct invocation. Key possession, door access, guidance and sleep must restore coherently through existing event flags and GameSaveManager. Optional travel cannot bypass the key door. Use live authored WorldAreaAsset/areaPrefab content rather than JSON-only map edits. Stable authored event/dialogue/interaction IDs, matching metadata, and meaningful boundary/repeat/restore tests are required.

## Implementation status

Planning checkpoint. Runtime/content worker is inspecting existing event, dialogue, map and save owners. Manager will review the actual diff and update this document with implemented and pending scope after a substantive implementation attempt.

## Validation and integration contract

No Unity Editor, MCP, CLI, Play Mode or build operations during parallel development. Offline compiler references are read-only at the installed Unity 6000.4.10f1 project; compiler responses and outputs stay in this worktree. Compilation cannot prove visual fade, collision, keyboard/gamepad input, authored interaction reachability or platform acceptance.

The independent world-route branch owns baseline bootstrap and area transitions; this branch owns story rules/interaction bridge and residential key predicates. Exact narrow merge overlaps and public APIs will be recorded here. No sibling commits are imported.
