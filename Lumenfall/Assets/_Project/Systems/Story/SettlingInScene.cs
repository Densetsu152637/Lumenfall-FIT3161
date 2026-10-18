using System;
using Lumenfall.Cutscenes;
using Lumenfall.DataDriven;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Inventory;
using Lumenfall.Player;
using Lumenfall.Quests;
using Lumenfall.Startup;
using UnityEngine;

namespace Lumenfall.Story
{
    /// <summary>
    /// The end of Chapter 1. After Prof. Bann sends the player to the dormitory, the receptionist Marigold tells them their
    /// room is not ready (<see cref="NotReadyDialogueId"/>): get to know the campus and meet the other students. Bann
    /// then chats like anyone else. Once every Chapter 1 puzzle (<see cref="ChapterQuestIds"/>) is solved, the player
    /// thinks their room should be ready by now (<see cref="ThoughtCutsceneId"/>) and walks back themselves. Marigold
    /// then hands over the key, the player walks to their door, "End of Chapter 1" plays over a fade
    /// (<see cref="EndingCutsceneId"/>), and the game saves and returns to the main menu. Loading that save puts the
    /// player back at their door, with no further story.
    /// </summary>
    public sealed class SettlingInScene : MonoBehaviour
    {
        public const string MapId = "B01.Ground";
        public const string ReceptionistNpcId = "marigold";
        public const string HeadmistressNpcId = LayOfTheLandScene.BannNpcId;
        public const string BannMetFlag = "story.orientation.bann_met";
        public const string NotReadyFlag = "story.dorm.room_not_ready";
        public const string ThoughtFlag = "story.dorm.room_ready_thought";
        public const string KeyFlag = "story.dorm.key_received";
        public const string CompleteFlag = "story.chapter1.complete";
        public const string NotReadyDialogueId = "dorm_room_not_ready";
        public const string ThoughtCutsceneId = "room_ready_thought";
        public const string EndingCutsceneId = "chapter1_room_ready";
        /// <summary>
        /// A short scene at the dorm desk, the first time the player talks to Rowan or Thistle: Rowan pleads for a room,
        /// Thistle is no help, and Marigold asks Thistle not to laugh. Then whoever the player spoke to carries on as usual.
        /// </summary>
        public const string NoRoomsDialogueId = "dorm_no_rooms";
        public const string NoRoomsFlag = "story.dorm.no_rooms_scene";
        public static readonly string[] NoRoomsPeople = { "rowan", "thistle" };

        /// <summary>
        /// The joke's payoff. From the room-ready thought on, Rowan, still without a room, has moved onto the lounge floor
        /// (<see cref="RowanHomelessCell"/>) and says so (<see cref="RowanHomelessDialogueId"/>); Thistle, asked, only
        /// gives the player a thought about how hard he is trying not to laugh, and Marigold tells him off
        /// (<see cref="ThistleLaughingDialogueId"/>).
        /// </summary>
        public const string RowanNpcId = "rowan";
        public const string ThistleNpcId = "thistle";
        public const string RowanHomelessDialogueId = "dorm_rowan_homeless";
        public const string ThistleLaughingDialogueId = "dorm_thistle_laughing";
        public static readonly Vector2Int RowanHomelessCell = new Vector2Int(27, 7);

        /// <summary>The room door the ending walks to.</summary>
        public const string RoomDoorName = "Room Door 3";

        /// <summary>The Chapter 1 puzzles; the last of them to be solved brings the room-ready thought.</summary>
        public static readonly string[] ChapterQuestIds =
        {
            "stella_first_light", "stella_string_them_together", "cinna_out_of_alignment",
            "cinna_cold_feet_warm_idea", "orio_patch_notes", "katrina_something_borrowed"
        };

        private static SettlingInScene instance;
        private bool endingWasPlaying;
        private bool thoughtFailed;
        private GameObject rowanPlacedIn;
        private GameEventRuntime rowanPlacedRuntime;
        private bool rowanPlacedHomeless;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            GameObject host = new GameObject("[Settling In]");
            instance = host.AddComponent<SettlingInScene>();
            DontDestroyOnLoad(host);
        }

        /// <summary>
        /// What the story has a person say instead of their usual conversation: a dialogue, a cutscene, or (with both
        /// null) their usual catalog dialogue rather than their general chat. False when the story has nothing for them.
        /// </summary>
        public static bool TryGetStoryTurn(GameEventRuntime runtime, string person, out string dialogueId, out string cutsceneId)
        {
            dialogueId = null;
            cutsceneId = null;
            if (runtime == null) return false;
            if (person == ReceptionistNpcId)
            {
                if (runtime.GetFlag(BannMetFlag) && !runtime.GetFlag(NotReadyFlag)) { dialogueId = NotReadyDialogueId; return true; }
                if (runtime.GetFlag(ThoughtFlag) && !runtime.GetFlag(KeyFlag)) { cutsceneId = EndingCutsceneId; return true; }
                return false;
            }
            // The headmistress keeps her orientation lines and the reminder to visit the dorm until the player has.
            if (person == HeadmistressNpcId) return !runtime.GetFlag(NotReadyFlag);
            // Rowan never did get a room.
            if (person == RowanNpcId && runtime.GetFlag(ThoughtFlag)) { dialogueId = RowanHomelessDialogueId; return true; }
            if (person == ThistleNpcId && runtime.GetFlag(ThoughtFlag)) { dialogueId = ThistleLaughingDialogueId; return true; }
            return false;
        }

        /// <summary>A dialogue that plays before the person's own conversation (and introduction), once.</summary>
        public static bool TryGetPrelude(GameEventRuntime runtime, string person, out string dialogueId)
        {
            dialogueId = null;
            // The desk argument is over once Rowan has been left without a room.
            if (runtime == null || runtime.GetFlag(NoRoomsFlag) || runtime.GetFlag(ThoughtFlag) || Array.IndexOf(NoRoomsPeople, person) < 0) return false;
            dialogueId = NoRoomsDialogueId;
            return true;
        }

        /// <summary>True once the player has been to the dorm and every Chapter 1 puzzle is solved.</summary>
        public static bool ShouldThinkRoomIsReady(GameEventRuntime runtime)
        {
            if (runtime == null || !runtime.GetFlag(NotReadyFlag) || runtime.GetFlag(ThoughtFlag)) return false;
            foreach (string quest in ChapterQuestIds)
                if (runtime.GetQuestStatus(quest) != QuestStatus.Completed) return false;
            return true;
        }

        private void Update()
        {
            GameEventRuntime runtime = GameEventManager.Instance != null ? GameEventManager.Instance.Runtime : null;
            if (runtime == null) return;

            // The ending has finished: save at the door and go back to the menu.
            bool endingPlaying = CutscenePlayer.IsPlaying && CutscenePlayer.CurrentId == EndingCutsceneId;
            if (endingWasPlaying && !endingPlaying && runtime.GetFlag(CompleteFlag)) StartupMenu.ReturnToMainMenu();
            endingWasPlaying = endingPlaying;

            // A missing or broken cutscene would otherwise be retried every frame.
            if (!thoughtFailed && ShouldThinkRoomIsReady(runtime) && IsQuiet(runtime)) thoughtFailed = !CutscenePlayer.Play(ThoughtCutsceneId);
            DataDrivenWorldLoader loader = DataDrivenWorldLoader.Instance;
            if (loader != null && loader.LoadedMap != null && loader.LoadedMap.id == MapId)
            {
                GridMover player = GridMover.FindActive();
                GridWorld world = player != null ? player.World : FindAnyObjectByType<GridWorld>();
                bool homeless = runtime.GetFlag(ThoughtFlag);
                if ((rowanPlacedIn != loader.CurrentAreaRoot || rowanPlacedRuntime != runtime || rowanPlacedHomeless != homeless) &&
                    RefreshRowanPlacement(loader.CurrentAreaRoot, world, runtime, player))
                {
                    rowanPlacedIn = loader.CurrentAreaRoot;
                    rowanPlacedRuntime = runtime;
                    rowanPlacedHomeless = homeless;
                }
            }
        }

        /// <summary>
        /// Reconciles the cached dorm's Rowan and blocker with the current story state, including after an earlier save
        /// or New Game. Area activation calls this before placing the player; live changes wait for their cell to clear.
        /// </summary>
        /// <returns>False when placement must be retried because dependencies or the destination are unavailable.</returns>
        public static bool RefreshRowanPlacement(GameObject area, GridWorld world, GameEventRuntime runtime, GridMover player = null)
        {
            if (area == null || world == null || runtime == null) return false;
            foreach (DynamicNpc npc in area.GetComponentsInChildren<DynamicNpc>(true))
            {
                if (npc.Definition == null || npc.Definition.id != RowanNpcId) continue;
                Vector2Int cell = runtime.GetFlag(ThoughtFlag) ? RowanHomelessCell : new Vector2Int(npc.Definition.x, npc.Definition.y);
                Vector3 spot = world.CellToWorld(cell);
                Vector3 current = npc.transform.position;
                if (Mathf.Approximately(current.x, spot.x) && Mathf.Approximately(current.z, spot.z)) continue;
                if (player != null && (player.CurrentCell == cell || player.DestinationCell == cell)) return false;
                npc.transform.position = new Vector3(spot.x, current.y, spot.z);
                foreach (GridObstacle obstacle in npc.GetComponentsInChildren<GridObstacle>(true)) obstacle.RefreshFootprint();
            }
            return true;
        }

        // Nothing else on screen, so the thought follows the last puzzle's own wrap-up rather than interrupting it.
        private static bool IsQuiet(GameEventRuntime runtime) =>
            !CutscenePlayer.IsPlaying && !StartupMenu.IsOpen && Time.timeScale > 0f && runtime.ActiveDialogue == null &&
            !CinnaTrialDeparture.InProgress && !PlayerNamePrompt.IsOpen && !InventoryUiState.IsOpen &&
            DataDrivenWorldLoader.Instance != null && DataDrivenWorldLoader.Instance.CurrentAreaRoot != null;
    }
}
