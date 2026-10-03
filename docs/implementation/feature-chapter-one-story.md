# Chapter 1 arrival, key mystery, dormitory and sleep/wake

Branch: `codex/feature-chapter-one-story`; independent main baseline `8ceebfa`. Draft PR: https://github.com/Densetsu152637/Lumenfall-FIT3161/pull/6.

## Required scope

The opening chapter follows `docs/main-cast.md`: reception's first account says another student has the key; meet the four friends and finish six required quests; return together for a contradictory account and residential key; discuss the contradiction in Sirius; unlock the residential door; receive a friend's guidance to the player's room; sleep; wake and record Chapter 1 completion. The contradiction stays unresolved. Every transition must reject premature/direct invocation, grant the key once, restore coherent checkpoints through existing saves and prevent optional travel bypass.

## Implemented

- `GameEventRuntime.ChapterOne` owns a deterministic story controller derived from existing saved triggered events and flags. There is no second quest, inventory or save authority. Eight unique guarded events and eleven dialogues are authored in `Resources/GameEvents/chapter_one_story.json`.
- Completing the first reception dialogue opens the friend stage. All six quests must be Completed and the player must complete terminal dialogue with each of Stella, Cinna Cirra, Orio and Katrina after reception. Abandoning a conversation does not count. Their existing quest dialogue selection and weighted ambient NPC selection stay intact.
- Terminal return dialogue grants `inventory.keyitem.dormitory-key` once. Sirius group discussion is additionally required for suite access. The unresolved mystery flag remains set through waking; no cause or resolution is invented.
- Read-only `Stage`, `CurrentObjective`, `HasKey`, `CanEnterSuite` and `AllFriendsReady` expose existing authoritative state. Stage objectives guide the player without introducing another story quest state.
- `ChapterOneInteraction.BindArea` runs inside the existing authored loader before area activation and spawn. `Resources/ChapterOne/world_story_bindings.json` places reception at B01 S02 `(13,3)`, the group in B04 D02 `(13,3)`, the door in D04, living zone D07 and sleeping room D08; bed `(29,8)`. New interaction capsules, names, labels and a cube/pillow bed are runtime placeholders on the live authored prefab root. Existing WorldArea/prefab/NpcCatalog GUIDs and file IDs are untouched; no map-JSON-only route is claimed.
- Composed GridWorld cell permissions guard D04/D07/D08 through collision, navigation, stepping, movement commit and teleport. D08 additionally requires room guidance. Story dialogue and transition permissions enforce actual map/cell/range context, including direct runtime calls. Other cell/interaction owners retain their own guards when this bridge unbinds.
- Explicit bed interaction opens a rest/later choice. Only completing the rest terminal sets sleeping. A provisional 0.8-second darkening, 1-second hold and 0.8-second brightening then permits wake and emits `chapter1.complete` once. Save restore restarts an unfinished fade, while area change, disable, reset and runtime replacement cancel presentation and release the current movement lock. Runtime replacement rebinds the bridge's subscriptions and predicates.
- Seven NUnit EditMode cases cover premature/direct calls, abandoned meetings, six-quest/four-friend gates, decline/sleep/wake/restore/idempotence, scoped context rejection at otherwise valid stages, real GridWorld step/path/teleport rejection and independent guard removal, plus bridge map/range/disable/rebind/runtime-replacement behavior. These test sources compile; they have not run in Unity.

## Validation performed

Manager reviewed the actual new runtime, content, bridge, shared hunks and tests. Independent baseline and final offline compilation succeeded for GameEvents, Runtime, GameEvents.Tests and World.PlayModeTests using installed Unity 6000.4.10f1 and read-only references at `D:/Git Repositories/Lumenfall-FIT3161/Lumenfall/Library/Bee/artifacts/1900b0aE.dag`. All stale imported first-party Lumenfall references were excluded; responses and outputs stayed in this worktree's ignored Temp folders. Existing obsolete search/serialized-field warnings remain.

An independent executable harness compiled the actual ChapterOneStory, GameEventRuntime, GameEventCatalog and DialogueSession sources with a .NET JSON shim only, and passed 109 behavioral assertions for full progression, premature/direct/repeated/abandoned calls and JSON stage/key restore before and after handover, door, guidance, sleep and wake. This is domain behavior evidence, not Unity JsonUtility or scene execution.

`validate_runtime_world.py`: PASS (8 maps, 7 NPCs). `validate_npc_dialogue.py`: PASS (existing 6 quests, 15 quest/ambient dialogues, 9 quest/ambient events; story package separately parsed/indexed by the executable harness). Python validator harness: 4 PASS. Unity integrity validator reports exactly the three baseline ThirdParty issues: orphaned URP.meta, non-UTF8 FantasyCastle ReadMe and Low Poly Tarvern README conflict marker. No unrelated ThirdParty changes were made. Committed-range whitespace check is required before handoff.

## Pending and provisional acceptance

No Unity Editor, Unity MCP/CLI, Play Mode, scene build or platform operation ran. Authored interaction reachability, fade rendering and brightness, camera/text appearance, physical collisions, save-provider integration and input/device behavior need licensed Play Mode acceptance.

The main baseline has an unavailable B01 live travel route and six quest teaching/progression defects assigned to independent branches. This branch implements live interaction integration once areas load, but does not independently establish an end-to-end playable chapter on that baseline. Merge and verify world-route's B01 portal repair and all six quest fixes before attempting chapter acceptance.

The current D04/D07/D08 regions, single group capsule, static friend guidance/directions, bed geometry and fade timing are provisional. Actual group actors returning to reception or physically following the player, final character/environment art, animation, audio and accessibility/reduced-motion integration remain pending. The required guided room approach is represented by authored friend dialogue, an objective and guarded arrival rather than follower movement. Legacy save payloads with no story flags begin at first reception; existing event-save validation owns corrupted/inconsistent payload rejection.

## Integration and public contracts

Preserve narrow changes in `GameEventRuntime` (ChapterOne construction/property/StartDialogue guard), `DynamicNpc.SelectDialogue` (story override before existing selector), `DataDrivenWorldLoader.Load` (BindArea before activation/spawn), and `GridWorld` (composed AddCellPermission/RemoveCellPermission and availability/commit guards). Do not replace these files wholesale with any independent branch's baseline.

World-route retains Load/SwitchArea/Restore APIs but stages map activation/rollback; preserve BindArea for each staged destination before validating its teleport, and rebind the source during rollback. Combine its reserved-room cell guard with these story guards. No sibling commits were imported.

Production UI's planned `OverlayInputGate.Acquire/Release(owner)` and `GridMover.SetMovementLocked(owner,bool)` must replace this bridge's legacy boolean sleep lock during merge so it releases only its own lock. Consume `CurrentObjective` in the production objective UI and remove the provisional bridge hint when it duplicates that UI. Preserve dialogue completion callbacks and opening-frame input protection.

Audio can read `chapter1.key_handover`, `chapter1.sleep`, `chapter1.complete` flags for mystery/sleep/wake contexts; story has no audio dependency. Optional D03/W07 travel stays outside protected residential zones; require `chapter1.first_account` plus its own visit gates, and use CanEnterSuite/room guidance for any residential arrival.

Stable events/flags: `chapter1.first_account`, `chapter1.key_handover`, `chapter1.group_discussion`, `chapter1.suite_entered`, `chapter1.room_guidance`, `chapter1.room_arrival`, `chapter1.sleep`, `chapter1.complete`; friend flags `chapter1.met.stella/cinna/orio/katrina`; key `inventory.keyitem.dormitory-key`; mystery `chapter1.mystery.unresolved`.
