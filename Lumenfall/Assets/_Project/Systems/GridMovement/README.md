# Tile-grid movement

This folder contains a component-based 2.5D movement system. The camera, lighting, sprites and
models remain in a 3D scene, while gameplay movement is restricted to a two-dimensional X/Z
ground plane. Every accepted movement command reserves and completes exactly one tile, including
diagonals.

Grid coordinates use `Vector2Int(x, y)`, mapped into Unity space as follows:

```text
grid (x, y) -> local Unity (x * cellSize, elevation, y * cellSize)
```

In other words, grid Y means Unity Z. Unity Y remains vertical height, so an actor's feet/model
offset is preserved while it walks across the grid.

## Minimal player setup

1. Create an empty `Grid World` object and add `GridWorld`. Its transform is the grid origin;
   one grid coordinate maps to one local X/Z cell.
2. Add `GridMover` and `GridPlayerInput` to the player. Place the player near a cell centre;
   `GridMover` snaps it precisely when enabled.
3. Optionally drag `Player/Move` and `Player/Sprint` from
   `Assets/_Project/Input/InputSystem_Actions.inputactions` into their action fields. If left empty,
   `GridPlayerInput` supplies WASD, arrows, gamepad movement, Left Shift sprint, and gamepad
   stick-click sprint automatically.
4. Tune Walk Move Duration and Sprint Move Duration on `GridMover`. Sprint has no stamina logic;
   it simply selects the faster whole-tile duration. Input remains locked until each step is
   complete, and the final position is snapped exactly to the target cell.
5. For a 3D character model, add `GridMoverPresentation` and assign its Animator. This optional
   component turns the model toward accepted steps and drives `Speed`, `IsGrounded`, and
   `IsFalling` parameters when they exist, while `GridMover` remains the movement authority.

`GridWorld` is unbounded by default. Enable Restrict To Bounds to prevent movement outside a
room. Prevent Diagonal Corner Cutting stops actors squeezing diagonally between two blockers.

## Items, furniture and buildings

Add `GridObstacle` to any blocking object. Footprint Size `(1, 1)` blocks one item tile. A size
such as `(4, 3)` blocks a multi-tile structure. Footprint Offset moves the bottom-left footprint
cell relative to the object's anchor cell. The Scene view selection gizmo shows every blocked
tile.

If code moves or resizes a blocker at runtime, call `RefreshFootprint()` afterward. Overlapping
blockers are reference-counted, so removing one does not accidentally unblock the other.

## NPCs

`GridNpcNavigator` moves an NPC toward a requested absolute grid coordinate. It runs A* against
the world's current structures, actors, movement reservations and diagonal corner rules, then
queues the result as ordinary whole-tile `GridMover` steps. It does not prescribe schedules or a
patrol; cozy-game behaviour code can decide when and where an NPC wants to go:

```csharp
navigator.SetDestination(new Vector2Int(12, 7));
navigator.SetMovementMode(GridMovementMode.Walk);
```

The navigator recalculates if a dynamic actor enters its queued route. `DestinationReached` and
`PathFailed` events let a schedule, conversation, animation or job system continue appropriately.
`GridPathfinder.TryFindPath(...)` is also public when other systems only need a path query.

## Portals and rooms

Add `GridPortal` to an entrance and set its Footprint Size. Portals may cover one tile or a
rectangle of tiles, do not block those tiles, and trigger only after a mover finishes stepping
onto one. When Preserve Entry Offset is enabled, entering the second tile of a wide doorway maps
to the second tile of its destination doorway. Arrival portals are suppressed until the mover
leaves, preventing linked door areas from immediately bouncing the player back.

Destination modes:

- **Portal Reference** links two portal components in the same loaded scene. This is convenient
  for an outside and building interior placed in separate areas of one scene.
- **World Cell** sends the actor to an explicit cell in another `GridWorld`, also in the same
  scene.
- **Scene Portal Id** loads a scene in Single mode and finds its enabled portal by Portal Id.
  Put the destination scene in Build Settings, use a unique non-empty id, and normally set Arrival
  Offset to an adjacent non-blocked cell such as `(0, -1)`.

For scene-to-scene travel, the moving GameObject is kept alive while the new scene loads and then
moved into the new active scene. Destination scenes should therefore contain the destination
world/portal and camera, but not another copy of the persistent player.

## Useful runtime queries

```csharp
Vector2Int cell = mover.CurrentCell;
bool canEnter = world.IsCellAvailable(cell + Vector2Int.right);
bool blocked = world.IsCellBlocked(cell);
GridMover occupant = world.GetMoverAt(cell);
GridPortal portal = world.GetPortalAt(cell);
bool found = GridPathfinder.TryFindPath(world, cell, targetCell, path);
```
