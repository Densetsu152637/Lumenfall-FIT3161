using Lumenfall.Cutscenes;
using Lumenfall.DataDriven;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Startup;
using Lumenfall.Story;
using UnityEditor;
using UnityEngine;

namespace Lumenfall.EditorTools
{
    /// <summary>
    /// Takes the player straight into a quiet secret room to look around, without finding its secret first. In Play Mode
    /// it goes at once; outside it, it enters Play Mode and goes as soon as a game is running (load a save or start a new
    /// one from the title menu). It does not mark the room visited, so its real way in still works, and in the forgotten
    /// room it brings the man back if he has already gone. The exit ring leaves as usual.
    /// </summary>
    public static class SecretRoomPreviewMenu
    {
        private const string PendingRoomKey = "Lumenfall.SecretRoomPreview.Room";
        private const string PendingTimeKey = "Lumenfall.SecretRoomPreview.Time";
        private const float PendingSeconds = 60f;

        private static string waitingFor;

        [MenuItem("Lumenfall/Story/Visit Forgotten Room (egg)")]
        public static void VisitForgottenRoom() => Visit(SecretRooms.ForgottenRoomId);

        [MenuItem("Lumenfall/Story/Visit Dog Room")]
        public static void VisitDogRoom() => Visit(SecretRooms.DogRoomId);

        private static void Visit(string roomId)
        {
            if (EditorApplication.isPlaying)
            {
                WaitThenGo(roomId);
                return;
            }
            // Play Mode reloads scripts, so the request waits in the editor session until play has begun. It is
            // timestamped: if play never starts (compile errors), a later, ordinary Play does not pick it up.
            SessionState.SetString(PendingRoomKey, roomId);
            SessionState.SetFloat(PendingTimeKey, (float)EditorApplication.timeSinceStartup);
            EditorApplication.EnterPlaymode();
        }

        [InitializeOnLoadMethod]
        private static void ListenForPlayMode()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode) { StopWaiting(); return; }
            if (change != PlayModeStateChange.EnteredPlayMode) return;
            string roomId = SessionState.GetString(PendingRoomKey, null);
            float requested = SessionState.GetFloat(PendingTimeKey, -1f);
            SessionState.EraseString(PendingRoomKey);
            SessionState.EraseFloat(PendingTimeKey);
            if (!string.IsNullOrEmpty(roomId) && requested >= 0f && EditorApplication.timeSinceStartup - requested <= PendingSeconds)
                WaitThenGo(roomId);
        }

        private static void WaitThenGo(string roomId)
        {
            StopWaiting();
            waitingFor = roomId;
            EditorApplication.update += Tick;
            if (StartupMenu.IsOpen) Debug.Log($"Secret room preview: load a save or start a new game, and you'll be taken to {roomId}.");
        }

        private static void StopWaiting()
        {
            EditorApplication.update -= Tick;
            waitingFor = null;
        }

        // Waits for play proper: the title menu closed, no opening or prologue running, the world and events loaded.
        private static void Tick()
        {
            if (!EditorApplication.isPlaying || waitingFor == null) { StopWaiting(); return; }
            DataDrivenWorldLoader loader = DataDrivenWorldLoader.Instance;
            GameEventManager events = GameEventManager.Instance;
            if (loader == null || events == null || events.Runtime == null) return;
            if (StartupMenu.IsOpen || CutscenePlayer.IsPlaying || PrologueScene.IsPlaying) return;
            if (events.Runtime.ActiveDialogue != null) return;
            if (!SecretRooms.TryGetRoom(waitingFor, out SecretRooms.Room room)) { StopWaiting(); return; }

            if (room.Id == SecretRooms.ForgottenRoomId) events.Runtime.SetFlag(SecretRoomMood.ForgottenManGoneFlag, false);
            // TravelTo refuses while the player is mid-step, so wait for them to stand still.
            GridMover player = Object.FindAnyObjectByType<GridMover>();
            if (player == null || player.IsMoving) return;
            StopWaiting();
            if (loader.TravelTo(room.Id, room.Arrival)) Debug.Log($"Secret room preview: in {room.Name}. Step on the exit ring to leave.");
            else Debug.LogWarning($"Secret room preview: could not travel to {room.Id}.");
        }
    }
}
