# Startup menu

`StartupMenu` creates a static overlay after the first scene has loaded. The current world stays
visible below it, while every `GridPlayerInput` component is disabled until Start or a successful
Load closes the menu. This keeps the backdrop useful for orientation without allowing movement or
portal interaction through the menu.

The menu uses the three slots `save-1`, `save-2`, and `save-3`. On first launch it reloads the
event definitions and creates a blank seed in the first available slot. On later launches the
newest valid slot is loaded as the backdrop. A separate `last-played.json` marker takes precedence
when valid, while save timestamps provide recovery when the marker is absent or invalid. Start
reuses the blank seed or selects the first empty slot. Load displays all three slots as cards. A
filled slot shows its file's last-write time and a LOAD button, and the slot the backdrop came from
is marked `Most recent` unless it is the blank seed. Missing files are labelled `Empty — a fresh page awaits`.

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

## Presentation

The menu is drawn with IMGUI. Apart from the button sheets, all of its art is generated at runtime
and destroyed with the menu: a dusk-coloured backdrop with a vignette, soft glows, rounded panels,
cards and pills, and the four-pointed star from `GameSaveOverlay.CreateSparkleTexture`. The palette
uses gold, cream and lavender to match the dialogue, building-sign and autosave overlays, and text
uses `Resources/Dialogue/Nunito-Regular`.

Animation uses `Time.unscaledTime` because the menu sets `Time.timeScale` to 0. The menu fades and
rises in on open, and each page fades in when selected. Lantern-like motes drift upward behind the
panel, and stars twinkle in the panel corners and on the title divider. Each button sheet has four
stacked frames (normal, hover, pressed, disabled). The menu draws the hover and pressed frames with
a soft glow and keeps the sheet's aspect ratio. The volume slider and the quest-hint switch are
custom-drawn over IMGUI controls. The Controls page temporarily swaps in a themed vertical
scrollbar and restores the skin afterwards.

Escape and the BACK buttons share one route: Load and Settings return to the main page, and
Controls returns to Settings. While a key is being rebound, Escape only cancels the rebind.

Future menus can replace the IMGUI drawing methods while retaining the slot constants and the
`GameSaveManager.FindMostRecentSlot` contract. A scene-specific menu may call `GameSaveManager.Load`
or `Save` directly and can reuse the same three-slot naming convention.
