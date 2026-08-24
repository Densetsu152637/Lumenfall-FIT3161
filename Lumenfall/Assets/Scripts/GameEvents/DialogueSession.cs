using System;
using System.Collections.Generic;

namespace Lumenfall.GameEvents
{
    /// <summary>
    /// One live traversal through a dialogue tree. UI reads CurrentNode and AvailableChoices,
    /// then calls Continue or SelectChoice.
    /// </summary>
    public sealed class DialogueSession
    {
        private readonly GameEventRuntime runtime;
        private readonly List<DialogueChoiceDefinition> availableChoices =
            new List<DialogueChoiceDefinition>();

        internal DialogueSession(GameEventRuntime runtime, DialogueDefinition definition)
        {
            this.runtime = runtime;
            Definition = definition;
        }

        public DialogueDefinition Definition { get; }
        public DialogueNodeDefinition CurrentNode { get; private set; }
        public IReadOnlyList<DialogueChoiceDefinition> AvailableChoices => availableChoices;
        public bool IsActive { get; private set; } = true;

        public event Action<DialogueSession, DialogueNodeDefinition> NodeChanged;
        public event Action<DialogueSession> Ended;

        public bool Continue()
        {
            if (!IsActive || CurrentNode == null || availableChoices.Count > 0)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(CurrentNode.nextNodeId))
            {
                End();
                return true;
            }

            return MoveTo(CurrentNode.nextNodeId);
        }

        public bool SelectChoice(string choiceId)
        {
            if (!IsActive || string.IsNullOrWhiteSpace(choiceId))
            {
                return false;
            }

            DialogueChoiceDefinition selected = null;
            foreach (DialogueChoiceDefinition choice in availableChoices)
            {
                if (string.Equals(choice.id, choiceId, StringComparison.Ordinal))
                {
                    selected = choice;
                    break;
                }
            }

            if (selected == null)
            {
                return false;
            }

            runtime.ExecuteActions(selected.actions);
            if (!IsActive)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(selected.nextNodeId))
            {
                End();
                return true;
            }

            return MoveTo(selected.nextNodeId);
        }

        public void End()
        {
            if (!IsActive)
            {
                return;
            }

            IsActive = false;
            availableChoices.Clear();
            runtime.FinishDialogue(this);
            Ended?.Invoke(this);
        }

        internal bool EnterStartingNode()
        {
            return MoveTo(Definition.startNodeId);
        }

        private bool MoveTo(string nodeId)
        {
            if (!IsActive || !runtime.Catalog.TryGetDialogueNode(Definition.id, nodeId, out DialogueNodeDefinition node))
            {
                return false;
            }

            CurrentNode = node;
            runtime.ExecuteActions(node.onEnterActions);
            if (IsActive)
            {
                // On-enter actions may alter flags, counters, or quests used by choice conditions.
                RefreshAvailableChoices();
                NodeChanged?.Invoke(this, node);
            }

            return true;
        }

        private void RefreshAvailableChoices()
        {
            availableChoices.Clear();
            foreach (DialogueChoiceDefinition choice in CurrentNode.choices ?? Array.Empty<DialogueChoiceDefinition>())
            {
                if (choice != null && runtime.EvaluateConditions(choice.conditions, choice.conditionMode))
                {
                    availableChoices.Add(choice);
                }
            }
        }
    }
}
