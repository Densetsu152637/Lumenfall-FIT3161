# Chapter 1 release acceptance

This is an evidence workflow for licensed target-player runs. Offline compilation/static tests do not satisfy it. Run separately for every approved target platform/device configuration; no platform list or performance budget is invented here.

## Record a run

From the repository root, substitute the actual full commit, platform, build and disposable acceptance slot identifiers:

```powershell
python scripts/release_acceptance.py init --platform 'Windows x64' --commit FULL_40_CHARACTER_SHA --build-id BUILD_ID --slot-id ACCEPTANCE_SLOT --output Lumenfall/Temp/release-evidence/windows-run.json
python scripts/release_acceptance.py check Lumenfall/Temp/release-evidence/windows-run.json --report Lumenfall/Temp/release-evidence/windows-report.md
```

`init` refuses to overwrite evidence. The new file intentionally has only pending checks; `check` returns 1 until all required checks pass with evidence. Invalid input returns 2. Fill tester, UTC ISO timestamp and device/OS, then follow each procedure in [the acceptance matrix](chapter-one-acceptance.json). Use actual log/capture paths and observed outcomes; failures and unavailable work remain `fail`/`blocked`. The six quest IDs in this matrix are acceptance case IDs, not replacements for runtime quest IDs.

A passing report confirms completeness of the recorded evidence; it cannot independently verify a tester's observations or a capture's provenance. Keep evidence/captures together with the build record; Temp files are not source control artifacts. Run the relevant source/static/compile checks from the content-validation branch as separate checks, then perform final licensed build/player acceptance on the integrated commit. This independent feature branch cannot attest combined-branch compilation.

## Capture targeted diagnostics

`Lumenfall.Diagnostics.PerformanceDiagnostics.Enabled` defaults to `false`. In an explicit development/test session, call `Reset()`, then enable it, run one bounded scenario and inspect `GetSnapshot(metric)` or `GetSnapshots()`; disable it afterwards. No scene/prefab wiring, persistent setting or automatic logging is introduced. Snapshots allocate only on request. Each fixed metric retains count, total/max/mean elapsed time and total work units; it keeps no object/NPC/map names, coordinates or save content. Measurements are synchronous CPU elapsed time using Stopwatch, not GPU time, allocation counts or a substitute for Unity Profiler.

Metrics are `WorldLoad`, `WorldInstantiate`, `WorldActivate`, `WorldBeginRebuild`, `WorldEndRebuild`, `WorldObjectProperty`, `WorldObjectMove`, `WorldConnection`, `WorldEffectQueue`, and `NpcBuildPath`. Nested durations are inclusive and must not be summed as exclusive frame cost. Attempts include failed/rejected/exceptional calls, so capture scenario and outcome counts alongside timing. Instantiate appears only for a cold first area visit; repeat the cold scenario with a fresh world instance.

For the performance acceptance item, fill `performance.hardware`, `scenario`, and capture paths; record each metric twice with `diagnostics` equal to `disabled` and `enabled`. Disabled runs require an external Profiler/capture because the collector does no recording. Each metric entry has `name`, positive `samples`, nonnegative finite `mean_ms`/`max_ms`, positive approved `budget_mean_ms`/`budget_max_ms`, and a `budget_approval` reference. Collect comparable cold/warm map loads, rebuilds, actual spell property/move/connection/effect requests, and path success/failure/blocker retry on the target build. Neither this document nor the gate approves numeric budgets. Regressions against the approved mean/max budgets fail the gate. Use Unity Profiler for frame CPU/GPU, allocations, object counts and percentile latency when approving budgets.

## Mandatory runtime gaps

All chapter checks start pending: startup/world singleton wiring, required map round trips, reserved gates, all six lessons/puzzles, key/sleep ending, save round trips/invalid saves, input overlay locks, grid/corner/portal collision, rendering/camera/accessibility, audio, measured performance and target builds. Optional activities remain opt-in and cannot silently become chapter prerequisites. Approved media, final art, supported platforms/devices and measured budgets must be supplied and verified by production acceptance. No Editor, Play Mode or build was launched for this change.

## Repeat offline collector tests

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test_performance_diagnostics.ps1 -ReferenceProject 'D:/Git Repositories/Lumenfall-FIT3161/Lumenfall'
python -m unittest discover -s scripts/tests -v
```

The collector runner compiles the actual `PerformanceDiagnosticsTests` NUnit fixture with the pure collector under the installed Unity Roslyn/.NET 8 runtime. `ReferenceProject` supplies an imported NUnit package read-only; optionally pass `-NUnitPath` and `-UnityEditorPath` on other machines. Outputs/logs go to this branch's `Lumenfall/Temp/diagnostics-validation`. It runs no Unity-dependent integration tests. Its warmed allocation probe verifies this .NET runtime only and does not establish Unity/Mono/IL2CPP allocations or game performance.
