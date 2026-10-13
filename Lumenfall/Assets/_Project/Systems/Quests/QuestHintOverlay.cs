using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Inventory;
using Lumenfall.Spells;
using Lumenfall.DataDriven;
using Lumenfall.Startup;
using Lumenfall.Controls;
using Lumenfall.Cutscenes;

namespace Lumenfall.Quests
{
    /// <summary>Small contextual objective card driven by quest and puzzle runtime state.</summary>
    [DefaultExecutionOrder(500)]
    [DisallowMultipleComponent]
    public sealed class QuestHintOverlay : MonoBehaviour
    {
        private const string EnabledKey = "lumenfall.quest-hints-enabled";
        internal static readonly string[] QuestOrder =
        {
            "stella_first_light", "stella_string_them_together", "cinna_out_of_alignment",
            "cinna_cold_feet_warm_idea", "orio_patch_notes", "katrina_something_borrowed"
        };

        private static QuestHintOverlay instance;
        private QuestPuzzleDatabase puzzleDatabase;
        private string hint;
        private string title;
        private string tracker;
        private float refreshAt;
        private bool settingsOpen;
        private bool hintsCollapsed;
        private float settingsOpenedAt;
        // The pop-out pauses the world; the previous time scale comes back when it closes.
        private bool pausedTime;
        private float pausedTimeScale = 1f;
        // Glaciate frost creeps in while paused and thaws after closing, starting from however far it had grown.
        private const float FrostSeconds = 1.2f;
        private const float ThawSeconds = 0.8f;
        private float frostAtClose;
        private float closedAt = float.NegativeInfinity;
        private readonly List<GridPlayerInput> pausedInputs = new List<GridPlayerInput>();
        private readonly MenuSkin skin = new MenuSkin();
        private readonly OptionsPanel options = new OptionsPanel();
        private GUIStyle questTrackerStyle;
        private GUIStyle questHintStyle;

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

        private void OnDestroy()
        {
            if (instance == this) instance = null;
            skin.Dispose();
        }

        private void OnDisable() { SetSettingsOpen(false); }

        private void Update()
        {
            if (StartupMenu.IsVisible && settingsOpen) SetSettingsOpen(false);
            if (settingsOpen) UpdateSettings();
            else if (CanOpenFromEscape()) SetSettingsOpen(true);
            Keyboard keyboard = Keyboard.current;
            if (!settingsOpen && !StartupMenu.IsVisible && HintsEnabled && !string.IsNullOrEmpty(hint) &&
                keyboard != null && keyboard.hKey.wasPressedThisFrame)
                hintsCollapsed = !hintsCollapsed;
            if (Time.unscaledTime < refreshAt) return;
            refreshAt = Time.unscaledTime + 0.35f;
            RefreshHint();
        }

        private void RefreshHint()
        {
            hint = null;
            title = null;
            tracker = null;
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
                QuestPuzzleDefinition puzzle = FindPuzzle(id, world.CurrentMapId, runtime);
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
            if (string.Equals(activeQuest.id, KatrinaLostEchoesGuide.QuestId, StringComparison.Ordinal) &&
                KatrinaLostEchoesGuide.IsActive(runtime))
            {
                title = "Lost Echoes";
                tracker = KatrinaLostEchoesGuide.Tracker(runtime);
                hint = KatrinaLostEchoesGuide.Instructions(runtime);
                if (!runtime.GetFlag(KatrinaLostEchoesGuide.KeepsakeFlag))
                {
                    if (selectedPuzzle == null)
                        hint += "\nContinue searching for the remaining memory anchors.";
                    else if (!IsOnMap(selectedPuzzle, world.CurrentMapId))
                        hint += $"\nTravel to {MapName(selectedPuzzle.mapId)} to continue the search.";
                    else if (selectedPuzzle?.targets != null)
                    {
                        foreach (QuestPuzzleTarget sourceTarget in selectedPuzzle.targets)
                        {
                            QuestPuzzleTarget target = MergeCurrentMapTarget(sourceTarget, world);
                            if (TargetComplete(target, world)) continue;
                            string direction = DirectionTo(target, world);
                            if (!string.IsNullOrEmpty(direction)) hint += "\n" + direction;
                            break;
                        }
                    }
                }
                return;
            }
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

            if (string.Equals(activeQuest.id, "stella_string_them_together", StringComparison.Ordinal))
            {
                if (IsOnMap(selectedPuzzle, world.CurrentMapId) &&
                    StellaStringThemTogetherPuzzle.TryGetCircuitStep(out string circuitHint, out Vector2Int circuitCell))
                {
                    hint = circuitHint;
                    if (circuitCell != default)
                    {
                        string direction = DirectionToCell(circuitCell);
                        if (!string.IsNullOrEmpty(direction)) hint += "\n" + direction;
                    }
                }
                else hint = "Return to Stella's room and weave power from her relic to all five board relics.";
                return;
            }

            if (string.Equals(activeQuest.id, CinnaTrialDeparture.QuestId, StringComparison.Ordinal))
            {
                hint = CinnaAlignmentRelic.DescribeStep(world, FindAnyObjectByType<SpellCaster>());
                return;
            }

            if (string.Equals(activeQuest.id, CinnaIceTrialRoom.QuestId, StringComparison.Ordinal))
            {
                hint = IsOnMap(selectedPuzzle, world.CurrentMapId)
                    ? CinnaIceTrialRoom.DescribeStep(world)
                    : $"Enter Cinna's ice trial through the portal on the campus path at ({CinnaIceTrialEntrance.EntranceCell.x}, {CinnaIceTrialEntrance.EntranceCell.y}).";
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
                requiredCollected = questTarget.requiredCollected,
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

        private QuestPuzzleDefinition FindPuzzle(string questId, string currentMapId, GameEventRuntime runtime)
        {
            QuestPuzzleDefinition firstRemaining = null;
            foreach (QuestPuzzleDefinition puzzle in puzzleDatabase?.puzzles ?? Array.Empty<QuestPuzzleDefinition>())
            {
                if (puzzle == null || !string.Equals(puzzle.questId, questId, StringComparison.Ordinal)) continue;
                if (runtime != null && !string.IsNullOrWhiteSpace(puzzle.completionEventId) &&
                    runtime.HasEventTriggered(puzzle.completionEventId)) continue;
                if (firstRemaining == null) firstRemaining = puzzle;
                if (string.Equals(puzzle.mapId, currentMapId, StringComparison.OrdinalIgnoreCase)) return puzzle;
            }
            return firstRemaining;
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
                    (!target.requiredRevealed || state.revealed) && (!target.requiredCollected || state.collected) &&
                    (!target.requiredFrozen || state.frozen) &&
                    (!target.requiredAligned || state.aligned) && (target.requiredRotation < 0 || Mathf.RoundToInt(state.rotation) % 360 == target.requiredRotation % 360) &&
                    (target.requiredPositionX == int.MinValue || state.cell.x == target.requiredPositionX) &&
                    (target.requiredPositionY == int.MinValue || state.cell.y == target.requiredPositionY);
                any |= valid; all &= valid;
            }
            return string.Equals(target.conditionMode, "Any", StringComparison.OrdinalIgnoreCase) ? any : all;
        }

        private static string Describe(QuestPuzzleTarget target, WorldStateService world)
        {
            if (target.requiredCollected)
                foreach (string id in target.objectIds ?? Array.Empty<string>())
                    if (world.TryGetObject(id, out WorldObjectSnapshot item) && item.revealed && !item.collected)
                        return "Approach the revealed memory anchor and pick it up.";
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
            string horizontal = dx == 0 ? null : $"{Mathf.Abs(dx)} {(Mathf.Abs(dx) == 1 ? "tile" : "tiles")} to the {(dx > 0 ? "right" : "left")}";
            string vertical = dy == 0 ? null : $"{Mathf.Abs(dy)} {(Mathf.Abs(dy) == 1 ? "tile" : "tiles")} to the {(dy > 0 ? "top" : "bottom")}";
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

        private static string MapName(string mapId) => mapId == "B01.Ground" ? "Training Centre" :
            mapId == "B02.Ground" ? "Arcane Workshop" : mapId == "B03.Ground" ? "Main Academy Building" :
            mapId == "B04.Ground" ? "Library" :
            mapId == CinnaIceTrialRoom.MapId ? "Cinna's ice trial (portal at campus 47, 45)" :
            mapId == "campus" ? "campus" : mapId == "westForest" ? "west forest" : mapId;

        private void OnGUI()
        {
            if (StartupMenu.IsVisible || CutscenePlayer.IsPlaying) return;
            skin.Ensure();
            if (questTrackerStyle == null) questTrackerStyle = new GUIStyle(skin.Body) { fontSize = 13 };
            if (questHintStyle == null) questHintStyle = new GUIStyle(skin.Body) { fontSize = 13 };
            // The pop-out draws above dialogue and other overlays; IMGUI puts lower depths in front.
            GUI.depth = settingsOpen ? -10 : 0;
            if (HintsEnabled && !settingsOpen && !string.IsNullOrEmpty(hint))
            {
                float width = Mathf.Min(360f, Screen.width - 32f);
                float innerWidth = width - 28f;
                float indentedWidth = innerWidth - 16f;
                float trackerHeight = string.IsNullOrEmpty(tracker) ? 0f : questTrackerStyle.CalcHeight(new GUIContent(tracker), indentedWidth);
                float hintHeight = hintsCollapsed ? 0f : questHintStyle.CalcHeight(new GUIContent(hint), indentedWidth);
                float trackerSpace = trackerHeight > 0f ? trackerHeight + 3f : 0f;
                float cardHeight = 12f + 42f + trackerSpace + 20f + (hintsCollapsed ? 12f : hintHeight + 16f);
                Rect card = new Rect(16f, 16f, width, cardHeight);
                float now = Time.unscaledTime;
                skin.Fade = 1f;
                skin.PanelFrame(card, now);

                float pulse = 0.5f + 0.5f * Mathf.Sin(now * 2.4f);
                Rect star = new Rect(card.x + 18f, card.y + 17f, 24f, 24f);
                if (Event.current.type == EventType.Repaint)
                {
                    skin.Draw(new Rect(star.x - 9f, star.y - 9f, star.width + 18f, star.height + 18f),
                        skin.Glow, MenuArt.Gold, 0.35f + 0.3f * pulse);
                    skin.Draw(star, skin.Sparkle, Color.Lerp(MenuArt.Gold, MenuArt.Cream, pulse * 0.45f), 1f);
                }
                // Size the title rectangle to its text so centring does not create a large invisible gap after the star.
                string questTitle = title ?? "Quest";
                float titleWidth = Mathf.Min(skin.Title.CalcSize(new GUIContent(questTitle)).x + 4f, card.width - 82f);
                skin.DrawTitle(new Rect(star.xMax + 10f, card.y + 9f, titleWidth, 38f), questTitle, now);

                float contentY = card.y + 54f;
                if (trackerHeight > 0f)
                {
                    skin.Label(new Rect(card.x + 30f, contentY, indentedWidth, trackerHeight), tracker, questTrackerStyle, MenuArt.Cream);
                    contentY += trackerHeight + 3f;
                }
                string toggle = hintsCollapsed ? "Click to show hints (H)" : "Click to hide hints (H)";
                // Indent the hide/show action by roughly two characters.
                Rect toggleRect = new Rect(card.x + 30f, contentY, indentedWidth, 20f);
                bool hoverToggle = toggleRect.Contains(Event.current.mousePosition);
                skin.Label(toggleRect, toggle, skin.Small, hoverToggle ? MenuArt.Gold : MenuArt.Muted);
                if (GUI.Button(toggleRect, GUIContent.none, GUIStyle.none))
                    hintsCollapsed = !hintsCollapsed;
                contentY += 23f;
                if (!hintsCollapsed)
                    skin.Label(new Rect(card.x + 30f, contentY, indentedWidth, hintHeight), hint, questHintStyle, MenuArt.Cream);
            }

            // The journal draws its own copy of these options, so the button steps aside while it is open.
            if (InventoryUiState.IsOpen) return;
            Color previousColor = GUI.color;
            if (settingsOpen) DrawSettingsPanel();
            skin.Fade = 1f;
            if (!settingsOpen) skin.DrawFrost(ThawAmount());
            if (SettingsButton(new Rect(Screen.width - 156f, 16f, 140f, 36f))) SetSettingsOpen(!settingsOpen);
            if (settingsOpen) skin.DrawSparks(1f);
            GUI.color = previousColor;
        }

        private void UpdateSettings()
        {
            float deltaTime = Time.unscaledDeltaTime;
            skin.Tick(deltaTime);
            // While a key is being rebound or a swap is waiting for an answer, Escape only cancels that.
            bool wasRebinding = options.IsRebinding;
            options.Tick(deltaTime);
            Keyboard keyboard = Keyboard.current;
            if (!wasRebinding && keyboard != null && keyboard.escapeKey.wasPressedThisFrame) SetSettingsOpen(false);
        }

        /// <summary>
        /// Escape opens the pop-out as a pause menu. The journal also closes on Escape and updates earlier in the frame, so
        /// a journal that closed this frame must not hand the same key press to the pop-out.
        /// </summary>
        private static bool CanOpenFromEscape()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame) return false;
            if (InventoryUiState.IsOpen || InventoryUiState.ClosedFrame == Time.frameCount || CutscenePlayer.BlocksInput) return false;
            return FindAnyObjectByType<StartupMenu>() == null;
        }

        /// <summary>The Settings pop-out, styled like the startup menu: the options, then a short paused hint.</summary>
        private void DrawSettingsPanel()
        {
            float now = Time.unscaledTime;
            float open = MenuSkin.Smooth((now - settingsOpenedAt) / 0.35f);
            skin.Fade = open;
            skin.ShowWandCursor(true);
            skin.DrawBackdrop(0.85f, now);
            skin.DrawFrost(FrostAmount());

            float width = Mathf.Min(Screen.width - 48f, 860f);
            float height = Mathf.Min(Screen.height - 48f, 620f);
            Rect panel = new Rect((Screen.width - width) * 0.5f - skin.Parallax.x * 4f,
                (Screen.height - height) * 0.5f + (1f - open) * 18f * skin.Motion - skin.Parallax.y * 3f, width, height);
            skin.PanelFrame(panel, now);
            skin.DrawTitle(new Rect(panel.x + 24f, panel.y + 22f, panel.width - 48f, 50f), "Settings", now);
            skin.Divider(panel.center.x, panel.y + 84f, Mathf.Min(280f, panel.width - 120f), now);

            float hintY = panel.yMax - 46f;
            float top = panel.y + 106f;
            options.Draw(new Rect(panel.x + 36f, top, panel.width - 72f, hintY - 14f - top), skin);
            DrawPausedLine(new Rect(panel.x + 36f, hintY, panel.width - 72f, 22f));
        }

        private float FrostAmount() => skin.Motion > 0f ? MenuSkin.Smooth((Time.unscaledTime - settingsOpenedAt) / FrostSeconds) : 1f;

        private float ThawAmount() => skin.Motion > 0f ? frostAtClose * (1f - MenuSkin.Smooth((Time.unscaledTime - closedAt) / ThawSeconds)) : 0f;

        /// <summary>A frozen hourglass beside "Game paused", and how to resume.</summary>
        private void DrawPausedLine(Rect line)
        {
            const string Paused = "Game paused";
            const string next = "Esc to resume";
            GUIStyle style = skin.Status;
            float pausedWidth = style.CalcSize(new GUIContent(Paused)).x;
            float nextWidth = style.CalcSize(new GUIContent(next)).x;
            float x = line.center.x - (22f + pausedWidth + 26f + nextWidth) * 0.5f;

            skin.DrawHourglass(new Rect(x, line.center.y - 10f, 14f, 20f));
            x += 22f;
            skin.Label(new Rect(x, line.y, pausedWidth + 4f, line.height), Paused, style, MenuArt.Muted);
            x += pausedWidth;
            if (Event.current.type == EventType.Repaint) skin.Draw(new Rect(x + 11f, line.center.y - 2f, 4f, 4f), skin.Glow, MenuArt.Gold, 0.8f);
            x += 26f;
            skin.Label(new Rect(x, line.y, nextWidth + 4f, line.height), next, style, MenuArt.Muted);
        }

        /// <summary>Top-right toggle with a small gold star; it reads Close while the pop-out is open.</summary>
        private bool SettingsButton(Rect rect)
        {
            bool clicked = skin.Button(rect, settingsOpen ? "Close" : "Settings");
            if (Event.current.type == EventType.Repaint)
            {
                float lift = rect.Contains(Event.current.mousePosition) ? 2f : 0f;
                Rect star = new Rect(rect.x + 12f, rect.center.y - 8f - lift, 16f, 16f);
                skin.Draw(MenuSkin.Expand(star, 6f, 6f), skin.Glow, MenuArt.Gold, 0.45f);
                skin.Draw(star, skin.Sparkle, MenuArt.Gold, 1f);
            }
            return clicked;
        }

        private void SetSettingsOpen(bool open)
        {
            if (open && settingsOpen) return;
            bool wasOpen = settingsOpen;
            settingsOpen = open;
            if (open)
            {
                settingsOpenedAt = Time.unscaledTime;
                options.Reset();
                skin.ResetAmbience();
                pausedTimeScale = Time.timeScale;
                Time.timeScale = 0f;
                pausedTime = true;
                foreach (GridPlayerInput input in FindObjectsByType<GridPlayerInput>(FindObjectsSortMode.None))
                    if (input != null && input.enabled) { input.enabled = false; pausedInputs.Add(input); }
            }
            else
            {
                options.CancelRebind();
                skin.ShowWandCursor(false);
                if (wasOpen)
                {
                    frostAtClose = FrostAmount();
                    closedAt = Time.unscaledTime;
                }
                // Leave the time scale alone if something else changed it while the pop-out was open.
                if (pausedTime && Time.timeScale == 0f) Time.timeScale = pausedTimeScale;
                pausedTime = false;
                RestoreGameplayInput();
            }
        }

        private void RestoreGameplayInput()
        {
            foreach (GridPlayerInput input in pausedInputs)
                if (input != null) input.enabled = true;
            pausedInputs.Clear();
        }

    }
}
