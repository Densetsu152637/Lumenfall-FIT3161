# Audio feedback and mix implementation contract

Branch: `codex/feature-audio-feedback-mix`; independent baseline `8ceebfa64c20d0f016dcd693c410bb64178d0dd5`.

## Required scope

Implement event-driven forest, campus, interior, dialogue, mystery and sleep/wake audio contexts, spell outcomes, player steps, UI feedback and quest feedback. Add clamped persisted master/music/SFX/ambience controls preserving `lumenfall.master-volume`. Keep gameplay state in existing runtime/world/movement owners. Refresh context after map restore and event-runtime replacement and balance enable/disable subscriptions. Provide safely reusable UI/accessibility contracts.

No audio clips or mixer assets were found in repository inspection. Missing approved media must be visible configuration state and safe at runtime. Do not generate or borrow license-unknown media, silently reference nonexistent assets, or mutate editor mixer assets.

## Implementation plan

1. Add focused deterministic context/mix policy and runtime playback bridge, using existing event surfaces and cached clip configuration.
2. Add code/config media manifest with explicit handoff and bounded source ownership.
3. Integrate startup volume controls narrowly and coordinate volume authority with accessibility and production UI branches.
4. Add behavior tests for contexts, clamp/persistence boundaries, missing configuration, restore and lifecycle where feasible.
5. Compile runtime/test assemblies against read-only Unity 6000.4 reference libraries, write outputs only under this branch Temp, and run existing offline content validators.

## Review criteria

Forest/campus/interior base state returns after dialogue; mystery/sleep flags take precedence deterministically. Teleports and map restore must not emit walking feedback or historical quest fanfare. Saved zero volume stays zero. Invalid float/settings input clamps safely; missing clips never crash. Domain reload, disable/re-enable and runtime replacement cannot duplicate subscriptions/music.

## Constraints and remaining acceptance

No shared Unity Editor, MCP, CLI, Play Mode or builds may be launched. Offline compilation/static checks do not establish audible, visual or platform acceptance. Approved media selection, audition, mixing, spatial tuning and Play Mode remain explicit handoff work. New Unity assets include stable unique metadata.

## Merge overlaps

`StartupMenu` currently owns master volume; accessibility and production UI branches also touch settings/UI. `GameEventManager` runtime lifecycle is changed by First Light; preserve its replacement notification implementation during merge. Chapter story flag/interaction IDs must be aligned with its authored route rather than invented resolved mystery content. Avoid overwriting whole shared files.

## Evidence

Pending implementation and review; this initial contract is not a delivery claim.
