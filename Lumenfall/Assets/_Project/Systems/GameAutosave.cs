using System.Collections;
using UnityEngine;
using Lumenfall.GridMovement;
using Lumenfall.Spells;

namespace Lumenfall.GameEvents
{
    /// <summary>Debounces autosaves after meaningful game-state changes and flushes on suspend/quit.</summary>
    [DefaultExecutionOrder(-9980)]
    [DisallowMultipleComponent]
    public sealed class GameAutosave : MonoBehaviour
    {
        private const float DebounceSeconds = 0.75f;
        private static GameAutosave instance;
        private GameEventRuntime subscribedRuntime;
        private WorldStateService subscribedWorldState;
        private GridMover subscribedPlayer;
        private GameSaveManager saveManager;
        private Coroutine pendingSave;
        private bool saving;

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
        }

        private void Update()
        {
            GameEventRuntime currentRuntime = GameEventManager.Instance.Runtime;
            if (currentRuntime != subscribedRuntime)
            {
                UnsubscribeRuntime();
                subscribedRuntime = currentRuntime;
                SubscribeRuntime();
            }

            WorldStateService currentWorldState = WorldStateService.Instance;
            if (currentWorldState != subscribedWorldState)
            {
                UnsubscribeWorldState();
                subscribedWorldState = currentWorldState;
                if (subscribedWorldState != null)
                    subscribedWorldState.StateChanged += OnWorldStateChanged;
            }

            GridMover currentPlayer = FindAnyObjectByType<GridMover>();
            if (currentPlayer != subscribedPlayer)
            {
                UnsubscribePlayer();
                subscribedPlayer = currentPlayer;
                if (subscribedPlayer != null)
                {
                    subscribedPlayer.MoveCompleted += OnPlayerMoved;
                    subscribedPlayer.Teleported += OnPlayerMoved;
                }
            }
        }

        private void OnDestroy()
        {
            UnsubscribeRuntime();
            UnsubscribeWorldState();
            UnsubscribePlayer();
            if (saveManager != null) saveManager.LoadCompleted -= OnLoadCompleted;
            if (instance == this) instance = null;
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) FlushPendingSave();
        }

        private void OnApplicationQuit()
        {
            FlushPendingSave();
        }

        private void SubscribeRuntime()
        {
            if (subscribedRuntime == null) return;
            subscribedRuntime.EventTriggered += OnEventTriggered;
            subscribedRuntime.FlagChanged += OnFlagChanged;
            subscribedRuntime.CounterChanged += OnCounterChanged;
            subscribedRuntime.QuestStatusChanged += OnQuestStatusChanged;
        }

        private void OnEventTriggered(GameEventDefinition _) => ScheduleSave();
        private void OnFlagChanged(string _, bool __) => ScheduleSave();
        private void OnCounterChanged(string _, int __) => ScheduleSave();
        private void OnQuestStatusChanged(QuestDefinition _, QuestStatus __, QuestStatus ___) => ScheduleSave();
        private void OnWorldStateChanged(MapStateSnapshot _) => ScheduleSave();
        private void OnPlayerMoved(GridMover _, Vector2Int __) => ScheduleSave();

        private void OnLoadCompleted(string _)
        {
            if (pendingSave == null) return;
            StopCoroutine(pendingSave);
            pendingSave = null;
        }

        private void ScheduleSave()
        {
            if (saving || !CanAutosave()) return;
            if (pendingSave != null) StopCoroutine(pendingSave);
            pendingSave = StartCoroutine(SaveAfterDebounce());
        }

        private IEnumerator SaveAfterDebounce()
        {
            yield return new WaitForSecondsRealtime(DebounceSeconds);
            pendingSave = null;
            SaveCurrentSlot();
        }

        private void FlushPendingSave()
        {
            if (pendingSave == null) return;
            StopCoroutine(pendingSave);
            pendingSave = null;
            SaveCurrentSlot();
        }

        private void SaveCurrentSlot()
        {
            if (saving || !CanAutosave()) return;
            saving = true;
            try
            {
                GameSaveOverlay.NotifySaveStarted();
                saveManager.Save(GameSaveOverlay.CurrentSlot);
            }
            finally
            {
                saving = false;
            }
        }

        private bool CanAutosave()
        {
            return saveManager != null && saveManager.HasSave(GameSaveOverlay.CurrentSlot);
        }

        private void UnsubscribeRuntime()
        {
            if (subscribedRuntime == null) return;
            subscribedRuntime.EventTriggered -= OnEventTriggered;
            subscribedRuntime.FlagChanged -= OnFlagChanged;
            subscribedRuntime.CounterChanged -= OnCounterChanged;
            subscribedRuntime.QuestStatusChanged -= OnQuestStatusChanged;
            subscribedRuntime = null;
        }

        private void UnsubscribeWorldState()
        {
            if (subscribedWorldState == null) return;
            subscribedWorldState.StateChanged -= OnWorldStateChanged;
            subscribedWorldState = null;
        }

        private void UnsubscribePlayer()
        {
            if (subscribedPlayer == null) return;
            subscribedPlayer.MoveCompleted -= OnPlayerMoved;
            subscribedPlayer.Teleported -= OnPlayerMoved;
            subscribedPlayer = null;
        }
    }
}
