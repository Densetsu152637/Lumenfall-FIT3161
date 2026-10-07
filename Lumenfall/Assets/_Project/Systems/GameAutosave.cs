using UnityEngine;
using UnityEngine.InputSystem;
using Lumenfall.Inventory;
using Lumenfall.Quests;
using Lumenfall.Startup;

namespace Lumenfall.GameEvents
{
    /// <summary>Writes periodic autosaves to the dedicated autosave slot.</summary>
    [DefaultExecutionOrder(-9980)]
    [DisallowMultipleComponent]
    public sealed class GameAutosave : MonoBehaviour
    {
        private const float PeriodicSaveSeconds = 30f;
        private static GameAutosave instance;
        private GameSaveManager saveManager;
        private bool saving;
        private bool initialAutosavePending;
        private bool waitingForNewRunToBegin;
        private float nextPeriodicSaveTime;

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
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && Time.timeScale > 0f &&
                FindAnyObjectByType<StartupMenu>() == null && !InventoryUiState.IsOpen &&
                !QuestHintOverlay.IsSettingsOpen && GameEventManager.Instance?.Runtime?.ActiveDialogue == null)
            {
                StartupMenu.ReturnToMainMenu();
            }

            if (waitingForNewRunToBegin && Time.timeScale > 0f && !StartupMenu.IsVisible)
            {
                waitingForNewRunToBegin = false;
                nextPeriodicSaveTime = Time.unscaledTime + PeriodicSaveSeconds;
            }

            if (Time.unscaledTime >= nextPeriodicSaveTime)
            {
                nextPeriodicSaveTime = Time.unscaledTime + PeriodicSaveSeconds;
                if (CanAutosave()) SaveCurrentSlot();
            }
        }

        private void OnDestroy()
        {
            if (saveManager != null) saveManager.LoadCompleted -= OnLoadCompleted;
            if (instance == this) instance = null;
        }

        private void OnLoadCompleted(string _) => RestartTimer();

        /// <summary>Starts a fresh 30-second interval after a load/new game.</summary>
        public static void RestartTimer()
        {
            if (instance == null) return;
            instance.initialAutosavePending = false;
            instance.waitingForNewRunToBegin = false;
            instance.nextPeriodicSaveTime = Time.unscaledTime + PeriodicSaveSeconds;
        }

        /// <summary>Arms the first autosave for 30 seconds after the new game actually enters gameplay.</summary>
        public static void BeginNewRun()
        {
            if (instance == null) return;
            instance.initialAutosavePending = true;
            instance.waitingForNewRunToBegin = true;
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
                autosave.initialAutosavePending = false;
                autosave.waitingForNewRunToBegin = false;
                autosave.nextPeriodicSaveTime = Time.unscaledTime + PeriodicSaveSeconds;
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
                bool wasInitialAutosave = initialAutosavePending;
                bool saved = saveManager.Save(GameSaveManager.AutosaveSlot);
                if (saved && wasInitialAutosave)
                {
                    initialAutosavePending = false;
                    saveManager.MarkPlayed(GameSaveManager.AutosaveSlot);
                }
            }
            finally
            {
                saving = false;
            }
        }

        private bool CanAutosave()
        {
            return saveManager != null && Time.timeScale > 0f && !StartupMenu.IsVisible &&
                (initialAutosavePending || saveManager.HasSave(GameSaveManager.AutosaveSlot));
        }
    }
}
