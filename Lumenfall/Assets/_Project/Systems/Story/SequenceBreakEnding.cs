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
    /// What happens if the player breaks the story's order in one of two obscure ways (<see cref="Cause"/>): finishing a
    /// secret without having heard the hints that lead to it (<see cref="KnowsTooMuch"/>; only someone who has played
    /// before would know to), or solving all six Chapter 1 puzzles before ever setting foot in the dormitory
    /// (<see cref="SolvedEverythingFirst"/>). Solving one puzzle early is fine. Once nothing else is on screen, a glitching
    /// voice notices, then the screen cuts to black and silence and a voice that speaks in hands spells out that this
    /// story cannot continue. The run is then reset to a new game and written over
    /// its autosave and linked manual slot, and the game returns to the main menu. Loading that file plays the opening
    /// again (<see cref="OpeningPendingFlag"/>), as if the game had never been played.
    /// </summary>
    public sealed class SequenceBreakEnding : MonoBehaviour
    {
        /// <summary>The two ways to break the story's order.</summary>
        public enum Cause { None, Knowledge, Everything }

        public const string DialogueId = "secret_sequence_break";
        public const string KnowledgeDialogueId = "secret_sequence_break_knowledge";
        /// <summary>Set the first time the player is in the dormitory, whatever they do there.</summary>
        public const string DormVisitedFlag = "story.dorm.visited";
        /// <summary>Saved in the reset file: loading it starts the New Game opening.</summary>
        public const string OpeningPendingFlag = "secret.sequence_break.opening_pending";

        // The black screen's lines: when each appears, in seconds, and the text. The first and last are in hands; the
        // second and third depend on how the order was broken.
        private static readonly (float at, string text, bool hands)[] Lines =
        {
            (1.5f, "☜☝✋☟ ✌☞✋☜ ☟✋✌☝", true),
            (4.5f, "THE ORDER OF EVENTS HAS BEEN BROKEN.", false),
            (8.0f, "{0}", false),
            (11.5f, "{1}", false),
            (15.5f, "THIS STORY CANNOT CONTINUE FROM HERE.", false),
            (19.0f, "IT WILL HAVE TO BEGIN AGAIN.", false),
            (22.0f, "...BEGIN AGAIN.", false),
            (25.0f, "✋ ☜☟☝✌ ☞✋", true),
        };
        private static readonly string[] KnowledgeLines =
        {
            "A SECRET, OPENED BEFORE IT COULD BE KNOWN.",
            "YOU HAVE WALKED THIS PATH BEFORE. YOU REMEMBER WHAT YOU SHOULD NOT.",
        };
        private static readonly string[] EverythingLines =
        {
            "EVERY TASK, FINISHED BEFORE ANY WAS GIVEN.",
            "A ROOM THAT WAS NEVER VISITED. A STUDENT WHO WAS NEVER EXPECTED.",
        };
        private const float LastLineFadesAt = 28.5f;
        private const float EndsAt = 31f;
        private const float RevealCharactersPerSecond = 18f;

        private static SequenceBreakEnding instance;
        private GameEventRuntime subscribed;
        private bool pending;
        private Cause cause;
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

        /// <summary>The Lantern Bearer's id here, alongside the secret rooms' map ids.</summary>
        public const string LanternBearerSecretId = "secret.lanternBearer";

        /// <summary>
        /// The hints a secret is found through, as the flags their lines set. Stella's knock hint is part of her
        /// introduction, so meeting her counts. The dog's hints are the gate's own notices, so it has none.
        /// </summary>
        public static string[] HintsFor(string secret) => secret switch
        {
            SecretRooms.DevRoomId => new[] { "npc.met.stella" },
            LanternBearerSecretId => new[] { "hint.heard.lantern_shape", "hint.heard.lantern_place" },
            SecretRooms.ClosetId => new[] { "hint.heard.closet_gossip", "hint.heard.closet_shelf" },
            _ => Array.Empty<string>(),
        };

        /// <summary>
        /// True when finishing <paramref name="secret"/> now would mean knowing it from another run: the player has not
        /// heard every hint that leads to it.
        /// </summary>
        public static bool KnowsTooMuch(GameEventRuntime runtime, string secret)
        {
            if (runtime == null) return false;
            foreach (string hint in HintsFor(secret)) if (!runtime.GetFlag(hint)) return true;
            return false;
        }

        /// <summary>True once the player has been in the dormitory (older saves count Marigold's or the desk's scene).</summary>
        public static bool VisitedDorm(GameEventRuntime runtime) =>
            runtime != null && (runtime.GetFlag(DormVisitedFlag) || runtime.GetFlag(SettlingInScene.NotReadyFlag) || runtime.GetFlag(SettlingInScene.NoRoomsFlag));

        /// <summary>True when every Chapter 1 puzzle is solved and the player has never been to the dormitory.</summary>
        public static bool SolvedEverythingFirst(GameEventRuntime runtime)
        {
            if (runtime == null || VisitedDorm(runtime)) return false;
            foreach (string quest in SettlingInScene.ChapterQuestIds)
                if (runtime.GetQuestStatus(quest) != QuestStatus.Completed) return false;
            return true;
        }

        /// <summary>Breaks the story for <paramref name="why"/>: the glitch plays once nothing else is on screen.</summary>
        public static void Begin(Cause why)
        {
            if (instance == null || why == Cause.None || instance.blackScreen || instance.pending) return;
            instance.cause = why;
            instance.pending = true;
        }

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
            if (Array.IndexOf(SettlingInScene.ChapterQuestIds, quest.id) >= 0 && SolvedEverythingFirst(subscribed)) Begin(Cause.Everything);
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

            // Being in the dormitory at all rules out solving everything first.
            DataDrivenWorldLoader loader = DataDrivenWorldLoader.Instance;
            if (loader != null && loader.LoadedMap != null && loader.LoadedMap.id == SettlingInScene.MapId && !runtime.GetFlag(DormVisitedFlag))
                runtime.SetFlag(DormVisitedFlag, true);

            if (blackScreen)
            {
                if (Time.unscaledTime - blackSince >= EndsAt) Finish();
                return;
            }
            if (!pending || !IsQuiet(runtime)) return;
            if (!glitchPlayed)
            {
                glitchPlayed = true;
                if (runtime.StartDialogue(cause == Cause.Knowledge ? KnowledgeDialogueId : DialogueId) != null) return;
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
            cause = Cause.None;
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
                string[] causeLines = cause == Cause.Knowledge ? KnowledgeLines : EverythingLines;
                string text = Lines[i].text == "{0}" ? causeLines[0] : Lines[i].text == "{1}" ? causeLines[1] : Lines[i].text;
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
            !BuildingSignInteraction.IsEInteractionBlocked && !CutscenePlayer.IsPlaying && !StartupMenu.IsOpen && Time.timeScale > 0f && runtime.ActiveDialogue == null &&
            !CinnaTrialDeparture.InProgress && !PlayerNamePrompt.IsOpen && !InventoryUiState.IsOpen &&
            DataDrivenWorldLoader.Instance != null && DataDrivenWorldLoader.Instance.CurrentAreaRoot != null;
    }
}
