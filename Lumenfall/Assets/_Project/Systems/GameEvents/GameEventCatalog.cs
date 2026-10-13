using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lumenfall.GameEvents
{
    public enum CatalogMessageSeverity
    {
        Warning,
        Error
    }

    public readonly struct CatalogValidationMessage
    {
        public CatalogValidationMessage(CatalogMessageSeverity severity, string source, string message)
        {
            Severity = severity;
            Source = source;
            Message = message;
        }

        public CatalogMessageSeverity Severity { get; }
        public string Source { get; }
        public string Message { get; }

        public override string ToString()
        {
            return string.IsNullOrEmpty(Source) ? Message : $"{Source}: {Message}";
        }
    }

    /// <summary>Merges JSON databases, indexes their definitions and reports authoring errors.</summary>
    public sealed class GameEventCatalog
    {
        private static readonly HashSet<string> BuiltInConditionTypes = new HashSet<string>(
            new[] { "flag", "counter", "queststatus", "eventtriggered" },
            StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> BuiltInActionTypes = new HashSet<string>(
            new[]
            {
                "setflag", "setcounter", "addcounter", "startquest", "completequest",
                "startdialogue", "triggerevent"
            },
            StringComparer.OrdinalIgnoreCase);

        // Custom types that a game system registers a handler for on the live runtime after the catalogue is built.
        private static readonly HashSet<string> DeclaredConditionTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> DeclaredActionTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Declares a custom condition type whose handler is registered on the runtime later, so the catalogue does not
        /// warn that it has no handler. Declare it before the catalogue is built (for example at SubsystemRegistration).
        /// </summary>
        public static void DeclareCustomConditionType(string type)
        {
            if (!string.IsNullOrWhiteSpace(type)) DeclaredConditionTypes.Add(type.Trim());
        }

        /// <summary>The action-type counterpart of <see cref="DeclareCustomConditionType"/>.</summary>
        public static void DeclareCustomActionType(string type)
        {
            if (!string.IsNullOrWhiteSpace(type)) DeclaredActionTypes.Add(type.Trim());
        }

        private readonly Dictionary<string, GameEventDefinition> events =
            new Dictionary<string, GameEventDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, QuestDefinition> quests =
            new Dictionary<string, QuestDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, DialogueDefinition> dialogues =
            new Dictionary<string, DialogueDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<string, DialogueNodeDefinition>> dialogueNodes =
            new Dictionary<string, Dictionary<string, DialogueNodeDefinition>>(StringComparer.Ordinal);
        private readonly List<CatalogValidationMessage> messages = new List<CatalogValidationMessage>();

        public GameEventCatalog(IEnumerable<GameEventDatabaseDefinition> databases)
        {
            if (databases != null)
            {
                int index = 0;
                foreach (GameEventDatabaseDefinition database in databases)
                {
                    AddDatabase(database, $"database[{index}]");
                    index++;
                }
            }

            ValidateReferences();
        }

        public IReadOnlyDictionary<string, GameEventDefinition> Events => events;
        public IReadOnlyDictionary<string, QuestDefinition> Quests => quests;
        public IReadOnlyDictionary<string, DialogueDefinition> Dialogues => dialogues;
        public IReadOnlyList<CatalogValidationMessage> ValidationMessages => messages;
        public bool IsValid => messages.TrueForAll(message => message.Severity != CatalogMessageSeverity.Error);

        public static bool TryParseJson(
            string json,
            string source,
            out GameEventDatabaseDefinition database,
            out string error)
        {
            database = null;
            error = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = $"{source}: JSON is empty.";
                return false;
            }

            try
            {
                database = JsonUtility.FromJson<GameEventDatabaseDefinition>(json);
            }
            catch (Exception exception)
            {
                error = $"{source}: invalid JSON ({exception.Message}).";
                return false;
            }

            if (database == null)
            {
                error = $"{source}: JSON does not contain a game-event database object.";
                return false;
            }

            if (database.schemaVersion != 1)
            {
                error = $"{source}: unsupported schemaVersion {database.schemaVersion}; expected 1.";
                database = null;
                return false;
            }

            return true;
        }

        public bool TryGetEvent(string id, out GameEventDefinition definition)
        {
            return events.TryGetValue(id ?? string.Empty, out definition);
        }

        public bool TryGetQuest(string id, out QuestDefinition definition)
        {
            return quests.TryGetValue(id ?? string.Empty, out definition);
        }

        public bool TryGetDialogue(string id, out DialogueDefinition definition)
        {
            return dialogues.TryGetValue(id ?? string.Empty, out definition);
        }

        public bool TryGetDialogueNode(string dialogueId, string nodeId, out DialogueNodeDefinition node)
        {
            node = null;
            return dialogueNodes.TryGetValue(dialogueId ?? string.Empty, out Dictionary<string, DialogueNodeDefinition> nodes) &&
                   nodes.TryGetValue(nodeId ?? string.Empty, out node);
        }

        private void AddDatabase(GameEventDatabaseDefinition database, string source)
        {
            if (database == null)
            {
                AddError(source, "Database is null.");
                return;
            }

            AddDefinitions(database.events, events, source, "event", definition => definition.id);
            AddDefinitions(database.quests, quests, source, "quest", definition => definition.id);
            AddDefinitions(database.dialogues, dialogues, source, "dialogue", definition => definition.id);
        }

        private void AddDefinitions<T>(
            T[] definitions,
            Dictionary<string, T> destination,
            string source,
            string kind,
            Func<T, string> getId)
            where T : class
        {
            if (definitions == null)
            {
                return;
            }

            for (int i = 0; i < definitions.Length; i++)
            {
                T definition = definitions[i];
                if (definition == null)
                {
                    AddError(source, $"{kind}[{i}] is null.");
                    continue;
                }

                string id = getId(definition);
                if (string.IsNullOrWhiteSpace(id))
                {
                    AddError(source, $"{kind}[{i}] has no id.");
                    continue;
                }

                if (destination.ContainsKey(id))
                {
                    AddError(source, $"Duplicate {kind} id '{id}'.");
                    continue;
                }

                destination.Add(id, definition);
            }
        }

        private void ValidateReferences()
        {
            foreach (KeyValuePair<string, GameEventDefinition> pair in events)
            {
                string source = $"event '{pair.Key}'";
                ValidateConditionMode(pair.Value.conditionMode, source);
                ValidateConditions(pair.Value.conditions, source);
                ValidateActions(pair.Value.actions, source);
            }

            foreach (KeyValuePair<string, QuestDefinition> pair in quests)
            {
                QuestDefinition quest = pair.Value;
                if (!string.IsNullOrWhiteSpace(quest.onStartedEventId) && !events.ContainsKey(quest.onStartedEventId))
                {
                    AddError($"quest '{pair.Key}'", $"Unknown onStartedEventId '{quest.onStartedEventId}'.");
                }

                if (!string.IsNullOrWhiteSpace(quest.onCompletedEventId) && !events.ContainsKey(quest.onCompletedEventId))
                {
                    AddError($"quest '{pair.Key}'", $"Unknown onCompletedEventId '{quest.onCompletedEventId}'.");
                }
            }

            foreach (KeyValuePair<string, DialogueDefinition> pair in dialogues)
            {
                ValidateDialogue(pair.Key, pair.Value);
            }
        }

        private void ValidateDialogue(string dialogueId, DialogueDefinition dialogue)
        {
            string source = $"dialogue '{dialogueId}'";
            Dictionary<string, DialogueNodeDefinition> nodes =
                new Dictionary<string, DialogueNodeDefinition>(StringComparer.Ordinal);
            DialogueNodeDefinition[] definitions = dialogue.nodes ?? Array.Empty<DialogueNodeDefinition>();

            for (int i = 0; i < definitions.Length; i++)
            {
                DialogueNodeDefinition node = definitions[i];
                if (node == null || string.IsNullOrWhiteSpace(node.id))
                {
                    AddError(source, $"node[{i}] is null or has no id.");
                    continue;
                }

                if (nodes.ContainsKey(node.id))
                {
                    AddError(source, $"Duplicate node id '{node.id}'.");
                    continue;
                }

                nodes.Add(node.id, node);
            }

            dialogueNodes[dialogueId] = nodes;
            if (string.IsNullOrWhiteSpace(dialogue.startNodeId) || !nodes.ContainsKey(dialogue.startNodeId))
            {
                AddError(source, $"Unknown startNodeId '{dialogue.startNodeId}'.");
            }

            foreach (KeyValuePair<string, DialogueNodeDefinition> pair in nodes)
            {
                DialogueNodeDefinition node = pair.Value;
                string nodeSource = $"{source}, node '{pair.Key}'";
                ValidateNodeReference(node.nextNodeId, nodes, nodeSource);
                ValidateActions(node.onEnterActions, nodeSource);

                HashSet<string> choiceIds = new HashSet<string>(StringComparer.Ordinal);
                DialogueChoiceDefinition[] choices = node.choices ?? Array.Empty<DialogueChoiceDefinition>();
                for (int i = 0; i < choices.Length; i++)
                {
                    DialogueChoiceDefinition choice = choices[i];
                    if (choice == null || string.IsNullOrWhiteSpace(choice.id))
                    {
                        AddError(nodeSource, $"choice[{i}] is null or has no id.");
                        continue;
                    }

                    if (!choiceIds.Add(choice.id))
                    {
                        AddError(nodeSource, $"Duplicate choice id '{choice.id}'.");
                    }

                    string choiceSource = $"{nodeSource}, choice '{choice.id}'";
                    ValidateNodeReference(choice.nextNodeId, nodes, choiceSource);
                    ValidateConditionMode(choice.conditionMode, choiceSource);
                    ValidateConditions(choice.conditions, choiceSource);
                    ValidateActions(choice.actions, choiceSource);
                }
            }
        }

        private void ValidateNodeReference(
            string nodeId,
            Dictionary<string, DialogueNodeDefinition> nodes,
            string source)
        {
            if (!string.IsNullOrWhiteSpace(nodeId) && !nodes.ContainsKey(nodeId))
            {
                AddError(source, $"Unknown nextNodeId '{nodeId}'.");
            }
        }

        private void ValidateConditions(GameConditionDefinition[] conditions, string source)
        {
            if (conditions == null)
            {
                return;
            }

            for (int i = 0; i < conditions.Length; i++)
            {
                GameConditionDefinition condition = conditions[i];
                if (condition == null || string.IsNullOrWhiteSpace(condition.type))
                {
                    AddError(source, $"condition[{i}] is null or has no type.");
                    continue;
                }

                if (!BuiltInConditionTypes.Contains(condition.type) && !DeclaredConditionTypes.Contains(condition.type))
                {
                    AddWarning(source, $"Condition type '{condition.type}' requires a custom handler.");
                }

                if (string.IsNullOrWhiteSpace(condition.key))
                {
                    AddError(source, $"condition[{i}] has no key.");
                }

                if (string.Equals(condition.type, "queststatus", StringComparison.OrdinalIgnoreCase) &&
                    !quests.ContainsKey(condition.key))
                {
                    AddError(source, $"condition[{i}] references unknown quest '{condition.key}'.");
                }

                if (string.Equals(condition.type, "eventtriggered", StringComparison.OrdinalIgnoreCase) &&
                    !events.ContainsKey(condition.key))
                {
                    AddError(source, $"condition[{i}] references unknown event '{condition.key}'.");
                }
            }
        }

        private void ValidateActions(GameActionDefinition[] actions, string source)
        {
            if (actions == null)
            {
                return;
            }

            for (int i = 0; i < actions.Length; i++)
            {
                GameActionDefinition action = actions[i];
                if (action == null || string.IsNullOrWhiteSpace(action.type))
                {
                    AddError(source, $"action[{i}] is null or has no type.");
                    continue;
                }

                if (!BuiltInActionTypes.Contains(action.type) && !DeclaredActionTypes.Contains(action.type))
                {
                    AddWarning(source, $"Action type '{action.type}' requires a custom handler.");
                }

                if (string.IsNullOrWhiteSpace(action.key))
                {
                    AddError(source, $"action[{i}] has no key.");
                    continue;
                }

                if ((string.Equals(action.type, "startquest", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(action.type, "completequest", StringComparison.OrdinalIgnoreCase)) &&
                    !quests.ContainsKey(action.key))
                {
                    AddError(source, $"action[{i}] references unknown quest '{action.key}'.");
                }

                if (string.Equals(action.type, "startdialogue", StringComparison.OrdinalIgnoreCase) &&
                    !dialogues.ContainsKey(action.key))
                {
                    AddError(source, $"action[{i}] references unknown dialogue '{action.key}'.");
                }

                if (string.Equals(action.type, "triggerevent", StringComparison.OrdinalIgnoreCase) &&
                    !events.ContainsKey(action.key))
                {
                    AddError(source, $"action[{i}] references unknown event '{action.key}'.");
                }
            }
        }

        private void ValidateConditionMode(string conditionMode, string source)
        {
            if (!string.IsNullOrWhiteSpace(conditionMode) &&
                !string.Equals(conditionMode, "all", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(conditionMode, "any", StringComparison.OrdinalIgnoreCase))
            {
                AddError(source, $"Unknown conditionMode '{conditionMode}'; use 'all' or 'any'.");
            }
        }

        private void AddWarning(string source, string message)
        {
            messages.Add(new CatalogValidationMessage(CatalogMessageSeverity.Warning, source, message));
        }

        private void AddError(string source, string message)
        {
            messages.Add(new CatalogValidationMessage(CatalogMessageSeverity.Error, source, message));
        }
    }
}
