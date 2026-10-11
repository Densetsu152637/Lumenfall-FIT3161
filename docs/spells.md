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

In the runtime, Lost Echoes is an area search. Right-clicking or pressing the interact key (`F` by default) with it selected reveals every spell-compatible object tagged `hidden` within its own search radius (four cells, Manhattan distance; `SpellCaster.lostEchoesRadius`, separate from the two-cell targeting radius of other spells) on the current map. It needs no target.

- Authoring: tag an object `hidden` to make it findable. Its renderers stay off until it is revealed; an object with no renderers stays invisible, like Katrina's keepsake.
- Reveal hooks: add `on-reveal:<eventId>` tags to trigger GameEvents when the object is revealed, for example `on-reveal:open_hidden_door`. Each event's own conditions and repeatability apply, so a hook can be gated on a quest. Hooks fire only on a live reveal, never when saved state is restored. Hidden passages, memories and collectibles can therefore be built from data: the event's actions set flags, start dialogue, or complete quests. Quest puzzle monitors with `requiredRevealed` targets still work.
- Pickups: tag an object `pickup` to let the player collect it with the talk key (`E` by default) from within one cell. Hidden pickups can be picked up too; Lost Echoes helps locate them, and picking up an unrevealed one reveals it first, so reveal feedback and `on-reveal:` hooks still run. `WorldPickupInteraction` draws a pulsing glow and an "E Pick up" prompt only for visible or revealed pickups, so nothing gives a concealed one away (the key still picks it up blind), gives NPCs in talk range priority on the key, and plays a short pickup sound. Collecting sets the object's saved `collected` state, hides its renderers, raises `WorldStateService.ObjectCollected`, and fires `on-pickup:<eventId>` hooks; restoring a save never fires them again. Quest puzzle targets can require `requiredCollected`.
- Code: `WorldStateService.ObjectRevealed` is raised once per reveal, and `SpellCaster.LostEchoesCast` is raised for every search with the cell, radius and number found.
- Feedback: there is no text feedback; the result is shown visually by `LostEchoesPresentation`. On every cast the player flares (a point light, a glow at hand height, and a brief cast pose using the player animator's `attack_01` state, held for 0.35 s, never extended by repeated casts, and always handed back to `idle_01` because that demo controller chains `attack_01` on into its attack, `hide` and `death` states), and an expanding diamond pulse plays with a tone.
- Sonar: if something hidden remains within eight cells, an echo of the pulse returns, sooner and louder the closer it is, with a fainter second echo; nothing within eight cells means no echo. The echo gives distance only, never direction, so the player still has to search.
- Reveal: motes gather into the spot, then a glow and light beam fade out completely, so the spot returns to its unfound look.
- Kind colours: tag `echo:item` (or `pickup`) for gold, `echo:passage` for blue and `echo:memory` for violet; anything else uses the default violet echo colour.
- Sounds: real clips placed at `Resources/Audio/LostEchoes/Pulse`, `Echo`, `Gather` and `Reveal`, and at `Resources/Audio/Pickup`, replace the generated fallbacks automatically. The fallbacks are generated tones with a short, softened echo tail.

## Runtime defaults

The runtime exposes these spells through `Lumenfall.Spells.SpellCaster`. Middle mouse (the mouse wheel button) opens and closes the spell wheel; mouse-wheel scrolling cycles the available spells, and right mouse casts the selected spell at the nearest compatible authored map object within two grid cells. Number keys 1-7 remain available as a keyboard selection fallback. The interact key (`F` by default) performs a targetless area search when Lost Echoes is selected, while the legacy `E` key remains owned by NPC interaction and world pickups. Direct integrations call `SpellCaster.Cast` with a stable object id and direction, or `CastLostEchoes` for the targetless search.

The component starts with no spells. Accepting Stella's First Light starts the quest, then finishing her terminal teaching dialogue awards Ampere before the powered-relic puzzle; abandoning or merely accepting leaves it locked. Accepting String Them Together grants Weave before its circuit puzzle. Cinna Cirra grants Vis and Glaciate, Orio grants Rotara and Gravitate, and Katrina grants Lost Echoes when their authored quests are completed. Quest completion and Stella's scoped dialogue actions/metadata are the progression sources of truth, while `GameEventRuntime` flags persist rewards through save export/import. Opening or abandoning a dialogue does not grant spells. The optional `unlockedSpells` field acts only as a designer allow-list over already-unlocked spells and cannot bypass progression. Opening the wheel temporarily locks grid movement so scroll input is consumed by spell selection. Right-click closes the wheel after attempting a cast. The wheel uses each spell's generated icon from `Resources/SpellIcons` with a small runtime UI overlay until a production UI is wired.

Ampere marks the target powered and queues a durable `lightning:{objectId}` effect; it refuses objects tagged `conductor`. Weave connects the target to its `weave-with:` partner or, without one, to the nearest powered object. Objects tagged `circuit` are wired by hand instead: the first cast holds a thread, each later cast ties it to the next circuit object within four tiles and keeps holding it there, and casting on an already linked pair cuts that cable. `WorldStateService` recomputes `conductor` power after every change: a conductor is powered only while live cables (strength at least `0.2`) lead back to a powered object without the tag. `WeaveCableView` draws every cable (cyan with a travelling spark when live, amber when dark), the held thread with faint lines to every circuit object in reach, and a glow on powered `ampere`/`weave` objects. Gravitate moves one tile after checking bounds, blockers, and mover occupancy. Rotara advances orientation by 90 degrees. Glaciate marks the target frozen and slides it in the supplied direction until blocked or the map boundary. Vis marks the target aligned at its nearest quarter turn. Lost Echoes reveals every object tagged `hidden` within its separate four-cell Manhattan search radius and fires its `on-reveal:<eventId>` hooks.

Map object ids come from `MapObjectDefinition.id`; generated tile objects use `{type}_{x}_{y}`. Puzzle targets use `puzzleTargets` and can require powered, connected, revealed, frozen, aligned, rotation, position, and exact `requiredConnections` conditions. A Glaciate object can carry `slide-goal:x,y`; it slides until that destination, a blocker, or a boundary. `WorldStateService` publishes `StateChanged` and `MapRebuilt`, and persists object transforms and properties, connections, reveals, and unacknowledged effects through the save provider key `spell-world-state`.
