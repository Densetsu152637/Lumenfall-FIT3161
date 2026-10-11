# Startup menu

`StartupMenu` creates an overlay after the first scene has loaded. The current world stays
visible below it, while every `GridPlayerInput` component is disabled until Start or a successful
Load closes the menu. This keeps the backdrop useful for orientation without allowing movement or
portal interaction through the menu.

The menu has one dedicated `autosave` slot and three manual snapshot slots: `save-1`, `save-2`,
and `save-3`. Autosave is the active writable slot throughout play, including after loading a
manual snapshot. The existing `save-*` files remain in place when upgrading; if no autosave exists,
the most recently played valid legacy slot is loaded and copied into `autosave` while remaining
available as a manual save. On a fresh profile no placeholder save is created. Starting a new game
overwrites an existing autosave only after a second Start press confirms it.

The Load page shows an Autosave card plus three Manual Slot cards, with no save actions. The menu
has no Save page, and nothing in the game writes to the manual slots, so they only hold snapshots
that already existed (for example, legacy saves from before autosave). Loading a manual slot
restores its state, copies that state into `autosave`, and continues with autosave active. Loading
Autosave simply continues the current autosave. The overlay also disables
active dialogue overlays while it is visible,
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
| [`MenuSkin.cs`](MenuSkin.cs) | The menu's look packaged for in-game menus: textures, styles, themed controls, motes, wand cursor and sparks. |
| [`MenuSkinProps.cs`](MenuSkinProps.cs) | Themed props: relic switches, volume lantern, frost, hourglass, wax seal, inked text, ribbons, runes, page flips and candle hovers. |
| [`MenuProps.cs`](MenuProps.cs) | Generated textures for those props. |
| [`OptionsPanel.cs`](OptionsPanel.cs) | Sectioned in-game options shared by the Settings pop-out and the journal's Settings tab. |

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

Escape opens the startup menu during gameplay and saves the current state first. With the menu
open, Escape returns from Load and Settings to the main page, and from Controls to Settings; from
the main page it closes the menu and resumes gameplay. While a key is being rebound or a swap prompt
is showing, Escape only cancels that. On the main page, Up/Down (or W/S) select an option and Enter
or Space activates it.

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
| Playtime | Unscaled time while `Time.timeScale > 0` and the menu is hidden. It resets on New Game and is restored from the loaded slot's summary; loading a manual save carries that playtime into autosave. |
| Thumbnail | A 320×180 render of `Camera.main` taken in `LateUpdate`. It uses a URP `StandardRequest` render request, falling back to `Camera.Render`. Autosaves refresh it at most every 10 seconds per slot. |

Future menus can replace the IMGUI drawing methods while retaining the slot constants and the
`GameSaveManager.FindMostRecentSlot` contract. `GameAutosave` writes to `autosave` every 30 seconds
of active gameplay and never writes to a manual slot. Returning to the main menu also explicitly
saves the current state. There are no event-triggered or app-pause autosaves. After New Game, the
first periodic autosave is delayed until 30 seconds of active gameplay have elapsed.

Manual validation (not yet executed): the title sequence, camera drift and return, cursor, rune
ring, page fades and thumbnails have been compiled but not yet checked in Play mode or a player
build.

## In-game options

Two in-game menus reuse the startup menu's look: the Settings pop-out opened by the top-right
button (`QuestHintOverlay`) and the journal (`InventoryEncyclopediaUI`). Each host owns a
`MenuSkin` and an `OptionsPanel`. `StartupMenu` keeps its own copies of the same drawing code and
does not use `MenuSkin` yet.

`MenuSkin` builds its textures and Nunito styles on first use inside `OnGUI` and destroys them in
`Dispose`. Every draw is multiplied by its `Fade`, so a host can fade whole layers. It also provides
the dusk backdrop with 18 drifting motes, pointer parallax, the wand cursor with its spark trail and
click bursts, the panel frame with twinkling corner stars, the title glow, the star divider, section
headings, text buttons, tabs with a gold underline, the gold slider, the sliding switch, and a
`using` scope that swaps in the gold scrollbar. Animation uses unscaled time.

`OptionsPanel` splits the options into five sections. Each section is a silk bookmark ribbon that
hangs from a spellbook spine and carries a rune: a compass for Gameplay, a bell for Audio, a lantern
for Display, a wand for Controls and an open hand for Accessibility. The chosen ribbon pulls out
further, and its rune lights gold. In the pop-out the chosen section fades in, while the journal
turns a page instead. The chosen section stays selected between openings.

| Section | Contents |
| --- | --- |
| Gameplay | `Show quest hints` (`QuestHintOverlay.HintsEnabled`, the same `lumenfall.quest-hints-enabled` key as the startup menu). |
| Audio | Master volume (`GameSettingsPanel.MasterVolume`, `lumenfall.master-volume`). |
| Display | Fullscreen (`Screen.fullScreen`, remembered by Unity, no effect in the editor) and VSync (`lumenfall.vsync`, applied at launch by `StartupMenu`). |
| Controls | The grouped rebinding list from `GameSettingsPanel.DrawControls`. |
| Accessibility | Reduce motion (`lumenfall.reduce-motion`). |

Reduce motion turns off the drifting motes, pointer parallax, the wand's spark trail and click
bursts, the star divider's rocking, the panels' rise and settle, and the decorative motion listed
under Themed props below. It applies to the in-game menus
and to the startup menu, which also skips its camera drift. The startup menu reads it when it opens.

The rebinding list (shared with the startup menu's Controls page) is grouped under Movement, Spells
and Interface headings. The seven spell slots are folded into one `Spell slots 1–7` row with a
Show/Hide button, and they open automatically while one of them is being rebound. If the chosen key
already belongs to another action, nothing changes until the player answers the prompt
(`E is already used by Talk to NPC. Swap them, so Talk to NPC moves to F?`): **Swap** or Enter swaps
the two keys, and **Cancel** or Escape keeps the old binding.

- **Settings pop-out (pause menu).** The top-right `Settings` button, or Escape during play, opens
  it. Escape does not open it while the startup menu exists or the journal is open, or on the frame
  the journal closed (`InventoryUiState.ClosedFrame`). The pop-out sets `Time.timeScale` to 0 and
  restores the previous value on close, unless something else changed it meanwhile. It also
  disables `GridPlayerInput` and sets `QuestHintOverlay.IsSettingsOpen`, which gates casting, NPC
  interaction and dialogue input. The panel (up to 860×660) shows the options and a status line
  (`Game paused · Last saved 2 min ago`, from `GameSaveManager.TryGetSavedAt`, checked at most once a
  second). Below that are four buttons: **Save now** saves the current slot and shows the autosave
  indicator. **Main menu** calls `ReturnToMainMenu`. **Quit game** saves the current slot and calls
  `Application.Quit`, which does nothing in the editor. **Close** closes the pop-out, as do Escape
  and the top-right button (which reads `Close` while open). The button is hidden while the journal
  is open.
- **Journal.** The journal shows the same options on its Settings tab, and its frame, tabs and list
  pages use the same skin. It locks the player mover but does not pause the world. See
  [`docs/inventory-encyclopedia.md`](../../../../../docs/inventory-encyclopedia.md).
- **Draw order.** Both menus set `GUI.depth` to -10 while open, so they draw above dialogue and the
  quest hint card. Each restores `GUI.color` when it finishes drawing.

### Themed props

The props tie the menus to the game's magic. Each one settles to its end state when Reduce motion is
on.

| Prop | Behaviour |
| --- | --- |
| Relic switches | Each switch is an Ampere relic in a socket track. Off, it rests left, dim amber, with an idle amber thread. On, it slides right and turns cyan, humming, with a powered cyan thread behind it. These are the `WeaveCableView` colours. Toggling sends a spark along the track into the relic. |
| Volume lantern | A brass lantern ends the volume slider. The flame and its light grow with the volume and flicker like a candle. At 0% it goes out with a wisp of smoke. |
| Glaciate frost | While the pop-out pauses the game, frost creeps in from the screen edges over 1.2 seconds, with glints twinkling along it. After closing, it thaws over 0.8 seconds from however far it had grown. With Reduce motion it appears and disappears at once. The journal does not pause, so it has no frost. |
| Hourglass | A small brass hourglass with sand frozen mid-fall sits beside `Game paused` in the pop-out's status line. |
| Wax seal | A successful Save now presses a gold wax seal beside the save status. It drops in, lands with a few sparks and glows briefly. It is cleared when the pop-out reopens. |
| Inked status | The save status writes itself in from left to right behind a glowing nib, then dries from cream to muted. This happens whenever the text changes. |
| Page turns | The journal's tabs and its Settings sections turn in from the left like a book page (0.4 seconds), with shading and a bright page edge. |
| Candle hovers | Hovered buttons, tabs, ribbons, switch rows and rebinding rows get a warm, flickering candle glow, which replaced the steady lavender glow. `GameSettingsPanel.RowHover` lets the host draw it on rebinding rows. |

The startup menu's own Settings and Controls pages still use the plain gold slider and switch, and
do not have these props.

Manual validation (not yet executed): the themed props, the pop-out and journal, the section navigation, Escape to
open and close, pausing, Save now and the status line, Quit in a player build, the swap prompt,
the folded spell slots, fullscreen and VSync in a player build, and Reduce motion have been compiled
but not yet checked in Play mode or a player build.
