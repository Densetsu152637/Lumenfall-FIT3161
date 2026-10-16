# Art

Store first-party presentation assets here, grouped by discipline:

- `Animations` — animation clips, animator controllers, avatars, masks, and blend trees.
- `Audio` — music, ambience, dialogue, and sound effects.
- `Materials` — project-owned materials and shaders.
- `Models` — project-owned meshes and source models.
- `Sprites` and `Textures` — 2D artwork and project-owned texture maps.
- `VFX` — particles, visual-effect graphs, and effect-specific assets.

Create a discipline folder only when the first asset of that type is added. Keep files
used exclusively by an imported pack inside `Assets/ThirdParty` instead of copying them
here. Feature-specific presentation assets may use a subfolder named after their system.

Extracted vendor assets under `Environment/ThirdPartyExtracted` are
project-maintained copies. Review shader compatibility edits there against the
vendor source and reapply them deliberately after any source-pack refresh.
