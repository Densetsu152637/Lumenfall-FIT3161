using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Lumenfall.Controls;
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

        private enum Page { Main, Load, Settings, Controls }

        private Page page;
        private bool ready;
        private bool seededBlankSave;
        private bool confirmOverwrite;
        private string backdropSlot;
        private string message;
        private readonly GameSettingsPanel settings = new GameSettingsPanel();
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
            GameSettingsPanel.ApplySavedVolume();
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
            if (ready) SetGameplayInput(false);
            settings.Tick();
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
            float panelHeight = page == Page.Main ? 540f : page == Page.Controls ? Mathf.Min(720f, Screen.height - 40f) : 620f;
            Rect panel = new Rect((Screen.width - panelWidth) * 0.5f, (Screen.height - panelHeight) * 0.5f,
                panelWidth, panelHeight);
            GUI.Box(panel, GUIContent.none, GUI.skin.window);
            GUI.Label(new Rect(panel.x + 24f, panel.y + 22f, panel.width - 48f, 56f),
                page == Page.Main ? "LUMENFALL" : page == Page.Load ? "LOAD GAME" : page == Page.Controls ? "CONTROLS" : "SETTINGS",
                HeaderStyle());

            if (page == Page.Main) DrawMain(panel);
            else if (page == Page.Load) DrawLoad(panel);
            else if (page == Page.Controls) DrawControls(panel);
            else DrawSettings(panel);
        }

        private void DrawMain(Rect panel)
        {
            float x = panel.x + 90f;
            float width = panel.width - 180f;
            float y = panel.y + 112f;
            if (Button("start", new Rect(x, y, width, 72f), "START")) StartNewGame();
            if (Button("load", new Rect(x, y + 88f, width, 72f), "LOAD")) page = Page.Load;
            if (Button("options", new Rect(x, y + 176f, width, 72f), "SETTINGS")) page = Page.Settings;
            if (Button("exit", new Rect(x, y + 264f, width, 72f), "EXIT")) Application.Quit();
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
                if (saves.HasSave(slot) && Button("load", new Rect(panel.xMax - 170f, y, 120f, 66f), "LOAD"))
                {
                    LoadSlot(slot);
                }
                y += 88f;
            }

            if (Button("exit", new Rect(panel.x + 86f, panel.yMax - 108f, panel.width - 172f, 66f), "BACK"))
            {
                page = Page.Main;
                message = string.Empty;
            }
        }

        private void DrawSettings(Rect panel)
        {
            GUI.Label(new Rect(panel.x + 54f, panel.y + 130f, 220f, 40f), "MASTER VOLUME", BodyStyle());
            float masterVolume = GameSettingsPanel.MasterVolume;
            float newVolume = GUI.HorizontalSlider(new Rect(panel.x + 54f, panel.y + 192f, panel.width - 108f, 30f),
                masterVolume, 0f, 1f);
            if (!Mathf.Approximately(newVolume, masterVolume)) GameSettingsPanel.MasterVolume = newVolume;
            GUI.Label(new Rect(panel.x + 54f, panel.y + 224f, panel.width - 108f, 40f),
                $"{Mathf.RoundToInt(newVolume * 100f)}%", BodyStyle());
            if (GUI.Button(new Rect(panel.x + 86f, panel.y + 290f, panel.width - 172f, 56f), "CONTROLS", ButtonStyle()))
            {
                page = Page.Controls;
                settings.Message = "Click an action, then press the key you want.";
            }
            if (Button("exit", new Rect(panel.x + 86f, panel.yMax - 108f, panel.width - 172f, 66f), "BACK")) page = Page.Main;
        }

        private void DrawControls(Rect panel)
        {
            Rect area = new Rect(panel.x + 30f, panel.y + 92f, panel.width - 60f, panel.height - 92f - 124f);
            GUILayout.BeginArea(area);
            settings.DrawControls(RowStyle(), KeyStyle(), BodyStyle(), area.height - 96f);
            GUILayout.EndArea();
            if (Button("exit", new Rect(panel.x + 86f, panel.yMax - 108f, panel.width - 172f, 66f), "BACK"))
            {
                settings.CancelRebind();
                settings.Message = string.Empty;
                page = Page.Settings;
            }
        }

        private bool Button(string textureKey, Rect rect, string fallbackLabel)
        {
            bool clicked = GUI.Button(rect, GUIContent.none, GUI.skin.button);
            if (buttonTextures.TryGetValue(textureKey, out Texture2D texture) && texture != null)
            {
                float aspect = texture.width / (texture.height * 0.25f);
                float drawWidth = Mathf.Min(rect.width, rect.height * aspect);
                Rect drawRect = new Rect(rect.center.x - drawWidth * 0.5f, rect.y, drawWidth, rect.height);
                GUI.DrawTextureWithTexCoords(drawRect, texture, new Rect(0f, 0.75f, 1f, 0.25f), true);
            }
            else
            {
                GUI.Label(rect, fallbackLabel, ButtonStyle());
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

        private static GUIStyle HeaderStyle() => new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 28, normal = { textColor = Color.white } };
        private static GUIStyle BodyStyle() => new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 18, normal = { textColor = Color.white } };
        private static GUIStyle RowStyle() => new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontSize = 17, normal = { textColor = Color.white } };
        private static GUIStyle KeyStyle() => new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleCenter, fontSize = 16 };
        private static GUIStyle ButtonStyle() => new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleCenter, fontSize = 20 };
    }
}
