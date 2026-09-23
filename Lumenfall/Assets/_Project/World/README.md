# Authored world content

World content is stored as editable Unity assets in this folder. The SampleScene's `DataDrivenWorldLoader` has direct references to the starting area, five additional areas, NPC catalog, content root, and grid world. At runtime the loader activates the authored area prefabs; it does not parse the legacy map or NPC text files.

To regenerate these assets after intentionally changing a legacy migration input, open Unity 6000.4.10f1 and run **Lumenfall > World > Bake Legacy World Content**. The batch entry point is `Lumenfall.EditorTools.WorldContentMigration.BakeFromBatch`. The baker validates saved prefab/asset references and scene wiring. Each area prefab contains its tile obstacles, building and object registrations, active portal components, and placed NPCs. Edit placements in Prefab Mode, portal destinations/arrival offsets on `PortalContentAsset`, and NPC dialogue or quest visibility in `NpcCatalog.asset`.

The migration preserves the prior portal glyph filter: reserved definitions and outdoor portals without the configured glyph are not live. It corrects three inherited blocked indoor return positions to the nearest free campus approach tile: B02 `(23,11)`, B03 `(53,7)`, and B04 `(85,47)`. Those values are serialized on the source portal assets and can be edited in the Inspector.

The seven legacy NPC entries name `Resources/NPCs/Elara.prefab`, but Unity imports that incomplete file as a `DefaultAsset` and it has no renderer. The catalog and baked NPC instances therefore reference the reusable `NPCs/NpcPlaceholder.prefab`, which matches the visible cube fallback used when the old runtime loader could not load the legacy prefab.

The verified bake contains six area prefabs, nine registered map objects, 1,805 obstacle footprints, seven NPCs, and ten active portals. The PlayMode smoke test covers SampleScene startup, all ten active portal edges, world save restoration, and Cinna's quest-gated NPC visibility after area revisits.

