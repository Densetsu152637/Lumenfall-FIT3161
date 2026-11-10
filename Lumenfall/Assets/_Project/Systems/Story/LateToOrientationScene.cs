using Lumenfall.Cutscenes;
using Lumenfall.DataDriven;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Startup;
using UnityEngine;

namespace Lumenfall.Story
{
    /// <summary>
    /// The academy gate, at the end of the walk through the dark forest. Until the scene has played, Prof. Kepler waits by
    /// the west-forest gate to campus (spawned from their NPC catalog record). The first time the player comes near the
    /// gate (<see cref="HasReachedTrigger"/>), the cutscene <see cref="CutsceneId"/> plays: Kepler calls the player over,
    /// tells them they have missed orientation and asks for their letter of admission, which they do not have. Right then
    /// a late student runs up the trail, trips, and their letter blows straight into the player's hands. Kepler cannot read
    /// the name on it, so the player gives theirs, and is hurried through the gate. Its flag step sets <see cref="MetFlag"/>, after which this Kepler is gone, and the late student is left
    /// outside the gate, crying. The late student is a story extra (<see cref="GateCast"/>), spawned through
    /// <see cref="StoryNpc"/> from a record made here, so they have no introduction and no journal entry.
    /// </summary>
    public sealed class LateToOrientationScene : MonoBehaviour
    {
        public const string MapId = "westForest";
        public const string CutsceneId = "late_to_orientation";
        public const string MetFlag = "story.orientation.kepler_met";
        /// <summary>Set by the cutscene once the player has no letter: the late student appears down the trail and runs for the gate.</summary>
        public const string LateStudentRunsFlag = "story.gate.late_student_runs";

        public const string KeplerNpcId = "prof_kepler";
        public const string LateStudentId = "gate_late_student";
        public const string LateStudentAfterId = "gate_late_student_after";
        public const string LateStudentName = "Late Student";
        public const string LateStudentChatDialogue = "gate_late_student_after";

        /// <summary>The middle of the gate to campus (portal P.F03.A01, three cells wide from x 16).</summary>
        public static readonly Vector2Int GateCell = new Vector2Int(17, 48);
        /// <summary>Where the player is walked to, as an offset from Kepler (who stands at (19,46)): two cells in front.</summary>
        public static readonly Vector2Int BeforeKepler = new Vector2Int(0, -2);
        /// <summary>Down the trail, out of sight from the gate, where the late student starts running.</summary>
        public static readonly Vector2Int LateStudentStart = new Vector2Int(16, 28);
        /// <summary>Beside the gate, where the late student is left afterwards.</summary>
        public static readonly Vector2Int LateStudentAfterCell = new Vector2Int(15, 47);

        /// <summary>
        /// The scene starts the first time the player comes this close to the gate, where the wisp leads them: this row or
        /// beyond it, anywhere across the map, since the ground between the trees is open and the forest's other campus
        /// portals are on these rows too.
        /// </summary>
        public const int TriggerRow = 41;

        private static LateToOrientationScene instance;

        private readonly StoryNpc kepler = new StoryNpc(KeplerNpcId);
        private StoryNpc lateRunner, lateAfter;
        private GameObject failedArea;
        private GameObject sadPosed;

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

        /// <summary>True once the player is near enough the gate for the scene to start.</summary>
        public static bool HasReachedTrigger(Vector2Int cell) => cell.y >= TriggerRow;

        /// <summary>A gate extra's record: one of the background models, standing at a cell on the west forest.</summary>
        internal static NpcDefinition Record(string id, string name, GameObject prefab, Vector2Int cell, string dialogue = null) => new NpcDefinition
        {
            id = id,
            displayName = name,
            prefab = prefab,
            mapId = MapId,
            x = cell.x,
            y = cell.y,
            dialogueIds = string.IsNullOrEmpty(dialogue) ? new string[0] : new[] { dialogue }
        };

        private void Awake()
        {
            GateCast cast = GateCast.Load();
            if (cast == null) Debug.LogError("Resources/GateCast is missing, so the late student cannot run for the gate.");
            lateRunner = new StoryNpc(Record(LateStudentId, LateStudentName, cast?.lateStudent, LateStudentStart), 0f);
            lateAfter = new StoryNpc(Record(LateStudentAfterId, LateStudentName, cast?.lateStudent, LateStudentAfterCell, LateStudentChatDialogue), 0f);
        }

        private void Update()
        {
            DataDrivenWorldLoader loader = DataDrivenWorldLoader.Instance;
            GameEventRuntime runtime = GameEventManager.Instance?.Runtime;
            if (loader == null || runtime == null) return;

            bool met = runtime.GetFlag(MetFlag);
            bool playing = CutscenePlayer.CurrentId == CutsceneId;
            // Kepler waits until the scene has played, and stays while it sends the player through the gate.
            kepler.Keep(!met || playing);
            lateRunner.Keep(playing && runtime.GetFlag(LateStudentRunsFlag));
            lateAfter.Keep(met && !playing);
            KeepCrying();

            if (met || !kepler.IsPresent || failedArea == loader.CurrentAreaRoot) return;
            if (CutscenePlayer.IsPlaying || StartupMenu.IsOpen || Time.timeScale <= 0f || runtime.ActiveDialogue != null) return;

            // The step onto the row counts as reaching it, so the scene starts at once; the cutscene halts the player.
            GridMover player = StoryNpc.Player();
            if (player == null || !(HasReachedTrigger(player.CurrentCell) || HasReachedTrigger(player.DestinationCell))) return;
            // A missing or broken cutscene would otherwise be retried every frame.
            if (!CutscenePlayer.Play(CutsceneId)) failedArea = loader.CurrentAreaRoot;
        }

        // Left outside, the late student sobs at the gate rather than idling and waving.
        private void KeepCrying()
        {
            GameObject crying = lateAfter.Instance;
            if (crying == null || crying == sadPosed) return;
            sadPosed = crying;
            NpcAmbientBehaviour ambient = crying.GetComponent<NpcAmbientBehaviour>();
            if (ambient != null) ambient.enabled = false;
            Animator animator = crying.GetComponentInChildren<Animator>();
            int sad = Animator.StringToHash("Sad");
            if (animator != null && animator.runtimeAnimatorController != null && animator.HasState(0, sad)) animator.Play(sad, 0, Random.value);
        }
    }
}
