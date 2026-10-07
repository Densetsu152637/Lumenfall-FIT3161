using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using Lumenfall.Controls;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.DataDriven;
using Lumenfall.Quests;

namespace Lumenfall.Startup
{
    /// <summary>
    /// Static startup overlay shown over the loaded world. It owns no world state: gameplay input is
    /// paused by disabling GridPlayerInput components until Start or a successful Load is selected.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    [DisallowMultipleComponent]
    public sealed class StartupMenu : MonoBehaviour
    {
        private static readonly string[] Slots = { "save-1", "save-2", "save-3" };
        private const string QuestHintsEnabledKey = "lumenfall.quest-hints-enabled";

        // Night-time academy palette, kept close to the dialogue, sign and autosave overlays.
        private static readonly Color Gold = new Color(0.96f, 0.80f, 0.45f);
        private static readonly Color Cream = new Color(0.97f, 0.93f, 0.85f);
        private static readonly Color Muted = new Color(0.75f, 0.71f, 0.85f);
        private static readonly Color Lavender = new Color(0.72f, 0.60f, 1f);
        private static readonly Color Ember = new Color(1f, 0.80f, 0.58f);
        private static readonly Color Ink = new Color(0.07f, 0.05f, 0.12f);
        private static readonly Color Dusk = new Color(0.25f, 0.21f, 0.36f);
        private const int MoteCount = 34;
        private const float FadeInSeconds = 0.7f;
        private const float PageFadeSeconds = 0.25f;

        private enum Page { Main, Load, Settings, Controls }

        private struct Mote
        {
            public float x, y, speed, sway, phase, size;
            public bool gold;
        }

        private Page page;
        private bool ready;
        private bool seededBlankSave;
        private bool confirmOverwrite;
        private string backdropSlot;
        private string message;
        private bool questHintsEnabled;
        private readonly GameSettingsPanel settings = new GameSettingsPanel();
        private readonly List<GridPlayerInput> disabledInputs = new List<GridPlayerInput>();
        private readonly List<DialogueOverlay> disabledDialogueOverlays = new List<DialogueOverlay>();
        private readonly List<GameSaveOverlay> disabledSaveOverlays = new List<GameSaveOverlay>();
        private readonly Dictionary<string, Texture2D> buttonTextures = new Dictionary<string, Texture2D>();
        private readonly List<Texture2D> generatedTextures = new List<Texture2D>();
        private readonly string[] slotDetails = new string[Slots.Length];
        private float previousTimeScale = 1f;

        // Presentation state. Time.timeScale is 0 while the menu is open, so animation uses unscaled time.
        private float openedAt;
        private float pageChangedAt;
        private float hintsKnob;
        private float fade = 1f;
        private Mote[] motes;
        private Font menuFont;
        private Texture2D backdropTexture, glowTexture, sparkleTexture, knobTexture, switchTexture;
        private GUIStyle panelStyle, panelTrimStyle, cardStyle, pillStyle, titleStyle, rowStyle, valueStyle,
            mutedStyle, messageStyle, textButtonStyle, keyStyle, sliderStyle, thumbStyle, scrollbarStyle, scrollThumbStyle;

        public static bool IsVisible
        {
            get
            {
                StartupMenu menu = FindAnyObjectByType<StartupMenu>();
                return menu != null && menu.isActiveAndEnabled && menu.ready;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindAnyObjectByType<StartupMenu>() == null)
            {
                GameObject menuObject = new GameObject("[StartupMenu]");
                menuObject.AddComponent<StartupMenu>();
            }
        }

        /// <summary>
        /// Saves the current slot and reopens the menu over the world. The menu then reloads the most recent save as its
        /// backdrop, so returning loses no progress and Load or Start behave exactly as they do on launch.
        /// </summary>
        public static void ReturnToMainMenu()
        {
            if (FindAnyObjectByType<StartupMenu>() != null) return;
            GameSaveManager saves = GameSaveManager.Instance;
            if (saves.HasSave(GameSaveOverlay.CurrentSlot)) saves.Save(GameSaveOverlay.CurrentSlot);
            new GameObject("[StartupMenu]").AddComponent<StartupMenu>();
        }

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            questHintsEnabled = PlayerPrefs.GetInt(QuestHintsEnabledKey, 1) != 0;
            hintsKnob = questHintsEnabled ? 1f : 0f;
            QuestHintOverlay.SetHintsEnabled(questHintsEnabled);
            GameSettingsPanel.ApplySavedVolume();
            SetGameplayInput(false);
            LoadButtonTextures();
            CreateArt();
            StartCoroutine(InitializeAfterSceneSystems());
        }

        private IEnumerator InitializeAfterSceneSystems()
        {
            yield return null;
            GameSaveManager saves = GameSaveManager.Instance;
            backdropSlot = saves.FindMostRecentSlot(Slots);
            if (backdropSlot == null)
            {
                // Reloading definitions gives the first save a clean event runtime state.
                GameEventManager.Instance.ReloadDefinitions();
                backdropSlot = FirstEmptySlot(saves);
                if (backdropSlot != null && saves.Save(backdropSlot))
                {
                    seededBlankSave = true;
                }
                else
                {
                    message = "Unable to create a starting save.";
                }
            }

            // The latest save is the backdrop state. Providers registered by the scene are ready now.
            if (!string.IsNullOrWhiteSpace(backdropSlot) && !saves.Load(backdropSlot, false))
            {
                message = "The latest save could not be loaded.";
            }
            if (!string.IsNullOrWhiteSpace(backdropSlot)) GameSaveOverlay.SetCurrentSlot(backdropSlot);
            openedAt = pageChangedAt = Time.unscaledTime;
            ready = true;
        }

        private void Update()
        {
            if (ready) SetGameplayInput(false);
            bool wasRebinding = settings.IsRebinding;
            settings.Tick();
            Keyboard keyboard = Keyboard.current;
            if (ready && !wasRebinding && keyboard != null && keyboard.escapeKey.wasPressedThisFrame) GoBack();
            hintsKnob = Mathf.MoveTowards(hintsKnob, questHintsEnabled ? 1f : 0f, Time.unscaledDeltaTime * 6f);
        }


        private void OnDestroy()
        {
            Time.timeScale = previousTimeScale;
            SetGameplayInput(true);
            foreach (Texture2D texture in buttonTextures.Values)
            {
                if (texture != null && texture.name.StartsWith("StartupButton_", StringComparison.Ordinal))
                {
                    Destroy(texture);
                }
            }
            foreach (Texture2D texture in generatedTextures)
            {
                if (texture != null) Destroy(texture);
            }
        }

        private void OnGUI()
        {
            if (!ready) return;
            EnsureStyles();

            float now = Time.unscaledTime;
            float opening = Smooth((now - openedAt) / FadeInSeconds);
            float pageIn = Smooth((now - pageChangedAt) / PageFadeSeconds);
            Color previousColor = GUI.color;
            Matrix4x4 previousMatrix = GUI.matrix;

            fade = opening;
            Draw(new Rect(0f, 0f, Screen.width, Screen.height), backdropTexture, Color.white, 1f);
            DrawMotes(now);

            float panelWidth = Mathf.Min(560f, Screen.width - 48f);
            float panelHeight = page == Page.Controls ? Mathf.Min(720f, Screen.height - 40f) : page == Page.Main ? 530f : 560f;
            Rect panel = new Rect((Screen.width - panelWidth) * 0.5f,
                (Screen.height - panelHeight) * 0.5f + (1f - opening) * 18f, panelWidth, panelHeight);
            DrawPanel(panel, now);

            // Each page fades in and settles upward slightly so page changes feel gentle rather than abrupt.
            fade = opening * pageIn;
            Rect content = new Rect(panel.x, panel.y + (1f - pageIn) * 8f, panel.width, panel.height);
            float top = DrawTitle(content, now);
            if (page == Page.Main) DrawMain(content, top);
            else if (page == Page.Load) DrawLoad(content, top);
            else if (page == Page.Controls) DrawControls(content, top);
            else DrawSettings(content, top);

            GUI.color = previousColor;
            GUI.matrix = previousMatrix;
        }

        private void DrawMain(Rect panel, float top)
        {
            float x = panel.x + 80f;
            float width = panel.width - 160f;
            if (Button("start", new Rect(x, top, width, 64f), "START")) StartNewGame();
            if (Button("load", new Rect(x, top + 78f, width, 64f), "LOAD")) SetPage(Page.Load);
            if (Button("options", new Rect(x, top + 156f, width, 64f), "SETTINGS"))
            {
                questHintsEnabled = PlayerPrefs.GetInt(QuestHintsEnabledKey, 1) != 0;
                hintsKnob = questHintsEnabled ? 1f : 0f;
                SetPage(Page.Settings);
            }
            if (Button("exit", new Rect(x, top + 234f, width, 64f), "EXIT")) Application.Quit();
            DrawMessage(new Rect(panel.x + 36f, panel.yMax - 74f, panel.width - 72f, 46f));
        }

        private void DrawLoad(Rect panel, float top)
        {
            GameSaveManager saves = GameSaveManager.Instance;
            float y = top + 4f;
            for (int i = 0; i < Slots.Length; i++)
            {
                string slot = Slots[i];
                bool hasSave = saves.HasSave(slot);
                Rect card = new Rect(panel.x + 36f, y, panel.width - 72f, 84f);
                Box(card, cardStyle, hasSave ? 1f : 0.55f);

                string title = $"Slot {i + 1}";
                Rect titleRect = new Rect(card.x + 22f, card.y + 13f, card.width - 210f, 30f);
                Label(titleRect, title, rowStyle, hasSave ? Cream : Muted);
                if (hasSave && slot == backdropSlot && !seededBlankSave)
                {
                    // Marks the save the backdrop was restored from, so returning players can find their place.
                    float badgeX = titleRect.x + rowStyle.CalcSize(new GUIContent(title)).x + 12f;
                    Draw(new Rect(badgeX, titleRect.center.y - 7f, 14f, 14f), sparkleTexture, Lavender, 0.9f);
                    Label(new Rect(badgeX + 18f, titleRect.y, 140f, titleRect.height), "Most recent", mutedStyle, Lavender);
                }
                Label(new Rect(titleRect.x, titleRect.yMax, titleRect.width, 26f),
                    hasSave ? slotDetails[i] : "Empty — a fresh page awaits", mutedStyle, Muted);

                if (hasSave && Button("load", new Rect(card.xMax - 186f, card.y + 14f, 170f, 56f), "LOAD"))
                {
                    LoadSlot(slot);
                }
                y += 98f;
            }

            DrawMessage(new Rect(panel.x + 36f, panel.yMax - 140f, panel.width - 72f, 42f));
            if (Button("exit", BackRect(panel), "BACK")) GoBack();
        }

        private void DrawSettings(Rect panel, float top)
        {
            Rect card = new Rect(panel.x + 36f, top + 4f, panel.width - 72f, 196f);
            Box(card, cardStyle, 1f);
            float x = card.x + 24f;
            float width = card.width - 48f;

            float masterVolume = GameSettingsPanel.MasterVolume;
            Label(new Rect(x, card.y + 16f, width, 30f), "Master volume", rowStyle, Cream);
            Label(new Rect(x, card.y + 16f, width, 30f), $"{Mathf.RoundToInt(masterVolume * 100f)}%", valueStyle, Gold);
            float newVolume = Slider(new Rect(x, card.y + 54f, width, 26f), masterVolume);
            if (!Mathf.Approximately(newVolume, masterVolume)) GameSettingsPanel.MasterVolume = newVolume;

            Draw(new Rect(x, card.y + 98f, width, 4f), glowTexture, Gold, 0.45f);

            Label(new Rect(x, card.y + 114f, width - 80f, 28f), "Show quest hints", rowStyle, Cream);
            Label(new Rect(x, card.y + 142f, width - 80f, 24f), "Gentle nudges toward what to do next", mutedStyle, Muted);
            Rect switchRect = new Rect(card.xMax - 24f - 60f, card.y + 124f, 60f, 30f);
            DrawSwitch(switchRect, hintsKnob);
            SetColor(Color.white, 1f);
            if (GUI.Button(new Rect(x, card.y + 108f, width, 66f), GUIContent.none, GUIStyle.none))
            {
                questHintsEnabled = !questHintsEnabled;
                QuestHintOverlay.SetHintsEnabled(questHintsEnabled);
                PlayerPrefs.SetInt(QuestHintsEnabledKey, questHintsEnabled ? 1 : 0);
                PlayerPrefs.Save();
            }

            SetColor(Color.white, 1f);
            if (GUI.Button(new Rect(panel.center.x - 130f, card.yMax + 28f, 260f, 52f), "Controls", textButtonStyle))
            {
                SetPage(Page.Controls);
                settings.Message = "Click an action, then press the key you want.";
            }
            if (Button("exit", BackRect(panel), "BACK")) GoBack();
        }

        private void DrawControls(Rect panel, float top)
        {
            Rect back = BackRect(panel);
            Rect area = new Rect(panel.x + 30f, top, panel.width - 60f, back.y - top - 12f);
            GUIStyle previousScrollbar = GUI.skin.verticalScrollbar;
            GUIStyle previousThumb = GUI.skin.verticalScrollbarThumb;
            GUI.skin.verticalScrollbar = scrollbarStyle;
            GUI.skin.verticalScrollbarThumb = scrollThumbStyle;
            try
            {
                SetColor(Color.white, 1f);
                rowStyle.normal.textColor = Cream;
                messageStyle.normal.textColor = Ember;
                GUILayout.BeginArea(area);
                settings.DrawControls(rowStyle, keyStyle, messageStyle, area.height - 96f);
                GUILayout.EndArea();
            }
            finally
            {
                GUI.skin.verticalScrollbar = previousScrollbar;
                GUI.skin.verticalScrollbarThumb = previousThumb;
            }
            if (Button("exit", back, "BACK")) GoBack();
        }

        private void SetPage(Page next)
        {
            page = next;
            pageChangedAt = Time.unscaledTime;
            if (next == Page.Load) RefreshSlotDetails();
        }

        /// <summary>Back buttons and Escape share one route so both always return to the parent page.</summary>
        private void GoBack()
        {
            if (page == Page.Load)
            {
                message = string.Empty;
                SetPage(Page.Main);
            }
            else if (page == Page.Settings)
            {
                SetPage(Page.Main);
            }
            else if (page == Page.Controls)
            {
                settings.CancelRebind();
                settings.Message = string.Empty;
                SetPage(Page.Settings);
            }
        }

        private static Rect BackRect(Rect panel) => new Rect(panel.x + 80f, panel.yMax - 92f, panel.width - 160f, 60f);

        private bool Button(string textureKey, Rect rect, string fallbackLabel)
        {
            if (!buttonTextures.TryGetValue(textureKey, out Texture2D texture) || texture == null)
            {
                SetColor(Color.white, 1f);
                return GUI.Button(rect, fallbackLabel, textButtonStyle);
            }

            // Sheets hold four stacked frames: normal, hover, pressed, then disabled.
            float aspect = texture.width / (texture.height * 0.25f);
            float drawWidth = Mathf.Min(rect.width, rect.height * aspect);
            float drawHeight = drawWidth / aspect;
            Rect hitRect = new Rect(rect.center.x - drawWidth * 0.5f, rect.center.y - drawHeight * 0.5f, drawWidth, drawHeight);
            bool hover = hitRect.Contains(Event.current.mousePosition);
            bool pressed = hover && Mouse.current != null && Mouse.current.leftButton.isPressed;
            Rect drawRect = hover && !pressed ? new Rect(hitRect.x, hitRect.y - 2f, hitRect.width, hitRect.height) : hitRect;

            if (hover) Draw(Expand(drawRect, 34f, 22f), glowTexture, Lavender, pressed ? 0.3f : 0.5f);
            SetColor(Color.white, 1f);
            float row = pressed ? 0.25f : hover ? 0.5f : 0.75f;
            GUI.DrawTextureWithTexCoords(drawRect, texture, new Rect(0f, row, 1f, 0.25f), true);
            return GUI.Button(hitRect, GUIContent.none, GUIStyle.none);
        }

        private float DrawTitle(Rect panel, float now)
        {
            bool main = page == Page.Main;
            string title = main ? "Lumenfall" : page == Page.Load ? "Load Game" : page == Page.Controls ? "Controls" : "Settings";
            Rect titleRect = new Rect(panel.x + 24f, panel.y + (main ? 30f : 26f), panel.width - 48f, main ? 72f : 52f);
            titleStyle.fontSize = main ? 54 : 36;

            float pulse = 0.5f + 0.5f * Mathf.Sin(now * 1.6f);
            Draw(Expand(titleRect, 20f, 6f), glowTexture, Lavender, 0.22f + 0.14f * pulse);
            Label(new Rect(titleRect.x, titleRect.y + 3f, titleRect.width, titleRect.height), title, titleStyle, Ink, 0.85f);
            Label(titleRect, title, titleStyle, Gold);

            float y = titleRect.yMax + 4f;
            DrawDivider(panel.center.x, y + 6f, Mathf.Min(280f, panel.width - 120f), now);
            return y + 30f;
        }

        private void DrawDivider(float centerX, float y, float width, float now)
        {
            Draw(new Rect(centerX - width * 0.5f, y - 2f, width, 4f), glowTexture, Gold, 0.85f);
            for (int side = -1; side <= 1; side += 2)
            {
                float twinkle = 0.5f + 0.5f * Mathf.Sin(now * 2.2f + side);
                Draw(new Rect(centerX + side * width * 0.32f - 4f, y - 4f, 8f, 8f), glowTexture, Cream, 0.5f + 0.4f * twinkle);
            }

            Rect star = new Rect(centerX - 12f, y - 12f, 24f, 24f);
            Matrix4x4 matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Sin(now * 0.8f) * 12f, star.center);
            Draw(Expand(star, 10f, 10f), glowTexture, Gold, 0.5f);
            Draw(star, sparkleTexture, Gold, 1f);
            GUI.matrix = matrix;
        }

        private void DrawPanel(Rect panel, float now)
        {
            Draw(Expand(panel, 70f, 70f), glowTexture, Lavender, 0.22f);
            Box(panel, panelStyle, 1f);
            Rect trim = Expand(panel, -8f, -8f);
            Box(trim, panelTrimStyle, 1f);

            // Small stars twinkle in the trim corners, out of step with each other.
            Vector2[] corners = { new Vector2(trim.x, trim.y), new Vector2(trim.xMax, trim.y), new Vector2(trim.x, trim.yMax), new Vector2(trim.xMax, trim.yMax) };
            for (int i = 0; i < corners.Length; i++)
            {
                float twinkle = 0.5f + 0.5f * Mathf.Sin(now * 1.7f + i * 1.9f);
                float size = 12f + 6f * twinkle;
                Draw(new Rect(corners[i].x - size * 0.5f, corners[i].y - size * 0.5f, size, size), sparkleTexture, Gold, 0.45f + 0.5f * twinkle);
            }
        }

        /// <summary>Lantern-light motes drift upward behind the panel, swaying and twinkling at their own pace.</summary>
        private void DrawMotes(float now)
        {
            float scale = Mathf.Max(0.6f, Screen.height / 1080f);
            for (int i = 0; i < motes.Length; i++)
            {
                Mote mote = motes[i];
                float y = Mathf.Repeat(mote.y - now * mote.speed, 1.1f) - 0.05f;
                float x = mote.x + Mathf.Sin(now * 0.5f + mote.phase) * mote.sway;
                float twinkle = 0.45f + 0.55f * (0.5f + 0.5f * Mathf.Sin(now * (1.2f + mote.speed * 25f) + mote.phase));
                float edge = Mathf.Clamp01(y / 0.25f) * Mathf.Clamp01((1.05f - y) / 0.2f);
                float size = mote.size * scale;
                Vector2 centre = new Vector2(x * Screen.width, y * Screen.height);
                Color colour = mote.gold ? Gold : Lavender;
                Draw(new Rect(centre.x - size * 1.5f, centre.y - size * 1.5f, size * 3f, size * 3f), glowTexture, colour, 0.35f * twinkle * edge);
                if (mote.size > 15f) Draw(new Rect(centre.x - size * 0.6f, centre.y - size * 0.6f, size * 1.2f, size * 1.2f), sparkleTexture, Cream, 0.8f * twinkle * edge);
                else Draw(new Rect(centre.x - size * 0.25f, centre.y - size * 0.25f, size * 0.5f, size * 0.5f), glowTexture, Cream, 0.9f * twinkle * edge);
            }
        }

        private float Slider(Rect rect, float value)
        {
            const float Knob = 24f;
            SetColor(Color.white, 1f);
            float next = GUI.HorizontalSlider(rect, value, 0f, 1f, sliderStyle, thumbStyle);

            Rect track = new Rect(rect.x + Knob * 0.5f, rect.center.y - 5f, rect.width - Knob, 10f);
            Box(track, pillStyle, 0.9f, Ink);
            float filled = track.width * next;
            if (filled >= 10f) Box(new Rect(track.x, track.y, filled, track.height), pillStyle, 0.95f, Lavender);

            float knobX = rect.x + Knob * 0.5f + next * (rect.width - Knob);
            Draw(new Rect(knobX - 20f, rect.center.y - 20f, 40f, 40f), glowTexture, Gold, 0.5f);
            Draw(new Rect(knobX - Knob * 0.5f, rect.center.y - Knob * 0.5f, Knob, Knob), knobTexture, Gold, 1f);
            return next;
        }

        private void DrawSwitch(Rect rect, float on)
        {
            Draw(rect, switchTexture, Color.Lerp(Dusk, Lavender, on), 1f);
            float knobX = Mathf.Lerp(rect.x + 3f, rect.xMax - 27f, on);
            Rect knob = new Rect(knobX, rect.center.y - 12f, 24f, 24f);
            if (on > 0.01f) Draw(Expand(knob, 10f, 10f), glowTexture, Gold, 0.5f * on);
            Draw(knob, knobTexture, Color.Lerp(Cream, Gold, on), 1f);
        }

        private void DrawMessage(Rect rect)
        {
            if (string.IsNullOrEmpty(message)) return;
            Box(rect, cardStyle, 1f);
            Label(rect, message, messageStyle, Ember);
        }

        private void StartNewGame()
        {
            GameSaveManager saves = GameSaveManager.Instance;
            string slot = seededBlankSave && !string.IsNullOrWhiteSpace(backdropSlot)
                ? backdropSlot
                : FirstEmptySlot(saves);
            if (slot == null)
            {
                if (!confirmOverwrite)
                {
                    confirmOverwrite = true;
                    message = "All slots are full. Press START again to replace SLOT 1.";
                    return;
                }
                slot = Slots[0];
            }

            GameEventManager.Instance.ReloadDefinitions();
            DataDrivenWorldLoader worldLoader = DataDrivenWorldLoader.Instance;
            if (worldLoader != null) worldLoader.ResetToStartingState();
            if (!saves.Save(slot))
            {
                message = "Could not start a new game.";
                return;
            }
            backdropSlot = slot;
            saves.MarkPlayed(slot);
            GameSaveOverlay.SetCurrentSlot(slot);
            seededBlankSave = false;
            confirmOverwrite = false;
            message = string.Empty;
            CloseMenu();
        }

        private void LoadSlot(string slot)
        {
            if (GameSaveManager.Instance.Load(slot))
            {
                backdropSlot = slot;
                GameSaveOverlay.SetCurrentSlot(slot);
                CloseMenu();
            }
            else
            {
                message = "That save could not be loaded.";
            }
        }

        private void CloseMenu()
        {
            SetGameplayInput(true);
            Destroy(gameObject);
        }

        private void SetGameplayInput(bool enabled)
        {
            if (enabled)
            {
                for (int i = 0; i < disabledInputs.Count; i++)
                    if (disabledInputs[i] != null) disabledInputs[i].enabled = true;
                disabledInputs.Clear();
                for (int i = 0; i < disabledDialogueOverlays.Count; i++)
                    if (disabledDialogueOverlays[i] != null) disabledDialogueOverlays[i].enabled = true;
                disabledDialogueOverlays.Clear();
                for (int i = 0; i < disabledSaveOverlays.Count; i++)
                    if (disabledSaveOverlays[i] != null) disabledSaveOverlays[i].enabled = true;
                disabledSaveOverlays.Clear();
                return;
            }

            foreach (GridPlayerInput input in FindObjectsByType<GridPlayerInput>(FindObjectsSortMode.None))
            {
                if (input != null && input.enabled)
                {
                    input.enabled = false;
                    disabledInputs.Add(input);
                }
            }
            foreach (DialogueOverlay overlay in FindObjectsByType<DialogueOverlay>(FindObjectsSortMode.None))
            {
                if (overlay != null && overlay.enabled)
                {
                    overlay.enabled = false;
                    disabledDialogueOverlays.Add(overlay);
                }
            }
            foreach (GameSaveOverlay overlay in FindObjectsByType<GameSaveOverlay>(FindObjectsSortMode.None))
            {
                if (overlay != null && overlay.enabled)
                {
                    overlay.enabled = false;
                    disabledSaveOverlays.Add(overlay);
                }
            }
        }

        private static string FirstEmptySlot(GameSaveManager saves)
        {
            for (int i = 0; i < Slots.Length; i++) if (!saves.HasSave(Slots[i])) return Slots[i];
            return null;
        }

        private void RefreshSlotDetails()
        {
            string directory = GameSaveManager.Instance.SaveDirectory;
            for (int i = 0; i < Slots.Length; i++)
            {
                slotDetails[i] = string.Empty;
                try
                {
                    string path = Path.Combine(directory, Slots[i] + ".json");
                    if (!File.Exists(path)) continue;
                    DateTime saved = File.GetLastWriteTime(path);
                    slotDetails[i] = "Saved " + saved.ToString("d MMM yyyy", CultureInfo.InvariantCulture) + " at " +
                        saved.ToString("h:mm", CultureInfo.InvariantCulture) + (saved.Hour < 12 ? " am" : " pm");
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"Could not read the save time for '{Slots[i]}': {exception.Message}");
                }
            }
        }

        private void LoadButtonTextures()
        {
            foreach (string key in new[] { "start", "load", "options", "exit" })
            {
                string fileName = key == "start" ? "btn_start_sheet4x" : key == "load" ? "btn_load_sheet4x" :
                    key == "options" ? "btn_options_sheet4x" : "btn_exit_sheet4x";
                Texture2D texture = Resources.Load<Texture2D>("StartupButtons/" + fileName);
                if (texture == null) texture = LoadEditorTexture(fileName);
                if (texture != null) buttonTextures[key] = texture;
            }
        }

        private static Texture2D LoadEditorTexture(string fileName)
        {
            string path = Path.Combine(Application.dataPath, "2D", fileName + ".png");
            if (!File.Exists(path)) return null;
            try
            {
                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "StartupButton_" + fileName };
                texture.LoadImage(File.ReadAllBytes(path));
                return texture;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not load startup button '{path}': {exception.Message}");
                return null;
            }
        }

        // ---- Drawing helpers. Every draw is multiplied by the current fade so the whole menu can ease in. ----

        private void SetColor(Color tint, float alpha) => GUI.color = new Color(tint.r, tint.g, tint.b, tint.a * alpha * fade);

        private void Draw(Rect rect, Texture texture, Color tint, float alpha)
        {
            if (texture == null) return;
            SetColor(tint, alpha);
            GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, true);
        }

        private void Box(Rect rect, GUIStyle style, float alpha) => Box(rect, style, alpha, Color.white);

        private void Box(Rect rect, GUIStyle style, float alpha, Color tint)
        {
            SetColor(tint, alpha);
            GUI.Box(rect, GUIContent.none, style);
        }

        private void Label(Rect rect, string text, GUIStyle style, Color colour, float alpha = 1f)
        {
            SetColor(Color.white, alpha);
            style.normal.textColor = colour;
            GUI.Label(rect, text, style);
        }

        private static Rect Expand(Rect rect, float x, float y) => new Rect(rect.x - x, rect.y - y, rect.width + x * 2f, rect.height + y * 2f);

        private static float Smooth(float t) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));

        // ---- Generated art. Everything below is created at runtime so the menu needs no imported UI assets. ----

        private void CreateArt()
        {
            menuFont = Resources.Load<Font>("Dialogue/Nunito-Regular");
            backdropTexture = Track(CreateBackdropTexture(32));
            glowTexture = Track(CreateGlowTexture(64));
            sparkleTexture = Track(GameSaveOverlay.CreateSparkleTexture(64));
            knobTexture = Track(CreateRoundedTexture(32, 32, 16f, Color.white, new Color(0.62f, 0.5f, 0.32f), 2f));
            switchTexture = Track(CreateRoundedTexture(60, 30, 15f, Color.white, Color.clear, 0f));

            System.Random random = new System.Random(1170);
            motes = new Mote[MoteCount];
            for (int i = 0; i < motes.Length; i++)
            {
                motes[i] = new Mote
                {
                    x = (float)random.NextDouble(),
                    y = (float)random.NextDouble() * 1.1f,
                    speed = 0.012f + 0.028f * (float)random.NextDouble(),
                    sway = 0.008f + 0.02f * (float)random.NextDouble(),
                    phase = (float)random.NextDouble() * Mathf.PI * 2f,
                    size = 6f + 14f * (float)random.NextDouble(),
                    gold = random.NextDouble() < 0.6
                };
            }
        }

        private void EnsureStyles()
        {
            if (titleStyle != null) return;

            panelStyle = SlicedStyle(CreateRoundedTexture(64, 64, 20f, new Color(0.11f, 0.085f, 0.18f, 0.95f), Gold, 2.5f), 24);
            panelTrimStyle = SlicedStyle(CreateRoundedTexture(64, 64, 15f, Color.clear, new Color(Gold.r, Gold.g, Gold.b, 0.3f), 1f), 20);
            cardStyle = SlicedStyle(CreateRoundedTexture(32, 32, 12f, new Color(0.18f, 0.14f, 0.27f, 0.85f), new Color(Gold.r, Gold.g, Gold.b, 0.32f), 1.5f), 14);
            pillStyle = SlicedStyle(CreateRoundedTexture(20, 10, 5f, Color.white, Color.clear, 0f), 5);

            titleStyle = TextStyle(54, TextAnchor.MiddleCenter);
            titleStyle.fontStyle = FontStyle.Bold;
            rowStyle = TextStyle(19, TextAnchor.MiddleLeft);
            valueStyle = TextStyle(19, TextAnchor.MiddleRight);
            valueStyle.fontStyle = FontStyle.Bold;
            mutedStyle = TextStyle(15, TextAnchor.MiddleLeft);
            messageStyle = TextStyle(17, TextAnchor.MiddleCenter);
            messageStyle.wordWrap = true;
            messageStyle.padding = new RectOffset(14, 14, 4, 4);

            textButtonStyle = new GUIStyle(GUI.skin.button)
            {
                font = menuFont,
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(12, 12, 12, 12),
                padding = new RectOffset(12, 12, 6, 6),
                normal = { background = Track(CreateRoundedTexture(32, 32, 10f, new Color(0.2f, 0.16f, 0.3f, 0.95f), new Color(Gold.r, Gold.g, Gold.b, 0.55f), 1.5f)), textColor = Cream },
                hover = { background = Track(CreateRoundedTexture(32, 32, 10f, new Color(0.28f, 0.21f, 0.4f, 0.98f), Gold, 2f)), textColor = Gold },
                active = { background = Track(CreateRoundedTexture(32, 32, 10f, new Color(0.13f, 0.1f, 0.2f, 1f), Gold, 2f)), textColor = Gold }
            };
            textButtonStyle.focused.background = textButtonStyle.normal.background;
            textButtonStyle.focused.textColor = Cream;
            keyStyle = new GUIStyle(textButtonStyle) { fontSize = 16 };

            sliderStyle = new GUIStyle();
            thumbStyle = new GUIStyle { fixedWidth = 24f };
            scrollbarStyle = new GUIStyle(GUI.skin.verticalScrollbar)
            {
                fixedWidth = 10f,
                border = new RectOffset(5, 5, 5, 5),
                margin = new RectOffset(6, 2, 2, 2),
                normal = { background = Track(CreateRoundedTexture(10, 20, 5f, new Color(0.07f, 0.05f, 0.12f, 0.7f), Color.clear, 0f)) }
            };
            scrollThumbStyle = new GUIStyle(GUI.skin.verticalScrollbarThumb)
            {
                fixedWidth = 10f,
                border = new RectOffset(5, 5, 5, 5),
                normal = { background = Track(CreateRoundedTexture(10, 20, 5f, new Color(Gold.r, Gold.g, Gold.b, 0.75f), Color.clear, 0f)) }
            };
        }

        private GUIStyle TextStyle(int size, TextAnchor alignment)
        {
            GUIStyle style = new GUIStyle(GUI.skin.label) { fontSize = size, alignment = alignment, normal = { textColor = Cream } };
            if (menuFont != null) style.font = menuFont;
            return style;
        }

        private GUIStyle SlicedStyle(Texture2D texture, int border)
        {
            return new GUIStyle { normal = { background = Track(texture) }, border = new RectOffset(border, border, border, border) };
        }

        private Texture2D Track(Texture2D texture)
        {
            if (texture != null && !generatedTextures.Contains(texture)) generatedTextures.Add(texture);
            return texture;
        }

        private static Texture2D NewTexture(int width, int height, string name)
        {
            return new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "StartupMenu_" + name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave
            };
        }

        /// <summary>Anti-aliased rounded rectangle with an optional border, suitable for nine-slice drawing.</summary>
        private static Texture2D CreateRoundedTexture(int width, int height, float radius, Color fill, Color border, float borderWidth)
        {
            Texture2D texture = NewTexture(width, height, "Rounded");
            Color[] pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float qx = Mathf.Abs(x + 0.5f - width * 0.5f) - (width * 0.5f - radius);
                    float qy = Mathf.Abs(y + 0.5f - height * 0.5f) - (height * 0.5f - radius);
                    float distance = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
                    float coverage = Mathf.Clamp01(0.5f - distance);
                    float borderMix = borderWidth > 0f ? Mathf.Clamp01(distance + borderWidth + 0.5f) : 0f;
                    Color colour = Color.Lerp(fill, border, borderMix);
                    colour.a *= coverage;
                    pixels[y * width + x] = colour;
                }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private static Texture2D CreateGlowTexture(int size)
        {
            Texture2D texture = NewTexture(size, size, "Glow");
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f;
                    float v = (y + 0.5f) / size * 2f - 1f;
                    float r2 = u * u + v * v;
                    pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Exp(-r2 * 4.5f) * Mathf.Clamp01(1f - r2));
                }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>Indigo-to-plum dusk wash with a soft vignette; the world stays faintly visible in the centre.</summary>
        private static Texture2D CreateBackdropTexture(int size)
        {
            Texture2D texture = NewTexture(size, size, "Backdrop");
            Color top = new Color(0.05f, 0.05f, 0.16f);
            Color bottom = new Color(0.15f, 0.07f, 0.19f);
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float v = y / (size - 1f);
                    float u = x / (size - 1f) * 2f - 1f;
                    float w = v * 2f - 1f;
                    Color colour = Color.Lerp(bottom, top, v);
                    colour.a = 0.5f + 0.38f * Mathf.Clamp01((u * u + w * w) * 0.7f);
                    pixels[y * size + x] = colour;
                }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
