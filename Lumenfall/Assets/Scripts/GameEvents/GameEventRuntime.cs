using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lumenfall.GameEvents
{
    public delegate bool CustomGameConditionHandler(
        GameConditionDefinition condition,
        GameEventRuntime runtime);

    public delegate void CustomGameActionHandler(
        GameActionDefinition action,
        GameEventRuntime runtime);

    /// <summary>
    /// Owns all mutable global event, quest and dialogue state. It has no UI dependency and can be
    /// constructed directly in tests or accessed through GameEventManager in a scene.
    /// </summary>
    public sealed class GameEventRuntime
    {
        private const int MaximumNestedEventDepth = 32;

        private readonly Dictionary<string, bool> flags =
            new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> counters =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, QuestStatus> questStatuses =
            new Dictionary<string, QuestStatus>(StringComparer.Ordinal);
        private readonly HashSet<string> triggeredEvents = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> executingEvents = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, CustomGameConditionHandler> customConditionHandlers =
            new Dictionary<string, CustomGameConditionHandler>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CustomGameActionHandler> customActionHandlers =
            new Dictionary<string, CustomGameActionHandler>(StringComparer.OrdinalIgnoreCase);

        public GameEventRuntime(GameEventCatalog catalog)
        {
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public GameEventCatalog Catalog { get; }
        public DialogueSession ActiveDialogue { get; private set; }

        public event Action<GameEventDefinition> EventTriggered;
        public event Action<string, bool> FlagChanged;
        public event Action<string, int> CounterChanged;
        public event Action<QuestDefinition, QuestStatus, QuestStatus> QuestStatusChanged;
        public event Action<DialogueSession> DialogueStarted;
        public event Action<DialogueSession> DialogueEnded;
        public event Action StateRestored;
        public event Action<string> DiagnosticRaised;

        public GameEventTriggerResult TriggerEvent(string eventId)
        {
            if (!Catalog.TryGetEvent(eventId, out GameEventDefinition definition))
            {
                return Result(GameEventTriggerStatus.NotFound, null, $"Game event '{eventId}' was not found.");
            }

            if (!definition.repeatable && triggeredEvents.Contains(definition.id))
            {
                return Result(
                    GameEventTriggerStatus.AlreadyTriggered,
                    definition,
                    $"Game event '{definition.id}' has already been triggered.");
            }

            if (executingEvents.Contains(definition.id) || executingEvents.Count >= MaximumNestedEventDepth)
            {
                return Result(
                    GameEventTriggerStatus.ReentrantTriggerBlocked,
                    definition,
                    $"A cyclic or excessively deep trigger involving '{definition.id}' was blocked.");
            }

            if (!EvaluateConditions(definition.conditions, definition.conditionMode))
            {
                return Result(
                    GameEventTriggerStatus.ConditionsNotMet,
                    definition,
                    $"Conditions for game event '{definition.id}' were not met.");
            }

            executingEvents.Add(definition.id);
            triggeredEvents.Add(definition.id);
            try
            {
                ExecuteActions(definition.actions);
                EventTriggered?.Invoke(definition);
            }
            finally
            {
                executingEvents.Remove(definition.id);
            }

            return Result(
                GameEventTriggerStatus.Triggered,
                definition,
                $"Game event '{definition.id}' triggered.");
        }

        public bool HasEventTriggered(string eventId)
        {
            return !string.IsNullOrWhiteSpace(eventId) && triggeredEvents.Contains(eventId);
        }

        public bool GetFlag(string key)
        {
            return !string.IsNullOrWhiteSpace(key) && flags.TryGetValue(key, out bool value) && value;
        }

        public void SetFlag(string key, bool value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                RaiseDiagnostic("Cannot set a flag with an empty key.");
                return;
            }

            bool changed = !flags.TryGetValue(key, out bool previous) || previous != value;
            flags[key] = value;
            if (changed)
            {
                FlagChanged?.Invoke(key, value);
            }
        }

        public int GetCounter(string key)
        {
            return !string.IsNullOrWhiteSpace(key) && counters.TryGetValue(key, out int value) ? value : 0;
        }

        public void SetCounter(string key, int value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                RaiseDiagnostic("Cannot set a counter with an empty key.");
                return;
            }

            bool changed = !counters.TryGetValue(key, out int previous) || previous != value;
            counters[key] = value;
            if (changed)
            {
                CounterChanged?.Invoke(key, value);
            }
        }

        public int AddCounter(string key, int amount)
        {
            int value = GetCounter(key) + amount;
            SetCounter(key, value);
            return value;
        }

        public QuestStatus GetQuestStatus(string questId)
        {
            return !string.IsNullOrWhiteSpace(questId) &&
                   questStatuses.TryGetValue(questId, out QuestStatus status)
                ? status
                : QuestStatus.NotStarted;
        }

        public bool StartQuest(string questId)
        {
            if (!Catalog.TryGetQuest(questId, out QuestDefinition quest))
            {
                RaiseDiagnostic($"Cannot start unknown quest '{questId}'.");
                return false;
            }

            QuestStatus previous = GetQuestStatus(questId);
            if (previous != QuestStatus.NotStarted)
            {
                return false;
            }

            questStatuses[questId] = QuestStatus.Active;
            QuestStatusChanged?.Invoke(quest, previous, QuestStatus.Active);
            TriggerQuestLifecycleEvent(quest.onStartedEventId, quest.id, "started");
            return true;
        }

        public bool CompleteQuest(string questId)
        {
            if (!Catalog.TryGetQuest(questId, out QuestDefinition quest))
            {
                RaiseDiagnostic($"Cannot complete unknown quest '{questId}'.");
                return false;
            }

            QuestStatus previous = GetQuestStatus(questId);
            if (previous != QuestStatus.Active)
            {
                return false;
            }

            questStatuses[questId] = QuestStatus.Completed;
            QuestStatusChanged?.Invoke(quest, previous, QuestStatus.Completed);
            TriggerQuestLifecycleEvent(quest.onCompletedEventId, quest.id, "completed");
            return true;
        }

        public DialogueSession StartDialogue(string dialogueId)
        {
            if (!Catalog.TryGetDialogue(dialogueId, out DialogueDefinition definition))
            {
                RaiseDiagnostic($"Cannot start unknown dialogue '{dialogueId}'.");
                return null;
            }

            if (ActiveDialogue != null)
            {
                ActiveDialogue.End();
            }

            DialogueSession session = new DialogueSession(this, definition);
            ActiveDialogue = session;
            if (!session.EnterStartingNode())
            {
                ActiveDialogue = null;
                RaiseDiagnostic($"Dialogue '{dialogueId}' has no valid starting node.");
                return null;
            }

            // An on-enter action is allowed to replace this dialogue with another one.
            if (!session.IsActive || ActiveDialogue != session)
            {
                return ActiveDialogue;
            }

            DialogueStarted?.Invoke(session);
            return session;
        }

        public void RegisterConditionHandler(string type, CustomGameConditionHandler handler)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                throw new ArgumentException("A custom condition type is required.", nameof(type));
            }

            customConditionHandlers[type] = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public void RegisterActionHandler(string type, CustomGameActionHandler handler)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                throw new ArgumentException("A custom action type is required.", nameof(type));
            }

            customActionHandlers[type] = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public bool EvaluateConditions(GameConditionDefinition[] conditions, string conditionMode = "all")
        {
            if (conditions == null || conditions.Length == 0)
            {
                return true;
            }

            bool matchAny = string.Equals(conditionMode, "any", StringComparison.OrdinalIgnoreCase);
            foreach (GameConditionDefinition condition in conditions)
            {
                bool result = EvaluateCondition(condition);
                if (matchAny && result)
                {
                    return true;
                }

                if (!matchAny && !result)
                {
                    return false;
                }
            }

            return !matchAny;
        }

        public string ExportStateJson(bool prettyPrint = true)
        {
            GameEventRuntimeState state = new GameEventRuntimeState
            {
                flags = ToFlagEntries(flags),
                counters = ToCounterEntries(counters),
                quests = ToQuestEntries(questStatuses),
                triggeredEventIds = ToSortedArray(triggeredEvents)
            };
            return JsonUtility.ToJson(state, prettyPrint);
        }

        public bool ImportStateJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                RaiseDiagnostic("Cannot import empty game-event state JSON.");
                return false;
            }

            GameEventRuntimeState state;
            try
            {
                state = JsonUtility.FromJson<GameEventRuntimeState>(json);
            }
            catch (Exception exception)
            {
                RaiseDiagnostic($"Could not import game-event state: {exception.Message}");
                return false;
            }

            if (state == null)
            {
                RaiseDiagnostic("Could not import game-event state: JSON did not contain an object.");
                return false;
            }

            if (ActiveDialogue != null)
            {
                ActiveDialogue.End();
            }

            flags.Clear();
            counters.Clear();
            questStatuses.Clear();
            triggeredEvents.Clear();

            foreach (FlagStateEntry entry in state.flags ?? Array.Empty<FlagStateEntry>())
            {
                if (entry != null && !string.IsNullOrWhiteSpace(entry.key))
                {
                    flags[entry.key] = entry.value;
                }
            }

            foreach (CounterStateEntry entry in state.counters ?? Array.Empty<CounterStateEntry>())
            {
                if (entry != null && !string.IsNullOrWhiteSpace(entry.key))
                {
                    counters[entry.key] = entry.value;
                }
            }

            foreach (QuestStateEntry entry in state.quests ?? Array.Empty<QuestStateEntry>())
            {
                if (entry == null || !Catalog.Quests.ContainsKey(entry.questId) ||
                    !Enum.TryParse(entry.status, true, out QuestStatus status))
                {
                    continue;
                }

                questStatuses[entry.questId] = status;
            }

            foreach (string eventId in state.triggeredEventIds ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(eventId) && Catalog.Events.ContainsKey(eventId))
                {
                    triggeredEvents.Add(eventId);
                }
            }

            StateRestored?.Invoke();
            return true;
        }

        public void ResetState()
        {
            if (ActiveDialogue != null)
            {
                ActiveDialogue.End();
            }

            flags.Clear();
            counters.Clear();
            questStatuses.Clear();
            triggeredEvents.Clear();
            StateRestored?.Invoke();
        }

        internal void ExecuteActions(GameActionDefinition[] actions)
        {
            if (actions == null)
            {
                return;
            }

            foreach (GameActionDefinition action in actions)
            {
                ExecuteAction(action);
            }
        }

        internal void FinishDialogue(DialogueSession session)
        {
            if (session == null || ActiveDialogue != session)
            {
                return;
            }

            ActiveDialogue = null;
            DialogueEnded?.Invoke(session);
        }

        private bool EvaluateCondition(GameConditionDefinition condition)
        {
            if (condition == null || string.IsNullOrWhiteSpace(condition.type))
            {
                return false;
            }

            bool result;
            switch (condition.type.Trim().ToLowerInvariant())
            {
                case "flag":
                    result = GetFlag(condition.key) == condition.boolValue;
                    break;
                case "counter":
                    result = CompareCounter(GetCounter(condition.key), condition.intValue, condition.comparison);
                    break;
                case "queststatus":
                    result = Enum.TryParse(condition.value, true, out QuestStatus expectedStatus) &&
                             GetQuestStatus(condition.key) == expectedStatus;
                    break;
                case "eventtriggered":
                    result = HasEventTriggered(condition.key) == condition.boolValue;
                    break;
                default:
                    if (customConditionHandlers.TryGetValue(condition.type, out CustomGameConditionHandler handler))
                    {
                        result = handler(condition, this);
                    }
                    else
                    {
                        RaiseDiagnostic($"No handler is registered for condition type '{condition.type}'.");
                        result = false;
                    }

                    break;
            }

            return condition.negate ? !result : result;
        }

        private void ExecuteAction(GameActionDefinition action)
        {
            if (action == null || string.IsNullOrWhiteSpace(action.type))
            {
                RaiseDiagnostic("Ignored a null action or an action with no type.");
                return;
            }

            try
            {
                switch (action.type.Trim().ToLowerInvariant())
                {
                    case "setflag":
                        SetFlag(action.key, action.boolValue);
                        break;
                    case "setcounter":
                        SetCounter(action.key, action.intValue);
                        break;
                    case "addcounter":
                        AddCounter(action.key, action.intValue);
                        break;
                    case "startquest":
                        StartQuest(action.key);
                        break;
                    case "completequest":
                        CompleteQuest(action.key);
                        break;
                    case "startdialogue":
                        StartDialogue(action.key);
                        break;
                    case "triggerevent":
                        GameEventTriggerResult nestedResult = TriggerEvent(action.key);
                        if (!nestedResult.Succeeded)
                        {
                            RaiseDiagnostic(nestedResult.Message);
                        }

                        break;
                    default:
                        if (customActionHandlers.TryGetValue(action.type, out CustomGameActionHandler handler))
                        {
                            handler(action, this);
                        }
                        else
                        {
                            RaiseDiagnostic($"No handler is registered for action type '{action.type}'.");
                        }

                        break;
                }
            }
            catch (Exception exception)
            {
                RaiseDiagnostic($"Action '{action.type}' failed: {exception.Message}");
            }
        }

        private static bool CompareCounter(int actual, int expected, string comparison)
        {
            switch ((comparison ?? "equals").Trim().ToLowerInvariant())
            {
                case "equals":
                case "equal":
                case "==":
                    return actual == expected;
                case "notequals":
                case "not_equal":
                case "!=":
                    return actual != expected;
                case "greaterthan":
                case "greater_than":
                case ">":
                    return actual > expected;
                case "greaterorequal":
                case "greater_or_equal":
                case ">=":
                    return actual >= expected;
                case "lessthan":
                case "less_than":
                case "<":
                    return actual < expected;
                case "lessorequal":
                case "less_or_equal":
                case "<=":
                    return actual <= expected;
                default:
                    return false;
            }
        }

        private void TriggerQuestLifecycleEvent(string eventId, string questId, string transition)
        {
            if (string.IsNullOrWhiteSpace(eventId))
            {
                return;
            }

            GameEventTriggerResult result = TriggerEvent(eventId);
            if (!result.Succeeded)
            {
                RaiseDiagnostic(
                    $"Quest '{questId}' {transition}, but lifecycle event '{eventId}' did not trigger: {result.Message}");
            }
        }

        private void RaiseDiagnostic(string message)
        {
            DiagnosticRaised?.Invoke(message);
        }

        private static GameEventTriggerResult Result(
            GameEventTriggerStatus status,
            GameEventDefinition definition,
            string message)
        {
            return new GameEventTriggerResult(status, definition, message);
        }

        private static FlagStateEntry[] ToFlagEntries(Dictionary<string, bool> values)
        {
            List<string> keys = new List<string>(values.Keys);
            keys.Sort(StringComparer.Ordinal);
            FlagStateEntry[] entries = new FlagStateEntry[keys.Count];
            for (int i = 0; i < keys.Count; i++)
            {
                entries[i] = new FlagStateEntry { key = keys[i], value = values[keys[i]] };
            }

            return entries;
        }

        private static CounterStateEntry[] ToCounterEntries(Dictionary<string, int> values)
        {
            List<string> keys = new List<string>(values.Keys);
            keys.Sort(StringComparer.Ordinal);
            CounterStateEntry[] entries = new CounterStateEntry[keys.Count];
            for (int i = 0; i < keys.Count; i++)
            {
                entries[i] = new CounterStateEntry { key = keys[i], value = values[keys[i]] };
            }

            return entries;
        }

        private static QuestStateEntry[] ToQuestEntries(Dictionary<string, QuestStatus> values)
        {
            List<string> keys = new List<string>(values.Keys);
            keys.Sort(StringComparer.Ordinal);
            QuestStateEntry[] entries = new QuestStateEntry[keys.Count];
            for (int i = 0; i < keys.Count; i++)
            {
                entries[i] = new QuestStateEntry { questId = keys[i], status = values[keys[i]].ToString() };
            }

            return entries;
        }

        private static string[] ToSortedArray(HashSet<string> values)
        {
            string[] result = new string[values.Count];
            values.CopyTo(result);
            Array.Sort(result, StringComparer.Ordinal);
            return result;
        }
    }

    [Serializable]
    internal sealed class GameEventRuntimeState
    {
        public FlagStateEntry[] flags = Array.Empty<FlagStateEntry>();
        public CounterStateEntry[] counters = Array.Empty<CounterStateEntry>();
        public QuestStateEntry[] quests = Array.Empty<QuestStateEntry>();
        public string[] triggeredEventIds = Array.Empty<string>();
    }

    [Serializable]
    internal sealed class FlagStateEntry
    {
        public string key;
        public bool value;
    }

    [Serializable]
    internal sealed class CounterStateEntry
    {
        public string key;
        public int value;
    }

    [Serializable]
    internal sealed class QuestStateEntry
    {
        public string questId;
        public string status;
    }
}
