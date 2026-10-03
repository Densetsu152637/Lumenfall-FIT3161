# Ambient NPC schedules and protected optional travel

Base: origin/main 8ceebfa64c20d0f016dcd693c410bb64178d0dd5. Plan commit: 0dc218acda03bcba574721bc18894cb0b34afb36. Draft PR: https://github.com/Densetsu152637/Lumenfall-FIT3161/pull/16.

## Full required scope and acceptance

- Opt-in schedules only for ambient NPCs; never move or gate main quest NPCs unexpectedly. No time-dependent mandatory progress.
- GridNpcNavigator/GridWorld own movement and occupancy. Stable schedule phase survives saves or has an explicit reset policy.
- D03/W07 optional waypoint activates only after both endpoints were visited, preserves walking and cannot bypass suite key, introduction or puzzle gates.
- Reserved north/south/west expansion gates reject travel unless explicitly authored allowed flags permit it; unavailable destinations fail without partial relocation.
- Ambient weighted dialogue remains quest-neutral with no reward/quest actions. Main NPC selection must not become newly randomized.
- Optional content stays disabled until design approval. Preserve IDs, metadata, provider keys and independently developed story/world-route changes.

## Implemented

- NpcDefinition gains ambientOnly and ambientSchedule (enabled, phaseCounterKey, ordered grid stops). AmbientNpcPolicy excludes all principal actors (including both Cinna IDs), quest-dependent visibility and quest/prerequisite dialogue rules. DynamicNpc creates the schedule/navigator/mover components only for valid enabled ambient definitions.
- AmbientNpcSchedule asks the existing navigator for in-map destinations, retries failed routes at most every 0.5 seconds, and cancels queued navigation during dialogue/inventory interaction. Already-started tiles finish under GridMover ownership. Disable cancels navigation; re-enable and runtime replacement reevaluate the current counter.
- Phase is the existing GameEventRuntime counter modulo stop count, including negative/large values. No wall-clock advancement or independent save provider. Authored events change the counter; its value persists through existing event saves. Old saves without the counter use phase zero. NPC spatial position is deliberately not a new saved owner: reconstructed areas start from authored placement and navigate to the saved phase; cached revisits retain their transform and reevaluate it.
- Only the anonymous campus_student and forest_student are marked ambient. Their two adjacent provisional stops and disabled schedule are synchronized narrowly across Resources/NPCs/npcs.json, NpcCatalog.asset and the campus/eastForest baked DynamicNpc definitions. Main quest actor definitions are unchanged.
- Ambient dialogue uses bounded weighted selection without repeating weight entries. All candidate nodes, choices and metadata are checked before selection. Node/choice actions, choice progression conditions, rewards and unknown metadata reject the entire candidate. Only passive ambient/portrait/speaker/NPC identity metadata is allowed; all nine shipped ambient lines remain eligible. Existing principal dialogue selection behavior remains owned by its quest branches.
- The loader exposes TryWaypointTravel(sourceId) separately from walking portals. Disabled D03/W07 defaults are B04.Ground/D03 (20,3) and B02.Ground/W07 (20,8). Both endpoints must be enabled, mutually paired, actually visited by the controlled player's settled cell, and within their existing walkable alcove region. Visit flags are travel.waypoint.visited.D03 and travel.waypoint.visited.W07.
- D03/W07 requires both the chapter1.first_account flag and triggered event plus both endpoints' authored requiredFlags. IDs/map/region are restricted to those two safe alcoves; residential/suite destinations and arbitrary alternative waypoint pairs reject. The endpoints lie outside the suite lock. Walking stays available; waypoint travel cannot complete puzzles or grant keys.
- PortalContentAsset gains allowReservedTravel, allowedFlag and requiredFlags. A reserved source AND destination require explicit opt-in plus true allowed flags; default/blank/false flags reject. The three legacy reserved campus glyph assets remain disabled/destination-less and now carry provisional dormant expansion.reserved.X.* allowed flag names; they are not claimed as approved cardinal gate authoring. No unapproved expansion map is invented.
- GridPortal rejects gated/incomplete authored content before legacy fallback and accepts authored map transfers only from the controlled player. SwitchArea verifies actual source ownership, live registration and player source occupancy, checks both gate policies and validates the available destination prefab before changing maps.
- Loader and DynamicNpc resolve an explicit loader playerMover or the GridPlayerInput owner, preventing ambient movers from being mistaken for the player.
- Unavailable destinations reject before loading. Blocked arrivals reload the previous area and restore the exact previous cell; a failed rollback emits an explicit error. Existing route tests now place the player at the real portal source before calling SwitchArea.

## Setup and provisional design

All new movement and waypoint content remains disabled. To review schedules, enable ambientSchedule.enabled in BOTH the baked anonymous NPC definition and its catalog/source copy. Set ambient.phase.campus_student or ambient.phase.forest_student via existing authored event counter actions. Provisional stops are current cell and one cell east; they are not final approved schedules. Existing anonymous dialogue lines are reused; no new plot or NPC identity is invented.

For waypoint review, explicitly enable both loader optionalWaypoints rows (defaults initialize when the serialized field is absent), retain the authored IDs/map/regions and safe arrivals, and invoke TryWaypointTravel("D03") or TryWaypointTravel("W07") from a reviewed interaction/UI. There is no new always-active input or scene interaction. Future gate activation also requires a real available area/prefab, destination connection and a live portal; toggling a flag alone cannot make a nonexistent map available.

The story branch supplies the real chapter1.first_account event+flag. This branch stays on its independent base and does not import sibling code. The eventual story GridWorld suite-cell guard must remain composed into ordinary walking/pathfinding/teleport checks. D03/W07 does not target the suite and adds no invented six-quest travel prerequisite.

## Validation performed

- Current-source offline compilation passed for Lumenfall.GameEvents (10 source files), Lumenfall.Runtime (27), Lumenfall.World.PlayModeTests (2) and Lumenfall.GameEvents.Tests (6).
- Compiler: Unity 6000.4.10f1 Editor/Data/NetCoreRuntime/dotnet.exe invoking DotNetSdkRoslyn/csc.dll. Readonly reference project: D:/Git Repositories/Lumenfall-FIT3161/Lumenfall/Library/Bee/artifacts/1900b0aE.dag. Every imported reference whose basename starts Lumenfall. is excluded; rebuilt branch GameEvents/Runtime assemblies are used. Response/output files are isolated in this worktree's Lumenfall/Temp/ambient-npc-offline. Compiler emitted existing deprecation/unassigned Inspector-field warnings and no errors.
- Executed python tools/validation/validate_optional_travel.py: PASS, 51 assertions against actual current C# policy/data/GameEvent sources, using minimal Unity value/JSON adapters. Covers principal exclusions, disabled/quest-bound schedules, phase normalization/restore, weighted boundaries, action/reward rejection, all shipped ambient lines, strict reserved flags, both visits, real introduction, restore and safe region boundaries. This does not execute Unity native lifecycle or prove Unity JsonUtility compatibility; compiled NUnit tests provide native restore fixtures.
- validate_runtime_world.py and validate_npc_dialogue.py passed. Narrow source/catalog/baked schedule parity and new metadata/GUID checks passed.
- Authored/compiled NUnit and Unity tests cover policy rejection/restore plus actual portal/waypoint unavailable/blocked rollback, successful safe arrival, real endpoint visits, ambient occupancy/path retry, controlled player ownership and disable/re-enable saved phase. Unity tests were not executed.

## Full pending scope and limitations

Product approval of schedule stops/phase events, waypoint activation and interaction presentation remains pending. Expanded north/south/west maps and live expansion portals do not exist here; dormant gates reject. New ambient content, additional timetable phases or plot dialogue are not authored. Main actors keep their existing stable quest flow.

Shared Unity Editor/MCP/CLI, Play Mode and builds were not launched. Native runtime movement, collisions, saved spatial reconstruction, waypoint rollback under dynamic occupancy, visuals and platform acceptance need the compiled Unity fixtures and a manual walkthrough. This is a disabled, bounded foundation, not claimed final content or release acceptance.

## Merge review

Overlap: DataDrivenWorldLoader, DynamicNpc, GridPortal/PortalContentAsset, NpcDefinition, NpcCatalog, campus/eastForest prefabs, NPC source JSON and existing route tests. Preserve world-route's hardened source/arrival handling and reserved-region rules; compose explicit permitted gates into its strict default reservation policy. Preserve story suite-cell guards and its introduction flag/event, main-cast teaching/lifecycle/dialogue selection, presentation identities and provider ownership. Do not replace shared files with whole baseline copies. No cherry-picks or automatic merges; root alone archives the worktree after clean exact remote parity.
