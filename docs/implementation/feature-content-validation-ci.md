# Content validation and offline compilation

## Plan and scope

Continue PR 2 on `CI/workflows` from `80b2a0b`. Runtime authority is WorldAreaAsset.definition, areaPrefab registrations/DynamicNpc definitions and NpcCatalog; map/NPC JSON is authoring source and fixture data. Validate source/baked parity, map cells and IDs, blocked spawns, portal references/reciprocity, serialized GUID resources, quest/event/dialogue/monitor references and lesson/optional/reserved gates. Do not migrate or modify gameplay assets to conceal mismatches.

Replace hardcoded dialogue and NPC entry counts with semantic checks accepting additive content. Add negative fixtures and CLI exit-code coverage; integrate the authored runtime check into existing CI. Preserve the existing repository/result harness and unrelated ThirdParty faults.

Offline compilation must use current-worktree sources and explicit read-only `ReferenceProject` for imported dependency assemblies. Exclude every imported Lumenfall assembly, discover supported test asmdefs, and write response/output files only to this worktree. No Unity Editor, MCP/CLI, Play Mode or player build launches.

## Validation contract

Run Python harness and content validators, current-source offline runtime/test compilation against Unity 6000.4.10f1 and the shared reference project, and committed-range whitespace checks. Report real baseline parity/reference failures and environment limitations. Compiler success does not establish Unity Test Runner execution, Play Mode, scene appearance or player-build acceptance.

## Merge ownership

Own scripts, Python tests, CI integration and this document. Quest branches change dialogue/monitor data and may improve baked parity; additive counts must remain accepted. Root owns final PR update/attachment, independent review and worktree cleanup.

## Implemented

`validate_authored_content.py` reads WorldArea definitions, NpcCatalog/layer rows, relevant prefab MonoBehaviours/Transforms and PortalContentAsset GUIDs with pinned PyYAML 6.0.3 SafeLoader. It checks source/baked object, target, NPC and portal metadata; related registration/DynamicNpc transforms; duplicate IDs; cells/footprints; tile resources/glyphs and blocked source cells; resource/prefab references; live GridPortal/content membership; reciprocal active links and full arrival footprints; and quest monitor object/connection/quest/event/dialogue references. Building placement records are distinct from ordinary objects. Future target and NPC serialized fields participate in parity checks. Unity's trailing empty NPC layer strings are normalized consistently with source line reading.

Portal live eligibility follows the existing migration's reserved and portalGlyph rules. Planned glyph-ineligible/reserved declarations need not have live components, but an active destination must have one. The graph recognizes only numeric `.x_y` suffix aliases with matching owner/logical IDs. Live reserved content with a destination or alternate travel mode fails. Legacy JSON arrival coordinates are not compared against runtime relative offsets.

Dialogue validation now accepts additive catalogues and eligible NPC handoff changes. It checks built-in references, node links, duplicates, status values, quest lifecycle branches, guarded completion, ambient neutrality and positive weights. Type/status case follows the runtime's case-insensitive handling. Authored lesson rewards need `spellRewardQuestId`; completion rewards cannot substitute for a teaching dialogue. No fixed quest/dialogue/event/NPC-entry counts remain. Custom action/condition types remain extensible.

Supported optional metadata gates (`optional`, `enabledByDefault`, `requiresQuest`) reject enabled-by-default content and mandatory prerequisites on optional quests. These checks cover that declared metadata contract; independent optional activity component/config schemas still require branch-specific acceptance and are not inferred from names.

CI adds `Content / Authored runtime assets` to the existing matrix and installs `scripts/requirements-validation.txt` before dependency-using harness/content checks. Existing workflow/result validation and licensed Unity jobs are retained.

`compile_assemblies.ps1` discovers current-source first-party asmdefs and source ownership, orders their dependencies, uses imported templates only for external references/defines, excludes every stale `Lumenfall.*`/`Assembly-CSharp` assembly and imported source, and compiles all supported test asmdefs. Wrappers require `-ReferenceProject` and accept `-ProjectPath`, `-UnityEditorPath`, `-PrepareOnly`. A source project inside/equal to the read-only reference project is rejected. Missing dependencies/templates, constraints/cycles and compiler failures fail explicitly. Outputs remain in the current-source project's `Temp/codex-compile`.

## Validation evidence

- Python harness: **45 passed**, including the retained 21 integrity/result tests and 24 new content/portability cases. New cases exercise malformed references, duplicate/link/bounds/blocked-cell failures, metadata and transform drift, additive fields/content, missing teaching and optional gates, CLI exit codes, nullable Unity layer strings, current-source membership, all test asmdefs, missing dependencies/templates, Windows path separators and read-only reference isolation. The portability fixture includes a third test asmdef and verifies dependency bytes/mtime unchanged.
- Actual offline Roslyn compilation against Unity **6000.4.10f1**, read-only reference `D:/Git Repositories/Lumenfall-FIT3161/Lumenfall`, current clean worktree without `Library`: **passed** `Lumenfall.GameEvents` (10 source files), `Lumenfall.Runtime` (24), `Lumenfall.GameEvents.Tests` (6), `Lumenfall.World.PlayModeTests` (1). Existing FindObjectsSortMode/API obsolescence warnings remain. This compiles test sources; it does not run NUnit tests.
- Existing source-fixture world check: **passed** (8 maps, 7 NPCs); NPC layers **passed**; garden/east-gate acceptance **passed**; reused-branch Unity repository integrity **passed**. Original main's three ThirdParty integrity faults are already addressed by retained CI branch history; this implementation changes no ThirdParty assets.
- Authored runtime content check: **failed with 22 existing gaps**, enumerated below. Dialogue semantic check: **failed with one missing First Light teaching guard** (merged resource catalogue currently 7 quests, 17 dialogues, 14 events; these are observed results, not required counts).
- Working-tree whitespace check passed. Final committed-range whitespace and remote HEAD parity are checked after commit/push. Local actionlint was unavailable and was not rerun; CI retains its existing lint job. No GitHub Actions or Unity Test Runner execution is claimed by this local run.

## Historical content gaps exposed

The 22 diagnostics are: divergent `campus` legacy `map.json` alias (1); all seven source NPC resource paths differ from NpcCatalog's editable NpcPlaceholder prefab (7); First Light dialogue reward lacks `spellRewardQuestId` (1); campus/eastForest spawn `(1,1)` is on a blocking source tile (2); campus B01 `(12,56)` size `20x14` and B04 `(72,48)` size `20x24` exceed `92x54` bounds (2); B02/B03/B04 indoor exits target campus PortalContentAssets absent from live prefab components (3); and all six quest completion spell rewards lack authored Active-gated teaching dialogue metadata on this branch (6). NPC placeholder fallback is documented by the migration, but it leaves source/resource identity parity incomplete and is reported explicitly. Quest and route branches own the narrow gameplay/content remediation.

The user-requested PR check repair now reconciles these gaps in source and baked assets. Existing placeholder GUIDs are retained under Resources, real building footprints are kept in bounds away from the garden/NPCs, spawns use free cells, and three reciprocal indoor approaches are baked without opening reserved gates. Teaching grants use terminal dialogue and an Active quest gate, with six behavior regressions covering acceptance, abandonment, reminders and completion. No content checks are disabled.

## Limits and merge handoff

Static tile/object collision checks do not prove baked GridObstacle geometry, root/content transforms, scene grid scale, reachability, portal bounce or successful runtime travel. Transform checks cover direct placed registrations/NPCs and serialized prefab-instance placement overrides under the existing unit-grid authoring convention. Runtime monitor predicate equivalence, custom optional component gates, terminal-dialogue grant timing and custom handlers still need gameplay acceptance after combining feature branches. No Unity Editor/MCP/CLI, Play Mode, player build, rendering/audio/device or performance acceptance ran.

Quest branches will resolve teaching/target data and NPC handoff eligibility; world-route/presentation branches will reconcile travel/asset placement. Retain authored source/baked parity and tests while resolving merges. CI/docs/compile scripts overlap PR 2's retained workflow work. Parent/root owns PR update/attachment, independent final review, remote verification and worktree archival.

## Current PR check repair

Merged current main, fixed actionlint heredoc termination, and made PrepareOnly compilation independent of a Windows Editor installation. The 62 Python harness tests and all five content validators pass locally. Current-source runtime/EditMode/PlayMode compilation passes against read-only Unity 6000.4.10f1 references. The authored runtime validator now reports zero errors. Licensed native Actions validation remains the readiness gate; manual gameplay, placement, visual and platform acceptance remain pending.
