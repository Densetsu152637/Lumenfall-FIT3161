using System;
using System.Collections;
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
    /// voice notices. A still of the game is taken, then the screen cuts to black and silence and a voice that speaks in
    /// hands (the real Wingdings font, <see cref="WingdingsText"/>) has its say, ending in English. The still comes back,
    /// is pressed between two pale hands reaching in from either side; it cracks as they squeeze, and as they meet its glass
    /// bursts out from between the palms and falls away in pieces before the game closes (<see cref="ScreenCrush"/>; without the hand model it cracks and
    /// shatters instead, <see cref="ScreenShatter"/>).
    /// The moment the screen goes black, the run is reset to a new game and written over the autosave and the run's linked
    /// manual slot, and the game is locked out (<see cref="SequenceBreakLockout"/>), so closing it
    /// early escapes nothing. Every launch after that opens on an empty screen until Miki lets the player back in. Loading
    /// the reset autosave then plays the opening again (<see cref="OpeningPendingFlag"/>), as if the game had never been
    /// played.
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

        /// <summary>How a black-screen line is shown.</summary>
        internal enum LineStyle
        {
            /// <summary>English, one letter at a time, slowly.</summary>
            Slow,
            /// <summary>Written in English, drawn in Wingdings, all at once.</summary>
            Hands,
            /// <summary>English, typed out.</summary>
            Typed,
            /// <summary>English, one letter at a time, each letter shaking on its own.</summary>
            Shaky,
        }

        // The black screen's lines: when each appears, in seconds, the text, and how it is shown. The same for either cause.
        internal static readonly (float at, string text, LineStyle style)[] Lines =
        {
            (1.5f, "Interesting.", LineStyle.Slow),
            (6.5f, "WE HAVE BEEN HERE BEFORE, HAVEN'T WE?", LineStyle.Hands),
            (10.0f, "I KNOW WHAT YOU DID.", LineStyle.Hands),
            (13.5f, "YOU ENJOY THIS, DON'T YOU?", LineStyle.Hands),
            (17.0f, "A CRACK IS A REMARKABLE THING ISNT IT", LineStyle.Hands),
            (20.5f, "NO MATTER HOW MUCH I CHANGE THIS WORLD", LineStyle.Hands),
            (24.0f, "YOU WILL ALWAYS BREAK IT", LineStyle.Hands),
            (27.5f, "TIME TO STOP BEING MYSTERIOUS", LineStyle.Hands),
            (31.0f, "TIME FOR ME TO HAVE SOME FUN", LineStyle.Typed),
            (35.0f, "BREAKING YOU", LineStyle.Shaky),
        };
        internal const float LastLineFadesAt = 39.1f;
        /// <summary>When the still of the game comes back and breaks.</summary>
        internal const float ShatterAt = 39.5f;

        /// <summary>Letters per second for each style (Hands lines appear whole).</summary>
        internal static float RevealRate(LineStyle style) => style switch
        {
            LineStyle.Slow => 4f,
            LineStyle.Typed => 18f,
            LineStyle.Shaky => 5f,
            _ => float.PositiveInfinity,
        };

        private static SequenceBreakEnding instance;
        private GameEventRuntime subscribed;
        private bool pending;
        private Cause cause;
        private bool glitchPlayed;
        private bool capturing;
        private bool blackScreen;
        private bool runReset;
        private float blackSince;
        private GridMover lockedMover;
        private Texture2D still;
        private IEndingEffect shatter;
        private GUIStyle lineStyle;
        private GUIStyle handsStyle;
        private Font handsFont;
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
        /// introduction, so meeting her counts. The dog's hints are the gate's own notices, so it has none. The forgotten
        /// (egg) room is deliberately outside the sequence break: Miki's white-dog story is only a loose hint, and stumbling
        /// onto the trail by chance must never end the game.
        /// </summary>
        public static string[] HintsFor(string secret) => secret switch
        {
            SecretRooms.DevRoomId => new[] { "npc.met.stella" },
            LanternBearerSecretId => new[] { "hint.heard.lantern_shape", "hint.heard.lantern_place" },
            SecretRooms.ClosetId => new[] { "hint.heard.closet_gossip", "hint.heard.closet_shelf" },
            SecretRooms.DogRoomId => Array.Empty<string>(),
            SecretRooms.ForgottenRoomId => Array.Empty<string>(),
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
            if (instance == null || why == Cause.None || instance.blackScreen || instance.pending || SequenceBreakLockout.InProgress) return;
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
                if (!runReset)
                {
                    // The run is erased and the game locked out as soon as the screen is black, so closing the game
                    // during it escapes nothing.
                    runReset = true;
                    ResetRun();
                    SequenceBreakLockout.Arm();
                }
                // Time stands still behind the black, so the fresh world cannot play its own scenes or autosave over the
                // reset. Everything here runs on unscaled time.
                Time.timeScale = 0f;
                float elapsed = Time.unscaledTime - blackSince;
                if (elapsed >= ShatterAt && still != null)
                {
                    if (shatter == null) shatter = (IEndingEffect)ScreenCrush.TryCreate(still) ?? new ScreenShatter(still);
                    shatter.Tick(elapsed - ShatterAt);
                }
                // The game closes when the effect is done: for the hands, the instant the game breaks between them.
                if (elapsed >= ShatterAt + (shatter != null ? shatter.Duration : ScreenShatter.Duration)) Finish();
                return;
            }
            if (capturing) return;
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
            capturing = true;
            OverlayInputGate.Acquire(this);
            lockedMover = GridMover.FindActive();
            if (lockedMover != null) lockedMover.SetMovementLocked(this, true);
            StartCoroutine(CaptureThenGoBlack());
        }

        // The game as it was the moment the voice fell silent: the picture crushed at the end.
        private IEnumerator CaptureThenGoBlack()
        {
            yield return new WaitForEndOfFrame();
            try { still = ScreenCapture.CaptureScreenshotAsTexture(); }
            catch (Exception exception) { Debug.LogWarning("The sequence break could not capture the screen: " + exception.Message); }
            capturing = false;
            blackScreen = true;
            blackSince = Time.unscaledTime;
            // Silence: every sound stops at once.
            AudioListener.pause = true;
        }

        private void Finish()
        {
            blackScreen = false;
            runReset = false;
            pending = false;
            glitchPlayed = false;
            cause = Cause.None;
            shatter?.Dispose();
            shatter = null;
            if (still != null) Destroy(still);
            still = null;
            if (lockedMover != null) lockedMover.SetMovementLocked(this, false);
            lockedMover = null;
            OverlayInputGate.Release(this);
            SequenceBreakLockout.QuitGame();
        }

        /// <summary>
        /// Starts the run over as New Game does, keeps its manual slot, and saves that fresh start over the autosave and
        /// the manual slot the run came from (<see cref="ManualSaveSlot.Linked"/>, or <see cref="ManualSaveSlot.Origin"/>
        /// for a run continued from the autosave), so the file the player had now holds the very beginning. This is the one
        /// deliberate exception to automatic saving never writing a manual slot: the story erasing itself is the point. A
        /// run that never had a slot has only the autosave to erase.
        /// </summary>
        private void ResetRun()
        {
            GameSaveManager saves = GameSaveManager.Instance;
            if (saves == null) return;
            string linked = ManualSaveSlot.Linked;
            // The file the player came from: the linked slot, or, for a run continued from the autosave (which drops the
            // Quick Save link), the slot that autosave came from.
            string erased = linked ?? ManualSaveSlot.Origin;
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
            // The fresh start still belongs to that file, so breaking the story again erases the same one.
            ManualSaveSlot.SetOrigin(erased);
            runtime.SetFlag(OpeningPendingFlag, true);
            SaveSlotSummary.BeginNewGame();
            if (!saves.Save(GameSaveManager.AutosaveSlot)) Debug.LogWarning("The sequence-break reset could not write the autosave: " + saves.LastError);
            if (erased != null)
            {
                if (saves.Save(erased)) SaveSlotSummary.RequestThumbnailRefresh(erased);
                else Debug.LogWarning($"The sequence-break reset could not write {erased}: " + saves.LastError);
            }
            // Nothing has been played since this fresh start, so returning to the menu does not save again.
            GameAutosave.RestartTimer();
        }

        // Drawn with GUI only, never GUILayout, so skip the per-frame layout pass and its allocations.
        private void Awake() => useGUILayout = false;

        private void OnGUI()
        {
            using (UiScale.Begin()) DrawGui();
        }

        private void DrawGui()
        {
            if (!blackScreen) return;
            int previousDepth = GUI.depth;
            GUI.depth = -3000;
            Color previous = GUI.color;
            float elapsed = Time.unscaledTime - blackSince;
            if (elapsed >= ShatterAt && shatter != null)
            {
                if (Event.current.type == EventType.Repaint) shatter.Draw(elapsed - ShatterAt);
                GUI.depth = previousDepth;
                return;
            }
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0f, 0f, UiScale.Width, UiScale.Height), Texture2D.whiteTexture);
            EnsureStyles();
            // One line at a time, trembling slightly, and gone before the next.
            for (int i = 0; i < Lines.Length; i++)
            {
                float start = Lines[i].at;
                float end = i + 1 < Lines.Length ? Lines[i + 1].at - 0.4f : LastLineFadesAt;
                if (elapsed < start || elapsed > end) continue;
                string text = Lines[i].text;
                LineStyle style = Lines[i].style;
                float rate = RevealRate(style);
                int shown = float.IsInfinity(rate) ? text.Length : Mathf.Min(text.Length, Mathf.FloorToInt((elapsed - start) * rate));
                float alpha = Mathf.Clamp01((end - elapsed) / 0.6f) * Mathf.Clamp01((elapsed - start) / 0.4f);
                GUI.color = new Color(1f, 1f, 1f, alpha);
                Rect rect = new Rect(UiScale.Width * 0.1f, UiScale.Height * 0.5f - 60f, UiScale.Width * 0.8f, 120f);
                if (style == LineStyle.Shaky)
                {
                    DrawShaking(rect, text, shown);
                    continue;
                }
                rect.position += new Vector2(UnityEngine.Random.Range(-1.2f, 1.2f), UnityEngine.Random.Range(-1.2f, 1.2f));
                if (style == LineStyle.Hands)
                    GUI.Label(rect, handsFont != null ? WingdingsText.ForFont(handsFont, text) : WingdingsText.AsSymbols(text), handsStyle);
                else GUI.Label(rect, text.Substring(0, shown), lineStyle);
            }
            GUI.color = previous;
            GUI.depth = previousDepth;
        }

        // Each letter where it would sit in the whole line, shaking on its own.
        private void DrawShaking(Rect rect, string text, int shown)
        {
            float total = lineStyle.CalcSize(new GUIContent(text)).x;
            float left = rect.center.x - total * 0.5f;
            float height = lineStyle.CalcSize(new GUIContent("X")).y;
            for (int c = 0; c < shown; c++)
            {
                if (text[c] == ' ') continue;
                float x = left + lineStyle.CalcSize(new GUIContent(text.Substring(0, c))).x - lineStyle.padding.horizontal;
                Vector2 shake = new Vector2(UnityEngine.Random.Range(-3f, 3f), UnityEngine.Random.Range(-3f, 3f));
                GUIContent letter = new GUIContent(text[c].ToString());
                float width = lineStyle.CalcSize(letter).x;
                GUI.Label(new Rect(x + shake.x, rect.center.y - height * 0.5f + shake.y, width + 2f, height), letter, lineStyle);
            }
        }

        private void EnsureStyles()
        {
            if (lineStyle != null) return;
            lineStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 26, wordWrap = true };
            handsFont = WingdingsText.Font;
            handsStyle = new GUIStyle(lineStyle) { fontSize = 30 };
            if (handsFont != null) handsStyle.font = handsFont;
        }

        // Nothing else on screen, so the glitch follows the puzzle's own wrap-up rather than interrupting it.
        private static bool IsQuiet(GameEventRuntime runtime) =>
            !BuildingSignInteraction.IsEInteractionBlocked && !CutscenePlayer.IsPlaying && !StartupMenu.IsOpen && Time.timeScale > 0f && runtime.ActiveDialogue == null &&
            !CinnaTrialDeparture.InProgress && !PlayerNamePrompt.IsOpen && !InventoryUiState.IsOpen &&
            DataDrivenWorldLoader.Instance != null && DataDrivenWorldLoader.Instance.CurrentAreaRoot != null;
    }
}
