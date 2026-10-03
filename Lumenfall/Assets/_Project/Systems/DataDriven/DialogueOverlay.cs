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
        private GameEventRuntime subscribedRuntime;
        private readonly Lumenfall.Startup.UiNavigationInput navigation = new Lumenfall.Startup.UiNavigationInput();
        private int selectedChoice;
        private bool scrollToChoice;
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
        private Font dialogueFont;
        private Texture2D speakerFrame;
        private Vector2 scrollPosition;
        private int inputBlockedFrame = -1;
        private const float CharactersPerSecond = 42f;

        private void OnEnable()
        {
            BindRuntime();
        }

        private void OnDisable()
        {
            if (lockedMover != null) lockedMover.SetMovementLocked(this, false);
            lockedMover = null;
            UnbindRuntime();
            session = null;
            OverlayInputGate.Release(this);
        }

        private void UnbindRuntime()
        {
            if (subscribedRuntime == null) return;
            subscribedRuntime.DialogueStarted -= OnStarted;
            subscribedRuntime.DialogueEnded -= OnEnded;
            subscribedRuntime = null;
        }
        private void BindRuntime()
        {
            GameEventRuntime current = GameEventManager.Instance.Runtime;
            if (current == subscribedRuntime) return;
            if (session != null) OnEnded(session);
            UnbindRuntime();
            subscribedRuntime = current;
            if (current == null) return;
            current.DialogueStarted += OnStarted;
            current.DialogueEnded += OnEnded;
            if (current.ActiveDialogue != null) OnStarted(current.ActiveDialogue);
        }

        private void OnStarted(DialogueSession value)
        {
            session = value;
            OverlayInputGate.Acquire(this);
            lockedMover = FindAnyObjectByType<GridMover>();
            if (lockedMover != null) lockedMover.SetMovementLocked(this, true);
            inputBlockedFrame = Time.frameCount;
            ResetReveal();
        }
        private void OnEnded(DialogueSession value)
        {
            if (session != value) return;
            if (lockedMover != null) lockedMover.SetMovementLocked(this, false);
            lockedMover = null;
            session = null;
            OverlayInputGate.Release(this);
            displayedText = string.Empty;
            nodeId = null;
        }

        private void Update()
        {
            BindRuntime();
            if (session == null || !session.IsActive || session.CurrentNode == null) return;
            EnsureNode();
            navigation.Read();
            if (Time.frameCount > inputBlockedFrame && !OverlayInputGate.IsBlockedExcept(this))
            {
                if (navigation.Cancel) { session.End(); return; }
                if (navigation.Vertical != 0 && session.AvailableChoices.Count > 0)
                {
                    selectedChoice = UiFocusIndex.Move(selectedChoice, navigation.Vertical, session.AvailableChoices.Count);
                    scrollToChoice = true;
                }
                if (navigation.Vertical != 0 && session.AvailableChoices.Count == 0) scrollPosition.y = Mathf.Max(0f, scrollPosition.y + navigation.Vertical * 70f);
                if (navigation.Submit || UnityEngine.InputSystem.Keyboard.current?.eKey.wasPressedThisFrame == true) Advance();
            }
            if (session == null || !session.IsActive) return;
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
            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && Time.frameCount > inputBlockedFrame && !OverlayInputGate.IsBlockedExcept(this);
            float width = Mathf.Min(Screen.width - 40f, 760f);
            // Generated frames are 3:1; preserve that ratio so their quiet center and border remain intact.
            float height = Mathf.Min(Screen.height * 0.42f, width / 3f);
            Rect panel = new Rect((Screen.width - width) * 0.5f, Screen.height - height - 22f, width, height);
            string speaker = string.IsNullOrWhiteSpace(session.CurrentNode.speaker) ? session.Definition.id : session.CurrentNode.speaker;
            speakerFrame = SpeakerFrame(speaker);
            panelStyle.normal.background = speakerFrame != null ? speakerFrame : borderTexture;
            GUI.Box(panel, GUIContent.none, panelStyle);
            float safeWidth = panel.width * 0.72f;
            float safeHeight = panel.height * 0.58f;
            GUILayout.BeginArea(new Rect(panel.x + panel.width * 0.14f, panel.y + panel.height * 0.18f, safeWidth, safeHeight));
            nameStyle.normal.textColor = SpeakerColor(speaker);
            GUILayout.Label(speaker, nameStyle);
            scrollPosition = GUILayout.BeginScrollView(scrollPosition, false, true, GUILayout.Height(Mathf.Max(40f, safeHeight - 34f)));
            GUILayout.Label(DialogueTextReveal.Reveal(displayedText, visibleCharacters), textStyle);
            bool complete = visibleCharacters >= DialogueTextReveal.VisibleCharacterCount(displayedText);
            if (complete && session.AvailableChoices.Count > 0)
            {
                for (int i = 0; i < session.AvailableChoices.Count; i++)
                {
                    DialogueChoiceDefinition choice = session.AvailableChoices[i];
                    if (GUILayout.Button((i == selectedChoice ? "> " : "") + (choice.text ?? choice.id), buttonStyle, GUILayout.MinHeight(36f)))
                    { session.SelectChoice(choice.id); break; }
                    if (i == selectedChoice && scrollToChoice && Event.current.type == EventType.Repaint)
                    {
                        Rect row = GUILayoutUtility.GetLastRect();
                        float viewport = Mathf.Max(40f, safeHeight - 34f);
                        if (row.height > viewport || row.yMin < scrollPosition.y) scrollPosition.y = row.yMin;
                        else if (row.yMax > scrollPosition.y + viewport) scrollPosition.y = Mathf.Max(0f, row.yMax - viewport);
                        scrollToChoice = false;
                    }
                }
            }
            else if (complete && GUILayout.Button(string.IsNullOrWhiteSpace(session.CurrentNode.nextNodeId) ? "Close" : "Continue", buttonStyle))
            {
                session.Continue();
            }
            else if (!complete) GUILayout.Label("Space / E / A: reveal   Esc / B: close", hintStyle);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUI.enabled = previousEnabled;
        }

        private void Advance()
        {
            if (visibleCharacters < DialogueTextReveal.VisibleCharacterCount(displayedText))
            {
                visibleCharacters = DialogueTextReveal.VisibleCharacterCount(displayedText);
                revealAccumulator = 0f;
            }
            else if (session.AvailableChoices.Count == 0) session.Continue();
            else session.SelectChoice(session.AvailableChoices[UiFocusIndex.Move(selectedChoice, 0, session.AvailableChoices.Count)].id);
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
            selectedChoice = 0;
            scrollToChoice = true;
            inputBlockedFrame = Time.frameCount;
        }

        private void EnsureStyles()
        {
            if (panelStyle != null) return;
            borderTexture = Resources.Load<Texture2D>("Dialogue/dialogue_border");
            dialogueFont = Resources.Load<Font>("Dialogue/Nunito-Regular");
            panelStyle = new GUIStyle(GUI.skin.box) { padding = new RectOffset(18, 18, 12, 12) };
            if (borderTexture != null) panelStyle.normal.background = borderTexture;
            nameStyle = new GUIStyle(GUI.skin.label) { fontSize = 21, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperLeft };
            textStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, wordWrap = true, richText = true, normal = { textColor = new Color(0.12f, 0.11f, 0.16f) } };
            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 16, wordWrap = true };
            hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleRight, normal = { textColor = new Color(0.35f, 0.34f, 0.40f) } };
            if (dialogueFont != null) { nameStyle.font = dialogueFont; textStyle.font = dialogueFont; buttonStyle.font = dialogueFont; hintStyle.font = dialogueFont; }
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
