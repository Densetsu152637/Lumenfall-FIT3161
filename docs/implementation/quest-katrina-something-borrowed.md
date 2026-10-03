# Katrina: Something Borrowed implementation

## Full scope and acceptance contract

Independent branch `codex/quest-katrina-something-borrowed` starts from main baseline `8ceebfa64c20d0f016dcd693c410bb64178d0dd5`. Implement the complete feasible Katrina lesson, local discovery, durable monitor completion and generic recovery reward. The keepsake identity and family history remain unresolved; no narrative identity is invented.

- Finish the terminal teaching dialogue only while `katrina_something_borrowed` is Active to learn Lost Echoes before solving. Opening, acceptance alone, abandonment and inactive/completed quests must not teach. Use shared `spellRewardQuestId` metadata and comma/semicolon spell lists, preserving Ampere compatibility.
- Intended keepsake is `keepsake.katrina.hidden` on campus at `(31,42)`. Synchronize exact map JSON, live WorldAreaAsset definition, prefab WorldObjectRegistration and quest monitor target metadata; inspect Katrina DynamicNpc and NpcCatalog parity.
- Lost Echoes reveals only nearby compatible hidden points in the current map, with readable search/retry/repeated-cast feedback. Initial hidden presentation and revealed presentation after save/load/rebuild must agree with WorldStateService authority.
- Monitor completion requires the correct Active quest, map, stable object and authored position/revealed state. Unrelated or wrong-area reveals fail. Completion is idempotent and durable.
- Award neutral `katrina-keepsake` once through existing progression/inventory flags, regardless of whether inventory UI exists. Remove premature teaching/acceptance dialogue inventory reward.
- Tests exercise terminal lesson gates, local range boundaries/unrelated reveal, completion identity/position, reward idempotence and persistence/rebuilt presentation. Static checks alone do not establish runtime acceptance.

## Plan

1. Commit and push this plan only; manager creates/attaches a draft PR containing the full scope before releasing implementation.
2. Inspect current spell, dialogue, inventory, world registration/save and monitor owners; extend those existing owners with narrowly scoped behavior.
3. Author local search instructions and quest target metadata in the live resources/assets and matching source fixtures without rewriting unrelated JSON or serialized blocks.
4. Add meaningful EditMode behavioral coverage and stable Unity metadata for new test assets.
5. Run dialogue/world validators and relevant Python harness; offline compile current first-party runtime and test sources using read-only Unity/Bee references with all imported `Lumenfall.*` references excluded. Compiler response/output files stay in this worktree Temp.
6. Review committed diff, report exact checks and unexecuted acceptance, commit/push implementation, and hand final evidence/merge overlaps to manager for PR update/review.

## Constraints, risk and rollback

No Unity Editor, Unity MCP/CLI, Play Mode or build operations; no migration rerun, sibling branch imports or automatic merges. Preserve IDs, serialized file IDs/GUIDs, provider keys and save format compatibility. Runtime component/presentation changes and campus prefab/asset edits are medium/high risk and need a licensed visual/Play Mode acceptance pass. Rollback is the independent commit range; no unrelated main content is modified.

Shared merge hotspots: SpellCaster terminal reward handling and Lost Echoes cast; WorldStateService registration/restore/presentation; QuestPuzzleMonitor evaluation/lifecycle/success feedback; GameEventRuntime completion reward ownership; InventoryEncyclopediaUI dialogue reward; Katrina blocks in quest dialogue and campus resources, WorldAreaAsset, prefab and NpcCatalog. Preserve First Light lifecycle/EvaluatePuzzles and later generic reset/feedback changes when combining branches.

## Evidence and pending acceptance

Plan phase only. Implementation, executed offline checks and final manual acceptance checklist will be recorded after manager release. Lost Echoes range is provisionally the existing two-cell interaction radius; final keepsake identity, family history, visual/VFX treatment and licensed Play Mode/render/platform acceptance remain pending.
