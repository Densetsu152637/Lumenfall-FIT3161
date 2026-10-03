# Optional dormitory grid power activity

Branch: codex/feature-optional-dormitory-power. Independent base: origin/main 8ceebfa64c20d0f016dcd693c410bb64178d0dd5. Draft PR: https://github.com/Densetsu152637/Lumenfall-FIT3161/pull/10. Plan commit: e8b9b1ce63826544bce10e1e933c4559124d1e0a.

## Full scope and acceptance contract

Provide an opt-in, disabled-by-default optional Ampere/Weave power restoration activity. Existing WorldStateService owns objects/cables and GameEventRuntime owns saved spell/completion flags. Require the intended source/sink connection, powered source and actual live attenuation at least 0.2. Unrelated cables, stale saved strength, wrong map, missing spells and missing authoring cannot satisfy it. Grant only one optional completion flag, once, with no inventory/economy award. Define reset, replay, persistence and actionable failure feedback. Main route and Chapter 1 quest prerequisites remain unchanged; this activity must never grant access to or modify D05. Activation/content/reward decisions remain provisional pending product approval.

## Implemented

`OptionalDormitoryPowerActivity` is an explicit, ready-to-author runtime component with serialized opt-in false by default, no bootstrap and no enabled scene/prefab installation. `Configure`, `TryBegin`, `TryCast`, `TryReset`, `TryReplay`, `Evaluate`, `Status`, `Feedback`, and `FeedbackChanged` supply a real interaction bridge. The component delegates actual Ampere and Weave effects to the existing SpellCaster; it does not duplicate those effects or award spells. Direct existing caster effects can also be observed while an explicit session is active. Enabled observation is bounded to ten checks per second; world-change feedback is immediate and read-only.

Access requires live B04.Ground, both spells already learned, the player's own enabled SpellCaster/GridMover, the same WorldStateService singleton used by Cast, an enabled component, unpaused gameplay, closed inventory/dialogue overlays, and proximity within two Manhattan cells of an endpoint. Individual casts also require range of that target. Malformed/missing reciprocal tags, ambiguous endpoint tags, incompatible objects, wrong area/target/spell and failed casts report concrete status and player-readable feedback.

Stable dedicated IDs: `power.dorm.optional.source`, `power.dorm.optional.sink`; both objects require `optional-dormitory-power` and exactly one reciprocal `weave-with:<otherId>` tag. Weave is explicitly cast at the sink. Completion requires this exact cable, current source power and `1 / (1 + Euclidean grid distance) >= 0.2`; it never trusts stored legacy cable strength. Provisional two-cell spacing gives approximately 0.333; the exact four-cell boundary gives 0.2. Other connected objects, direct power at the sink, source-off and excessively long saved cables cannot substitute.

Only `optional.dormitory-power.completed` is granted through the existing GameEventRuntime. No new quest, provider, item, currency, prerequisite, event catalogue, main route or reserved-room gate is introduced. Completion checks are deferred out of world callbacks until the current update or explicit cast/begin, preventing awards midway through multi-provider restore.

## Reset, replay and save contract

- Begin is explicit. Solved world state before Begin does not grant completion until accepted. A component session is transient, not an additional save authority.
- Existing spell-world-state provider saves power/cables and map archives. Existing event provider saves completion/spell flags. Mid-attempt restore requires Begin again, then continues from saved partial world state. Restore, map departure, disable/re-enable and runtime replacement end the transient session.
- Reset during an accepted incomplete session removes only the named source/sink cable, turns off the dedicated source and acknowledges its pending Ampere on/off effects. It preserves unrelated objects, cables, flags and main quest state. `WorldStateService.RemoveConnection` is the only shared-world API addition.
- Reset does not relocate props or erase saved completion. This activity supports power/cable reset only; arbitrary repositioning by generic Gravitate is outside its interaction bridge. An excessively displaced prop reports insufficient power and needs the approved layout restored by its author. Final mobility restrictions and any position-reset UX require product design and generic spell/reset integration.
- Completion ends the session. Begin/reset are then rejected; explicit Replay resets power/cable for practice while retaining the saved one-time flag. Replay cannot grant another reward or erase main progress. Repeated evaluation is idempotent.
- Runtime binding is captured and balanced on disable/reconfigure/replacement; the next operation/update resolves a replaced manager runtime without depending on a new GameEventManager API.

## Provisional authoring and remaining activation

Product approval is still required for activation, final location, props/signage, presentation and player-facing interaction UI. B04.Ground is the existing area chosen as a provisional authoring boundary, not approved final room placement. Baseline B04 source JSON describes D05 as walkable; this branch makes no claim that another system already blocks it. It never changes its portal/tiles/gates or grants entry.

After approval:

1. Place two dedicated placeholder props in approved reachable B04.Ground cells, provisionally two cells apart. Keep main travel/arrivals clear and do not place them in D05. Names, art and any narrative remain provisional.
2. Add matching WorldObjectRegistration components with the fixed IDs, correct map cells/footprints, spellCompatible true and exact dedicated/reciprocal tags. The actual loader reads WorldAreaAsset plus baked prefab registrations, not Maps JSON.
3. Synchronize only the applicable B04.Ground.asset object definitions, B04.Ground.prefab registrations/transforms, and Resources/Maps/B04.Ground.json authoring/fixture copy. Include metadata for new assets. This branch intentionally authors no dormant objects into currently active content.
4. Add the component to an approved interaction host. Assign worldState, caster and player references; caster and player must share the same GameObject. Set activityEnabled only after approval. Runtime `Configure(true, state, caster, player)` supports an explicit bootstrap/interaction installation instead.
5. Wire the approved existing interaction/UI to TryBegin, Ampere-on-source/Weave-on-sink via TryCast, TryReset and explicit TryReplay. Display Feedback and observe FeedbackChanged. Source targeting for Weave is deliberately not offered by this activity on main's existing caster.
6. Verify the optional props do not grant new main-route spells, prerequisites or room access. Configure immutable placement/presentation or resolve the generic-movement boundary above before shipping.
7. In a permitted Unity session, import/check metadata, execute the tests below, verify entry/reset/replay controls and feedback, perform before/after-save round trips, and confirm main route/D05 behavior. Visual, input and platform acceptance remain pending.

## Validation evidence

- Offline Roslyn compilation PASS: Lumenfall.GameEvents and Lumenfall.Runtime using Unity 6000.4.10f1 compiler and read-only `D:/Git Repositories/Lumenfall-FIT3161/Lumenfall/Library/Bee/artifacts/1900b0aE.dag` references. All imported Lumenfall.* and Assembly-CSharp references excluded; first-party sources compiled from this branch. Warnings are baseline serialized fields/obsolete FindObjectsByType plus the response-file noconfig warning; no compilation errors.
- Offline compile PASS: all current GameEvents EditMode test sources, including 13 new NUnit cases (12 methods, one parameterized with two cases), and World PlayMode test sources. Unity-native integration cases were **authored and compiled, not executed**. They cover actual casts, one-time flag/main quest isolation, disabled/missing-spell/map gates, exact cable/source-off, saved-vs-live strength, 0.2 boundary, pre-Begin state, invalid casts/overlay/range, malformed tags, scoped reset, partial save reentry, completed save/rebuild/replay and runtime/disable/mismatched-actor safety.
- `python scripts/validate_npc_dialogue.py` PASS: 6 quests, 15 dialogues, 9 events.
- `python scripts/validate_runtime_world.py` PASS: 8 maps, 7 NPCs.
- `python -m unittest discover -s scripts/tests -v` PASS: 4 Python validator harness tests.
- Scope manager independently reran those same two validators and four Python harness tests: PASS.
- Full committed `git diff --check origin/main...HEAD` is required after final commit; final worker/manager handoff records its result and remote parity. New .meta empty fields contain no trailing whitespace.
- Compile helper scripts/response files/outputs remain ignored under this worktree's Lumenfall/Temp; shared references were read only. No Unity Editor/MCP/CLI, Play Mode or build launch occurred. Static/offline checks do not establish runtime, rendering or platform acceptance.

## Merge review

WorldStateService.RemoveConnection overlaps conceptually with String's connection semantics and generic spell/reset additions: reconcile rather than duplicate that API. Keep String's live connection rules, teaching and SpellCaster lifecycle/feedback improvements. This branch deliberately avoids changing SpellCaster, QuestPuzzleMonitor, GameEventManager, save formats, JSON catalogues or active world assets; combine their future changes without replacing whole files. In particular, generic target/tag/mobility restrictions should preserve these dedicated endpoints and the activity's sink cast contract. Manager reviews implementation and evidence before root handoff. Root owns eventual managed worktree cleanup after remote HEAD parity is verified.
