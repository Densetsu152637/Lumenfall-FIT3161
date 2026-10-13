using System;
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
    /// (spawned here from his NPC catalog record). The first time the player reaches the trigger row, the cutscene
    /// <see cref="CutsceneId"/> plays: Kepler calls from the gate, the player is walked up to him, gives their name and
    /// is walked through the gate onto campus. Its flag step sets <see cref="MetFlag"/>, after which this Kepler is
    /// removed for good; the character is meant to be reused elsewhere later with his own placement.
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

        private GameObject kepler;
        private GameObject keplerArea;
        private GameObject failedArea;
        private bool warnedMissingRecord;

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

            if (runtime.GetFlag(MetFlag))
            {
                // Kepler stays while the cutscene walks the player through the gate, then leaves for good.
                if (kepler != null && !CutscenePlayer.IsPlaying) RemoveKepler();
                return;
            }

            // Saves are loaded while the startup menu is open, and a save may have the player standing on Kepler's cell.
            // His blocker would make that placement fail and the whole load be rejected, so he only exists during play.
            if (StartupMenu.IsOpen)
            {
                RemoveKepler();
                return;
            }

            bool inForest = loader.LoadedMap != null && string.Equals(loader.LoadedMap.id, MapId, StringComparison.OrdinalIgnoreCase);
            if (!inForest) return;
            EnsureKepler(loader);
            if (kepler == null || failedArea == loader.CurrentAreaRoot) return;
            if (CutscenePlayer.IsPlaying || StartupMenu.IsOpen || Time.timeScale <= 0f || runtime.ActiveDialogue != null) return;

            GridMover player = Player();
            if (player == null || player.IsMoving || !HasReachedTrigger(player.CurrentCell)) return;
            // A missing or broken cutscene would otherwise be retried every frame.
            if (!CutscenePlayer.Play(CutsceneId)) failedArea = loader.CurrentAreaRoot;
        }

        private void EnsureKepler(DataDrivenWorldLoader loader)
        {
            GameObject area = loader.CurrentAreaRoot;
            if (area == null || (kepler != null && keplerArea == area)) return;
            RemoveKepler();

            NpcDefinition record = Array.Find(loader.LoadedNpcs?.npcs ?? Array.Empty<NpcDefinition>(),
                npc => npc != null && npc.id == KeplerNpcId);
            GridWorld world = Player()?.World ?? FindAnyObjectByType<GridWorld>();
            if (record == null || record.prefab == null || world == null)
            {
                if (!warnedMissingRecord) Debug.LogError($"The NPC catalog has no '{KeplerNpcId}' record with a prefab, so the gate scene cannot play.", this);
                warnedMissingRecord = true;
                return;
            }

            // A save can leave the player standing where Kepler waits; he appears once they have stepped off.
            Vector2Int cell = new Vector2Int(record.x, record.y);
            GridMover player = Player();
            if (player != null && (player.CurrentCell == cell || player.DestinationCell == cell)) return;

            // A child of the forest's area object, so he is switched off (with his blocker) whenever another area is loaded.
            kepler = Instantiate(record.prefab, area.transform);
            kepler.name = record.displayName;
            kepler.transform.position = world.CellToWorld(cell);
            kepler.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            kepler.AddComponent<DynamicNpc>().Configure(record, null);
            GameObject blocker = new GameObject(record.displayName + " obstacle");
            blocker.transform.SetParent(kepler.transform, false);
            blocker.AddComponent<GridObstacle>().Configure(world, Vector2Int.one);
            keplerArea = area;
        }

        private void RemoveKepler()
        {
            if (kepler != null) Destroy(kepler);
            kepler = null;
            keplerArea = null;
        }

        private static GridMover Player()
        {
            GridPlayerInput input = FindAnyObjectByType<GridPlayerInput>();
            return input == null ? null : input.GetComponent<GridMover>();
        }
    }
}
