using System;
using System.Collections.Generic;

namespace Lumenfall.GameEvents
{
    /// <summary>Pure slot ordering policy used by startup-menu logic and edit-mode tests.</summary>
    public static class GameSaveSlotPolicy
    {
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
