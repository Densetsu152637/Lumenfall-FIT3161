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

## Graphics options

Settings offers VSync (Off/On), antialiasing (Off/FXAA/SMAA), ambient occlusion (Off/On),
and shading quality (Low/Medium/High). Changes apply immediately and save automatically;
Reset Graphics restores the device defaults without changing master volume or gameplay saves.
The settings content scrolls on smaller windows.

`GraphicsOptions` bootstraps before the first scene and survives the startup menu and scene
loads. It applies antialiasing to game cameras before they render, including cameras created
later. FXAA/SMAA enable camera post-processing; Off preserves the camera's authored
post-processing state. MSAA is omitted because the PC renderer uses deferred rendering.

Shading quality controls shadow distance, main-light shadow-map resolution, cascade count,
and LOD bias. Low caps these at 20 units / 512 pixels / 1 cascade / 0.75 LOD bias.
Medium uses 35 / 1024 / 2 / 1 LOD bias. Soft-shadow support remains as authored.
High uses the authored pipeline and LOD settings. The caps never exceed the authored values.
This setting preserves the project's materials and lighting model.

Defaults retain VSync Off, antialiasing Off, and authored shading (High). Ambient occlusion
defaults On for desktop and Off for mobile. The AO control is unavailable when the active
renderer configuration contains no `ScreenSpaceAmbientOcclusion` feature, as with the current
mobile pipeline. The system applies to the active quality pipeline at session startup;
external quality-pipeline switching during a session is not part of this menu.

Preferences use the `lumenfall.graphics.vsync`, `lumenfall.graphics.antialiasing`,
`lumenfall.graphics.ambient-occlusion`, and `lumenfall.graphics.shading` PlayerPrefs keys.
Missing entries use defaults; invalid enum values fall back safely. No save migration or
Inspector wiring is required. Shading uses a transient pipeline copy. Shared AO feature state,
camera settings, VSync, LOD bias, and the quality pipeline are restored when the graphics
component is destroyed, including leaving Play Mode.

URP unused-variant stripping is disabled in the global settings so runtime AO and shadow-cascade
switches remain available in players. See the [render settings guide](../../Settings/README.md)
for the build-size tradeoff. EditMode preference tests live in `Tests/EditMode`.

### Manual verification

1. Open `SampleScene` in Unity 6000.4.10f1, enter Play Mode, and choose Settings.
2. Change each option. Check game-camera antialiasing/post-processing, the transient URP
   shadow settings, and the AO renderer feature. Inspect visible edges and contact shadows
   in a lit 3D area; flat/unlit sprites may show little difference.
3. Reset Graphics, then select non-default choices. Start or load a game and confirm those
   settings remain active. Restart and confirm they are restored.
4. Resize the window to 800x600 and confirm all settings and Back remain reachable by scrolling.
5. Exit Play Mode and confirm the authored pipeline, cameras, AO feature, VSync, and LOD bias
   have their original values. Repeat in a Windows player to check shader availability.
