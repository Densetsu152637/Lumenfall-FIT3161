using Lumenfall.Controls;
using Lumenfall.GameEvents;

namespace Lumenfall.Quests
{
    /// <summary>Katrina-specific progress and instructions presented by the shared <see cref="QuestHintOverlay"/>.</summary>
    internal static class KatrinaLostEchoesGuide
    {
        internal const string QuestId = "katrina_something_borrowed";
        internal const string LessonFlag = "spell.lesson.LostEchoes";
        internal const string KeepsakeFlag = "inventory.keyitem.katrina-keepsake";

        private static readonly string[] AnchorFlags =
        {
            "inventory.keyitem.katrina-library-anchor",
            "inventory.keyitem.katrina-garden-anchor",
            "inventory.keyitem.katrina-stream-anchor"
        };

        private static readonly string[] AnchorLocations = { "Library", "sakura garden", "forest stream" };

        internal static bool IsActive(GameEventRuntime runtime) => runtime != null &&
            runtime.GetQuestStatus(QuestId) == QuestStatus.Active && runtime.GetFlag(LessonFlag);

        internal static string Tracker(GameEventRuntime runtime)
        {
            int recovered = 0;
            string remaining = string.Empty;
            for (int i = 0; i < AnchorFlags.Length; i++)
            {
                if (runtime.GetFlag(AnchorFlags[i])) recovered++;
                else remaining += (remaining.Length == 0 ? string.Empty : ", ") + AnchorLocations[i];
            }

            return recovered == AnchorFlags.Length
                ? "Memory anchors: 3/3\nRemaining: none"
                : $"Memory anchors: {recovered}/3\nRemaining: {remaining}";
        }

        internal static string Instructions(GameEventRuntime runtime)
        {
            if (runtime.GetFlag(KeepsakeFlag)) return "Return the recovered memory anchors to Katrina on campus.";
            return $"1. Press {KeyBindings.GetKeyLabel(GameAction.SpellWheel)} to open the spell wheel and select Lost Echoes.\n" +
                   "2. Stand within four cells of a hidden location.\n" +
                   $"3. Right-click or press {KeyBindings.GetKeyLabel(GameAction.Interact)} to cast. If there is no trace, move closer and try again.\n" +
                   $"4. Press {KeyBindings.GetKeyLabel(GameAction.Talk)} to pick up what you reveal.";
        }
    }
}
