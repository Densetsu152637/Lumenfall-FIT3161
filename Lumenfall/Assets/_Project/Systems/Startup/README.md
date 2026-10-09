# Startup menu and shared settings

`StartupMenu` bootstraps after scene load and pauses gameplay until Start or a successful Load completes its closing transition. It owns an `OverlayInputGate` lease, disables active gameplay input and dialogue overlays, and restores its previous time scale and input state when destroyed. Generated textures and event subscriptions are released with their owner.

## Settings

The startup Settings page, in-game pause/settings panel (`QuestHintOverlay`) and journal Settings page (`InventoryEncyclopediaUI`) all draw the same `OptionsPanel`. Each opening starts on **Main**. Changing tabs cancels any pending rebind and resets scroll/focus.

| Tab | Options |
| --- | --- |
| Main | Quest hints; master, music, sound effects and ambience volume; text size; dialogue reveal speed; instant reveal; reduced camera motion; high contrast; hold/toggle sprint; reduced menu motion; Controls/key rebinding. |
| Graphics | Fullscreen; VSync; antialiasing (Off/FXAA/SMAA); ambient occlusion; shading quality (Low/Medium/High); Use Recommended. |

Use the pointer, or Up/Down to focus a tab or setting, Left/Right to select a tab or adjust the focused value, and Enter/Space to activate it. A controller uses the d-pad/left stick and A/B. Fullscreen is available in a player build. Unsupported URP/AO controls display Unavailable and preserve their saved values.

The journal opens with the configured Journal key (Tab by default) or the controller Select button; shoulder buttons switch its outer Inventory/Encyclopedia/Settings pages. Escape closes it. Escape during gameplay, F10, controller Start or the Settings button opens the pause panel. The pause panel provides Save now, Main menu, Quit game and Close. A failed save remains visible and prevents Quit from closing the game.

Controls groups movement, spell and interface actions. Selecting an action captures its next key; a conflicting assignment requires an explicit Swap or Cancel. Escape cancels the capture. Runtime movement, sprint, talk, spell selection/casting and journal input use these bindings.

## First-install graphics recommendations

`GraphicsPreferences.LoadOrCreate` fills missing graphics keys from `GraphicsRecommendations.ForCurrentDevice`. Existing choices survive launch and hardware changes. Use Recommended explicitly reapplies the current device's preset without changing Main settings.

| PC tier | Minimum reported specs | VSync | AA | AO | Shading |
| --- | --- | --- | --- | --- | --- |
| High | 15,000 MB RAM, 8 logical processors, 4,000 MB VRAM, shader level 45, graphics device | On | SMAA | On | High |
| Medium | 7,500 MB RAM, 4 logical processors, 2,000 MB VRAM, shader level 45, graphics device | On | FXAA | Off | Medium |
| Low | Below any Medium threshold, or unknown/headless hardware | On | Off | Off | Low |

These are conservative hardware heuristics, not measured frame-rate guarantees. Shared/integrated GPU memory can be reported imprecisely; manual overrides remain available. Mobile retains Off VSync/AA/AO and High shading to preserve the existing mobile configuration.

Preferences use `lumenfall.graphics.vsync`, `.antialiasing`, `.ambient-occlusion`, and `.shading`. `GraphicsOptions` applies them before scene load, clones the active URP asset for runtime changes, configures current and later game cameras, and restores authored resources on destruction. VSync maps to `QualitySettings.vSyncCount`; AA uses URP camera FXAA/SMAA; AO toggles the renderer's existing SSAO feature. Shading adjusts shadow distance/resolution/cascades and LOD bias. Disabling AA restores each camera's original post-processing state. URP runtime variant stripping is disabled so player builds retain selectable AA variants.

Main preferences retain their own stores: `AudioMixSettings` owns bus levels, `AccessibilitySettings` owns accessibility preferences, `KeyBindings` owns keyboard bindings and `GameSettingsPanel` owns reduced menu motion. The old display VSync adapter delegates to `GraphicsOptions`, so no second VSync authority competes with it.

## Main menu and saves

The first launch presents the authored starting world behind the menu without creating a placeholder save. Later launches load `autosave` as a backdrop. Legacy manual saves can be promoted to autosave without overwriting their originals. Backdrop loading does not mark a slot as newly played.

Start resets event/world state through the save manager's reset transaction. Replacing an existing autosave requires a second Start press. Gameplay autosaves use the dedicated `autosave` slot every 30 seconds and on eligible events. Three manual snapshots (`save-1`, `save-2`, `save-3`) are available from the Save page; overwrites require confirmation. Loading a manual snapshot resumes it into autosave, leaving the manual file unchanged.

The Load page shows location, quest, playtime, timestamp and thumbnail summaries. If a primary save is invalid, the menu offers explicit valid previous/interrupted-write recovery candidates. Save validation, migrations, unknown-provider preservation and rollback live in `GameSaveManager`; the menu never silently promotes a recovery candidate. See [save recovery](../../../../../docs/implementation/feature-save-recovery-migrations.md).

`SaveSlotSummary` writes optional `.summary.json` and `.thumb.png` sidecars. They affect presentation only. Missing/stale summaries do not invalidate saves. `GameSaveOverlay` presents save feedback without a competing full-screen slot UI.

## Presentation and ownership

The IMGUI menu uses Nunito, the existing button sheets and runtime-generated gold/cream/lavender art. `MenuArt`, `MenuProps`, `MenuSparks`, `MenuCameraDrift`, `MenuSkin` and `MenuSkinProps` contain reusable presentation. Hosts dispose their own generated textures. Animation uses unscaled time. Reduced menu motion suppresses camera drift, pointer parallax, motes and spark trails; it is separate from reduced gameplay camera motion.

Panels scale to the available window. Settings use scrollable rows and honor text-size preferences. Input leases prevent the journal, spell wheel, dialogue and pause menu from controlling gameplay simultaneously; each closes only its own movement lock. Spell and inventory subscriptions rebind when New Game replaces the event runtime.

See [UI integration sources and validation](../../../../../docs/implementation/graphics-ui-integration.md) for branch provenance, scoped imports, verification and remaining graphical checks.
