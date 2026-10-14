using System;
using Lumenfall.GameEvents;

namespace Lumenfall.Startup
{
    /// <summary>
    /// The manual save slot the current run belongs to. New Game or loading a manual slot sets it; choosing a slot on the
    /// save screen changes it; and Quick Save writes to it. The link is a game-event counter
    /// (<c>save.manual-slot</c>: 1 to 3, or 0 for none), so it is written into every save and comes back with it. Saves made
    /// before the link existed load unlinked, because importing event state clears all counters.
    /// </summary>
    public static class ManualSaveSlot
    {
        private const string CounterKey = "save.manual-slot";

        /// <summary>The linked slot id (such as <c>save-1</c>), or null when the run has no slot yet.</summary>
        public static string Linked
        {
            get
            {
                int number = GameEventManager.Instance?.Runtime?.GetCounter(CounterKey) ?? 0;
                return number >= 1 && number <= GameSaveManager.ManualSlots.Length ? GameSaveManager.ManualSlots[number - 1] : null;
            }
        }

        /// <summary>Links the run to a manual slot; any other value (including null) unlinks it.</summary>
        public static void Link(string slot)
        {
            GameEventRuntime runtime = GameEventManager.Instance?.Runtime;
            if (runtime != null) runtime.SetCounter(CounterKey, Array.IndexOf(GameSaveManager.ManualSlots, slot) + 1);
        }

        public static bool IsManual(string slot) => Array.IndexOf(GameSaveManager.ManualSlots, slot) >= 0;

        /// <summary>"Slot 1" for <c>save-1</c>, and so on.</summary>
        public static string Label(string slot)
        {
            int index = Array.IndexOf(GameSaveManager.ManualSlots, slot);
            return index >= 0 ? "Slot " + (index + 1) : slot;
        }

        /// <summary>
        /// Quick-saves the current run to its linked slot. The autosave is refreshed first so both hold the same moment,
        /// and the slot's Load-page thumbnail is retaken. Returns false with a reason when there is no link or the write
        /// fails.
        /// </summary>
        public static bool SaveToLinked(out string error)
        {
            error = null;
            string slot = Linked;
            if (slot == null)
            {
                error = "This game has no save slot yet.";
                return false;
            }
            return SaveTo(slot, out error);
        }

        /// <summary>
        /// Saves to a player-selected manual slot and makes it the destination of later quick saves. The new link is
        /// included in both the refreshed autosave and the selected manual save. If either write fails, the run keeps its
        /// previous quick-save destination.
        /// </summary>
        public static bool SaveTo(string slot, out string error)
        {
            error = null;
            if (!IsManual(slot))
            {
                error = "Choose a valid manual save slot.";
                return false;
            }

            string previous = Linked;
            Link(slot);
            if (!GameAutosave.SaveNow())
            {
                Link(previous);
                error = GameSaveManager.Instance.LastError ?? "Could not refresh the autosave.";
                return false;
            }

            GameSaveManager saves = GameSaveManager.Instance;
            if (!saves.Save(slot))
            {
                Link(previous);
                error = saves.LastError ?? "Could not save to " + Label(slot) + ".";
                return false;
            }
            SaveSlotSummary.RequestThumbnailRefresh(slot);
            return true;
        }
    }
}
