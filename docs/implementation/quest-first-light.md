# Stella: First Light implementation scope

Branch: `codex/quest-first-light`, based on `origin/main` (8ceebfa).

## Required player loop

- Reach Stella in W02 on B02.Ground and accept First Light.
- Diagnose the broken electrical relic through Stella's lesson and receive Ampere before attempting the puzzle.
- Make lesson and active hints match available actions and the authored target; avoid inventing an unsupported repair interaction or receiver.
- Cast Ampere on `relic.stella.power`; completion requires an active quest, the correct map, and authoritative powered state.
- Give clear success feedback and preserve the completed conversation before the String Them Together handoff.
- Keep reward flags, completion state and monitor lifecycle reliable across reload/reset/save import.
- Add meaningful automated coverage for the implemented progression and edge cases.

## Existing baseline

Main already awards Ampere through First Light's terminal teaching dialogue and defines the powered relic target. Extend that path; do not add duplicate reward sources or replace existing managers.

## Work plan

Inspect dialogue, NPC priority, monitor and save lifecycle; implement the most concrete gaps, run repository validators and isolated compilation where possible, record results here, and update the draft PR.

## Outstanding manual work

- Approved NPC/relic artwork, final animations, audio and scene presentation.
- Play Mode traversal from reception to W02, spell targeting and completed-dialogue handoff.
- Unity Editor/build verification in a dedicated project session after parallel branches are reviewed.

Shared Unity Editor and Unity MCP/CLI access are deliberately excluded from this parallel work.

## Implemented

- First Light's lesson diagnoses an empty reservoir in an already repaired link. The acceptance and active hint explain the available Ampere/right-click interaction, two-step range and toggle behavior rather than requiring an absent repair mechanic or receiver.
- The monitor opens a dedicated Stella success line only after successful First Light completion. It cannot interrupt a dialogue, complete on another map, run while disabled, or replay the line after importing completed state.
- `GameEventManager.RuntimeChanged` announces definition reloads. SpellCaster and QuestPuzzleMonitor keep their exact subscribed runtime, unsubscribe on disable, and bind the replacement immediately. Spell wheel cleanup uses the captured runtime, avoiding singleton creation during teardown.
- Seven new EditMode regression tests cover teaching recovery after abandonment, powered-target success, wrong-map/quest/power guards, disabled/dialogue guards, definition reloads and old runtime isolation, completed save/reset behavior, and caster disable/re-enable.
- The existing standalone Ampere reward test now creates the actual SpellCaster integration owner. The terminal reward path and progression flags remain the existing sources of truth.

## Validation results

- `python scripts/validate_npc_dialogue.py`: PASS, 6 quests / 16 dialogues / 9 events (the additional dialogue is First Light's success line).
- `python scripts/validate_runtime_world.py`: PASS, 8 maps / 7 NPCs.
- `python -m unittest discover -s scripts/tests -v`: PASS, 4 Python validator tests.
- Runtime and all EditMode test source compilation: PASS through installed Unity Roslyn compiler using read-only reference assemblies in the existing project's Library. Generated response files and DLLs were written only to this worktree's ignored Temp directory. Existing scripts were adjusted in memory to use external references and remove baseline Lumenfall assembly references and relative template source rows. Runtime emitted preexisting obsolete API / unassigned serialized-field warnings. This verifies source compilation, not Unity test execution.
- `python scripts/validate_unity_project.py Lumenfall`: reports exactly the 3 baseline third-party errors: orphaned `URP.meta`, UTF-16 FantasyCastle `ReadMe.txt`, and a conflict-marker Tarvern `README.txt`.
- `git diff --check`: PASS.

## Remaining integration and merge notes

EditMode integration tests have been authored and compiled but not executed; Unity Editor, scene import, targeting feedback, animation/audio/art and full player build remain unverified. This branch adds no scene/prefab edits and needs no Inspector wiring. First Light success uses its own dialogue so the completion conversation is guaranteed once. Subsequent NPC selection of String Them Together remains the follow-up quest branch's responsibility.

String Them Together changes monitor target evaluation and puzzle definitions; reconcile those changes with this branch's lifecycle and optional `successDialogueId` field. The content-validation branch owns compile-script portability; reconcile its dialogue validator with the added success dialogue (16 total). All work starts from main; no sibling branch commits were imported.
