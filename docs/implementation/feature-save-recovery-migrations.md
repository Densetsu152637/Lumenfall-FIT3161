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
Plan committed before implementation; implementation and exact validation evidence will be added below.
