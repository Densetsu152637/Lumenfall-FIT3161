# Game systems

Organise code by gameplay feature rather than by technical file type. A system owns its
runtime code, tests, documentation, and system-specific data so related changes are easy
to find and review.

Current systems:

- `Camera` — player camera behaviour.
- `GameEvents` — event, quest, dialogue, persistence, resource data, and EditMode tests.
- `GridMovement` — grid world, movement, pathfinding, portals, input, and presentation.

Create a new system folder when a feature has a distinct responsibility. Shared utilities
should only be introduced once at least two systems genuinely need them.
