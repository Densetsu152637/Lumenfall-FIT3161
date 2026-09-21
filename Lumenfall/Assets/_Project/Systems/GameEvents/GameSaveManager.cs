using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Lumenfall.GameEvents
{
    /// <summary>Captures the event runtime and registered world-state providers into versioned save slots.</summary>
    [DefaultExecutionOrder(-9990)]
    [DisallowMultipleComponent]
    public sealed class GameSaveManager : MonoBehaviour
    {
        private const int CurrentSchemaVersion = 1;
        private const string DefaultSlot = "save-1";
        private static GameSaveManager instance;
        private readonly Dictionary<string, ISaveStateProvider> providers =
            new Dictionary<string, ISaveStateProvider>(StringComparer.Ordinal);

        public static GameSaveManager Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindAnyObjectByType<GameSaveManager>();
                }

                if (instance == null)
                {
                    GameObject managerObject = new GameObject("[GameSaveManager]");
                    instance = managerObject.AddComponent<GameSaveManager>();
                }

                return instance;
            }
        }

        public event Action<string> SaveCompleted;
        public event Action<string> LoadCompleted;
        public event Action<string> SaveFailed;

        public string SaveDirectory => Application.persistentDataPath;

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
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        /// <summary>Registers a provider. The key is the stable namespace in the save file.</summary>
        public bool RegisterProvider(ISaveStateProvider provider)
        {
            if (provider == null || string.IsNullOrWhiteSpace(provider.SaveKey))
            {
                Debug.LogError("Cannot register a save provider without a key.");
                return false;
            }

            if (providers.ContainsKey(provider.SaveKey))
            {
                Debug.LogError($"A save provider with key '{provider.SaveKey}' is already registered.");
                return false;
            }

            providers.Add(provider.SaveKey, provider);
            return true;
        }

        public bool UnregisterProvider(string key)
        {
            return !string.IsNullOrWhiteSpace(key) && providers.Remove(key);
        }

        public bool Save(string slot = DefaultSlot)
        {
            if (!TryGetSlotPath(slot, out string path, out string error))
            {
                return Fail(error);
            }

            string temporaryPath = path + ".tmp";
            try
            {
                SaveFile file = new SaveFile
                {
                    schemaVersion = CurrentSchemaVersion,
                    savedAtUtc = DateTime.UtcNow.ToString("O"),
                    eventStateJson = GameEventManager.Instance.Runtime.ExportStateJson(false),
                    providers = CaptureProviders()
                };
                Directory.CreateDirectory(SaveDirectory);
                File.WriteAllText(temporaryPath, JsonUtility.ToJson(file, true), Encoding.UTF8);
                if (File.Exists(path))
                {
                    File.Replace(temporaryPath, path, null);
                }
                else
                {
                    File.Move(temporaryPath, path);
                }

                SaveCompleted?.Invoke(slot);
                return true;
            }
            catch (Exception exception)
            {
                TryDelete(temporaryPath);
                return Fail($"Could not save slot '{slot}': {exception.Message}");
            }
        }

        public bool Load(string slot = DefaultSlot)
        {
            if (!TryGetSlotPath(slot, out string path, out string error))
            {
                return Fail(error);
            }

            if (!File.Exists(path))
            {
                return Fail($"Save slot '{slot}' does not exist.");
            }

            SaveFile file;
            try
            {
                file = JsonUtility.FromJson<SaveFile>(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (Exception exception)
            {
                return Fail($"Could not read save slot '{slot}': {exception.Message}");
            }

            if (file == null || file.schemaVersion != CurrentSchemaVersion ||
                string.IsNullOrWhiteSpace(file.eventStateJson))
            {
                return Fail($"Save slot '{slot}' has an unsupported or invalid schema.");
            }

            GameEventRuntime runtime = GameEventManager.Instance.Runtime;
            string previousEventState = runtime.ExportStateJson(false);
            ProviderState[] previousProviderStates;
            try
            {
                previousProviderStates = CaptureProviders();
            }
            catch (Exception exception)
            {
                return Fail($"Could not snapshot current state before loading: {exception.Message}");
            }

            if (!runtime.ImportStateJson(file.eventStateJson))
            {
                return Fail($"Save slot '{slot}' contains invalid game-event state.");
            }

            foreach (ProviderState state in file.providers ?? Array.Empty<ProviderState>())
            {
                if (state == null || string.IsNullOrWhiteSpace(state.key) || !providers.TryGetValue(state.key, out ISaveStateProvider provider))
                {
                    continue;
                }

                bool restored;
                try
                {
                    restored = provider.RestoreStateJson(state.payload ?? string.Empty);
                }
                catch (Exception exception)
                {
                    Rollback(runtime, previousEventState, previousProviderStates);
                    return Fail($"Save provider '{state.key}' could not restore its state: {exception.Message}");
                }

                if (!restored)
                {
                    Rollback(runtime, previousEventState, previousProviderStates);
                    return Fail($"Save provider '{state.key}' rejected its state.");
                }
            }

            LoadCompleted?.Invoke(slot);
            return true;
        }

        private void Rollback(GameEventRuntime runtime, string eventState, ProviderState[] providerStates)
        {
            if (!runtime.ImportStateJson(eventState))
            {
                Debug.LogError("Could not roll back game-event state after a failed load.");
            }

            foreach (ProviderState state in providerStates ?? Array.Empty<ProviderState>())
            {
                if (state == null || !providers.TryGetValue(state.key, out ISaveStateProvider provider)) continue;
                try
                {
                    if (!provider.RestoreStateJson(state.payload ?? string.Empty))
                    {
                        Debug.LogError($"Save provider '{state.key}' rejected rollback state.");
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogError($"Save provider '{state.key}' could not roll back: {exception.Message}");
                }
            }
        }

        public bool HasSave(string slot = DefaultSlot)
        {
            return TryGetSlotPath(slot, out string path, out _) && File.Exists(path);
        }

        public bool DeleteSave(string slot = DefaultSlot)
        {
            if (!TryGetSlotPath(slot, out string path, out string error))
            {
                return Fail(error);
            }

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                return true;
            }
            catch (Exception exception)
            {
                return Fail($"Could not delete save slot '{slot}': {exception.Message}");
            }
        }

        private ProviderState[] CaptureProviders()
        {
            List<string> keys = new List<string>(providers.Keys);
            keys.Sort(StringComparer.Ordinal);
            ProviderState[] states = new ProviderState[keys.Count];
            for (int i = 0; i < keys.Count; i++)
            {
                ISaveStateProvider provider = providers[keys[i]];
                states[i] = new ProviderState { key = keys[i], payload = provider.CaptureStateJson() ?? string.Empty };
            }

            return states;
        }

        private bool TryGetSlotPath(string slot, out string path, out string error)
        {
            path = null;
            error = null;
            if (string.IsNullOrWhiteSpace(slot) || slot.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                slot.Contains("..") || slot.Contains("/") || slot.Contains("\\"))
            {
                error = "Save slot names must be non-empty file names without path separators.";
                return false;
            }

            path = Path.Combine(SaveDirectory, slot + ".json");
            return true;
        }

        private bool Fail(string message)
        {
            Debug.LogError(message);
            SaveFailed?.Invoke(message);
            return false;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not clean up temporary save file: {exception.Message}");
            }
        }

        [Serializable]
        private sealed class SaveFile
        {
            public int schemaVersion;
            public string savedAtUtc;
            public string eventStateJson;
            public ProviderState[] providers = Array.Empty<ProviderState>();
        }

        [Serializable]
        private sealed class ProviderState
        {
            public string key;
            public string payload;
        }
    }

    /// <summary>Implemented by systems that own mutable world state outside the event runtime.</summary>
    public interface ISaveStateProvider
    {
        string SaveKey { get; }
        string CaptureStateJson();
        bool RestoreStateJson(string json);
    }
}
