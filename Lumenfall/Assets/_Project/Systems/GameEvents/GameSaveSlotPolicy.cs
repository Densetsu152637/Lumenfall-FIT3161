using System;
using System.Collections.Generic;

namespace Lumenfall.GameEvents
{
    /// <summary>Pure slot ordering policy used by startup-menu logic and edit-mode tests.</summary>
    public static class GameSaveSlotPolicy
    {
        /// <summary>Dispatches a slot selection without treating a manual-save destination as a load request.</summary>
        public static void SubmitSelection(IList<string> slots, int index, bool saving, Action<string> save, Action<string> load)
        {
            if (slots == null || index < 0 || index >= slots.Count) return;
            if (saving) save(slots[index]);
            else load(slots[index]);
        }

        public static string SelectMostRecent(
            IList<string> slots,
            string markedSlot,
            Func<string, bool> isValid,
            Func<string, DateTime> savedAtUtc)
        {
            if (slots == null || isValid == null || savedAtUtc == null) return null;
            if (!string.IsNullOrWhiteSpace(markedSlot) && isValid(markedSlot)) return markedSlot;

            string selected = null;
            DateTime newest = DateTime.MinValue;
            for (int i = 0; i < slots.Count; i++)
            {
                string slot = slots[i];
                if (!isValid(slot)) continue;
                DateTime timestamp = savedAtUtc(slot);
                if (selected == null || timestamp > newest)
                {
                    selected = slot;
                    newest = timestamp;
                }
            }
            return selected;
        }
    }
}
