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

The component starts with no spells. Accepting Stella's First Light starts the quest, then finishing her terminal teaching dialogue awards Ampere before the powered-relic puzzle; abandoning or merely accepting leaves it locked. Accepting String Them Together grants Weave before its circuit puzzle. Cinna Cirra grants Vis and Glaciate, Orio grants Rotara and Gravitate, and Katrina grants Lost Echoes when their authored quests are completed. Quest completion and Stella's scoped dialogue actions/metadata are the progression sources of truth, while `GameEventRuntime` flags persist rewards through save export/import. Opening or abandoning a dialogue does not grant spells. The optional `unlockedSpells` field acts only as a designer allow-list over already-unlocked spells and cannot bypass progression. Opening the wheel temporarily locks grid movement so scroll input is consumed by spell selection. Right-click closes the wheel after attempting a cast. `SpellWheelView` draws the wheel as an animated IMGUI rune circle: it blooms open and fades out on close while the spell orbs orbit the centre into place (each a little behind the last) and swing onward as it closes, rune rings turn, each spell's icon from `Resources/SpellIcons` sits in an orb tinted by its element with its number-key badge, and a beam and ripple follow the selection. Each orb also carries its spell's own motion: Ampere lightning arcs, Glaciate frost, a Rotara gear that turns a tooth at a time, a wobbling Gravitate orb inside a pulling ring, a Lost Echoes ghost that lags behind it, a Weave thread to the next orb, and a Vis frame that snaps level. Hovering an orb or its sector previews that spell's name and a one-line description in the centre; after a scroll the centre follows the selection until the pointer moves, and scroll steps are at least 0.06 s apart. Clicking selects the spell and unlocks movement at once, while the picked orb flies into the core and bursts into sparks before the wheel finishes closing. While the wheel is closed, a chip in the bottom-left corner shows the selected spell with the same motion on a dark plate, with the spell wheel key drawn as a key-cap, and pops when the selection changes. It glides up out of the way while a `RotaraGearMechanism` status panel is on screen (`RotaraGearMechanism.PanelVisible`). Its textures are generated at runtime, so it needs no extra assets. Keyboard/controller wheel navigation uses arrows or the stick to select, Enter/A to cast, and Escape/B to cancel. Owner-scoped movement locks and OverlayInputGate leases prevent the wheel from releasing a sibling overlay or leaking input into gameplay. The selected-spell chip is hidden while paused.

Ampere marks the target powered and queues a durable `lightning:{objectId}` effect; it refuses objects tagged `conductor`. Weave connects the target to its `weave-with:` partner or, without one, to the nearest powered object. Objects tagged `circuit` are wired by hand instead: the first cast holds a thread, each later cast ties it to the next circuit object within four tiles and keeps holding it there, and casting on an already linked pair cuts that cable. `WorldStateService` recomputes `conductor` power after every change: a conductor is powered only while live cables (strength at least `0.2`) lead back to a powered object without the tag. `WeaveCableView` draws every cable (cyan with a travelling spark when live, amber when dark), the held thread with faint lines to every circuit object in reach, and a glow on powered `ampere`/`weave` objects. Gravitate moves one tile after checking bounds, blockers, and mover occupancy. Rotara advances orientation by 90 degrees. Glaciate marks the target frozen and slides it in the supplied direction until blocked or the map boundary. Vis marks the target aligned at its nearest quarter turn. Lost Echoes reveals the target and all objects tagged `hidden`.

Map object ids come from `MapObjectDefinition.id`; generated tile objects use `{type}_{x}_{y}`. Puzzle targets use `puzzleTargets` and can require powered, connected, revealed, frozen, aligned, rotation, position, and exact `requiredConnections` conditions. A Glaciate object can carry `slide-goal:x,y`; it slides until that destination, a blocker, or a boundary. `WorldStateService` publishes `StateChanged` and `MapRebuilt`, and persists object transforms and properties, connections, reveals, and unacknowledged effects through the save provider key `spell-world-state`.
