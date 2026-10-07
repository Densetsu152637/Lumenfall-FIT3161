# Startup menu

`StartupMenu` creates an overlay after the first scene has loaded. The current world stays
visible below it, while every `GridPlayerInput` component is disabled until Start or a successful
Load closes the menu. This keeps the backdrop useful for orientation without allowing movement or
portal interaction through the menu.

The menu uses the three slots `save-1`, `save-2`, and `save-3`. On first launch it reloads the
event definitions and creates a blank seed in the first available slot. On later launches the
newest valid slot is loaded as the backdrop. A separate `last-played.json` marker takes precedence
when valid, while save timestamps provide recovery when the marker is absent or invalid. Start
reuses the blank seed or selects the first empty slot.

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

## Files

| File | Responsibility |
| --- | --- |
| [`StartupMenu.cs`](StartupMenu.cs) | Menu flow, slot logic, pages, title sequence and rune ring. |
| [`MenuArt.cs`](MenuArt.cs) | Palette and runtime-generated textures (panels, glows, rings, wand cursor). |
| [`MenuSparks.cs`](MenuSparks.cs) | Screen-space motes and sparks: ambient motes, wand trail, click bursts, dissolve. |
| [`MenuCameraDrift.cs`](MenuCameraDrift.cs) | Pans the gameplay camera while the menu is open and hands it back. |
| [`SaveSlotSummary.cs`](SaveSlotSummary.cs) | Writes the location, quest, playtime and thumbnail shown on the Load page. |

## Flow

On the first open after launch the menu plays a title sequence. `Lumenfall` appears unlit in the
centre of the screen, and a spark crosses it, lighting each letter like a lantern. A
`Press any key to begin` prompt then appears. Any key or click (including one during the lighting)
moves the title into the panel and fades the main page in. Reopening the menu from the journal
with `ReturnToMainMenu` skips the sequence. The `introPlayed` flag is static and is reset at
`SubsystemRegistration` so the sequence still plays when domain reload is disabled.

Start and a successful Load dissolve the panel into light. The backdrop fades out and the camera
eases back to its follow pose over `CloseSeconds` (1.3 seconds). Only then is the menu destroyed
and input restored.

Escape and the BACK buttons share one route: Load and Settings return to the main page, and
Controls returns to Settings. While a key is being rebound, Escape only cancels the rebind. On the
main page, Up/Down (or W/S) select an option and Enter or Space activates it.

## Presentation

The menu is drawn with IMGUI. Apart from the button sheets, all of its art is generated at runtime
and destroyed with the menu. That includes the four-pointed star from
`GameSaveOverlay.CreateSparkleTexture`. The palette uses gold, cream and lavender to match the
dialogue, building-sign and autosave overlays. Text uses `Resources/Dialogue/Nunito-Regular`.
Animation uses `Time.unscaledTime` because the menu sets `Time.timeScale` to 0.

Pages are authored at fixed pixel sizes (Main 640×520, Load 700×650, Settings 560×560, Controls
560×440–720) and draw at that size whenever they fit. If the current page, plus a 48×24 margin,
does not fit the window, `FitScale` shrinks the title and panel uniformly through `GUI.matrix`, by
just enough to fit, and the scale eases over a few frames when the page changes. The Controls page
shortens its scrolling list (down to 440) before any scaling is needed. The backdrop, motes and
sparks always use real window pixels; IMGUI maps the pointer into the scaled panel automatically.

- **Living backdrop.** `MenuCameraDrift` moves the camera from `LateUpdate`, which runs after
  `CameraFollow2_5D` because of the menu's execution order. It eases from the camera's current pose
  into a wider view that sways a few degrees and wanders a few tiles around the player. On close it
  returns to `CameraFollow2_5D.TryGetFollowPose` and calls `ResetSmoothing`. The world itself stays
  paused, so only the camera moves.
- **Parallax.** The pointer position, smoothed over time, shifts the motes (nearer motes shift
  more), nudges the panel a few pixels and adds a small camera offset.
- **Wand cursor.** While the menu is open the system cursor becomes a generated wand. The default
  is restored in `OnDestroy`. Moving leaves a trail of sparks, and clicks throw a small burst.
- **Rune ring and woven light.** The main page has a rune ring beside the buttons, with one rune
  per option. At rest the ring turns slowly. Hovering a button or rune, or selecting one with the
  keyboard, turns that rune to the socket on the ring's right. Once it is aligned, cyan light fills
  the weave thread from the socket to the button, and beads of light travel along it. The threads
  are amber while idle, matching `WeaveCableView`. Clicking a rune activates its option.
- **Page changes.** Each page fades in and settles upward slightly over 0.25 seconds. The panel
  takes the new page's size immediately.
- **Controls.** Each button sheet has four stacked frames (normal, hover, pressed, disabled). The
  menu uses the hover and pressed frames and keeps the sheet's aspect ratio. The volume slider and
  the quest-hint switch are custom-drawn over IMGUI controls. The Controls page temporarily swaps
  in a themed vertical scrollbar.

## Load page and save summaries

The Load page shows each slot as a journal card. A filled card shows a thumbnail, the location, the
quest in progress (or a status such as `A new term begins`), and a row of stars with one per quest,
lit for each completed one, followed by `n of m quests`. The save file's last-write time and the total
playtime sit in a small column under the card's LOAD button. Card text is single-line. Text that
is too long steps its font size down to 12 and is then trimmed with an ellipsis. The slot the backdrop came from is marked `Most recent` unless it is the blank seed.
Missing files read `Empty — a fresh page awaits`.

`SaveSlotSummary` bootstraps itself after scene load and listens to `GameSaveManager.SaveCompleted`
and `LoadCompleted`. It writes `<slot>.summary.json` and `<slot>.thumb.png` next to the save in
`Application.persistentDataPath`, and never modifies the save file. A missing or stale summary only
changes what the card shows.

| Field | Source |
| --- | --- |
| Location | The loaded map's name (`westForest` is `West Forest`, `B04.Ground` is `Sirius Dormitory`, and so on), refined by the smallest named map region containing the player. Code-like region names such as `S03` are ignored. |
| Quest | `quest` holds the first active quest in `QuestHintOverlay.QuestOrder`, or `A new term begins`, `Between quests` or `Every quest complete`. `questsDone` and `questsTotal` hold the progress counts. Older summaries that stored the combined `title · n of m quests done` text are split when the Load page reads them. |
| Playtime | Unscaled time while `Time.timeScale > 0` and the menu is hidden. It resets on New Game and is restored from the summary on load. |
| Thumbnail | A 320×180 render of `Camera.main` taken in `LateUpdate`. It uses a URP `StandardRequest` render request, falling back to `Camera.Render`. Autosaves refresh it at most every 10 seconds per slot. |

Future menus can replace the IMGUI drawing methods while retaining the slot constants and the
`GameSaveManager.FindMostRecentSlot` contract. A scene-specific menu may call `GameSaveManager.Load`
or `Save` directly and can reuse the same three-slot naming convention.

Manual validation (not yet executed): the title sequence, camera drift and return, cursor, rune
ring, page fades and thumbnails have been compiled but not yet checked in Play mode or a player
build.
