using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lumenfall.GameEvents
{
    /// <summary>
    /// Persistent Unity entry point for the global game-event runtime. It is created automatically
    /// before the first scene and loads every JSON TextAsset under Resources/GameEvents.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    public sealed class GameEventManager : MonoBehaviour
    {
        private const string ResourcePath = "GameEvents";
        private static GameEventManager instance;

        public static GameEventManager Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindAnyObjectByType<GameEventManager>();
                }

                if (instance == null)
                {
                    GameObject managerObject = new GameObject("[GameEventManager]");
                    instance = managerObject.AddComponent<GameEventManager>();
                }

                instance.EnsureInitialized();
                return instance;
            }
        }

        public GameEventRuntime Runtime { get; private set; }
        public event Action<GameEventRuntime> RuntimeChanged;
        public GameEventCatalog Catalog => Runtime?.Catalog;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            _ = Instance;
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
            EnsureInitialized();
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        public GameEventTriggerResult TriggerEvent(string eventId)
        {
            EnsureInitialized();
            return Runtime.TriggerEvent(eventId);
        }

        public bool StartQuest(string questId)
        {
            EnsureInitialized();
            return Runtime.StartQuest(questId);
        }

        public bool CompleteQuest(string questId)
        {
            EnsureInitialized();
            return Runtime.CompleteQuest(questId);
        }

        public DialogueSession StartDialogue(string dialogueId)
        {
            EnsureInitialized();
            return Runtime.StartDialogue(dialogueId);
        }

        /// <summary>Rebuilds the catalogue from Resources. Existing runtime state is discarded.</summary>
        public void ReloadDefinitions()
        {
            Runtime = BuildRuntimeFromResources();
            RuntimeChanged?.Invoke(Runtime);
        }

        private void EnsureInitialized()
        {
            if (Runtime == null)
            {
                Runtime = BuildRuntimeFromResources();
                RuntimeChanged?.Invoke(Runtime);
            }
        }

        private static GameEventRuntime BuildRuntimeFromResources()
        {
            TextAsset[] assets = Resources.LoadAll<TextAsset>(ResourcePath);
            Array.Sort(assets, (left, right) => string.CompareOrdinal(left.name, right.name));

            List<GameEventDatabaseDefinition> databases = new List<GameEventDatabaseDefinition>();
            foreach (TextAsset asset in assets)
            {
                if (GameEventCatalog.TryParseJson(asset.text, asset.name, out GameEventDatabaseDefinition database,
                        out string error))
                {
                    databases.Add(database);
                }
                else
                {
                    Debug.LogError(error);
                }
            }

            GameEventCatalog catalog = new GameEventCatalog(databases);
            foreach (CatalogValidationMessage message in catalog.ValidationMessages)
            {
                if (message.Severity == CatalogMessageSeverity.Error)
                {
                    Debug.LogError($"Game event catalogue: {message}");
                }
                else
                {
                    Debug.LogWarning($"Game event catalogue: {message}");
                }
            }

            if (assets.Length == 0)
            {
                Debug.LogWarning(
                    $"No game-event JSON files were found under a Resources/{ResourcePath} folder.");
            }

            GameEventRuntime runtime = new GameEventRuntime(catalog);
            runtime.DiagnosticRaised += message => Debug.LogWarning($"Game event runtime: {message}");
            return runtime;
        }
    }
}
