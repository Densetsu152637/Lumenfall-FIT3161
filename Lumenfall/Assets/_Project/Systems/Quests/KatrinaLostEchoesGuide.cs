using UnityEngine;
using Lumenfall.Controls;
using Lumenfall.GameEvents;
using Lumenfall.Inventory;

namespace Lumenfall.Quests
{
    /// <summary>
    /// On-screen Lost Echoes usage guide for Something Borrowed. It appears once Katrina's lesson lends the spell
    /// (<c>spell.lesson.LostEchoes</c>) while the quest is Active, tracks the three memory anchors, switches to a
    /// return reminder once all are carried, and disappears when the quest completes.
    /// </summary>
    public sealed class KatrinaLostEchoesGuide : MonoBehaviour
    {
        private const string QuestId = "katrina_something_borrowed";
        private const string LessonFlag = "spell.lesson.LostEchoes";
        private const string KeepsakeFlag = "inventory.keyitem.katrina-keepsake";
        private static readonly string[] AnchorFlags =
        {
            "inventory.keyitem.katrina-library-anchor",
            "inventory.keyitem.katrina-garden-anchor",
            "inventory.keyitem.katrina-stream-anchor"
        };
        private static readonly string[] AnchorLocations = { "Library", "sakura garden", "forest stream" };
        private static KatrinaLostEchoesGuide instance;

        private GUIStyle titleStyle;
        private GUIStyle bodyStyle;
        private GUIStyle panelStyle;
        private Texture2D panelTexture;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            GameObject host = new GameObject("[Katrina Lost Echoes Guide]");
            instance = host.AddComponent<KatrinaLostEchoesGuide>();
            DontDestroyOnLoad(host);
        }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
            if (panelTexture != null) Destroy(panelTexture);
        }

        private void OnGUI()
        {
            GameEventRuntime runtime = GameEventManager.Instance?.Runtime;
            if (runtime == null || runtime.ActiveDialogue != null || InventoryUiState.IsOpen || Time.timeScale <= 0f) return;
            if (runtime.GetQuestStatus(QuestId) != QuestStatus.Active || !runtime.GetFlag(LessonFlag)) return;
            EnsureStyles();

            int recovered = 0;
            string remaining = string.Empty;
            for (int i = 0; i < AnchorFlags.Length; i++)
            {
                if (runtime.GetFlag(AnchorFlags[i])) recovered++;
                else remaining += (remaining.Length == 0 ? string.Empty : ", ") + AnchorLocations[i];
            }
            string body = runtime.GetFlag(KeepsakeFlag)
                ? "All three memory anchors recovered. Return them to Katrina on campus."
                : $"Memory anchors: {recovered}/3\nRemaining: {remaining}\n\n" +
                  $"1. Press {KeyBindings.GetKeyLabel(GameAction.Spell7)} to select Lost Echoes " +
                  $"(or {KeyBindings.GetKeyLabel(GameAction.SpellWheel)} for the spell wheel).\n" +
                  "2. Stand within four cells of a hidden spot.\n" +
                  $"3. Right-click or press {KeyBindings.GetKeyLabel(GameAction.Interact)} to cast. No trace? Move closer and try again.\n" +
                  $"4. Press {KeyBindings.GetKeyLabel(GameAction.Talk)} to pick up what you find.";
            float width = Mathf.Min(360f, Screen.width - 40f);
            float height = 40f + bodyStyle.CalcHeight(new GUIContent(body), width - 28f);
            Rect panel = new Rect(Screen.width - width - 20f, 20f, width, height);
            GUI.Box(panel, GUIContent.none, panelStyle);
            GUI.Label(new Rect(panel.x + 14f, panel.y + 8f, width - 28f, 24f), "Lost Echoes", titleStyle);
            GUI.Label(new Rect(panel.x + 14f, panel.y + 34f, width - 28f, height - 40f), body, bodyStyle);
        }

        private void EnsureStyles()
        {
            if (panelStyle != null) return;
            panelTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            panelTexture.SetPixel(0, 0, new Color(0.08f, 0.07f, 0.12f, 0.82f));
            panelTexture.Apply();
            panelStyle = new GUIStyle(GUI.skin.box);
            panelStyle.normal.background = panelTexture;
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
            titleStyle.normal.textColor = new Color(0.82f, 0.72f, 1f);
            bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
            bodyStyle.normal.textColor = new Color(0.93f, 0.91f, 0.96f);
            Font font = Resources.Load<Font>("Dialogue/Nunito-Regular");
            if (font != null) { titleStyle.font = font; bodyStyle.font = font; }
        }
    }
}
