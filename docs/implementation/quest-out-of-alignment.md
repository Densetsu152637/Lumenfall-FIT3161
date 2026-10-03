# Cinna Cirra: Out of Alignment

Independent branch `codex/quest-out-of-alignment`, started at `origin/main` baseline `8ceebfa`; existing draft PR 5 is reused. The full contract is terminal Active-only Vis teaching, exact target rotation plus alignment, authored parity, readable objective/success, scoped reset/retry, saved/rebuilt progress, deterministic trial handoff, and meaningful rejection/restore tests. No Unity Editor, Unity MCP/CLI, Play Mode or build operations were used.

## Implemented

- The existing provisional orientation marker `orientation.cinna.align` stays at campus `(40,22)`, authored angle 0, with target `vis-target:90`. This remains a generic marker category, not approved final art or prop design.
- Completing Cinna's accept/reminder terminal node teaches Vis only while `cinna_out_of_alignment` is Active. Opening, acceptance without terminal Continue, leaving, abandonment and a Completed dialogue do not teach it. The existing saved `spell.unlocked.Vis` flag and quest-completion spell rewards remain compatible. The generic lesson guard supports the shared `spellRewardQuestId` / optional `spellRewardPrerequisite` contract and the baseline First Light fallback.
- Vis requires the `vis` compatibility tag, rejects malformed/non-quarter-turn/duplicate target declarations (including an invalid declaration following a valid one), normalizes quarter turns, and aligns to the authored target. Generic `vis` targets without an explicit angle retain nearest-quarter-turn behavior. Recasting an already correct/aligned marker returns false without another world-state notification.
- Both the live map predicate and quest monitor require this exact object, aligned AND normalized rotation 90. Wrong angle, facing-only, aligned-only, and other objects fail. Source map JSON, live WorldArea asset and prefab registration agree; existing GUIDs/file IDs are preserved.
- The quest description/reminder explains the actual ninety-degree rule. Near the marker, a provisional existing IMGUI panel shows authoritative facing/alignment, the target, reset entry and completion/next trial at `(20,34)`.
- Press R within the existing two-cell interaction radius or use the nearby Reset marker button while the quest is Active on campus. Dialogue/inventory overlays and pause block reset. Reset changes only the marker's authored rotation and aligned state, preserving spells, completed quests, unrelated flags/objects and map archives. The direct reset API follows the existing direct Cast integration style; player proximity is enforced by its input/UI entry points. Completed/NotStarted quests and other maps reject it; an already-reset marker is a no-op.
- `WorldObjectRegistration` retains the original authored rotation across cached prefab disable/re-enable, and forwards it through an optional `RegisterObject` parameter. Registration reseeds the visual transform before saved mutable state is restored, including new-game reset of cached rotated instances.
- Save restore keeps mutable progress but retains current authored ID/map/footprint/compatibility/tags. A baseline save with only `[vis]` therefore acquires the new live target tag rather than making the ninety-degree rule unsolvable. No save schema/provider-key change or second save authority was introduced.
- Cinna's trial NPC has only its three next-quest status rules; the old Completed alignment dialogue can no longer be randomly selected there. Source NPC JSON, NpcCatalog and the baked campus DynamicNpc block agree. Orientation hide/trial show gates are retained, and ambient weighted selection code is untouched.

## Validation performed

- License-free Roslyn compile of GameEvents, the first-party runtime, and the full GameEvents EditMode test assembly: PASS. Existing response files under this branch's `Lumenfall/Temp/codex-compile` use read-only `D:/Git Repositories/Lumenfall-FIT3161/Lumenfall/Library` reference assemblies; stale imported `Lumenfall.*` references were filtered, and output stayed in this worktree. The existing compiler response-file, unassigned serialized-field and deprecated Unity API warnings remain. Compilation does not execute Unity tests.
- `python scripts/validate_npc_dialogue.py`: PASS, 6 quests / 15 dialogues / 9 events. Its expected trial count now matches the three deliberately retained rules.
- `python scripts/validate_runtime_world.py`: PASS, 8 maps / 7 NPCs.
- `python scripts/validate_out_of_alignment.py`: PASS, scoped source/baked marker position/rotation/metadata, map and monitor predicates, trial rules and visibility parity. This is an authoring check, not gameplay execution.
- `python -m unittest discover -s scripts/tests -v`: PASS, 4 existing validator regression tests.
- Full committed-range `git diff --check origin/main...HEAD`: PASS after commit; new test metadata trailing spaces were removed.

`OutOfAlignmentTests.cs` adds 20 authored NUnit cases, compiled but **not executed**: terminal teaching/abandon/reopen/saved flag, Completed-dialogue rejection; valid/negative/wrapped/invalid target angles; malformed and repeated target metadata in both orders; incompatible/locked/overlay casts; exact/normalized/wrong-object predicates; repeat-cast and monitor-event idempotence after save/rebuild; baseline-save metadata/compatibility preservation; Active reset/retry preserving unrelated progression and object power; NotStarted/Completed/wrong-map/overlay reset rejection; cached-prefab original rotation/visual reseeding; and deterministic repeated trial interaction/visibility.

## Pending acceptance and provisional design

Unity EditMode/PlayMode execution, rendered HUD/readability/small-screen/controller comfort, save-slot end-to-end acceptance, and platform builds remain unverified because the parallel contract forbids shared Editor use. The marker category, final art and physical presentation remain provisional. Cold Feet's Glaciate lesson/sequence implementation belongs to its independent branch; this branch provides the deterministic handoff only.

## Merge dependencies

- Preserve the First Light branch's captured-runtime subscription/replacement lifecycle and QuestPuzzleMonitor completion-feedback changes. This independent main-based branch retains baseline lifecycle code and does not duplicate that redesign. Merge `OnDialogueEnded`/lesson metadata narrowly so First Light and the later quest lessons all remain Active/terminal gated.
- `SpellCaster` overlaps all quest spell/teaching changes and generic spell feedback/reset. Keep strict Vis metadata, ninety-degree alignment, no-op recast and the scoped reset entry when merging; the provisional HUD should move into production UI through the same authoritative world/progression APIs.
- `WorldStateService` and `WorldObjectRegistration` overlap generic authored-baseline/reset and save-recovery work. Preserve current authoring on restore and original rotation on cached re-registration; combine per-puzzle baselines without duplicate owners. Save-recovery still owns invalid-payload rejection/provider cleanup.
- Resolve campus asset/prefab, NpcCatalog, NPC/dialogue/quest JSON and `docs/quests.md` by exact Cinna blocks. Retain independent Cold Feet targets/teaching, Stella, Katrina and other NPC changes. Do not replace complete shared files with this branch's baseline.
- Content-validation CI may call the new narrow parity script or incorporate its checks into its broader validator. No automatic merge/cherry-pick or worktree cleanup was performed.
