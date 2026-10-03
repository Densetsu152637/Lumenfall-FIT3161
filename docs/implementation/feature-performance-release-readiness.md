# Performance observability and release acceptance

Branch: `codex/feature-performance-release-readiness`, independent baseline `8ceebfa64c20d0f016dcd693c410bb64178d0dd5`.

## Required scope and review contract

Add useful opt-in timing/count diagnostics for actual authored map activation/loading, map rebuild/object effects and NPC pathfinding. Memory must be bounded; disabled overhead minimal; no logging spam, personal data or speculative optimization. Runtime state and serialized IDs remain owned by existing systems. Authored WorldArea assets/prefabs are live authority; JSON is source/fixture content.

Provide a reproducible acceptance matrix covering all six quest lessons/puzzles, required route/key/sleep-wake, before/after-save restoration, input locks, diagonal collision, portal bounce and invalid saves. Record Unity `6000.4.10f1`, platform, full commit, build and slot identifiers and evidence. Licensed builds, rendering, audio, devices and performance budgets are unverified until measured on target platforms.

## Implementation plan

1. Add an opt-in bounded timing/count collector and narrow existing-owner instrumentation, plus meaningful collector behavior tests.
2. Add a machine-readable acceptance checklist and Python report tool that generates pending evidence, validates completed evidence, and exits nonzero for incomplete/failed checks. It must never infer release readiness from offline validation.
3. Run relevant Python/static checks, pure collector tests and offline Roslyn compilation using installed Unity compiler and shared read-only references. Do not launch Editor, Unity CLI, Play Mode or builds.
4. Review committed diff, update draft PR with actual evidence and outstanding runtime/platform work.

## Merge ownership

Only narrow instrumentation blocks in DataDrivenWorldLoader, WorldStateService and GridNpcNavigator overlap route/save/spell/NPC branches; preserve their lifecycle and behavior changes. New diagnostics and release acceptance tooling have independent ownership. Content-validation branch owns existing CI and compile scripts; this branch does not rewrite them.

## Current evidence and gaps

Implemented bounded default-disabled diagnostics with ten fixed buckets, elapsed count/total/mean/max and work totals. Disabled scopes do not read the clock/acquire a lock/allocate. Reset/enable generation prevents old scopes contaminating new sessions, counter saturation prevents wraparound, and SubsystemRegistration disables/resets collection when domain reload is disabled. Existing loader Load/Instantiate/SetActive, world Begin/End rebuild/property/move/connection/effect queue and navigator BuildPath have narrow synchronous timing scopes; no gameplay rule or serialized field changed.

Implemented [18-case acceptance matrix](../release/chapter-one-acceptance.json), [usage/capture workflow](../release/README.md) and `scripts/release_acceptance.py`: new records always pending, no evidence overwrite, complete environment/full commit required, every case must pass with evidence, ten metrics require disabled/enabled captures and approved finite mean/max budgets. The report validates recorded evidence and does not verify human observations. All six quests, route/key/sleep, saved state/recovery, input locks, collision/portal bounce, presentation/settings/audio, performance and platform builds are included.

Added 13 pure collector NUnit tests, five Unity hook integration tests and 11 Python gate tests. `scripts/test_performance_diagnostics.ps1` plus `PerformanceCollectorHarness.cs` makes the pure collector tests reproducible with explicit read-only reference project.

### Exact offline checks (2026-10-03)

- `python -m unittest discover -s scripts/tests -v`: 15 tests PASS (11 new gate cases plus four existing validator cases).
- `python scripts/validate_npc_dialogue.py`: PASS, six quests/15 dialogues/nine events.
- `python scripts/validate_runtime_world.py`: PASS, eight maps/seven NPCs.
- `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test_performance_diagnostics.ps1 -ReferenceProject 'D:/Git Repositories/Lumenfall-FIT3161/Lumenfall'`: 13 actual repository NUnit collector tests PASS; 100,000 warmed scopes each enabled/disabled allocated zero bytes on the installed .NET 8.0.16 runtime only.
- Offline Roslyn compiled current GameEvents, Runtime, EditMode and PlayMode test sources with Unity 6000.4.10f1 installed compiler, shared read-only Bee/NUnit references, all imported `Lumenfall.*` refs excluded, and outputs/rsp files in this branch's Temp. Existing serialization/obsolete-API warnings remain. The branch-local fallback compiler files are Temp artifacts; content-validation owns the reusable compiler contract.
- `python scripts/validate_unity_project.py Lumenfall`: same three baseline ThirdParty issues (orphaned URP metadata, FantasyCastle UTF-8 read failure, Tavern README conflict marker). No new metadata/GUID issue reported; those unrelated files were left intact.
- Manager reviewed actual diagnostics/hooks/test/tool diffs. Full committed `git diff --check origin/main...HEAD` must pass before final handoff.

### Pending acceptance and merge

Five Unity hook tests are compiled but unexecuted. No shared Editor, Unity MCP/CLI, Play Mode or licensed build was run. CPU timings, actual chapter completion/save/portal behavior, rendering/audio/device/input navigation, platform binaries and performance budgets remain pending. No numeric game budget/platform claim is fabricated. Collect artifacts against the final integrated full commit and rerun every declared target platform matrix.

Loader, WorldStateService and GridNpcNavigator changes consist only of timing scopes/imports; retain owner behavior from route/save/spell/NPC branches when resolving merges. This branch creates no competing state/provider manager, area JSON loader, scene wiring, optimizations or CI scripts. No data/assets/prefabs are changed beyond metadata for new diagnostic/test source files. Root owns final worktree archive/removal after remote HEAD parity verification.
