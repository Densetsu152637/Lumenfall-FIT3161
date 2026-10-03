# Content validation and offline compilation

## Plan and scope

Continue PR 2 on `CI/workflows` from `80b2a0b`. Runtime authority is WorldAreaAsset.definition, areaPrefab registrations/DynamicNpc definitions and NpcCatalog; map/NPC JSON is authoring source and fixture data. Validate source/baked parity, map cells and IDs, blocked spawns, portal references/reciprocity, serialized GUID resources, quest/event/dialogue/monitor references and lesson/optional/reserved gates. Do not migrate or modify gameplay assets to conceal mismatches.

Replace hardcoded dialogue and NPC entry counts with semantic checks accepting additive content. Add negative fixtures and CLI exit-code coverage; integrate the authored runtime check into existing CI. Preserve the existing repository/result harness and unrelated ThirdParty faults.

Offline compilation must use current-worktree sources and explicit read-only `ReferenceProject` for imported dependency assemblies. Exclude every imported Lumenfall assembly, discover supported test asmdefs, and write response/output files only to this worktree. No Unity Editor, MCP/CLI, Play Mode or player build launches.

## Validation contract

Run Python harness and content validators, current-source offline runtime/test compilation against Unity 6000.4.10f1 and the shared reference project, and committed-range whitespace checks. Report real baseline parity/reference failures and environment limitations. Compiler success does not establish Unity Test Runner execution, Play Mode, scene appearance or player-build acceptance.

## Merge ownership

Own scripts, Python tests, CI integration and this document. Quest branches change dialogue/monitor data and may improve baked parity; additive counts must remain accepted. Root owns final PR update/attachment, independent review and worktree cleanup.
