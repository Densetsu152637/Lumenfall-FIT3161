# Runtime data contract

Maps are TextAssets at `Assets/Resources/Maps/{mapId}.json`; `tileFile` names a sibling grid text asset. Top-level `portals` use `destinationScene` as a map id and `destinationPortalId` plus arrival coordinates. Building maps may include `coordinateOffset` metadata for converting catalog world cells to local interior cells. Place custom building prefabs at `Resources/Buildings/{id}.prefab` and NPC prefabs at `Resources/NPCs/{id}.prefab`; NPC records in `Resources/NPCs/npcs.json` support `mapId`, `asset`, coordinates, weighted quest dialogue, and existing GameEvent dialogue IDs.

Walk within roughly two grid cells of a loaded NPC and press `E` to open dialogue. The runtime overlay displays speaker/text, choices, and Continue/Close controls. Choice and node actions execute through the existing `GameEventRuntime`, preserving quest state.

For a licensed-editor-independent compile check, run `powershell -ExecutionPolicy Bypass -File scripts/compile_runtime.ps1` from the repository root. This compiles all non-test C# sources against the installed Unity 6000.4 reference assemblies; it does not execute Unity scenes, render assets, or prove play-mode travel and interaction.
