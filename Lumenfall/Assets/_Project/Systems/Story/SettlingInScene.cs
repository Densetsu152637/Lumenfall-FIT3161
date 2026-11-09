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
    /// The end of Chapter 1, a wild goose chase for the room key. After Prof. Bann sends the player to the dormitory, the
    /// receptionist Marigold says she has already handed their key to one of their four dormmates
    /// (<see cref="NotReadyDialogueId"/>). Bann then chats like anyone else. Once a dormmate's last favour is done, the
    /// player asks them about the key (<see cref="KeyAskDialogueId"/>), straight after that favour's wrap-up, or the next
    /// time they talk; each says they do not have it and someone else must. Once all four have been asked, the player
    /// thinks they will have to ask the receptionist (<see cref="ThoughtCutsceneId"/>) and walks back themselves. At the
    /// desk the story glitches (<see cref="EndingCutsceneId"/>): Marigold's aura breaks through, she asks why they did
    /// not ask for it, the player turns slowly to stare into the camera, and the screen tears. It rewinds, and Marigold
    /// hands over the key as if nothing happened. The player walks to their door, "End of Chapter 1" plays over a fade,
    /// and the game saves and returns to the main menu. Loading that save puts the player back at their door, with no
    /// further story.
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

        /// <summary>
        /// The four dormmates who might have the key, each with the favour that has to be done before they are asked.
        /// </summary>
        public static readonly (string person, string lastQuest)[] Dormmates =
        {
            ("stella", "stella_string_them_together"), ("cinna_cirra", "cinna_cold_feet_warm_idea"),
            ("orio", "orio_patch_notes"), ("katrina", "katrina_something_borrowed")
        };

        public const string KeyAskedFlagPrefix = "story.key.asked.";
        public const string KeyAskDialoguePrefix = "key_ask_";

        // Speaker names in dialogue that belong to each dormmate.
        private static readonly (string person, string speaker)[] DormmateSpeakers =
        {
            ("stella", "Stella"), ("cinna_cirra", "Cinna Cirra"), ("orio", "Orio"), ("katrina", "Katrina")
        };

        public static string KeyAskedFlag(string person) => KeyAskedFlagPrefix + person;

        public static string KeyAskDialogueId(string person) => KeyAskDialoguePrefix + person;

        /// <summary>The Chapter 1 puzzles; every one of them is solved by the time the last dormmate is asked.</summary>
        public static readonly string[] ChapterQuestIds =
        {
            "stella_first_light", "stella_string_them_together", "cinna_out_of_alignment",
            "cinna_cold_feet_warm_idea", "orio_patch_notes", "katrina_something_borrowed"
        };

        private static SettlingInScene instance;
        private bool endingWasPlaying;
        private bool thoughtFailed;
        private GameEventRuntime keyRuntime;
        private string pendingKeyAsk;
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
            // A dormmate whose last favour is done and thanked is asked about the key before anything else.
            if (ShouldAskAboutKey(runtime, person) && runtime.GetFlag(NpcIntroductions.QuestThankedFlag(LastQuestOf(person))))
            {
                dialogueId = KeyAskDialogueId(person);
                return true;
            }
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

        /// <summary>The favour a dormmate needs done before they are asked about the key, or null for anyone else.</summary>
        public static string LastQuestOf(string person)
        {
            foreach ((string dormmate, string quest) in Dormmates) if (dormmate == person) return quest;
            return null;
        }

        /// <summary>
        /// True when the player is hunting for the key (Marigold has sent them, and the hunt is not over), this dormmate's
        /// last favour is done, and they have not been asked yet.
        /// </summary>
        public static bool ShouldAskAboutKey(GameEventRuntime runtime, string person)
        {
            string quest = LastQuestOf(person);
            return quest != null && runtime != null && runtime.GetFlag(NotReadyFlag) && !runtime.GetFlag(ThoughtFlag) &&
                   !runtime.GetFlag(KeyAskedFlag(person)) && runtime.GetQuestStatus(quest) == QuestStatus.Completed;
        }

        /// <summary>How many of the four dormmates have been asked about the key.</summary>
        public static int DormmatesAsked(GameEventRuntime runtime)
        {
            int asked = 0;
            if (runtime != null)
                foreach ((string person, _) in Dormmates) if (runtime.GetFlag(KeyAskedFlag(person))) asked++;
            return asked;
        }

        /// <summary>
        /// True once the player has been to the dorm, every Chapter 1 puzzle is solved and all four dormmates have been
        /// asked about the key.
        /// </summary>
        public static bool ShouldThinkRoomIsReady(GameEventRuntime runtime)
        {
            if (runtime == null || !runtime.GetFlag(NotReadyFlag) || runtime.GetFlag(ThoughtFlag)) return false;
            foreach (string quest in ChapterQuestIds)
                if (runtime.GetQuestStatus(quest) != QuestStatus.Completed) return false;
            return DormmatesAsked(runtime) == Dormmates.Length;
        }

        /// <summary>
        /// The dormmate to ask about the key once <paramref name="session"/> has ended, or null: a dormmate who spoke in it
        /// (or whose conversation it was) and should now be asked. This is how the question follows straight on from
        /// the end of their last favour, including Cinna Cirra's, which ends in the ice trial rather than in a conversation.
        /// </summary>
        public static string KeyAskAfter(GameEventRuntime runtime, DialogueSession session)
        {
            DialogueDefinition dialogue = session?.Definition;
            if (runtime == null || dialogue == null || !session.IsCompleted) return null;
            if (dialogue.id != null && dialogue.id.StartsWith(KeyAskDialoguePrefix, StringComparison.Ordinal)) return null;
            string owner = NpcIntroductions.PersonId(dialogue.metadata.GetValue("npcId"));
            foreach ((string person, string speaker) in DormmateSpeakers)
            {
                if (!ShouldAskAboutKey(runtime, person)) continue;
                if (owner == person) return person;
                foreach (DialogueNodeDefinition node in dialogue.nodes ?? Array.Empty<DialogueNodeDefinition>())
                    if (node != null && string.Equals(node.speaker, speaker, StringComparison.OrdinalIgnoreCase)) return person;
            }
            return null;
        }

        private void Update()
        {
            GameEventRuntime runtime = GameEventManager.Instance != null ? GameEventManager.Instance.Runtime : null;
            WatchKeyHunt(runtime);
            if (runtime == null) return;

            // A dormmate's wrap-up has just ended: ask them about the key, unless something else has the screen.
            if (pendingKeyAsk != null && IsQuiet(runtime))
            {
                string person = pendingKeyAsk;
                pendingKeyAsk = null;
                if (ShouldAskAboutKey(runtime, person)) runtime.StartDialogue(KeyAskDialogueId(person));
            }

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

        private void OnDestroy() => WatchKeyHunt(null);

        // New Game and loading rebuild the runtime, so the watch moves to each new one.
        private void WatchKeyHunt(GameEventRuntime runtime)
        {
            if (runtime == keyRuntime) return;
            if (keyRuntime != null) keyRuntime.DialogueEnded -= OnDialogueEnded;
            keyRuntime = runtime;
            pendingKeyAsk = null;
            if (keyRuntime != null) keyRuntime.DialogueEnded += OnDialogueEnded;
        }

        // Straight on in the same dialogue box when nothing else has started; otherwise once the screen is quiet.
        private void OnDialogueEnded(DialogueSession session)
        {
            string person = KeyAskAfter(keyRuntime, session);
            if (person == null) return;
            if (keyRuntime.ActiveDialogue == null && !CutscenePlayer.IsPlaying && keyRuntime.StartDialogue(KeyAskDialogueId(person)) != null) return;
            pendingKeyAsk = person;
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
