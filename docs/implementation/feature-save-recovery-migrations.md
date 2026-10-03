# Save corruption/recovery and compatible migrations

Baseline: origin/main 8ceebfa; branch codex/feature-save-recovery-migrations.

## Required contract and plan
Extend GameSaveManager, retaining provider keys and existing atomic replacement/rollback. Diagnose missing, corrupt, truncated and unsupported slots without silently overwriting them. Preserve previous saves and offer validated explicit recovery candidates. Migrate actual schema 1 saves into a schema 2 envelope; schema 0 and future formats fail. Reject duplicate/malformed provider entries and retain unknown provider payloads. Restore event quests/unlocked spell flags, map/player and world state in deterministic order, rolling back both data and presentation on failure. Guard autosave/load/reset and provider lifecycle changes during operations.

1. Add diagnostics, migration, backup/recovery, operation boundaries and focused save tests.
2. Narrowly harden map/player and world providers where necessary for validation and rollback; integrate autosave/reset guarding.
3. Offline compile using Unity 6000.4.10 and read-only main reference assemblies (exclude all baseline Lumenfall assemblies), run repository validators, and record limits.

## Validation and limitations
No shared Unity Editor, MCP/CLI, Play Mode or build operations. Offline compilation and authored tests cannot prove scene rendering or licensed runtime execution. Tests use isolated temporary save directories. Manual acceptance must exercise corrupt/truncated files, backup recovery, old v1 saves, cross-map rollback, invalid cells and autosave suppression in Play Mode.

## Merge ownership
Expected overlaps: DataDrivenWorldLoader with world-route; WorldStateService with spell feedback/string/ice scopes; GameAutosave and StartupMenu with production UI. Preserve those branches’ independent lifecycle, puzzle and presentation changes during merge.

## Progress
Implemented in the existing owners; no scenes, prefabs, packages or provider keys changed.

- Schema 2 adds SHA-256 integrity checking; actual schema 1 exports migrate in memory and original bytes are untouched until explicit Save. Schema 0/future versions fail.
- InspectSlot reports missing/corrupt/truncated/unsupported/unavailable states. Save preserves the previous valid slot in .bak and damaged primary bytes in unique .preserved files. Existing interrupted .tmp is never silently overwritten/deleted by a refused Save.
- GetRecoveryCandidates validates candidates without importing live state. RecoverSave restores providers transactionally before atomic promotion, preserves candidate/damaged bytes, and rolls back when restore or promotion fails. Interrupted candidate bytes are archived after successful promotion to free the next write path.
- Strict bounded JSON syntax, duplicate decoded member/provider rejection, required event arrays, quest-status validation, required registered gameplay payloads, matching map IDs, 128 providers/8 MiB bounds. Unknown provider objects round-trip through subsequent saves and clear on new-game reset.
- Map content prepare -> puzzle-world state -> saved player placement; rollback uses the same phases. Loader validates required fields/map/bounds/player/world readiness; world validates null/duplicate snapshots, object sizes/map bounds, finite transforms, connection endpoints and strengths. Transform and obstacle refresh use existing world application.
- OperationStarted cancels pending autosave; callbacks cannot save/load/delete/reset/change registration during operations. Failed load/recovery keeps autosave paused until a successful load or explicit Save. Startup new-game reset uses ResetState(Action); known provider destruction unregisters only its own instance, and destroyed Unity owners can be replaced.
- Save overlay exposes cached explicit recovery buttons after a failure; Startup reports actionable LastError. UI production branch must preserve this adapter.

## Executed evidence (2026-10-03)
- Offline GameEvents + Runtime compilation: exit 0, Unity 6000.4.10f1 compiler and read-only main Bee references, filtering every baseline Lumenfall.* reference. Generated adaptation and outputs confined to Lumenfall/Temp/codex-compile; source compile scripts unchanged.
- Offline full GameEvents EditMode assembly compilation: exit 0. Existing Unity/serialization warnings remain; new nullable DTO-field warnings describe JsonUtility-populated validation fields.
- 18 save test cases executed: 18 passed/0 failed in a temporary .NET 8 harness compiling the actual GameSaveManager, GameEventRuntime, GameEventCatalog, definitions/dialogue and committed NUnit test source. Only Unity lifecycle/lookup, JsonUtility (System.Text.Json/reflection substitute), logging, and GameEventManager bootstrap were substituted. Manager independently reran and inspected this harness. This is filesystem/transaction evidence, not Unity Test Runner evidence.
- Tests cover schema 1 quest/unlock migration, schema 2 tampering, missing/truncated/future files, malformed embedded events, decoded duplicate keys/providers, unknown-provider round-trip, provider rejection and rollback, reentrant operations, backup/interrupted recovery, failed recovery preservation, required/mismatched gameplay providers, phased final placement/rollback, provider ownership and reset clearing.
- Installed Unity ext NUnit lacks NonParallelizable (NUnit 2), so the fixture follows Unity's serial default; do not run these singleton/runtime tests concurrently in an external runner.
- python scripts/validate_runtime_world.py: pass (8 maps/7 NPCs).
- python scripts/validate_npc_dialogue.py: pass (6 quests/15 dialogues/9 events).
- python -m unittest discover -s scripts/tests -v: 4 passed.
- python scripts/validate_unity_project.py Lumenfall: the same three baseline ThirdParty errors (orphan URP.meta, UTF-16 FantasyCastle ReadMe, Low Poly Tarvern README conflict marker); no authored feature metadata failure.
- git diff --check and full committed origin/main...HEAD check: pass.

## Pending licensed Unity acceptance
Run the committed GameSaveRecoveryTests in the real Unity Test Runner. Exercise cross-map restore and failure rollback with real player occupancy/obstacles, including a saved player cell vacated by Gravitate; verify object transforms and presentation after rollback, quests/unlocked spells, pending spell effects, save/recovery buttons, pause/quit autosave, new-game/reset, scene owner replacement and inaccessible disk paths. Recovery UI layout/rendering and target player builds were not run. Schema 2 provides accidental-corruption detection, not authentication/encryption. If interrupted-file archival fails after successful promotion, the bytes remain intact and another save reports the interrupted-file condition; resolve the filesystem issue before retrying.
