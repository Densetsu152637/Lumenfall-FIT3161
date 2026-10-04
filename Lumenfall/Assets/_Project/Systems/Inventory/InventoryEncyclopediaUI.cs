using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Controls;
using Lumenfall.Spells;
using Lumenfall.Startup;

namespace Lumenfall.Inventory
{
    /// Shared input gate for overlays that temporarily own gameplay input.
    /// SpellCaster checks this state before handling wheel and cast input.
    public static class InventoryUiState
    {
        public static bool IsOpen { get; internal set; }
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

        private enum Page { Inventory, Encyclopedia, Settings }
        private Page page;
        private readonly GameSettingsPanel settingsPanel = new GameSettingsPanel();
        private GridMover lockedMover;
        private GameEventRuntime subscribedRuntime;
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
        }

        private void OnEnable()
        {
            SubscribeRuntime();
        }

        private void OnDisable()
        {
            UnsubscribeRuntime();
            Close();
        }

        // Starting a new game rebuilds the event runtime, so follow the live instance rather than the one seen at enable time.
        private void SubscribeRuntime()
        {
            GameEventRuntime runtime = GameEventManager.Instance?.Runtime;
            if (runtime == null || runtime == subscribedRuntime) return;
            UnsubscribeRuntime();
            subscribedRuntime = runtime;
            runtime.DialogueStarted += OnDialogueStarted;
            runtime.StateRestored += OnStateRestored;
            runtime.DialogueEnded += OnDialogueEnded;
            runtime.QuestStatusChanged += OnQuestStatusChanged;
        }

        private void UnsubscribeRuntime()
        {
            if (subscribedRuntime == null) return;
            subscribedRuntime.DialogueStarted -= OnDialogueStarted;
            subscribedRuntime.StateRestored -= OnStateRestored;
            subscribedRuntime.DialogueEnded -= OnDialogueEnded;
            subscribedRuntime.QuestStatusChanged -= OnQuestStatusChanged;
            subscribedRuntime = null;
        }

        private void Update()
        {
            SubscribeRuntime();
            // While a control is being rebound, every key (including the journal key) belongs to the rebind.
            if (InventoryUiState.IsOpen && settingsPanel.IsRebinding)
            {
                settingsPanel.Tick();
                return;
            }
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !KeyBindings.WasPressed(GameAction.Journal)) return;
            if (GameEventManager.Instance?.Runtime?.ActiveDialogue != null) return;
            if (InventoryUiState.IsOpen) Close();
            else Open();
        }

        private void Open()
        {
            if (GameEventManager.Instance?.Runtime?.ActiveDialogue != null || FindAnyObjectByType<StartupMenu>() != null) return;
            InventoryUiState.IsOpen = true;
            lockedMover = FindAnyObjectByType<GridMover>();
            if (lockedMover != null) lockedMover.SetMovementLocked(true);
        }

        private void Close()
        {
            InventoryUiState.IsOpen = false;
            settingsPanel.CancelRebind();
            if (lockedMover != null) lockedMover.SetMovementLocked(false);
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
            float width = Mathf.Min(Screen.width - 48f, 920f);
            float height = Mathf.Min(Screen.height - 48f, 640f);
            Rect panel = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            GUILayout.BeginArea(panel, panelStyle);
            GUILayout.Label("Journal", headingStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Inventory", page == Page.Inventory ? buttonStyle : GUI.skin.button)) page = Page.Inventory;
            if (GUILayout.Button("Character encyclopedia", page == Page.Encyclopedia ? buttonStyle : GUI.skin.button)) page = Page.Encyclopedia;
            if (GUILayout.Button("Settings", page == Page.Settings ? buttonStyle : GUI.skin.button)) page = Page.Settings;
            GUILayout.FlexibleSpace();
            bool returnToMenu = GUILayout.Button("Main menu", GUILayout.Width(120f));
            GUILayout.Label(KeyBindings.GetKeyLabel(GameAction.Journal) + ": close", mutedStyle, GUILayout.Width(120f));
            GUILayout.EndHorizontal();
            GUILayout.Space(12f);
            if (page == Page.Inventory) DrawInventory();
            else if (page == Page.Encyclopedia) DrawEncyclopedia();
            else DrawSettings(height);
            GUILayout.EndArea();
            if (returnToMenu)
            {
                Close();
                StartupMenu.ReturnToMainMenu();
            }
        }

        private void DrawSettings(float panelHeight)
        {
            GUILayout.Label("Audio", headingStyle);
            settingsPanel.DrawVolume(bodyStyle);
            GUILayout.Space(16f);
            GUILayout.Label("Controls", headingStyle);
            settingsPanel.DrawControls(bodyStyle, GUI.skin.button, mutedStyle, Mathf.Max(120f, panelHeight - 340f));
        }

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
