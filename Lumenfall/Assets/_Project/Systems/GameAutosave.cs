using UnityEngine;
using Lumenfall.Startup;
using Lumenfall.Cutscenes;
using Lumenfall.GridMovement;

namespace Lumenfall.GameEvents
{
    /// <summary>
    /// Writes periodic autosaves to the dedicated autosave slot. The 30-second interval counts only active gameplay
    /// (time running and the startup menu closed), so menus, the Settings pause and the menu's closing transition never
    /// bring a save closer. Nothing is written the moment a game is loaded or started: the first autosave waits for
    /// 30 seconds of real play.
    /// </summary>
    [DefaultExecutionOrder(-9980)]
    [DisallowMultipleComponent]
    public sealed class GameAutosave : MonoBehaviour
    {
        private const float PeriodicSaveSeconds = 30f;
        // Play shorter than this after a load counts as peeking, so Main menu and Quit leave every file untouched.
        private const float PlayedGraceSeconds = 5f;
        private static GameAutosave instance;
        private GameSaveManager saveManager;
        private bool saving;
        // Set after a load or new game; the run's first save then marks autosave as last played.
        private bool initialAutosavePending;
        // A new game's manual slot is written together with the run's first save, not when the game starts.
        private string pendingManualSlot;
        private float secondsUntilSave;
        private float activeSecondsSinceLoad;
        private GridMover player;

        /// <summary>True once the player has had a few seconds of real gameplay since the last load or new game.</summary>
        public static bool HasPlayedSinceLoad => instance == null || instance.activeSecondsSinceLoad >= PlayedGraceSeconds;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (FindAnyObjectByType<GameAutosave>() == null)
            {
                GameObject go = new GameObject("[GameAutosave]");
                go.AddComponent<GameAutosave>();
            }
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
            saveManager = GameSaveManager.Instance;
            saveManager.LoadCompleted += OnLoadCompleted;
            RestartTimer();
        }

        private void Update()
        {
            // Escape during play is handled by QuestHintOverlay, which opens the Settings pause pop-out.
            if (Time.timeScale <= 0f || StartupMenu.IsVisible) return;
            float deltaTime = Time.unscaledDeltaTime;
            activeSecondsSinceLoad += deltaTime;
            secondsUntilSave -= deltaTime;
            if (secondsUntilSave > 0f) return;
            // A save taken mid-step fails validation, so a due save waits for the player to stand still. This runs
            // before movement each frame, so it sees the gap between held steps.
            if (PlayerIsMoving()) return;
            secondsUntilSave = PeriodicSaveSeconds;
            if (CanAutosave()) SaveCurrentSlot();
        }

        private void OnDestroy()
        {
            if (saveManager != null) saveManager.LoadCompleted -= OnLoadCompleted;
            if (instance == this) instance = null;
        }

        private void OnLoadCompleted(string _) => RestartTimer();

        /// <summary>Starts a fresh 30 seconds of active play before the next autosave, after any load.</summary>
        public static void RestartTimer()
        {
            if (instance == null) return;
            instance.initialAutosavePending = false;
            instance.pendingManualSlot = null;
            instance.secondsUntilSave = PeriodicSaveSeconds;
            instance.activeSecondsSinceLoad = 0f;
        }

        /// <summary>
        /// Arms the first save of a run that has just started (a new game, or a loaded manual slot). It happens after 30
        /// seconds of active play, or sooner if something calls <see cref="SaveNow"/>. A new game passes its manual slot,
        /// which is written alongside that first save.
        /// </summary>
        public static void BeginNewRun(string manualSlot = null)
        {
            if (instance == null) return;
            instance.initialAutosavePending = true;
            instance.pendingManualSlot = manualSlot;
            instance.secondsUntilSave = PeriodicSaveSeconds;
            instance.activeSecondsSinceLoad = 0f;
        }

        /// <summary>Writes the current game state to the dedicated autosave slot immediately.</summary>
        public static bool SaveNow()
        {
            GameAutosave autosave = instance;
            GameSaveManager saves = GameSaveManager.Instance;
            GameSaveOverlay.SetCurrentSlot(GameSaveManager.AutosaveSlot);
            GameSaveOverlay.NotifySaveStarted();
            bool succeeded = saves.Save(GameSaveManager.AutosaveSlot);
            if (succeeded) SaveSlotSummary.RequestThumbnailRefresh(GameSaveManager.AutosaveSlot);
            if (autosave != null)
            {
                if (succeeded) autosave.CompleteFirstSave();
                autosave.secondsUntilSave = PeriodicSaveSeconds;
            }
            return succeeded;
        }

        private void SaveCurrentSlot()
        {
            if (saving || !CanAutosave()) return;
            saving = true;
            try
            {
                GameSaveOverlay.SetCurrentSlot(GameSaveManager.AutosaveSlot);
                GameSaveOverlay.NotifySaveStarted();
                if (saveManager.Save(GameSaveManager.AutosaveSlot)) CompleteFirstSave();
            }
            finally
            {
                saving = false;
            }
        }

        /// <summary>After a run's first successful save: mark autosave as last played and write a new game's slot.</summary>
        private void CompleteFirstSave()
        {
            if (initialAutosavePending)
            {
                initialAutosavePending = false;
                saveManager.MarkPlayed(GameSaveManager.AutosaveSlot);
            }
            // Empty, not null, after Unity reloads scripts during Play Mode, which serializes private strings.
            if (string.IsNullOrEmpty(pendingManualSlot)) return;
            string slot = pendingManualSlot;
            pendingManualSlot = null;
            if (saveManager.Save(slot)) SaveSlotSummary.RequestThumbnailRefresh(slot);
        }

        private bool PlayerIsMoving()
        {
            if (player == null)
            {
                GridPlayerInput input = FindAnyObjectByType<GridPlayerInput>();
                player = input == null ? null : input.GetComponent<GridMover>();
            }
            return player != null && player.IsMoving;
        }

        private bool CanAutosave()
        {
            return saveManager != null && saveManager.AutosaveAllowed && Time.timeScale > 0f && !StartupMenu.IsVisible && !CutscenePlayer.IsPlaying &&
                (initialAutosavePending || saveManager.HasSave(GameSaveManager.AutosaveSlot));
        }
    }
}
