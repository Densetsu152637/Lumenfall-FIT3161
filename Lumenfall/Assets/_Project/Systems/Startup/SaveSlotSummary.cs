using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using Lumenfall.DataDriven;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Quests;

namespace Lumenfall.Startup
{
    /// <summary>
    /// Keeps a small, display-only summary beside each save slot for the startup menu's Load page: where the player was,
    /// the quest in progress, total playtime and a camera thumbnail. Summaries are written after every successful save
    /// as <c>&lt;slot&gt;.summary.json</c> and <c>&lt;slot&gt;.thumb.png</c>; the save file itself is never changed,
    /// so a missing or stale summary only affects what the Load page shows.
    /// </summary>
    [DefaultExecutionOrder(2000)]
    [DisallowMultipleComponent]
    public sealed class SaveSlotSummary : MonoBehaviour
    {
        private const int ThumbnailWidth = 320;
        private const int ThumbnailHeight = 180;
        private const float ThumbnailRefreshSeconds = 10f;

        private static SaveSlotSummary instance;
        private static readonly Dictionary<string, string> MapNames = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "westForest", "West Forest" },
            { "campus", "Academy Campus" },
            { "C04.Recess", "Garden Recess" },
            { "B01.Ground", "Training Centre" },
            { "B02.Ground", "Arcane Workshop" },
            { "B03.Ground", "Main Academy Building" },
            { "B04.Ground", "Library" }
        };

        private readonly HashSet<string> pendingThumbnails = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> lastThumbnailTimes = new Dictionary<string, float>(StringComparer.Ordinal);
        private GameSaveManager saves;
        private float playSeconds;

        [Serializable]
        public sealed class Info
        {
            public string location;
            /// <summary>Active quest title or a status phrase. Summaries written before quest progress was split out
            /// hold the combined "title · n of m quests done" text here and leave the counts at zero.</summary>
            public string quest;
            public int questsDone;
            public int questsTotal;
            public float playSeconds;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindAnyObjectByType<SaveSlotSummary>() == null) new GameObject("[SaveSlotSummary]").AddComponent<SaveSlotSummary>();
        }

        /// <summary>Starts the playtime clock from zero; the startup menu calls this before saving a new game.</summary>
        public static void BeginNewGame()
        {
            if (instance != null) instance.playSeconds = 0f;
        }

        public static bool TryRead(string slot, out Info info)
        {
            info = null;
            string path = PathFor(slot, ".summary.json");
            if (path == null || !File.Exists(path)) return false;
            try
            {
                info = JsonUtility.FromJson<Info>(File.ReadAllText(path, Encoding.UTF8));
                return info != null;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not read the summary for '{slot}': {exception.Message}");
                return false;
            }
        }

        public static string ThumbnailPath(string slot) => PathFor(slot, ".thumb.png");

        /// <summary>Forces the next late-frame thumbnail capture for a slot, bypassing autosave throttling.</summary>
        public static void RequestThumbnailRefresh(string slot)
        {
            if (instance != null && !string.IsNullOrWhiteSpace(slot)) instance.pendingThumbnails.Add(slot);
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
            saves = GameSaveManager.Instance;
            saves.SaveCompleted += OnSaveCompleted;
            saves.LoadCompleted += OnLoadCompleted;
        }

        private void OnDestroy()
        {
            if (instance != this) return;
            instance = null;
            if (saves == null) return;
            saves.SaveCompleted -= OnSaveCompleted;
            saves.LoadCompleted -= OnLoadCompleted;
        }

        private void Update()
        {
            // Playtime counts only while the world is running, not while the startup menu or another pause is up.
            if (Time.timeScale > 0f && !StartupMenu.IsVisible) playSeconds += Time.unscaledDeltaTime;
        }

        private void LateUpdate()
        {
            // Thumbnails are rendered here, after the camera has settled for the frame, rather than inside the save call.
            if (pendingThumbnails.Count == 0) return;
            foreach (string slot in pendingThumbnails) CaptureThumbnail(slot);
            pendingThumbnails.Clear();
        }

        private void OnLoadCompleted(string slot)
        {
            playSeconds = TryRead(slot, out Info info) ? Mathf.Max(0f, info.playSeconds) : 0f;
        }

        private void OnSaveCompleted(string slot)
        {
            string path = PathFor(slot, ".summary.json");
            if (path == null) return;
            Info info = new Info { location = DescribeLocation(), playSeconds = playSeconds };
            DescribeQuest(info);
            try
            {
                File.WriteAllText(path, JsonUtility.ToJson(info, true), Encoding.UTF8);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not write the summary for '{slot}': {exception.Message}");
            }

            // Autosave runs after most moves, so the thumbnail is refreshed at most every few seconds per slot.
            bool stale = !lastThumbnailTimes.TryGetValue(slot, out float last) || Time.unscaledTime - last >= ThumbnailRefreshSeconds ||
                !File.Exists(ThumbnailPath(slot)) || StartupMenu.IsVisible;
            if (stale) pendingThumbnails.Add(slot);
        }

        private void CaptureThumbnail(string slot)
        {
            Camera camera = Camera.main;
            string path = ThumbnailPath(slot);
            if (camera == null || path == null) return;

            RenderTexture target = RenderTexture.GetTemporary(ThumbnailWidth, ThumbnailHeight, 24);
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            Texture2D image = null;
            try
            {
                // URP renders off-screen through a render request; Camera.Render remains the built-in pipeline fallback.
                // A save made while the menu hides the player still shows them in its thumbnail.
                RenderPipeline.StandardRequest request = new RenderPipeline.StandardRequest { destination = target };
                PlayerVisibility.RenderVisible(() =>
                {
                    if (GraphicsSettings.currentRenderPipeline != null && RenderPipeline.SupportsRenderRequest(camera, request))
                    {
                        RenderPipeline.SubmitRenderRequest(camera, request);
                    }
                    else
                    {
                        camera.targetTexture = target;
                        camera.Render();
                    }
                });
                RenderTexture.active = target;
                image = new Texture2D(ThumbnailWidth, ThumbnailHeight, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0f, 0f, ThumbnailWidth, ThumbnailHeight), 0, 0);
                image.Apply(false);
                File.WriteAllBytes(path, image.EncodeToPNG());
                lastThumbnailTimes[slot] = Time.unscaledTime;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not capture the thumbnail for '{slot}': {exception.Message}");
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target);
                if (image != null) Destroy(image);
            }
        }

        private static string PathFor(string slot, string suffix)
        {
            if (string.IsNullOrWhiteSpace(slot) || slot.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || slot.Contains("..")) return null;
            return Path.Combine(GameSaveManager.Instance.SaveDirectory, slot + suffix);
        }

        /// <summary>Names the map, preferring the smallest named region around the player (for example "Garden walk").</summary>
        private static string DescribeLocation()
        {
            DataDrivenWorldLoader loader = DataDrivenWorldLoader.Instance;
            MapDefinition map = loader == null ? null : loader.LoadedMap;
            if (map == null) return string.Empty;
            string mapName = MapNames.TryGetValue(map.id ?? string.Empty, out string friendly) ? friendly : map.id;

            GridPlayerInput input = FindAnyObjectByType<GridPlayerInput>();
            GridMover player = input != null ? input.GetComponent<GridMover>() : null;
            if (player == null || map.regions == null) return mapName;

            Vector2Int cell = player.CurrentCell;
            MapRegionDefinition best = null;
            foreach (MapRegionDefinition region in map.regions)
            {
                if (region == null || region.rect == null || IsCodeName(region.name)) continue;
                MapRectDefinition r = region.rect;
                bool inside = cell.x >= r.x && cell.x < r.x + r.width && cell.y >= r.y && cell.y < r.y + r.height;
                if (inside && (best == null || r.width * r.height < best.rect.width * best.rect.height)) best = region;
            }
            if (best == null) return mapName;
            return map.id == "westForest" ? mapName + " · " + best.name : best.name;
        }

        // Region names such as "B01" or "S03" are layout codes rather than places a player would recognise.
        private static bool IsCodeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 4 || !char.IsLetter(name[0])) return true;
            for (int i = 1; i < name.Length; i++) if (!char.IsDigit(name[i])) return false;
            return true;
        }

        private static void DescribeQuest(Info info)
        {
            info.quest = string.Empty;
            GameEventRuntime runtime = GameEventManager.Instance != null ? GameEventManager.Instance.Runtime : null;
            if (runtime == null) return;

            string active = null;
            foreach (string id in QuestHintOverlay.QuestOrder)
            {
                if (!runtime.Catalog.TryGetQuest(id, out QuestDefinition quest) || quest == null) continue;
                info.questsTotal++;
                QuestStatus status = runtime.GetQuestStatus(id);
                if (status == QuestStatus.Completed) info.questsDone++;
                else if (status == QuestStatus.Active && active == null) active = string.IsNullOrWhiteSpace(quest.title) ? id : quest.title;
            }
            if (info.questsTotal == 0) return;
            info.quest = info.questsDone == info.questsTotal ? "Every quest complete"
                : active ?? (info.questsDone == 0 ? "A new term begins" : "Between quests");
        }
    }
}
