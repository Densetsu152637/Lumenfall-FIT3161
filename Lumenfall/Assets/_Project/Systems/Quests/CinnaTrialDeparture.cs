using System;
using UnityEngine;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;

namespace Lumenfall.Quests
{
    /// <summary>
    /// Cinna Cirra heads off to the ice trial at the end of Out of Alignment. Once the relic is on its stand, the player
    /// reports back on the C02 terrace and Cinna asks to meet at the trial; that dialogue node sets
    /// <see cref="DepartureFlag"/>. When the dialogue ends the screen fades to black, the quest completes, and the screen
    /// fades back in with Cinna gone from the terrace: the terrace Cinna hides and the trial Cinna appears in the trial
    /// lobby off screen. The player stays where they are and walks to the trial themselves.
    /// </summary>
    public sealed class CinnaTrialDeparture : MonoBehaviour
    {
        public const string DepartureFlag = "cinna.trial.departure";
        public const string QuestId = "cinna_out_of_alignment";
        public const string CompletionEventId = "complete_cinna_out_of_alignment";
        public const string DialogueId = "cinna_out_of_alignment_dialogue";
        private const float FadeOutSeconds = 0.6f;
        private const float HoldSeconds = 0.35f;
        private const float FadeInSeconds = 0.6f;

        private enum Phase { Idle, FadingOut, Holding, FadingIn }

        private static CinnaTrialDeparture instance;
        private GameEventRuntime subscribedRuntime;
        private Phase phase;
        private float phaseTime;
        private float alpha;

        /// <summary>True from the first frame of the fade-out until the fade-in finishes.</summary>
        public static bool InProgress => instance != null && instance.phase != Phase.Idle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            GameObject host = new GameObject("[Cinna Trial Departure]");
            instance = host.AddComponent<CinnaTrialDeparture>();
            DontDestroyOnLoad(host);
        }

        private void Awake()
        {
            // Drawn with GUI only, never GUILayout, so skip the per-frame layout pass and its allocations.
            useGUILayout = false;
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
        }

        private void OnDestroy()
        {
            Unsubscribe();
            if (instance == this) instance = null;
        }

        private void Update()
        {
            Subscribe();
            Advance(Time.unscaledDeltaTime);
        }

        // Starting a new game rebuilds the event runtime, so follow the live instance.
        private void Subscribe()
        {
            GameEventRuntime runtime = GameEventManager.Instance?.Runtime;
            if (runtime == null || runtime == subscribedRuntime) return;
            Unsubscribe();
            subscribedRuntime = runtime;
            runtime.DialogueEnded += OnDialogueEnded;
        }

        private void Unsubscribe()
        {
            if (subscribedRuntime != null) subscribedRuntime.DialogueEnded -= OnDialogueEnded;
            subscribedRuntime = null;
        }

        private void OnDialogueEnded(DialogueSession session)
        {
            if (phase != Phase.Idle || session == null || !session.IsCompleted ||
                !string.Equals(session.Definition?.id, DialogueId, StringComparison.Ordinal) ||
                subscribedRuntime == null || !subscribedRuntime.GetFlag(DepartureFlag) ||
                subscribedRuntime.GetQuestStatus(QuestId) != QuestStatus.Active) return;
            phase = Phase.FadingOut;
            phaseTime = 0f;
        }

        private void Advance(float deltaTime)
        {
            if (phase == Phase.Idle) return;
            LockPlayer(true);
            phaseTime += deltaTime;
            switch (phase)
            {
                case Phase.FadingOut:
                    alpha = Mathf.Clamp01(phaseTime / FadeOutSeconds);
                    if (alpha < 1f) return;
                    CinnaLeaves();
                    Next(Phase.Holding);
                    break;
                case Phase.Holding:
                    alpha = 1f;
                    if (phaseTime >= HoldSeconds) Next(Phase.FadingIn);
                    break;
                case Phase.FadingIn:
                    alpha = 1f - Mathf.Clamp01(phaseTime / FadeInSeconds);
                    if (alpha > 0f) return;
                    Next(Phase.Idle);
                    LockPlayer(false);
                    break;
            }
        }

        private void Next(Phase next) { phase = next; phaseTime = 0f; }

        // Completing at black swaps the terrace Cinna for the trial Cinna while the screen hides it.
        private void CinnaLeaves()
        {
            GameEventRuntime runtime = subscribedRuntime ?? GameEventManager.Instance?.Runtime;
            if (runtime == null) return;
            runtime.SetFlag(DepartureFlag, false);
            runtime.TriggerEvent(CompletionEventId);
        }

        // Dialogue and menus also lock movement, so the lock is reasserted each frame until the fade-in ends.
        private static void LockPlayer(bool locked)
        {
            GridPlayerInput input = FindAnyObjectByType<GridPlayerInput>();
            GridMover player = input == null ? null : input.GetComponent<GridMover>();
            if (player != null) player.SetMovementLocked(locked);
        }

        private void OnGUI()
        {
            if (alpha <= 0f) return;
            GUI.depth = -1000;
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, alpha);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = previous;
        }
    }
}
