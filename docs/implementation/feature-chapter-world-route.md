# Chapter 1 world route

## Contract and inherited basis
Continue existing feat/game-systems at 56322c1 without reset, imports from sibling branches or editor migration. Inherited forest is westForest (old eastForest save IDs normalize); inherited Stella circuit completion and all authored assets remain intact. The full PR necessarily includes that substantial inherited work and needs a separate asset/content review.

Make the production entry deterministic using the existing authored WorldAreaAsset/prefab loader. Ensure one player, GridWorld, camera, dialogue, caster, puzzle monitor, save/provider and startup authority. Harden required forest/campus/B01/B02/B04 round trips: collision-safe reachable arrivals, footprint mapping, bounce suppression and failure restoration. Block and label reserved rooms/gates. Preserve GridWorld occupancy, bounds and diagonal corner authority. Story predicates and suite-key interactions belong to chapter-one-story and must be integrated at merge.

## Implementation plan
1. Worker bootstrap: inspect existing SampleScene and lifecycle; narrowly wire missing production components, deterministic start and author meaningful PlayMode acceptance cases.
2. Worker travel: make loader transitions transactional, reject invalid/reserved/unregistered destinations, clamp footprint entry offsets, verify live destination and restore origin on failure; author travel tests.
3. Manager: inspect authored route graph and reserved-region fixtures, synchronize narrow configuration where needed, add static scene/portal route validation, audit incremental changes from 56322c1 and the inherited full diff from main.
4. Compile current runtime and tests against installed Unity 6000.4.10f1 and read-only shared Bee references; run static validators. No Unity Editor, MCP/CLI, Play Mode or build operations.

## Acceptance and merge boundaries
Tests must exercise observable behavior for reset, repeat entry, reserved rejection, blocked/missing arrivals and restored map/cell, not only source strings. Static serialized checks cover scene authorities and .meta/GUID references. Authored area assets/prefabs are runtime authority; map JSON is fixture/planning source. World-route owns bootstrap and baseline travel; story owns suite key predicates and map interactions. Camera/UI/save/presentation branches overlap scene/loader APIs; preserve their narrow changes when merging.

## Pending validation
Implementation in progress. PlayMode tests will be authored and compiled but not executed here. Visual, platform, licensed build and whole-Chapter acceptance remain pending. No production-readiness claim from offline compilation.
