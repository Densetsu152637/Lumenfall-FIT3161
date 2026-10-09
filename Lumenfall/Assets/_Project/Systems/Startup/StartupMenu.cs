using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.DataDriven;

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
        private static readonly string[] ToggleLabels = { "OFF", "ON" };
        private static readonly string[] AntialiasingLabels = { "OFF", "FXAA", "SMAA" };
        private static readonly string[] ShadingLabels = { "LOW", "MEDIUM", "HIGH" };

        private enum Page { Main, Load, Settings }

        private Page page;
        private int focused;
        private int drawingIndex;
        private readonly UiNavigationInput navigation = new UiNavigationInput();
        private GameSaveManager subscribedSaves;
        private GUIStyle headerStyle, bodyStyle, buttonStyle;
        private bool ready;
        private bool seededBlankSave;
        private bool confirmOverwrite;
        private string backdropSlot;
        private string message;
        private float masterVolume;
        private Vector2 settingsScroll;
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

        private void OnEnable() { OverlayInputGate.Acquire(this); }
        private void OnDisable()
        {
            OverlayInputGate.Release(this);
            if (subscribedSaves != null) subscribedSaves.SaveFailed -= OnSaveFailed;
            subscribedSaves = null;
        }
        private void OnSaveFailed(string error) { message = error; }
        private void ChangePage(Page value)
        {
            page = value; focused = 0; confirmOverwrite = false; message = "";
            if (value == Page.Load) focused = UiFocusIndex.Move(0, 0, 4, index => index == 3 || GameSaveManager.Instance.HasSave(Slots[index]));
        }
        private void CancelOverwrite() { confirmOverwrite = false; focused = 0; message = ""; }

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            masterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MasterVolumeKey, 1f));
            AudioListener.volume = masterVolume;
            SetGameplayInput(false);
            LoadButtonTextures();
            StartCoroutine(InitializeAfterSceneSystems());
        }

        private IEnumerator InitializeAfterSceneSystems()
        {
            yield return null;
            GameSaveManager saves = GameSaveManager.Instance;
            subscribedSaves = saves;
            saves.SaveFailed += OnSaveFailed;
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
                    if (string.IsNullOrEmpty(message)) message = "Unable to create a starting save.";
                }
            }

            // The latest save is the backdrop state. Providers registered by the scene are ready now.
            if (!string.IsNullOrWhiteSpace(backdropSlot) && !saves.Load(backdropSlot, false))
            {
                if (string.IsNullOrEmpty(message)) message = "The latest save could not be loaded.";
            }
            if (!string.IsNullOrWhiteSpace(backdropSlot) && string.IsNullOrEmpty(message)) GameSaveOverlay.SetCurrentSlot(backdropSlot);
            ready = true;
        }

        private void Update()
        {
            if (!ready) return;
            SetGameplayInput(false);
            navigation.Read();
            int count = confirmOverwrite ? 2 : page == Page.Main ? 4 : page == Page.Load ? 4 : 2;
            if (navigation.Cancel)
            {
                if (confirmOverwrite) CancelOverwrite();
                else if (page != Page.Main) ChangePage(Page.Main);
                return;
            }
            if (navigation.Vertical != 0)
                focused = UiFocusIndex.Move(focused, navigation.Vertical, count,
                    index => page != Page.Load || index == 3 || GameSaveManager.Instance.HasSave(Slots[index]));
            if (page == Page.Settings && focused == 0 && navigation.Horizontal != 0) SetMasterVolume(masterVolume + navigation.Horizontal * 0.05f);
            if (!navigation.Submit) return;
            if (confirmOverwrite) { if (focused == 0) StartNewGame(); else CancelOverwrite(); }
            else if (page == Page.Main)
            {
                if (focused == 0) StartNewGame();
                else if (focused == 1) ChangePage(Page.Load);
                else if (focused == 2) ChangePage(Page.Settings);
                else Application.Quit();
            }
            else if (page == Page.Load) { if (focused == 3) ChangePage(Page.Main); else if (GameSaveManager.Instance.HasSave(Slots[focused])) LoadSlot(Slots[focused]); }
            else if (focused == 1) ChangePage(Page.Main);
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

            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.68f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = previous;

            float panelWidth = Mathf.Min(560f, Screen.width - 48f);
            float panelHeight = Mathf.Min(page == Page.Main ? 540f : 620f, Screen.height - 24f);
            Rect panel = new Rect((Screen.width - panelWidth) * 0.5f, (Screen.height - panelHeight) * 0.5f,
                panelWidth, panelHeight);
            GUI.Box(panel, GUIContent.none, GUI.skin.window);
            GUI.Label(new Rect(panel.x + 24f, panel.y + 22f, panel.width - 48f, 56f),
                page == Page.Main ? "LUMENFALL" : page == Page.Load ? "LOAD GAME" : "SETTINGS",
                HeaderStyle());

            if (page == Page.Main) DrawMain(panel);
            else if (page == Page.Load) DrawLoad(panel);
            else DrawSettings(panel);
            GUI.Label(new Rect(panel.x + 20f, panel.yMax - 28f, panel.width - 40f, 24f), "Up/Down: choose   Enter / A: confirm   Esc / B: back", BodyStyle());
            if (page != Page.Main && !string.IsNullOrEmpty(message)) GUI.Label(new Rect(panel.x + 24f, panel.yMax - 170f, panel.width - 48f, 54f), message, BodyStyle());
            GUI.matrix = previousMatrix;
        }

        private void DrawMain(Rect panel)
        {
            if (confirmOverwrite)
            {
                GUI.Label(new Rect(panel.x + 24f, panel.y + 100f, panel.width - 48f, 100f), "All slots are full. Replace SLOT 1 with a new game? This replaces its saved progress.", BodyStyle());
                if (Button("start", new Rect(panel.x + 60f, panel.y + 220f, panel.width - 120f, 60f), "REPLACE SLOT 1")) StartNewGame();
                if (Button("exit", new Rect(panel.x + 60f, panel.y + 300f, panel.width - 120f, 60f), "CANCEL")) CancelOverwrite();
                if (!string.IsNullOrEmpty(message)) GUI.Label(new Rect(panel.x + 24f, panel.yMax - 120f, panel.width - 48f, 80f), message, BodyStyle());
                return;
            }
            float x = panel.x + 60f;
            float width = panel.width - 120f;
            float y = panel.y + 112f;
            if (Button("start", new Rect(x, y, width, Mathf.Min(64f, (panel.height - 190f) / 4f)), "START")) StartNewGame();
            if (Button("load", new Rect(x, y + (panel.height - 180f) / 4f, width, Mathf.Min(64f, (panel.height - 190f) / 4f)), "LOAD")) ChangePage(Page.Load);
            if (Button("options", new Rect(x, y + (panel.height - 180f) / 2f, width, Mathf.Min(64f, (panel.height - 190f) / 4f)), "SETTINGS")) ChangePage(Page.Settings);
            if (Button("exit", new Rect(x, y + (panel.height - 180f) * 0.75f, width, Mathf.Min(64f, (panel.height - 190f) / 4f)), "EXIT")) Application.Quit();
            if (!string.IsNullOrEmpty(message)) GUI.Label(new Rect(panel.x + 30f, panel.yMax - 68f, panel.width - 60f, 42f), message, BodyStyle());
        }

        private void DrawLoad(Rect panel)
        {
            GameSaveManager saves = GameSaveManager.Instance;
            float y = panel.y + 102f;
            for (int i = 0; i < Slots.Length; i++)
            {
                string slot = Slots[i];
                string label = saves.HasSave(slot) ? $"SLOT {i + 1}" : "empty save file";
                GUI.Label(new Rect(panel.x + 52f, y + 12f, panel.width - 210f, 48f), label, BodyStyle());
                drawingIndex = i;
                if (Button("load", new Rect(panel.xMax - 170f, y, 120f, 66f), saves.HasSave(slot) ? "LOAD" : "EMPTY"))
                {
                    if (saves.HasSave(slot)) LoadSlot(slot); else message = "This slot is empty. Start a new game to create a save.";
                }
                y += Mathf.Min(88f, (panel.height - 240f) / 3f);
            }

            drawingIndex = 3;
            if (Button("exit", new Rect(panel.x + 86f, panel.yMax - 108f, panel.width - 172f, 66f), "BACK"))
            {
                ChangePage(Page.Main);
            }
        }

        private void DrawSettings(Rect panel)
        {
            Rect viewport = new Rect(panel.x + 24f, panel.y + 90f, panel.width - 48f, panel.height - 200f);
            float width = viewport.width - 24f;
            settingsScroll = GUI.BeginScrollView(viewport, settingsScroll, new Rect(0f, 0f, width, 480f));
            GUI.Label(new Rect(0f, 0f, width, 32f), "MASTER VOLUME", BodyStyle());
            float newVolume = GUI.HorizontalSlider(new Rect(20f, 40f, width - 40f, 24f),
                masterVolume, 0f, 1f);
            if (!Mathf.Approximately(newVolume, masterVolume))
            {
                SetMasterVolume(newVolume);
            }
            GUI.Label(new Rect(0f, 66f, width, 28f),
                $"{Mathf.RoundToInt(masterVolume * 100f)}%", BodyStyle());

            GraphicsOptions graphics = GraphicsOptions.Instance;
            if (graphics != null)
            {
                GraphicsPreferences current = graphics.Preferences;
                bool vSync = DrawGraphicsChoice(108f, width, "VSYNC", current.VSync ? 1 : 0, ToggleLabels) == 1;
                bool previousEnabled = GUI.enabled;
                GUI.enabled = previousEnabled && graphics.SupportsUrpOptions;
                GraphicsAntialiasing antialiasing = (GraphicsAntialiasing)DrawGraphicsChoice(176f, width,
                    "ANTIALIASING", (int)current.Antialiasing, AntialiasingLabels);
                GUI.enabled = previousEnabled && graphics.SupportsAmbientOcclusion;
                bool ao = DrawGraphicsChoice(244f, width, graphics.SupportsAmbientOcclusion
                    ? "AMBIENT OCCLUSION" : "AMBIENT OCCLUSION (UNAVAILABLE)",
                    graphics.SupportsAmbientOcclusion && current.AmbientOcclusion ? 1 : 0, ToggleLabels) == 1;
                GUI.enabled = previousEnabled && graphics.SupportsUrpOptions;
                ShadingQuality shading = (ShadingQuality)DrawGraphicsChoice(312f, width,
                    "SHADING QUALITY", (int)current.Shading, ShadingLabels);
                GUI.enabled = previousEnabled;
                if (vSync != current.VSync || antialiasing != current.Antialiasing ||
                    (graphics.SupportsAmbientOcclusion && ao != current.AmbientOcclusion) || shading != current.Shading)
                    graphics.SetPreferences(new GraphicsPreferences(vSync, antialiasing,
                        graphics.SupportsAmbientOcclusion ? ao : current.AmbientOcclusion, shading));
                GUI.Label(new Rect(0f, 384f, width, 44f),
                    "Recommended: " + ShadingLabels[(int)graphics.RecommendedPreferences.Shading]
                    + "\nChanges are saved automatically.", BodyStyle());
                if (GUI.Button(new Rect(20f, 438f, width - 40f, 34f), "USE RECOMMENDED"))
                    graphics.SetPreferences(graphics.RecommendedPreferences);
            }
            GUI.EndScrollView();
            if (Button("exit", new Rect(panel.x + 86f, panel.yMax - 108f, panel.width - 172f, 66f), "BACK")) page = Page.Main;
        }

        private static int DrawGraphicsChoice(float y, float width, string label, int selected, string[] choices)
        {
            GUI.Label(new Rect(0f, y, width, 28f), label, BodyStyle());
            return GUI.SelectionGrid(new Rect(20f, y + 30f, width - 40f, 30f), selected, choices, choices.Length);
        }

        private bool Button(string textureKey, Rect rect, string fallbackLabel)
        {
            int index = drawingIndex++;
            if (index == focused) GUI.Box(new Rect(rect.x - 4f, rect.y - 4f, rect.width + 8f, rect.height + 8f), GUIContent.none);
            bool clicked = GUI.Button(rect, GUIContent.none, GUI.skin.button);
            bool standardLabel = fallbackLabel == "START" || fallbackLabel == "LOAD" || fallbackLabel == "SETTINGS" || fallbackLabel == "EXIT";
            buttonTextures.TryGetValue(textureKey, out Texture2D texture);
            bool hasArtwork = standardLabel && texture != null;
            if (hasArtwork)
            {
                float aspect = texture.width / (texture.height * 0.25f);
                float drawWidth = Mathf.Min(rect.width, rect.height * aspect);
                Rect drawRect = new Rect(rect.center.x - drawWidth * 0.5f, rect.y, drawWidth, rect.height);
                GUI.DrawTextureWithTexCoords(drawRect, texture, new Rect(0f, 0.75f, 1f, 0.25f), true);
            }
            if (!hasArtwork) GUI.Label(rect, fallbackLabel, ButtonStyle());
            if (index == focused) GUI.Label(new Rect(rect.x - 28f, rect.y, 24f, rect.height), ">", ButtonStyle());
            if (clicked) focused = index;
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
                    focused = 1;
                    message = "All slots are full. Confirm replacement or cancel.";
                    return;
                }
                slot = Slots[0];
            }

            message = "";
            GameEventManager.Instance.ReloadDefinitions();
            DataDrivenWorldLoader worldLoader = DataDrivenWorldLoader.Instance;
            if (worldLoader != null) worldLoader.ResetToStartingState();
            if (!saves.Save(slot))
            {
                if (string.IsNullOrEmpty(message)) message = "Could not start a new game.";
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
            message = "";
            if (GameSaveManager.Instance.Load(slot))
            {
                backdropSlot = slot;
                GameSaveOverlay.SetCurrentSlot(slot);
                CloseMenu();
            }
            else
            {
                if (string.IsNullOrEmpty(message)) message = "That save could not be loaded.";
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

        private GUIStyle HeaderStyle() => headerStyle ?? (headerStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 28, normal = { textColor = Color.white } });
        private GUIStyle BodyStyle() => bodyStyle ?? (bodyStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 16, wordWrap = true, normal = { textColor = Color.white } });
        private GUIStyle ButtonStyle() => buttonStyle ?? (buttonStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 20, wordWrap = true, normal = { textColor = Color.white } });
    }
}
