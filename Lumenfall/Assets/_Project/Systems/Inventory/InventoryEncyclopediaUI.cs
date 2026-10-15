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
using Lumenfall.Cutscenes;
using Lumenfall.Story;

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
        private static readonly string[] KeyItemIds =
        {
            "dormitory-key", "katrina-keepsake", "optional-recovered-item", "katrina-library-anchor", "katrina-garden-anchor", "katrina-stream-anchor"
        };
        private static readonly Dictionary<string, KeyItemEntry> KeyItemEntries =
            new Dictionary<string, KeyItemEntry>(StringComparer.OrdinalIgnoreCase)
            {
                { "katrina-keepsake", new KeyItemEntry("Katrina's keepsake", "Katrina's recovered keepsake.", null) },
                { "optional-recovered-item", new KeyItemEntry("Recovered item (optional)", "An optional recovered item.", null) },
                { "dormitory-key", new KeyItemEntry("Dormitory Key", "Your key to the dormitory room assigned at reception.", null) },
                { "katrina-library-anchor", new KeyItemEntry("Brass Bookmark", "Katrina's brass bookmark, its edge covered in tiny corrections.", "KeyItemIcons/KatrinaBrassBookmark") },
                { "katrina-garden-anchor", new KeyItemEntry("Silver Constellation Ribbon", "A silver ribbon embroidered with three slightly crooked constellations.", "KeyItemIcons/KatrinaSilverRibbon") },
                { "katrina-stream-anchor", new KeyItemEntry("Riverglass Chime", "Katrina's riverglass chime, still humming softly in time with running water.", "KeyItemIcons/KatrinaRiverglassChime") }
            };
        private static readonly Dictionary<string, CharacterEntry> CharacterEntries =
            new Dictionary<string, CharacterEntry>(StringComparer.OrdinalIgnoreCase)
            {
                { "stella", new CharacterEntry("Stella", "A student working through practical relic and circuit puzzles.") },
                { "cinna_cirra", new CharacterEntry("Cinna Cirra", "An orientation guide learning how the academy\'s practical objects fit together.") },
                { "orio", new CharacterEntry("Orio", "A clever, reliable student who enjoys games, automation, and optimisation.") },
                { "katrina", new CharacterEntry("Katrina", "A student searching for a keepsake while keeping its importance understated.") },
                { "campus_student", new CharacterEntry("Kit", "A friendly student by the campus entrance who gives newcomers the lay of the land.") },
                { "forest_student", new CharacterEntry("Grace", "A student familiar with the lantern trail and quiet forest practice spots.") },
                { "prof_bann", new CharacterEntry("Prof. Bann", "The headmistress of Lumenfall, found by the fountain in the courtyard.") },
                { "prof_voltaire", new CharacterEntry("Prof. Voltaire", "A professor who keeps the Library in order, and has opinions about overdue books.") },
                { "vex", new CharacterEntry("Vex", "A student who treats the Library's quiet as a personal responsibility.") },
                { "luna", new CharacterEntry("Luna", "A student who spends more time looking at the sky than at the path.") },
                { "juno", new CharacterEntry("Juno", "A student who always seems to know the latest campus news first.") },
                { "rowan", new CharacterEntry("Rowan", "A student who has already claimed the dormitory lobby as their own.") },
                { "marigold", new CharacterEntry("Marigold", "One of the dormitory receptionists, keeper of the room list and the key rack.") },
                { "thistle", new CharacterEntry("Thistle", "One of the dormitory receptionists, who always seems to be mid-way through finding something.") },
                { "moss", new CharacterEntry("Moss", "A student who goes to the forest to think, and to listen to it.") }
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
        // The Miscellaneous item whose description is open, if any.
        private string openMiscItem;
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
        private GUIStyle wrappedSaveStatusStyle;

        private enum FooterAction { None, MainMenu, QuickSave, Save, Quit }

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
                QuestHintOverlay.IsSettingsOpen || CutscenePlayer.BlocksInput) return;
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
            else if (action == FooterAction.QuickSave) QuickSave();
            else if (action == FooterAction.Save)
            {
                Close();
                StartupMenu.OpenSaveSlotPicker();
            }
            else if (action == FooterAction.Quit) QuitGame();
        }

        /// <summary>Main menu, quick save, manual slot selection and Quit game, then the inked save status.</summary>
        private FooterAction DrawFooter(Rect panel)
        {
            const float Height = 36f;
            const float Gap = 12f;
            float y = panel.yMax - 58f;
            float x = panel.x + 36f;
            FooterAction action = FooterAction.None;
            if (skin.Button(new Rect(x, y, 126f, Height), "Main menu")) action = FooterAction.MainMenu;
            x += 126f + Gap;
            string quickSaveSlot = ManualSaveSlot.Linked;
            if (quickSaveSlot != null && GameSaveManager.Instance.HasSave(quickSaveSlot))
            {
                // Wide enough for the hourglass, "Quick Save · Slot 3", and trailing text breathing room.
                if (QuickSaveButton(new Rect(x, y, 230f, Height))) action = FooterAction.QuickSave;
                x += 230f + Gap;
            }
            if (skin.Button(new Rect(x, y, 104f, Height), "Save")) action = FooterAction.Save;
            x += 104f + Gap;
            if (skin.Button(new Rect(x, y, 108f, Height), "Quit game")) action = FooterAction.Quit;
            x += 108f + Gap;
            DrawSaveStatus(new Rect(x + 8f, y - 2f, panel.xMax - 36f - x - 8f, Height + 4f));
            return action;
        }

        private bool QuickSaveButton(Rect rect)
        {
            string slot = ManualSaveSlot.Linked;
            // Leave room inside the label for the hourglass at the left, plus one character of breathing room at the end.
            string label = "   Quick Save · " + ManualSaveSlot.Label(slot) + " ";
            bool clicked = skin.Button(rect, label);
            float lift = rect.Contains(Event.current.mousePosition) ? 2f : 0f;
            skin.DrawHourglass(new Rect(rect.x + 10f, rect.center.y - 9f - lift, 12f, 18f));
            return clicked;
        }

        /// <summary>
        /// Right-aligned "Last saved 2 min ago" that writes itself in like ink, with the wax seal after a Save.
        /// On narrow screens the status uses the full remaining footer width and wraps onto a second line.
        /// </summary>
        private void DrawSaveStatus(Rect area)
        {
            const float SealSize = 24f;
            const float SealGap = 8f;
            string status = SaveStatus();
            if (status != inkText)
            {
                inkText = status;
                inkedAt = Time.unscaledTime;
            }
            GUIStyle style = skin.Status;
            float textWidth = style.CalcSize(new GUIContent(status)).x;
            bool stamped = !float.IsNegativeInfinity(sealedAt) && saveFeedback == null;
            float sealWidth = stamped ? SealSize + SealGap : 0f;
            float availableTextWidth = Mathf.Max(0f, area.width - sealWidth);
            bool wrap = textWidth > availableTextWidth;
            if (wrap)
            {
                wrappedSaveStatusStyle ??= new GUIStyle(style)
                {
                    alignment = TextAnchor.MiddleLeft,
                    wordWrap = true,
                    clipping = TextClipping.Clip
                };
                style = wrappedSaveStatusStyle;
                status = FitToTwoLines(status, style, availableTextWidth);
            }

            float x = wrap ? area.x : Mathf.Max(area.x, area.xMax - textWidth - sealWidth);
            if (stamped)
            {
                skin.DrawSeal(new Rect(x, area.center.y - SealSize * 0.5f, SealSize, SealSize), sealedAt);
                x += sealWidth;
            }
            skin.InkLabel(new Rect(x, area.y, Mathf.Max(0f, area.xMax - x), area.height), status, style, inkedAt,
                saveFeedback != null ? MenuArt.Ember : MenuArt.Muted);
        }

        private static string FitToTwoLines(string text, GUIStyle style, float width)
        {
            if (string.IsNullOrEmpty(text) || width <= 0f) return string.Empty;
            GUIContent content = new GUIContent(text);
            float twoLineHeight = style.CalcHeight(new GUIContent("Ag\nAg"), width);
            if (style.CalcHeight(content, width) <= twoLineHeight + 0.5f) return text;

            const string Ellipsis = "…";
            int low = 0;
            int high = text.Length;
            while (low < high)
            {
                int length = (low + high + 1) / 2;
                string candidate = text.Substring(0, length).TrimEnd() + Ellipsis;
                if (style.CalcHeight(new GUIContent(candidate), width) <= twoLineHeight + 0.5f) low = length;
                else high = length - 1;
            }
            return text.Substring(0, low).TrimEnd() + Ellipsis;
        }

        /// <summary>"Slot 1 saved 2 min ago", from the linked slot's file time. The file is checked at most once a second.</summary>
        private string SaveStatus()
        {
            if (saveFeedback != null) return saveFeedback;
            if (Time.unscaledTime - saveStatusAt < 1f && saveStatus != null) return saveStatus;
            saveStatusAt = Time.unscaledTime;
            string slot = ManualSaveSlot.Linked;
            if (slot == null) return saveStatus = "Choose a slot with Save";
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
        /// Quick-saves to the manual slot the run was loaded from, or the slot most recently chosen in the Save dialog.
        /// </summary>
        private void QuickSave()
        {
            saveStatusAt = float.NegativeInfinity;
            if (ManualSaveSlot.Linked == null)
            {
                saveFeedback = "Choose a slot with Save before using Quick Save.";
                return;
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
                KeyItemEntry entry = KeyItemEntries.TryGetValue(id, out KeyItemEntry configured)
                    ? configured : new KeyItemEntry(id, string.Empty, null);
                DrawInventoryEntry(entry.Name, entry.Description, entry.IconResourcePath);
            }
            if (!shown) GUILayout.Label("No key items discovered yet.", skin.Muted);

            GUILayout.Space(14f);
            DrawMiscellaneous();

            GUILayout.Space(14f);
            Heading("Unlocked spells");
            GameEventRuntime runtime = GameEventManager.Instance?.Runtime;
            bool spellsShown = false;
            foreach (SpellId spell in (SpellId[])Enum.GetValues(typeof(SpellId)))
            {
                // The journal records permanent progression, not the current caster's optional designer allow-list.
                // This also keeps unlocked spells visible while changing maps before a player SpellCaster is rebuilt.
                if (runtime == null || !runtime.GetFlag("spell.unlocked." + spell)) continue;
                spellsShown = true;
                string description = SpellDescriptions.TryGetValue(spell, out string authoredDescription)
                    ? authoredDescription : "A learned spell.";
                DrawInventoryEntry(SpellWheelView.DisplayName(spell), description, SpellCaster.GetIconResourcePath(spell));
            }
            if (!spellsShown) GUILayout.Label("No spells unlocked yet.", skin.Muted);
        }

        // Curios found around the maps: just their names, and clicking one shows or hides its description.
        private void DrawMiscellaneous()
        {
            Heading("Miscellaneous");
            GameEventRuntime runtime = GameEventManager.Instance?.Runtime;
            bool shown = false;
            foreach (string key in MiscCollectibles.JournalKeys)
            {
                if (!MiscCollectibles.IsFound(runtime, key) ||
                    !MiscCollectibles.TryGetText(runtime, key, out string itemName, out string description)) continue;
                shown = true;
                bool open = openMiscItem == key;
                GUILayout.BeginVertical(skin.Entry);
                if (GUILayout.Button(itemName, skin.EntryTitle)) openMiscItem = open ? null : key;
                if (open && !string.IsNullOrWhiteSpace(description)) GUILayout.Label(description, skin.Muted);
                GUILayout.EndVertical();
            }
            if (!shown) GUILayout.Label("Nothing found yet.", skin.Muted);
        }

        /// <summary>Shared journal row used by key items and spells: icon left, gold title and description right.</summary>
        private void DrawInventoryEntry(string entryName, string description, string iconResourcePath)
        {
            GUILayout.BeginHorizontal(skin.Entry);
            Texture2D icon = string.IsNullOrWhiteSpace(iconResourcePath) ? null : Resources.Load<Texture2D>(iconResourcePath);
            if (icon != null)
            {
                GUILayout.Label(icon, GUIStyle.none, GUILayout.Width(40f), GUILayout.Height(40f));
                GUILayout.Space(12f);
            }
            GUILayout.BeginVertical();
            GUILayout.Label(entryName, skin.EntryTitle);
            if (!string.IsNullOrWhiteSpace(description)) GUILayout.Label(description, skin.Muted);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        private void DrawEncyclopedia()
        {
            Heading("Discovered characters");
            GameEventRuntime runtime = GameEventManager.Instance?.Runtime;
            bool shown = false;
            foreach (KeyValuePair<string, CharacterEntry> pair in CharacterEntries)
            {
                if (!AnyRecordDiscovered(runtime, pair.Key)) continue;
                shown = true;
                GUILayout.BeginVertical(skin.Entry);
                GUILayout.Label(NpcIntroductions.NameFor(pair.Key, pair.Value.Name), skin.EntryTitle);
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

        /// <summary>
        /// Adds a character to the journal. Several NPC records can be one person (Cinna on campus and in the ice trial),
        /// so the entry is the person's, whichever record was met.
        /// </summary>
        public static void DiscoverCharacter(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            string person = NpcIntroductions.PersonId(id);
            if (!CharacterEntries.ContainsKey(person)) return;
            GameEventManager.Instance?.Runtime?.SetFlag(CharacterFlagPrefix + person, true);
        }

        // Saves from before records were merged hold the flag under the record's own id.
        private static bool AnyRecordDiscovered(GameEventRuntime runtime, string person)
        {
            if (runtime == null) return false;
            foreach (string record in NpcIntroductions.RecordIdsOf(person))
                if (runtime.GetFlag(CharacterFlagPrefix + record)) return true;
            return false;
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
            if (string.IsNullOrWhiteSpace(id)) return false;
            string person = NpcIntroductions.PersonId(id);
            return CharacterEntries.ContainsKey(person) && AnyRecordDiscovered(GameEventManager.Instance?.Runtime, person);
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

        private readonly struct KeyItemEntry
        {
            public readonly string Name;
            public readonly string Description;
            public readonly string IconResourcePath;

            public KeyItemEntry(string name, string description, string iconResourcePath)
            {
                Name = name;
                Description = description;
                IconResourcePath = iconResourcePath;
            }
        }
    }
}
