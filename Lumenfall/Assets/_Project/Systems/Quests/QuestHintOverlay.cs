using System;
using System.Collections.Generic;
using UnityEngine;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Spells;
using Lumenfall.DataDriven;
using Lumenfall.Startup;

namespace Lumenfall.Quests
{
    /// <summary>Small contextual objective card driven by quest and puzzle runtime state.</summary>
    [DefaultExecutionOrder(500)]
    [DisallowMultipleComponent]
    public sealed class QuestHintOverlay : MonoBehaviour
    {
        private const string EnabledKey = "lumenfall.quest-hints-enabled";
        private static readonly string[] QuestOrder =
        {
            "stella_first_light", "stella_string_them_together", "cinna_out_of_alignment",
            "cinna_cold_feet_warm_idea", "orio_patch_notes", "katrina_something_borrowed"
        };

        private static QuestHintOverlay instance;
        private static GUIStyle boxStyle;
        private static GUIStyle titleStyle;
        private static GUIStyle bodyStyle;
        private QuestPuzzleDatabase puzzleDatabase;
        private string hint;
        private string title;
        private float refreshAt;
        private bool settingsOpen;
        private readonly List<GridPlayerInput> pausedInputs = new List<GridPlayerInput>();

        public static bool HintsEnabled => PlayerPrefs.GetInt(EnabledKey, 1) != 0;
        public static bool IsSettingsOpen => instance != null && instance.settingsOpen;

        public static void SetHintsEnabled(bool enabled)
        {
            PlayerPrefs.SetInt(EnabledKey, enabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindAnyObjectByType<QuestHintOverlay>() != null) return;
            GameObject host = new GameObject("[QuestHintOverlay]");
            DontDestroyOnLoad(host);
            host.AddComponent<QuestHintOverlay>();
        }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            DontDestroyOnLoad(gameObject);
            TextAsset definitions = Resources.Load<TextAsset>("Quests/quest_puzzle_definitions");
            if (definitions != null)
            {
                try { puzzleDatabase = JsonUtility.FromJson<QuestPuzzleDatabase>(definitions.text); }
                catch (Exception exception) { Debug.LogWarning($"Could not load quest hint targets: {exception.Message}", this); }
            }
        }

        private void OnDestroy() { if (instance == this) instance = null; }

        private void OnDisable() { settingsOpen = false; RestoreGameplayInput(); }

        private void Update()
        {
            if (StartupMenu.IsVisible && settingsOpen) SetSettingsOpen(false);
            if (Time.unscaledTime < refreshAt) return;
            refreshAt = Time.unscaledTime + 0.35f;
            RefreshHint();
        }

        private void RefreshHint()
        {
            hint = null;
            title = null;
            if (!HintsEnabled || StartupMenu.IsVisible) return;
            GameEventRuntime runtime = GameEventManager.Instance?.Runtime;
            WorldStateService world = WorldStateService.Instance;
            if (runtime == null || world == null) return;

            QuestPuzzleDefinition activeSameMap = null, activeElsewhere = null;
            QuestDefinition activeQuest = null;
            QuestPuzzleDefinition availableSameMap = null, availableElsewhere = null;
            QuestDefinition availableQuest = null;
            foreach (string id in QuestOrder)
            {
                if (!runtime.Catalog.TryGetQuest(id, out QuestDefinition quest) || quest == null) continue;
                QuestPuzzleDefinition puzzle = FindPuzzle(id);
                QuestStatus status = runtime.GetQuestStatus(id);
                if (status == QuestStatus.Active)
                {
                    if (activeQuest == null || IsOnMap(puzzle, world.CurrentMapId))
                    {
                        if (IsOnMap(puzzle, world.CurrentMapId)) { activeSameMap = puzzle; activeQuest = quest; }
                        else if (activeQuest == null) { activeElsewhere = puzzle; activeQuest = quest; }
                    }
                    continue;
                }
                if (status != QuestStatus.NotStarted || !PrerequisitesMet(quest, runtime)) continue;
                if (IsOnMap(puzzle, world.CurrentMapId))
                {
                    if (availableSameMap == null) { availableSameMap = puzzle; availableQuest = quest; }
                }
                else if (availableElsewhere == null) { availableElsewhere = puzzle; }
            }

            QuestPuzzleDefinition selectedPuzzle;
            if (activeSameMap != null) selectedPuzzle = activeSameMap;
            else if (activeQuest != null) selectedPuzzle = activeElsewhere;
            else if (availableSameMap != null) { selectedPuzzle = availableSameMap; activeQuest = availableQuest; }
            else if (availableElsewhere != null)
            {
                selectedPuzzle = availableElsewhere;
                activeQuest = FindQuestForPuzzle(selectedPuzzle, runtime);
            }
            else return;

            if (activeQuest == null) return;
            title = activeQuest.title;
            if (runtime.GetQuestStatus(activeQuest.id) == QuestStatus.NotStarted)
            {
                hint = $"Talk to {CharacterFor(activeQuest.id)} to begin this quest.";
                if (selectedPuzzle != null && !string.IsNullOrEmpty(selectedPuzzle.mapId) &&
                    !string.Equals(selectedPuzzle.mapId, world.CurrentMapId, StringComparison.OrdinalIgnoreCase))
                    hint += $"\nLocation: {MapName(selectedPuzzle.mapId)}.";
                return;
            }

            if (string.Equals(activeQuest.id, "stella_first_light", StringComparison.Ordinal))
            {
                if (IsOnMap(selectedPuzzle, world.CurrentMapId) &&
                    StellaStringThemTogetherPuzzle.TryGetFirstLightStep(out string boardHint, out Vector2Int boardCell))
                {
                    hint = boardHint;
                    if (!string.IsNullOrWhiteSpace(boardHint) && boardCell != default)
                    {
                        string direction = DirectionToCell(boardCell);
                        if (!string.IsNullOrEmpty(direction)) hint += "\n" + direction;
                    }
                }
                else hint = "Find the five circuit relics in Stella's room. Each switch changes several relics.";
                return;
            }

            if (selectedPuzzle == null || selectedPuzzle.targets == null)
            {
                hint = string.Equals(activeQuest.id, "stella_first_light", StringComparison.Ordinal)
                    ? "Find the five circuit relics in Stella's room. Each switch changes several relics."
                    : "Continue the quest and speak with the quest giver if you need help.";
                return;
            }
            if (!IsOnMap(selectedPuzzle, world.CurrentMapId))
            {
                hint = $"Travel to {MapName(selectedPuzzle.mapId)} to continue this quest.";
                return;
            }
            foreach (QuestPuzzleTarget sourceTarget in selectedPuzzle.targets)
            {
                QuestPuzzleTarget target = MergeCurrentMapTarget(sourceTarget, world);
                if (TargetComplete(target, world)) continue;
                hint = Describe(target, world);
                string direction = DirectionTo(target, world);
                if (!string.IsNullOrEmpty(direction)) hint += "\n" + direction;
                return;
            }
            hint = $"The objective is complete. Return to {CharacterFor(activeQuest.id)}.";
        }

        private static QuestPuzzleTarget MergeCurrentMapTarget(QuestPuzzleTarget questTarget, WorldStateService world)
        {
            PuzzleTargetDefinition best = null;
            int bestOverlap = 0;
            foreach (PuzzleTargetDefinition mapTarget in world.PuzzleTargets ?? Array.Empty<PuzzleTargetDefinition>())
            {
                if (mapTarget == null || mapTarget.objectIds == null || questTarget?.objectIds == null) continue;
                bool overlaps = false;
                foreach (string id in questTarget.objectIds)
                    foreach (string mapId in mapTarget.objectIds)
                        if (string.Equals(id, mapId, StringComparison.Ordinal)) overlaps = true;
                if (!overlaps) continue;
                int overlap = 0;
                foreach (string id in questTarget.objectIds)
                    foreach (string mapId in mapTarget.objectIds)
                        if (string.Equals(id, mapId, StringComparison.Ordinal)) overlap++;
                if (overlap > bestOverlap) { best = mapTarget; bestOverlap = overlap; }
            }
            if (best == null) return questTarget;
            return new QuestPuzzleTarget
            {
                objectIds = questTarget.objectIds,
                requiredConnections = MergeConnections(questTarget.requiredConnections, best.requiredConnections),
                conditionMode = string.Equals(questTarget.conditionMode, "Any", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(best.conditionMode, "Any", StringComparison.OrdinalIgnoreCase) ? "Any" : "All",
                requiredPowered = questTarget.requiredPowered || best.requiredPowered,
                requiredConnected = questTarget.requiredConnected || best.requiredConnected,
                requiredRevealed = questTarget.requiredRevealed || best.requiredRevealed,
                requiredFrozen = questTarget.requiredFrozen || best.requiredFrozen,
                requiredAligned = questTarget.requiredAligned || best.requiredAligned,
                requiredRotation = questTarget.requiredRotation >= 0 ? questTarget.requiredRotation : best.requiredRotation,
                requiredPositionX = questTarget.requiredPositionX != int.MinValue ? questTarget.requiredPositionX : best.requiredPositionX,
                requiredPositionY = questTarget.requiredPositionY != int.MinValue ? questTarget.requiredPositionY : best.requiredPositionY
            };
        }

        private static string[] MergeConnections(string[] first, string[] second)
        {
            List<string> connections = new List<string>();
            foreach (string item in first ?? Array.Empty<string>()) if (!string.IsNullOrWhiteSpace(item) && !connections.Contains(item)) connections.Add(item);
            foreach (string item in second ?? Array.Empty<string>()) if (!string.IsNullOrWhiteSpace(item) && !connections.Contains(item)) connections.Add(item);
            return connections.ToArray();
        }

        private QuestPuzzleDefinition FindPuzzle(string questId)
        {
            foreach (QuestPuzzleDefinition puzzle in puzzleDatabase?.puzzles ?? Array.Empty<QuestPuzzleDefinition>())
                if (puzzle != null && string.Equals(puzzle.questId, questId, StringComparison.Ordinal)) return puzzle;
            return null;
        }

        private static bool IsOnMap(QuestPuzzleDefinition puzzle, string mapId) => puzzle != null &&
            string.Equals(puzzle.mapId, mapId, StringComparison.OrdinalIgnoreCase);

        private static bool PrerequisitesMet(QuestDefinition quest, GameEventRuntime runtime)
        {
            string requirement = quest.metadata.GetValue("requiresQuest");
            if (string.IsNullOrWhiteSpace(requirement)) return true;
            string[] parts = requirement.Split(':');
            if (parts.Length != 2 || !Enum.TryParse(parts[1], true, out QuestStatus expected)) return false;
            return runtime.GetQuestStatus(parts[0]) == expected;
        }

        private static QuestDefinition FindQuestForPuzzle(QuestPuzzleDefinition puzzle, GameEventRuntime runtime)
        {
            return puzzle != null && runtime.Catalog.TryGetQuest(puzzle.questId, out QuestDefinition quest) ? quest : null;
        }

        private static bool TargetComplete(QuestPuzzleTarget target, WorldStateService world)
        {
            if (target == null || target.objectIds == null || target.objectIds.Length == 0) return false;
            foreach (string connection in target.requiredConnections ?? Array.Empty<string>())
            {
                string[] ends = (connection ?? string.Empty).Split('|');
                if (ends.Length != 2 || !world.IsConnected(ends[0], ends[1])) return false;
            }
            bool any = false, all = true;
            foreach (string id in target.objectIds)
            {
                if (!world.TryGetObject(id, out WorldObjectSnapshot state)) { all = false; continue; }
                bool connected = false;
                foreach (string otherId in target.objectIds)
                    if (!string.Equals(id, otherId, StringComparison.Ordinal) && world.IsConnected(id, otherId)) connected = true;
                bool valid = (!target.requiredPowered || state.powered) && (!target.requiredConnected || connected) &&
                    (!target.requiredRevealed || state.revealed) && (!target.requiredFrozen || state.frozen) &&
                    (!target.requiredAligned || state.aligned) && (target.requiredRotation < 0 || Mathf.RoundToInt(state.rotation) % 360 == target.requiredRotation % 360) &&
                    (target.requiredPositionX == int.MinValue || state.cell.x == target.requiredPositionX) &&
                    (target.requiredPositionY == int.MinValue || state.cell.y == target.requiredPositionY);
                any |= valid; all &= valid;
            }
            return string.Equals(target.conditionMode, "Any", StringComparison.OrdinalIgnoreCase) ? any : all;
        }

        private static string Describe(QuestPuzzleTarget target, WorldStateService world)
        {
            if (target.requiredPositionX != int.MinValue && target.requiredPositionY != int.MinValue)
            {
                foreach (string id in target.objectIds ?? Array.Empty<string>())
                    if (world.TryGetObject(id, out WorldObjectSnapshot state) &&
                        (state.cell.x != target.requiredPositionX || state.cell.y != target.requiredPositionY))
                    {
                        string action = target.requiredFrozen && !state.frozen ? "Freeze the object, then move it" : "Move the object";
                        if (target.requiredRevealed && state.revealed) action = "Return the revealed keepsake";
                        return $"{action} to cell ({target.requiredPositionX}, {target.requiredPositionY}).";
                    }
            }
            if (target.requiredPowered) return "Power the relic with Ampere.";
            if (target.requiredConnections != null && target.requiredConnections.Length > 0) return "Connect the relics with Weave.";
            if (target.requiredConnected) return "Connect the puzzle objects.";
            if (target.requiredAligned) return "Align the objects with Vis.";
            if (target.requiredFrozen) return "Freeze the trial object with Glaciate and move it to the marked cell.";
            if (target.requiredRotation >= 0) return $"Rotate the mechanism to {target.requiredRotation} degrees.";
            if (target.requiredRevealed) return "Reveal the hidden keepsake with Lost Echoes.";
            return "Work on the quest puzzle.";
        }

        private static string DirectionTo(QuestPuzzleTarget target, WorldStateService world)
        {
            GridPlayerInput input = FindAnyObjectByType<GridPlayerInput>();
            GridMover mover = input == null ? null : input.GetComponent<GridMover>();
            if (mover == null || target.objectIds == null) return null;
            foreach (string objectId in target.objectIds)
            {
                if (!world.TryGetObject(objectId, out WorldObjectSnapshot state)) continue;
                Vector2Int from = mover.IsMoving ? mover.DestinationCell : mover.CurrentCell;
                Vector2Int destination = ResolveDestination(target, state, out bool headingToGoal);
                return DirectionBetweenCells(from, destination, headingToGoal);
            }
            return null;
        }

        private static string DirectionToCell(Vector2Int destination)
        {
            GridPlayerInput input = FindAnyObjectByType<GridPlayerInput>();
            GridMover mover = input == null ? null : input.GetComponent<GridMover>();
            if (mover == null) return null;
            Vector2Int from = mover.IsMoving ? mover.DestinationCell : mover.CurrentCell;
            return DirectionBetweenCells(from, destination, false);
        }

        private static Vector2Int ResolveDestination(QuestPuzzleTarget target, WorldObjectSnapshot state, out bool headingToGoal)
        {
            headingToGoal = state != null && ((target.requiredFrozen && state.frozen) ||
                (target.requiredRevealed && state.revealed)) &&
                target.requiredPositionX != int.MinValue && target.requiredPositionY != int.MinValue;
            return headingToGoal ? new Vector2Int(target.requiredPositionX, target.requiredPositionY) : state.cell;
        }

        private static string DirectionBetweenCells(Vector2Int from, Vector2Int destination, bool headingToGoal)
        {
            int dx = destination.x - from.x, dy = destination.y - from.y;
            if (dx == 0 && dy == 0) return headingToGoal ? "Goal: move the object to your cell." : "The target is here.";
            string horizontal = dx == 0 ? null : $"{Mathf.Abs(dx)} {(Mathf.Abs(dx) == 1 ? "tile" : "tiles")} {(dx > 0 ? "east" : "west")}";
            string vertical = dy == 0 ? null : $"{Mathf.Abs(dy)} {(Mathf.Abs(dy) == 1 ? "tile" : "tiles")} {(dy > 0 ? "north" : "south")}";
            string direction = horizontal == null ? vertical : vertical == null ? horizontal : horizontal + ", " + vertical;
            return (headingToGoal ? "Goal: " : "Target: ") + $"{direction} (cell {destination.x}, {destination.y}).";
        }

        private static string CharacterFor(string questId)
        {
            if (questId.StartsWith("stella_", StringComparison.Ordinal)) return "Stella";
            if (questId.StartsWith("cinna_", StringComparison.Ordinal)) return "Cinna Cirra";
            if (questId.StartsWith("orio_", StringComparison.Ordinal)) return "Orio";
            if (questId.StartsWith("katrina_", StringComparison.Ordinal)) return "Katrina";
            return "the quest giver";
        }

        private static string MapName(string mapId) => mapId == "B02.Ground" ? "Stella's classroom" :
            mapId == "campus" ? "campus" : mapId == "westForest" ? "west forest" : mapId;

        private void OnGUI()
        {
            EnsureStyles();
            if (StartupMenu.IsVisible) return;
            if (HintsEnabled && !settingsOpen && !string.IsNullOrEmpty(hint))
            {
                float width = Mathf.Min(360f, Screen.width - 32f);
                Rect card = new Rect(Screen.width - width - 16f, 58f, width, 94f);
                WorldStateService world = WorldStateService.Instance;
                if (world != null && string.Equals(world.CurrentMapId, "B02.Ground", StringComparison.OrdinalIgnoreCase) && card.xMin < 438f)
                    card.position = new Vector2(16f, 124f);
                GUI.Box(card, GUIContent.none, boxStyle);
                GUI.Label(new Rect(card.x + 14f, card.y + 9f, card.width - 28f, 24f), title ?? "Quest", titleStyle);
                GUI.Label(new Rect(card.x + 14f, card.y + 35f, card.width - 28f, 54f), hint, bodyStyle);
            }

            if (GUI.Button(new Rect(Screen.width - 136f, 16f, 120f, 34f), settingsOpen ? "Close Settings" : "Settings"))
                SetSettingsOpen(!settingsOpen);
            if (!settingsOpen) return;
            Rect panel = new Rect(Screen.width - 310f, 58f, 294f, 116f);
            GUI.Box(panel, GUIContent.none, boxStyle);
            GUI.Label(new Rect(panel.x + 14f, panel.y + 10f, panel.width - 28f, 25f), "SETTINGS", titleStyle);
            bool enabled = GUI.Toggle(new Rect(panel.x + 14f, panel.y + 48f, panel.width - 28f, 30f), HintsEnabled, "Show quest hints");
            if (enabled != HintsEnabled) SetHintsEnabled(enabled);
            GUI.Label(new Rect(panel.x + 14f, panel.y + 82f, panel.width - 28f, 24f), "Changes apply immediately.", bodyStyle);
        }

        private void SetSettingsOpen(bool open)
        {
            settingsOpen = open;
            if (open)
            {
                foreach (GridPlayerInput input in FindObjectsByType<GridPlayerInput>(FindObjectsSortMode.None))
                    if (input != null && input.enabled) { input.enabled = false; pausedInputs.Add(input); }
            }
            else RestoreGameplayInput();
        }

        private void RestoreGameplayInput()
        {
            foreach (GridPlayerInput input in pausedInputs)
                if (input != null) input.enabled = true;
            pausedInputs.Clear();
        }

        private static void EnsureStyles()
        {
            if (boxStyle != null) return;
            boxStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, padding = new RectOffset(12, 12, 8, 8) };
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true, normal = { textColor = Color.white } };
        }
    }
}
