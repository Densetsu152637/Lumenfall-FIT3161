# ambientCG textures

PBR textures downloaded from [ambientCG](https://ambientcg.com) on 2026-10-14 in the 1K JPG variant. All ambientCG assets are released under [CC0 1.0](https://docs.ambientcg.com/license/), so no attribution is required. Only the colour and OpenGL normal maps are kept; the Blender, USD, MaterialX and Godot files, displacement and roughness maps from the downloads were left out.

| Folder | Asset | Source | Used for |
| --- | --- | --- | --- |
| `Ice002` | Ice 002 | [ambientcg.com/view?id=Ice002](https://ambientcg.com/view?id=Ice002) | Frozen pool in `C03.IceTrial` |
| `Ice003` | Ice 003 | [ambientcg.com/view?id=Ice003](https://ambientcg.com/view?id=Ice003) | Ice-block walls in `C03.IceTrial` |
| `Snow013` | Snow 013 | [ambientcg.com/view?id=Snow013](https://ambientcg.com/view?id=Snow013) | Trial floor and the frost patch at the campus entrance |

`Assets/_Project/Editor/C03IceTrialArtTool.cs` builds the project materials from these textures; the textures themselves are not edited.
