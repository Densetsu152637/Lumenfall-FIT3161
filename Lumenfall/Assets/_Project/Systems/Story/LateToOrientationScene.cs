using Lumenfall.Cutscenes;
using Lumenfall.DataDriven;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Startup;
using UnityEngine;

namespace Lumenfall.Story
{
    /// <summary>
    /// The walk from the forest to the academy gate. Until it has played, Prof. Kepler waits by the west-forest gate
    /// (spawned from their NPC catalog record). The first time the player reaches the trigger row, the cutscene
    /// <see cref="CutsceneId"/> plays: Kepler calls from the gate, the player is walked up to them, gives their name and
    /// is walked through the gate onto campus. Its flag step sets <see cref="MetFlag"/>, after which this Kepler is
    /// removed for good; the character is meant to be reused elsewhere later with their own placement.
    /// </summary>
    public sealed class LateToOrientationScene : MonoBehaviour
    {
        public const string MapId = "westForest";
        public const string KeplerNpcId = "prof_kepler";
        public const string CutsceneId = "late_to_orientation";
        public const string MetFlag = "story.orientation.kepler_met";

        /// <summary>
        /// The scene starts the first time the player stands on this row or beyond it (y 22, around (18,22)) in the
        /// forest. The gate is further on, so the row cannot be walked around.
        /// </summary>
        public static readonly Vector2Int TriggerCell = new Vector2Int(18, 22);

        private static LateToOrientationScene instance;

        private readonly StoryNpc kepler = new StoryNpc(KeplerNpcId);
        private GameObject failedArea;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            GameObject host = new GameObject("[Late To Orientation]");
            instance = host.AddComponent<LateToOrientationScene>();
            DontDestroyOnLoad(host);
        }

        /// <summary>True once the player is on or past the trigger row.</summary>
        public static bool HasReachedTrigger(Vector2Int cell) => cell.y >= TriggerCell.y;

        private void Update()
        {
            DataDrivenWorldLoader loader = DataDrivenWorldLoader.Instance;
            GameEventRuntime runtime = GameEventManager.Instance?.Runtime;
            if (loader == null || runtime == null) return;

            // Kepler waits until the scene has played, and stays while it walks the player through the gate.
            bool met = runtime.GetFlag(MetFlag);
            kepler.Keep(!met || CutscenePlayer.CurrentId == CutsceneId);
            if (met || !kepler.IsPresent || failedArea == loader.CurrentAreaRoot) return;
            if (CutscenePlayer.IsPlaying || StartupMenu.IsOpen || Time.timeScale <= 0f || runtime.ActiveDialogue != null) return;

            // The step onto the row counts as reaching it, so the scene starts at once; the cutscene halts the player.
            GridMover player = StoryNpc.Player();
            if (player == null || !(HasReachedTrigger(player.CurrentCell) || HasReachedTrigger(player.DestinationCell))) return;
            // A missing or broken cutscene would otherwise be retried every frame.
            if (!CutscenePlayer.Play(CutsceneId)) failedArea = loader.CurrentAreaRoot;
        }
    }
}
