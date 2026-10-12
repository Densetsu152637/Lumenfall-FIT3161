using System;
using System.Collections.Generic;
using System.Linq;
using Lumenfall.Controls;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Quests;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Lumenfall.DataDriven
{
    /// <summary>
    /// Cozy, data-driven dialogue presentation. Traversal and actions remain in DialogueSession; this component reveals
    /// the text, takes input and drives the UI Toolkit <see cref="DialogueView"/>: the speaker's painted frame and name
    /// ribbon, a live portrait of the speaking NPC, keyboard, mouse and gamepad choices, voice blips, text effects, and
    /// a camera that eases in to frame the conversation.
    /// </summary>
    public sealed class DialogueOverlay : MonoBehaviour
    {
        private const float CharactersPerSecond = 42f;
        private const float ConversationNpcRange = 3f;
        private const float ConversationZoom = 0.72f;
        // Roughly three lines in the full-size box; longer lines are split into pages.
        private const int MaxPageCharacters = 130;

        private DialogueSession session;
        private GridMover lockedMover;
        private DynamicNpc conversationNpc;
        private DialogueView view;
        private DialoguePortrait portrait;
        private DialogueBlips blips;

        private string nodeId;
        private string speaker = string.Empty;
        private List<string> pages = new List<string>();
        private int pageIndex;
        private DialogueTextEffects.Prepared prepared = new DialogueTextEffects.Prepared();
        private int visibleCharacters;
        private int shownCharacters = -1;
        private float revealAccumulator;
        private int selectedChoice;
        private string choicesForNode;
        private int inputBlockedFrame = -1;
        private bool pointerConfirm;
        private static int npcInteractionBlockUntilFrame = -1;

        internal static bool IsNpcInteractionBlocked => Time.frameCount <= npcInteractionBlockUntilFrame;

        private bool Revealed => visibleCharacters >= DialogueTextReveal.VisibleCharacterCount(prepared.RichText);

        private bool OnLastPage => pageIndex >= pages.Count - 1;

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
            EndPresentation();
            if (GameEventManager.Instance != null)
            {
                GameEventManager.Instance.Runtime.DialogueStarted -= OnStarted;
                GameEventManager.Instance.Runtime.DialogueEnded -= OnEnded;
            }
        }

        private void OnDestroy()
        {
            view?.Dispose();
            portrait?.Dispose();
        }

        private void EnsurePresentation()
        {
            if (view != null) return;
            view = new DialogueView(transform);
            view.ChoiceHovered += SelectChoice;
            view.ChoiceClicked += index => { if (CanTakeInput()) Choose(index); };
            view.BoxClicked += () => pointerConfirm = true;
            portrait = new DialoguePortrait(transform);
            blips = new DialogueBlips(gameObject);
        }

        private void OnStarted(DialogueSession value)
        {
            EnsurePresentation();
            session = value;
            GridPlayerInput input = FindAnyObjectByType<GridPlayerInput>();
            lockedMover = input != null ? input.GetComponent<GridMover>() : FindAnyObjectByType<GridMover>();
            if (lockedMover != null) lockedMover.SetMovementLocked(true);
            conversationNpc = NearestNpc();
            inputBlockedFrame = Time.frameCount;
            nodeId = null;
            view.Show(true);
            if (session.CurrentNode != null) EnsureNode();
        }

        private void OnEnded(DialogueSession value)
        {
            if (session != value) return;
            npcInteractionBlockUntilFrame = Time.frameCount + 1;
            EndPresentation();
        }

        private void EndPresentation()
        {
            if (lockedMover != null) lockedMover.SetMovementLocked(false);
            lockedMover = null;
            session = null;
            nodeId = null;
            conversationNpc = null;
            view?.Show(false);
            portrait?.Hide();
            CameraFollow2_5D.ClearConversationFrame();
        }

        private void Update()
        {
            if (session == null || !session.IsActive || session.CurrentNode == null) return;
            EnsureNode();
            Reveal();
            HandleInput();
            // Input may have ended the dialogue or moved to another node.
            if (session == null || !session.IsActive || session.CurrentNode == null) return;
            EnsureNode();
            RefreshText();
            RefreshChoices();
            view.SetWaiting(Revealed && (!OnLastPage || session.AvailableChoices.Count == 0));
            FrameCamera();
            view.Tick();
        }

        private void LateUpdate()
        {
            if (session != null) portrait?.Frame();
        }

        private void EnsureNode()
        {
            if (session.CurrentNode.id == nodeId) return;
            DialogueNodeDefinition node = session.CurrentNode;
            nodeId = node.id;
            speaker = string.IsNullOrWhiteSpace(node.speaker) ? session.Definition.id : node.speaker;
            pages = DialoguePaging.Split(node.text ?? string.Empty, MaxPageCharacters);
            pageIndex = 0;
            ShowPage();

            Color colour = SpeakerColor(speaker);
            view.SetSpeaker(speaker, colour, SpeakerFrame(speaker));
            Transform subject = SpeakerTransform(speaker);
            Color background = Color.Lerp(colour, new Color(0.96f, 0.91f, 0.82f), 0.72f);
            bool showPortrait = subject != null && portrait.Show(subject, background);
            if (!showPortrait) portrait.Hide();
            view.SetPortrait(showPortrait ? portrait.Texture : null);
        }

        /// <summary>Starts revealing the current page and resizes the box to fit it (plus the node's choices on the last page).</summary>
        private void ShowPage()
        {
            prepared = DialogueTextEffects.Prepare(pages[pageIndex]);
            visibleCharacters = 0;
            shownCharacters = -1;
            revealAccumulator = 0f;
            selectedChoice = 0;
            choicesForNode = null;
            view.ClearChoices();
            view.FitTo(prepared.RichText, OnLastPage ? session.AvailableChoices.Select(c => c.text ?? c.id).ToList() : null);
        }

        private void Reveal()
        {
            int count = DialogueTextReveal.VisibleCharacterCount(prepared.RichText);
            if (visibleCharacters >= count) return;
            revealAccumulator += Time.unscaledDeltaTime * CharactersPerSecond;
            while (revealAccumulator >= 1f && visibleCharacters < count)
            {
                revealAccumulator -= 1f;
                char last = DialogueTextReveal.VisibleCharacterAt(prepared.RichText, visibleCharacters++);
                if (char.IsLetterOrDigit(last) && visibleCharacters % 2 == 0) blips.Play(speaker);
                float punctuationPause = DialogueTextReveal.PunctuationPause(last);
                if (punctuationPause > 0f) revealAccumulator = Mathf.Min(revealAccumulator, -punctuationPause);
            }
        }

        private void RefreshText()
        {
            if (shownCharacters == visibleCharacters) return;
            shownCharacters = visibleCharacters;
            view.SetText(DialogueTextReveal.Reveal(prepared.RichText, visibleCharacters), prepared.GlyphEffects);
        }

        private void RefreshChoices()
        {
            IReadOnlyList<DialogueChoiceDefinition> available = session.AvailableChoices;
            if (!Revealed || !OnLastPage || available.Count == 0 || choicesForNode == nodeId) return;
            choicesForNode = nodeId;
            view.SetChoices(available.Select(c => c.text ?? c.id).ToList());
            selectedChoice = 0;
            view.SetSelected(0);
        }

        private bool CanTakeInput() => session != null && Time.frameCount > inputBlockedFrame && !QuestHintOverlay.IsSettingsOpen;

        private void HandleInput()
        {
            bool pointer = pointerConfirm;
            pointerConfirm = false;
            if (!CanTakeInput()) return;

            Gamepad pad = Gamepad.current;
            bool confirm = pointer || KeyBindings.WasPressed(GameAction.Talk) || Pressed(Key.Space) || Pressed(Key.Enter) ||
                           Pressed(Key.NumpadEnter) || (pad != null && pad.buttonSouth.wasPressedThisFrame);
            if (!Revealed)
            {
                if (!confirm) return;
                visibleCharacters = DialogueTextReveal.VisibleCharacterCount(prepared.RichText);
                revealAccumulator = 0f;
                return;
            }

            if (!OnLastPage)
            {
                if (!confirm) return;
                pageIndex++;
                ShowPage();
                return;
            }

            int choiceCount = session.AvailableChoices.Count;
            if (choiceCount == 0)
            {
                if (confirm) session.Continue();
                return;
            }

            if (!view.HasChoices) return;
            if (KeyBindings.WasPressed(GameAction.MoveUp) || Pressed(Key.UpArrow) || (pad != null && pad.dpad.up.wasPressedThisFrame))
                SelectChoice((selectedChoice + choiceCount - 1) % choiceCount);
            if (KeyBindings.WasPressed(GameAction.MoveDown) || Pressed(Key.DownArrow) || (pad != null && pad.dpad.down.wasPressedThisFrame))
                SelectChoice((selectedChoice + 1) % choiceCount);
            for (int i = 0; i < Mathf.Min(choiceCount, 9); i++)
            {
                if (!Pressed(Key.Digit1 + i) && !Pressed(Key.Numpad1 + i)) continue;
                Choose(i);
                return;
            }

            // A click on the box itself (not a choice row) should not pick the highlighted choice.
            if (confirm && !pointer) Choose(selectedChoice);
        }

        private void SelectChoice(int index)
        {
            if (session == null || index < 0 || index >= session.AvailableChoices.Count) return;
            selectedChoice = index;
            view.SetSelected(index);
        }

        private void Choose(int index)
        {
            if (session == null || index < 0 || index >= session.AvailableChoices.Count) return;
            // Selecting can end the dialogue or move to another node; Update re-reads the session afterwards.
            session.SelectChoice(session.AvailableChoices[index].id);
        }

        private void FrameCamera()
        {
            if (lockedMover == null) return;
            Transform subject = portrait.Subject != null ? portrait.Subject : conversationNpc != null ? conversationNpc.transform : null;
            Vector3 player = lockedMover.transform.position;
            Vector3 focus = subject == null ? player : Vector3.Lerp(player, subject.position, 0.5f);
            CameraFollow2_5D.FrameConversation(focus, ConversationZoom);
        }

        private static bool Pressed(Key key)
        {
            Keyboard keyboard = Keyboard.current;
            return keyboard != null && keyboard[key].wasPressedThisFrame;
        }

        /// <summary>The NPC the player just spoke to: the nearest one within talking distance.</summary>
        private DynamicNpc NearestNpc()
        {
            if (lockedMover == null) return null;
            Vector3 player = lockedMover.transform.position;
            return FindObjectsByType<DynamicNpc>(FindObjectsInactive.Exclude)
                .Where(n => Vector3.Distance(n.transform.position, player) <= ConversationNpcRange)
                .OrderBy(n => Vector3.Distance(n.transform.position, player))
                .FirstOrDefault();
        }

        /// <summary>
        /// The speaking NPC for the portrait: a nearby NPC whose name matches the speaker, otherwise the NPC the player
        /// is talking to (generic speakers such as "Student"). The Narrator has no portrait.
        /// </summary>
        private Transform SpeakerTransform(string name)
        {
            if (string.Equals(name, "Narrator", StringComparison.OrdinalIgnoreCase)) return null;
            if (lockedMover != null)
            {
                Vector3 player = lockedMover.transform.position;
                DynamicNpc named = FindObjectsByType<DynamicNpc>(FindObjectsInactive.Exclude)
                    .Where(n => string.Equals(n.DisplayName, name, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(n => Vector3.Distance(n.transform.position, player))
                    .FirstOrDefault();
                if (named != null && Vector3.Distance(named.transform.position, player) <= 8f) return named.transform;
            }

            return conversationNpc != null ? conversationNpc.transform : null;
        }

        private static Texture2D SpeakerFrame(string speaker)
        {
            if (string.Equals(speaker, "Stella", StringComparison.OrdinalIgnoreCase)) return Resources.Load<Texture2D>("Dialogue/Stella");
            if (string.Equals(speaker, "Cinna Cirra", StringComparison.OrdinalIgnoreCase)) return Resources.Load<Texture2D>("Dialogue/CinnaCirra");
            if (string.Equals(speaker, "Orio", StringComparison.OrdinalIgnoreCase)) return Resources.Load<Texture2D>("Dialogue/Orio");
            if (string.Equals(speaker, "Katrina", StringComparison.OrdinalIgnoreCase)) return Resources.Load<Texture2D>("Dialogue/Katrina");
            // Students, the Narrator and any other speaker without a frame share the neutral parchment frame.
            return Resources.Load<Texture2D>("Dialogue/Random NPC");
        }

        private static Color SpeakerColor(string speaker)
        {
            if (string.Equals(speaker, "Stella", StringComparison.OrdinalIgnoreCase)) return new Color(0.76f, 0.22f, 0.30f);
            if (string.Equals(speaker, "Cinna Cirra", StringComparison.OrdinalIgnoreCase)) return new Color(0.30f, 0.48f, 0.72f);
            if (string.Equals(speaker, "Orio", StringComparison.OrdinalIgnoreCase)) return new Color(0.18f, 0.53f, 0.43f);
            if (string.Equals(speaker, "Katrina", StringComparison.OrdinalIgnoreCase)) return new Color(0.64f, 0.33f, 0.58f);
            return new Color(0.42f, 0.36f, 0.30f);
        }
    }
}
