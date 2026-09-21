# Game event system

`GameEventManager` is created automatically and merges every JSON `TextAsset` under
`Assets/**/Resources/GameEvents`. IDs are global across all loaded files. The included
`sample_events.json` demonstrates an NPC dialogue tree, quest lifecycle events, counters,
conditions and event chaining.

## Triggering events

From gameplay code:

```csharp
using Lumenfall.GameEvents;

GameEventTriggerResult result =
    GameEventManager.Instance.TriggerEvent("talk_to_elara");

if (!result.Succeeded)
{
    Debug.Log(result.Message);
}
```

For a scene volume, add `GameEventTrigger`, enter an event ID and choose a trigger moment. For an
NPC interaction, leave it in `Manual` mode and call its public `Trigger()` method when the player
interacts.

Inventory or objective code can report progress through events too:

```csharp
GameEventManager.Instance.TriggerEvent("collect_moonpetal");
GameEventManager.Instance.TriggerEvent("turn_in_moonpetals");
```

## Connecting dialogue UI

The runtime deliberately does not choose a UI framework. Subscribe once, render the current node,
then call `Continue()` or `SelectChoice(id)` from buttons:

```csharp
GameEventRuntime events = GameEventManager.Instance.Runtime;
events.DialogueStarted += session => Show(session);
events.DialogueEnded += session => HideDialogue();

void Show(DialogueSession session)
{
    DialogueNodeDefinition node = session.CurrentNode;
    speakerLabel.text = node.speaker;
    bodyLabel.text = node.text;

    foreach (DialogueChoiceDefinition choice in session.AvailableChoices)
    {
        AddChoiceButton(choice.text, () =>
        {
            session.SelectChoice(choice.id);
            if (session.IsActive) Show(session);
        });
    }
}
```

For linear nodes with no choices, the continue button calls `session.Continue()`. A blank
`nextNodeId` ends the conversation. Choice conditions are evaluated immediately before the node is
shown, and choice actions execute before moving to the next node.

## JSON schema

Each file has `schemaVersion: 1` and any combination of these arrays:

- `events`: `id`, `description`, `repeatable`, `conditionMode` (`all`/`any`), `conditions`,
  `actions`, `metadata`
- `quests`: `id`, `title`, `description`, optional `onStartedEventId` and
  `onCompletedEventId`, `metadata`
- `dialogues`: `id`, `startNodeId`, `nodes`, `metadata`
- dialogue nodes: `id`, `speaker`, `text`, optional `nextNodeId`, `onEnterActions`, `choices`
- choices: `id`, `text`, optional `nextNodeId`, `conditionMode`, `conditions`, `actions`

All IDs and state keys are case-sensitive. Type names and comparison names are case-insensitive.
`metadata` is an optional array of `{ "key": "...", "value": "..." }` entries available to UI
or gameplay systems; the core runtime does not interpret it. Read a value with
`definition.metadata.GetValue("key")`.

Built-in conditions:

| Type | Fields | Meaning |
| --- | --- | --- |
| `Flag` | `key`, `boolValue` | Flag equals the requested value; missing flags are false |
| `Counter` | `key`, `intValue`, `comparison` | Numeric comparison; missing counters are zero |
| `QuestStatus` | `key`, `value` | Status is `NotStarted`, `Active`, or `Completed` |
| `EventTriggered` | `key`, `boolValue` | Whether an event has triggered at least once |

Counter comparisons are `equals`, `notEquals`, `greaterThan`, `greaterOrEqual`, `lessThan`, and
`lessOrEqual`. Every condition also supports `negate: true`.

Built-in actions:

| Type | Fields | Meaning |
| --- | --- | --- |
| `SetFlag` | `key`, `boolValue` | Set global boolean state |
| `SetCounter` | `key`, `intValue` | Replace global numeric state |
| `AddCounter` | `key`, `intValue` | Add to global numeric state |
| `StartQuest` | `key` | Move a quest from `NotStarted` to `Active` |
| `CompleteQuest` | `key` | Move an `Active` quest to `Completed` |
| `StartDialogue` | `key` | Open a dialogue tree |
| `TriggerEvent` | `key` | Trigger another global event; cycles are blocked |

Quest state changes raise `QuestStatusChanged` and automatically trigger their configured lifecycle
event. The runtime also exposes events for flags, counters, global events and dialogue.

## Saving and extensions

`GameSaveManager` provides versioned, file-backed save slots. Slots are JSON files in
`Application.persistentDataPath`; writes use a temporary file and replace the previous slot only
after the new file has been written. Invalid slot names, missing files, unsupported schema versions,
and provider failures return `false` and raise `SaveFailed`. Loads snapshot the current event and
provider state and roll it back when a provider rejects its payload, avoiding an intentionally
partial restore. `SaveCompleted` and `LoadCompleted` can be used to update a save-menu UI.

```csharp
GameSaveManager saves = GameSaveManager.Instance;
saves.Save("slot-1");
// Later, after systems have registered their providers:
saves.Load("slot-1");
```

The sample game creates `GameSaveOverlay` automatically. Its Save and Load buttons operate on
`save-1`; a larger menu can replace the overlay and call the same manager methods. The generated
`DataDrivenWorldLoader` registers a provider that restores the active map and player grid cell.

The current file schema is version `1`. It contains the event runtime JSON plus a sorted array of
keyed provider payloads. Providers must be registered before `Load`; known payloads are restored in
file order, while unknown keys are skipped for forward compatibility. Event state preserves flags,
counters, quest states, and non-repeatable event history. Active conversations are intentionally not
saved. A future schema change should add a migration before increasing the version, rather than
silently accepting incompatible data.

Systems that own mutable world state register an `ISaveStateProvider` with a stable key. Provider
payloads are opaque JSON strings, so a quest, inventory, or world-object system can evolve its own
data without changing the save manager:

```csharp
public sealed class WorldState : ISaveStateProvider
{
    public string SaveKey => "world";

    public string CaptureStateJson() => JsonUtility.ToJson(new State
    {
        openedChestIds = openedChestIds.ToArray()
    });

    public bool RestoreStateJson(string json)
    {
        State state = JsonUtility.FromJson<State>(json);
        if (state == null) return false;
        openedChestIds = new HashSet<string>(state.openedChestIds ?? Array.Empty<string>());
        return true;
    }

    [Serializable]
    private sealed class State
    {
        public string[] openedChestIds;
    }
}

// Register once during bootstrap, before loading a slot.
GameSaveManager.Instance.RegisterProvider(worldState);
```

Use stable IDs for provider keys and world objects. A provider missing from a later build is
skipped so saves remain loadable as systems are added; a registered provider that rejects its
payload fails the load and reports the key. For quest progress, prefer the built-in event runtime
(`StartQuest`, `CompleteQuest`, and `QuestStatus`) unless the quest also owns custom world data such
as spawned objects or a puzzle layout, which belongs in its own provider.

Project-specific condition/action types can be registered without editing the runtime:

```csharp
GameEventRuntime runtime = GameEventManager.Instance.Runtime;

runtime.RegisterConditionHandler("HasItem", (condition, state) =>
    inventory.Contains(condition.key, condition.intValue));

runtime.RegisterActionHandler("GiveItem", (action, state) =>
    inventory.Add(action.key, action.intValue));
```

Unknown custom types produce catalogue warnings until their handlers are registered. Invalid IDs,
duplicate definitions, broken dialogue links and unsupported schema versions are reported in the
Unity Console during startup.
