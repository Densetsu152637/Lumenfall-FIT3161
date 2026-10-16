using UnityEngine;

namespace Lumenfall.GameEvents
{
    /// <summary>Tracks the active save slot and shows a small bottom-right autosave indicator.</summary>
    public sealed class GameSaveOverlay : MonoBehaviour
    {
        // Saves finish within a frame, so the saving phase is held briefly to be readable before it turns to saved.
        private const float MinimumSavingSeconds = 0.8f;
        private const float SavedSeconds = 1.6f;
        private const float FadeSeconds = 0.5f;
        private const float SavingTimeoutSeconds = 10f;
        private static readonly Color SavingColorA = new Color(0.68f, 0.52f, 1f);
        private static readonly Color SavingColorB = new Color(0.45f, 0.9f, 1f);
        private static readonly Color SavedColor = new Color(1f, 0.86f, 0.45f);
        private static readonly Color FailedColor = new Color(1f, 0.42f, 0.38f);

        private enum Phase { Hidden, Saving, Saved, Failed }

        private static GameSaveOverlay instance;
        public static string CurrentSlot { get; private set; } = "save-1";
        private Phase phase;
        private Phase pendingResult;
        private float phaseStarted;
        private Texture2D sparkle;
        private GUIStyle labelStyle;

        public static void SetCurrentSlot(string slot)
        {
            if (!string.IsNullOrWhiteSpace(slot)) CurrentSlot = slot;
        }

        /// <summary>Called by autosave just before it writes, so the indicator can show the save in progress.</summary>
        public static void NotifySaveStarted()
        {
            if (instance != null) instance.BeginPhase(Phase.Saving);
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
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
            GameSaveManager.Instance.SaveCompleted += OnSaved;
            GameSaveManager.Instance.SaveFailed += OnFailed;
        }

        private void OnDestroy()
        {
            if (sparkle != null) Destroy(sparkle);
            if (instance != this) return;
            instance = null;
            GameSaveManager manager = FindAnyObjectByType<GameSaveManager>();
            if (manager == null) return;
            manager.SaveCompleted -= OnSaved;
            manager.SaveFailed -= OnFailed;
        }

        private void OnSaved(string slot) { Finish(Phase.Saved); }
        private void OnFailed(string message) { Finish(Phase.Failed); }

        private void Finish(Phase result)
        {
            if (phase == Phase.Saving && Elapsed < MinimumSavingSeconds) pendingResult = result;
            else BeginPhase(result);
        }

        private void BeginPhase(Phase next)
        {
            phase = next;
            pendingResult = Phase.Hidden;
            phaseStarted = Time.unscaledTime;
        }

        private float Elapsed => Time.unscaledTime - phaseStarted;

        private void Update()
        {
            float elapsed = Elapsed;
            if (phase == Phase.Saving && pendingResult != Phase.Hidden && elapsed >= MinimumSavingSeconds) BeginPhase(pendingResult);
            else if (phase == Phase.Saving && elapsed >= SavingTimeoutSeconds) phase = Phase.Hidden;
            else if ((phase == Phase.Saved || phase == Phase.Failed) && elapsed >= SavedSeconds + FadeSeconds) phase = Phase.Hidden;
        }

        private void OnGUI()
        {
            using (UiScale.Begin()) DrawGui();
        }

        private void DrawGui()
        {
            if (phase == Phase.Hidden) return;
            GameEventManager manager = FindAnyObjectByType<GameEventManager>();
            if (manager != null && manager.Runtime != null && manager.Runtime.ActiveDialogue != null) return;
            if (sparkle == null) sparkle = CreateSparkleTexture(64);
            if (labelStyle == null)
                labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } };

            float t = Elapsed;
            float alpha = phase == Phase.Saving ? Mathf.Clamp01(t / 0.15f) : 1f - Mathf.Clamp01((t - SavedSeconds) / FadeSeconds);
            Rect area = new Rect(UiScale.Width - 168f, UiScale.Height - 54f, 152f, 38f);
            Rect icon = new Rect(area.x + 6f, area.y + 4f, 30f, 30f);
            Color previousColor = GUI.color;
            Matrix4x4 previousMatrix = GUI.matrix;

            GUI.color = new Color(0.05f, 0.06f, 0.12f, 0.55f * alpha);
            GUI.DrawTexture(area, Texture2D.whiteTexture);

            string label;
            if (phase == Phase.Saving)
            {
                // A spinning, colour-shifting star with three motes orbiting it while the save is in progress.
                Color tint = Color.Lerp(SavingColorA, SavingColorB, 0.5f + 0.5f * Mathf.Sin(t * 4f));
                UiScale.RotateAroundPivot(t * 180f, icon.center);
                DrawSparkle(Scale(icon, 1f + 0.12f * Mathf.Sin(t * 6f)), tint, alpha);
                GUI.matrix = previousMatrix;
                for (int i = 0; i < 3; i++)
                {
                    float angle = t * 3f + i * Mathf.PI * 2f / 3f;
                    Vector2 mote = icon.center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 13f;
                    DrawSparkle(new Rect(mote.x - 5f, mote.y - 5f, 10f, 10f), SavingColorB, alpha * 0.85f);
                }
                label = "Saving" + new string('.', 1 + (int)(t * 3f) % 3);
            }
            else
            {
                // Settles into a steady glow with a small pop as the save lands.
                Color tint = phase == Phase.Saved ? SavedColor : FailedColor;
                DrawSparkle(Scale(icon, 1f + 0.35f * Mathf.Exp(-t * 8f)), tint, alpha);
                label = phase == Phase.Saved ? "Saved" : "Save failed";
            }

            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.Label(new Rect(icon.xMax + 8f, area.y, area.xMax - icon.xMax - 10f, area.height), label, labelStyle);
            GUI.color = previousColor;
            GUI.matrix = previousMatrix;
        }

        private void DrawSparkle(Rect rect, Color tint, float alpha)
        {
            GUI.color = new Color(tint.r, tint.g, tint.b, alpha);
            GUI.DrawTexture(rect, sparkle, ScaleMode.ScaleToFit, true);
        }

        private static Rect Scale(Rect rect, float scale)
        {
            Vector2 size = rect.size * scale;
            return new Rect(rect.center - size * 0.5f, size);
        }

        /// <summary>Four-pointed star with a soft halo, generated so the indicator needs no imported art.</summary>
        public static Texture2D CreateSparkleTexture(int size)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "AutosaveSparkle", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave
            };
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f;
                    float v = (y + 0.5f) / size * 2f - 1f;
                    float r2 = u * u + v * v;
                    float arms = Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Abs(u)), 2f) * Mathf.Exp(-v * v / 0.006f)
                               + Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Abs(v)), 2f) * Mathf.Exp(-u * u / 0.006f);
                    float core = Mathf.Exp(-r2 / 0.02f);
                    float halo = Mathf.Exp(-r2 / 0.16f) * 0.35f;
                    byte a = (byte)(Mathf.Clamp01(arms + core + halo) * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, a);
                }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
