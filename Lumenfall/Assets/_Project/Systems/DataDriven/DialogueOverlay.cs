using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Accessibility;

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
        private string highContrastText = string.Empty;
        private int visibleCharacters;
        private float revealAccumulator;
        private string nodeId;
        private Texture2D borderTexture;
        private Font dialogueFont;
        private Texture2D speakerFrame;
        private Vector2 scrollPosition;
        private int inputBlockedFrame = -1;
        private int choiceFocus;
        private bool scrollToFocus;
        private readonly AccessibilityMenuInput menuInput = new AccessibilityMenuInput();

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
            if (lockedMover != null) lockedMover.SetMovementLocked(false);
            lockedMover = null;
            session = null;
            displayedText = string.Empty;
            nodeId = null;
        }
        private void Update()
        {
            if (AccessibilitySettingsOverlay.InputBlocked || session == null || !session.IsActive || session.CurrentNode == null) return;
            EnsureNode();
            AccessibilitySnapshot settings = AccessibilitySettings.Current;
            int count = DialogueTextReveal.VisibleCharacterCount(displayedText);
            if (settings.InstantReveal) visibleCharacters = count;
            else if (visibleCharacters < count)
            {
                revealAccumulator += Time.unscaledDeltaTime * settings.CharactersPerSecond;
                while (revealAccumulator >= 1f && visibleCharacters < count)
                {
                    revealAccumulator -= 1f;
                    char last = DialogueTextReveal.VisibleCharacterAt(displayedText, visibleCharacters++);
                    float punctuationPause = DialogueTextReveal.PunctuationPause(last);
                    if (punctuationPause > 0f) revealAccumulator = Mathf.Min(revealAccumulator, -punctuationPause);
                }
            }
            if (Time.frameCount <= inputBlockedFrame) return;
            menuInput.Poll();
            if (menuInput.Cancel) { session.End(); return; }
            if (menuInput.Direction.y != 0 && session.AvailableChoices.Count > 0)
            {
                choiceFocus = (choiceFocus + menuInput.Direction.y + session.AvailableChoices.Count) % session.AvailableChoices.Count;
                scrollToFocus = true;
            }
            bool advance = menuInput.Submit || Keyboard.current?.eKey.wasPressedThisFrame == true;
            if (!advance) return;
            if (visibleCharacters < count) { visibleCharacters = count; revealAccumulator = 0f; }
            else if (session.AvailableChoices.Count == 0) session.Continue();
            else session.SelectChoice(session.AvailableChoices[Mathf.Clamp(choiceFocus, 0, session.AvailableChoices.Count - 1)].id);
        }
        private void OnGUI()
        {
            if (AccessibilitySettingsOverlay.InputBlocked || session == null || !session.IsActive || session.CurrentNode == null) return;
            AccessibilityMenuInput.ConsumeGuiNavigation();
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.E) Event.current.Use();
            EnsureStyles();
            EnsureNode();
            AccessibilitySnapshot settings = AccessibilitySettings.Current;
            float width = Mathf.Min(Screen.width - 24f, settings.TextScale > 1f ? 960f : 760f);
            float height = settings.TextScale > 1f ? Mathf.Min(Screen.height - 40f, Screen.height * 0.7f) : Mathf.Min(Screen.height * 0.42f, width / 3f);
            Rect panel = new Rect((Screen.width - width) * 0.5f, Screen.height - height - 18f, width, height);
            string speaker = string.IsNullOrWhiteSpace(session.CurrentNode.speaker) ? session.Definition.id : session.CurrentNode.speaker;
            speakerFrame = SpeakerFrame(speaker);
            panelStyle.normal.background = settings.HighContrast ? Texture2D.whiteTexture : speakerFrame != null ? speakerFrame : borderTexture;
            Color previousBackground = GUI.backgroundColor;
            if (settings.HighContrast) GUI.backgroundColor = Color.black;
            GUI.Box(panel, GUIContent.none, panelStyle);
            GUI.backgroundColor = previousBackground;
            float safeWidth = panel.width * 0.72f;
            float safeHeight = panel.height * 0.64f;
            GUILayout.BeginArea(new Rect(panel.x + panel.width * 0.14f, panel.y + panel.height * 0.16f, safeWidth, safeHeight));
            nameStyle.normal.textColor = settings.HighContrast ? Color.white : SpeakerColor(speaker);
            GUILayout.Label(Localization.Resolve("speaker." + speaker, speaker), nameStyle);
            scrollPosition = GUILayout.BeginScrollView(scrollPosition, false, true, GUILayout.Height(Mathf.Max(40f, safeHeight - nameStyle.fontSize * 1.8f)));
            GUILayout.Label(DialogueTextReveal.Reveal(settings.HighContrast ? highContrastText : displayedText, visibleCharacters), textStyle);
            bool complete = visibleCharacters >= DialogueTextReveal.VisibleCharacterCount(displayedText);
            bool previousEnabled = GUI.enabled;
            GUI.enabled = Time.frameCount > inputBlockedFrame;
            if (complete && session.AvailableChoices.Count > 0)
            {
                choiceFocus = Mathf.Clamp(choiceFocus, 0, session.AvailableChoices.Count - 1);
                for (int i = 0; i < session.AvailableChoices.Count; i++)
                {
                    DialogueChoiceDefinition choice = session.AvailableChoices[i];
                    if (i == choiceFocus) GUI.backgroundColor = Color.yellow;
                    string choiceText = Localization.Resolve(Localization.DialogueChoiceId(session.Definition.id, nodeId, choice.id), choice.text ?? choice.id);
                    bool clicked = GUILayout.Button((i == choiceFocus ? "> " : "") + (settings.HighContrast ? AccessibleDialogueText.RemoveColorTags(choiceText) : choiceText), buttonStyle);
                    GUI.backgroundColor = previousBackground;
                    if (scrollToFocus && i == choiceFocus && Event.current.type == EventType.Repaint)
                    {
                        Rect row = GUILayoutUtility.GetLastRect();
                        float viewportHeight = Mathf.Max(40f, safeHeight - nameStyle.fontSize * 1.8f);
                        if (row.y < scrollPosition.y) scrollPosition.y = row.y;
                        else if (row.yMax > scrollPosition.y + viewportHeight) scrollPosition.y = row.yMax - viewportHeight;
                        scrollToFocus = false;
                    }
                    if (clicked) { session.SelectChoice(choice.id); break; }
                }
            }
            else if (complete && GUILayout.Button(Localization.Resolve(string.IsNullOrWhiteSpace(session.CurrentNode.nextNodeId) ? "dialogue.close" : "dialogue.continue",
                string.IsNullOrWhiteSpace(session.CurrentNode.nextNodeId) ? "Close" : "Continue"), buttonStyle)) session.Continue();
            else if (!complete) GUILayout.Label(Localization.Resolve("dialogue.reveal_hint", "Space / E / controller A: reveal. F10 / Start: settings."), hintStyle);
            GUI.enabled = previousEnabled;
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
        private void EnsureNode()
        {
            if (session.CurrentNode.id == nodeId) return;
            ResetReveal();
        }
        private void ResetReveal()
        {
            nodeId = session != null && session.CurrentNode != null ? session.CurrentNode.id : null;
            string fallback = session != null && session.CurrentNode != null ? session.CurrentNode.text ?? string.Empty : string.Empty;
            displayedText = session != null ? Localization.Resolve(Localization.DialogueTextId(session.Definition.id, nodeId), fallback) : fallback;
            highContrastText = AccessibleDialogueText.RemoveColorTags(displayedText);
            visibleCharacters = AccessibilitySettings.Current.InstantReveal ? DialogueTextReveal.VisibleCharacterCount(displayedText) : 0;
            revealAccumulator = 0f;
            scrollPosition = Vector2.zero;
            choiceFocus = 0;
            scrollToFocus = false;
        }
        private void EnsureStyles()
        {
            if (panelStyle == null)
            {
                borderTexture = Resources.Load<Texture2D>("Dialogue/dialogue_border");
                dialogueFont = Resources.Load<Font>("Dialogue/Nunito-Regular");
                panelStyle = new GUIStyle(GUI.skin.box) { padding = new RectOffset(18, 18, 12, 12) };
                nameStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperLeft, wordWrap = true };
                textStyle = new GUIStyle(GUI.skin.label) { wordWrap = true, richText = true };
                buttonStyle = new GUIStyle(GUI.skin.button) { wordWrap = true };
                hintStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleRight, wordWrap = true };
                if (dialogueFont != null) { nameStyle.font = dialogueFont; textStyle.font = dialogueFont; buttonStyle.font = dialogueFont; hintStyle.font = dialogueFont; }
            }
            AccessibilitySnapshot value = AccessibilitySettings.Current;
            nameStyle.fontSize = Mathf.RoundToInt(21f * value.TextScale);
            textStyle.fontSize = Mathf.RoundToInt(18f * value.TextScale);
            buttonStyle.fontSize = Mathf.RoundToInt(16f * value.TextScale);
            hintStyle.fontSize = Mathf.RoundToInt(13f * value.TextScale);
            textStyle.normal.textColor = value.HighContrast ? Color.white : new Color(0.12f, 0.11f, 0.16f);
            hintStyle.normal.textColor = value.HighContrast ? Color.white : new Color(0.35f, 0.34f, 0.40f);
        }
        private static Texture2D SpeakerFrame(string speaker)
        {
            if (string.Equals(speaker, "Stella", StringComparison.OrdinalIgnoreCase)) return Resources.Load<Texture2D>("Dialogue/Stella");
            if (string.Equals(speaker, "Cinna Cirra", StringComparison.OrdinalIgnoreCase)) return Resources.Load<Texture2D>("Dialogue/CinnaCirra");
            if (string.Equals(speaker, "Orio", StringComparison.OrdinalIgnoreCase)) return Resources.Load<Texture2D>("Dialogue/Orio");
            if (string.Equals(speaker, "Katrina", StringComparison.OrdinalIgnoreCase)) return Resources.Load<Texture2D>("Dialogue/Katrina");
            return null;
        }

        private static Color SpeakerColor(string speaker)
        {
            if (string.Equals(speaker, "Stella", StringComparison.OrdinalIgnoreCase)) return new Color(0.76f, 0.22f, 0.30f);
            if (string.Equals(speaker, "Cinna Cirra", StringComparison.OrdinalIgnoreCase)) return new Color(0.30f, 0.48f, 0.72f);
            if (string.Equals(speaker, "Orio", StringComparison.OrdinalIgnoreCase)) return new Color(0.18f, 0.53f, 0.43f);
            if (string.Equals(speaker, "Katrina", StringComparison.OrdinalIgnoreCase)) return new Color(0.64f, 0.33f, 0.58f);
            return new Color(0.26f, 0.25f, 0.30f);
        }
    }
}
