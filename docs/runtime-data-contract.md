# Runtime data contract

Runtime maps are `WorldAreaAsset.definition` at `Assets/_Project/World/Areas/{mapId}.asset`. `DataDrivenWorldLoader.Load` instantiates `areaPrefab`; live `WorldObjectRegistration` components and baked `DynamicNpc.definition` entries register metadata, while `NpcCatalog.asset` supplies NPC rules. Keep these copies synchronized with `Resources/Maps/{mapId}.json` and `Resources/NPCs/npcs.json`, which are source/fixture data rather than runtime loaders. Quest monitors still load `Resources/Quests` JSON and game events/dialogues load `Resources/GameEvents` JSON.

`PortalContentAsset` references resolve runtime areas and destinations by GUID. Travel arrives at `destination.cell + source.arrivalOffset + entryOffset`; legacy map JSON arrival coordinates and `coordinateOffset` are authoring metadata and are not the runtime travel rule. Portal fixtures may describe reserved or glyph-ineligible planned endpoints without live `GridPortal` components. Every active destination needs a live component.

Walk within roughly two grid cells of a loaded NPC and press `E` to open dialogue. The runtime overlay displays speaker/text, choices, and Continue/Close controls. Choice and node actions execute through the existing `GameEventRuntime`, preserving quest state.

Source map JSON and the baked definition may add `objects[].id`, `objects[].tags`, and `objects[].compatibleWithSpells`, plus `puzzleTargets[]`. Object ids are stable within a map and are exposed by `WorldStateService`. A map rebuild emits `MapRebuilt` only after all objects are registered and `IsReady` is true. The spell world save provider persists object cells, elevation, rotation, powered/frozen/revealed/aligned properties, wire connections, and pending effect requests under `spell-world-state`.

For an offline compilation check using Unity 6000.4.10f1 Roslyn, run PowerShell from the repository root with an explicit imported, read-only reference project:

```powershell
./scripts/compile_runtime.ps1 -ReferenceProject 'D:/Git Repositories/Lumenfall-FIT3161/Lumenfall'
./scripts/compile_editmode_tests.ps1 -ReferenceProject 'D:/Git Repositories/Lumenfall-FIT3161/Lumenfall'
```

The compatibility-named test script compiles every supported first-party test asmdef, including PlayMode source. Both scripts accept `-ProjectPath` for current-source compilation in another clean worktree, `-UnityEditorPath`, and `-PrepareOnly` to verify/create response files without invoking Roslyn. Imported templates supply external references and defines; every stale `Lumenfall.*`/`Assembly-CSharp` reference and imported source is replaced or excluded. Outputs stay in the current-source project's `Temp/codex-compile`, outside the read-only reference project. Missing templates, dependencies, unsupported asmdef constraints and compiler failures exit nonzero. Compilation does not run NUnit, scenes, Unity Editor imports, rendering or player builds.
