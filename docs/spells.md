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

The runtime exposes these spells through `Lumenfall.Spells.SpellCaster`. Middle mouse (the mouse wheel button) opens and closes the spell wheel; mouse-wheel scrolling cycles the available spells, and right mouse casts the selected spell at the nearest compatible authored map object within two grid cells. Number keys 1-7 remain available as a keyboard selection fallback, while `F` no longer casts. The legacy `E` key remains owned by NPC interaction. Direct integrations call `SpellCaster.Cast` with a stable object id and direction.

The component starts with no spells. Accepting Stella's First Light starts the quest, then finishing her terminal teaching dialogue awards Ampere before the powered-relic puzzle; abandoning or merely accepting leaves it locked. Accepting String Them Together grants Weave before its circuit puzzle. Cinna Cirra grants Vis and Glaciate, Orio grants Rotara and Gravitate, and Katrina grants Lost Echoes when their authored quests are completed. Quest completion and Stella's scoped dialogue actions/metadata are the progression sources of truth, while `GameEventRuntime` flags persist rewards through save export/import. Opening or abandoning a dialogue does not grant spells. The optional `unlockedSpells` field acts only as a designer allow-list over already-unlocked spells and cannot bypass progression. Opening the wheel temporarily locks grid movement so scroll input is consumed by spell selection. Right-click closes the wheel after attempting a cast. The wheel uses each spell's generated icon from `Resources/SpellIcons` with a small runtime UI overlay until a production UI is wired.

Ampere marks the target powered and queues a durable `lightning:{objectId}` effect. Weave connects the target to the nearest powered object. Gravitate moves one tile after checking bounds, blockers, and mover occupancy. Rotara advances orientation by 90 degrees. Glaciate marks the target frozen and slides it in the supplied direction until blocked or the map boundary. Vis marks the target aligned at its nearest quarter turn. Lost Echoes reveals the target and all objects tagged `hidden`.

Map object ids come from `MapObjectDefinition.id`; generated tile objects use `{type}_{x}_{y}`. Puzzle targets use `puzzleTargets` and can require powered, connected, revealed, frozen, aligned, rotation, position, and exact `requiredConnections` conditions. A Glaciate object can carry `slide-goal:x,y`; it slides until that destination, a blocker, or a boundary. `WorldStateService` publishes `StateChanged` and `MapRebuilt`, and persists object transforms and properties, connections, reveals, and unacknowledged effects through the save provider key `spell-world-state`.
