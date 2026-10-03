# Optional item recovery activity

## Required scope and acceptance contract
- Opt-in, disabled by default; neutral recovered item, no invented family/narrative identity or economy.
- Local Lost Echoes targets only, existing world/inventory/event flag owners, clear spell gate.
- One-time reward; reset only a partial search, no replay or removal of a completed reward.
- Cannot reveal Katrina's unrelated keepsake, complete its quest, or gate the main route.
- Test disabled content, unavailable spell, wrong/local targets, partial reset, save/rebuild and repeated completion.
- Document provisional product activation, entry surface, target presentation, unresolved item identity and placement.

## Plan and implementation
The initial plan commit preceded implementation and the draft PR. Implementation follows the planned local reveal API, optional activity bridge, existing flag persistence, bounded preview controls and behavioral tests.

`OptionalRecoveryActivity` is auto-created after scene load, with `activityEnabled=false`. Its disabled path creates no targets, controls, quest or rewards. Explicit developer/product opt-in uses `SetActivityEnabled(true)` or the serialized field before play. This activation configuration is not saved player progress. Begun/search progress/reward flags are saved through existing GameEventRuntime, with no extra save manager/provider.

An enabled preview provides Begin / resume search, Reset partial search, and status messages for missing spell, wrong map, unavailable cells and completed recovery. It rejects paused gameplay, inventory and dialogue overlays. Begin checks Lost Echoes availability and the configured map, creates two small neutral sphere placeholders without collision/occupancy/quest triggers, and starts a search. Default provisional locations are campus `(30,40)` and `(34,40)`; GridWorld must report both available and free of portals. Invisible items become visible on their individual reveal; the preview text gives explicit search coordinates.

The activity owns only `optional.recovery.item.1` and `.2`; rules check exact IDs, configured cells, map, compatibility and tags. SpellCaster rejects every spell except Lost Echoes on these targets. `WorldStateService.TryRevealObject(id, searchCell, radius)` requires current-map, compatible, unrevealed `hidden` + `lost-echoes` targets inside Manhattan radius, and never fans out to other items. Normal caster radius is two cells; direct Cast uses GridMover.CurrentCell as well as nearest-target filtering. Optional markers additionally require the activity's saved active-search flag and exact owned IDs. This does not teach or grant Lost Echoes; it becomes available through existing progression (Katrina lesson timing is independently owned by the Katrina branch).

Observing both local reveals sets one durable `optional.recovery.reward-claimed` receipt and `inventory.keyitem.optional-recovered-item`, shown as **Recovered item (optional)** in the existing journal. No counters, consumables, sell value, replay, rewards to any main quest, or invented narrative identity are introduced. Repeated observation/begin/reset after completion cannot award again or erase the receipt/item.

## Persistence and reset policy
- Found flags `optional.recovery.found.<targetId>` mirror actual local reveal changes immediately through WorldStateService.StateChanged.
- Begin/resume uses `optional.recovery.started` and `optional.recovery.search-active`; no parallel quest is added.
- Before completion, Reset clears only these two targets' revealed state and found flags. It keeps search active for retry and preserves unrelated objects, quests, inventory and flags.
- After completion, reset and replay are rejected; the item and receipt remain saved.
- Map reentry/rebuild recreates only these transient targets and reapplies their saved found flags. World snapshots continue through the existing provider; event flags retain progress even when marker registrations are absent during the loader's rebuild.
- Disable/destroy unsubscribes captured world/runtime events, suspends the search and unregisters only exact optional IDs, so importing a previously active save cannot revive stale markers while disabled. Reenable requires Begin/resume; progress survives suspension.
- Runtime replacement is detected on enabled periodic checks, with balanced subscription rebinding. Disabled overhead is an immediate Update return; enabled owner resolution is throttled to 0.2 seconds.

## Setup and unresolved product decisions
Use the bootstrapped component; do not add a second activity component. To preview, opt in with `SetActivityEnabled(true)`, visit campus, learn Lost Echoes through normal progression and choose Begin. The serialized map/cells may be configured before activation. The two target cells must pass occupancy and portal checks. Product approval must choose final placement, search clues, item identity, presentation assets and whether to expose this activity to players. Current spheres, exact-coordinate hints and IMGUI panel are provisional preview presentation, not final approved art/UI. Activation stays false by default.

Live DataDrivenWorldLoader loads WorldArea assets/prefabs rather than map JSON. No authored map asset, prefab, NPC, quest, dialogue, route condition or scene was modified. This deliberate transient preview is registered in the actual WorldStateService after map activation and restored from existing event flags. It does not pretend that JSON fixture edits activate playable content. No final content activation is claimed.

## Validation
- Offline Unity 6000.4.10f1 Roslyn compile: current GameEvents + Runtime assemblies PASS; all current GameEvents EditMode tests including ten recovery tests compile PASS. Shared read-only references: `D:/Git Repositories/Lumenfall-FIT3161/Lumenfall/Library/Bee/artifacts/1900b0aE.dag`; **all Lumenfall.* imported references excluded**. Adapted response files/scripts and outputs are local ignored `Lumenfall/Temp`; no shared Editor/MCP/CLI/play/build launch.
- Ten authored behavioral NUnit tests cover disabled and spell gates, local reveal/Katrina independence, wrong tags/map/compatibility/radius and exact boundary, unrelated optional IDs/relocated targets, scoped reset/retry, partial save/import/map reentry, completion receipts/idempotence, completed save/replay rejection, disabled restore cleanup, direct SpellCaster range/other-spell rejection, and missing targets/wrong map/suspension. These Unity tests are compiled **but not executed**; licensed EditMode/PlayMode and rendered input/marker acceptance remain pending.
- Executed `validate_runtime_world.py`: PASS (8 maps, 7 NPCs); `validate_npc_dialogue.py`: PASS (6 quests, 15 dialogues, 9 events); Python validator unit harness: PASS (4 tests).
- Compiler emitted existing serialized-field/obsolete-API warnings and response-file diagnostic info; no compilation errors. Static/offline checks do not prove gameplay, rendering, UI usability or platform acceptance.
- Executed Unity repository integrity validator: the same three baseline ThirdParty faults remain (orphaned URP metadata, UTF-16 FantasyCastle ReadMe and Tavern README conflict marker); no new scoped integrity errors.
- Final committed-range whitespace check and remote parity are recorded in the PR handoff after commit/push.

## Merge overlaps and risks
WorldStateService local reveal and SpellCaster targeting overlap Katrina's independently implemented `TryRevealObject` API. Keep one compatible implementation, retain optional exact-ID/active-search checks and preserve Katrina reveal presentation. Inventory changes append one neutral item/display label; preserve production UI and generic item metadata changes. Preserve First Light runtime lifecycle/teaching and later spell feedback/quest reset improvements when resolving shared files; never replace another branch's full file with this baseline.

Placement/visual/input acceptance remains provisional and unexecuted. The neutral item never substitutes for `inventory.keyitem.katrina-keepsake`; no main route predicate reads optional recovery flags. A combined-branch compile and six-quest/route acceptance pass are required after integration.
