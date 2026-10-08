# Authored world content

World content is stored as editable Unity assets in this folder. The SampleScene's `DataDrivenWorldLoader` has direct references to the starting area, five additional areas, NPC catalog, content root, and grid world. At runtime the loader activates the authored area prefabs; it does not parse the legacy map or NPC text files.

To regenerate these assets after intentionally changing a legacy migration input, open Unity 6000.4.10f1 and run **Lumenfall > World > Bake Legacy World Content**. The batch entry point is `Lumenfall.EditorTools.WorldContentMigration.BakeFromBatch`. The baker validates saved prefab/asset references and scene wiring. Each area prefab contains its tile obstacles, building and object registrations, active portal components, and placed NPCs. Edit placements in Prefab Mode, portal destinations/arrival offsets on `PortalContentAsset`, and NPC dialogue or quest visibility in `NpcCatalog.asset`.

The migration preserves the prior portal glyph filter: reserved definitions and outdoor portals without the configured glyph are not live. It corrects three inherited blocked indoor return positions to the nearest free campus approach tile: B02 `(23,10)`, B03 `(53,6)`, and B04 `(85,26)`. Those values are serialized on the source portal assets and can be edited in the Inspector.

The seven NPC source entries, catalog and baked instances use `Assets/Resources/NPCs/NpcPlaceholder.prefab`. Moving this existing visible cube fallback into Resources preserves its GUID and all baked references, and makes the source identity loadable. The legacy incomplete Elara file is retained as historical content.

The verified bake contains six area prefabs, nine registered map objects, 1,805 obstacle footprints, seven NPCs, and thirteen active portals. The PlayMode smoke test covers SampleScene startup, all ten active portal edges, world save restoration, and Cinna's quest-gated NPC visibility after area revisits.


PR check repair places the provisional B01 footprint at `(40,36)` and B04 at `(72,28)` within the existing 92x54 campus, keeping the garden and NPC cells clear. Campus `(42,30)` and forest `(25,10)` are nonblocking spawn cells. B02/B03/B04 campus approaches `(23,11)`, `(53,7)`, `(85,27)` are baked as reciprocal live portals; each lands at indoor `(8,1)` and returns one cell outside its campus endpoint. Reserved gates remain closed. Final composition and live traversal still need manual acceptance.
