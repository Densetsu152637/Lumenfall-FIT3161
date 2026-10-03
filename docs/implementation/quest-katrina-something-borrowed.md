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

Plan-only commit `05a2bbf` was pushed before manager created and attached draft PR [#14](https://github.com/Densetsu152637/Lumenfall-FIT3161/pull/14) and released implementation.

### Implemented behavior

- `GameEventRuntime.FinishDialogue` grants authored spell flags only after terminal Continue with `spellRewardQuestId` Active. Missing/unknown/inactive/completed quest gates fail. Quest `requiresQuest` supports comma/semicolon `questId:Status` entries; optional dialogue `spellRewardPrerequisiteQuestIds` requires known Completed quests. Malformed/unknown prerequisites fail. Spell reward lists accept comma/semicolon delimiters and canonical names case-insensitively. First Light now has explicit generic gate metadata; opening, choosing leave, accepting alone or calling End does not teach.
- Runtime quest completion owns generic `keyItemReward` inventory flags before notifying observers. Katrina's teaching dialogue no longer grants a keepsake. This works with no caster/UI observer, is idempotent, and persists in existing event-runtime JSON. Import reconciles missing item flags from legacy Completed quest saves before StateRestored; Active quests receive no restore-time reward. Existing completed-quest spell rewards remain supported.
- `WorldStateService.TryRevealObject(id, searchCell, radius)` requires Ready current-map state, a compatible object carrying both `hidden` and `lost-echoes`, an unrevealed state, and Manhattan distance at most radius. It reveals only the requested point. Negative radius, distant/incompatible/unhidden/repeated targets fail without mutation. SpellCaster uses the attached GridMover current cell and the same two-cell rule for direct and input casts; nearest-target selection filters unrevealed echo targets. Readable feedback covers revealed/new/no-trace search attempts and no nearby input target.
- Authored hidden objects disable renderers immediately on registration. Original enabled flags persist in a Renderer-keyed service cache across cached-area returns/new game and preserve authored disabled child renderers. Destroyed renderer entries are pruned on rebuild/reset. Save restores apply the revealed flag to presentation while keeping current registration tags/compatibility/map identity authoritative. Non-hidden renderer controllers retain ownership; GameObjects and registration stay enabled.
- Campus map JSON and live WorldAreaAsset target both require the intended keepsake revealed at `(31,42)`, matching the existing monitor. Monitor `EvaluatePuzzles()` exposes deterministic evaluation and optional `successDialogueId` opens one neutral recovery message after successful completion. Repeated/completed/restored completion does not replay success.
- Katrina's lesson, objective and reminder describe the actual garden-alcove position and two-cell search/retry rule. This is a provisional single-point search; a multi-point trail and final trail/VFX design remain pending.

### Authoritative authored parity

The existing campus prefab keepsake transform/WorldObjectRegistration is already `(31,42)`, compatible, with `hidden` and `lost-echoes` tags; its file IDs/GUIDs and component block remain unchanged. Katrina's baked DynamicNpc and NpcCatalog both retain campus `(23,40)`, symbol `k`, and exactly the same NotStarted/Active/Completed `katrina_something_borrowed_dialogue` rules (weight 5) as NPC JSON. No prefab/NpcCatalog mutation or editor migration was needed. The scoped JSON semantic diff preserves every unrelated quest/dialogue/event apart from First Light's necessary generic lesson gate. A temporary encoding regression was repaired from baseline Git blob bytes and the final UTF-8 diff preserves unrelated curly apostrophes.

### Executed validation

- `python scripts/validate_npc_dialogue.py`: PASS, 6 quests / 16 dialogues / 9 events. The validator's exact 15-dialogue assertion was changed to allow valid additional dialogues while preserving all existing reference/lifecycle checks.
- `python scripts/validate_runtime_world.py`: PASS, 8 maps / 7 NPCs.
- `python -m unittest discover -s scripts/tests -v`: PASS, all 4 existing Python validator tests.
- Offline Roslyn compilation against Unity 6000.4.10f1 references: PASS GameEvents (10 sources), Runtime (24), EditMode test assembly (8), authored-world PlayMode test assembly (1). All imported `Lumenfall.*` and Assembly-CSharp references were excluded; current branch output assemblies were used. Correct Unity .NET 4.8 references were supplied to NUnit test compilation. Existing obsolete Unity API / serialized-field warnings remain; no compile errors. Response files, logs and outputs are under `Lumenfall/Temp/borrowed-validation` and are not committed.
- The 7 new committed pure progression tests executed against current GameEventRuntime/Catalog/Dialogue source in standalone .NET 8 with a System.Text.Json and Resources adapter: PASS all 7. This exercises actual progression methods and authored JSON without a Unity engine, but does not establish Unity JsonUtility equivalence. Log: `Lumenfall/Temp/borrowed-validation/progression-executed.log`.
- All 14 new behavioral cases compile: 7 progression cases executed through that adapter; 7 world/caster/renderer/monitor cases await Unity execution. They cover terminal/abandonment/active/prerequisite guards, delimited rewards, item authority/idempotence/legacy saves, Manhattan boundary and unrelated target rejection, direct distant casts, original disabled renderers, same-instance map return/new game, save/rebuild with missing saved tags, non-hidden controller preservation, wrong-map/position/unrelated/inactive monitor rejection and no completion replay after restore.

### Pending licensed acceptance and merge constraints

No Unity Editor, MCP/CLI, Play Mode, test runner, build or rendering was launched. Check a fresh slot: accept/leave/end without reward; finish lesson, walk from Katrina to the garden alcove, search outside/at the radius, reveal/recover and see inventory/success once. Save before/after reveal; return via another map; load into rebuilt and cached campus; start a new game; verify renderer state and one-time completion. Verify authored disabled renderer children, actual alcove accessibility, input overlays and right-click feedback placement in licensed Play Mode.

Baseline loader limitation: loading the same already-active cached area does not retrigger WorldObjectRegistration after BeginMapRebuild clears entries. The world-route scope must resolve that lifecycle integration; these tests manually re-register objects and do not claim loader/new-game runtime acceptance. Final keepsake identity, family history, final art, a multi-point trail, VFX and platform/visual acceptance remain unresolved. Existing baseline completion semantics allow an already solved object to complete when its quest becomes Active.

During merge preserve First Light's `GameEventManager.RuntimeChanged`, captured-runtime subscriptions and monitor cleanup around this branch's `EvaluatePuzzles`/success dialogue addition. Spell progression now lives in GameEventRuntime: combine later generic SpellCaster teaching branches without duplicate reward observers or lost prerequisite gates. WorldStateService cache/restore blocks overlap spell reset/feedback and String live connection work; merge individual changes, never overwrite complete baseline files. Inventory completion reward ownership overlaps production UI; keep UI as an observer. Campus target/quest dialogue/validator changes overlap other quest and content-validation branches. No sibling commits were imported.
