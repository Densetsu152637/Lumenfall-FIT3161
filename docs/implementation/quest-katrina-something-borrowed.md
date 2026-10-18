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

## Repairs after merging `feat/rotara-puzzle`

Merge commit `0f8fb85` resolved two Katrina conflicts in ways that broke this branch's code, and the merged campus layout moved the keepsake. These follow-up changes touch only Katrina's work, plus Orio's two dialogue-file entries noted below.

- `WorldStateService`: restores this branch's hidden-renderer cache (`PruneDestroyedRenderers`, `SyncPresentation`, tag-preserving `ApplySnapshot`) alongside Rotara's `NormalizeMapId` save migration. The merge commit did not compile without it.
- `InventoryEncyclopediaUI`: removes the leftover `OnQuestStatusChanged` unsubscription; `GameEventRuntime` owns `keyItemReward`.
- In the merged layout the keepsake is at `(42,60)` in C04 and Katrina at `(40,68)`. The Katrina target in `Maps/campus.json` and `campus.asset`, the `katrina_something_borrowed_monitor`, Katrina's dialogue text and `KatrinaBorrowedWorldTests` now use `(42,60)`.
- `quest_npc_dialogue.json`: the merge kept the pre-Rotara Orio quest and dialogue, which Nicholas's Katrina commit never changed; the conflict came only from adjacent lines. Both entries are restored exactly from `feat/rotara-puzzle` (`6f5bb18` and Nicholas's `a2ba96c`): lesson metadata that teaches Rotara, the `report` choice and the Rotara-only reward. The Orio offline harness passes 35 assertions.

## Garden recess (`C04.Recess`)

Katrina and her search now live in a small interior area modelled on Stella's B02 room, using the Rotara pattern: finishing Katrina's lesson while Something Borrowed is Active teaches Lost Echoes, the puzzle is solved with it, and quest completion grants the keepsake. The campus keepsake object, its campus puzzle target and its `campus.prefab` instance are removed; the keepsake exists only in the recess.

Katrina stands on campus (see "Katrina on campus and general Lost Echoes" below); the recess holds only the search.

- Area: `World/Areas/C04.Recess.asset` (15 x 11 cells, spawn and landing stone `(7,1)`, keepsake `(12,9)` tagged `hidden`, four blocking planters) with the minimal prefab `Prefabs/C04.Recess.prefab`, listed in SampleScene's `availableAreas`. `KatrinaRecessRoom` on the prefab root builds the placeholder floor, hedges, planters and landing stone from the area definition, registers the keepsake as an invisible marker (it is picked up the moment Lost Echoes reveals it, so the spot keeps its unfound look), and registers the keepsake on every activation so saved reveal state is restored.
- Travel: `KatrinaRecessEntrance` builds portal `P.CAMPUS.RECESS` at campus `(39,49)`, open whenever campus is loaded, with no quest requirement. The recess landing stone (`P.RECESS.CAMPUS`) is always open back to campus `(39,49)`. The paired portal assets suppress an immediate re-entry on arrival in both directions.
- Completion: `katrina_something_borrowed_monitor` uses `mapId` `C04.Recess` and position `(12,9)`. Katrina's lesson, reminder, quest description and recovery message refer to the recess, and `KatrinaBorrowedWorldTests` uses the recess map and cell.
- Both portals are built at runtime, so the PlayMode baked-route count of 10 is unchanged. Since the `campus-updates` portal rework, `PortalContentAsset` only holds the connection and a `GridPortal`'s cell is its transform position, so the entrance is placed at the fixed cell `(39,49)` and the landing stone at the recess spawn `(7,1)`. The recess has no legacy `Resources/Maps` JSON; its layout lives only in the area asset.

## Lesson access, return to Katrina and usage guide

- Lesson: Katrina's dialogue uses the new `spellLesson` metadata instead of `spellReward`. When the lesson finishes while the quest is Active, `GameEventRuntime` sets `spell.lesson.LostEchoes`. `SpellCaster` treats a lesson flag as castable, but the inventory lists only learned spells (`SpellCaster.IsSpellLearned`, which needs `spell.unlocked.*`). The quest's own `spellReward` still sets `spell.unlocked.LostEchoes` on completion, so the spell joins the inventory only then.
- Finding: the monitor's completion event is now `found_katrina_keepsake`. It sets `inventory.keyitem.katrina-keepsake` and plays the Narrator line "Looks like you found the keepsake. You take it with you. You should return it to Katrina." The quest stays Active.
- Returning: Katrina's `report` choice ("I found the keepsake.") needs the carried keepsake flag and triggers `complete_katrina_something_borrowed`, following Orio's report pattern. Her hint choice is hidden once the keepsake is carried.
- Guide: `KatrinaLostEchoesGuide` draws a top-right panel while the quest is Active and the lesson flag is set. It shows the select (current Spell slot 7 and spell wheel keys), range and right-click cast steps, switches to a return reminder once the keepsake is carried, and disappears on completion.
- Tests: `KatrinaBorrowedProgressionTests` now expects the lesson to set only the lesson flag and adds a find-then-return test. `validate_npc_dialogue.py` expects 10 events and checks the find and report wiring.

## Katrina on campus and general Lost Echoes

- Katrina: the updated campus prefab already places the `Katarina.fbx` model, with the project `ChibiCharacter_Controler`, at cell `(29,70)`. That instance now carries a `DynamicNpc` with her catalog record, so she has her model, animation and status-based dialogue on campus. `npcs.json` and `NpcCatalog.asset` use `mapId` `campus` at `(29,70)` with layer symbol `k`, marked in both `campus.npcs.txt` copies and the catalog's campus layer. The recess no longer spawns her; `KatrinaRecessRoom` only builds the room, registers hidden objects and hosts the return portal.
- Lost Echoes is general purpose. `WorldStateService.IsLostEchoesTarget` accepts any spell-compatible object tagged `hidden`; the old `lost-echoes` tag is no longer needed. `RevealHiddenAround(cell, radius)` reveals every unrevealed hidden object on the current map within the Manhattan radius. Right-clicking with Lost Echoes selected calls `SpellCaster.CastLostEchoes()`, an area search around the player that needs no target, and reports general feedback. Any area can add a hidden object by tagging it `hidden`, and a quest can watch it with a `requiredRevealed` puzzle target.
- Katrina's lines and the quest description describe the recess portal by landmark rather than coordinates: east of the fountain, right beside the Library (`P.C04`). The guide's return reminder points to Katrina on campus.
- Tests: `KatrinaBorrowedWorldTests` now expects a hidden-only object to be revealable and adds an area-search test.

## Lost Echoes reveal hooks and feedback

Lost Echoes gained data-driven reveal hooks (`on-reveal:<eventId>` tags), a range preview, a cast pulse and a reveal flash with sound. See the Lost Echoes section of [`spells.md`](../spells.md). Lost Echoes now casts with right-click or `F`; the player picks the keepsake up with `E` (it is tagged `hidden` and `pickup`). It can be picked up even before it is revealed; Lost Echoes is how it is located: only a revealed keepsake shows a glowing marker and the "E Pick up" prompt. Picking it up while concealed reveals it first. `katrina_something_borrowed_monitor` additionally requires `requiredCollected`, so the found event and the Narrator line fire on pickup, not on reveal. `QuestHintOverlay` merges `requiredCollected` and hints "Pick up the revealed keepsake with E" once it is revealed, otherwise "Find the hidden keepsake and pick it up with E; Lost Echoes helps locate it". `KatrinaBorrowedWorldTests` adds the pickup step to the monitor test. `LostEchoesRevealEventTests` covers hooks firing once, honouring event conditions, not firing on restore, and untagged reveals.

Checks: offline runtime and EditMode compiles with no errors; `validate_npc_dialogue.py`, `validate_runtime_world.py`, `validate_unity_project.py` and the Python validator tests pass. Not run: Unity import of the new assets, Play Mode traversal, save/load inside the recess, or the EditMode test runner.


## Three memory anchors: synchronous pickup credit (2026-10-18)

The current quest is **Three Lost Echoes**. Katrina's brass bookmark is in the Library
(`B04.Ground`, `katrina.item.library`), the silver ribbon in the campus sakura garden
(`campus`, `katrina.item.garden`), and the riverglass chime beside the forest stream
(`westForest`, `katrina.item.stream`). The three `Kat_item_1/2/3` prefab markers are
registered by `KatrinaQuestItemMarker`. Earlier single-keepsake/recess descriptions
above record previous implementations.

Each marker now carries its matching `on-pickup:found_katrina_*_item` event tag.
`WorldStateService` sets `collected` before invoking that hook, so a valid pickup
while the quest is Active sets its anchor inventory flag during the same call.
Credit no longer depends on staying in the map until the monitor's next 0.1-second
poll. The found event also owns its recovery narration, once; these three monitors
retain collected/revealed requirements but no separate success dialogue. They
remain a fallback for an item collected before quest activation or restored without
its credit, when its map is visited while the quest is Active. Inactive quest event
conditions still reject credit, reveal alone is insufficient, and unrelated Library
curios do not contribute. All three anchor flags permit returning to Katrina; the
pickup itself does not complete the quest. Existing object IDs, prefab GUIDs and
save formats remain unchanged, and loading a save does not replay pickup hooks.

`AuthoredWorldPlayModeTests.KatrinaAuthoredPickupsCreditBeforeMonitorAndMapTransition`
loads the actual area prefabs, checks each registered marker, reveals/collects all
three without yielding to the polling monitor, checks immediate flags and exactly
one recovery narration each, transitions maps, returns the anchors, and round-trips
runtime progress. It must fail on the pre-fix pickup-credit assertion. Runtime
execution remains a separate validation step; this change addresses a demonstrated
source-level timing gap, not a confirmed reproduction of feedback issue #42's
unspecified Library item/build/save sequence.

The adjacent `KatrinaInactivePickupUsesMonitorFallbackWithoutRestoreReplay` case
checks that an unrelated Library curio and a pickup before quest activation do not
grant anchor credit, that activation allows the existing monitor fallback, and
that save restoration plus a cached-area revisit does not replay its narration.
