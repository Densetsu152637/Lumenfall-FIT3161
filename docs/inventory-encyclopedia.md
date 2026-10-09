# Inventory and character encyclopedia

`InventoryEncyclopediaUI` is created automatically after a scene loads. Press **Tab** to open or
close the journal. The Inventory page lists owned key items and currently unlocked spells,
including any available spell icon. The Character encyclopedia page lists each supported authored
NPC record after the player starts one of their dialogues; duplicate Cinna Cirra placements share
the same authored character description.

Discovery uses `GameEventRuntime` flags (`character.discovered.<npcId>` and
`inventory.keyitem.<id>`), so it is included in the existing event-runtime save snapshot. Quest and
dialogue definitions can award an item with `keyItemReward` metadata. Katrina's `Something
Borrowed` quest uses this authored path to grant `katrina-keepsake` only after quest completion or
terminal dialogue. `GrantKeyItem` remains available for future authored item systems. Dialogue
start automatically records a supported `npcId` from its metadata.

While the journal is open it sets `InventoryUiState.IsOpen` and locks the player mover. Spell input
must also honor that shared state so movement and casting remain paused until Tab closes the page.
Opening is ignored while a dialogue is active, and starting a dialogue closes the journal.

The journal now shares the main menu theme and has Inventory, Encyclopedia and Settings pages.
Settings contains **Main** (hints, audio, accessibility, menu motion and Controls) and **Graphics**
(fullscreen, VSync, AA, AO, shading and recommended preset). All three settings entry points use
`OptionsPanel`; opening settings starts on Main. Controller Select opens the journal and shoulder
buttons change its outer pages. Keyboard arrows navigate lists/settings, with Enter to activate;
Tab or Escape closes the journal. Rebinding captures own their keys until confirmed or cancelled.
The journal owns an `OverlayInputGate` lease and a mover lock and releases only those on close.
Runtime replacement after New Game rebinds discovery/reward subscriptions. The spell list records
learned spells even when a designer allow-list temporarily prevents casting them.
