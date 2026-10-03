# Camera framing, occlusion and interior transition

## Required contract

Extend CameraFollow2_5D while preserving serialized target, offset, focusOffset and smoothTime names and types. Frame authored maps/interiors with tunable profiles and bounds. Snap/reseed on map transitions, teleports and replacement targets to avoid traversing off-map gaps. Preserve a fixed 2.5D angle. Optional occlusion must protect the player and important props, avoid permanent shared-material mutation and restore renderer state on transition/disable. Reduced-motion and overlay behavior must remain coherent. These provisional settings require visual comfort and collision acceptance in Play Mode.

## Baseline and constraints

Independent codex/feature-camera-interiors starts at origin/main 8ceebfa. Existing camera is a smooth positional follower with target LookAt. Loader exposes LoadedMap and CurrentArea but no transition event. GridMover exposes Teleported and World. GridWorld owns transformed grid coordinate conversion. No camera profile exists on shared map schema. Do not change loader/map definitions, other feature branches, ThirdParty content, scene GUIDs/file IDs or save providers. No Editor, Unity MCP/CLI, Play Mode or build operations. Current-source offline compile uses read-only Unity 6000.4.10f1/reference project assemblies excluding imported Lumenfall.* and Assembly-CSharp; local outputs stay under Temp.

## Implementation plan

1. Extend the existing camera with target assignment, explicit snap, teleport subscription and discontinuity fallback; fixed rotation derives from offset/focusOffset rather than camera tracking lag.
2. Add camera-owned serializable per-map profiles, resolved through the existing loader. Use GridWorld local bounds and transforms for framing. Keep existing outdoor offsets and optional bounds; author provisional interior profiles for existing B01/B02/B04 map IDs after checking data. No broad shared schema edits.
3. Add optional opt-in collider occluders with reversible renderer forceRenderingOff state. Exclude target hierarchy and protected authored renderers; use bounded nonalloc physics queries. No shader/material change.
4. Expose SetReducedMotion(bool); bypass smoothing and clear velocity/reseed on changes. Accessibility branch should call this API from its adapter during merge, rather than duplicate camera smoothing logic. Camera consumes no input and changes no overlay locks.
5. Add deterministic offline framing tests and Unity lifecycle/teleport/restore tests, compile current source, run feasible offline checks and record exact results.

## Acceptance and risk

Preserve existing field serialization and old scenes. Verify narrow matching .meta creation. Verify transformed-grid/inset/small-room framing math, stationary fixed angle, snapping, profile fallback, disable/transition restoration and protected renderer exclusion. Physics/visual behavior, actual interior composition, occlusion-layer authoring and motion comfort remain pending licensed Play Mode acceptance. Occlusion defaults off and placeholder profile numbers remain provisional.

## Status

Planning commit only. Implementation, tests and final evidence follow in this branch. Manager owns draft PR, independent review and remote checks; root owns archive after verification.
