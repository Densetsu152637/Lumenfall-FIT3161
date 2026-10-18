using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Lumenfall.GameEvents
{
    public enum SaveSlotStatus { Valid, Missing, Corrupt, Truncated, Unsupported, Unavailable }
    public enum SaveRecoverySource { Previous, InterruptedWrite }
    public sealed class SaveSlotInspection
    {
        public SaveSlotStatus Status { get; internal set; }
        public string Message { get; internal set; }
        public int SchemaVersion { get; internal set; }
        public bool CanLoad => Status == SaveSlotStatus.Valid;
    }

    /// <summary>Owns versioned slots, explicit recovery and transactional provider restore.</summary>
    [DefaultExecutionOrder(-9990)]
    [DisallowMultipleComponent]
    public sealed class GameSaveManager : MonoBehaviour
    {
        private const int CurrentSchemaVersion = 2;
        private const int MaximumFileBytes = 8 * 1024 * 1024;
        private const int MaximumProviders = 128;
        public const string AutosaveSlot = "autosave";
        public static readonly string[] ManualSlots = { "save-1", "save-2", "save-3" };
        private static readonly string[] RequiredWorldProviderKeys = { "data-driven-world", "spell-world-state" };
        private const string DefaultSlot = AutosaveSlot;
        private const string LastPlayedFileName = "last-played.json";
        private static GameSaveManager instance;
        private readonly Dictionary<string, ISaveStateProvider> providers = new Dictionary<string, ISaveStateProvider>(StringComparer.Ordinal);
        private readonly Dictionary<string, ProviderState> retainedProviders = new Dictionary<string, ProviderState>(StringComparer.Ordinal);
        private bool autosaveSuspended;
        public static GameSaveManager Instance
        {
            get
            {
                if (instance == null) instance = FindAnyObjectByType<GameSaveManager>();
                if (instance == null) instance = new GameObject("[GameSaveManager]").AddComponent<GameSaveManager>();
                return instance;
            }
        }
        public event Action<string> SaveCompleted;
        public event Action<string> LoadCompleted;
        public event Action<string> SaveFailed;
        public event Action OperationStarted;
        public bool IsBusy { get; private set; }
        public bool AutosaveAllowed => !IsBusy && !autosaveSuspended;
        public bool RequiredWorldProvidersRegistered
        {
            get
            {
                foreach (string key in RequiredWorldProviderKeys) if (!providers.ContainsKey(key)) return false;
                return true;
            }
        }
        public string LastError { get; private set; }
        /// <summary>
        /// True while providers restore a save, so state they apply then can be shown as already settled rather than as a
        /// live change (for example, a restored frozen pool should not replay its freeze).
        /// </summary>
        public bool IsRestoring { get; private set; }
        public string SaveDirectory => Application.persistentDataPath;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)] private static void Bootstrap() { _ = Instance; }
        private void Awake() { if (instance != null && instance != this) { Destroy(gameObject); return; } instance = this; DontDestroyOnLoad(gameObject); }
        private void OnDestroy() { if (instance == this) instance = null; }

        public bool RegisterProvider(ISaveStateProvider provider)
        {
            if (IsBusy || provider == null || string.IsNullOrWhiteSpace(provider.SaveKey)) return false;
            if (providers.TryGetValue(provider.SaveKey, out ISaveStateProvider existing))
            {
                if (ReferenceEquals(existing, provider)) return true;
                // Unity destroys the native object before managed references become null.
                if (!(existing is UnityEngine.Object unityObject) || unityObject != null) return false;
            }
            providers[provider.SaveKey] = provider;
            return true;
        }
        public bool UnregisterProvider(string key) => !IsBusy && !string.IsNullOrWhiteSpace(key) && providers.Remove(key);
        public bool UnregisterProvider(ISaveStateProvider provider) => provider != null && !IsBusy &&
            providers.TryGetValue(provider.SaveKey, out ISaveStateProvider existing) && ReferenceEquals(existing, provider) && providers.Remove(provider.SaveKey);

        /// <summary>Checks exact object syntax and required fields without importing state.</summary>
        public static bool IsValidPayloadObject(string json, params string[] requiredFields) => JsonSyntax.HasFields(json, requiredFields);

        public SaveSlotInspection InspectSlot(string slot = DefaultSlot)
        {
            if (!TryGetSlotPath(slot, out string path, out string error)) return Inspection(SaveSlotStatus.Unavailable, error);
            return ReadValidated(path, out _);
        }
        public SaveRecoverySource[] GetRecoveryCandidates(string slot = DefaultSlot)
        {
            if (!TryGetSlotPath(slot, out string path, out _)) return Array.Empty<SaveRecoverySource>();
            List<SaveRecoverySource> result = new List<SaveRecoverySource>();
            if (ReadValidated(path + ".bak", out _).CanLoad) result.Add(SaveRecoverySource.Previous);
            if (ReadValidated(path + ".tmp", out _).CanLoad) result.Add(SaveRecoverySource.InterruptedWrite);
            return result.ToArray();
        }
        /// <summary>Explicitly promotes a validated candidate, preserving the original and candidate bytes.</summary>
        public bool RecoverSave(string slot, SaveRecoverySource source)
        {
            if (!BeginOperation()) return false;
            try
            {
                autosaveSuspended = true;
                if (!TryGetSlotPath(slot, out string path, out string error)) return Fail(error);
                if (source != SaveRecoverySource.Previous && source != SaveRecoverySource.InterruptedWrite) return Fail("Choose a listed recovery candidate.");
                string candidate = path + (source == SaveRecoverySource.Previous ? ".bak" : ".tmp");
                SaveSlotInspection result = ReadValidated(candidate, out SaveFile file);
                if (!result.CanLoad) return Fail("Recovery candidate is unusable. " + result.Message);
                return RestoreAndCommit(file, slot, false, () =>
                {
                    string promotion = path + ".recovering";
                    try
                    {
                        File.Copy(candidate, promotion, true);
                        if (File.Exists(path)) File.Replace(promotion, path, PreservedPath(path));
                        else File.Move(promotion, path);
                        // Keep interrupted bytes as evidence, but free .tmp for future atomic writes.
                        if (source == SaveRecoverySource.InterruptedWrite)
                            try { File.Move(candidate, PreservedPath(path)); }
                            catch (Exception exception) { Debug.LogWarning("Recovered slot, but interrupted candidate could not be archived: " + exception.Message); }
                    }
                    finally { TryDelete(promotion); }
                });
            }
            catch (Exception exception) { return Fail("Could not recover slot: " + exception.Message); }
            finally { IsBusy = false; }
        }

        public bool Save(string slot = DefaultSlot)
        {
            if (!BeginOperation()) return false;
            string temporaryPath = null;
            try
            {
                if (!TryGetSlotPath(slot, out string path, out string error)) return Fail(error);
                ProviderState[] capturedProviders = CaptureProviders();
                if (IsPlayerSlot(slot) && !ContainsRequiredWorldProviders(capturedProviders))
                    return Fail($"Could not save slot '{slot}': the world save providers are not ready. The existing save was left unchanged.");
                SaveFile file = new SaveFile { schemaVersion = CurrentSchemaVersion, savedAtUtc = DateTime.UtcNow.ToString("O"),
                    eventStateJson = GameEventManager.Instance.Runtime.ExportStateJson(false), providers = capturedProviders };
                file.payloadHash = PayloadHash(file);
                Directory.CreateDirectory(SaveDirectory);
                // An interrupted write is evidence until explicitly recovered or discarded.
                if (File.Exists(path + ".tmp")) return Fail("An interrupted save exists. Inspect recovery candidates before saving this slot.");
                temporaryPath = path + ".tmp";
                using (FileStream stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(file, true));
                    if (bytes.Length > MaximumFileBytes) return Fail("Save exceeds the supported size limit.");
                    stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
                }
                SaveSlotInspection written = ReadValidated(temporaryPath, out _);
                if (!written.CanLoad) return Fail("Captured save is invalid. " + written.Message);
                if (File.Exists(path))
                {
                    bool validPrevious = ReadValidated(path, out _).CanLoad;
                    File.Replace(temporaryPath, path, validPrevious ? path + ".bak" : PreservedPath(path));
                }
                else File.Move(temporaryPath, path);
                temporaryPath = null;
                autosaveSuspended = false;
                LastError = null;
                Notify(SaveCompleted, slot);
                return true;
            }
            catch (Exception exception) { return Fail($"Could not save slot '{slot}': {exception.Message}"); }
            finally { if (temporaryPath != null) TryDelete(temporaryPath); IsBusy = false; }
        }

        public bool Load(string slot = DefaultSlot, bool markPlayed = true)
        {
            if (!BeginOperation()) return false;
            autosaveSuspended = true;
            try
            {
                if (!TryGetSlotPath(slot, out string path, out string error)) return Fail(error);
                SaveSlotInspection result = ReadValidated(path, out SaveFile file);
                if (!result.CanLoad) return Fail($"Slot '{slot}': {result.Message} Use a listed recovery candidate, or choose another slot.");
                return RestoreAndCommit(file, slot, markPlayed, null);
            }
            catch (Exception exception) { return Fail($"Could not load slot '{slot}': {exception.Message}"); }
            finally { IsBusy = false; }
        }

        private bool RestoreAndCommit(SaveFile file, string slot, bool markPlayed, Action commit)
        {
                GameEventRuntime runtime = GameEventManager.Instance.Runtime;
                string previousEvents = runtime.ExportStateJson(false);
                ProviderState[] previousProviders = CaptureProviders();
                Dictionary<string, ProviderState> previousRetained = new Dictionary<string, ProviderState>(retainedProviders, StringComparer.Ordinal);
                try
                {
                    if (!runtime.ImportStateJson(file.eventStateJson)) throw new InvalidDataException("Game-event state was rejected.");
                    ProviderState[] ordered = OrderedStates(file.providers);
                    RestoreProviders(ordered);
                    retainedProviders.Clear();
                    foreach (ProviderState state in ordered) if (!providers.ContainsKey(state.key)) retainedProviders.Add(state.key, state);
                    commit?.Invoke();
                }
                catch (Exception exception)
                {
                    retainedProviders.Clear();
                    foreach (var pair in previousRetained) retainedProviders.Add(pair.Key, pair.Value);
                    bool rolledBack = Rollback(runtime, previousEvents, previousProviders);
                    return Fail($"Could not load slot '{slot}': {exception.Message} " +
                        (rolledBack ? "Previous game state restored; autosave paused until a successful load or explicit save." : "Rollback failed; reload a valid slot before continuing. Autosave is paused."));
                }
                if (markPlayed) WriteLastPlayed(slot);
                autosaveSuspended = false; LastError = null;
                Notify(LoadCompleted, slot);
                return true;
        }

        /// <summary>Guards new-game/reset callbacks. Call Save only after this returns successfully.</summary>
        public bool ResetState(Action reset, bool resumeAutosave = false)
        {
            if (reset == null || !BeginOperation()) return false;
            autosaveSuspended = true;
            try { reset(); retainedProviders.Clear(); autosaveSuspended = !resumeAutosave; return true; }
            catch (Exception exception) { return Fail("Could not reset game state: " + exception.Message); }
            finally { IsBusy = false; }
        }
        /// <summary>Local write time for the menu status line.</summary>
        public bool TryGetSavedAt(string slot, out DateTime savedAt)
        {
            savedAt = default;
            if (!TryGetSlotPath(slot, out string path, out _) || !File.Exists(path)) return false;
            try { savedAt = File.GetLastWriteTime(path); return true; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        public bool HasSave(string slot = DefaultSlot) => TryGetSlotPath(slot, out string path, out _) && File.Exists(path);
        public bool DeleteSave(string slot = DefaultSlot)
        {
            if (!BeginOperation()) return false;
            try
            {
                if (!TryGetSlotPath(slot, out string path, out string error)) return Fail(error);
                foreach (string suffix in new[] { "", ".bak", ".tmp" }) if (File.Exists(path + suffix)) File.Delete(path + suffix);
                return true;
            }
            catch (Exception exception) { return Fail("Could not delete save: " + exception.Message); }
            finally { IsBusy = false; }
        }
        public bool MarkPlayed(string slot)
        {
            if (!BeginOperation()) return false;
            try { if (!InspectSlot(slot).CanLoad) return Fail("Only a valid save can be marked played."); return WriteLastPlayed(slot); }
            finally { IsBusy = false; }
        }
        private bool WriteLastPlayed(string slot)
        {
            string path = Path.Combine(SaveDirectory, LastPlayedFileName), temporaryPath = path + ".tmp";
            try
            {
                Directory.CreateDirectory(SaveDirectory);
                File.WriteAllText(temporaryPath, JsonUtility.ToJson(new LastPlayedMarker { slot = slot, playedAtUtc = DateTime.UtcNow.ToString("O") }), Encoding.UTF8);
                if (File.Exists(path)) File.Replace(temporaryPath, path, null); else File.Move(temporaryPath, path);
                return true;
            }
            catch (Exception exception) { Debug.LogWarning("Could not record last-played slot: " + exception.Message); return false; }
            finally { TryDelete(temporaryPath); }
        }
        public string FindMostRecentSlot(IList<string> slots)
        {
            if (slots == null) return null;
            Dictionary<string, DateTime> times = new Dictionary<string, DateTime>(StringComparer.Ordinal);
            foreach (string slot in slots)
                if (TryGetSlotPath(slot, out string path, out _) && ReadValidated(path, out SaveFile file).CanLoad)
                    times[slot] = DateTime.TryParse(file.savedAtUtc, null, DateTimeStyles.RoundtripKind, out DateTime time) ? time.ToUniversalTime() : File.GetLastWriteTimeUtc(path);
            string marked = null;
            try
            {
                string path = Path.Combine(SaveDirectory, LastPlayedFileName);
                if (File.Exists(path))
                {
                    LastPlayedMarker marker = JsonUtility.FromJson<LastPlayedMarker>(File.ReadAllText(path));
                    if (marker != null && DateTime.TryParse(marker.playedAtUtc, out _) && times.ContainsKey(marker.slot ?? "")) marked = marker.slot;
                }
            }
            catch { /* Invalid markers fall back to validated timestamps. */ }
            return GameSaveSlotPolicy.SelectMostRecent(slots, marked, times.ContainsKey, slot => times[slot]);
        }

        private SaveSlotInspection ReadValidated(string path, out SaveFile file)
        {
            file = null;
            if (!File.Exists(path)) return Inspection(SaveSlotStatus.Missing, "Save file is missing.");
            try
            {
                if (new FileInfo(path).Length > MaximumFileBytes) return Inspection(SaveSlotStatus.Corrupt, "Save exceeds the supported size limit.");
                string json = File.ReadAllText(path, new UTF8Encoding(false, true));
                if (!JsonSyntax.IsObject(json)) return Inspection(JsonSyntax.IsTruncated(json) ? SaveSlotStatus.Truncated : SaveSlotStatus.Corrupt, "Save JSON is corrupt or incomplete.");
                file = JsonUtility.FromJson<SaveFile>(json);
                if (file == null) return Inspection(SaveSlotStatus.Corrupt, "Save envelope is invalid.");
                int sourceVersion = file.schemaVersion;
                if (sourceVersion != 1 && sourceVersion != CurrentSchemaVersion)
                    return Inspection(SaveSlotStatus.Unsupported, $"Schema {sourceVersion} is unsupported; use the game version that wrote it.", sourceVersion);
                if (!ValidateEvents(file.eventStateJson)) return Inspection(SaveSlotStatus.Corrupt, "Game-event payload is invalid.", sourceVersion);
                ProviderState[] states = file.providers ?? Array.Empty<ProviderState>();
                if (states.Length > MaximumProviders) return Inspection(SaveSlotStatus.Corrupt, "Too many provider payloads.", sourceVersion);
                HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (ProviderState state in states)
                    if (state == null || string.IsNullOrWhiteSpace(state.key) || state.key.Length > 128 || !keys.Add(state.key) || !JsonSyntax.IsObject(state.payload))
                        return Inspection(SaveSlotStatus.Corrupt, "Provider keys must be unique and payloads must be JSON objects.", sourceVersion);
                foreach (string required in RequiredWorldProviderKeys)
                    if (providers.ContainsKey(required) && !keys.Contains(required)) return Inspection(SaveSlotStatus.Corrupt, $"Required provider '{required}' is missing.", sourceVersion);
                string mapId = null;
                foreach (ProviderState state in states)
                    if (state.key == "data-driven-world" || state.key == "spell-world-state")
                    {
                        string nextMap = JsonUtility.FromJson<MapIdentity>(state.payload)?.mapId;
                        if (string.IsNullOrWhiteSpace(nextMap) || (mapId != null && mapId != nextMap)) return Inspection(SaveSlotStatus.Corrupt, "Saved map and puzzle-world identities disagree.", sourceVersion);
                        mapId = nextMap;
                    }
                foreach (ProviderState state in states)
                    if (providers.TryGetValue(state.key, out ISaveStateProvider provider) && provider is ISaveStateValidator validator && !validator.ValidateStateJson(state.payload))
                        return Inspection(SaveSlotStatus.Corrupt, $"Provider '{state.key}' payload is invalid.", sourceVersion);
                if (sourceVersion == CurrentSchemaVersion && !string.Equals(file.payloadHash, PayloadHash(file), StringComparison.Ordinal))
                    return Inspection(SaveSlotStatus.Corrupt, "Save integrity check failed.", sourceVersion);
                // v1 payloads retain their exact meaning; no speculative schema-0 migration.
                file.schemaVersion = CurrentSchemaVersion;
                return Inspection(SaveSlotStatus.Valid, sourceVersion == 1 ? "Compatible schema 1 save; upgraded on next save." : "Save is valid.", sourceVersion);
            }
            catch (IOException exception) { return Inspection(SaveSlotStatus.Unavailable, "Save file is unavailable: " + exception.Message); }
            catch (UnauthorizedAccessException exception) { return Inspection(SaveSlotStatus.Unavailable, "Save file is inaccessible: " + exception.Message); }
            catch (Exception) { return Inspection(SaveSlotStatus.Corrupt, "Save could not be decoded."); }
        }
        private static bool ValidateEvents(string json)
        {
            if (!JsonSyntax.IsObject(json)) return false;
            // Required arrays distinguish an actual exported empty state from a permissive {} parse.
            ValidatedEventState state = JsonUtility.FromJson<ValidatedEventState>(json);
            if (state == null || state.flags == null || state.counters == null || state.quests == null || state.triggeredEventIds == null) return false;
            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (FlagStateEntry e in state.flags) if (e == null || string.IsNullOrWhiteSpace(e.key) || !keys.Add(e.key)) return false;
            keys.Clear(); foreach (CounterStateEntry e in state.counters) if (e == null || string.IsNullOrWhiteSpace(e.key) || !keys.Add(e.key)) return false;
            keys.Clear(); foreach (QuestStateEntry e in state.quests)
                if (e == null || string.IsNullOrWhiteSpace(e.questId) || !keys.Add(e.questId) || !Enum.TryParse(e.status, true, out QuestStatus status) || !Enum.IsDefined(typeof(QuestStatus), status)) return false;
            keys.Clear(); foreach (string id in state.triggeredEventIds) if (string.IsNullOrWhiteSpace(id) || !keys.Add(id)) return false;
            return true;
        }
        private ProviderState[] CaptureProviders()
        {
            Dictionary<string, ProviderState> states = new Dictionary<string, ProviderState>(retainedProviders, StringComparer.Ordinal);
            foreach (var pair in providers) states[pair.Key] = new ProviderState { key = pair.Key, payload = pair.Value.CaptureStateJson() };
            return OrderedStates(new List<ProviderState>(states.Values).ToArray());
        }
        private static ProviderState[] OrderedStates(ProviderState[] states)
        {
            ProviderState[] sorted = (ProviderState[])(states ?? Array.Empty<ProviderState>()).Clone();
            Array.Sort(sorted, (a, b) => { int priority = RestorePriority(a.key).CompareTo(RestorePriority(b.key)); return priority != 0 ? priority : string.CompareOrdinal(a.key, b.key); });
            return sorted;
        }
        private static int RestorePriority(string key) => key == "data-driven-world" ? 0 : key == "spell-world-state" ? 1 : 2;
        private static bool IsPlayerSlot(string slot) => string.Equals(slot, AutosaveSlot, StringComparison.Ordinal) || Array.IndexOf(ManualSlots, slot) >= 0;
        private static bool ContainsRequiredWorldProviders(ProviderState[] states)
        {
            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (ProviderState state in states ?? Array.Empty<ProviderState>())
                if (state != null && !string.IsNullOrWhiteSpace(state.key)) keys.Add(state.key);
            foreach (string required in RequiredWorldProviderKeys) if (!keys.Contains(required)) return false;
            return true;
        }
        private void RestoreProviders(ProviderState[] states)
        {
            IsRestoring = true;
            try
            {
            foreach (ProviderState state in states)
                if (providers.TryGetValue(state.key, out ISaveStateProvider provider) && provider is ISaveStateRestorePhases phased && !phased.PrepareRestoreStateJson(state.payload))
                    throw new InvalidDataException($"Provider '{state.key}' could not prepare restore.");
            foreach (ProviderState state in states)
                if (providers.TryGetValue(state.key, out ISaveStateProvider provider) && !(provider is ISaveStateRestorePhases) && !provider.RestoreStateJson(state.payload))
                    throw new InvalidDataException($"Provider '{state.key}' rejected its payload.");
            foreach (ProviderState state in states)
                if (providers.TryGetValue(state.key, out ISaveStateProvider provider) && provider is ISaveStateRestorePhases && !provider.RestoreStateJson(state.payload))
                    throw new InvalidDataException($"Provider '{state.key}' could not finalize restore.");
            }
            finally { IsRestoring = false; }
        }
        private bool Rollback(GameEventRuntime runtime, string events, ProviderState[] states)
        {
            try { if (!runtime.ImportStateJson(events)) return false; RestoreProviders(OrderedStates(states)); return true; }
            catch (Exception exception) { Debug.LogError("Save rollback failed: " + exception.Message); return false; }
        }
        private bool BeginOperation()
        {
            if (IsBusy) { LastError = "A save, load or reset is already in progress."; return false; }
            IsBusy = true;
            try { OperationStarted?.Invoke(); return true; }
            catch (Exception exception) { IsBusy = false; return Fail("Could not begin save operation: " + exception.Message); }
        }
        private bool TryGetSlotPath(string slot, out string path, out string error)
        {
            path = null; error = null;
            if (string.IsNullOrWhiteSpace(slot) || slot.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || slot.Contains("..") || slot.Contains("/") || slot.Contains("\\"))
            { error = "Save slot names must be non-empty file names without path separators."; return false; }
            path = Path.Combine(SaveDirectory, slot + ".json"); return true;
        }
        private bool Fail(string message) { LastError = message; Debug.LogError(message); Notify(SaveFailed, message); return false; }
        private static void Notify(Action<string> observers, string value)
        {
            if (observers == null) return;
            foreach (Action<string> observer in observers.GetInvocationList())
                try { observer(value); } catch (Exception exception) { Debug.LogWarning("Save notification listener failed: " + exception.Message); }
        }
        private static SaveSlotInspection Inspection(SaveSlotStatus status, string message, int version = 0) => new SaveSlotInspection { Status = status, Message = message, SchemaVersion = version };
        private static string PreservedPath(string path) => path + ".preserved-" + Guid.NewGuid().ToString("N");
        private static string PayloadHash(SaveFile file)
        {
            StringBuilder data = new StringBuilder();
            AppendHashField(data, file.savedAtUtc); AppendHashField(data, file.eventStateJson);
            foreach (ProviderState state in file.providers ?? Array.Empty<ProviderState>()) { AppendHashField(data, state.key); AppendHashField(data, state.payload); }
            using (SHA256 algorithm = SHA256.Create()) return BitConverter.ToString(algorithm.ComputeHash(Encoding.UTF8.GetBytes(data.ToString()))).Replace("-", "");
        }
        private static void AppendHashField(StringBuilder builder, string value) { value = value ?? ""; builder.Append(value.Length).Append(':').Append(value); }
        private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch (Exception exception) { Debug.LogWarning("Temporary save cleanup failed: " + exception.Message); } }
        [Serializable] private sealed class MapIdentity { public string mapId; }
        [Serializable] private sealed class SaveFile { public int schemaVersion; public string savedAtUtc; public string eventStateJson; public ProviderState[] providers; public string payloadHash; }
        [Serializable] private sealed class ProviderState { public string key; public string payload; }
        [Serializable] private sealed class LastPlayedMarker { public string slot; public string playedAtUtc; }
        [Serializable] private sealed class ValidatedEventState { public FlagStateEntry[] flags; public CounterStateEntry[] counters; public QuestStateEntry[] quests; public string[] triggeredEventIds; }

        // JsonUtility tolerates truncated input. Validate JSON grammar before any DTO restore.
        private sealed class JsonSyntax
        {
            private readonly string json; private int index; private bool incomplete;
            private readonly HashSet<string> rootFields = new HashSet<string>(StringComparer.Ordinal);
            private JsonSyntax(string value) { json = value ?? ""; }
            public static bool IsObject(string value) { var p = new JsonSyntax(value); p.White(); if (p.index >= p.json.Length || p.json[p.index] != '{') return false; bool valid = p.Value(0); p.White(); return valid && p.index == p.json.Length; }
            public static bool HasFields(string value, string[] required)
            {
                var p = new JsonSyntax(value); p.White();
                if (p.index >= p.json.Length || p.json[p.index] != '{' || !p.Value(0)) return false;
                p.White(); if (p.index != p.json.Length) return false;
                foreach (string name in required ?? Array.Empty<string>()) if (!p.rootFields.Contains(name)) return false;
                return true;
            }
            public static bool IsTruncated(string value) { var p = new JsonSyntax(value); p.Value(0); return p.incomplete; }
            private void White() { while (index < json.Length && (json[index] == ' ' || json[index] == '\n' || json[index] == '\r' || json[index] == '\t')) index++; }
            private bool Take(char c) { White(); if (index >= json.Length) { incomplete = true; return false; } if (json[index] != c) return false; index++; return true; }
            private bool Value(int depth)
            {
                White(); if (index >= json.Length) { incomplete = true; return false; } if (depth > 64) return false;
                char c = json[index];
                if (c == '{')
                {
                    index++; White(); if (index < json.Length && json[index] == '}') { index++; return true; }
                    HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
                    do
                    {
                        White(); int start = index; if (!String()) return false;
                        string name = JsonUtility.FromJson<JsonMember>("{\"name\":" + json.Substring(start, index - start) + "}").name;
                        if (!names.Add(name)) return false;
                        if (depth == 0) rootFields.Add(name);
                        if (!Take(':') || !Value(depth + 1)) return false;
                        White(); if (index < json.Length && json[index] == '}') { index++; return true; }
                    } while (Take(','));
                    return false;
                }
                if (c == '[') { index++; White(); if (index < json.Length && json[index] == ']') { index++; return true; } do { if (!Value(depth + 1)) return false; White(); if (index < json.Length && json[index] == ']') { index++; return true; } } while (Take(',')); return false; }
                if (c == '"') return String();
                foreach (string literal in new[] { "true", "false", "null" }) if (c == literal[0]) { if (index + literal.Length > json.Length) { incomplete = true; return false; } if (json.Substring(index, literal.Length) != literal) return false; index += literal.Length; return true; }
                int numberStart = index;
                if (c == '-') index++;
                if (index >= json.Length) { incomplete = true; return false; }
                if (json[index] == '0') index++; else { if (json[index] < '1' || json[index] > '9') return false; while (index < json.Length && json[index] >= '0' && json[index] <= '9') index++; }
                if (index < json.Length && json[index] == '.') { index++; int start = index; while (index < json.Length && json[index] >= '0' && json[index] <= '9') index++; if (start == index) return false; }
                if (index < json.Length && (json[index] == 'e' || json[index] == 'E')) { index++; if (index < json.Length && (json[index] == '+' || json[index] == '-')) index++; int start = index; while (index < json.Length && json[index] >= '0' && json[index] <= '9') index++; if (start == index) return false; }
                return index > numberStart;
            }
            [Serializable] private sealed class JsonMember { public string name; }
            private bool String()
            {
                if (!Take('"')) return false;
                while (index < json.Length)
                {
                    char c = json[index++]; if (c == '"') return true; if (c < 32) return false;
                    if (c == '\\') { if (index >= json.Length) { incomplete = true; return false; } c = json[index++]; if (c == 'u') { for (int n = 0; n < 4; n++) { if (index >= json.Length) { incomplete = true; return false; } if (!Uri.IsHexDigit(json[index++])) return false; } } else if ("\"\\/bfnrt".IndexOf(c) < 0) return false; }
                }
                incomplete = true; return false;
            }
        }
    }
    /// <summary>Optional side-effect-free payload validation used by slot inspection and recovery.</summary>
    /// <summary>Prepare map content first; finalize placement after other world providers restore.</summary>
    public interface ISaveStateRestorePhases { bool PrepareRestoreStateJson(string json); }
    public interface ISaveStateValidator { bool ValidateStateJson(string json); }
    public interface ISaveStateProvider { string SaveKey { get; } string CaptureStateJson(); bool RestoreStateJson(string json); }
}
