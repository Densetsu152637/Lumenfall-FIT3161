# Startup menu

`StartupMenu` creates a static overlay after the first scene has loaded. The current world stays
visible below it, while every `GridPlayerInput` component is disabled until Start or a successful
Load closes the menu. This keeps the backdrop useful for orientation without allowing movement or
portal interaction through the menu.

The menu uses the three slots `save-1`, `save-2`, and `save-3`. On first launch it reloads the
event definitions and creates a blank seed in the first available slot. On later launches the
newest valid slot is loaded as the backdrop. A separate `last-played.json` marker takes precedence
when valid, while save timestamps provide recovery when the marker is absent or invalid. Start
reuses the blank seed or selects the first empty slot. Load displays all three slots and labels
missing files `empty save file`.

If all three slots are occupied, the first Start press asks for an explicit second Start press
before replacing slot 1. The overlay also disables active dialogue overlays while it is visible,
so dialogue choices cannot be selected behind the menu.

Button sheets are authored in `Assets/2D` and copied into `Resources/StartupButtons` for player
build loading. The source files remain the authoring assets; replace both locations when art is
updated, or change `LoadButtonTextures` to use a different asset pipeline. Master volume is stored
in the `lumenfall.master-volume` PlayerPrefs key and currently maps to `AudioListener.volume`.

The startup overlay freezes `Time.timeScale`, restores its previous value on exit, and repeatedly
guards movement, dialogue, and the legacy save overlay as world objects are created. Successful
gameplay loads and New Game starts update the last-played marker; backdrop restoration does not.

Future menus can replace the IMGUI drawing methods while retaining the slot constants and the
`GameSaveManager.FindMostRecentSlot` contract. A scene-specific menu may call `GameSaveManager.Load`
or `Save` directly and can reuse the same three-slot naming convention.
