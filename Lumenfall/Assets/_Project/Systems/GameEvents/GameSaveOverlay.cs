using UnityEngine;

namespace Lumenfall.GameEvents
{
    /// <summary>Small IMGUI bridge for the sample game; production menus can call GameSaveManager directly.</summary>
    public sealed class GameSaveOverlay : MonoBehaviour
    {
        public static string CurrentSlot { get; private set; } = "save-1";
        private string status = "";
        private GUIStyle statusStyle;
        private string recoverySlot;
        private SaveRecoverySource[] recoveryCandidates = System.Array.Empty<SaveRecoverySource>();

        public static void SetCurrentSlot(string slot)
        {
            if (!string.IsNullOrWhiteSpace(slot)) CurrentSlot = slot;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (FindAnyObjectByType<GameSaveOverlay>() == null)
            {
                GameObject go = new GameObject("[GameSaveOverlay]");
                go.AddComponent<GameSaveOverlay>();
            }
        }

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            GameSaveManager.Instance.SaveCompleted += OnSaved;
            GameSaveManager.Instance.LoadCompleted += OnLoaded;
            GameSaveManager.Instance.SaveFailed += OnFailed;
        }

        private void OnDestroy()
        {
            GameSaveManager manager = FindAnyObjectByType<GameSaveManager>();
            if (manager == null) return;
            manager.SaveCompleted -= OnSaved;
            manager.LoadCompleted -= OnLoaded;
            manager.SaveFailed -= OnFailed;
        }

        private void OnGUI()
        {
            GameEventManager manager = FindAnyObjectByType<GameEventManager>();
            if (manager != null && manager.Runtime != null && manager.Runtime.ActiveDialogue != null)
            {
                return;
            }

            if (statusStyle == null)
            {
                statusStyle = new GUIStyle(GUI.skin.label) { normal = { textColor = Color.white } };
            }

            GUILayout.BeginArea(new Rect(Screen.width - 190f, 12f, 178f, 160f), GUI.skin.box);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save")) GameSaveManager.Instance.Save(CurrentSlot);
            if (GUILayout.Button("Load")) GameSaveManager.Instance.Load(CurrentSlot);
            GUILayout.EndHorizontal();
            GUILayout.Label(status, statusStyle);
            if (recoverySlot == CurrentSlot)
                foreach (SaveRecoverySource candidate in recoveryCandidates)
                    if (GUILayout.Button(candidate == SaveRecoverySource.Previous ? "Recover previous save" : "Recover interrupted save"))
                        GameSaveManager.Instance.RecoverSave(CurrentSlot, candidate);
            GUILayout.EndArea();
        }

        private void OnSaved(string slot) { status = $"Saved {slot}"; recoveryCandidates = System.Array.Empty<SaveRecoverySource>(); }
        private void OnLoaded(string slot) { status = $"Loaded {slot}"; recoveryCandidates = System.Array.Empty<SaveRecoverySource>(); }
        private void OnFailed(string message)
        {
            status = message;
            recoverySlot = CurrentSlot;
            recoveryCandidates = GameSaveManager.Instance.GetRecoveryCandidates(CurrentSlot);
        }
    }
}
