# Relic mesh backup

This folder keeps the original Stella circuit relic model, `tripo_convert_249be0a3-6c2e-4c2d-ba97-9f130d8c7ed0.fbx`. It is about 1,866,832 triangles and 56 MB. The copy in `Lumenfall/Assets/_Project/Art/Environment/Puzzle Elements/` was replaced on 2026-10-12 with a 29,998-triangle version made in Blender 5.2 by `decimate_fbx.py`. The full-detail mesh made the Arcane Workshop (B02) lag.

The reduced file keeps the original object, mesh and material names, units (`UnitScaleFactor` 100), axes and transform. Unity therefore imports it under the same mesh fileID, and the `PF_Runestone_*` prefabs keep their references. `preview_original.png` and `preview_reduced_30k.png` compare the two with the base colour texture.

## Reverting

1. Close the Unity editor, or switch away from it.
2. Copy `tripo_convert_249be0a3-6c2e-4c2d-ba97-9f130d8c7ed0.fbx` from this folder over the one in `Puzzle Elements/`.
3. Optionally copy the `.meta` from this folder too. It is the original import settings, with Mesh LOD generation off. Either `.meta` keeps the same GUID.

The original is also in git history at commit `c8b3f44`. Running `git checkout c8b3f44 -- "Lumenfall/Assets/_Project/Art/Environment/Puzzle Elements/tripo_convert_249be0a3-6c2e-4c2d-ba97-9f130d8c7ed0.fbx"` restores it as well.

This folder is outside `Assets`, so Unity does not import it.
