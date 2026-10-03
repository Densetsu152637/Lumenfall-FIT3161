# Ambient NPC schedules and optional travel

Base: origin/main 8ceebfa64c20d0f016dcd693c410bb64178d0dd5.

## Required scope and acceptance
- Opt-in data-driven schedules only for ambient NPCs; main quest NPCs remain stable and progression has no time dependency.
- GridNpcNavigator/GridWorld retain movement and occupancy ownership. Persist schedule phase in GameEvent flags, with explicit blocked-path retry and reset policy.
- Weighted ambient dialogue must be quest-neutral and cannot grant items, flags or quests.
- Optional D03/W07 waypoint activates only once both endpoints were visited, preserves walking and respects story/introduction/key/puzzle protection flags.
- Reserved north/south/west gates require explicit authored unlock flags; unavailable destinations reject travel without relocation.
- Optional content disabled until design approval; preserve existing assets, IDs and saved provider keys.

## Implementation plan
Inspect existing dialogue, grid, portal and save contracts; add minimal deterministic policy/data and runtime adapters to existing owners. Add focused rejection/restore/boundary tests. Include metadata for new Unity assets. Compile offline using readonly Unity 6000.4 reference assemblies with all baseline Lumenfall assemblies excluded. Never control the shared Editor, run Play Mode or launch a build.

## Validation and remaining scope
Implementation and validation pending. Full runtime/visual acceptance, approved schedule/dialogue authorship and optional waypoint activation require design and Unity review.

## Merge risks
DataDrivenWorldLoader, DynamicNpc and portal assets may overlap world-route/story/presentation branches. Preserve lifecycle, teaching and dialogue selection changes when integrating. No sibling implementation is imported.
