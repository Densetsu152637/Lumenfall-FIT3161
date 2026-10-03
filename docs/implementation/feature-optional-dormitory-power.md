# Optional dormitory grid power activity

Branch: codex/feature-optional-dormitory-power. Independent base: origin/main 8ceebfa64c20d0f016dcd693c410bb64178d0dd5.

## Full scope and acceptance contract

Provide an opt-in, disabled-by-default optional Ampere/Weave power restoration activity. Existing WorldStateService owns objects/cables and GameEventRuntime owns saved spell/completion flags. Require the intended source/sink connection, powered source and actual live attenuation at least 0.2. Unrelated cables, stale saved strength, wrong map, missing spells and missing authoring cannot satisfy it. Grant only one optional completion flag, once, with no inventory/economy award. Define reset, replay, persistence and actionable failure feedback. Main route and Chapter 1 quest prerequisites remain unchanged; reserved D05 remains closed.

## Planned implementation

- Add a reusable explicit opt-in component, without global bootstrap or enabled scene wiring, which binds existing owners and supplies status, optional cast entry, reset and replay APIs.
- Gate entry to a configured non-reserved dorm area after Ampere and Weave have already been learned; do not grant spells. Validate dedicated reciprocal authored endpoints.
- Evaluate live power using current source/cell state, independent of stored legacy strength. Reuse existing Cast and world mutation events, with scoped disconnect/reset support.
- Persist partial state in the existing spell-world-state provider and completion in the existing event provider. Reset only the dedicated source power and intended cable; completed flag survives replay.
- Add meaningful integration tests for casts, gates, wrong endpoint/map, live range/power, save/rebuild, one-time completion and reset. Offline compile from read-only shared Unity 6000.4.10f1/Bee references; do not launch Unity.

## Provisional authoring and product boundary

Product approval is required for activation, final location, props/signage and player-facing entry UI. Proposed configurable defaults: B04 (existing interior), power.dorm.optional.source and power.dorm.optional.sink, two-cell spacing, reciprocal weave-with tags plus optional-dormitory-power tags. This branch will not add these objects to active scene/prefab/map data, add a quest prerequisite or open D05. The component supplies an actual callable activity bridge for approved authoring; visible interaction wiring will remain documented pending activation.

## Merge review

Potential overlap: WorldStateService scoped connection removal with String and generic spell reset branches. Preserve String live connection rules, SpellCaster teaching/lifecycle/feedback changes, and save provider fixes. Avoid changing baseline quest/map/prefab content. Manager must independently review final diff and evidence before root handoff.

## Validation status

Plan only at this commit. Implementation, tests, checks and remaining setup will be recorded after substantive work. Editor import, Unity tests, visuals/input and platform acceptance remain unverified under the shared Editor prohibition.
