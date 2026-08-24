# First-party assets

This directory is the home for content developed specifically for Lumenfall.

## Folders

- [`Art`](Art/README.md) — project-owned animation and, as the game grows, audio,
  materials, models, sprites, textures, and VFX.
- [`Input`](Input/README.md) — shared Input System action assets.
- [`Scenes`](Scenes/README.md) — playable scenes included in build settings.
- [`Settings`](Settings/README.md) — render-pipeline and other project-owned Unity
  configuration assets.
- [`Systems`](Systems/README.md) — feature-oriented runtime code, tests, documentation,
  and owned data.

Keep imported packages in `Assets/ThirdParty` and Unity samples in `Assets/Samples`.
Always move assets inside Unity when practical; if moving them outside the editor, move
the matching `.meta` file too so serialized GUID references remain valid.
