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

Store `GameEventManager.Instance.Runtime.ExportStateJson()` with the rest of the save file and pass
it back to `ImportStateJson(json)` when loading. This preserves flags, counters, quest states, and
non-repeatable event history. Active conversations are intentionally not saved.

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
