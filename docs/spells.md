# Spell reference

These spells define the current interaction vocabulary for Lumenfall's grid-based puzzles. The descriptions are design intent; range, animation, and failure feedback should be confirmed during implementation.

## Ampere

Ampere supplies electrical power to a compatible relic or object. A powered object can activate a mechanism or participate in an electrical network. The current design assumes that a target within the grid interaction radius is powered immediately.

## Weave

Weave creates a cable or wire connection between compatible relics. The connection transmits power, with effectiveness reduced as the distance between its endpoints increases. Puzzle rules should define permitted endpoints and minimum effective power.

## Gravitate

Gravitate moves a compatible object by one tile in a selected direction. It is intended for deliberate, tile-by-tile repositioning during environmental puzzles.

## Rotara (Redirect)

Rotara rotates a compatible object by 90 degrees per turn. Rotation changes the object's orientation and is used to align directional mechanisms.

## Glaciate

Glaciate freezes a compatible surface or object and permits movement in one direction over an effectively unrestricted distance. Level design must define stopping conditions, including obstacles and boundaries.

## Vis

Vis aligns or reorients designated objects. The exact object category and alignment rules remain to be specified alongside the related orientation quest.

## Lost Echoes

Lost Echoes reveals points of interest and hidden sections. It can also identify items separated from their intended purpose or owner, supporting investigation quests and item recovery.

## Runtime defaults

The runtime exposes these spells through `Lumenfall.Spells.SpellCaster`: number keys 1-7 select Ampere, Weave, Gravitate, Rotara, Glaciate, Vis, and Lost Echoes; `F` casts at the nearest compatible authored map object within two grid cells. Direct integrations call `SpellCaster.Cast` with a stable object id and direction.

Ampere marks the target powered and queues a durable `lightning:{objectId}` effect. Weave connects the target to the nearest powered object. Gravitate moves one tile after checking bounds, blockers, and mover occupancy. Rotara advances orientation by 90 degrees. Glaciate marks the target frozen and slides it in the supplied direction until blocked or the map boundary. Vis marks the target aligned at its nearest quarter turn. Lost Echoes reveals the target and all objects tagged `hidden`.

Map object ids come from `MapObjectDefinition.id`; generated tile objects use `{type}_{x}_{y}`. Puzzle targets use `puzzleTargets` and can require powered, connected, revealed, frozen, aligned, rotation, position, and exact `requiredConnections` conditions. A Glaciate object can carry `slide-goal:x,y`; it slides until that destination, a blocker, or a boundary. `WorldStateService` publishes `StateChanged` and `MapRebuilt`, and persists object transforms and properties, connections, reveals, and unacknowledged effects through the save provider key `spell-world-state`.
