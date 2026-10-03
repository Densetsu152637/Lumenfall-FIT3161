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

Plan committed before implementation. No runtime, build, visual, platform or performance claim is made. The complete acceptance matrix and diagnostics are being implemented in this branch. Final evidence will replace this section.
