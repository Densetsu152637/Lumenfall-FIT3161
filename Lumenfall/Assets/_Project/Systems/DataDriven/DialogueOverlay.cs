using UnityEngine;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;

namespace Lumenfall.DataDriven
{
    /// Minimal runtime presentation for data-driven dialogue. Actions and quest state remain in GameEventRuntime.
    public sealed class DialogueOverlay : MonoBehaviour
    {
        private DialogueSession session;
        private GUIStyle panelStyle;
        private GUIStyle textStyle;
        private GUIStyle buttonStyle;
        private GridMover lockedMover;

        private void OnEnable()
        {
            GameEventManager.Instance.Runtime.DialogueStarted += OnStarted;
            GameEventManager.Instance.Runtime.DialogueEnded += OnEnded;
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
        }
        private void OnEnded(DialogueSession value)
        {
            if (session != value) return;
            if (lockedMover != null) lockedMover.SetMovementLocked(false);
            lockedMover = null;
            session = null;
        }

        private void OnGUI()
        {
            if (session == null || !session.IsActive || session.CurrentNode == null) return;
            EnsureStyles();
            float width = Mathf.Min(Screen.width - 40f, 760f);
            Rect panel = new Rect((Screen.width - width) * 0.5f, Screen.height - 220f, width, 190f);
            GUILayout.BeginArea(panel, panelStyle);
            string speaker = string.IsNullOrWhiteSpace(session.CurrentNode.speaker) ? session.Definition.id : session.CurrentNode.speaker;
            GUILayout.Label(speaker, textStyle);
            GUILayout.Label(session.CurrentNode.text ?? string.Empty, textStyle);
            if (session.AvailableChoices.Count > 0)
            {
                for (int i = 0; i < session.AvailableChoices.Count; i++)
                {
                    DialogueChoiceDefinition choice = session.AvailableChoices[i];
                    if (GUILayout.Button(choice.text ?? choice.id, buttonStyle)) session.SelectChoice(choice.id);
                }
            }
            else if (GUILayout.Button(string.IsNullOrWhiteSpace(session.CurrentNode.nextNodeId) ? "Close" : "Continue", buttonStyle))
            {
                session.Continue();
            }
            GUILayout.EndArea();
        }

        private void EnsureStyles()
        {
            if (panelStyle != null) return;
            panelStyle = new GUIStyle(GUI.skin.box) { padding = new RectOffset(18, 18, 12, 12) };
            textStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, wordWrap = true, normal = { textColor = Color.white } };
            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 16, wordWrap = true };
        }
    }
}
