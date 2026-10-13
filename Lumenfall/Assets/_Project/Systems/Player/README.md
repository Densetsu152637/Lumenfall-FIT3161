# Player

The player character's identity. For now that is only their name.

## Name

`PlayerProfile` holds the name. A new game starts unnamed: New Game calls `PlayerProfile.Forget()`. Dialogue shows the player as `???` until the story asks for a name. Leaving the name blank gives the default, **Sol**. Typed names are trimmed, lose line breaks and the `<`, `>`, `{` and `}` characters (so a name cannot open rich text or a token), and are cut to 16 characters.

The name is saved with the game under the provider key `player-profile`. Loading a save made before the profile existed leaves the player unnamed.

## Dialogue token

Write `{player}` in GameEvents dialogue to mean the player character:

- **As a node's `speaker`:** the ribbon shows the name (or `???`) on the neutral parchment frame. There is never a portrait, even when an NPC stands nearby.
- **In a node's `text` or a choice's `text`:** it is replaced with the name, for example `"Alright {player}, hurry up and get in."`.

`DialogueOverlay` makes the substitution when a node is shown, so a name chosen mid-conversation appears from the next node on.

## Asking for the name

`PlayerNamePrompt` is a small panel in the startup menu's style: a "What's your name?" title, a text field with `Sol` as its placeholder, and a Confirm button. Enter also confirms. Prof. Kepler asks for it at the forest gate (see [the Story README](../Story/README.md)). The prompt is a cutscene `name` step between two dialogue steps (see [the Cutscenes README](../Cutscenes/README.md)):

```json
{ "type": "dialogue", "key": "kepler_late" },
{ "type": "name" },
{ "type": "dialogue", "key": "kepler_get_in" }
```

The step waits for the answer while the cutscene keeps gameplay input blocked. Skipping a cutscene during the prompt keeps what was typed, or gives the default name.

## Verification

`PlayerProfileTests` (EditMode) cover the unnamed `???` display, the default name, tidying typed names, the speaker token and the awakening dialogue's content. They compile but have not yet been run in Unity. The prompt's look and typing have not been checked in Play Mode.
