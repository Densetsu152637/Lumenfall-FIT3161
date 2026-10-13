# Game systems

Organise code by gameplay feature rather than by technical file type. A system owns its
runtime code, tests, documentation, and system-specific data so related changes are easy
to find and review.

Current systems:

- `Camera` — player camera behaviour.
- `Cutscenes` — data-driven cutscenes and the New Game arrival; see [`Cutscenes/README.md`](Cutscenes/README.md).
- `Story` — scripted story scenes and their story-only characters, starting with the forest-to-campus gate scene; see [`Story/README.md`](Story/README.md).
- `Player` — the player character's name, the `{player}` dialogue token and the name prompt; see [`Player/README.md`](Player/README.md).
- `GameEvents` — event, quest, dialogue, persistence, resource data, and EditMode tests.
- `GridMovement` — grid world, movement, pathfinding, portals, input, and presentation.

Startup menu behavior is documented in [`Startup/README.md`](Startup/README.md).

Create a new system folder when a feature has a distinct responsibility. Shared utilities
should only be introduced once at least two systems genuinely need them.
