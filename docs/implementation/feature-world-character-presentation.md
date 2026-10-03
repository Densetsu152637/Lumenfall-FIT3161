# Character identity and core environment presentation

## Branch and scope

Continue existing `assets` from `ed74bb4` (full inherited history retained); target main. This branch includes earlier forest/campus layouts, LFS/binary asset changes, portraits, player occlusion and Stella puzzle work. Review the presentation increment separately and review the complete inherited diff before integration. Never reset, rewrite, cherry-pick, or remove existing binary history.

Required full scope: distinct approved-name presentation for Player, Stella, Cinna Cirra, Orio, Katrina and Receptionist; legible puzzle props/markers/signage and dormitory props where safely supported; explicit provisional meshes and replacement contract. Preserve `docs/main-cast.md` identity details: Orio relaxed tropical outfit and flip-flops; Stella neat outfit, combat boots, red bow and regular glasses; Katrina long white skirt and white/blue sneakers; Cinna loose comfortable clothing. No invented final style, item backstory, receptionist identity, or console motivation.

## Implementation plan

1. Inspect actual WorldArea assets, baked NPC definitions, NpcCatalog, current visual assets, player and object registration ownership.
2. Add a narrow runtime presentation layer with approved names, identity colors plus readable names/symbols, avoiding ThirdParty material edits and collision/grid changes.
3. Add labels and safe provisional props to actual authored puzzle objects and entrances; keep keepsake generic and reserved destinations unavailable. Presentation must not drive quest state or enable travel.
4. Provide art provenance and replacement handoff, preserve all existing GUIDs/animations and add matching meta files for new assets.
5. Add meaningful behavior/identity tests; offline compile using readonly Unity 6000.4.10f1 references and static serialized inspection. No Unity Editor/MCP/CLI, Play Mode or builds.

## Acceptance and integration contract

Names distinguish player/four friends/receptionist, provisional geometry stays honest, props explain actual state/location without altering gameplay. Baked NPC placements and catalog must be inspected; no marker leaks between cached areas. Material ownership and lifecycle cleanup must be safe. Labels should not expose hidden keepsakes before reveal. Tests cover absent/unknown identity and repeated area activation. Any optional display-only props must preserve collision, anchors and navigation.

Highest overlap: DataDrivenWorldLoader, DynamicNpc, WorldObjectRegistration, campus/B02 authored assets/prefabs and source JSON. Other independent scopes own travel, story locks, quest teaching/targets, spell feedback, UI, camera, save and accessibility. Merge exact blocks and retain their behavior. Inherited branch renames eastForest to westForest and contains custom Stella puzzles; these are integration risks, not new presentation changes.

## Validation and pending acceptance

Plan only at this commit. Implementation and evidence follow in the next commit. Licensed Unity import, Play Mode route/visibility checks, rendered label readability, occlusion/accessibility evaluation, approved final art/style, animation and portrait consistency remain pending. Full main-to-HEAD diff may contain inherited whitespace or integrity faults; record baseline and incremental results separately.
