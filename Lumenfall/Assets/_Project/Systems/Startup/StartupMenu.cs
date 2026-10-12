using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
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
        private static readonly string[] Slots =
        {
            GameSaveManager.AutosaveSlot,
            GameSaveManager.ManualSlots[0],
            GameSaveManager.ManualSlots[1],
            GameSaveManager.ManualSlots[2]
        };
        private const string QuestHintsEnabledKey = "lumenfall.quest-hints-enabled";
        private const string GameTitle = "Lumenfall";

        private const int MoteCount = 34;
        private const int IntroFontSize = 86;
        private const int MainTitleFontSize = 56;
        private const float FadeInSeconds = 0.7f;
        private const float IntroLightDelay = 0.6f;
        private const float IntroLightSeconds = 1.8f;
        private const float IntroToMenuSeconds = 0.9f;
        private const float PageFadeSeconds = 0.25f;
        private const float CloseSeconds = 1.3f;
        private const float RingRadius = 88f;
        private const float InnerRingRadius = 56f;
        private const int OuterRingTexture = 256;
        private const int OuterRingTick = 10;
        private const int InnerRingTexture = 192;
        private const int InnerRingTick = 6;

        private enum Page { Main, Load, NewGame, Settings, Controls }
        private enum Phase { Intro, Menu, Closing }

        private static readonly string[] OptionKeys = { "start", "load", "options", "exit" };
        private static readonly string[] OptionLabels = { "START", "LOAD", "SETTINGS", "EXIT" };

        // One rune per main option, as line segments in a unit square (x right, y down): a rising spark for Start,
        // a bound page for Load, a balanced diamond for Settings and a resting stroke for Exit.
        private static readonly Vector2[][] Runes =
        {
            new[] { new Vector2(0f, 1f), new Vector2(0f, -1f), new Vector2(0f, -1f), new Vector2(-0.65f, -0.3f), new Vector2(0f, -1f), new Vector2(0.65f, -0.3f) },
            new[] { new Vector2(-0.45f, -1f), new Vector2(-0.45f, 1f), new Vector2(-0.45f, -1f), new Vector2(0.5f, -0.5f), new Vector2(0.5f, -0.5f), new Vector2(-0.45f, 0f),
                    new Vector2(-0.45f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-0.45f, 1f) },
            new[] { new Vector2(0f, -1f), new Vector2(0.75f, 0f), new Vector2(0.75f, 0f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(-0.75f, 0f),
                    new Vector2(-0.75f, 0f), new Vector2(0f, -1f), new Vector2(-1f, 0f), new Vector2(1f, 0f) },
            new[] { new Vector2(0f, -1f), new Vector2(0f, 1f), new Vector2(-0.55f, 0.35f), new Vector2(0.55f, -0.35f) }
        };

        private static bool introPlayed;
        private static StartupMenu current;

        private Page page;
        private bool ready;
        // New Game asks for a second START before replacing a filled slot (or the autosave); this holds the slot asked about.
        private string confirmSlot;
        // The manual slot the backdrop game belongs to, read when a slot page opens, for the Current tag.
        private string linkedSlot;
        private string backdropSlot;
        private string message;
        private bool questHintsEnabled;
        // Read once on open; the Reduce motion switch lives in the in-game options, so it cannot change while the menu is up.
        private bool reduceMotion;
        private readonly GameSettingsPanel settings = new GameSettingsPanel();
        private readonly List<GridPlayerInput> disabledInputs = new List<GridPlayerInput>();
        private readonly List<DialogueOverlay> disabledDialogueOverlays = new List<DialogueOverlay>();
        private readonly List<GameSaveOverlay> disabledSaveOverlays = new List<GameSaveOverlay>();
        private readonly Dictionary<string, Texture2D> buttonTextures = new Dictionary<string, Texture2D>();
        private readonly List<Texture2D> generatedTextures = new List<Texture2D>();
        private readonly string[] slotDetails = new string[Slots.Length];
        private readonly SaveSlotSummary.Info[] slotInfos = new SaveSlotSummary.Info[Slots.Length];
        private readonly Texture2D[] slotThumbnails = new Texture2D[Slots.Length];
        private float previousTimeScale = 1f;

        // Presentation state. Time.timeScale is 0 while the menu is open, so animation uses unscaled time.
        private Phase phase;
        private float openedAt;
        private float phaseStartedAt;
        private float menuShownAt;
        private bool cameFromIntro;
        private bool introBurstDone;
        private float worldAlpha = 1f;
        private float titleFly = 1f;
        private float fade = 1f;
        private bool interactive;
        private Rect lastPanel;
        // Panels keep their authored pixel size and only shrink, uniformly, when the current page would not fit the window.
        private float uiScale = 1f;
        private bool uiScaleSet;
        private Vector2 viewSize;
        private float lastPanelAlpha;
        private float closingPanelAlpha = 1f;
        private float hintsKnob;

        private float pageChangedAt;

        private int hoverOption = -1;
        private int keyboardOption = -1;
        private bool keyboardMode;
        private float ringAngle;
        private float ringVelocity;
        private readonly float[] threadPower = new float[OptionKeys.Length];
        private readonly float[] runeLight = new float[OptionKeys.Length];
        private readonly Rect[] optionRects = new Rect[OptionKeys.Length];
        private readonly Vector2[] runePositions = new Vector2[OptionKeys.Length];

        private Vector2 pointer;
        private Vector2 lastTrail;
        private Vector2 parallax;
        private bool pointerKnown;

        private readonly MenuCameraDrift cameraDrift = new MenuCameraDrift();
        private MenuSparks sparks;
        private Font menuFont;
        private Texture2D backdropTexture, glowTexture, sparkleTexture, knobTexture, switchTexture, lineTexture,
            ringTexture, innerRingTexture, discTexture, wandCursor;
        private GUIStyle panelStyle, panelTrimStyle, cardStyle, thumbFrameStyle, pillStyle, letterStyle, promptStyle,
            rowStyle, valueStyle, mutedStyle, groupStyle, smallStyle, smallCentreStyle, loadDetailStyle, loadMetaStyle, messageStyle, textButtonStyle, keyStyle, sliderStyle, thumbStyle,
            scrollbarStyle, scrollThumbStyle;

        public static bool IsVisible => current != null && current.isActiveAndEnabled && current.ready;

        private int SelectedOption => keyboardMode ? keyboardOption : hoverOption;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Keeps the opening title sequence working when domain reload is disabled in the editor.
            introPlayed = false;
            current = null;
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
        /// Saves current progress to the dedicated autosave slot and reopens the menu over the world.
        /// </summary>
        public static void ReturnToMainMenu()
        {
            if (FindAnyObjectByType<StartupMenu>() != null) return;
            // Peeking at a load and backing straight out leaves every file as it was.
            if (GameAutosave.HasPlayedSinceLoad) GameAutosave.SaveNow();
            new GameObject("[StartupMenu]").AddComponent<StartupMenu>();
        }

        private void Awake()
        {
            current = this;
            DontDestroyOnLoad(gameObject);
            previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            questHintsEnabled = PlayerPrefs.GetInt(QuestHintsEnabledKey, 1) != 0;
            hintsKnob = questHintsEnabled ? 1f : 0f;
            QuestHintOverlay.SetHintsEnabled(questHintsEnabled);
            GameSettingsPanel.ApplySavedVolume();
            GameSettingsPanel.ApplySavedDisplay();
            reduceMotion = GameSettingsPanel.ReduceMotion;
            SetGameplayInput(false);
            LoadButtonTextures();
            CreateArt();
            // The title sequence plays once per launch; returning from the journal goes straight to the menu.
            phase = introPlayed ? Phase.Menu : Phase.Intro;
            cameFromIntro = !introPlayed;
            introPlayed = true;
            StartCoroutine(InitializeAfterSceneSystems());
        }

        private IEnumerator InitializeAfterSceneSystems()
        {
            yield return null;
            GameSaveManager saves = GameSaveManager.Instance;
            if (saves.HasSave(GameSaveManager.AutosaveSlot))
            {
                backdropSlot = GameSaveManager.AutosaveSlot;
                if (!saves.Load(backdropSlot, false)) message = saves.LastError;
            }
            else
            {
                // Preserve pre-autosave games: promote the most recently played legacy slot into autosave,
                // while leaving the original file available as a manual snapshot.
                string legacySlot = saves.FindMostRecentSlot(GameSaveManager.ManualSlots);
                if (!string.IsNullOrWhiteSpace(legacySlot))
                {
                    if (saves.Load(legacySlot, false))
                    {
                        ManualSaveSlot.Link(legacySlot);
                        backdropSlot = GameSaveManager.AutosaveSlot;
                        if (!saves.Save(GameSaveManager.AutosaveSlot)) message = saves.LastError;
                        else saves.MarkPlayed(GameSaveManager.AutosaveSlot);
                    }
                    else message = saves.LastError;
                }
                else
                {
                    // First launch uses the scene's authored starting state. No placeholder save is
                    // created, so starting a new game on an empty profile needs no overwrite prompt.
                    GameEventManager.Instance.ReloadDefinitions();
                    DataDrivenWorldLoader worldLoader = DataDrivenWorldLoader.Instance;
                    if (worldLoader != null) worldLoader.ResetToStartingState();
                }
            }

            GameSaveOverlay.SetCurrentSlot(GameSaveManager.AutosaveSlot);
            float now = Time.unscaledTime;
            openedAt = phaseStartedAt = menuShownAt = now;
            cameraDrift.Begin(now);
            ready = true;
        }

        private void Update()
        {
            if (ready) SetGameplayInput(false);
            float now = Time.unscaledTime;
            float deltaTime = Time.unscaledDeltaTime;
            UpdatePointer(deltaTime);
            sparks.Tick(deltaTime);
            bool wasRebinding = settings.IsRebinding;
            settings.Tick();
            if (!ready) return;

            if (phase == Phase.Intro) UpdateIntro(now);
            else if (phase == Phase.Menu) UpdateMenuKeys(wasRebinding);
            else if (now - phaseStartedAt >= CloseSeconds)
            {
                CloseMenu();
                return;
            }

            UpdateRing(deltaTime);
            hintsKnob = Mathf.MoveTowards(hintsKnob, questHintsEnabled ? 1f : 0f, deltaTime * 6f);
            // Ease between page scales so moving from a short page to a tall one does not snap.
            uiScale = Mathf.Lerp(uiScale, FitScale(), 1f - Mathf.Exp(-12f * deltaTime));
        }

        private void LateUpdate()
        {
            if (!ready) return;
            float now = Time.unscaledTime;
            float returnBlend = phase == Phase.Closing ? Smooth((now - phaseStartedAt) / (CloseSeconds - 0.15f)) : 0f;
            if (!reduceMotion) cameraDrift.Apply(now, parallax, returnBlend);
        }

        private void OnDestroy()
        {
            if (current == this) current = null;
            Time.timeScale = previousTimeScale;
            SetGameplayInput(true);
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
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
            ClearThumbnails();
        }

        // ---- Input ----

        /// <summary>Tracks the pointer for parallax and leaves a trail of sparks behind the wand cursor.</summary>
        private void UpdatePointer(float deltaTime)
        {
            Mouse mouse = Mouse.current;
            if (mouse == null) return;
            Vector2 raw = mouse.position.ReadValue();
            Vector2 position = new Vector2(raw.x, Screen.height - raw.y);
            if (!pointerKnown)
            {
                pointer = lastTrail = position;
                pointerKnown = true;
            }
            if ((position - pointer).sqrMagnitude > 9f) keyboardMode = false;
            pointer = position;

            Vector2 target = new Vector2(Mathf.Clamp(position.x / Mathf.Max(1f, Screen.width) * 2f - 1f, -1f, 1f),
                Mathf.Clamp(position.y / Mathf.Max(1f, Screen.height) * 2f - 1f, -1f, 1f));
            parallax = reduceMotion ? Vector2.zero : Vector2.Lerp(parallax, target, 1f - Mathf.Exp(-2.5f * deltaTime));

            if (!ready || phase == Phase.Closing || reduceMotion)
            {
                lastTrail = position;
                return;
            }
            float moved = Vector2.Distance(position, lastTrail);
            if (moved > 240f)
            {
                lastTrail = position;
            }
            else
            {
                while (moved >= 10f)
                {
                    lastTrail = Vector2.MoveTowards(lastTrail, position, 10f);
                    moved -= 10f;
                    Color colour = sparks.Next() < 0.5f ? MenuArt.Gold : sparks.Next() < 0.5f ? MenuArt.Lavender : MenuArt.Cream;
                    sparks.Emit(lastTrail + new Vector2(sparks.Range(-3f, 3f), sparks.Range(-3f, 3f)),
                        new Vector2(sparks.Range(-14f, 14f), sparks.Range(-6f, 22f)), sparks.Range(0.35f, 0.7f),
                        sparks.Range(3.5f, 7f), colour, sparks.Next() < 0.25f, 2.5f, 25f);
                }
            }
            if (mouse.leftButton.wasPressedThisFrame) sparks.Burst(position, 14, 70f, 180f, 8f);
        }

        private void UpdateIntro(float now)
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            bool pressed = (keyboard != null && keyboard.anyKey.wasPressedThisFrame) || (mouse != null && mouse.leftButton.wasPressedThisFrame);
            if (pressed) EnterMenu(now);
        }

        private void EnterMenu(float now)
        {
            phase = Phase.Menu;
            phaseStartedAt = menuShownAt = now;
        }

        private void UpdateMenuKeys(bool wasRebinding)
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (!wasRebinding && keyboard.escapeKey.wasPressedThisFrame)
            {
                if (page == Page.Main) BeginClosing();
                else GoBack();
                return;
            }
            if (page != Page.Main) return;

            int step = keyboard.downArrowKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame ? 1
                : keyboard.upArrowKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame ? -1 : 0;
            if (step != 0)
            {
                keyboardOption = keyboardMode && keyboardOption >= 0 ? (keyboardOption + step + OptionKeys.Length) % OptionKeys.Length : step > 0 ? 0 : OptionKeys.Length - 1;
                keyboardMode = true;
            }
            bool confirm = keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame;
            if (confirm && keyboardMode && keyboardOption >= 0) Activate(keyboardOption);
        }

        /// <summary>
        /// The rune ring idles in a slow turn. When an option is chosen it turns that option's rune to the socket; once the
        /// rune is aligned the circuit closes and light travels along the thread to the button.
        /// </summary>
        private void UpdateRing(float deltaTime)
        {
            int selected = page == Page.Main && phase == Phase.Menu ? SelectedOption : -1;
            if (selected >= 0)
            {
                ringAngle = Mathf.SmoothDampAngle(ringAngle, -selected * (360f / OptionKeys.Length), ref ringVelocity, 0.28f, Mathf.Infinity, deltaTime);
            }
            else
            {
                ringAngle += 9f * deltaTime;
                ringVelocity = 0f;
            }
            ringAngle = Mathf.Repeat(ringAngle, 360f);

            for (int i = 0; i < threadPower.Length; i++)
            {
                bool aligned = Mathf.Abs(Mathf.DeltaAngle(ringAngle + i * (360f / OptionKeys.Length), 0f)) < 7f;
                bool powered = i == selected && aligned;
                threadPower[i] = Mathf.MoveTowards(threadPower[i], powered ? 1f : 0f, deltaTime * (powered ? 2.2f : 3f));
                runeLight[i] = Mathf.MoveTowards(runeLight[i], i == selected ? 1f : 0f, deltaTime * 5f);
            }
        }

        private void Activate(int option)
        {
            if (phase != Phase.Menu) return;
            if (option == 0) SetPage(Page.NewGame);
            else if (option == 1) SetPage(Page.Load);
            else if (option == 2)
            {
                questHintsEnabled = PlayerPrefs.GetInt(QuestHintsEnabledKey, 1) != 0;
                hintsKnob = questHintsEnabled ? 1f : 0f;
                SetPage(Page.Settings);
            }
            else if (option == 3) Application.Quit();
        }

        private void SetPage(Page next)
        {
            if (next == page) return;
            page = next;
            pageChangedAt = Time.unscaledTime;
            keyboardMode = false;
            if (next == Page.Load || next == Page.NewGame) RefreshSlotDetails();
        }

        /// <summary>Back buttons and Escape share one route so both always return to the parent page.</summary>
        private void GoBack()
        {
            if (page == Page.Load || page == Page.NewGame)
            {
                message = string.Empty;
                confirmSlot = null;
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

        // ---- Frame ----

        private void OnGUI()
        {
            if (!ready) return;
            EnsureStyles();

            float now = Time.unscaledTime;
            Event guiEvent = Event.current;
            bool repaint = guiEvent.type == EventType.Repaint;
            Color previousColor = GUI.color;
            Matrix4x4 previousMatrix = GUI.matrix;

            float opening = Smooth((now - openedAt) / FadeInSeconds);
            float sinceClose = phase == Phase.Closing ? now - phaseStartedAt : 0f;
            worldAlpha = opening * (phase == Phase.Closing ? 1f - Smooth((sinceClose - 0.2f) / 0.9f) : 1f);
            float panelAlpha = phase == Phase.Intro ? 0f
                : phase == Phase.Menu ? Smooth((now - menuShownAt - (cameFromIntro ? 0.3f : 0f)) / 0.7f)
                : closingPanelAlpha * (1f - Smooth(sinceClose / 0.35f));
            titleFly = cameFromIntro ? Smooth((now - menuShownAt) / IntroToMenuSeconds) : 1f;
            interactive = phase == Phase.Menu && panelAlpha > 0.6f;
            if (!interactive) hoverOption = -1;
            // While the panel is fading in or out, swallow pointer and key events so nothing is half-clicked.
            if (!interactive && (guiEvent.isMouse || guiEvent.isKey || guiEvent.type == EventType.ScrollWheel)) guiEvent.Use();

            fade = worldAlpha;
            if (repaint)
            {
                Draw(new Rect(0f, 0f, Screen.width, Screen.height), backdropTexture, Color.white, 1f);
                if (!reduceMotion) sparks.DrawMotes(now, worldAlpha, parallax, glowTexture, sparkleTexture);
            }

            // The backdrop and motes fill the real window; the title and panels are laid out in view units scaled to fit.
            if (!uiScaleSet)
            {
                uiScale = FitScale();
                uiScaleSet = true;
            }
            viewSize = new Vector2(Screen.width, Screen.height) / uiScale;
            GUI.matrix = previousMatrix * Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));

            if (phase == Phase.Intro)
            {
                DrawIntro(now, repaint);
            }
            else
            {
                Rect panel = PanelRect(panelAlpha);
                lastPanel = new Rect(panel.x * uiScale, panel.y * uiScale, panel.width * uiScale, panel.height * uiScale);
                lastPanelAlpha = panelAlpha;
                fade = panelAlpha;
                if (repaint) DrawPanelFrame(panel, now);
                // Each page fades in and settles upward slightly so page changes feel gentle rather than abrupt.
                float pageIn = Smooth((now - pageChangedAt) / PageFadeSeconds);
                fade = panelAlpha * pageIn;
                DrawPage(page, new Rect(panel.x, panel.y + (1f - pageIn) * 8f, panel.width, panel.height), now);
            }

            GUI.matrix = previousMatrix;
            if (repaint)
            {
                fade = 1f;
                sparks.DrawSparks(opening, glowTexture, sparkleTexture);
            }
            GUI.color = previousColor;
            GUI.matrix = previousMatrix;
        }

        private Rect PanelRect(float panelAlpha)
        {
            Vector2 size = PageSize(page);
            return new Rect((viewSize.x - size.x) * 0.5f - parallax.x * 5f,
                (viewSize.y - size.y) * 0.5f + (1f - panelAlpha) * (reduceMotion ? 0f : 18f) - parallax.y * 4f, size.x, size.y);
        }

        /// <summary>
        /// Authored page size in view units. The Controls list scrolls, so that page gives up height (down to 440) before
        /// the menu needs to shrink.
        /// </summary>
        private static Vector2 PageSize(Page value)
        {
            return value == Page.Main ? new Vector2(640f, 520f)
                : value == Page.Load ? new Vector2(700f, 780f)
                : value == Page.NewGame ? new Vector2(700f, 690f)
                : value == Page.Controls ? new Vector2(560f, Mathf.Clamp(Screen.height - 40f, 440f, 720f))
                : new Vector2(560f, 560f);
        }

        /// <summary>1 when the current page (or the opening title) fits with a margin; otherwise just small enough to fit.</summary>
        private float FitScale()
        {
            Vector2 needed = phase == Phase.Intro ? new Vector2(808f, 330f) : PageSize(page) + new Vector2(48f, 24f);
            return Mathf.Min(1f, Screen.width / needed.x, Screen.height / needed.y);
        }

        private void DrawPage(Page value, Rect panel, float now)
        {
            float top = DrawTitle(value, panel, now);
            if (value == Page.Main) DrawMain(panel, top, now);
            else if (value == Page.Load) DrawLoad(panel, top);
            else if (value == Page.NewGame) DrawNewGame(panel, top);
            else if (value == Page.Controls) DrawControls(panel, top);
            else DrawSettings(panel, top);
        }

        // ---- Opening title ----

        private Rect IntroTitleRect() => new Rect(viewSize.x * 0.5f - 380f, viewSize.y * 0.5f - 110f, 760f, 120f);

        private void DrawIntro(float now, bool repaint)
        {
            float progress = (now - phaseStartedAt - IntroLightDelay) / IntroLightSeconds;
            Rect titleRect = IntroTitleRect();
            fade = worldAlpha;

            float pulse = 0.5f + 0.5f * Mathf.Sin(now * 1.6f);
            Draw(Expand(titleRect, -120f, 0f), glowTexture, MenuArt.Lavender, (0.12f + 0.14f * pulse) * Smooth(progress));
            Vector2 spark = DrawLanternTitle(GameTitle, titleRect, IntroFontSize, progress, now);

            if (repaint && progress > 0f && progress < 1f)
            {
                Draw(new Rect(spark.x - 22f, spark.y - 22f, 44f, 44f), glowTexture, MenuArt.Cream, 0.9f);
                Draw(new Rect(spark.x - 13f, spark.y - 13f, 26f, 26f), sparkleTexture, MenuArt.Gold, 1f);
                sparks.Emit(spark * uiScale, new Vector2(sparks.Range(-30f, 30f), sparks.Range(-10f, 40f)), sparks.Range(0.4f, 0.8f),
                    sparks.Range(4f, 9f), sparks.Next() < 0.6f ? MenuArt.Gold : MenuArt.Cream, sparks.Next() < 0.3f, 2f, 30f);
            }
            if (repaint && progress >= 1f && !introBurstDone)
            {
                introBurstDone = true;
                sparks.Burst(spark * uiScale, 22, 80f, 220f, 10f);
            }

            float promptIn = Smooth((progress - 1.1f) / 0.5f);
            if (promptIn <= 0f) return;
            DrawDivider(titleRect.center.x, titleRect.yMax + 14f, 300f, now, promptIn);
            Label(new Rect(titleRect.x, titleRect.yMax + 40f, titleRect.width, 32f), "Press any key to begin", promptStyle, MenuArt.Cream,
                promptIn * (0.55f + 0.45f * Mathf.Sin(now * 2.4f)));
        }

        /// <summary>
        /// Draws a title letter by letter like a row of lanterns. A spark sweeps across as <paramref name="progress"/> goes from 0
        /// to 1, each letter flaring as it lights; lit letters keep a gentle flicker. Returns the spark's position.
        /// </summary>
        private Vector2 DrawLanternTitle(string text, Rect rect, int fontSize, float progress, float now)
        {
            letterStyle.fontSize = fontSize;
            float padding = letterStyle.padding.horizontal;
            float total = letterStyle.CalcSize(new GUIContent(text)).x - padding;
            float x0 = rect.center.x - total * 0.5f;
            float sparkX = Mathf.Lerp(x0 - fontSize * 0.2f, x0 + total + fontSize * 0.2f, Mathf.Clamp01(progress));
            float letterHeight = fontSize * 1.4f;
            float y = rect.center.y - letterHeight * 0.5f;
            float previous = 0f;

            for (int i = 0; i < text.Length; i++)
            {
                float next = letterStyle.CalcSize(new GUIContent(text.Substring(0, i + 1))).x - padding;
                float left = x0 + previous;
                float width = next - previous;
                previous = next;
                if (char.IsWhiteSpace(text[i])) continue;

                float centre = left + width * 0.5f;
                float lit = progress >= 1f ? 1f : Mathf.Clamp01((sparkX - centre) / (fontSize * 0.5f) + 0.5f);
                float flare = Mathf.Sin(lit * Mathf.PI);
                float flicker = 0.86f + 0.14f * Mathf.PerlinNoise(now * 2.2f, i * 1.37f);
                string letter = text[i].ToString();
                Rect glyph = new Rect(left - letterStyle.padding.left, y, width + padding + fontSize * 0.2f, letterHeight);

                Draw(new Rect(centre - fontSize * 0.55f, rect.center.y - fontSize * 0.6f, fontSize * 1.1f, fontSize * 1.2f),
                    glowTexture, MenuArt.Gold, 0.16f * flicker * lit + 0.45f * flare);
                Label(new Rect(glyph.x, glyph.y + fontSize * 0.05f, glyph.width, glyph.height), letter, letterStyle, MenuArt.Ink, 0.75f);
                Color colour = Color.Lerp(MenuArt.Dusk, Color.Lerp(MenuArt.Gold, MenuArt.Cream, 0.35f * flare), lit);
                float brightness = Mathf.Lerp(1f, flicker, lit);
                Label(glyph, letter, letterStyle, new Color(colour.r * brightness, colour.g * brightness, colour.b * brightness), Mathf.Lerp(0.75f, 1f, lit));
            }

            float hop = progress > 0f && progress < 1f ? Mathf.Abs(Mathf.Sin(progress * text.Length * Mathf.PI)) * fontSize * 0.22f : 0f;
            return new Vector2(sparkX, rect.center.y - fontSize * 0.42f - hop);
        }

        private float DrawTitle(Page value, Rect panel, float now)
        {
            bool main = value == Page.Main;
            string title = main ? GameTitle : value == Page.Load ? "Load Game" : value == Page.NewGame ? "New Game"
                : value == Page.Controls ? "Controls" : "Settings";
            float height = main ? 76f : 54f;
            Rect rect = new Rect(panel.x + 24f, panel.y + 24f, panel.width - 48f, height);
            int size = main ? MainTitleFontSize : 36;

            float savedFade = fade;
            if (main && titleFly < 1f)
            {
                // The opening title glides from the centre of the screen into the panel while the panel fades in around it.
                Rect from = IntroTitleRect();
                rect = new Rect(Mathf.Lerp(from.x, rect.x, titleFly), Mathf.Lerp(from.y, rect.y, titleFly),
                    Mathf.Lerp(from.width, rect.width, titleFly), Mathf.Lerp(from.height, rect.height, titleFly));
                size = Mathf.RoundToInt(Mathf.Lerp(IntroFontSize, MainTitleFontSize, titleFly));
                fade = worldAlpha;
            }
            float pulse = 0.5f + 0.5f * Mathf.Sin(now * 1.6f);
            Draw(Expand(rect, -rect.width * 0.18f, 4f), glowTexture, MenuArt.Lavender, 0.2f + 0.12f * pulse);
            DrawLanternTitle(title, rect, size, 1f, now);
            fade = savedFade;

            float y = panel.y + 24f + height + 4f;
            DrawDivider(panel.center.x, y + 6f, Mathf.Min(280f, panel.width - 120f), now, 1f);
            return y + 30f;
        }

        private void DrawDivider(float centerX, float y, float width, float now, float alpha)
        {
            if (Event.current.type != EventType.Repaint) return;
            Draw(new Rect(centerX - width * 0.5f, y - 2f, width, 4f), glowTexture, MenuArt.Gold, 0.85f * alpha);
            for (int side = -1; side <= 1; side += 2)
            {
                float twinkle = 0.5f + 0.5f * Mathf.Sin(now * 2.2f + side);
                Draw(new Rect(centerX + side * width * 0.32f - 4f, y - 4f, 8f, 8f), glowTexture, MenuArt.Cream, (0.5f + 0.4f * twinkle) * alpha);
            }

            Rect star = new Rect(centerX - 12f, y - 12f, 24f, 24f);
            Matrix4x4 matrix = GUI.matrix;
            RotateLocal(Mathf.Sin(now * 0.8f) * 12f, star.center);
            Draw(Expand(star, 10f, 10f), glowTexture, MenuArt.Gold, 0.5f * alpha);
            Draw(star, sparkleTexture, MenuArt.Gold, alpha);
            GUI.matrix = matrix;
        }

        private void DrawPanelFrame(Rect panel, float now)
        {
            Draw(Expand(panel, 70f, 70f), glowTexture, MenuArt.Lavender, 0.22f);
            Box(panel, panelStyle, 1f);
            Rect trim = Expand(panel, -8f, -8f);
            Box(trim, panelTrimStyle, 1f);

            // Small stars twinkle in the trim corners, out of step with each other.
            for (int i = 0; i < 4; i++)
            {
                Vector2 corner = new Vector2(i % 2 == 0 ? trim.x : trim.xMax, i < 2 ? trim.y : trim.yMax);
                float twinkle = 0.5f + 0.5f * Mathf.Sin(now * 1.7f + i * 1.9f);
                float size = 12f + 6f * twinkle;
                Draw(new Rect(corner.x - size * 0.5f, corner.y - size * 0.5f, size, size), sparkleTexture, MenuArt.Gold, 0.45f + 0.5f * twinkle);
            }
        }

        // ---- Main page: rune ring and woven light ----

        private void DrawMain(Rect panel, float top, float now)
        {
            Vector2 centre = new Vector2(panel.x + 136f, top + 149f);
            Vector2 socket = centre + new Vector2(RingRadius + 24f, 0f);
            float buttonX = panel.x + 380f;
            for (int i = 0; i < OptionKeys.Length; i++)
            {
                optionRects[i] = SpriteRect(OptionKeys[i], buttonX, top + i * 78f, 62f);
                float angle = (ringAngle + i * (360f / OptionKeys.Length)) * Mathf.Deg2Rad;
                runePositions[i] = centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * RingRadius;
            }

            if (interactive)
            {
                Vector2 mouse = Event.current.mousePosition;
                hoverOption = -1;
                for (int i = 0; i < OptionKeys.Length; i++)
                {
                    if (optionRects[i].Contains(mouse) || (mouse - runePositions[i]).sqrMagnitude < 22f * 22f) hoverOption = i;
                }
            }
            int selected = interactive ? SelectedOption : -1;

            if (Event.current.type == EventType.Repaint)
            {
                DrawRing(centre, socket, selected, now);
                for (int i = 0; i < OptionKeys.Length; i++)
                {
                    Rect target = optionRects[i];
                    DrawThread(socket, new Vector2(target.x + 4f, target.center.y), threadPower[i], i, now);
                }
                DrawRunes();
            }

            for (int i = 0; i < OptionKeys.Length; i++)
            {
                if (Button(OptionKeys[i], optionRects[i], OptionLabels[i], selected == i, threadPower[i])) Activate(i);
            }
            if (interactive)
            {
                SetColor(Color.white, 1f);
                for (int i = 0; i < OptionKeys.Length; i++)
                {
                    Vector2 rune = runePositions[i];
                    if (GUI.Button(new Rect(rune.x - 20f, rune.y - 20f, 40f, 40f), GUIContent.none, GUIStyle.none)) Activate(i);
                }
            }
            DrawMessage(new Rect(panel.x + 36f, panel.yMax - 82f, panel.width - 72f, 58f));
        }

        private void DrawRing(Vector2 centre, Vector2 socket, int selected, float now)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(now * 1.4f);
            Draw(new Rect(centre.x - 130f, centre.y - 130f, 260f, 260f), glowTexture, MenuArt.Lavender, 0.22f);

            Matrix4x4 matrix = GUI.matrix;
            float outer = RingDrawSize(RingRadius, OuterRingTexture, OuterRingTick);
            RotateLocal(ringAngle, centre);
            Draw(new Rect(centre.x - outer * 0.5f, centre.y - outer * 0.5f, outer, outer), ringTexture, MenuArt.Gold, 0.75f);
            GUI.matrix = matrix;
            float inner = RingDrawSize(InnerRingRadius, InnerRingTexture, InnerRingTick);
            RotateLocal(-now * 12f, centre);
            Draw(new Rect(centre.x - inner * 0.5f, centre.y - inner * 0.5f, inner, inner), innerRingTexture, MenuArt.Lavender, 0.6f);
            GUI.matrix = matrix;

            for (int i = 0; i < runePositions.Length; i++)
            {
                float power = threadPower[i];
                DrawLine(centre, runePositions[i], 3f, MenuArt.Gold, 0.16f);
                if (power > 0f) DrawLine(centre, Vector2.Lerp(centre, runePositions[i], power), 4.5f, MenuArt.ThreadLive, 0.7f * power);
            }

            // The lumen orb at the heart of the ring brightens while a circuit is closed.
            float charge = selected >= 0 ? threadPower[selected] : 0f;
            Draw(new Rect(centre.x - 36f, centre.y - 36f, 72f, 72f), glowTexture, Color.Lerp(MenuArt.Gold, MenuArt.ThreadLive, charge), 0.55f + 0.25f * pulse);
            RotateLocal(now * 20f, centre);
            Draw(new Rect(centre.x - 15f, centre.y - 15f, 30f, 30f), sparkleTexture, MenuArt.Cream, 0.9f);
            GUI.matrix = matrix;

            float live = 0f;
            for (int i = 0; i < threadPower.Length; i++) live = Mathf.Max(live, threadPower[i]);
            DrawLine(socket - new Vector2(12f, 0f), socket, 4f, Color.Lerp(MenuArt.Gold, MenuArt.ThreadLive, live), 0.7f);
            Draw(new Rect(socket.x - 14f, socket.y - 14f, 28f, 28f), glowTexture, Color.Lerp(MenuArt.ThreadIdle, MenuArt.ThreadLive, live), 0.35f + 0.5f * live);
            Draw(new Rect(socket.x - 8f, socket.y - 8f, 16f, 16f), sparkleTexture, Color.Lerp(MenuArt.Muted, MenuArt.Cream, live), 0.9f);
        }

        private void DrawRunes()
        {
            for (int i = 0; i < runePositions.Length; i++)
            {
                Vector2 rune = runePositions[i];
                float light = runeLight[i];
                float power = threadPower[i];
                if (light > 0f) Draw(new Rect(rune.x - 30f, rune.y - 30f, 60f, 60f), glowTexture, Color.Lerp(MenuArt.Gold, MenuArt.ThreadLive, power), 0.6f * light);
                Draw(new Rect(rune.x - 18f, rune.y - 18f, 36f, 36f), discTexture, Color.white, 1f);
                Color stroke = Color.Lerp(MenuArt.Muted, Color.Lerp(MenuArt.Gold, MenuArt.Cream, power), light);
                Vector2[] segments = Runes[i];
                for (int s = 0; s + 1 < segments.Length; s += 2)
                    DrawLine(rune + segments[s] * 8f, rune + segments[s + 1] * 8f, 3.4f, stroke, 0.95f);
            }
        }

        /// <summary>
        /// A weave thread from the ring's socket to a button: amber and faint while idle, then filled with cyan light from the
        /// socket outward as <paramref name="power"/> rises, with beads of light travelling along it once fully powered.
        /// </summary>
        private void DrawThread(Vector2 from, Vector2 to, float power, int index, float now)
        {
            const int Segments = 18;
            float flicker = 0.85f + 0.15f * Mathf.Sin(now * 3f + index * 1.7f);
            Vector2 previous = from;
            for (int s = 1; s <= Segments; s++)
            {
                float t = s / (float)Segments;
                Vector2 point = ThreadPoint(from, to, t);
                float startT = (s - 1) / (float)Segments;
                if (startT < power)
                {
                    Vector2 end = t <= power ? point : Vector2.Lerp(previous, point, (power - startT) * Segments);
                    DrawLine(previous, end, 12f, MenuArt.ThreadLive, 0.22f);
                    DrawLine(previous, end, 4.5f, MenuArt.ThreadLive, 0.95f);
                }
                if (t > power) DrawLine(previous, point, 3.5f, MenuArt.ThreadIdle, 0.35f * flicker);
                previous = point;
            }

            if (power > 0f && power < 1f)
            {
                Vector2 head = ThreadPoint(from, to, power);
                Draw(new Rect(head.x - 10f, head.y - 10f, 20f, 20f), glowTexture, MenuArt.Cream, 0.95f);
            }
            if (power >= 0.999f)
            {
                for (int k = 0; k < 3; k++)
                {
                    Vector2 bead = ThreadPoint(from, to, Mathf.Repeat(now * 0.7f + k / 3f, 1f));
                    Draw(new Rect(bead.x - 9f, bead.y - 9f, 18f, 18f), glowTexture, MenuArt.ThreadLive, 0.9f);
                    Draw(new Rect(bead.x - 3f, bead.y - 3f, 6f, 6f), glowTexture, MenuArt.Cream, 1f);
                }
            }
        }

        private static Vector2 ThreadPoint(Vector2 from, Vector2 to, float t)
        {
            // Cubic curve that leaves the socket and meets the button horizontally, with a slight droop like slack thread.
            Vector2 c1 = from + new Vector2(50f, 0f);
            Vector2 c2 = to - new Vector2(50f, 0f);
            float u = 1f - t;
            Vector2 point = u * u * u * from + 3f * u * u * t * c1 + 3f * u * t * t * c2 + t * t * t * to;
            point.y += Mathf.Sin(t * Mathf.PI) * 8f;
            return point;
        }

        private static float RingDrawSize(float radius, int textureSize, int tickLength)
        {
            float circle = textureSize * 0.5f - tickLength - 2f;
            return radius * 2f * (textureSize * 0.5f) / circle;
        }

        // ---- Load page: journal cards ----

        private void DrawLoad(Rect panel, float top)
        {
            float y = top + 4f;
            for (int i = 0; i < Slots.Length; i++)
            {
                if (DrawSlotCard(i, new Rect(panel.x + 32f, y, panel.width - 64f, 118f), "load", "LOAD", false)) LoadSlot(Slots[i]);
                y += 130f;
            }

            Rect back = BackRect(panel);
            DrawMessage(new Rect(panel.x + 36f, back.y - 58f, panel.width - 72f, 52f));
            if (Button("exit", back, "BACK")) GoBack();
        }

        /// <summary>New Game: choose one of the manual slots to play in. Empty slots start straight away.</summary>
        private void DrawNewGame(Rect panel, float top)
        {
            Label(new Rect(panel.x + 36f, top - 4f, panel.width - 72f, 24f), "Choose a page for your new story", promptStyle, MenuArt.Muted);
            float y = top + 30f;
            for (int i = 1; i < Slots.Length; i++)
            {
                if (DrawSlotCard(i, new Rect(panel.x + 32f, y, panel.width - 64f, 118f), "start", "START", true)) StartNewGame(Slots[i]);
                y += 130f;
            }

            Rect back = BackRect(panel);
            DrawMessage(new Rect(panel.x + 36f, back.y - 58f, panel.width - 72f, 52f));
            if (Button("exit", back, "BACK")) GoBack();
        }

        /// <summary>
        /// One save slot as a journal card: thumbnail, title with its tags, location, quest and stars, and a sprite button
        /// with the save time and playtime under it. Empty slots show the button only when <paramref name="buttonWhenEmpty"/>.
        /// Returns true when the button was pressed.
        /// </summary>
        private bool DrawSlotCard(int i, Rect card, string buttonKey, string buttonLabel, bool buttonWhenEmpty)
        {
            GameSaveManager saves = GameSaveManager.Instance;
            string slot = Slots[i];
            bool isAutosave = i == 0;
            bool hasSave = saves.HasSave(slot);
            Box(card, cardStyle, hasSave ? 1f : 0.55f);

            Rect thumb = new Rect(card.x + 14f, card.y + 14f, 160f, 90f);
            if (hasSave && slotThumbnails[i] != null)
            {
                SetColor(Color.white, 1f);
                GUI.DrawTexture(thumb, slotThumbnails[i], ScaleMode.ScaleAndCrop, false);
                Box(thumb, thumbFrameStyle, 1f);
            }
            else
            {
                Box(thumb, thumbFrameStyle, 0.6f);
                Draw(new Rect(thumb.center.x - 12f, thumb.center.y - 12f, 24f, 24f), sparkleTexture, MenuArt.Muted, hasSave ? 0.6f : 0.35f);
            }

            // Right column: the button with the save time and playtime tucked underneath it.
            float textX = thumb.xMax + 16f;
            Rect button = SpriteRect(buttonKey, 0f, 0f, 50f);
            button = new Rect(card.xMax - 14f - button.width, card.y + 12f, button.width, button.height);
            float textWidth = button.x - 14f - textX;
            string title = isAutosave ? "Autosave" : $"Manual Slot {i}";
            Rect titleRect = new Rect(textX, card.y + 10f, textWidth, 26f);
            Label(titleRect, title, rowStyle, hasSave ? MenuArt.Cream : MenuArt.Muted);
            float badgeX = titleRect.x + rowStyle.CalcSize(new GUIContent(title)).x + 10f;
            // Most recent marks the save the backdrop was restored from; Current marks the slot this game belongs to.
            if (hasSave && slot == backdropSlot) badgeX = DrawSlotTag(badgeX, titleRect, "Most recent", MenuArt.Lavender);
            if (slot == linkedSlot) DrawSlotTag(badgeX, titleRect, "Current", MenuArt.Gold);

            bool pressed = false;
            if (hasSave)
            {
                SaveSlotSummary.Info info = slotInfos[i];
                string location = info != null && !string.IsNullOrWhiteSpace(info.location) ? info.location : "Somewhere in Lumenfall";
                string quest = info != null && !string.IsNullOrWhiteSpace(info.quest) ? info.quest : string.Empty;
                FitLabel(new Rect(textX, card.y + 37f, textWidth, 22f), location, loadDetailStyle, MenuArt.Cream, 1f, 15);
                FitLabel(new Rect(textX, card.y + 59f, textWidth, 22f), quest, loadDetailStyle, MenuArt.Muted, 1f, 15);
                if (info != null && info.questsTotal > 0) DrawQuestStars(new Rect(textX, card.y + 84f, textWidth, 16f), info.questsDone, info.questsTotal);

                Rect details = new Rect(button.x - 10f, button.yMax + 6f, button.width + 20f, 18f);
                Draw(new Rect(details.x + 16f, details.y - 2f, details.width - 32f, 3f), glowTexture, MenuArt.Gold, 0.4f);
                Label(new Rect(details.x, details.y + 2f, details.width, 20f), slotDetails[i], loadMetaStyle, MenuArt.Cream, 0.9f);
                if (info != null)
                    Label(new Rect(details.x, details.y + 22f, details.width, 20f), FormatPlaytime(info.playSeconds), loadMetaStyle, MenuArt.Muted);
                pressed = Button(buttonKey, button, buttonLabel);
            }
            else
            {
                Label(new Rect(textX, card.y + 45f, (buttonWhenEmpty ? button.x - 14f : card.xMax - 14f) - textX, 24f),
                    "Empty — a fresh page awaits", loadDetailStyle, MenuArt.Muted);
                if (buttonWhenEmpty) pressed = Button(buttonKey, button, buttonLabel);
            }
            return pressed;
        }

        /// <summary>A small sparkle and label after a slot title. Returns the x where a following tag can start.</summary>
        private float DrawSlotTag(float x, Rect titleRect, string text, Color colour)
        {
            Draw(new Rect(x, titleRect.center.y - 7f, 14f, 14f), sparkleTexture, colour, 0.9f);
            Label(new Rect(x + 18f, titleRect.y, titleRect.xMax - x - 18f, titleRect.height), text, mutedStyle, colour);
            return x + 18f + mutedStyle.CalcSize(new GUIContent(text)).x + 12f;
        }

        /// <summary>One star per quest, lit gold for each completed one, followed by a short count.</summary>
        private void DrawQuestStars(Rect rect, int done, int total)
        {
            float x = rect.x;
            for (int i = 0; i < total; i++)
            {
                Rect star = new Rect(x, rect.center.y - 6f, 12f, 12f);
                if (i < done)
                {
                    Draw(Expand(star, 4f, 4f), glowTexture, MenuArt.Gold, 0.5f);
                    Draw(star, sparkleTexture, MenuArt.Gold, 1f);
                }
                else
                {
                    Draw(Expand(star, -3f, -3f), glowTexture, MenuArt.Muted, 0.45f);
                }
                x += 15f;
            }
            Label(new Rect(x + 4f, rect.y - 2f, rect.xMax - x - 4f, rect.height + 4f), $"{done} of {total} quests", smallStyle, MenuArt.Muted, 0.85f);
        }

        private static string FormatPlaytime(float seconds)
        {
            int minutes = Mathf.FloorToInt(seconds / 60f);
            if (minutes < 1) return "under a minute played";
            int hours = minutes / 60;
            return hours > 0 ? $"{hours}h {minutes % 60}m played" : $"{minutes}m played";
        }

        // ---- Settings and controls ----

        private void DrawSettings(Rect panel, float top)
        {
            Rect card = new Rect(panel.x + 36f, top + 4f, panel.width - 72f, 196f);
            Box(card, cardStyle, 1f);
            float x = card.x + 24f;
            float width = card.width - 48f;

            float masterVolume = GameSettingsPanel.MasterVolume;
            Label(new Rect(x, card.y + 16f, width, 30f), "Master volume", rowStyle, MenuArt.Cream);
            Label(new Rect(x, card.y + 16f, width, 30f), $"{Mathf.RoundToInt(masterVolume * 100f)}%", valueStyle, MenuArt.Gold);
            float newVolume = Slider(new Rect(x, card.y + 54f, width, 26f), masterVolume);
            if (!Mathf.Approximately(newVolume, masterVolume)) GameSettingsPanel.MasterVolume = newVolume;

            Draw(new Rect(x, card.y + 98f, width, 4f), glowTexture, MenuArt.Gold, 0.45f);

            Label(new Rect(x, card.y + 114f, width - 80f, 28f), "Show quest hints", rowStyle, MenuArt.Cream);
            Label(new Rect(x, card.y + 142f, width - 80f, 24f), "Gentle nudges toward what to do next", mutedStyle, MenuArt.Muted);
            DrawSwitch(new Rect(card.xMax - 24f - 60f, card.y + 124f, 60f, 30f), hintsKnob);
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
                rowStyle.normal.textColor = MenuArt.Cream;
                messageStyle.normal.textColor = MenuArt.Ember;
                groupStyle.normal.textColor = MenuArt.Gold;
                GUILayout.BeginArea(area);
                settings.DrawControls(rowStyle, keyStyle, messageStyle, area.height - 96f, groupStyle);
                GUILayout.EndArea();
            }
            finally
            {
                GUI.skin.verticalScrollbar = previousScrollbar;
                GUI.skin.verticalScrollbarThumb = previousThumb;
            }
            if (Button("exit", back, "BACK")) GoBack();
        }

        private static Rect BackRect(Rect panel) => new Rect(panel.x + 80f, panel.yMax - 92f, panel.width - 160f, 60f);

        // ---- Controls ----

        /// <summary>A sprite-sheet button at its natural aspect ratio, placed with its left edge at <paramref name="x"/>.</summary>
        private Rect SpriteRect(string textureKey, float x, float y, float height)
        {
            float aspect = buttonTextures.TryGetValue(textureKey, out Texture2D texture) && texture != null ? SheetAspect(texture) : 3.3f;
            return new Rect(x, y, height * aspect, height);
        }

        private static float SheetAspect(Texture2D texture) => texture.width / (texture.height * 0.25f);

        private bool Button(string textureKey, Rect rect, string fallbackLabel, bool highlighted = false, float power = 0f)
        {
            if (!buttonTextures.TryGetValue(textureKey, out Texture2D texture) || texture == null)
            {
                SetColor(Color.white, 1f);
                return GUI.Button(rect, fallbackLabel, textButtonStyle);
            }

            // Sheets hold four stacked frames: normal, hover, pressed, then disabled.
            float aspect = SheetAspect(texture);
            float drawWidth = Mathf.Min(rect.width, rect.height * aspect);
            float drawHeight = drawWidth / aspect;
            Rect hitRect = new Rect(rect.center.x - drawWidth * 0.5f, rect.center.y - drawHeight * 0.5f, drawWidth, drawHeight);
            bool hover = interactive && hitRect.Contains(Event.current.mousePosition);
            bool lit = hover || highlighted;
            bool pressed = hover && Mouse.current != null && Mouse.current.leftButton.isPressed;
            Rect drawRect = lit && !pressed ? new Rect(hitRect.x, hitRect.y - 2f, hitRect.width, hitRect.height) : hitRect;

            if (lit) Draw(Expand(drawRect, 34f, 22f), glowTexture, Color.Lerp(MenuArt.Lavender, MenuArt.ThreadLive, power), pressed ? 0.3f : 0.5f);
            SetColor(Color.white, 1f);
            float row = pressed ? 0.25f : lit ? 0.5f : 0.75f;
            GUI.DrawTextureWithTexCoords(drawRect, texture, new Rect(0f, row, 1f, 0.25f), true);
            return GUI.Button(hitRect, GUIContent.none, GUIStyle.none);
        }

        private float Slider(Rect rect, float value)
        {
            const float Knob = 24f;
            SetColor(Color.white, 1f);
            float next = GUI.HorizontalSlider(rect, value, 0f, 1f, sliderStyle, thumbStyle);

            Rect track = new Rect(rect.x + Knob * 0.5f, rect.center.y - 5f, rect.width - Knob, 10f);
            Box(track, pillStyle, 0.9f, MenuArt.Ink);
            float filled = track.width * next;
            if (filled >= 10f) Box(new Rect(track.x, track.y, filled, track.height), pillStyle, 0.95f, MenuArt.Lavender);

            float knobX = rect.x + Knob * 0.5f + next * (rect.width - Knob);
            Draw(new Rect(knobX - 20f, rect.center.y - 20f, 40f, 40f), glowTexture, MenuArt.Gold, 0.5f);
            Draw(new Rect(knobX - Knob * 0.5f, rect.center.y - Knob * 0.5f, Knob, Knob), knobTexture, MenuArt.Gold, 1f);
            return next;
        }

        private void DrawSwitch(Rect rect, float on)
        {
            Draw(rect, switchTexture, Color.Lerp(MenuArt.Dusk, MenuArt.Lavender, on), 1f);
            float knobX = Mathf.Lerp(rect.x + 3f, rect.xMax - 27f, on);
            Rect knob = new Rect(knobX, rect.center.y - 12f, 24f, 24f);
            if (on > 0.01f) Draw(Expand(knob, 10f, 10f), glowTexture, MenuArt.Gold, 0.5f * on);
            Draw(knob, knobTexture, Color.Lerp(MenuArt.Cream, MenuArt.Gold, on), 1f);
        }

        private void DrawMessage(Rect rect)
        {
            if (string.IsNullOrEmpty(message)) return;
            Box(rect, cardStyle, 1f);
            Label(rect, message, messageStyle, MenuArt.Ember);
        }

        // ---- Game flow ----

        /// <summary>
        /// Starts a new game in a manual slot. A filled slot, or an existing autosave, needs a second START to confirm. The
        /// run is linked to the slot straight away, but nothing is written until the run's first save (30 seconds of play,
        /// or an earlier Save), so a new game abandoned at once leaves the old files intact.
        /// </summary>
        private void StartNewGame(string slot)
        {
            GameSaveManager saves = GameSaveManager.Instance;
            bool replacesSlot = saves.HasSave(slot);
            if ((replacesSlot || saves.HasSave(GameSaveManager.AutosaveSlot)) && confirmSlot != slot)
            {
                confirmSlot = slot;
                message = replacesSlot
                    ? $"{ManualSaveSlot.Label(slot)} already holds a save. Press START again; it is replaced once the new game first saves."
                    : "A new game replaces your autosave once it first saves. Press START again to begin.";
                return;
            }

            GameEventManager.Instance.ReloadDefinitions();
            DataDrivenWorldLoader worldLoader = DataDrivenWorldLoader.Instance;
            if (worldLoader != null) worldLoader.ResetToStartingState();
            ManualSaveSlot.Link(slot);
            SaveSlotSummary.BeginNewGame();
            GameSaveOverlay.SetCurrentSlot(GameSaveManager.AutosaveSlot);
            GameAutosave.BeginNewRun(slot);
            backdropSlot = GameSaveManager.AutosaveSlot;
            confirmSlot = null;
            message = string.Empty;
            BeginClosing();
        }

        private void LoadSlot(string slot)
        {
            GameSaveManager saves = GameSaveManager.Instance;
            if (saves == null || !saves.HasSave(slot))
            {
                message = $"{slot} is empty. Choose a slot with a saved game to load.";
                return;
            }

            bool isManual = Array.IndexOf(GameSaveManager.ManualSlots, slot) >= 0;
            if (saves.Load(slot, !isManual))
            {
                GameSaveOverlay.SetCurrentSlot(GameSaveManager.AutosaveSlot);
                if (isManual)
                {
                    // Older manual saves carry no link, so set it from the slot that was chosen. The manual file stays a
                    // source snapshot: play continues in autosave, which is first written after 30 seconds of real play.
                    ManualSaveSlot.Link(slot);
                    GameAutosave.BeginNewRun();
                }
                else GameAutosave.RestartTimer();
                backdropSlot = GameSaveManager.AutosaveSlot;
                message = string.Empty;
                BeginClosing();
            }
            else
            {
                message = saves.LastError ?? "That save could not be loaded.";
            }
        }

        /// <summary>
        /// Dissolves the panel into drifting light while the camera eases back to the player. Input stays paused until
        /// <see cref="CloseMenu"/> runs at the end of the transition.
        /// </summary>
        private void BeginClosing()
        {
            if (phase == Phase.Closing) return;
            phase = Phase.Closing;
            phaseStartedAt = Time.unscaledTime;
            closingPanelAlpha = lastPanelAlpha;
            sparks.Dissolve(lastPanel, 170);
        }

        private void CloseMenu()
        {
            cameraDrift.Release();
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

        private void RefreshSlotDetails()
        {
            ClearThumbnails();
            linkedSlot = ManualSaveSlot.Linked;
            string directory = GameSaveManager.Instance.SaveDirectory;
            for (int i = 0; i < Slots.Length; i++)
            {
                slotDetails[i] = string.Empty;
                slotInfos[i] = SaveSlotSummary.TryRead(Slots[i], out SaveSlotSummary.Info info) ? UpgradeLegacySummary(info) : null;
                try
                {
                    string path = Path.Combine(directory, Slots[i] + ".json");
                    if (!File.Exists(path)) continue;
                    DateTime saved = File.GetLastWriteTime(path);
                    slotDetails[i] = saved.ToString("d MMM yyyy", CultureInfo.InvariantCulture) + ", " +
                        saved.ToString("h:mm", CultureInfo.InvariantCulture) + (saved.Hour < 12 ? " am" : " pm");

                    string thumbnailPath = SaveSlotSummary.ThumbnailPath(Slots[i]);
                    if (thumbnailPath == null || !File.Exists(thumbnailPath)) continue;
                    Texture2D thumbnail = new Texture2D(2, 2, TextureFormat.RGB24, false) { name = "StartupMenu_Thumb", hideFlags = HideFlags.HideAndDontSave };
                    if (thumbnail.LoadImage(File.ReadAllBytes(thumbnailPath))) slotThumbnails[i] = thumbnail;
                    else Destroy(thumbnail);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"Could not read the save details for '{Slots[i]}': {exception.Message}");
                }
            }
        }

        /// <summary>Splits the combined "title · n of m quests done" text written by earlier summaries into its parts.</summary>
        private static SaveSlotSummary.Info UpgradeLegacySummary(SaveSlotSummary.Info info)
        {
            if (info.questsTotal > 0 || string.IsNullOrEmpty(info.quest)) return info;
            Match match = Regex.Match(info.quest, @"^(.*?) · (\d+) of (\d+) quests done$");
            if (!match.Success) return info;
            info.quest = match.Groups[1].Value;
            info.questsDone = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            info.questsTotal = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
            return info;
        }

        private void ClearThumbnails()
        {
            for (int i = 0; i < slotThumbnails.Length; i++)
            {
                if (slotThumbnails[i] != null) Destroy(slotThumbnails[i]);
                slotThumbnails[i] = null;
            }
        }

        private void LoadButtonTextures()
        {
            foreach (string key in OptionKeys)
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

        // ---- Drawing helpers. Every draw is multiplied by the current fade so whole layers can ease in and out. ----

        private void SetColor(Color tint, float alpha) => GUI.color = new Color(tint.r, tint.g, tint.b, tint.a * alpha * fade);

        private void Draw(Rect rect, Texture texture, Color tint, float alpha)
        {
            if (texture == null) return;
            SetColor(tint, alpha);
            GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, true);
        }

        /// <summary>Draws an anti-aliased line as a rotated strip; used for runes and weave threads.</summary>
        private void DrawLine(Vector2 from, Vector2 to, float thickness, Color colour, float alpha)
        {
            Vector2 delta = to - from;
            float length = delta.magnitude;
            if (length < 0.01f) return;
            Matrix4x4 matrix = GUI.matrix;
            RotateLocal(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, from);
            Draw(new Rect(from.x, from.y - thickness * 0.5f, length + 0.6f, thickness), lineTexture, colour, alpha);
            GUI.matrix = matrix;
        }

        // Rotates in the current GUI space, so it composes correctly with any transform already applied.
        private static void RotateLocal(float degrees, Vector2 pivot)
        {
            GUI.matrix = GUI.matrix * Matrix4x4.TRS(pivot, Quaternion.Euler(0f, 0f, degrees), Vector3.one) *
                Matrix4x4.TRS(-pivot, Quaternion.identity, Vector3.one);
        }

        private void Box(Rect rect, GUIStyle style, float alpha) => Box(rect, style, alpha, Color.white);

        private void Box(Rect rect, GUIStyle style, float alpha, Color tint)
        {
            SetColor(tint, alpha);
            GUI.Box(rect, GUIContent.none, style);
        }

        /// <summary>Single-line label that steps the font down to a minimum size, then trims with an ellipsis.</summary>
        private void FitLabel(Rect rect, string text, GUIStyle style, Color colour, float alpha = 1f, int minimumFontSize = 12)
        {
            int original = style.fontSize;
            GUIContent content = new GUIContent(text);
            while (style.fontSize > minimumFontSize && style.CalcSize(content).x > rect.width) style.fontSize--;
            if (style.CalcSize(content).x > rect.width)
            {
                string trimmed = text;
                while (trimmed.Length > 1 && style.CalcSize(new GUIContent(trimmed + "…")).x > rect.width) trimmed = trimmed.Substring(0, trimmed.Length - 1);
                content.text = trimmed.TrimEnd(' ', '·') + "…";
            }
            Label(rect, content.text, style, colour, alpha);
            style.fontSize = original;
        }

        private void Label(Rect rect, string text, GUIStyle style, Color colour, float alpha = 1f)
        {
            SetColor(Color.white, alpha);
            style.normal.textColor = colour;
            GUI.Label(rect, text, style);
        }

        private static Rect Expand(Rect rect, float x, float y) => new Rect(rect.x - x, rect.y - y, rect.width + x * 2f, rect.height + y * 2f);

        private static float Smooth(float t) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));

        // ---- Generated art ----

        private void CreateArt()
        {
            menuFont = Resources.Load<Font>("Dialogue/Nunito-Regular");
            backdropTexture = Track(MenuArt.Backdrop(32));
            glowTexture = Track(MenuArt.Glow(64));
            sparkleTexture = Track(GameSaveOverlay.CreateSparkleTexture(64));
            knobTexture = Track(MenuArt.Rounded(32, 32, 16f, Color.white, new Color(0.62f, 0.5f, 0.32f), 2f));
            switchTexture = Track(MenuArt.Rounded(60, 30, 15f, Color.white, Color.clear, 0f));
            lineTexture = Track(MenuArt.Line());
            ringTexture = Track(MenuArt.Ring(OuterRingTexture, 1.8f, 48, 4, OuterRingTick));
            innerRingTexture = Track(MenuArt.Ring(InnerRingTexture, 1.2f, 36, 6, InnerRingTick));
            discTexture = Track(MenuArt.Rounded(40, 40, 20f, new Color(0.17f, 0.13f, 0.27f, 1f), MenuArt.Gold, 2f));
            wandCursor = Track(MenuArt.WandCursor());
            Cursor.SetCursor(wandCursor, new Vector2(5f, 5f), CursorMode.Auto);
            sparks = new MenuSparks(MoteCount);
        }

        private void EnsureStyles()
        {
            if (letterStyle != null) return;

            Color goldTrim = MenuArt.WithAlpha(MenuArt.Gold, 0.32f);
            panelStyle = SlicedStyle(MenuArt.Rounded(64, 64, 20f, MenuArt.PanelFill, MenuArt.Gold, 2.5f), 24);
            panelTrimStyle = SlicedStyle(MenuArt.Rounded(64, 64, 15f, Color.clear, MenuArt.WithAlpha(MenuArt.Gold, 0.3f), 1f), 20);
            cardStyle = SlicedStyle(MenuArt.Rounded(32, 32, 12f, new Color(0.18f, 0.14f, 0.27f, 0.85f), goldTrim, 1.5f), 14);
            thumbFrameStyle = SlicedStyle(MenuArt.Rounded(24, 24, 6f, MenuArt.WithAlpha(MenuArt.Ink, 0.15f), MenuArt.WithAlpha(MenuArt.Gold, 0.6f), 1.5f), 8);
            pillStyle = SlicedStyle(MenuArt.Rounded(20, 10, 5f, Color.white, Color.clear, 0f), 5);

            letterStyle = TextStyle(MainTitleFontSize, TextAnchor.MiddleLeft);
            letterStyle.fontStyle = FontStyle.Bold;
            letterStyle.clipping = TextClipping.Overflow;
            letterStyle.wordWrap = false;
            promptStyle = TextStyle(20, TextAnchor.MiddleCenter);
            promptStyle.fontStyle = FontStyle.Italic;
            rowStyle = TextStyle(19, TextAnchor.MiddleLeft);
            valueStyle = TextStyle(19, TextAnchor.MiddleRight);
            valueStyle.fontStyle = FontStyle.Bold;
            mutedStyle = TextStyle(15, TextAnchor.MiddleLeft);
            groupStyle = TextStyle(15, TextAnchor.MiddleLeft);
            groupStyle.fontStyle = FontStyle.Bold;
            mutedStyle.clipping = TextClipping.Clip;
            smallStyle = TextStyle(13, TextAnchor.MiddleLeft);
            smallStyle.clipping = TextClipping.Clip;
            smallCentreStyle = TextStyle(13, TextAnchor.MiddleCenter);
            smallCentreStyle.clipping = TextClipping.Clip;
            loadDetailStyle = TextStyle(17, TextAnchor.MiddleLeft);
            loadDetailStyle.clipping = TextClipping.Clip;
            loadMetaStyle = TextStyle(15, TextAnchor.MiddleCenter);
            loadMetaStyle.clipping = TextClipping.Clip;
            messageStyle = TextStyle(17, TextAnchor.MiddleCenter);
            messageStyle.wordWrap = true;
            messageStyle.padding = new RectOffset(16, 16, 8, 8);

            textButtonStyle = new GUIStyle(GUI.skin.button)
            {
                font = menuFont,
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(12, 12, 12, 12),
                padding = new RectOffset(12, 12, 6, 6),
                normal = { background = Track(MenuArt.Rounded(32, 32, 10f, new Color(0.2f, 0.16f, 0.3f, 0.95f), MenuArt.WithAlpha(MenuArt.Gold, 0.55f), 1.5f)), textColor = MenuArt.Cream },
                hover = { background = Track(MenuArt.Rounded(32, 32, 10f, new Color(0.28f, 0.21f, 0.4f, 0.98f), MenuArt.Gold, 2f)), textColor = MenuArt.Gold },
                active = { background = Track(MenuArt.Rounded(32, 32, 10f, new Color(0.13f, 0.1f, 0.2f, 1f), MenuArt.Gold, 2f)), textColor = MenuArt.Gold }
            };
            textButtonStyle.focused.background = textButtonStyle.normal.background;
            textButtonStyle.focused.textColor = MenuArt.Cream;
            keyStyle = new GUIStyle(textButtonStyle) { fontSize = 16 };

            sliderStyle = new GUIStyle();
            thumbStyle = new GUIStyle { fixedWidth = 24f };
            scrollbarStyle = new GUIStyle(GUI.skin.verticalScrollbar)
            {
                fixedWidth = 10f,
                border = new RectOffset(5, 5, 5, 5),
                margin = new RectOffset(6, 2, 2, 2),
                normal = { background = Track(MenuArt.Rounded(10, 20, 5f, MenuArt.WithAlpha(MenuArt.Ink, 0.7f), Color.clear, 0f)) }
            };
            scrollThumbStyle = new GUIStyle(GUI.skin.verticalScrollbarThumb)
            {
                fixedWidth = 10f,
                border = new RectOffset(5, 5, 5, 5),
                normal = { background = Track(MenuArt.Rounded(10, 20, 5f, MenuArt.WithAlpha(MenuArt.Gold, 0.75f), Color.clear, 0f)) }
            };
        }

        private GUIStyle TextStyle(int size, TextAnchor alignment)
        {
            // Single-line by default; the skin's label wraps, which pushes short card lines onto a clipped second row.
            GUIStyle style = new GUIStyle(GUI.skin.label) { fontSize = size, alignment = alignment, wordWrap = false, normal = { textColor = MenuArt.Cream } };
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
    }
}
