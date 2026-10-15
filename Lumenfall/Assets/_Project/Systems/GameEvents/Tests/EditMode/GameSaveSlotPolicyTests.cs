using System;
using System.Collections.Generic;
using NUnit.Framework;
using Lumenfall.GameEvents;

namespace Lumenfall.Tests.GameEvents
{
    public sealed class GameSaveSlotPolicyTests
    {
        [Test]
        public void ManualSaveSelectionDispatchesSaveIncludingAnEmptyDestinationAndNeverLoads()
        {
            string selected = null;
            int loads = 0;
            string[] manualSlots = { "save-1", "save-2", "save-3" };
            GameSaveSlotPolicy.SubmitSelection(manualSlots, 1, true, slot => selected = slot, _ => loads++);
            Assert.That(selected, Is.EqualTo("save-2"));
            Assert.That(loads, Is.Zero);
            GameSaveSlotPolicy.SubmitSelection(manualSlots, manualSlots.Length, true, _ => Assert.Fail("Back is not a slot."), _ => loads++);
            GameSaveSlotPolicy.SubmitSelection(new[] { "autosave", "save-1" }, 0, false, _ => Assert.Fail("Load must not save."), slot => { Assert.That(slot, Is.EqualTo("autosave")); loads++; });
            Assert.That(loads, Is.EqualTo(1));
        }

        [Test]
        public void ValidLastPlayedMarkerWinsOverNewerSaveTimestamp()
        {
            Dictionary<string, DateTime> timestamps = new Dictionary<string, DateTime>
            {
                ["save-1"] = new DateTime(2026, 1, 1),
                ["save-2"] = new DateTime(2026, 2, 1)
            };

            string selected = GameSaveSlotPolicy.SelectMostRecent(
                new[] { "save-1", "save-2", "save-3" }, "save-1", timestamps.ContainsKey, slot => timestamps[slot]);

            Assert.That(selected, Is.EqualTo("save-1"));
        }

        [Test]
        public void InvalidMarkerFallsBackToNewestValidSave()
        {
            Dictionary<string, DateTime> timestamps = new Dictionary<string, DateTime>
            {
                ["save-2"] = new DateTime(2026, 2, 1),
                ["save-3"] = new DateTime(2026, 3, 1)
            };

            string selected = GameSaveSlotPolicy.SelectMostRecent(
                new[] { "save-1", "save-2", "save-3" }, "save-1", timestamps.ContainsKey, slot => timestamps[slot]);

            Assert.That(selected, Is.EqualTo("save-3"));
        }
    }
}
