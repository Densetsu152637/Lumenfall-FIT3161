using System;
using System.Collections.Generic;
using Lumenfall.DataDriven;
using Lumenfall.GameEvents;
using UnityEngine;

namespace Lumenfall.Story
{
    /// <summary>
    /// The dorm's room doors all say the devs are sleeping. Knocking out pi on them, checking doors 3, 1 and 4 in a row,
    /// wakes the developers: door 4 opens instead, and closing its card takes the player into their room
    /// (<see cref="SecretRooms.DevRoomId"/>). Stella's dormmate line is the hint. The knocks are not saved. Once the room
    /// has been visited the doors are only doors again.
    /// </summary>
    public static class DormKnockSecret
    {
        public static readonly int[] Sequence = { 3, 1, 4 };
        private const string DoorPrefix = "Room Door ";
        private const string OpenedTitle = "Dormitory Room";
        private const string OpenedMessage = "A groan from behind the door. \"...Fine. FINE. It's open.\"";
        // Knocked without having heard Stella's hint: only someone who has been here before knows the knock.
        private const string TooSoonMessage = "Silence. Then, very quietly: \"...How do you know that knock? You aren't supposed to know about us yet.\"";

        private static readonly List<int> knocks = new List<int>();
        private static bool entering;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            BuildingSignInteraction.Checked -= OnChecked;
            BuildingSignInteraction.Closed -= OnClosed;
            knocks.Clear();
            entering = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            BuildingSignInteraction.Checked -= OnChecked;
            BuildingSignInteraction.Checked += OnChecked;
            BuildingSignInteraction.Closed -= OnClosed;
            BuildingSignInteraction.Closed += OnClosed;
        }

        /// <summary>The number on a dorm room door ("Room Door 3" is 3), or 0 for anything else.</summary>
        public static int DoorNumber(string objectName)
        {
            if (objectName == null || !objectName.StartsWith(DoorPrefix, StringComparison.Ordinal)) return 0;
            return int.TryParse(objectName.Substring(DoorPrefix.Length).Trim(), out int number) ? number : 0;
        }

        /// <summary>Adds a knock on <paramref name="door"/>; true when the latest knocks spell out <see cref="Sequence"/>.</summary>
        public static bool Knock(List<int> history, int door)
        {
            history.Add(door);
            while (history.Count > Sequence.Length) history.RemoveAt(0);
            if (history.Count < Sequence.Length) return false;
            for (int i = 0; i < Sequence.Length; i++) if (history[i] != Sequence[i]) return false;
            return true;
        }

        private static void OnChecked(BuildingSignInteraction sign)
        {
            DataDrivenWorldLoader loader = DataDrivenWorldLoader.Instance;
            if (sign == null || loader == null || loader.LoadedMap == null ||
                !string.Equals(loader.LoadedMap.id, SettlingInScene.MapId, StringComparison.OrdinalIgnoreCase)) return;
            GameEventRuntime runtime = GameEventManager.Instance != null ? GameEventManager.Instance.Runtime : null;
            if (SecretRooms.HasVisited(runtime, SecretRooms.DevRoomId)) return;
            int door = DoorNumber(sign.gameObject.name);
            if (door == 0) { knocks.Clear(); return; }
            if (!Knock(knocks, door)) return;
            knocks.Clear();
            entering = true;
            sign.ShowInstead(OpenedTitle, SequenceBreakEnding.KnowsTooMuch(runtime, SecretRooms.DevRoomId) ? TooSoonMessage : OpenedMessage);
        }

        private static void OnClosed(BuildingSignInteraction sign)
        {
            if (!entering) return;
            entering = false;
            SecretRooms.Enter(SecretRooms.DevRoomId);
        }
    }
}
