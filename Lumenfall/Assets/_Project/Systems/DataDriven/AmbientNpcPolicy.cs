using System;
using Lumenfall.GameEvents;

namespace Lumenfall.DataDriven
{
    [Serializable]
    public sealed class AmbientNpcScheduleDefinition
    {
        public bool enabled;
        public string phaseCounterKey;
        public AmbientNpcStopDefinition[] stops = Array.Empty<AmbientNpcStopDefinition>();
    }

    [Serializable]
    public sealed class AmbientNpcStopDefinition
    {
        public int x;
        public int y;
    }

    /// <summary>Opt-in ambient content cannot take ownership of Chapter 1 quest actors or rewards.</summary>
    public static class AmbientNpcPolicy
    {
        public static bool IsAmbient(NpcDefinition npc)
        {
            if (npc == null || !npc.ambientOnly || string.IsNullOrWhiteSpace(npc.id) ||
                !string.IsNullOrWhiteSpace(npc.showWhenQuestCompleted) ||
                !string.IsNullOrWhiteSpace(npc.hideWhenQuestCompleted)) return false;
            switch (npc.id.ToLowerInvariant())
            {
                case "stella": case "cinna": case "cinna_cirra": case "orio": case "katrina":
                case "cinna_cirra_orientation": case "cinna_cirra_trial": case "receptionist": return false;
            }
            foreach (NpcDialogueDefinition rule in npc.dialogue ?? Array.Empty<NpcDialogueDefinition>())
                if (rule != null && (!string.IsNullOrWhiteSpace(rule.questId) ||
                    !string.IsNullOrWhiteSpace(rule.prerequisiteQuestId))) return false;
            return true;
        }

        public static bool TryGetStop(NpcDefinition npc, GameEventRuntime runtime, out AmbientNpcStopDefinition stop)
        {
            stop = null;
            AmbientNpcScheduleDefinition schedule = npc?.ambientSchedule;
            if (!IsAmbient(npc) || runtime == null || schedule == null || !schedule.enabled ||
                string.IsNullOrWhiteSpace(schedule.phaseCounterKey) || schedule.stops == null ||
                schedule.stops.Length == 0) return false;
            int phase = runtime.GetCounter(schedule.phaseCounterKey);
            int index = ((phase % schedule.stops.Length) + schedule.stops.Length) % schedule.stops.Length;
            stop = schedule.stops[index];
            return stop != null;
        }

        public static bool IsQuestNeutral(DialogueDefinition dialogue)
        {
            if (dialogue == null || !NeutralMetadata(dialogue.metadata)) return false;
            foreach (DialogueNodeDefinition node in dialogue.nodes ?? Array.Empty<DialogueNodeDefinition>())
            {
                if (node == null || (node.onEnterActions?.Length ?? 0) != 0 || !NeutralMetadata(node.metadata))
                    return false;
                foreach (DialogueChoiceDefinition choice in node.choices ?? Array.Empty<DialogueChoiceDefinition>())
                    if (choice == null || (choice.actions?.Length ?? 0) != 0 ||
                        (choice.conditions?.Length ?? 0) != 0 || !NeutralMetadata(choice.metadata)) return false;
            }
            return dialogue.nodes != null && dialogue.nodes.Length > 0;
        }

        private static bool NeutralMetadata(GameMetadataEntry[] metadata)
        {
            // Only passive presentation keys are allowed; future action-bearing metadata fails closed.
            foreach (GameMetadataEntry entry in metadata ?? Array.Empty<GameMetadataEntry>())
            {
                if (entry == null) continue;
                switch ((entry.key ?? string.Empty).ToLowerInvariant())
                {
                    case "ambient": case "npcid": case "portrait": case "portraitid": case "speaker": break;
                    default: return false;
                }
            }
            return true;
        }

        public static string SelectDialogue(NpcDefinition npc, GameEventRuntime runtime, double sample)
        {
            if (!IsAmbient(npc) || runtime == null) return null;
            long total = 0;
            foreach (string id in npc.dialogueIds ?? Array.Empty<string>())
                if (Eligible(id, runtime)) total++;
            foreach (NpcDialogueDefinition rule in npc.dialogue ?? Array.Empty<NpcDialogueDefinition>())
                if (rule != null && Eligible(rule.dialogueId, runtime)) total += Math.Max(1, rule.weight);
            if (total == 0) return null;
            long ticket = (long)(Math.Min(0.999999999, Math.Max(0, sample)) * total);
            foreach (string id in npc.dialogueIds ?? Array.Empty<string>())
                if (Eligible(id, runtime) && ticket-- == 0) return id;
            foreach (NpcDialogueDefinition rule in npc.dialogue ?? Array.Empty<NpcDialogueDefinition>())
            {
                if (rule == null || !Eligible(rule.dialogueId, runtime)) continue;
                ticket -= Math.Max(1, rule.weight);
                if (ticket < 0) return rule.dialogueId;
            }
            return null;
        }

        private static bool Eligible(string id, GameEventRuntime runtime)
        {
            return !string.IsNullOrWhiteSpace(id) && runtime.Catalog.TryGetDialogue(id, out DialogueDefinition dialogue) &&
                IsQuestNeutral(dialogue);
        }
    }
}
