using UnityEngine;

namespace Lumenfall.GameEvents
{
    /// Current-slot save presenter; input adapter resides in Runtime to avoid assembly cycles.
    public sealed class GameSaveOverlay : MonoBehaviour
    {
        public static string CurrentSlot { get; private set; } = "save-1";
        public bool IsOpen { get; private set; }
        private string status = "";
        private GUIStyle statusStyle;
        private GameSaveManager subscribedManager;
        private int focused;
        private bool confirmLoad;
        private int openedFrame;
        public static void SetCurrentSlot(string slot)
        {
            if (!string.IsNullOrWhiteSpace(slot)) CurrentSlot = slot;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (FindAnyObjectByType<GameSaveOverlay>() != null) return;
            new GameObject("[GameSaveOverlay]").AddComponent<GameSaveOverlay>();
        }
        private void Awake() { DontDestroyOnLoad(gameObject); }
        private void OnEnable()
        {
            subscribedManager = GameSaveManager.Instance;
            subscribedManager.SaveCompleted += OnSaved;
            subscribedManager.LoadCompleted += OnLoaded;
            subscribedManager.SaveFailed += OnFailed;
        }
        private void OnDisable()
        {
            Close();
            if (subscribedManager == null) return;
            subscribedManager.SaveCompleted -= OnSaved;
            subscribedManager.LoadCompleted -= OnLoaded;
            subscribedManager.SaveFailed -= OnFailed;
            subscribedManager = null;
        }
        public void TogglePanel()
        {
            if (IsOpen) { Close(); return; }
            if (OverlayInputGate.IsBlocked) return;
            IsOpen = true; focused = 0; confirmLoad = false; openedFrame = Time.frameCount;
            OverlayInputGate.Acquire(this);
        }
        public void SaveCurrent()
        {
            if (OverlayInputGate.IsBlockedExcept(this)) return;
            status = "";
            if (!GameSaveManager.Instance.Save(CurrentSlot) && string.IsNullOrEmpty(status)) status = "Save failed. Try again or select another slot from startup.";
        }
        public void HandleNavigation(int direction, bool submit, bool cancel)
        {
            if (!IsOpen || Time.frameCount <= openedFrame || OverlayInputGate.IsBlockedExcept(this)) return;
            if (cancel) { if (confirmLoad) { confirmLoad = false; focused = 1; } else Close(); return; }
            if (direction != 0) focused = UiFocusIndex.Move(focused, direction, confirmLoad ? 2 : 3);
            if (submit) Activate(focused);
        }
        private void Activate(int index)
        {
            if (confirmLoad)
            {
                confirmLoad = false;
                if (index == 0)
                {
                    status = "";
                    if (GameSaveManager.Instance.Load(CurrentSlot)) Close();
                    else if (string.IsNullOrEmpty(status)) status = "Load failed. The current game is still available.";
                }
                focused = 1;
                return;
            }
            if (index == 0) SaveCurrent();
            else if (index == 1)
            {
                if (!GameSaveManager.Instance.HasSave(CurrentSlot)) { status = "This slot has no save yet. Save your progress first."; return; }
                confirmLoad = true; focused = 1;
            }
            else Close();
        }
        private void Close() { IsOpen = false; confirmLoad = false; OverlayInputGate.Release(this); }
        private void OnGUI()
        {
            if (statusStyle == null) statusStyle = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 16, normal = { textColor = Color.white } };
            if (!IsOpen)
            {
                if (OverlayInputGate.IsBlocked) return;
                GUILayout.BeginArea(new Rect(Mathf.Max(12f, Screen.width - 260f), 12f, Mathf.Min(248f, Screen.width - 24f), 145f), GUI.skin.box);
                GUILayout.Label("Current slot: " + CurrentSlot, statusStyle);
                if (GUILayout.Button("Save / Load (F9 / Menu)")) TogglePanel();
                GUILayout.Label(string.IsNullOrEmpty(status) ? "F5: save   Tab / View: journal   E / A: interact" : status, statusStyle);
                GUILayout.EndArea();
                return;
            }
            float width = Mathf.Min(520f, Screen.width - 32f), height = Mathf.Min(340f, Screen.height - 32f);
            GUILayout.BeginArea(new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f, width, height), GUI.skin.window);
            GUILayout.Label("Progress — " + CurrentSlot, statusStyle);
            GUILayout.Label(confirmLoad ? "Reload this slot? Progress since your last save will be lost." : "Save and reload the slot selected at startup.", statusStyle);
            bool wasEnabled = GUI.enabled;
            GUI.enabled = Time.frameCount > openedFrame && !OverlayInputGate.IsBlockedExcept(this);
            string[] labels = confirmLoad ? new[] { "Reload saved progress", "Cancel" } : new[] { "Save current progress", "Load current slot", "Close" };
            for (int i = 0; i < labels.Length; i++)
                if (GUILayout.Button((i == focused ? "> " : "") + labels[i], GUILayout.MinHeight(40f))) { Activate(i); break; }
            GUI.enabled = wasEnabled;
            GUILayout.Label(status, statusStyle);
            GUILayout.Label("Up/Down: choose   Enter / A: confirm   Esc / B: cancel", statusStyle);
            GUILayout.EndArea();
        }
        private void OnSaved(string slot) { status = "Saved " + slot; }
        private void OnLoaded(string slot) { SetCurrentSlot(slot); status = "Loaded " + slot; }
        private void OnFailed(string message) { status = message; }
    }
}
