using System;
using Lumenfall.Controls;
using Lumenfall.Cutscenes;
using Lumenfall.DataDriven;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Inventory;
using Lumenfall.Quests;
using Lumenfall.Startup;
using UnityEngine;

namespace Lumenfall.Story
{
    /// <summary>
    /// Arriving on campus. Once Kepler has sent the player through the forest gate, Kit (the student at the campus
    /// entrance) calls them over (<see cref="CallCutsceneId"/>). The player walks over themselves, and the first time they
    /// talk to Kit, Kit gives them the lay of the land (<see cref="TourCutsceneId"/>) instead of Kit's usual chat: they
    /// missed the orientation ceremony and should see the headmistress, Prof. Bann, by the fountain straight away. After
    /// that, talking to Kit opens Kit's usual choice-based conversation again. Bann waits there from the moment the
    /// player reaches campus; she is spawned from her NPC catalog record, like Kepler.
    /// </summary>
    // Runs before NPC input, so the talk key starts the tour before the same press could open Kit's usual chat.
    [DefaultExecutionOrder(-400)]
    public sealed class LayOfTheLandScene : MonoBehaviour
    {
        public const string MapId = "campus";
        public const string KitNpcId = "campus_student";
        public const string BannNpcId = "prof_bann";
        public const string CallCutsceneId = "kit_call";
        public const string TourCutsceneId = "kit_lay_of_the_land";
        public const string CalledFlag = "story.orientation.kit_called";
        public const string MetFlag = "story.orientation.kit_met";

        /// <summary>The distance within which the talk key reaches an NPC, matching <see cref="DynamicNpc"/>.</summary>
        public const float TalkRange = 2.01f;

        private static LayOfTheLandScene instance;

        private readonly StoryNpc bann = new StoryNpc(BannNpcId);
        private GameObject failedArea;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            GameObject host = new GameObject("[Lay Of The Land]");
            instance = host.AddComponent<LayOfTheLandScene>();
            DontDestroyOnLoad(host);
        }

        /// <summary>Kit calls out once the player has come through the gate.</summary>
        public static bool ShouldCall(GameEventRuntime runtime) =>
            runtime != null && runtime.GetFlag(LateToOrientationScene.MetFlag) && !runtime.GetFlag(CalledFlag);

        /// <summary>After the call, Kit gives the tour the first time the player talks to them.</summary>
        public static bool ShouldGiveTour(GameEventRuntime runtime) =>
            runtime != null && runtime.GetFlag(CalledFlag) && !runtime.GetFlag(MetFlag);


        private void Update()
        {
            DataDrivenWorldLoader loader = DataDrivenWorldLoader.Instance;
            GameEventRuntime runtime = GameEventManager.Instance?.Runtime;
            if (loader == null || runtime == null) return;

            // The headmistress is by the fountain once the player has reached campus.
            bann.Keep(runtime.GetFlag(LateToOrientationScene.MetFlag));

            bool call = ShouldCall(runtime);
            bool tour = !call && ShouldGiveTour(runtime);
            if ((!call && !tour) || failedArea == loader.CurrentAreaRoot) return;
            bool onCampus = loader.LoadedMap != null && string.Equals(loader.LoadedMap.id, MapId, StringComparison.OrdinalIgnoreCase);
            if (!onCampus || CutscenePlayer.IsPlaying || StartupMenu.IsOpen || Time.timeScale <= 0f || runtime.ActiveDialogue != null) return;

            GridMover player = StoryNpc.Player();
            if (player == null) return;
            if (tour && !IsTalkingToKit(player)) return;
            if (call && player.IsMoving) return;

            // A missing or broken cutscene would otherwise be retried every frame.
            if (!CutscenePlayer.Play(call ? CallCutsceneId : TourCutsceneId)) failedArea = loader.CurrentAreaRoot;
        }

        // The same press, range and blockers as DynamicNpc's talk, so Kit's prompt behaves exactly as it does for any NPC.
        private static bool IsTalkingToKit(GridMover player)
        {
            if (!KeyBindings.WasPressed(GameAction.Talk) || QuestHintOverlay.IsSettingsOpen || InventoryUiState.IsOpen ||
                DialogueOverlay.IsNpcInteractionBlocked || BuildingSignInteraction.IsEInteractionBlocked ||
                GridPortal.IsPlayerAtInteractivePortal) return false;
            DynamicNpc nearest = null;
            float best = TalkRange;
            foreach (DynamicNpc npc in FindObjectsByType<DynamicNpc>(FindObjectsInactive.Exclude))
            {
                float distance = Vector3.Distance(player.transform.position, npc.transform.position);
                if (distance <= best) { best = distance; nearest = npc; }
            }
            return nearest != null && nearest.Id == KitNpcId;
        }
    }
}
