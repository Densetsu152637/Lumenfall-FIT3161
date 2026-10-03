using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.DataDriven;
using Lumenfall.Accessibility;

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
        private const string MasterVolumeKey = "lumenfall.master-volume";

        private enum Page { Main, Load, Settings }

        private Page page;
        private bool ready;
        private bool seededBlankSave;
        private bool confirmOverwrite;
        private string backdropSlot;
        private string message;
        private readonly AccessibilitySettingsView settingsView = new AccessibilitySettingsView();
        private readonly AccessibilityMenuInput menuInput = new AccessibilityMenuInput();
        private int menuFocus;
        private int drawnButtons;
        private Vector2 pageScroll;
        private bool scrollToMenuFocus;
        private readonly List<GridPlayerInput> disabledInputs = new List<GridPlayerInput>();
        private readonly List<DialogueOverlay> disabledDialogueOverlays = new List<DialogueOverlay>();
        private readonly List<GameSaveOverlay> disabledSaveOverlays = new List<GameSaveOverlay>();
        private readonly Dictionary<string, Texture2D> buttonTextures = new Dictionary<string, Texture2D>();
        private float previousTimeScale = 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindAnyObjectByType<StartupMenu>() == null)
            {
                GameObject menuObject = new GameObject("[StartupMenu]");
                menuObject.AddComponent<StartupMenu>();
            }
        }

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            ApplyMasterVolume(ReadMasterVolume());
            settingsView.AudioAdjustRequested += AdjustMasterVolume;
            settingsView.AudioResetRequested += ResetMasterVolume;
            SetGameplayInput(false);
            LoadButtonTextures();
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
            ready = true;
        }

        private void Update()
        {
            if (!ready) return;
            SetGameplayInput(false);
            menuInput.Poll();
            if (page == Page.Settings)
            {
                if (menuInput.Cancel) { page = Page.Main; menuFocus = 2; return; }
                settingsView.HandleNavigation(menuInput.Direction.y, menuInput.Direction.x, menuInput.Submit);
                return;
            }
            const int count = 4;
            if (menuInput.Direction.y != 0) { menuFocus = (menuFocus + menuInput.Direction.y + count) % count; scrollToMenuFocus = true; }
            if (menuInput.Cancel && page != Page.Main) { page = Page.Main; menuFocus = 1; return; }
            if (!menuInput.Submit) return;
            if (page == Page.Main)
            {
                if (menuFocus == 0) StartNewGame();
                else if (menuFocus == 1) { page = Page.Load; menuFocus = 0; pageScroll = Vector2.zero; }
                else if (menuFocus == 2) { page = Page.Settings; settingsView.ResetFocus(); }
                else Application.Quit();
            }
            else if (menuFocus == 3) { page = Page.Main; menuFocus = 1; pageScroll = Vector2.zero; }
            else if (GameSaveManager.Instance.HasSave(Slots[menuFocus])) LoadSlot(Slots[menuFocus]);
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
        }

        private void OnGUI()
        {
            if (!ready) return;
            AccessibilityMenuInput.ConsumeGuiNavigation();
            drawnButtons = 0;

            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.68f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = previous;

            float panelWidth = Mathf.Min(560f, Screen.width - 48f);
            float panelHeight = Mathf.Min(Screen.height - 24f, page == Page.Main ? 540f : 760f);
            Rect panel = new Rect((Screen.width - panelWidth) * 0.5f, (Screen.height - panelHeight) * 0.5f,
                panelWidth, panelHeight);
            if (page == Page.Settings) { DrawSettings(panel); return; }
            GUI.Box(panel, GUIContent.none, GUI.skin.window);
            GUI.Label(new Rect(panel.x + 24f, panel.y + 22f, panel.width - 48f, 56f),
                page == Page.Main ? "LUMENFALL" : Localization.Resolve("startup.load_title", "LOAD GAME"),
                HeaderStyle());

            if (page == Page.Main) DrawMain(panel);
            else if (page == Page.Load) DrawLoad(panel);
            else DrawSettings(panel);
        }

        private void DrawMain(Rect panel)
        {
            Rect viewport = new Rect(panel.x + 24f, panel.y + 90f, panel.width - 48f, Mathf.Max(50f, panel.height - 110f));
            float width = viewport.width - 24f;
            UpdateMenuScroll(viewport.height, 88f);
            pageScroll = GUI.BeginScrollView(viewport, pageScroll, new Rect(0f, 0f, width, 430f));
            if (Button("start", new Rect(10f, 0f, width - 20f, 72f), "START")) StartNewGame();
            if (Button("load", new Rect(10f, 88f, width - 20f, 72f), "LOAD")) { page = Page.Load; menuFocus = 0; pageScroll = Vector2.zero; }
            if (Button("options", new Rect(10f, 176f, width - 20f, 72f), "SETTINGS")) { page = Page.Settings; settingsView.ResetFocus(); }
            if (Button("exit", new Rect(10f, 264f, width - 20f, 72f), "EXIT")) Application.Quit();
            if (!string.IsNullOrEmpty(message)) GUI.Label(new Rect(10f, 346f, width - 20f, 80f), message, BodyStyle());
            GUI.EndScrollView();
        }
        private void DrawLoad(Rect panel)
        {
            GameSaveManager saves = GameSaveManager.Instance;
            Rect viewport = new Rect(panel.x + 24f, panel.y + 90f, panel.width - 48f, Mathf.Max(50f, panel.height - 110f));
            float width = viewport.width - 24f;
            UpdateMenuScroll(viewport.height, 112f);
            pageScroll = GUI.BeginScrollView(viewport, pageScroll, new Rect(0f, 0f, width, 448f));
            for (int i = 0; i < Slots.Length; i++)
            {
                string slot = Slots[i];
                string label = saves.HasSave(slot) ? Localization.Resolve("startup.slot", "Slot") + " " + (i + 1) : Localization.Resolve("startup.empty_slot", "Empty save slot");
                GUI.Label(new Rect(10f, i * 112f, width - 20f, 36f), label, BodyStyle());
                bool previousEnabled = GUI.enabled;
                GUI.enabled = saves.HasSave(slot);
                if (Button("load", new Rect(10f, i * 112f + 38f, width - 20f, 64f), "LOAD")) LoadSlot(slot);
                GUI.enabled = previousEnabled;
            }
            if (Button("exit", new Rect(10f, 350f, width - 20f, 72f), "BACK")) { page = Page.Main; menuFocus = 1; pageScroll = Vector2.zero; message = string.Empty; }
            GUI.EndScrollView();
        }
        private void UpdateMenuScroll(float viewportHeight, float rowHeight)
        {
            if (!scrollToMenuFocus) return;
            float y = menuFocus * rowHeight;
            if (y < pageScroll.y) pageScroll.y = y;
            else if (y + rowHeight > pageScroll.y + viewportHeight) pageScroll.y = y + rowHeight - viewportHeight;
            scrollToMenuFocus = false;
        }
        private void DrawSettings(Rect panel)
        {
            if (settingsView.Draw(panel, DrawMasterVolume)) { page = Page.Main; menuFocus = 2; }
        }

        // Independent baseline audio adapter. Merge replaces these controls with AudioMixSettings.
        public static void DrawMasterVolume(GUIStyle labelStyle)
        {
            float volume = ReadMasterVolume();
            GUILayout.Label(Localization.Resolve("settings.master_volume", "Master volume") + ": " + Mathf.RoundToInt(volume * 100f) + "%", labelStyle);
            float next = GUILayout.HorizontalSlider(volume, 0f, 1f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Localization.Resolve("settings.decrease", "Decrease"))) next -= 0.1f;
            if (GUILayout.Button(Localization.Resolve("settings.increase", "Increase"))) next += 0.1f;
            GUILayout.EndHorizontal();
            if (!Mathf.Approximately(volume, next)) ApplyMasterVolume(next);
        }
        public static void AdjustMasterVolume(int direction) => ApplyMasterVolume(ReadMasterVolume() + direction * 0.1f);
        public static void ResetMasterVolume() => ApplyMasterVolume(1f);
        private static float ReadMasterVolume()
        {
            float value = PlayerPrefs.GetFloat(MasterVolumeKey, 1f);
            return float.IsNaN(value) || float.IsInfinity(value) ? 1f : Mathf.Clamp01(value);
        }
        private static void ApplyMasterVolume(float value)
        {
            value = float.IsNaN(value) || float.IsInfinity(value) ? 1f : Mathf.Clamp01(value);
            AudioListener.volume = value;
            PlayerPrefs.SetFloat(MasterVolumeKey, value);
            PlayerPrefs.Save();
        }
        private bool Button(string textureKey, Rect rect, string fallbackLabel)
        {
            int index = drawnButtons++;
            Color previousBackground = GUI.backgroundColor;
            if (index == menuFocus) GUI.backgroundColor = Color.yellow;
            bool clicked = GUI.Button(rect, GUIContent.none, GUI.skin.button);
            GUI.backgroundColor = previousBackground;
            if (clicked) menuFocus = index;
            if (!AccessibilitySettings.Current.HighContrast && AccessibilitySettings.Current.TextScale == 1f && Localization.Resolver == null && buttonTextures.TryGetValue(textureKey, out Texture2D texture) && texture != null)
            {
                float aspect = texture.width / (texture.height * 0.25f);
                float drawWidth = Mathf.Min(rect.width, rect.height * aspect);
                Rect drawRect = new Rect(rect.center.x - drawWidth * 0.5f, rect.y, drawWidth, rect.height);
                GUI.DrawTextureWithTexCoords(drawRect, texture, new Rect(0f, 0.75f, 1f, 0.25f), true);
            }
            else
            {
                GUI.Label(rect, Localization.Resolve("startup." + fallbackLabel.ToLowerInvariant(), fallbackLabel), ButtonStyle());
            }
            return clicked;
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

        private static GUIStyle HeaderStyle() => new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(28f * AccessibilitySettings.Current.TextScale), normal = { textColor = Color.white } };
        private static GUIStyle BodyStyle() => new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true, fontSize = Mathf.RoundToInt(18f * AccessibilitySettings.Current.TextScale), normal = { textColor = Color.white } };
        private static GUIStyle ButtonStyle() => new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(20f * AccessibilitySettings.Current.TextScale) };
    }
}
