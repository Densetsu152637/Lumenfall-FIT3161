# Startup menu and shared settings

`StartupMenu` bootstraps after scene load and pauses gameplay until Start or a successful Load completes its closing transition. It owns an `OverlayInputGate` lease, disables active gameplay input and dialogue overlays, and restores its previous time scale and input state when destroyed. Generated textures and event subscriptions are released with their owner.

## Settings

The menus keep their original look; the graphics, volume-bus and accessibility settings are added to them.

- **New Game.** START opens a New Game page that lists the three manual slots as cards. Pressing a
  card's START begins a new game in that slot. A filled slot asks for a second START before it is
  replaced (`Slot 2 already holds a save…`). If the slot is empty but an autosave exists, the second
  press confirms replacing the autosave instead. The run is linked at once, but nothing is written
  until the run's first save: the first autosave after 30 seconds of real play, or an earlier journal
  Save or Main menu. The slot is written together with that save
  (`GameAutosave.BeginNewRun(slot)`), so a new game abandoned straight away leaves the old files as
  they were. New Game also queues the opening cutscene (`opening_arrival`). It takes over the panel's
  dissolve, so the light streams into the player's spawn and the player appears from it; see
  [the Cutscenes README](../Cutscenes/README.md). Autosave waits while it plays.
- **Load.** The Load page shows an Autosave card plus three Manual Slot cards. Loading a manual slot
  restores its state and links the run to that slot (older files carry no link). Play then
  continues in `autosave`, which is first written after 30 seconds of real play; the manual file is
  never touched by autosave. Loading Autosave continues the current autosave, keeping whatever link
  it holds.
- **Save.** The journal's Save button writes the linked slot (see In-game options). A run with no
  link, such as one carried over from before slots were linked, takes the first empty slot. If all
  three are full, the player is asked to load one first. The overlay also disables
active dialogue overlays while it is visible,
so dialogue choices cannot be selected behind the menu.
**Startup menu.** Settings is the original card: master, music, sound effects and ambience volume sliders, then the
Show quest hints switch. Below it, **Controls** opens the key-rebinding page and **Graphics** opens a page in the same
card style with VSync (relic switch), antialiasing Off/FXAA/SMAA (lavender pills), ambient occlusion (switch), shading
quality Low/Medium/High (pills), the recommended preset and **Use recommended**. Pages keep fixed sizes (Settings
560×758, Graphics 600×610) and shrink through `FitScale` on small windows.

**In-game options.** The Settings pop-out (`QuestHintOverlay`) and the journal's Settings tab (`InventoryEncyclopediaUI`)
draw the same `OptionsPanel`: silk bookmark ribbons hanging from a spellbook spine, each with a rune, relic switches,
lantern volume sliders, and page turns in the journal. The chosen section stays selected between openings. A section
whose rows do not fit (for example with a large text size) scrolls with the gold scrollbar.

| Section | Options |
| --- | --- |
| Gameplay | Show quest hints; sprint Hold/Toggle. |
| Audio | Master, music, sound effects and ambience volume. |
| Display | Fullscreen (player build only); VSync. |
| Graphics | Antialiasing (Off/FXAA/SMAA); ambient occlusion; shading quality (Low/Medium/High); recommended preset and Use recommended. |
| Controls | Grouped key rebinding. |
| Accessibility | Reduce menu motion; reduce camera motion; high contrast; instant dialogue; dialogue speed; text size (100–175%). |

Keyboard and controller: on the startup Settings and Graphics pages, Up/Down move the focus, Left/Right adjust the
focused slider, switch or choice, and Enter/A toggles or presses it. In the in-game options, Up/Down move between
ribbons; Right or Enter steps into the section's rows, where Up/Down move between rows (Up from the first returns to the
ribbons) and Left/Right/Enter adjust as above. The focus glow appears only once keys or a controller are used. Options
the renderer cannot apply are dimmed, read `Not available with this renderer`, and keep their saved values.

The journal opens with the configured Journal key (Tab by default) or the controller Select button; shoulder buttons switch its outer Inventory/Encyclopedia/Settings pages. Escape closes it. Escape during gameplay or the Settings button opens the pause panel. The pause panel holds only the options and a `Game paused · Esc to resume` line; its top-right button reads Close. The journal footer provides **Main menu**, **Save · Slot N** and **Quit game**, with the linked slot's save time inked beside them. A failed save stays visible in the footer and prevents Quit from closing the game.

Controls groups movement, spell and interface actions. Selecting an action captures its next key; a conflicting assignment requires an explicit Swap or Cancel. Escape cancels the capture. Runtime movement, sprint, talk, spell selection/casting and journal input use these bindings.

## First-install graphics recommendations

`GraphicsPreferences.LoadOrCreate` fills missing graphics keys from `GraphicsRecommendations.ForCurrentDevice`. Existing choices survive launch and hardware changes. Use Recommended explicitly reapplies the current device's preset without changing Main settings.

| PC tier | Minimum reported specs | VSync | AA | AO | Shading |
| --- | --- | --- | --- | --- | --- |
| High | 15,000 MB RAM, 8 logical processors, 4,000 MB VRAM, shader level 45, graphics device | On | SMAA | On | High |
| Medium | 7,500 MB RAM, 4 logical processors, 2,000 MB VRAM, shader level 45, graphics device | On | FXAA | Off | Medium |
| Low | Below any Medium threshold, or unknown/headless hardware | On | Off | Off | Low |

These are conservative hardware heuristics, not measured frame-rate guarantees. Shared/integrated GPU memory can be reported imprecisely; manual overrides remain available. Mobile retains Off VSync/AA/AO and High shading to preserve the existing mobile configuration.

- **Living backdrop.** `MenuCameraDrift` moves the camera from `LateUpdate`, which runs after
  `CameraFollow2_5D` because of the menu's execution order. It eases from the camera's current pose
  into a wider view that sways a few degrees and wanders a few tiles around the player. On close it
  returns to `CameraFollow2_5D.TryGetFollowPose` and calls `ResetSmoothing`. The world itself stays
  paused, so only the camera moves.
- **No player in the backdrop.** While the menu is open, the player is hidden through
  `PlayerVisibility` (re-applied each frame, since loading the backdrop save can rebuild the world),
  and the tree see-through circle (`PlayerOcclusionReveal`) is off, so a new player does not think
  they are already playing. Load and Escape show the player as the panel dissolves. After New Game
  the opening hides them first, so they stay hidden until its flash. A save thumbnail taken while
  the menu is open (Main menu's autosave, or promoting a legacy slot) briefly shows the player for
  the render (`PlayerVisibility.RenderVisible`).
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
Preferences use `lumenfall.graphics.vsync`, `.antialiasing`, `.ambient-occlusion`, and `.shading`. `GraphicsOptions` applies them before scene load, clones the active URP asset for runtime changes, configures current and later game cameras, and restores authored resources on destruction. VSync maps to `QualitySettings.vSyncCount`; AA uses URP camera FXAA/SMAA; AO toggles the renderer's existing SSAO feature. Shading adjusts shadow distance/resolution/cascades and LOD bias. Disabling AA restores each camera's original post-processing state. URP runtime variant stripping is disabled so player builds retain selectable AA variants.

Main preferences retain their own stores: `AudioMixSettings` owns bus levels, `AccessibilitySettings` owns accessibility preferences, `KeyBindings` owns keyboard bindings and `GameSettingsPanel` owns reduced menu motion. The old display VSync adapter delegates to `GraphicsOptions`, so no second VSync authority competes with it.

## Main menu and saves

The first launch presents the authored starting world behind the menu without creating a placeholder save. Later launches load `autosave` as a backdrop. Legacy manual saves can be promoted to autosave without overwriting their originals. Backdrop loading does not mark a slot as newly played.

| Field | Source |
| --- | --- |
| Location | The loaded map's name (`westForest` is `West Forest`, `B04.Ground` is `Library`, and so on), refined by the smallest named map region containing the player. Code-like region names such as `S03` are ignored. |
| Quest | `quest` holds the first active quest in `QuestHintOverlay.QuestOrder`, or `A new term begins`, `Between quests` or `Every quest complete`. `questsDone` and `questsTotal` hold the progress counts. Older summaries that stored the combined `title · n of m quests done` text are split when the Load page reads them. |
| Playtime | Unscaled time while `Time.timeScale > 0` and the menu is hidden. It resets on New Game and is restored from the loaded slot's summary; loading a manual save carries that playtime into autosave. |
| Thumbnail | A 320×180 render of `Camera.main` taken in `LateUpdate`. It uses a URP `StandardRequest` render request, falling back to `Camera.Render`. Autosaves refresh it at most every 10 seconds per slot. |
Each game belongs to one manual slot (`save-1`, `save-2`, `save-3`). `ManualSaveSlot` stores the link as the game-event counter `save.manual-slot`, so it is written into every save and restored with it.

- **New Game.** START opens a New Game page with the three manual slots as cards. A card's START resets event/world state through the save manager's reset transaction and links the run to that slot. A filled slot, or an existing autosave, needs a second START. Nothing is written until the run's first save (`GameAutosave.BeginNewRun(slot)`), so a new game abandoned at once leaves the old files untouched.
- **Load.** The Load page shows an Autosave card and three Manual Slot cards, tagged `Most recent` and `Current`. Loading a manual slot links the run to it and continues in `autosave`; the manual file is never touched by autosave.
- **Save.** The journal's Save button writes the linked slot (`ManualSaveSlot.SaveToLinked`) after refreshing the autosave. An unlinked run takes the first empty slot.

`GameAutosave` writes `autosave` after every 30 seconds of active play (time running and the startup menu closed), and never while `GameSaveManager.AutosaveAllowed` is false during recovery or another save operation. There are no event-triggered autosaves. Main menu and Quit game save first only once the player has played five seconds since the last load (`GameAutosave.HasPlayedSinceLoad`). The startup menu has no Save option; on the main page Up/Down (W/S, d-pad or stick) select START, LOAD, SETTINGS or EXIT, and on the New Game and Load pages they move between cards.

## In-game HUD

`HudSkin` gives the small boxes drawn over the world the quest hint card's look: the startup menu's plum-and-gold panel from `MenuSkin`. Conversations keep their own painted dialogue frames. It provides:

- **Interaction prompts:** a plum pill with a gold key cap and the action, shown above NPCs (Talk), portals (Enter), pickups (Pick up) and building signs (Read). The key cap shows the bound key from `KeyBindings`; building signs now use the Talk binding too, instead of a fixed E.
- **Side panels:** the menu panel frame with a gold title and cream text, in the top-right corner below the Settings button, sized to their text. The quest hint card has the top-left. Stella's First Light and String Them Together progress panels and the ice trial guide use these; Stella's panels used to sit under the hint card.
- **Messages:** a slim plum card near the bottom of the screen, for spell feedback, with the ice trial's feedback just above it.
- **Sign card:** the building sign's popup, with a gold "Campus Building" heading, a divider, the building's name as a title, and an Exit prompt.

One skin is shared by every caller and drawn from OnGUI. The portal "door is locked" popup, the journal, the Settings pop-out and the name prompt already used `MenuSkin` directly.

## In-game options
The Load page shows location, quest, playtime, timestamp and thumbnail summaries. If a primary save is invalid, the menu offers explicit valid previous/interrupted-write recovery candidates. Save validation, migrations, unknown-provider preservation and rollback live in `GameSaveManager`; the menu never silently promotes a recovery candidate. See [save recovery](../../../../../docs/implementation/feature-save-recovery-migrations.md).

`SaveSlotSummary` writes optional `.summary.json` and `.thumb.png` sidecars. They affect presentation only. Missing/stale summaries do not invalidate saves. `GameSaveOverlay` presents save feedback without a competing full-screen slot UI.

## Presentation and ownership

The IMGUI menu uses Nunito, the existing button sheets and runtime-generated gold/cream/lavender art. `MenuArt`, `MenuProps`, `MenuSparks`, `MenuCameraDrift`, `MenuSkin` and `MenuSkinProps` contain reusable presentation. Hosts dispose their own generated textures. Animation uses unscaled time. Reduced menu motion suppresses camera drift, pointer parallax, motes and spark trails; it is separate from reduced gameplay camera motion.

Panels scale to the available window. In-game option sections scroll when needed and honor text-size preferences. Input leases prevent the journal, spell wheel, dialogue and pause menu from controlling gameplay simultaneously; each closes only its own movement lock. Spell and inventory subscriptions rebind when New Game replaces the event runtime.

See [UI integration sources and validation](../../../../../docs/implementation/graphics-ui-integration.md) for branch provenance, scoped imports, verification and remaining graphical checks.

## Integration validation

The current test contract is defined by the [fixture inventory](../../../../../tests/gameplay_suites.json)
and [test procedures](../../../../../tests/README.md). Batch CI explicitly excludes
`RequiresGraphics`; run that category separately in a graphical Editor and inspect
its screenshots. Follow the test procedures for exact verification and local
preference/save isolation.

Confirmed New Game resumes periodic autosaves only after the guarded reset succeeds. Failed resets keep autosaves suspended. The manual save picker offers all three manual slots, including empty destinations, and keyboard/controller confirmation saves to the selected slot.
