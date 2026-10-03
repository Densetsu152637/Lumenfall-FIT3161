using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Spells;

namespace Lumenfall.Inventory
{
    /// Shared input gate for overlays that temporarily own gameplay input.
    /// SpellCaster checks this state before handling wheel and cast input.
    public static class InventoryUiState
    {
        public static bool IsOpen { get; internal set; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { IsOpen = false; }
    }

    /// Runtime inventory and character encyclopedia. It is bootstrapped after a scene loads so
    /// scenes do not need manual UI wiring. Mutable discovery state is represented by event-runtime
    /// flags and therefore travels through the existing save provider.
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class InventoryEncyclopediaUI : MonoBehaviour
    {
        private const string CharacterFlagPrefix = "character.discovered.";
        private const string ItemFlagPrefix = "inventory.keyitem.";
        private static readonly string[] KeyItemIds = { "dormitory-key", "katrina-keepsake" };
        private static readonly Dictionary<string, CharacterEntry> CharacterEntries =
            new Dictionary<string, CharacterEntry>(StringComparer.OrdinalIgnoreCase)
            {
                { "stella", new CharacterEntry("Stella", "A student working through practical relic and circuit puzzles.") },
                { "cinna_cirra", new CharacterEntry("Cinna Cirra", "An orientation guide learning how the academy\'s practical objects fit together.") },
                { "cinna_cirra_orientation", new CharacterEntry("Cinna Cirra", "An orientation guide learning how the academy\'s practical objects fit together.") },
                { "cinna_cirra_trial", new CharacterEntry("Cinna Cirra", "An orientation guide learning how the academy\'s practical objects fit together.") },
                { "orio", new CharacterEntry("Orio", "A clever, reliable student who enjoys games, automation, and optimisation.") },
                { "katrina", new CharacterEntry("Katrina", "A student searching for a keepsake while keeping its importance understated.") },
                { "campus_student", new CharacterEntry("Campus student", "A student seen around the courtyard and commons, sharing practical campus observations.") },
                { "forest_student", new CharacterEntry("Forest student", "A student familiar with the lantern trail and quiet forest practice spots.") }
            };
        private static readonly Dictionary<SpellId, string> SpellDescriptions =
            new Dictionary<SpellId, string>
            {
                { SpellId.Ampere, "Toggle power in a compatible object." },
                { SpellId.Weave, "Connect a compatible relic to a powered partner." },
                { SpellId.Gravitate, "Move a compatible object across the grid." },
                { SpellId.Rotara, "Rotate a compatible mechanism." },
                { SpellId.Glaciate, "Freeze and slide a compatible object." },
                { SpellId.Vis, "Align a compatible object." },
                { SpellId.LostEchoes, "Reveal hidden points of interest." }
            };

        private enum Page { Objectives, Inventory, Encyclopedia }
        private Page page;
        private GameEventRuntime subscribedRuntime;
        private readonly Lumenfall.Startup.UiNavigationInput navigation = new Lumenfall.Startup.UiNavigationInput();
        private Vector2 scrollPosition;
        private Lumenfall.Quests.QuestPuzzleDatabase objectiveDefinitions;
        private int openedFrame;
        private GridMover lockedMover;
        private GUIStyle panelStyle;
        private GUIStyle headingStyle;
        private GUIStyle bodyStyle;
        private GUIStyle mutedStyle;
        private GUIStyle buttonStyle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindAnyObjectByType<InventoryEncyclopediaUI>() != null) return;
            GameObject host = new GameObject("[InventoryEncyclopediaUI]");
            host.AddComponent<InventoryEncyclopediaUI>();
        }

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            TextAsset objectives = Resources.Load<TextAsset>("Quests/quest_puzzle_definitions");
            if (objectives != null)
            {
                try { objectiveDefinitions = JsonUtility.FromJson<Lumenfall.Quests.QuestPuzzleDatabase>(objectives.text); }
                catch (Exception exception) { Debug.LogWarning("Journal objectives could not be loaded: " + exception.Message, this); }
            }
        }

        private void OnEnable()
        {
            BindRuntime();
        }

        private void OnDisable()
        {
            GameEventRuntime runtime = subscribedRuntime;
            if (runtime != null)
            {
                runtime.DialogueStarted -= OnDialogueStarted;
                runtime.StateRestored -= OnStateRestored;
                runtime.DialogueEnded -= OnDialogueEnded;
                runtime.QuestStatusChanged -= OnQuestStatusChanged;
            }
            subscribedRuntime = null;
            Close();
        }

        private void BindRuntime()
        {
            GameEventRuntime runtime = GameEventManager.Instance.Runtime;
            if (runtime == subscribedRuntime) return;
            if (subscribedRuntime != null)
            {
                subscribedRuntime.DialogueStarted -= OnDialogueStarted;
                subscribedRuntime.StateRestored -= OnStateRestored;
                subscribedRuntime.DialogueEnded -= OnDialogueEnded;
                subscribedRuntime.QuestStatusChanged -= OnQuestStatusChanged;
            }
            subscribedRuntime = runtime;
            if (runtime == null) return;
            runtime.DialogueStarted += OnDialogueStarted;
            runtime.StateRestored += OnStateRestored;
            runtime.DialogueEnded += OnDialogueEnded;
            runtime.QuestStatusChanged += OnQuestStatusChanged;
        }

        private void Update()
        {
            BindRuntime();
            bool toggle = Keyboard.current?.tabKey.wasPressedThisFrame == true || Gamepad.current?.selectButton.wasPressedThisFrame == true;
            if (InventoryUiState.IsOpen)
            {
                navigation.Read();
                if (Time.frameCount <= openedFrame || OverlayInputGate.IsBlockedExcept(this)) return;
                if (toggle || navigation.Cancel) { Close(); return; }
                if (navigation.Horizontal != 0) { page = (Page)UiFocusIndex.Move((int)page, navigation.Horizontal, 3); scrollPosition = Vector2.zero; }
                scrollPosition.y = Mathf.Max(0f, scrollPosition.y + navigation.Vertical * 70f);
            }
            else if (toggle && !OverlayInputGate.IsBlocked) Open();
        }

        private void Open()
        {
            if (OverlayInputGate.IsBlocked || GameEventManager.Instance?.Runtime?.ActiveDialogue != null) return;
            InventoryUiState.IsOpen = true;
            OverlayInputGate.Acquire(this);
            openedFrame = Time.frameCount;
            scrollPosition = Vector2.zero;
            lockedMover = FindAnyObjectByType<GridMover>();
            if (lockedMover != null) lockedMover.SetMovementLocked(this, true);
        }

        private void Close()
        {
            InventoryUiState.IsOpen = false;
            OverlayInputGate.Release(this);
            if (lockedMover != null) lockedMover.SetMovementLocked(this, false);
            lockedMover = null;
        }

        private void OnDialogueStarted(DialogueSession session)
        {
            if (InventoryUiState.IsOpen) Close();
            if (session == null || session.Definition == null) return;
            string npcId = MetadataValue(session.Definition.metadata, "npcId");
            if (!string.IsNullOrWhiteSpace(npcId)) DiscoverCharacter(npcId);
        }

        private static void OnDialogueEnded(DialogueSession session)
        {
            if (session == null || !session.IsCompleted) return;
            GrantMetadataItem(session.Definition?.metadata);
        }

        private static void OnQuestStatusChanged(QuestDefinition quest, QuestStatus previous, QuestStatus current)
        {
            if (current != QuestStatus.Completed || quest == null) return;
            GrantMetadataItem(quest.metadata);
        }

        private static void GrantMetadataItem(GameMetadataEntry[] metadata)
        {
            string item = MetadataValue(metadata, "keyItemReward");
            if (!string.IsNullOrWhiteSpace(item)) GrantKeyItem(item);
        }

        private static void OnStateRestored() { }

        private void OnGUI()
        {
            if (!InventoryUiState.IsOpen) return;
            EnsureStyles();
            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && !OverlayInputGate.IsBlockedExcept(this) && Time.frameCount > openedFrame;
            float width = Mathf.Min(Screen.width - 48f, 920f);
            float height = Mathf.Min(Screen.height - 48f, 640f);
            Rect panel = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            GUILayout.BeginArea(panel, panelStyle);
            GUILayout.Label("Journal", headingStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Objectives", page == Page.Objectives ? buttonStyle : GUI.skin.button)) { page = Page.Objectives; scrollPosition = Vector2.zero; }
            if (GUILayout.Button("Inventory", page == Page.Inventory ? buttonStyle : GUI.skin.button)) page = Page.Inventory;
            if (GUILayout.Button("Character encyclopedia", page == Page.Encyclopedia ? buttonStyle : GUI.skin.button)) page = Page.Encyclopedia;
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", GUILayout.Width(70f))) Close();
            GUILayout.EndHorizontal();
            GUILayout.Space(12f);
            GUILayout.Label("Left/Right or stick: page   Up/Down: scroll   Tab / Esc / B: close", mutedStyle);
            scrollPosition = GUILayout.BeginScrollView(scrollPosition);
            if (page == Page.Objectives) DrawObjectives(); else if (page == Page.Inventory) DrawInventory(); else DrawEncyclopedia();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUI.enabled = previousEnabled;
        }

        private void DrawObjectives()
        {
            GameEventRuntime runtime = GameEventManager.Instance.Runtime;
            bool shown = false;
            foreach (QuestStatus status in new[] { QuestStatus.Active, QuestStatus.Completed })
            foreach (QuestDefinition quest in runtime.Catalog.Quests.Values)
            {
                if (runtime.GetQuestStatus(quest.id) != status) continue;
                shown = true;
                GUILayout.Label((quest.title ?? quest.id) + " - " + status, headingStyle);
                string objective = MetadataValue(quest.metadata, "objective");
                GUILayout.Label(string.IsNullOrWhiteSpace(objective) ? quest.description ?? "" : objective, bodyStyle);
                string location = MetadataValue(quest.metadata, "location");
                if (!string.IsNullOrWhiteSpace(location)) GUILayout.Label("Location: " + location, mutedStyle);
                if (status == QuestStatus.Active)
                foreach (var puzzle in objectiveDefinitions?.puzzles ?? Array.Empty<Lumenfall.Quests.QuestPuzzleDefinition>())
                {
                    if (puzzle == null || puzzle.questId != quest.id) continue;
                    GUILayout.Label("Area: " + AreaName(puzzle.mapId), mutedStyle);
                    foreach (var target in puzzle.targets ?? Array.Empty<Lumenfall.Quests.QuestPuzzleTarget>())
                        if (target != null) GUILayout.Label(DescribeTarget(target), bodyStyle);
                }
                GUILayout.Space(14f);
            }
            if (!shown) GUILayout.Label("No quests accepted yet. Talk to nearby students to find your next objective.", mutedStyle);
        }

        private static string DescribeTarget(Lumenfall.Quests.QuestPuzzleTarget target)
        {
            List<string> conditions = new List<string>();
            if (target.requiredPowered) conditions.Add("powered");
            if (target.requiredConnected) conditions.Add("connected");
            if (target.requiredRevealed) conditions.Add("revealed");
            if (target.requiredFrozen) conditions.Add("frozen");
            if (target.requiredAligned) conditions.Add("aligned");
            if (target.requiredRotation >= 0) conditions.Add("rotation " + target.requiredRotation + " degrees");
            if (target.requiredPositionX != int.MinValue && target.requiredPositionY != int.MinValue)
                conditions.Add(target.requiredFrozen ? "reach the trial destination" : "at the quest destination");
            List<string> labels = new List<string>();
            foreach (string id in target.objectIds ?? Array.Empty<string>()) labels.Add(ObjectName(id));
            string join = string.Equals(target.conditionMode, "Any", StringComparison.OrdinalIgnoreCase) ? " or " : " and ";
            return string.Join(join, labels) + ": " + (conditions.Count == 0 ? "follow the quest instructions" : string.Join(", ", conditions));
        }

        private static string AreaName(string id)
        {
            switch (id)
            {
                case "campus": return "Campus commons";
                case "eastForest": return "East forest";
                case "B01.Ground": return "Arrival building";
                case "B02.Ground": return "Teaching building";
                case "B03.Ground": return "Shared facilities";
                case "B04.Ground": return "Sirius dormitory";
                default: return ReadableId(id);
            }
        }
        private static string ObjectName(string id)
        {
            switch (id)
            {
                case "relic.stella.power": return "Stella's power relic";
                case "circuit.stella.link": return "Stella's circuit relic";
                case "orientation.cinna.align": return "Cinna's orientation marker";
                case "trial.cinna.ice": return "Cinna's ice trial";
                case "mechanism.orio.rotate": return "Orio's mechanism";
                case "keepsake.katrina.hidden": return "Katrina's keepsake";
                default: return ReadableId(id);
            }
        }
        private static string ReadableId(string id) => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase((id ?? "Quest object").Replace('.', ' ').Replace('_', ' ').Replace('-', ' '));

        private void DrawInventory()
        {
            GUILayout.Label("Owned key items", headingStyle);
            GameEventRuntime runtime = GameEventManager.Instance?.Runtime;
            bool shown = false;
            foreach (string id in KeyItemIds)
            {
                if (!IsKeyItemOwned(id)) continue;
                shown = true;
                GUILayout.Label(id == "dormitory-key" ? "Dormitory key" : id == "katrina-keepsake" ? "Katrina's keepsake" : id, bodyStyle);
            }
            if (!shown) GUILayout.Label("No key items discovered yet.", mutedStyle);

            GUILayout.Space(18f);
            GUILayout.Label("Unlocked spells", headingStyle);
            SpellCaster caster = FindAnyObjectByType<SpellCaster>();
            bool spellsShown = false;
            foreach (SpellId spell in (SpellId[])Enum.GetValues(typeof(SpellId)))
            {
                if (caster == null || !caster.IsSpellUnlocked(spell)) continue;
                spellsShown = true;
                GUILayout.BeginHorizontal(GUILayout.Height(42f));
                Texture2D icon = Resources.Load<Texture2D>(SpellCaster.GetIconResourcePath(spell));
                if (icon != null) GUILayout.Label(icon, GUILayout.Width(36f), GUILayout.Height(36f));
                GUILayout.BeginVertical();
                GUILayout.Label(spell.ToString(), bodyStyle);
                GUILayout.Label(SpellDescriptions[spell], mutedStyle);
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
            }
            if (!spellsShown) GUILayout.Label("No spells unlocked yet.", mutedStyle);
        }

        private void DrawEncyclopedia()
        {
            GUILayout.Label("Discovered characters", headingStyle);
            GameEventRuntime runtime = GameEventManager.Instance?.Runtime;
            bool shown = false;
            foreach (KeyValuePair<string, CharacterEntry> pair in CharacterEntries)
            {
                if (!runtime.GetFlag(CharacterFlagPrefix + pair.Key)) continue;
                shown = true;
                GUILayout.Label(pair.Value.Name, bodyStyle);
                GUILayout.Label(pair.Value.Description, mutedStyle);
                GUILayout.Space(8f);
            }
            if (!shown) GUILayout.Label("Talk to a character to add them here.", mutedStyle);
        }

        public static void DiscoverCharacter(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || !CharacterEntries.ContainsKey(id)) return;
            GameEventManager.Instance?.Runtime?.SetFlag(CharacterFlagPrefix + id, true);
        }

        public static void GrantKeyItem(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            GameEventManager.Instance?.Runtime?.SetFlag(ItemFlagPrefix + id, true);
        }

        // Compatibility alias for integrations that used the earlier discovery name.
        public static void DiscoverKeyItem(string id) { GrantKeyItem(id); }

        public static bool IsKeyItemOwned(string id)
        {
            return !string.IsNullOrWhiteSpace(id) && GameEventManager.Instance?.Runtime?.GetFlag(ItemFlagPrefix + id) == true;
        }

        public static bool IsCharacterDiscovered(string id)
        {
            return !string.IsNullOrWhiteSpace(id) && CharacterEntries.ContainsKey(id) &&
                   GameEventManager.Instance?.Runtime?.GetFlag(CharacterFlagPrefix + id) == true;
        }

        private void EnsureStyles()
        {
            if (panelStyle != null) return;
            panelStyle = new GUIStyle(GUI.skin.box) { padding = new RectOffset(22, 22, 18, 18) };
            headingStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, wordWrap = true, normal = { textColor = Color.white } };
            bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 17, wordWrap = true, normal = { textColor = Color.white } };
            mutedStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true, normal = { textColor = new Color(0.75f, 0.78f, 0.82f) } };
            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 16 };
        }

        private static string MetadataValue(GameMetadataEntry[] metadata, string key)
        {
            foreach (GameMetadataEntry entry in metadata ?? Array.Empty<GameMetadataEntry>())
                if (entry != null && string.Equals(entry.key, key, StringComparison.OrdinalIgnoreCase)) return entry.value;
            return null;
        }

        private readonly struct CharacterEntry
        {
            public CharacterEntry(string name, string description) { Name = name; Description = description; }
            public string Name { get; }
            public string Description { get; }
        }
    }
}
