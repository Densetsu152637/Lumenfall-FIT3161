# Cutscenes

A cutscene is a JSON file of steps in `Resources/Cutscenes/<id>.json`; the file name must match its `id`. `CutscenePlayer` plays one at a time over the live world. Nothing in a cutscene is saved: it is presentation, plus any GameEvents flags its steps set.

## Opening: arriving out of the menu's light

New Game queues `opening_arrival` (`CutscenePlayer.QueueAfterMenu`). Loading a save never plays it.

1. When START dissolves the menu panel, the opening takes over the dissolve (`TakeOverMenuDissolve`). The player is hidden, and `PlayerArrivalEffect` starts its own 170 sparks across the panel with the menu dissolve's colours, textures, spread, drag and lift, in place of the menu's sparks.
2. While the menu fades and the camera eases back to the player, the sparks curl and stream onto the player's spawn. All of them have landed 2.6 seconds after START. Each landing feeds a gold glow, a spinning spark and a column of light at the spawn, and a point light warms the trees around it. A soft chord swells with small twinkles over it.
3. Once every spark has landed, the glow pulses for half a second. The camera is held a little closer meanwhile.
4. A cream flash, and the player appears behind it with a chime. A ring of sparks bursts outward, the column flares and fades, and the player grows from 90% to full size as the camera eases back out.
5. Motes rise around the player for about two seconds.
6. The player's first thoughts play as an inner monologue (`opening_awakening` in `Resources/GameEvents/opening_story.json`). The speaker is the unnamed player, shown as `???`, and every line is italic. The tone follows the wake-up scene in the game design document: they assume they have died, grumble about having studied physics only to be hit by one of the things they study, wonder where they are, and take the single choice, **Look around**, before deciding to get up and explore. There is no hint card or direction marker. Control returns when the dialogue closes.

The light takes about 4.5 seconds from START, and the dialogue follows. It is a play-once cutscene (`cutscene.played.opening_arrival`), and it is unskippable because it is so short. If the menu cannot hand over (no player was found), the `arrive` step starts the sparks across the middle of the screen instead. If nothing reveals the player, the effect reveals them itself after 15 seconds.

The earlier falling-star opening is kept as `falling_star`, which nothing plays now. A star dives onto the player, then captions play over black. It can be played with `CutscenePlayer.Play("falling_star")` or a `playcutscene` action.

## Playing a cutscene

- **From code:** `CutscenePlayer.Play(id)` starts it now, and `CutscenePlayer.QueueAfterMenu(id)` starts it after the startup menu closes. `Play` returns false if another cutscene is playing, the file is missing or invalid, or a play-once cutscene has already played in this game.
- **From GameEvents data:** an action `{ "type": "playcutscene", "key": "<id>" }`, for example in a dialogue node's `onEnterActions` or an event's actions. `CutscenePlayer` registers this custom action on each live runtime. `GameEventCatalog` logs its usual warning for a custom action type it does not recognise.
- **Skipping:** Escape twice. The first press shows "Press Esc again to skip" for 2.5 seconds. A faint "Esc twice to skip" hint sits in the bottom-right corner throughout. Set `"unskippable": true` to turn skipping off. Skipping still sets the flags from steps that had not run yet, so the game ends up in the same state as watching.

While a cutscene plays (`CutscenePlayer.IsPlaying`), the player's movement is locked every frame, and these all wait for it: spell input, NPC talk, portals, pickups, building signs, the journal, the Escape pause pop-out and autosave. The quest hint card and Settings button are hidden. `CutscenePlayer.BlocksInput` also stays true for the frame a cutscene ends, so the Escape press that skips it does not also open the pause pop-out.

## Step reference

Each step has a `type`. Timed steps hold the sequence for their `seconds` unless `"background": true`, which lets the next steps start at once while the effect carries on.

| Type | Fields | Effect |
| --- | --- | --- |
| `wait` | `seconds` | Pause. |
| `fade` | `alpha` (0–1), `color` (optional `#RRGGBB`), `seconds` | Ease the full-screen overlay to that opacity and colour, with smoothstep easing. `seconds` 0 is instant. The overlay starts clear and black. |
| `caption` | `text`, `seconds` | Centred italic text over everything, fading in and out over 0.6 seconds (or a quarter of its time if shorter). |
| `star` | `seconds` | The falling star (`FallingStarEffect`) lands on the player after `seconds`. |
| `arrive` | | Hide the player and gather light onto them (`PlayerArrivalEffect`), continuing the menu's dissolve if it was handed over. Waits until the light has gathered. |
| `reveal` | | Show the player with a burst of light. Put it just after a flash so the swap is covered. Skipping or ending a cutscene always leaves the player visible at full size. |
| `shake` | `amplitude`, `seconds` | Camera shake in world units, dying away linearly. Reduce motion turns it off. |
| `camera` | `x`, `y`, `z`, `zoom` | Hold the camera on the player plus that offset, at `zoom` (0.3–1 of the normal distance; 0 or missing is 1). It uses the conversation framing in `CameraFollow2_5D`, and an open dialogue frames itself until it closes. |
| `cameraRelease` | | Return the camera to normal following. |
| `dialogue` | `key` | Open that dialogue and wait until it ends. |
| `name` | | Ask the player for their name (`PlayerNamePrompt`, see [the Player README](../Player/README.md)) and wait for the answer. Put it between two dialogue steps. |
| `walk` | `x`, `y`, `value`, `key` | The game walks the player to that cell on the current map (`CutsceneWalk`) and waits until they arrive. `value` true makes them hurry. With `key` set to an NPC id, `x`, `y` is instead an offset from wherever that NPC stands in the scene, for example `0`, `-2` for two cells in front of them, so it does not depend on their catalog cell. The player's own input is off and they take ordinary grid steps along a `GridPathfinder` path, so they go around obstacles. A cell that cannot be stood on is replaced by the nearest one that can. After three seconds blocked, or with no path, the player is placed at the destination. |
| `enter` | `x`, `y` | Take the player through the portal at that cell, placing them on it first if they are not already. Usually wrapped in a fade. |
| `flag` | `key`, `value` | Set a GameEvents flag. |

`CutsceneDefinition.Parse` rejects a cutscene with no id, no steps, an unknown step type, negative seconds, a fade alpha outside 0–1, an unreadable colour, a caption without text, a dialogue or flag without a key, or a star without a duration. `Load` logs why and the cutscene does not play.

## Runtime pieces

- `CutsceneDefinition` / `CutsceneStep` are the JSON shape (read with `JsonUtility`) and its validation.
- `CutsceneSequence` steps through a definition on `Advance(deltaTime)`. It owns the overlay colour and alpha, the caption and the shake strength, and calls an `ICutsceneHost` for world effects. Time left over when a step ends carries into the next one, so a fade that follows a wait stays in step with it. It has no scene dependencies, so EditMode tests drive it directly.
- `CutscenePlayer` is a bootstrapped, `DontDestroyOnLoad` host. It runs on unscaled time and draws the overlay, captions and skip hint with IMGUI at `GUI.depth` -1000, in front of the other overlays.
- `PlayerArrivalEffect` hides the player through `PlayerVisibility` (in `GridMovement`), which it shares with the startup menu. It takes the hide over before the menu lets go, so the player never flickers into view, and shows them when revealing or when destroyed. Its sparks are drawn with IMGUI at depth -999, so they sit in front of the menu but behind the flash. Each spark's shown position blends from its free drift to the player's screen position, with a sideways curl, and is guaranteed to land by its deadline. The glow, light column, light and motes are world-space. It runs on unscaled time, so it works while the menu still has `Time.timeScale` at 0.
- `FallingStarEffect` builds the star at runtime: camera-facing quads, a `TrailRenderer`, a point light, shed motes, and generated audio clips. It starts where a top-left view ray is six units above the player, so it appears high over the canopy rather than hidden among the trees.
- `CutsceneArt` holds the shared runtime art: `Sprites/Default` glow, sparkle and light-column materials with generated textures, the startup menu's screen-space spark textures (`MenuArt.Glow` and `GameSaveOverlay.CreateSparkleTexture`), collider-free quads, property-block tinting and clip generation. None of the effects have colliders or touch world state.
- `CameraFollow2_5D.SetShake(offset)` adds an offset after the follow position is smoothed and removes it before the next frame's smoothing, so a shake never drifts the camera.

## Verification

- `CutsceneSequenceTests` (EditMode, in `GameEvents/Tests/EditMode`) check that every authored cutscene parses, has an id matching its file name and ends with the screen clear. They also check that the opening waits for the light to gather and reveals the player under the flash; that `falling_star` still plays its star before its captions; and they cover fade easing and timing, leftover time between steps, background steps, captions, dialogue and arrival waits, skipping, unskippable cutscenes and parse errors. They compile with `scripts/compile_editmode_tests.ps1` but have not yet been run in Unity.
- Manual validation (not yet executed): start a New Game and check that the sparks visibly leave the panel and land on the player as the camera returns; that the player is never seen before the flash; that the glow, column and rising motes read well against the forest at several resolutions; that the player returns to exactly their normal size; and that the first autosave after the opening has a normal thumbnail.
