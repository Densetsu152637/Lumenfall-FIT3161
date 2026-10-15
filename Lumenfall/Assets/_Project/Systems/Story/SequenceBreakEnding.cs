using System;
using Lumenfall.Cutscenes;
using Lumenfall.DataDriven;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Inventory;
using Lumenfall.Player;
using Lumenfall.Quests;
using Lumenfall.Startup;
using UnityEngine;

namespace Lumenfall.Story
{
    /// <summary>
    /// What happens if the player solves a Chapter 1 puzzle before Marigold has told them their room is not ready
    /// (<see cref="SettlingInScene.NotReadyFlag"/>), out of the story's order. Once the puzzle's own wrap-up is over, a
    /// glitching voice notices (<see cref="DialogueId"/>), then the screen cuts to black and silence and a voice that
    /// speaks in hands spells out that this story cannot continue. The run is then reset to a new game and written over
    /// its autosave and linked manual slot, and the game returns to the main menu. Loading that file plays the opening
    /// again (<see cref="OpeningPendingFlag"/>), as if the game had never been played.
    /// </summary>
    public sealed class SequenceBreakEnding : MonoBehaviour
    {
        public const string DialogueId = "secret_sequence_break";
        /// <summary>Saved in the reset file: loading it starts the New Game opening.</summary>
        public const string OpeningPendingFlag = "secret.sequence_break.opening_pending";

        // The black screen's lines: when each appears, in seconds, and the text. The first and last are in hands.
        private static readonly (float at, string text, bool hands)[] Lines =
        {
            (1.5f, "☜☝✋☟ ✌☞✋☜ ☟✋✌☝", true),
            (4.5f, "THE ORDER OF EVENTS HAS BEEN BROKEN.", false),
            (8.0f, "A TASK, FINISHED BEFORE IT WAS EVER GIVEN.", false),
            (11.5f, "A ROOM THAT IS NOT YET READY. A STUDENT WHO IS NOT YET EXPECTED.", false),
            (15.5f, "THIS STORY CANNOT CONTINUE FROM HERE.", false),
            (19.0f, "IT WILL HAVE TO BEGIN AGAIN.", false),
            (22.0f, "...BEGIN AGAIN.", false),
            (25.0f, "✋ ☜☟☝✌ ☞✋", true),
        };
        private const float LastLineFadesAt = 28.5f;
        private const float EndsAt = 31f;
        private const float RevealCharactersPerSecond = 18f;

        private static SequenceBreakEnding instance;
        private GameEventRuntime subscribed;
        private bool pending;
        private bool glitchPlayed;
        private bool blackScreen;
        private float blackSince;
        private GridMover lockedMover;
        private GUIStyle lineStyle;
        private GUIStyle handsStyle;
        private bool queuedOpening;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            GameObject host = new GameObject("[Sequence Break]");
            instance = host.AddComponent<SequenceBreakEnding>();
            DontDestroyOnLoad(host);
        }

        /// <summary>True when finishing <paramref name="questId"/> now would break the story's order.</summary>
        public static bool BreaksOrder(GameEventRuntime runtime, string questId) =>
            runtime != null && Array.IndexOf(SettlingInScene.ChapterQuestIds, questId) >= 0 && !runtime.GetFlag(SettlingInScene.NotReadyFlag);

        /// <summary>True while the black screen is up (or about to be).</summary>
        public static bool InProgress => instance != null && (instance.pending || instance.blackScreen);

        private void OnEnable()
        {
            GameSaveManager saves = GameSaveManager.Instance;
            if (saves != null) saves.LoadCompleted += OnLoadCompleted;
        }

        private void OnDisable()
        {
            GameSaveManager saves = GameSaveManager.Instance;
            if (saves != null) saves.LoadCompleted -= OnLoadCompleted;
            Subscribe(null);
        }

        private void Subscribe(GameEventRuntime runtime)
        {
            if (subscribed == runtime) return;
            if (subscribed != null) subscribed.QuestStatusChanged -= OnQuestStatusChanged;
            subscribed = runtime;
            if (runtime != null) runtime.QuestStatusChanged += OnQuestStatusChanged;
        }

        private void OnQuestStatusChanged(QuestDefinition quest, QuestStatus previous, QuestStatus current)
        {
            GameSaveManager saves = GameSaveManager.Instance;
            if (quest == null || current != QuestStatus.Completed || (saves != null && saves.IsRestoring) || blackScreen) return;
            if (BreaksOrder(subscribed, quest.id)) pending = true;
        }

        // A reset file opens on the New Game opening; any other file loaded afterwards does not.
        private void OnLoadCompleted(string slot)
        {
            GameEventRuntime runtime = GameEventManager.Instance != null ? GameEventManager.Instance.Runtime : null;
            if (runtime != null && runtime.GetFlag(OpeningPendingFlag))
            {
                CutscenePlayer.QueueAfterMenu(CutscenePlayer.OpeningCutsceneId);
                queuedOpening = true;
            }
            else if (queuedOpening)
            {
                CutscenePlayer.QueueAfterMenu(null);
                queuedOpening = false;
            }
        }

        private void Update()
        {
            GameEventRuntime runtime = GameEventManager.Instance != null ? GameEventManager.Instance.Runtime : null;
            Subscribe(runtime);
            if (runtime == null) return;
            // The reset file's opening has begun: from here it is an ordinary new game.
            if (runtime.GetFlag(OpeningPendingFlag) && CutscenePlayer.CurrentId == CutscenePlayer.OpeningCutsceneId)
            {
                runtime.SetFlag(OpeningPendingFlag, false);
                queuedOpening = false;
            }

            if (blackScreen)
            {
                if (Time.unscaledTime - blackSince >= EndsAt) Finish();
                return;
            }
            if (!pending || !IsQuiet(runtime)) return;
            if (!glitchPlayed)
            {
                glitchPlayed = true;
                if (runtime.StartDialogue(DialogueId) != null) return;
            }
            BeginBlackScreen();
        }

        private void BeginBlackScreen()
        {
            blackScreen = true;
            blackSince = Time.unscaledTime;
            // Silence: every sound stops at once.
            AudioListener.pause = true;
            OverlayInputGate.Acquire(this);
            lockedMover = GridMover.FindActive();
            if (lockedMover != null) lockedMover.SetMovementLocked(this, true);
        }

        private void Finish()
        {
            blackScreen = false;
            pending = false;
            glitchPlayed = false;
            if (lockedMover != null) lockedMover.SetMovementLocked(this, false);
            lockedMover = null;
            OverlayInputGate.Release(this);
            AudioListener.pause = false;
            ResetRun();
            StartupMenu.ReturnToMainMenu();
        }

        /// <summary>
        /// Starts the run over as New Game does, keeps its manual slot, and saves that fresh start over the autosave and
        /// the manual slot, so the file the player had now holds the very beginning.
        /// </summary>
        private void ResetRun()
        {
            GameSaveManager saves = GameSaveManager.Instance;
            if (saves == null) return;
            string linked = ManualSaveSlot.Linked;
            if (!saves.ResetState(() =>
            {
                GameEventManager.Instance.ReloadDefinitions();
                DataDrivenWorldLoader loader = DataDrivenWorldLoader.Instance;
                if (loader != null) loader.ResetToStartingState();
                PlayerProfile.Forget();
            }, resumeAutosave: true))
            {
                Debug.LogWarning("The sequence-break reset could not reset the game: " + saves.LastError);
                return;
            }
            GameEventRuntime runtime = GameEventManager.Instance.Runtime;
            Subscribe(runtime);
            ManualSaveSlot.Link(linked);
            runtime.SetFlag(OpeningPendingFlag, true);
            SaveSlotSummary.BeginNewGame();
            if (!saves.Save(GameSaveManager.AutosaveSlot)) Debug.LogWarning("The sequence-break reset could not write the autosave: " + saves.LastError);
            if (linked != null && !saves.Save(linked)) Debug.LogWarning($"The sequence-break reset could not write {linked}: " + saves.LastError);
            // Nothing has been played since this fresh start, so returning to the menu does not save again.
            GameAutosave.RestartTimer();
        }

        private void OnGUI()
        {
            if (!blackScreen) return;
            int previousDepth = GUI.depth;
            GUI.depth = -3000;
            Color previous = GUI.color;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            if (lineStyle == null)
            {
                lineStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 26, wordWrap = true };
                handsStyle = new GUIStyle(lineStyle) { fontSize = 34 };
            }
            float elapsed = Time.unscaledTime - blackSince;
            // One line at a time, typed out slowly, trembling slightly, and gone before the next.
            for (int i = 0; i < Lines.Length; i++)
            {
                float start = Lines[i].at;
                float end = i + 1 < Lines.Length ? Lines[i + 1].at - 0.4f : LastLineFadesAt;
                if (elapsed < start || elapsed > end) continue;
                string text = Lines[i].text;
                int shown = Lines[i].hands ? text.Length : Mathf.Min(text.Length, Mathf.FloorToInt((elapsed - start) * RevealCharactersPerSecond));
                float alpha = Mathf.Clamp01((end - elapsed) / 0.6f) * Mathf.Clamp01((elapsed - start) / 0.4f);
                GUI.color = new Color(1f, 1f, 1f, alpha);
                Vector2 tremble = new Vector2(UnityEngine.Random.Range(-1.2f, 1.2f), UnityEngine.Random.Range(-1.2f, 1.2f));
                Rect rect = new Rect(Screen.width * 0.1f + tremble.x, Screen.height * 0.5f - 60f + tremble.y, Screen.width * 0.8f, 120f);
                GUI.Label(rect, text.Substring(0, shown), Lines[i].hands ? handsStyle : lineStyle);
            }
            GUI.color = previous;
            GUI.depth = previousDepth;
        }

        // Nothing else on screen, so the glitch follows the puzzle's own wrap-up rather than interrupting it.
        private static bool IsQuiet(GameEventRuntime runtime) =>
            !CutscenePlayer.IsPlaying && !StartupMenu.IsOpen && Time.timeScale > 0f && runtime.ActiveDialogue == null &&
            !CinnaTrialDeparture.InProgress && !PlayerNamePrompt.IsOpen && !InventoryUiState.IsOpen &&
            DataDrivenWorldLoader.Instance != null && DataDrivenWorldLoader.Instance.CurrentAreaRoot != null;
    }
}
