using Lumenfall.GameEvents;
using Lumenfall.Startup;
using UnityEngine;

namespace Lumenfall.Player
{
    /// <summary>
    /// Asks the player for their name in a small panel styled like the startup menu. Enter or Confirm accepts; a blank
    /// name becomes <see cref="PlayerProfile.DefaultName"/>, which is shown as the field's placeholder. It is opened by a
    /// cutscene's <c>name</c> step, which waits for it and blocks gameplay input meanwhile, so it should sit between two
    /// dialogue steps rather than inside an open dialogue.
    /// </summary>
    public sealed class PlayerNamePrompt : MonoBehaviour
    {
        private const string FieldName = "lumenfall.player-name";
        private const float FadeSeconds = 0.3f;
        private static readonly Vector2 PanelSize = new Vector2(560f, 300f);

        private static PlayerNamePrompt current;

        private readonly MenuSkin skin = new MenuSkin();
        private GUIStyle fieldStyle, placeholderStyle;
        private Texture2D fieldTexture, focusedFieldTexture;
        private string typed = string.Empty;
        private float openedAt;
        private bool focused;

        public static bool IsOpen => current != null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => current = null;

        /// <summary>Opens the prompt, starting from the player's current name if they have one. Returns false if it is already open.</summary>
        public static bool Open()
        {
            if (current != null) return false;
            GameObject host = new GameObject("[Player Name Prompt]");
            DontDestroyOnLoad(host);
            current = host.AddComponent<PlayerNamePrompt>();
            current.typed = PlayerProfile.HasName ? PlayerProfile.Name : string.Empty;
            current.openedAt = Time.unscaledTime;
            return true;
        }

        /// <summary>Closes the prompt, keeping what was typed (or the default name).</summary>
        public static void Close()
        {
            if (current != null) current.Confirm();
        }

        private void Update() => skin.Tick(Time.unscaledDeltaTime);

        private void OnDestroy()
        {
            skin.ShowWandCursor(false);
            skin.Dispose();
            if (fieldTexture != null) Destroy(fieldTexture);
            if (focusedFieldTexture != null) Destroy(focusedFieldTexture);
            if (current == this) current = null;
        }

        private void Confirm()
        {
            PlayerProfile.SetName(typed);
            Destroy(gameObject);
            current = null;
        }

        // Drawn with GUI only, never GUILayout, so skip the per-frame layout pass and its allocations.
        private void Awake() => useGUILayout = false;

        private void OnGUI()
        {
            using (UiScale.Begin()) DrawGui();
        }

        private void DrawGui()
        {
            skin.Ensure();
            EnsureStyles();
            // Just behind the cutscene overlay, in front of the world and the other overlays.
            GUI.depth = -998;
            float now = Time.unscaledTime;
            float open = MenuSkin.Smooth((now - openedAt) / FadeSeconds);
            Color previousColor = GUI.color;
            skin.Fade = open;
            skin.ShowWandCursor(true);
            skin.DrawBackdrop(0.75f * open, now);

            // Laid out in UI units; OnGUI's UiScale scope scales them to the window.
            Vector2 view = new Vector2(UiScale.Width, UiScale.Height);
            float rise = (1f - open) * 14f * skin.Motion;
            Rect panel = new Rect((view.x - PanelSize.x) / 2f, (view.y - PanelSize.y) / 2f + rise, PanelSize.x, PanelSize.y);

            skin.PanelFrame(panel, now);
            skin.DrawTitle(new Rect(panel.x, panel.y + 26f, panel.width, 46f), "What's your name?", now);
            skin.Divider(panel.center.x, panel.y + 84f, 220f, now);

            Rect field = new Rect(panel.x + 70f, panel.y + 112f, panel.width - 140f, 56f);
            bool submit = Event.current.type == EventType.KeyDown &&
                (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
            if (submit) Event.current.Use();

            skin.SetColor(Color.white, 1f);
            GUI.SetNextControlName(FieldName);
            typed = GUI.TextField(field, typed ?? string.Empty, PlayerProfile.MaxNameLength, fieldStyle);
            if (string.IsNullOrEmpty(typed) && Event.current.type == EventType.Repaint)
            {
                skin.SetColor(Color.white, 0.55f);
                GUI.Label(field, PlayerProfile.DefaultName, placeholderStyle);
            }
            // Focus the field once it exists, so the player can start typing straight away.
            if (!focused && Event.current.type == EventType.Repaint)
            {
                GUI.FocusControl(FieldName);
                focused = true;
            }

            skin.SetColor(Color.white, 1f);
            GUI.Label(new Rect(panel.x + 40f, panel.y + 178f, panel.width - 80f, 26f),
                $"Leave it blank to be called {PlayerProfile.DefaultName}.", skin.SmallCentre);

            bool confirmed = skin.Button(new Rect(panel.center.x - 90f, panel.yMax - 74f, 180f, 44f), "Confirm");
            skin.DrawSparks(open);
            GUI.color = previousColor;

            if (submit || confirmed) Confirm();
        }

        private void EnsureStyles()
        {
            if (fieldStyle != null) return;
            Font font = Resources.Load<Font>("Dialogue/Nunito-Regular");
            Texture2D normal = fieldTexture = MenuArt.Rounded(32, 32, 10f, new Color(0.13f, 0.1f, 0.21f, 0.95f), MenuArt.WithAlpha(MenuArt.Gold, 0.55f), 1.5f);
            Texture2D active = focusedFieldTexture = MenuArt.Rounded(32, 32, 10f, new Color(0.16f, 0.12f, 0.25f, 0.98f), MenuArt.Gold, 2f);
            fieldStyle = new GUIStyle(GUI.skin.textField)
            {
                font = font,
                fontSize = 26,
                alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(12, 12, 12, 12),
                padding = new RectOffset(16, 16, 6, 6),
                normal = { background = normal, textColor = MenuArt.Cream },
                hover = { background = normal, textColor = MenuArt.Cream },
                focused = { background = active, textColor = MenuArt.Gold },
                active = { background = active, textColor = MenuArt.Gold }
            };
            placeholderStyle = new GUIStyle(fieldStyle)
            {
                normal = { background = null, textColor = MenuArt.Muted },
                fontStyle = FontStyle.Italic
            };
        }
    }
}
