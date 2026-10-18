using System;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;

namespace Lumenfall.Story
{
    /// <summary>
    /// The way into the forgotten room (<see cref="SecretRooms.ForgottenRoomId"/>): follow the white dog's trail. Miki,
    /// in the devs' room, eventually tells how they chased a little white dog from the forest to the ice trial, the dorm,
    /// the Arcane Workshop and the library, and lost it running to a gate. Entering those areas in that order (campus,
    /// the hub between them, does not count; the west or deep forest both count as the forest), then trying the north
    /// gate where the dog sleeps, leads into the room once. Entering any other area on the way loses the trail, and
    /// entering the forest starts it again. Progress is a saved counter.
    /// </summary>
    public static class WhiteDogTrailSecret
    {
        public const string StepCounter = "secret.trail.step";

        /// <summary>The trail's areas, in order; the forest step accepts either forest.</summary>
        private static readonly string[][] Trail =
        {
            new[] { "westForest", "deepForest" },
            new[] { "C03.IceTrial" },
            new[] { "B01.Ground" },
            new[] { "B02.Ground" },
            new[] { "B04.Ground" },
        };

        public static int Length => Trail.Length;

        /// <summary>Called on every map load: moves the trail on, keeps it, or loses it.</summary>
        public static void OnMapLoaded(GameEventRuntime runtime, string mapId)
        {
            if (runtime == null || string.IsNullOrEmpty(mapId) || SecretRooms.HasVisited(runtime, SecretRooms.ForgottenRoomId)) return;
            // Campus joins every area, and the secret rooms are side trips, so passing through them keeps the trail.
            if (string.Equals(mapId, "campus", StringComparison.OrdinalIgnoreCase) || SecretRooms.IsSecretRoom(mapId)) return;
            int step = Math.Max(0, Math.Min(runtime.GetCounter(StepCounter), Trail.Length));
            int next;
            if (step < Trail.Length && Matches(Trail[step], mapId)) next = step + 1;
            else if (step > 0 && Matches(Trail[step - 1], mapId)) next = step;
            else next = Matches(Trail[0], mapId) ? 1 : 0;
            if (next != step) runtime.SetCounter(StepCounter, next);
        }

        /// <summary>True once the whole trail has been followed and the gate is the only place left to look.</summary>
        public static bool AtTheGate(GameEventRuntime runtime) =>
            runtime != null && !SecretRooms.HasVisited(runtime, SecretRooms.ForgottenRoomId) && runtime.GetCounter(StepCounter) >= Trail.Length;

        /// <summary>
        /// Called when the player tries the north gate. At the end of the trail it follows the paw prints into the
        /// forgotten room and returns true; otherwise the gate behaves as it always does.
        /// </summary>
        public static bool TryFollowAtGate(GameEventRuntime runtime)
        {
            if (!AtTheGate(runtime)) return false;
            runtime.SetCounter(StepCounter, 0);
            PortalBlockedPopup.Show("Small white paw prints run right under the gate, then turn aside, through a door you have never noticed.", "The north gate", 4f);
            SecretRooms.Enter(SecretRooms.ForgottenRoomId);
            return true;
        }

        private static bool Matches(string[] maps, string mapId)
        {
            foreach (string map in maps)
                if (string.Equals(map, mapId, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
