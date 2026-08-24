using System;

namespace Lumenfall.GameEvents
{
    /// <summary>
    /// Root object for one JSON game-event file. Multiple files are merged into one catalogue.
    /// </summary>
    [Serializable]
    public sealed class GameEventDatabaseDefinition
    {
        public int schemaVersion = 1;
        public GameEventDefinition[] events = Array.Empty<GameEventDefinition>();
        public QuestDefinition[] quests = Array.Empty<QuestDefinition>();
        public DialogueDefinition[] dialogues = Array.Empty<DialogueDefinition>();
    }

    [Serializable]
    public sealed class GameEventDefinition
    {
        public string id;
        public string description;
        public bool repeatable;
        public string conditionMode = "all";
        public GameConditionDefinition[] conditions = Array.Empty<GameConditionDefinition>();
        public GameActionDefinition[] actions = Array.Empty<GameActionDefinition>();
        public GameMetadataEntry[] metadata = Array.Empty<GameMetadataEntry>();
    }

    [Serializable]
    public sealed class QuestDefinition
    {
        public string id;
        public string title;
        public string description;
        public string onStartedEventId;
        public string onCompletedEventId;
        public GameMetadataEntry[] metadata = Array.Empty<GameMetadataEntry>();
    }

    [Serializable]
    public sealed class DialogueDefinition
    {
        public string id;
        public string startNodeId;
        public DialogueNodeDefinition[] nodes = Array.Empty<DialogueNodeDefinition>();
        public GameMetadataEntry[] metadata = Array.Empty<GameMetadataEntry>();
    }

    [Serializable]
    public sealed class DialogueNodeDefinition
    {
        public string id;
        public string speaker;
        public string text;
        public string nextNodeId;
        public GameActionDefinition[] onEnterActions = Array.Empty<GameActionDefinition>();
        public DialogueChoiceDefinition[] choices = Array.Empty<DialogueChoiceDefinition>();
        public GameMetadataEntry[] metadata = Array.Empty<GameMetadataEntry>();
    }

    [Serializable]
    public sealed class DialogueChoiceDefinition
    {
        public string id;
        public string text;
        public string nextNodeId;
        public string conditionMode = "all";
        public GameConditionDefinition[] conditions = Array.Empty<GameConditionDefinition>();
        public GameActionDefinition[] actions = Array.Empty<GameActionDefinition>();
        public GameMetadataEntry[] metadata = Array.Empty<GameMetadataEntry>();
    }

    /// <summary>
    /// A data-driven condition. Built-in types are Flag, Counter, QuestStatus and EventTriggered.
    /// Custom types can reuse these generic value fields.
    /// </summary>
    [Serializable]
    public sealed class GameConditionDefinition
    {
        public string type;
        public string key;
        public string value;
        public int intValue;
        public bool boolValue;
        public string comparison = "equals";
        public bool negate;
        public GameMetadataEntry[] metadata = Array.Empty<GameMetadataEntry>();
    }

    /// <summary>
    /// A data-driven action. Built-in types are SetFlag, SetCounter, AddCounter, StartQuest,
    /// CompleteQuest, StartDialogue and TriggerEvent. Custom types can reuse these value fields.
    /// </summary>
    [Serializable]
    public sealed class GameActionDefinition
    {
        public string type;
        public string key;
        public string value;
        public int intValue;
        public bool boolValue;
        public GameMetadataEntry[] metadata = Array.Empty<GameMetadataEntry>();
    }

    [Serializable]
    public sealed class GameMetadataEntry
    {
        public string key;
        public string value;
    }

    public enum QuestStatus
    {
        NotStarted,
        Active,
        Completed
    }

    public enum GameEventTriggerStatus
    {
        Triggered,
        NotFound,
        AlreadyTriggered,
        ConditionsNotMet,
        ReentrantTriggerBlocked
    }

    public readonly struct GameEventTriggerResult
    {
        public GameEventTriggerResult(
            GameEventTriggerStatus status,
            GameEventDefinition definition,
            string message)
        {
            Status = status;
            Definition = definition;
            Message = message;
        }

        public GameEventTriggerStatus Status { get; }
        public GameEventDefinition Definition { get; }
        public string Message { get; }
        public bool Succeeded => Status == GameEventTriggerStatus.Triggered;
    }
}
