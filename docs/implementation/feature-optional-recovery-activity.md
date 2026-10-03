# Optional item recovery activity

## Required scope and acceptance contract
- Opt-in, disabled by default; neutral recovered item, no invented family/narrative identity or economy.
- Local Lost Echoes targets only, existing world/inventory/event flag owners, clear spell gate.
- One-time reward; reset only a partial search, no replay or removal of a completed reward.
- Cannot reveal Katrina's unrelated keepsake, complete its quest, or gate the main route.
- Test disabled content, unavailable spell, wrong/local targets, partial reset, save/rebuild and repeated completion.
- Document provisional product activation, entry surface, target presentation, unresolved item identity and placement.

## Implementation plan
1. Replace global Lost Echoes reveal with the independently agreed WorldStateService.TryRevealObject(id, searchCell, radius): require hidden + lost-echoes tags, compatible target and local Manhattan range. SpellCaster uses GridMover.CurrentCell and filters nearest candidates.
2. Add an opt-in optional recovery component and deterministic rules using existing GameEventRuntime flags and WorldStateService. Provide a small entry/status/reset surface and two explicitly optional, nonblocking, neutral runtime markers after Begin. Keep authored WorldArea assets/prefabs and six quest resources untouched.
3. Keep progress/reward under optional.recovery.*; item ownership under inventory.keyitem.optional-recovered-item. Re-register runtime targets on map rebuild from saved activity flags; reset only the activity's revealed state, retain reward and unrelated world state. No separate save provider.
4. Add behavioral EditMode tests exercising state owners, rejection boundaries, save/import/rebuild and idempotence. Compile offline with Unity 6000.4.10f1 references; no Editor/MCP/CLI/play/build operations.

## Integration and provisional decisions
Live loader uses WorldArea assets/prefabs; JSON map data are not the runtime entrypoint. Runtime targets are scoped to an enabled component's configured map and checked cells, preserving authored assets. Default activation is false and no quests require this component. Product approval must choose placement and final interaction/art; placeholders and menu are a bounded preview foundation.
Shared merge overlap: WorldStateService.TryRevealObject and SpellCaster LostEchoes targeting must reconcile with Katrina's independently implemented identical API. Preserve First Light lifecycle, teaching and later feedback changes in shared files; do not wholesale replace them.

## Validation status
Plan only at this commit. Implementation, tests and exact final results will be recorded here before handoff.
