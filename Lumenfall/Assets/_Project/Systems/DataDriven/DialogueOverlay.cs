using System;
using UnityEngine;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;

namespace Lumenfall.DataDriven
{
    /// Cozy, data-driven dialogue presentation. Traversal and actions remain in DialogueSession.
    public sealed class DialogueOverlay : MonoBehaviour
    {
        private DialogueSession session;
        private GUIStyle panelStyle;
        private GUIStyle nameStyle;
        private GUIStyle textStyle;
        private GUIStyle buttonStyle;
        private GUIStyle hintStyle;
        private GridMover lockedMover;
        private string displayedText = string.Empty;
        private int visibleCharacters;
        private float revealAccumulator;
        private string nodeId;
        private Texture2D borderTexture;
        private Texture2D fallbackPanelTexture;
        private Font dialogueFont;
        private Texture2D speakerFrame;
        private Vector2 scrollPosition;
        private int inputBlockedFrame = -1;
        private static int npcInteractionBlockUntilFrame = -1;
        private const float CharactersPerSecond = 42f;

        private void OnEnable()
        {
            if (GameEventManager.Instance != null)
            {
                GameEventManager.Instance.Runtime.DialogueStarted += OnStarted;
                GameEventManager.Instance.Runtime.DialogueEnded += OnEnded;
            }
        }

        private void OnDisable()
        {
            if (lockedMover != null) lockedMover.SetMovementLocked(false);
            lockedMover = null;
            if (GameEventManager.Instance != null)
            {
                GameEventManager.Instance.Runtime.DialogueStarted -= OnStarted;
                GameEventManager.Instance.Runtime.DialogueEnded -= OnEnded;
            }
        }

        private void OnStarted(DialogueSession value)
        {
            session = value;
            lockedMover = FindAnyObjectByType<GridMover>();
            if (lockedMover != null) lockedMover.SetMovementLocked(true);
            inputBlockedFrame = Time.frameCount;
            ResetReveal();
        }
        private void OnEnded(DialogueSession value)
        {
            if (session != value) return;
            npcInteractionBlockUntilFrame = Time.frameCount + 1;
            if (lockedMover != null) lockedMover.SetMovementLocked(false);
            lockedMover = null;
            session = null;
            displayedText = string.Empty;
            nodeId = null;
        }

        private void Update()
        {
            if (session == null || !session.IsActive || session.CurrentNode == null) return;
            EnsureNode();
            int count = DialogueTextReveal.VisibleCharacterCount(displayedText);
            if (visibleCharacters >= count) return;
            revealAccumulator += Time.unscaledDeltaTime * CharactersPerSecond;
            while (revealAccumulator >= 1f && visibleCharacters < count)
            {
                revealAccumulator -= 1f;
                char last = DialogueTextReveal.VisibleCharacterAt(displayedText, visibleCharacters++);
                float punctuationPause = DialogueTextReveal.PunctuationPause(last);
                if (punctuationPause > 0f) revealAccumulator = Mathf.Min(revealAccumulator, -punctuationPause);
            }
        }

        private void OnGUI()
        {
            if (session == null || !session.IsActive || session.CurrentNode == null) return;
            EnsureStyles();
            EnsureNode();
            if (Time.frameCount > inputBlockedFrame) HandleAdvanceInput();
            // Advancing past the last node ends the dialogue, and OnEnded clears the session.
            if (session == null || !session.IsActive || session.CurrentNode == null) return;
            float width = Mathf.Min(Screen.width - 40f, 760f);
            // Generated frames are 3:1; preserve that ratio so their quiet center and border remain intact.
            float height = Mathf.Min(Screen.height * 0.42f, width / 3f);
            Rect panel = new Rect((Screen.width - width) * 0.5f, Screen.height - height - 22f, width, height);
            string speaker = string.IsNullOrWhiteSpace(session.CurrentNode.speaker) ? session.Definition.id : session.CurrentNode.speaker;
            speakerFrame = SpeakerFrame(speaker);
            panelStyle.normal.background = speakerFrame != null ? speakerFrame : (borderTexture != null ? borderTexture : fallbackPanelTexture);
            GUI.Box(panel, GUIContent.none, panelStyle);
            float safeWidth = panel.width * 0.72f;
            float safeHeight = panel.height * 0.58f;
            GUILayout.BeginArea(new Rect(panel.x + panel.width * 0.14f, panel.y + panel.height * 0.18f, safeWidth, safeHeight));
            SetTextColor(nameStyle, SpeakerColor(speaker));
            GUILayout.Label(speaker, nameStyle);
            scrollPosition = GUILayout.BeginScrollView(scrollPosition, false, true, GUILayout.Height(Mathf.Max(40f, safeHeight - 34f)));
            GUILayout.Label(DialogueTextReveal.Reveal(displayedText, visibleCharacters), textStyle);
            bool complete = visibleCharacters >= DialogueTextReveal.VisibleCharacterCount(displayedText);
            if (complete && session.AvailableChoices.Count > 0)
            {
                for (int i = 0; i < session.AvailableChoices.Count; i++)
                {
                    DialogueChoiceDefinition choice = session.AvailableChoices[i];
                    if (!GUILayout.Button(choice.text ?? choice.id, buttonStyle)) continue;
                    // Selecting can end the dialogue (clearing session) or replace the choice list, so stop drawing choices.
                    session.SelectChoice(choice.id);
                    break;
                }
            }
            else if (complete && GUILayout.Button(string.IsNullOrWhiteSpace(session.CurrentNode.nextNodeId) ? "Close" : "Continue", buttonStyle))
            {
                session.Continue();
            }
            else if (!complete) GUILayout.Label("Press Space or E to reveal", hintStyle);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void HandleAdvanceInput()
        {
            Event e = Event.current;
            if (e.type != EventType.KeyDown || (e.keyCode != KeyCode.Space && e.keyCode != KeyCode.Return && e.keyCode != KeyCode.E)) return;
            e.Use();
            if (visibleCharacters < DialogueTextReveal.VisibleCharacterCount(displayedText))
            {
                visibleCharacters = DialogueTextReveal.VisibleCharacterCount(displayedText);
                revealAccumulator = 0f;
            }
            else if (session.AvailableChoices.Count == 0) session.Continue();
        }

        private void EnsureNode()
        {
            if (session.CurrentNode.id == nodeId) return;
            ResetReveal();
        }

        private void ResetReveal()
        {
            nodeId = session != null && session.CurrentNode != null ? session.CurrentNode.id : null;
            displayedText = session != null && session.CurrentNode != null ? session.CurrentNode.text ?? string.Empty : string.Empty;
            visibleCharacters = 0;
            revealAccumulator = 0f;
            scrollPosition = Vector2.zero;
        }

        private void EnsureStyles()
        {
            if (panelStyle != null) return;
            borderTexture = Resources.Load<Texture2D>("Dialogue/dialogue_border");
            dialogueFont = Resources.Load<Font>("Dialogue/Nunito-Regular");
            panelStyle = new GUIStyle(GUI.skin.box) { padding = new RectOffset(18, 18, 12, 12) };
            if (borderTexture != null) panelStyle.normal.background = borderTexture;
            else
            {
                fallbackPanelTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                fallbackPanelTexture.SetPixel(0, 0, new Color(0.055f, 0.07f, 0.09f, 0.94f));
                fallbackPanelTexture.Apply();
                panelStyle.normal.background = fallbackPanelTexture;
            }
            nameStyle = new GUIStyle(GUI.skin.label) { fontSize = 21, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperLeft };
            textStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, wordWrap = true, richText = true };
            SetTextColor(textStyle, new Color(0.12f, 0.11f, 0.16f));
            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 16, wordWrap = true };
            // The default skin turns hovered text white; use a warm highlight that stays readable on the dark button.
            SetTextColor(buttonStyle, new Color(0.92f, 0.90f, 0.86f));
            buttonStyle.hover.textColor = new Color(1f, 0.82f, 0.36f);
            buttonStyle.active.textColor = new Color(1f, 0.72f, 0.22f);
            hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleRight };
            SetTextColor(hintStyle, new Color(0.35f, 0.34f, 0.40f));
            if (dialogueFont != null) { nameStyle.font = dialogueFont; textStyle.font = dialogueFont; buttonStyle.font = dialogueFont; hintStyle.font = dialogueFont; }
        }

        /// Applies one colour to every interaction state so hovering or clicking never swaps in the skin's white text.
        private static void SetTextColor(GUIStyle style, Color color)
        {
            style.normal.textColor = color;
            style.hover.textColor = color;
            style.active.textColor = color;
            style.focused.textColor = color;
            style.onNormal.textColor = color;
            style.onHover.textColor = color;
            style.onActive.textColor = color;
            style.onFocused.textColor = color;
        }

        private static Texture2D SpeakerFrame(string speaker)
        {
            if (string.Equals(speaker, "Stella", StringComparison.OrdinalIgnoreCase)) return Resources.Load<Texture2D>("Dialogue/Stella");
            if (string.Equals(speaker, "Cinna Cirra", StringComparison.OrdinalIgnoreCase)) return Resources.Load<Texture2D>("Dialogue/CinnaCirra");
            if (string.Equals(speaker, "Orio", StringComparison.OrdinalIgnoreCase)) return Resources.Load<Texture2D>("Dialogue/Orio");
            if (string.Equals(speaker, "Katrina", StringComparison.OrdinalIgnoreCase)) return Resources.Load<Texture2D>("Dialogue/Katrina");
            // Students, the Narrator and any other speaker without a frame share the neutral parchment frame, so the
            // dark body text never lands on the dark fallback panel.
            return Resources.Load<Texture2D>("Dialogue/Random NPC");
        }

        private static Color SpeakerColor(string speaker)
        {
            if (string.Equals(speaker, "Stella", StringComparison.OrdinalIgnoreCase)) return new Color(0.76f, 0.22f, 0.30f);
            if (string.Equals(speaker, "Cinna Cirra", StringComparison.OrdinalIgnoreCase)) return new Color(0.30f, 0.48f, 0.72f);
            if (string.Equals(speaker, "Orio", StringComparison.OrdinalIgnoreCase)) return new Color(0.18f, 0.53f, 0.43f);
            if (string.Equals(speaker, "Katrina", StringComparison.OrdinalIgnoreCase)) return new Color(0.64f, 0.33f, 0.58f);
            return new Color(0.26f, 0.25f, 0.30f);
        }

        internal static bool IsNpcInteractionBlocked => Time.frameCount <= npcInteractionBlockUntilFrame;
    }
}
