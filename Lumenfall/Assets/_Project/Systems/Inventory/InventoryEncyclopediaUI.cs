using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Controls;
using Lumenfall.Quests;
using Lumenfall.Spells;
using Lumenfall.Startup;

namespace Lumenfall.Inventory
{
    /// Shared input gate for overlays that temporarily own gameplay input.
    /// SpellCaster checks this state before handling wheel and cast input.
    public static class InventoryUiState
    {
        public static bool IsOpen { get; internal set; }
        /// Frame the journal last closed on, so a key press that closed it is not reused by another overlay.
        public static int ClosedFrame { get; internal set; } = -1;
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
        private static readonly string[] KeyItemIds = { "dormitory-key", "katrina-keepsake", "optional-recovered-item" };
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
                { SpellId.Vis, "Align a compatible object, or push a relic into its socket." },
                { SpellId.LostEchoes, "Reveal hidden points of interest." }
            };

        private enum Page { Inventory, Encyclopedia, Settings }
        private static readonly string[] PageLabels = { "Inventory", "Character encyclopedia", "Settings" };
        private const float OpenSeconds = 0.35f;
        private const float PageFlipSeconds = 0.4f;

        private Page page;
        private readonly UiNavigationInput navigation = new UiNavigationInput();
        private readonly MenuSkin skin = new MenuSkin();
        private readonly OptionsPanel options = new OptionsPanel { FlipPages = true };
        private readonly Rect[] tabRects = new Rect[PageLabels.Length];
        private GridMover lockedMover;
        private GameEventRuntime subscribedRuntime;
        private float openedAt;
        private float pageChangedAt;
        private Vector2 listScroll;
        // The active-tab underline slides between tabs; it is stored relative to the panel so parallax does not drag it.
        private float underlineOffset;
        private float underlineWidth;
        private bool underlinePlaced;
        // Footer save status: it writes itself in again whenever its text changes, and a wax seal marks a Save this visit.
        private string saveStatus;
        private float saveStatusAt = float.NegativeInfinity;
        private string saveFeedback;
        private string inkText;
        private float inkedAt;
        private float sealedAt = float.NegativeInfinity;

        private enum FooterAction { None, MainMenu, Save, Quit }

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

        private void OnDestroy()
        {
            skin.Dispose();
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
            Keyboard keyboard = Keyboard.current;
            Gamepad pad = Gamepad.current;
            if (InventoryUiState.IsOpen)
            {
                float deltaTime = Time.unscaledDeltaTime;
                skin.Tick(deltaTime);
                // While a control is being rebound, every key (including the journal key and Escape) belongs to the rebind.
                bool wasRebinding = options.IsRebinding;
                if (page == Page.Settings) options.Tick(deltaTime);
                navigation.Read();
                if (page != Page.Settings && navigation.Horizontal != 0)
                { page = (Page)(((int)page + navigation.Horizontal + PageLabels.Length) % PageLabels.Length); pageChangedAt = Time.unscaledTime; listScroll = Vector2.zero; }
                if (wasRebinding) return;
                int pageStep = pad?.leftShoulder.wasPressedThisFrame == true ? -1 : pad?.rightShoulder.wasPressedThisFrame == true ? 1 : 0;
                if (pageStep != 0)
                {
                    options.CancelRebind();
                    page = (Page)(((int)page + pageStep + PageLabels.Length) % PageLabels.Length);
                    pageChangedAt = Time.unscaledTime;
                    listScroll = Vector2.zero;
                }
                if (navigation.Cancel)
                {
                    Close();
                    return;
                }
            }
            if (!InventoryUiState.IsOpen && OverlayInputGate.IsBlocked) return;
            if (!KeyBindings.WasPressed(GameAction.Journal) && pad?.selectButton.wasPressedThisFrame != true) return;
            if (GameEventManager.Instance?.Runtime?.ActiveDialogue != null) return;
            if (InventoryUiState.IsOpen) Close();
            else Open();
        }

        private void Open()
        {
            if (GameEventManager.Instance?.Runtime?.ActiveDialogue != null || FindAnyObjectByType<StartupMenu>() != null ||
                QuestHintOverlay.IsSettingsOpen) return;
            InventoryUiState.IsOpen = true;
            OverlayInputGate.Acquire(this);
            openedAt = pageChangedAt = Time.unscaledTime;
            underlinePlaced = false;
            saveFeedback = null;
            saveStatusAt = sealedAt = float.NegativeInfinity;
            inkText = null;
            options.Reset();
            skin.ResetAmbience();
            lockedMover = FindAnyObjectByType<GridMover>();
            if (lockedMover != null) lockedMover.SetMovementLocked(this, true);
        }

        private void Close()
        {
            if (InventoryUiState.IsOpen) InventoryUiState.ClosedFrame = Time.frameCount;
            InventoryUiState.IsOpen = false;
            OverlayInputGate.Release(this);
            options.CancelRebind();
            skin.ShowWandCursor(false);
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
            if (current == QuestStatus.Completed && quest != null) GrantMetadataItem(quest.metadata);
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
            skin.Ensure();
            skin.ShowWandCursor(true);
            // Draw above the quest hint card and dialogue; IMGUI puts lower depths in front.
            GUI.depth = -10;
            Color previousColor = GUI.color;
            float now = Time.unscaledTime;
            float open = MenuSkin.Smooth((now - openedAt) / OpenSeconds);
            skin.Fade = open;
            skin.DrawBackdrop(0.85f, now);

            float width = Mathf.Min(Screen.width - 48f, 920f);
            float height = Mathf.Min(Screen.height - 48f, 640f);
            Rect panel = new Rect((Screen.width - width) * 0.5f - skin.Parallax.x * 4f,
                (Screen.height - height) * 0.5f + (1f - open) * 18f * skin.Motion - skin.Parallax.y * 3f, width, height);
            skin.PanelFrame(panel, now);
            skin.DrawTitle(new Rect(panel.x + 24f, panel.y + 20f, panel.width - 48f, 50f), "Journal", now);
            skin.Divider(panel.center.x, panel.y + 80f, Mathf.Min(280f, panel.width - 120f), now);

            float tabY = panel.y + 98f;
            DrawTabs(panel, tabY);
            Rect lastTab = tabRects[tabRects.Length - 1];
            skin.Label(new Rect(lastTab.xMax + 16f, tabY, panel.xMax - 36f - lastTab.xMax - 16f, 36f),
                KeyBindings.GetKeyLabel(GameAction.Journal) + " or Esc to close", skin.SmallRight, MenuArt.Muted);

            // Choosing a tab turns a page in from the left, like a book; Reduce motion shows the page at once.
            float turn = skin.Motion > 0f ? Mathf.Clamp01((now - pageChangedAt) / PageFlipSeconds) : 1f;
            float top = tabY + 62f;
            Rect content = new Rect(panel.x + 36f, top, panel.width - 72f, panel.yMax - 72f - top);
            Matrix4x4 unturned = skin.BeginPageFlip(content, turn);
            if (page == Page.Settings) options.Draw(content, skin);
            else DrawListPage(content);
            skin.EndPageFlip(content, turn, unturned);

            skin.Fade = open;
            FooterAction action = DrawFooter(panel);
            skin.Fade = 1f;
            skin.DrawSparks(1f);
            GUI.color = previousColor;
            if (action == FooterAction.MainMenu)
            {
                Close();
                StartupMenu.ReturnToMainMenu();
            }
            else if (action == FooterAction.Save) SaveNow();
            else if (action == FooterAction.Quit) QuitGame();
        }

        /// <summary>Main menu, Save to the game's slot (with the hourglass) and Quit game, then the inked save status.</summary>
        private FooterAction DrawFooter(Rect panel)
        {
            const float Height = 36f;
            const float Gap = 12f;
            float y = panel.yMax - 58f;
            float x = panel.x + 36f;
            FooterAction action = FooterAction.None;
            if (skin.Button(new Rect(x, y, 140f, Height), "Main menu")) action = FooterAction.MainMenu;
            x += 140f + Gap;
            if (SaveButton(new Rect(x, y, 160f, Height))) action = FooterAction.Save;
            x += 160f + Gap;
            if (skin.Button(new Rect(x, y, 130f, Height), "Quit game")) action = FooterAction.Quit;
            x += 130f + Gap;
            DrawSaveStatus(new Rect(x + 8f, y, panel.xMax - 36f - x - 8f, Height));
            return action;
        }

        private bool SaveButton(Rect rect)
        {
            string slot = ManualSaveSlot.Linked;
            bool clicked = skin.Button(rect, slot != null ? "Save · " + ManualSaveSlot.Label(slot) : "Save");
            float lift = rect.Contains(Event.current.mousePosition) ? 2f : 0f;
            skin.DrawHourglass(new Rect(rect.x + 10f, rect.center.y - 9f - lift, 12f, 18f));
            return clicked;
        }

        /// <summary>Right-aligned "Last saved 2 min ago" that writes itself in like ink, with the wax seal after a Save.</summary>
        private void DrawSaveStatus(Rect area)
        {
            const float SealSize = 24f;
            string status = SaveStatus();
            if (status != inkText)
            {
                inkText = status;
                inkedAt = Time.unscaledTime;
            }
            GUIStyle style = skin.Status;
            float textWidth = style.CalcSize(new GUIContent(status)).x;
            bool stamped = !float.IsNegativeInfinity(sealedAt) && saveFeedback == null;
            float x = Mathf.Max(area.x, area.xMax - textWidth - (stamped ? SealSize + 8f : 0f));
            if (stamped)
            {
                skin.DrawSeal(new Rect(x, area.center.y - SealSize * 0.5f, SealSize, SealSize), sealedAt);
                x += SealSize + 8f;
            }
            skin.InkLabel(new Rect(x, area.y, Mathf.Max(0f, area.xMax - x), area.height), status, style, inkedAt,
                saveFeedback != null ? MenuArt.Ember : MenuArt.Muted);
        }

        /// <summary>"Slot 1 saved 2 min ago", from the linked slot's file time. The file is checked at most once a second.</summary>
        private string SaveStatus()
        {
            if (saveFeedback != null) return saveFeedback;
            if (Time.unscaledTime - saveStatusAt < 1f && saveStatus != null) return saveStatus;
            saveStatusAt = Time.unscaledTime;
            string slot = ManualSaveSlot.Linked;
            if (slot == null) return saveStatus = ManualSaveSlot.FirstEmpty() != null ? "Save picks an empty slot" : "No save slot yet";
            string label = ManualSaveSlot.Label(slot);
            if (!GameSaveManager.Instance.TryGetSavedAt(slot, out DateTime savedAt)) return saveStatus = label + " not saved yet";
            TimeSpan age = DateTime.Now - savedAt;
            saveStatus = age.TotalSeconds < 10 ? label + " saved just now"
                : age.TotalMinutes < 1 ? label + " saved moments ago"
                : age.TotalHours < 1 ? $"{label} saved {(int)age.TotalMinutes} min ago"
                : age.TotalDays < 1 ? $"{label} saved {(int)age.TotalHours} h ago"
                : label + " saved " + savedAt.ToString("d MMM 'at' h:mm tt", System.Globalization.CultureInfo.InvariantCulture).ToLowerInvariant();
            return saveStatus;
        }

        /// <summary>
        /// Saves to the game's manual slot (the autosave is refreshed too). A game with no slot yet, such as one carried
        /// over from before slots were linked, takes the first empty slot; if all are full it asks the player to load one.
        /// </summary>
        private void SaveNow()
        {
            saveStatusAt = float.NegativeInfinity;
            if (ManualSaveSlot.Linked == null)
            {
                string empty = ManualSaveSlot.FirstEmpty();
                if (empty == null)
                {
                    saveFeedback = "Every slot is full. Load one from the main menu to save over it.";
                    return;
                }
                ManualSaveSlot.Link(empty);
            }
            bool saved = ManualSaveSlot.SaveToLinked(out string error);
            saveFeedback = saved ? null : error ?? "Could not save. Please try again.";
            if (saved) sealedAt = Time.unscaledTime;
        }

        /// <summary>
        /// Autosaves (unless nothing has been played since the last load), then quits. Like the main menu's Exit, quitting
        /// does nothing in the editor.
        /// </summary>
        private static void QuitGame()
        {
            if (GameAutosave.HasPlayedSinceLoad) GameAutosave.SaveNow();
            Application.Quit();
        }

        private void DrawTabs(Rect panel, float y)
        {
            float x = panel.x + 36f;
            for (int i = 0; i < PageLabels.Length; i++)
            {
                float tabWidth = skin.Tab.CalcSize(new GUIContent(PageLabels[i])).x;
                tabRects[i] = new Rect(x, y, tabWidth, 36f);
                x += tabWidth + 8f;
                if (skin.TabButton(tabRects[i], PageLabels[i], (int)page == i)) SetPage((Page)i);
            }

            if (Event.current.type != EventType.Repaint) return;
            skin.Draw(new Rect(panel.x + 36f, y + 44f, panel.width - 72f, 2f), skin.Glow, MenuArt.Gold, 0.3f);
            Rect active = tabRects[(int)page];
            float blend = underlinePlaced ? 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime) : 1f;
            underlineOffset = Mathf.Lerp(underlineOffset, active.center.x - panel.x, blend);
            underlineWidth = Mathf.Lerp(underlineWidth, active.width - 20f, blend);
            underlinePlaced = true;
            skin.TabUnderline(panel.x + underlineOffset, y + 44f, underlineWidth);
        }

        private void SetPage(Page next)
        {
            if (next == page) return;
            if (page == Page.Settings) options.CancelRebind();
            page = next;
            pageChangedAt = Time.unscaledTime;
            listScroll = Vector2.zero;
        }

        private void DrawListPage(Rect content)
        {
            using (skin.ThemedScrollbars())
            {
                skin.SetColor(Color.white, 1f);
                GUILayout.BeginArea(content);
                listScroll = GUILayout.BeginScrollView(listScroll);
                if (page == Page.Inventory) DrawInventory();
                else DrawEncyclopedia();
                GUILayout.EndScrollView();
                GUILayout.EndArea();
            }
        }

        private void DrawInventory()
        {
            Heading("Owned key items");
            bool shown = false;
            foreach (string id in KeyItemIds)
            {
                if (!IsKeyItemOwned(id)) continue;
                shown = true;
                GUILayout.BeginVertical(skin.Entry);
                GUILayout.Label(id == "dormitory-key" ? "Dormitory key" : id == "katrina-keepsake" ? "Katrina's keepsake" : id == "optional-recovered-item" ? "Recovered item (optional)" : id, skin.EntryTitle);
                GUILayout.EndVertical();
            }
            if (!shown) GUILayout.Label("No key items discovered yet.", skin.Muted);

            GUILayout.Space(14f);
            Heading("Unlocked spells");
            SpellCaster caster = FindAnyObjectByType<SpellCaster>();
            bool spellsShown = false;
            foreach (SpellId spell in (SpellId[])Enum.GetValues(typeof(SpellId)))
            {
                if (caster == null || !caster.IsSpellLearned(spell)) continue;
                spellsShown = true;
                GUILayout.BeginHorizontal(skin.Entry);
                Texture2D icon = Resources.Load<Texture2D>(SpellCaster.GetIconResourcePath(spell));
                if (icon != null)
                {
                    GUILayout.Label(icon, GUIStyle.none, GUILayout.Width(40f), GUILayout.Height(40f));
                    GUILayout.Space(12f);
                }
                GUILayout.BeginVertical();
                GUILayout.Label(spell.ToString(), skin.EntryTitle);
                GUILayout.Label(SpellDescriptions[spell], skin.Muted);
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
            }
            if (!spellsShown) GUILayout.Label("No spells unlocked yet.", skin.Muted);
        }

        private void DrawEncyclopedia()
        {
            Heading("Discovered characters");
            GameEventRuntime runtime = GameEventManager.Instance?.Runtime;
            bool shown = false;
            foreach (KeyValuePair<string, CharacterEntry> pair in CharacterEntries)
            {
                if (runtime == null || !runtime.GetFlag(CharacterFlagPrefix + pair.Key)) continue;
                shown = true;
                GUILayout.BeginVertical(skin.Entry);
                GUILayout.Label(pair.Value.Name, skin.EntryTitle);
                GUILayout.Label(pair.Value.Description, skin.Muted);
                GUILayout.EndVertical();
            }
            if (!shown) GUILayout.Label("Talk to a character to add them here.", skin.Muted);
        }

        /// <summary>Gold section heading laid out in the current GUILayout group.</summary>
        private void Heading(string text)
        {
            Rect rect = GUILayoutUtility.GetRect(10f, 28f, GUILayout.ExpandWidth(true));
            skin.SectionHeading(rect, text);
            // The heading's star tints GUI.color gold; reset it so the entries below keep their own colours.
            skin.SetColor(Color.white, 1f);
            GUILayout.Space(6f);
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
