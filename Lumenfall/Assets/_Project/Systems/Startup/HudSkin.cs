using UnityEngine;

namespace Lumenfall.Startup
{
    /// <summary>
    /// The in-game HUD's look, shared by the small boxes drawn over the world: interaction prompts ("E Talk"), info panels
    /// and short messages. It follows the quest hint card, which uses the startup menu's plum-and-gold panel
    /// (<see cref="MenuSkin"/>); conversations keep their own painted dialogue frames. One skin is shared by every caller.
    /// Call it from OnGUI.
    /// </summary>
    internal static class HudSkin
    {
        /// <summary>The fixed top edge for a side panel, below the Settings button in the top-right corner.</summary>
        public const float SidePanelTop = 64f;
        public const float SidePanelWidth = 360f;
        private const float PanelPadding = 18f;

        private static MenuSkin skin;
        private static GUIStyle promptText, keyCap, keyText, panelTitle, panelBody, messageText, signHeading;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Only references are dropped; destroying textures this early in start-up is not allowed.
            skin = null;
            promptText = null;
        }

        private static MenuSkin Skin
        {
            get
            {
                if (skin == null) skin = new MenuSkin();
                skin.Ensure();
                skin.Fade = 1f;
                EnsureStyles();
                return skin;
            }
        }

        /// <summary>A key prompt centred on <paramref name="centre"/> (IMGUI pixels): a plum pill, a gold key cap and the action.</summary>
        public static void Prompt(Vector2 centre, string key, string action)
        {
            MenuSkin s = Skin;
            key = string.IsNullOrEmpty(key) ? "?" : key;
            float keyWidth = Mathf.Max(24f, keyText.CalcSize(new GUIContent(key)).x + 12f);
            float textWidth = promptText.CalcSize(new GUIContent(action)).x;
            float width = 8f + keyWidth + 9f + textWidth + 14f;
            Rect pill = new Rect(Mathf.Round(centre.x - width * 0.5f), Mathf.Round(centre.y - 17f), width, 34f);
            Color previous = GUI.color;
            s.Box(pill, s.Card, 0.96f);
            Rect cap = new Rect(pill.x + 8f, pill.y + 5f, keyWidth, 24f);
            s.Box(cap, keyCap, 1f);
            s.Label(cap, key, keyText, MenuArt.Ink);
            s.Label(new Rect(cap.xMax + 9f, pill.y, textWidth + 4f, pill.height), action, promptText, MenuArt.Cream);
            GUI.color = previous;
        }

        /// <summary>A prompt over a world position, if it is in front of the camera.</summary>
        public static void PromptAbove(Vector3 worldPosition, string key, string action)
        {
            Camera view = Camera.main;
            if (view == null) return;
            Vector3 screen = view.WorldToScreenPoint(worldPosition);
            if (screen.z <= 0f) return;
            Prompt(new Vector2(screen.x, Screen.height - screen.y), key, action);
        }

        /// <summary>The height a side panel needs for <paramref name="body"/> at <paramref name="width"/>.</summary>
        public static float PanelHeight(string body, float width = SidePanelWidth)
        {
            MenuSkin _ = Skin;
            return PanelPadding + 26f + panelBody.CalcHeight(new GUIContent(body ?? string.Empty), width - PanelPadding * 2f) + PanelPadding;
        }

        /// <summary>A titled info panel, such as a puzzle's progress: the menu panel frame, a gold title and cream text.</summary>
        public static void Panel(Rect rect, string title, string body)
        {
            MenuSkin s = Skin;
            Color previous = GUI.color;
            s.PanelFrame(rect, Time.unscaledTime);
            s.Label(new Rect(rect.x + PanelPadding, rect.y + PanelPadding - 4f, rect.width - PanelPadding * 2f, 26f), title, panelTitle, MenuArt.Gold);
            s.Label(new Rect(rect.x + PanelPadding, rect.y + PanelPadding + 24f, rect.width - PanelPadding * 2f, rect.height - PanelPadding * 2f - 24f),
                body ?? string.Empty, panelBody, MenuArt.Cream);
            GUI.color = previous;
        }

        /// <summary>A centred card naming something the player is reading, such as a building sign: a small gold heading over a large name.</summary>
        public static void SignCard(Rect rect, string heading, string name)
        {
            MenuSkin s = Skin;
            Color previous = GUI.color;
            s.PanelFrame(rect, Time.unscaledTime);
            s.Label(new Rect(rect.x + 24f, rect.y + 28f, rect.width - 48f, 30f), heading, signHeading, MenuArt.Gold);
            s.Divider(rect.center.x, rect.y + 66f, Mathf.Min(220f, rect.width - 80f), Time.unscaledTime);
            s.DrawTitle(new Rect(rect.x + 24f, rect.y + 82f, rect.width - 48f, 58f), name ?? string.Empty, Time.unscaledTime);
            GUI.color = previous;
        }

        /// <summary>A side panel in the top-right corner, below the Settings button, sized to its text.</summary>
        public static void SidePanel(string title, string body)
        {
            float width = Mathf.Min(SidePanelWidth, Screen.width - 32f);
            Panel(new Rect(Screen.width - width - 16f, SidePanelTop, width, PanelHeight(body, width)), title, body);
        }

        /// <summary>A short message in a slim plum card, centred horizontally with its bottom edge <paramref name="bottomOffset"/> above the screen's.</summary>
        public static void Message(string text, float bottomOffset = 28f)
        {
            if (string.IsNullOrEmpty(text)) return;
            MenuSkin s = Skin;
            float width = Mathf.Min(Screen.width - 32f, Mathf.Max(260f, messageText.CalcSize(new GUIContent(text)).x + 44f));
            float height = Mathf.Max(42f, messageText.CalcHeight(new GUIContent(text), width - 36f) + 18f);
            Rect card = new Rect((Screen.width - width) * 0.5f, Screen.height - bottomOffset - height, width, height);
            Color previous = GUI.color;
            s.Box(card, s.Card, 0.97f);
            s.Label(new Rect(card.x + 18f, card.y, card.width - 36f, card.height), text, messageText, MenuArt.Cream);
            GUI.color = previous;
        }

        private static void EnsureStyles()
        {
            if (promptText != null) return;
            Font font = Resources.Load<Font>("Dialogue/Nunito-Regular");
            promptText = Text(font, 15, TextAnchor.MiddleLeft, false);
            keyText = Text(font, 13, TextAnchor.MiddleCenter, false);
            keyText.fontStyle = FontStyle.Bold;
            keyCap = new GUIStyle
            {
                normal = { background = MenuArt.Rounded(24, 24, 6f, MenuArt.Gold, MenuArt.Ember, 1f) },
                border = new RectOffset(8, 8, 8, 8)
            };
            panelTitle = Text(font, 17, TextAnchor.MiddleLeft, false);
            panelTitle.fontStyle = FontStyle.Bold;
            panelBody = Text(font, 14, TextAnchor.UpperLeft, true);
            messageText = Text(font, 15, TextAnchor.MiddleCenter, true);
            signHeading = Text(font, 17, TextAnchor.MiddleCenter, false);
            signHeading.fontStyle = FontStyle.Bold;
        }

        private static GUIStyle Text(Font font, int size, TextAnchor alignment, bool wrap)
        {
            GUIStyle style = new GUIStyle(GUI.skin.label) { fontSize = size, alignment = alignment, wordWrap = wrap, richText = false };
            if (font != null) style.font = font;
            return style;
        }
    }
}
