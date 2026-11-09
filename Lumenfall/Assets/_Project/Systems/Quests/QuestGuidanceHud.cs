using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Lumenfall.Audio;
using Lumenfall.Controls;
using Lumenfall.Cutscenes;
using Lumenfall.DataDriven;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Inventory;
using Lumenfall.Startup;
using Lumenfall.Story;

namespace Lumenfall.Quests
{
    /// <summary>
    /// Wayfinding and quest feedback drawn over the world, following the quest hint (<see cref="QuestHintOverlay.CurrentWaypoint"/>):
    /// <list type="bullet">
    /// <item>A gold marker bobs over the person to talk to when they are on screen; otherwise an arrow at the screen edge
    /// points to them. Someone the player has not met yet is never named or pinpointed: the arrow, labelled
    /// <see cref="UnmetLabel"/>, leads toward them from afar and fades once the player is near, with no marker over them. A person or quest area elsewhere points at the door that leads there, found through the areas'
    /// portal links. Puzzle pieces are never marked (see <see cref="QuestHintOverlay.CurrentWaypoint"/>).</item>
    /// <item>A north-up minimap in the bottom-right corner, with the objective, doors and the player. M opens the whole
    /// area as a labelled map, so a building that has been mentioned can be found without knowing the campus yet.</item>
    /// <item>A banner when a quest is completed, naming the spell learned and how close the room key is.</item>
    /// <item>One-time tips at the moments they help: the first time the arrow appears, the first quest taken on and the
    /// first spell learned. Each is shown once per playthrough (a saved <c>tip.seen.*</c> flag).</item>
    /// </list>
    /// The arrow and markers follow the "Show quest hints" setting and show only while the hints are shown (H); the
    /// minimap and map are always available.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class QuestGuidanceHud : MonoBehaviour
    {
        private const float ResolveInterval = 0.2f;
        private const float BannerSeconds = 5.5f;
        private const float BannerFadeSeconds = 0.4f;
        private const int MiniMapCells = 30;
        private const float MiniMapSize = 168f;
        private const float MapRebuildSeconds = 2f;
        /// <summary>What the arrow calls someone the player has not met yet.</summary>
        internal const string UnmetLabel = "Someone to meet";
        /// <summary>Within this many tiles of someone not met yet the arrow goes: they are somewhere about.</summary>
        internal const int UnmetNearbyTiles = 6;
        private static readonly Color WalkableColour = new Color(0.42f, 0.38f, 0.55f, 0.92f);
        private static readonly Color BlockedColour = new Color(0.1f, 0.08f, 0.17f, 0.92f);
        private static readonly Color DoorColour = new Color(0.96f, 0.80f, 0.45f, 1f);

        private static QuestGuidanceHud instance;
        private readonly MenuSkin skin = new MenuSkin();
        private Texture2D arrowTexture;
        private Texture2D dotTexture;

        // The objective as resolved for the current area: the place itself, or the door toward it.
        private bool hasTarget;
        private Vector3 targetPosition;
        private Vector2Int targetCell;
        private string targetLabel;
        private string targetVia;
        // True when the objective is someone not met yet in this area: no name, distance or marker, and no arrow up close.
        private bool targetVague;
        private float resolveAt;

        // The current area's walkable shape, one pixel per cell with a clear one-cell border.
        private Texture2D mapTexture;
        private string mapTextureId;
        private RectInt mapBounds;
        private float mapBuiltAt = float.NegativeInfinity;
        private readonly List<GridPortal> doors = new List<GridPortal>();
        private bool fullMapOpen;

        private readonly Queue<(string title, string body)> pendingBanners = new Queue<(string, string)>();
        private string bannerTitle;
        private string bannerBody;
        private float bannerShownAt = float.NegativeInfinity;
        private GameEventRuntime subscribedRuntime;
        private const float TipSeconds = 8f;
        private const string TipFlagPrefix = "tip.seen.";
        private readonly Queue<string> pendingTips = new Queue<string>();
        private string tipText;
        private float tipShownAt = float.NegativeInfinity;
        private GUIStyle labelStyle;
        private GUIStyle bannerBodyStyle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            GameObject host = new GameObject("[QuestGuidanceHud]");
            DontDestroyOnLoad(host);
            host.AddComponent<QuestGuidanceHud>();
        }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            // Drawn with GUI only, never GUILayout, so skip the per-frame layout pass and its allocations.
            useGUILayout = false;
            arrowTexture = CreateArrowTexture(64);
            dotTexture = MenuArt.Rounded(16, 16, 8f, Color.white, Color.clear, 0f);
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
            Unsubscribe();
            SetFullMap(false);
            skin.Dispose();
            if (arrowTexture != null) Destroy(arrowTexture);
            if (dotTexture != null) Destroy(dotTexture);
            if (mapTexture != null) Destroy(mapTexture);
        }

        private void OnDisable() => SetFullMap(false);

        // ---- State ----

        private static bool WorldHidden => StartupMenu.IsVisible || CutscenePlayer.IsPlaying || QuestHintOverlay.IsSettingsOpen ||
            InventoryUiState.IsOpen || GameEventManager.Instance?.Runtime?.ActiveDialogue != null;

        private bool Hidden => WorldHidden || OverlayInputGate.IsBlockedExcept(this);

        private void Update()
        {
            Subscribe();
            if (fullMapOpen || bannerTitle != null) skin.Tick(Time.unscaledDeltaTime);
            Keyboard keyboard = Keyboard.current;
            if (fullMapOpen && (WorldHidden || keyboard == null || keyboard.mKey.wasPressedThisFrame || keyboard.escapeKey.wasPressedThisFrame))
                SetFullMap(false);
            else if (!fullMapOpen && !Hidden && keyboard != null && keyboard.mKey.wasPressedThisFrame && Player() != null)
                SetFullMap(true);

            if (Time.unscaledTime >= resolveAt)
            {
                resolveAt = Time.unscaledTime + ResolveInterval;
                Resolve();
                RefreshMapTexture();
                if (hasTarget && !Hidden)
                    QueueTip("first-arrow", "Follow the gold arrow to find who to talk to next. Press H to hide or show it with the hints, " +
                        "and M at any time for a map of the area.");
            }
        }

        // The full map is an overlay like the journal: it holds the input gate, so the player stands still and Escape
        // closes it instead of opening the settings.
        private void SetFullMap(bool open)
        {
            if (fullMapOpen == open) return;
            fullMapOpen = open;
            if (open) { OverlayInputGate.Acquire(this); AudioFeedbackService.PlayUi(AudioUiCue.Open); }
            else OverlayInputGate.Release(this);
        }

        private void Subscribe()
        {
            GameEventRuntime runtime = GameEventManager.Instance?.Runtime;
            if (runtime == subscribedRuntime) return;
            Unsubscribe();
            subscribedRuntime = runtime;
            if (runtime != null)
            {
                runtime.QuestStatusChanged += OnQuestStatusChanged;
                runtime.FlagChanged += OnFlagChanged;
            }
            // A new game or loaded save starts without a leftover banner or tip.
            pendingBanners.Clear();
            bannerTitle = null;
            pendingTips.Clear();
            tipText = null;
        }

        private void Unsubscribe()
        {
            if (subscribedRuntime != null)
            {
                subscribedRuntime.QuestStatusChanged -= OnQuestStatusChanged;
                subscribedRuntime.FlagChanged -= OnFlagChanged;
            }
            subscribedRuntime = null;
        }

        private void OnQuestStatusChanged(QuestDefinition quest, QuestStatus previous, QuestStatus status)
        {
            if (quest != null && status == QuestStatus.Active)
                QueueTip("first-quest", "You took on a quest! Its steps are on the card in the top-left corner. " +
                    "Quests you take on later are listed under it; click one to follow it instead.");
            if (quest == null || status != QuestStatus.Completed || previous == QuestStatus.Completed) return;
            List<string> lines = new List<string>();
            string spell = quest.metadata.GetValue("spellReward");
            if (!string.IsNullOrWhiteSpace(spell)) lines.Add("Spell learned: " + SpellName(spell));
            int done = 0;
            foreach (string id in SettlingInScene.ChapterQuestIds)
                if (subscribedRuntime != null && subscribedRuntime.GetQuestStatus(id) == QuestStatus.Completed) done++;
            if (Array.IndexOf(SettlingInScene.ChapterQuestIds, quest.id) >= 0)
                lines.Add(done >= SettlingInScene.ChapterQuestIds.Length
                    ? "Every favour is done. Your room should be ready by now!"
                    : $"Room key: {done} of {SettlingInScene.ChapterQuestIds.Length} favours done");
            pendingBanners.Enqueue((quest.title, string.Join("\n", lines)));
        }

        private void OnFlagChanged(string key, bool value)
        {
            if (!value || key == null || !key.StartsWith("spell.unlocked.", StringComparison.Ordinal)) return;
            QueueTip("first-spell", $"You learned a spell! Press {KeyBindings.GetKeyLabel(GameAction.SpellWheel)} to choose a spell, " +
                $"then face a relic and press {KeyBindings.GetKeyLabel(GameAction.Interact)} or right-click to cast it.");
        }

        /// <summary>Shows a tip once per playthrough; the saved flag keeps it from coming back after a load.</summary>
        private void QueueTip(string id, string text)
        {
            if (subscribedRuntime == null || subscribedRuntime.GetFlag(TipFlagPrefix + id)) return;
            subscribedRuntime.SetFlag(TipFlagPrefix + id, true);
            pendingTips.Enqueue(text);
        }

        private static string SpellName(string spell) => spell == "LostEchoes" ? "Lost Echoes" : spell;

        private static GridMover Player()
        {
            GridPlayerInput input = FindAnyObjectByType<GridPlayerInput>();
            return input == null ? null : input.GetComponent<GridMover>();
        }

        // ---- Resolving the objective ----

        private void Resolve()
        {
            hasTarget = false;
            targetVague = false;
            QuestWaypoint? current = QuestHintOverlay.CurrentWaypoint;
            DataDrivenWorldLoader loader = DataDrivenWorldLoader.Instance;
            GridMover player = Player();
            if (current == null || loader == null || loader.LoadedMap == null || player == null || player.World == null) return;
            QuestWaypoint waypoint = current.Value;
            string here = loader.LoadedMap.id;
            string mapId = waypoint.MapId;
            Vector2Int cell = waypoint.Cell;
            string label = waypoint.Label;
            if (!string.IsNullOrEmpty(waypoint.NpcId))
            {
                NpcDefinition record = Array.Find(loader.LoadedNpcs?.npcs ?? Array.Empty<NpcDefinition>(), npc => npc != null && npc.id == waypoint.NpcId);
                if (record == null) return;
                mapId = record.mapId;
                cell = new Vector2Int(record.x, record.y);
                label = string.IsNullOrWhiteSpace(record.displayName) ? QuestHintOverlay.CharacterFor(waypoint.NpcId) : record.displayName;
            }
            bool unmet = !string.IsNullOrEmpty(waypoint.NpcId) && !NpcIntroductions.IsKnown(waypoint.NpcId);
            if (unmet) label = UnmetLabel;
            if (string.IsNullOrEmpty(mapId)) return;

            if (SameMap(mapId, here))
            {
                if (waypoint.AreaOnly) return;
                targetCell = cell;
                targetPosition = player.World.CellToWorld(cell);
                // The arrow aims at the person themselves, so it never leads past them; someone not met yet just gets no
                // name, no marker overhead and no arrow once the player is close (targetVague).
                if (!string.IsNullOrEmpty(waypoint.NpcId) && TryFindLiveNpc(waypoint.NpcId, out Transform npc))
                {
                    targetPosition = npc.position;
                    targetCell = player.World.WorldToCell(npc.position);
                }
                targetVague = unmet;
                targetLabel = label;
                targetVia = null;
                hasTarget = true;
                return;
            }

            GridPortal door = FindDoorToward(loader.CurrentArea, mapId, player.World, out string nextMap);
            if (door == null) return;
            targetPosition = door.transform.position;
            targetCell = door.CentreCell;
            targetLabel = label ?? AreaName(mapId);
            targetVia = "via the door to " + AreaName(nextMap);
            hasTarget = true;
        }

        private static bool SameMap(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);


        private static bool TryFindLiveNpc(string npcId, out Transform npc)
        {
            foreach (DynamicNpc candidate in DynamicNpc.Active)
            {
                if (candidate == null || !candidate.isActiveAndEnabled || candidate.Id != npcId) continue;
                npc = candidate.transform;
                return true;
            }
            npc = null;
            return false;
        }

        /// <summary>
        /// The door in this area on the shortest open route to <paramref name="mapId"/>, by breadth-first search over the
        /// areas' portal links. Locked and disabled portals are not part of a route.
        /// </summary>
        private GridPortal FindDoorToward(WorldAreaAsset from, string mapId, GridWorld world, out string nextMap)
        {
            nextMap = null;
            if (from == null) return null;
            Dictionary<WorldAreaAsset, PortalContentAsset> firstStep = new Dictionary<WorldAreaAsset, PortalContentAsset> { { from, null } };
            Queue<WorldAreaAsset> frontier = new Queue<WorldAreaAsset>();
            frontier.Enqueue(from);
            while (frontier.Count > 0)
            {
                WorldAreaAsset area = frontier.Dequeue();
                foreach (PortalContentAsset portal in area.portals ?? Array.Empty<PortalContentAsset>())
                {
                    WorldAreaAsset next = portal == null || portal.disabled || portal.blocked ? null : portal.destination?.area;
                    if (next == null || firstStep.ContainsKey(next)) continue;
                    PortalContentAsset step = firstStep[area] ?? portal;
                    firstStep[next] = step;
                    if (SameMap(next.definition?.id, mapId))
                    {
                        nextMap = step.destination.area.definition?.id;
                        foreach (GridPortal door in doors)
                            if (door != null && door.Content == step && door.World == world) return door;
                        return null;
                    }
                    frontier.Enqueue(next);
                }
            }
            return null;
        }

        private static string AreaName(string mapId)
        {
            switch (mapId)
            {
                case "campus": return "Campus";
                case "westForest": return "West Forest";
                case "deepForest": return "Deep Forest";
                case "B01.Ground": return "Dormitory";
                case "B02.Ground": return "Arcane Workshop";
                case "B03.Ground": return "Main Academy Building";
                case "B04.Ground": return "Library";
                case "C03.IceTrial": return "Ice Trial";
                case "C04.Recess": return "Garden Recess";
                default: return string.IsNullOrEmpty(mapId) ? "?" : mapId;
            }
        }

        // ---- Map texture ----

        private void RefreshMapTexture()
        {
            DataDrivenWorldLoader loader = DataDrivenWorldLoader.Instance;
            GridMover player = Player();
            MapDefinition map = loader == null ? null : loader.LoadedMap;
            if (map == null || player == null || player.World == null || map.width <= 0 || map.height <= 0) return;
            // Rebuilt when the area changes, and now and then so opened gates and moved relics show up.
            if (map.id == mapTextureId && Time.unscaledTime - mapBuiltAt < MapRebuildSeconds) return;
            bool newArea = map.id != mapTextureId;
            mapTextureId = map.id;
            mapBuiltAt = Time.unscaledTime;
            mapBounds = new RectInt(map.boundsMinX, map.boundsMinY, map.width, map.height);
            if (newArea)
            {
                doors.Clear();
                foreach (GridPortal portal in FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude))
                    if (portal.World == player.World && portal.Content != null && !portal.Content.disabled) doors.Add(portal);
            }

            int width = map.width + 2, height = map.height + 2;
            if (mapTexture == null || mapTexture.width != width || mapTexture.height != height)
            {
                if (mapTexture != null) Destroy(mapTexture);
                mapTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "QuestGuidanceMap" };
            }
            Color32[] pixels = new Color32[width * height];
            GridWorld world = player.World;
            for (int y = 0; y < map.height; y++)
                for (int x = 0; x < map.width; x++)
                {
                    Vector2Int cell = new Vector2Int(map.boundsMinX + x, map.boundsMinY + y);
                    pixels[(y + 1) * width + x + 1] = world.IsCellBlocked(cell) ? BlockedColour : WalkableColour;
                }
            foreach (GridPortal door in doors)
            {
                if (door == null) continue;
                foreach (Vector2Int cell in door.EnumeratePortalCells())
                {
                    int x = cell.x - map.boundsMinX + 1, y = cell.y - map.boundsMinY + 1;
                    if (x > 0 && y > 0 && x < width - 1 && y < height - 1) pixels[y * width + x] = DoorColour;
                }
            }
            mapTexture.SetPixels32(pixels);
            mapTexture.Apply(false);
        }

        // Texture coordinates of a cell's centre; the texture has a one-cell clear border.
        private Vector2 CellUv(Vector2 cell) => new Vector2(
            (cell.x - mapBounds.x + 1.5f) / (mapBounds.width + 2f), (cell.y - mapBounds.y + 1.5f) / (mapBounds.height + 2f));

        // ---- Drawing ----

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint && !fullMapOpen) return;
            using (UiScale.Begin()) DrawGui();
        }

        private void DrawGui()
        {
            if (WorldHidden) return;
            skin.Ensure();
            skin.Fade = 1f;
            if (labelStyle == null) labelStyle = new GUIStyle(skin.SmallCentre) { fontStyle = FontStyle.Bold, wordWrap = false };
            if (bannerBodyStyle == null) bannerBodyStyle = new GUIStyle(skin.Body) { alignment = TextAnchor.UpperCenter, fontSize = 16 };
            Color previous = GUI.color;
            int previousDepth = GUI.depth;
            if (fullMapOpen)
            {
                GUI.depth = -5;
                DrawFullMap();
            }
            else if (!OverlayInputGate.IsBlocked)
            {
                if (hasTarget) DrawWaypoint();
                DrawMiniMap();
            }
            DrawBanner();
            DrawTip();
            GUI.color = previous;
            GUI.depth = previousDepth;
        }

        private void DrawWaypoint()
        {
            Camera view = Camera.main;
            GridMover player = Player();
            if (view == null || player == null) return;
            // Close enough that the interaction prompt takes over.
            int tiles = Mathf.Max(Mathf.Abs(targetCell.x - player.CurrentCell.x), Mathf.Abs(targetCell.y - player.CurrentCell.y));
            if (tiles <= 1) return;
            // Someone not met yet is somewhere about here: the arrow has done its job, and finding them is up to the player.
            if (targetVague && tiles <= UnmetNearbyTiles) return;

            Vector3 screen = view.WorldToScreenPoint(targetPosition + Vector3.up * 2.3f);
            Vector2 point = UiScale.FromScreenPoint(screen);
            // Keep clear of the hint card (top left), the settings button and minimap (right) and the spell chip (bottom).
            Rect safe = new Rect(56f, 72f, UiScale.Width - 56f - MiniMapSize - 48f, UiScale.Height - 72f - 120f);
            string distance = tiles + (tiles == 1 ? " tile" : " tiles");
            string caption = targetVague ? targetLabel : string.IsNullOrEmpty(targetLabel) ? distance : targetLabel + "  " + distance;
            float now = Time.unscaledTime;

            if (screen.z > 0f && safe.Contains(point))
            {
                // No marker over the place where someone unmet might be: it would point them out.
                if (targetVague) return;
                float bob = Mathf.Sin(now * 3f) * 5f * skin.Motion;
                Rect marker = new Rect(point.x - 15f, point.y - 30f + bob, 30f, 30f);
                DrawArrow(marker, 180f, 1f);
                DrawCaption(new Vector2(point.x, marker.y - 14f), caption, targetVia);
                return;
            }

            // Off screen: an arrow on the edge of the safe area, pointing toward the objective. The direction comes from the
            // camera's ground-plane axes rather than the projection, which flips for anything behind the camera (such as a
            // target well to the south of a camera looking north) and would point the arrow the wrong way.
            Vector2 centre = safe.center;
            Vector3 toTarget = targetPosition - player.transform.position;
            Vector3 right = view.transform.right;
            Vector3 ahead = view.transform.forward;
            right.y = 0f;
            ahead.y = 0f;
            // IMGUI y runs down the screen, so "ahead" is negative y.
            Vector2 direction = new Vector2(Vector3.Dot(toTarget, right.normalized), -Vector3.Dot(toTarget, ahead.normalized));
            if (direction.sqrMagnitude < 0.0001f) direction = Vector2.down;
            direction.Normalize();
            float reachX = direction.x > 0f ? (safe.xMax - centre.x) / direction.x : direction.x < 0f ? (safe.xMin - centre.x) / direction.x : float.PositiveInfinity;
            float reachY = direction.y > 0f ? (safe.yMax - centre.y) / direction.y : direction.y < 0f ? (safe.yMin - centre.y) / direction.y : float.PositiveInfinity;
            Vector2 edge = centre + direction * Mathf.Min(reachX, reachY);
            float pulse = 0.85f + 0.15f * Mathf.Sin(now * 4f) * skin.Motion;
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg + 90f;
            DrawArrow(new Rect(edge.x - 18f, edge.y - 18f, 36f, 36f), angle, pulse);
            DrawCaption(edge - direction * 40f, caption, targetVia);
        }

        // The arrow texture points up; IMGUI angles turn clockwise.
        private void DrawArrow(Rect rect, float degrees, float alpha)
        {
            Matrix4x4 matrix = GUI.matrix;
            UiScale.RotateAroundPivot(degrees, rect.center);
            skin.Draw(MenuSkin.Expand(rect, 10f, 10f), skin.Glow, MenuArt.Gold, 0.45f * alpha);
            skin.Draw(rect, arrowTexture, MenuArt.Gold, alpha);
            GUI.matrix = matrix;
        }

        private void DrawCaption(Vector2 centre, string caption, string via)
        {
            float width = Mathf.Max(labelStyle.CalcSize(new GUIContent(caption)).x, string.IsNullOrEmpty(via) ? 0f : skin.SmallCentre.CalcSize(new GUIContent(via)).x) + 20f;
            float height = string.IsNullOrEmpty(via) ? 24f : 42f;
            Rect pill = new Rect(Mathf.Round(centre.x - width * 0.5f), Mathf.Round(centre.y - height * 0.5f), width, height);
            pill.x = Mathf.Clamp(pill.x, 8f, UiScale.Width - pill.width - 8f);
            pill.y = Mathf.Clamp(pill.y, 8f, UiScale.Height - pill.height - 8f);
            skin.Box(pill, skin.Card, 0.92f);
            skin.Label(new Rect(pill.x, pill.y + 2f, pill.width, 20f), caption, labelStyle, MenuArt.Gold);
            if (!string.IsNullOrEmpty(via)) skin.Label(new Rect(pill.x, pill.y + 20f, pill.width, 20f), via, skin.SmallCentre, MenuArt.Cream);
        }

        private void DrawMiniMap()
        {
            GridMover player = Player();
            if (mapTexture == null || player == null || mapTextureId != DataDrivenWorldLoader.Instance?.LoadedMap?.id) return;
            Rect frame = new Rect(UiScale.Width - MiniMapSize - 20f, UiScale.Height - MiniMapSize - 72f, MiniMapSize, MiniMapSize);
            skin.Box(MenuSkin.Expand(frame, 6f, 6f), skin.Card, 0.95f);
            Vector2 centreUv = CellUv(player.CurrentCell);
            Vector2 span = new Vector2(MiniMapCells / (mapBounds.width + 2f), MiniMapCells / (mapBounds.height + 2f));
            Rect uv = new Rect(centreUv - span * 0.5f, span);
            GUI.color = Color.white;
            GUI.DrawTextureWithTexCoords(frame, mapTexture, uv);

            // The objective, clamped to the edge of the minimap when it lies outside it.
            if (hasTarget)
            {
                Vector2 offset = (Vector2)(targetCell - player.CurrentCell) / MiniMapCells;
                Vector2 point = frame.center + new Vector2(offset.x, -offset.y) * frame.width;
                bool outside = !frame.Contains(point);
                point.x = Mathf.Clamp(point.x, frame.xMin + 8f, frame.xMax - 8f);
                point.y = Mathf.Clamp(point.y, frame.yMin + 8f, frame.yMax - 8f);
                if (outside) DrawArrow(new Rect(point.x - 8f, point.y - 8f, 16f, 16f), Mathf.Atan2(-offset.y, offset.x) * Mathf.Rad2Deg + 90f, 1f);
                // Someone unmet shows as a soft glow over the area they could be in, not a star on them.
                else if (targetVague) skin.Draw(new Rect(point.x - 22f, point.y - 22f, 44f, 44f), skin.Glow, MenuArt.Gold, 0.6f);
                else skin.Draw(new Rect(point.x - 9f, point.y - 9f, 18f, 18f), skin.Sparkle, MenuArt.Gold, 1f);
            }
            skin.Draw(new Rect(frame.center.x - 5f, frame.center.y - 5f, 10f, 10f), dotTexture, MenuArt.Cream, 1f);
            skin.Label(new Rect(frame.x, frame.y + 2f, frame.width, 18f), "N", labelStyle, MenuArt.Gold);
            skin.Label(new Rect(frame.x, frame.yMax - 20f, frame.width, 18f), AreaName(mapTextureId) + "   M: map", skin.SmallCentre, MenuArt.Cream);
        }

        private void DrawFullMap()
        {
            GridMover player = Player();
            if (mapTexture == null || player == null) { SetFullMap(false); return; }
            float now = Time.unscaledTime;
            skin.DrawBackdrop(0.75f, now);
            float aspect = (mapBounds.width + 2f) / (mapBounds.height + 2f);
            float maxWidth = Mathf.Min(UiScale.Width - 96f, 900f), maxHeight = Mathf.Min(UiScale.Height - 170f, 640f);
            float width = Mathf.Min(maxWidth, maxHeight * aspect), height = width / aspect;
            Rect mapRect = new Rect((UiScale.Width - width) * 0.5f, (UiScale.Height - height) * 0.5f + 20f, width, height);
            Rect panel = MenuSkin.Expand(mapRect, 24f, 24f);
            panel.yMin -= 52f;
            skin.PanelFrame(panel, now);
            skin.DrawTitle(new Rect(panel.x, panel.y + 12f, panel.width, 36f), AreaName(mapTextureId), now);
            GUI.color = Color.white;
            GUI.DrawTexture(mapRect, mapTexture);

            foreach (GridPortal door in doors)
            {
                PortalContentAsset content = door == null ? null : door.Content;
                string destination = content?.destination?.area?.definition?.id;
                if (string.IsNullOrEmpty(destination)) continue;
                Vector2 point = MapPoint(mapRect, door.CentreCell);
                string name = AreaName(destination) + (content.blocked ? " (locked)" : string.Empty);
                float labelWidth = labelStyle.CalcSize(new GUIContent(name)).x + 14f;
                Rect pill = new Rect(point.x - labelWidth * 0.5f, point.y - 30f, labelWidth, 22f);
                skin.Box(pill, skin.Card, 0.9f);
                skin.Label(pill, name, labelStyle, content.blocked ? MenuArt.Muted : MenuArt.Gold);
            }
            if (hasTarget)
            {
                Vector2 target = MapPoint(mapRect, targetCell);
                float pulse = 1f + 0.2f * Mathf.Sin(now * 4f) * skin.Motion;
                if (targetVague)
                {
                    float spread = mapRect.width / (mapBounds.width + 2f) * UnmetNearbyTiles * pulse;
                    skin.Draw(new Rect(target.x - spread, target.y - spread, spread * 2f, spread * 2f), skin.Glow, MenuArt.Gold, 0.55f);
                }
                else skin.Draw(new Rect(target.x - 13f * pulse, target.y - 13f * pulse, 26f * pulse, 26f * pulse), skin.Sparkle, MenuArt.Gold, 1f);
                DrawCaption(target + new Vector2(0f, 26f), targetLabel ?? "Objective", targetVia);
            }
            Vector2 you = MapPoint(mapRect, player.CurrentCell);
            skin.Draw(new Rect(you.x - 7f, you.y - 7f, 14f, 14f), dotTexture, MenuArt.Cream, 1f);
            skin.Label(new Rect(you.x - 30f, you.y + 7f, 60f, 18f), "You", labelStyle, MenuArt.Cream);
            skin.Label(new Rect(mapRect.x, mapRect.y + 4f, mapRect.width, 18f), "N", labelStyle, MenuArt.Gold);
            skin.Label(new Rect(panel.x, panel.yMax - 26f, panel.width, 20f), "Gold: doors   Star: objective   M or Esc to close", skin.SmallCentre, MenuArt.Muted);
        }

        private Vector2 MapPoint(Rect mapRect, Vector2Int cell)
        {
            Vector2 uv = CellUv(cell);
            return new Vector2(mapRect.x + uv.x * mapRect.width, mapRect.yMax - uv.y * mapRect.height);
        }

        private void DrawBanner()
        {
            float now = Time.unscaledTime;
            if (bannerTitle != null && now - bannerShownAt > BannerSeconds) bannerTitle = null;
            if (bannerTitle == null && pendingBanners.Count > 0 && !OverlayInputGate.IsBlocked)
            {
                (bannerTitle, bannerBody) = pendingBanners.Dequeue();
                bannerShownAt = now;
                AudioFeedbackService.PlayUi(AudioUiCue.Confirm);
            }
            if (bannerTitle == null) return;
            float elapsed = now - bannerShownAt;
            skin.Fade = Mathf.Min(Mathf.Clamp01(elapsed / BannerFadeSeconds), Mathf.Clamp01((BannerSeconds - elapsed) / BannerFadeSeconds));
            float width = Mathf.Min(UiScale.Width - 40f, 460f);
            float bodyHeight = string.IsNullOrEmpty(bannerBody) ? 0f : bannerBodyStyle.CalcHeight(new GUIContent(bannerBody), width - 48f);
            float rise = (1f - skin.Fade) * 14f * skin.Motion;
            Rect panel = new Rect((UiScale.Width - width) * 0.5f, 84f - rise, width, 86f + bodyHeight);
            skin.PanelFrame(panel, now);
            skin.Label(new Rect(panel.x, panel.y + 12f, panel.width, 20f), "Quest complete", skin.SmallCentre, MenuArt.Lavender);
            skin.DrawTitle(new Rect(panel.x + 24f, panel.y + 32f, panel.width - 48f, 32f), bannerTitle, now);
            if (bodyHeight > 0f) skin.Label(new Rect(panel.x + 24f, panel.y + 68f, panel.width - 48f, bodyHeight), bannerBody, bannerBodyStyle, MenuArt.Cream);
            skin.DrawSparks(skin.Fade);
            skin.Fade = 1f;
        }

        // A small card above the spell chip and below the world's middle, so it never covers the player.
        private void DrawTip()
        {
            float now = Time.unscaledTime;
            if (tipText != null && now - tipShownAt > TipSeconds) tipText = null;
            if (tipText == null && pendingTips.Count > 0 && !OverlayInputGate.IsBlocked && bannerTitle == null)
            {
                tipText = pendingTips.Dequeue();
                tipShownAt = now;
                AudioFeedbackService.PlayUi(AudioUiCue.Open);
            }
            if (tipText == null || fullMapOpen || OverlayInputGate.IsBlocked) return;
            float elapsed = now - tipShownAt;
            skin.Fade = Mathf.Min(Mathf.Clamp01(elapsed / BannerFadeSeconds), Mathf.Clamp01((TipSeconds - elapsed) / BannerFadeSeconds));
            float width = Mathf.Min(UiScale.Width - 40f, 540f);
            float bodyHeight = bannerBodyStyle.CalcHeight(new GUIContent(tipText), width - 40f);
            Rect card = new Rect((UiScale.Width - width) * 0.5f, UiScale.Height - 150f - bodyHeight, width, bodyHeight + 44f);
            skin.PanelFrame(card, now);
            skin.Label(new Rect(card.x, card.y + 8f, card.width, 18f), "Tip", skin.SmallCentre, MenuArt.Gold);
            skin.Label(new Rect(card.x + 20f, card.y + 28f, card.width - 40f, bodyHeight), tipText, bannerBodyStyle, MenuArt.Cream);
            skin.Fade = 1f;
        }

        /// <summary>A notched arrowhead pointing up, white with soft edges, for tinting.</summary>
        private static Texture2D CreateArrowTexture(int size)
        {
            Vector2[] shape = { new Vector2(0.5f, 0.06f), new Vector2(0.9f, 0.88f), new Vector2(0.5f, 0.68f), new Vector2(0.1f, 0.88f) };
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "QuestArrow", wrapMode = TextureWrapMode.Clamp };
            Color32[] pixels = new Color32[size * size];
            const int Samples = 4;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int inside = 0;
                    for (int sy = 0; sy < Samples; sy++)
                        for (int sx = 0; sx < Samples; sx++)
                        {
                            // Texture rows run bottom to top; the shape is written top to bottom.
                            Vector2 p = new Vector2((x + (sx + 0.5f) / Samples) / size, 1f - (y + (sy + 0.5f) / Samples) / size);
                            if (InsidePolygon(shape, p)) inside++;
                        }
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(255 * inside / (Samples * Samples)));
                }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static bool InsidePolygon(Vector2[] polygon, Vector2 point)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
                if ((polygon[i].y > point.y) != (polygon[j].y > point.y) &&
                    point.x < (polygon[j].x - polygon[i].x) * (point.y - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x)
                    inside = !inside;
            return inside;
        }
    }
}
