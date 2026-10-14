using UnityEngine;
using UnityEngine.InputSystem;
using Lumenfall.Controls;
using Lumenfall.DataDriven;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Player;
using Lumenfall.Startup;

namespace Lumenfall.Cutscenes
{
    /// <summary>
    /// Plays authored cutscenes from <c>Resources/Cutscenes</c>. While one plays, the player is locked, other gameplay
    /// input is blocked (<see cref="IsPlaying"/>), and this draws the screen overlay, captions and skip hint above every
    /// other overlay. Escape twice skips it unless it is unskippable. Cutscenes start from <see cref="Play"/>, from a
    /// queued request once the startup menu has closed (<see cref="QueueAfterMenu"/>, used by New Game for the opening),
    /// or from the GameEvents action <c>{"type": "playcutscene", "key": "&lt;id&gt;"}</c>. A queued cutscene with an
    /// <c>arrive</c> step takes over the menu's closing dissolve (<see cref="TakeOverMenuDissolve"/>), so the panel's
    /// light streams into the player's arrival.
    /// </summary>
    // Runs before player input, so a cutscene that starts as the menu closes locks movement before the first step is read.
    [DefaultExecutionOrder(-500)]
    public sealed class CutscenePlayer : MonoBehaviour, ICutsceneHost
    {
        public const string OpeningCutsceneId = "opening_arrival";
        public const string PlayActionType = "playcutscene";
        private const float SkipConfirmSeconds = 2.5f;
        private static readonly Color Cream = new Color(1f, 0.96f, 0.88f);

        private static CutscenePlayer instance;
        private static string queuedId;
        private static int endedFrame = -1;

        private GameEventRuntime subscribedRuntime;
        private CutsceneSequence sequence;
        private FallingStarEffect star;
        private PlayerArrivalEffect arrival;
        private CutsceneWalk walk;
        private bool cameraHeld;
        private Vector3 cameraOffset;
        private float cameraZoom = 1f;
        private float skipArmedUntil = float.NegativeInfinity;
        private GUIStyle captionStyle, hintStyle;
        private int styledHeight;

        /// <summary>True while a cutscene is playing; gameplay input, the journal, the pause menu and autosave wait for it.</summary>
        public static bool IsPlaying => instance != null && instance.sequence != null;

        /// <summary>
        /// True while a cutscene plays and on the frame it ends, so the Escape press that skips it is not also taken as
        /// a request for the pause menu by an overlay that updates later in the same frame.
        /// </summary>
        public static bool BlocksInput => IsPlaying || endedFrame == Time.frameCount;

        /// <summary>The id of the cutscene playing now, or null.</summary>
        public static string CurrentId => IsPlaying ? instance.sequence.Definition.id : null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
            queuedId = null;
            endedFrame = -1;
            DeclareActionType();
        }

        // Also declared after every editor script reload, since SubsystemRegistration only runs when Play Mode starts.
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        private static void DeclareActionType() => GameEventCatalog.DeclareCustomActionType(PlayActionType);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            GameObject host = new GameObject("[Cutscene Player]");
            instance = host.AddComponent<CutscenePlayer>();
            DontDestroyOnLoad(host);
        }

        /// <summary>Plays <paramref name="cutsceneId"/> as soon as the startup menu has closed.</summary>
        public static void QueueAfterMenu(string cutsceneId) => queuedId = cutsceneId;

        /// <summary>
        /// Starts a cutscene now. Returns false if another is playing, it cannot be loaded, or it is a play-once cutscene
        /// that has already played in this game.
        /// </summary>
        public static bool Play(string cutsceneId) => instance != null && instance.PlayNow(cutsceneId);

        /// <summary>
        /// Called as the startup menu begins to close. If the queued cutscene has an <c>arrive</c> step, the player is
        /// hidden and the arrival's sparks start across <paramref name="panel"/> (screen pixels) in place of the menu's own
        /// dissolve. Returns false, so the menu dissolves as usual, when there is nothing to take over.
        /// </summary>
        public static bool TakeOverMenuDissolve(Rect panel)
        {
            if (instance == null || queuedId == null || instance.arrival != null) return false;
            CutsceneDefinition definition = CutsceneDefinition.Load(queuedId);
            if (definition == null || !System.Array.Exists(definition.steps, step => step.Kind == CutsceneStepKind.Arrive)) return false;
            GridMover player = Player();
            if (player == null) return false;
            instance.arrival = PlayerArrivalEffect.Begin(player.transform, panel);
            return instance.arrival != null;
        }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
        }

        private void OnDestroy()
        {
            if (instance != this) return;
            if (sequence != null) Finish();
            instance = null;
        }

        private bool PlayNow(string cutsceneId)
        {
            if (sequence != null)
            {
                Debug.LogWarning($"Cutscene '{cutsceneId}' was not started because '{sequence.Definition.id}' is playing.");
                return false;
            }

            CutsceneDefinition definition = CutsceneDefinition.Load(cutsceneId);
            if (definition == null) return false;
            if (definition.once && Runtime != null && Runtime.GetFlag(definition.PlayedFlag)) return false;
            sequence = new CutsceneSequence(definition, this);
            skipArmedUntil = float.NegativeInfinity;
            // Halt the player at once, so a held movement key cannot carry them a step further into the scene.
            LockPlayer(true);
            return true;
        }

        private void Update()
        {
            Subscribe();
            if (sequence == null && queuedId != null && !StartupMenu.IsOpen && Player() != null)
            {
                string id = queuedId;
                queuedId = null;
                PlayNow(id);
            }
            if (sequence == null) return;

            // A walk moves the player itself; otherwise they are held still.
            if (walk != null && !walk.IsFinished) walk.Tick();
            else LockPlayer(true);
            HandleSkipInput();
            if (sequence == null) return;
            sequence.Advance(Time.unscaledDeltaTime);
            HoldCamera();
            Shake();
            if (sequence.IsFinished) Finish();
        }

        private void HandleSkipInput()
        {
            Keyboard keyboard = Keyboard.current;
            if (!sequence.CanSkip || keyboard == null || !keyboard.escapeKey.wasPressedThisFrame) return;
            if (Time.unscaledTime <= skipArmedUntil)
            {
                sequence.Skip();
                Finish();
                return;
            }
            skipArmedUntil = Time.unscaledTime + SkipConfirmSeconds;
        }

        private void Finish()
        {
            CutsceneDefinition definition = sequence.Definition;
            sequence = null;
            endedFrame = Time.frameCount;
            if (definition.once) Runtime?.SetFlag(definition.PlayedFlag, true);
            StopStar();
            walk?.Arrive();
            walk = null;
            // A finished arrival keeps its rising motes going on its own; it only has to have shown the player.
            if (arrival != null && !arrival.IsRevealed) arrival.Reveal();
            arrival = null;
            ReleaseCamera();
            CameraFollow2_5D.SetShake(Vector3.zero);
            LockPlayer(false);
        }

        // Starting a new game rebuilds the event runtime, so the action handler is registered on each live instance.
        private void Subscribe()
        {
            GameEventRuntime runtime = GameEventManager.Instance?.Runtime;
            if (runtime == null || runtime == subscribedRuntime) return;
            subscribedRuntime = runtime;
            runtime.RegisterActionHandler(PlayActionType, (action, _) => PlayNow(action.key));
        }

        private static GameEventRuntime Runtime => GameEventManager.Instance?.Runtime;

        private static GridMover Player()
        {
            GridPlayerInput input = FindAnyObjectByType<GridPlayerInput>();
            return input == null ? null : input.GetComponent<GridMover>();
        }

        // Dialogue also locks and then unlocks movement, so the lock is reasserted every frame until the cutscene ends.
        private static void LockPlayer(bool locked)
        {
            GridMover player = Player();
            if (player != null) player.SetMovementLocked(locked);
        }

        // An open dialogue frames its own speakers; the cutscene's framing resumes once it closes.
        private void HoldCamera()
        {
            if (!cameraHeld || Runtime?.ActiveDialogue != null) return;
            GridMover player = Player();
            if (player != null) CameraFollow2_5D.FrameConversation(player.transform.position + cameraOffset, cameraZoom);
        }

        private void Shake()
        {
            float strength = sequence == null || GameSettingsPanel.ReduceMotion ? 0f : sequence.ShakeStrength;
            Camera view = Camera.main;
            if (strength <= 0f || view == null)
            {
                CameraFollow2_5D.SetShake(Vector3.zero);
                return;
            }
            float time = Time.unscaledTime * 28f;
            Vector3 offset = view.transform.right * (Mathf.PerlinNoise(time, 0.3f) * 2f - 1f) +
                view.transform.up * (Mathf.PerlinNoise(0.7f, time) * 2f - 1f);
            CameraFollow2_5D.SetShake(offset * strength);
        }

        // ---- ICutsceneHost ----

        public void SetFlag(string key, bool value) => Runtime?.SetFlag(key, value);

        public bool StartDialogue(string dialogueId) => Runtime?.StartDialogue(dialogueId) != null;

        public bool IsDialogueActive => Runtime?.ActiveDialogue != null;

        public void EndDialogue() => Runtime?.ActiveDialogue?.End();

        public void FocusCamera(Vector3 offset, float zoom)
        {
            cameraHeld = true;
            cameraOffset = offset;
            cameraZoom = zoom;
            HoldCamera();
        }

        public void ReleaseCamera()
        {
            if (!cameraHeld) return;
            cameraHeld = false;
            if (Runtime?.ActiveDialogue == null) CameraFollow2_5D.ClearConversationFrame();
        }

        public void StartStar(float seconds)
        {
            StopStar();
            GridMover player = Player();
            if (player != null) star = FallingStarEffect.Launch(player.transform, seconds);
        }

        public bool StartArrival()
        {
            if (arrival == null)
            {
                GridMover player = Player();
                if (player != null) arrival = PlayerArrivalEffect.Begin(player.transform, null);
            }
            return arrival != null;
        }

        public bool IsArrivalGathering => arrival != null && arrival.IsGathering;

        public void RevealPlayer()
        {
            if (arrival != null) arrival.Reveal();
        }

        public bool StartNamePrompt() => PlayerNamePrompt.Open();

        public bool IsNamePromptOpen => PlayerNamePrompt.IsOpen;

        public bool StartWalk(Vector2Int cell, bool hurry, string npcId)
        {
            walk?.Arrive();
            walk = null;
            GridMover player = Player();
            if (player == null || player.World == null) return false;
            Vector2Int target = cell;
            if (!string.IsNullOrEmpty(npcId))
            {
                // Measured from where the NPC actually stands, which can differ from their catalog cell.
                DynamicNpc npc = FindNpc(npcId);
                if (npc == null)
                {
                    Debug.LogWarning($"Cutscene '{CurrentId}' cannot walk to NPC '{npcId}': they are not in this area.");
                    return false;
                }
                target = player.World.WorldToCell(npc.transform.position) + cell;
            }
            walk = new CutsceneWalk(player, target, hurry);
            return true;
        }

        private static DynamicNpc FindNpc(string npcId)
        {
            foreach (DynamicNpc npc in FindObjectsByType<DynamicNpc>(FindObjectsInactive.Exclude))
                if (npc != null && npc.Id == npcId) return npc;
            return null;
        }

        public bool IsWalking => walk != null && !walk.IsFinished;

        public bool EnterPortal(Vector2Int cell)
        {
            GridMover player = Player();
            GridPortal portal = player == null || player.World == null ? null : player.World.GetPortalAt(cell);
            if (portal == null)
            {
                Debug.LogWarning($"Cutscene '{CurrentId}' found no portal at {cell} to enter.");
                return false;
            }
            walk?.Arrive();
            walk = null;
            if (!portal.ContainsCell(player.CurrentCell)) player.TryTeleportTo(player.World, cell);
            // Portals refuse a locked player; the lock returns on the next frame.
            player.SetMovementLocked(false);
            bool entered = portal.TryEnter(player);
            player.SetMovementLocked(true);
            return entered;
        }

        public void StopEffects()
        {
            // Skipping past the question keeps what was typed, or gives the default name.
            if (PlayerNamePrompt.IsOpen) PlayerNamePrompt.Close();
            walk?.Arrive();
            walk = null;
            StopStar();
            if (arrival != null) arrival.Stop();
            arrival = null;
        }

        private void StopStar()
        {
            if (star != null) star.Stop();
            star = null;
        }

        // ---- Drawing ----

        private void OnGUI()
        {
            if (sequence == null) return;
            // IMGUI draws lower depths in front, so this covers the hint card, the spell chip and the Settings button.
            GUI.depth = -1000;
            Color previous = GUI.color;
            EnsureStyles();

            if (sequence.OverlayAlpha > 0f)
            {
                Color overlay = sequence.OverlayColor;
                GUI.color = new Color(overlay.r, overlay.g, overlay.b, sequence.OverlayAlpha);
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            }

            if (!string.IsNullOrEmpty(sequence.Caption) && sequence.CaptionAlpha > 0f)
            {
                float width = Mathf.Min(Screen.width - 64f, Screen.height * 0.95f);
                Rect rect = new Rect((Screen.width - width) / 2f, Screen.height * 0.42f, width, Screen.height * 0.2f);
                GUI.color = new Color(1f, 1f, 1f, sequence.CaptionAlpha);
                GUI.Label(rect, sequence.Caption, captionStyle);
            }

            if (sequence.CanSkip)
            {
                bool armed = Time.unscaledTime <= skipArmedUntil;
                GUI.color = new Color(1f, 1f, 1f, armed ? 0.9f : 0.35f);
                Rect hint = new Rect(Screen.width - 340f, Screen.height - 44f, 320f, 28f);
                GUI.Label(hint, armed ? "Press Esc again to skip" : "Esc twice to skip", hintStyle);
            }

            GUI.color = previous;
        }

        private void EnsureStyles()
        {
            if (captionStyle != null && styledHeight == Screen.height) return;
            styledHeight = Screen.height;
            float scale = Screen.height / 1080f;
            Font font = Resources.Load<Font>("Dialogue/Nunito-Regular");
            captionStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                fontSize = Mathf.Max(16, Mathf.RoundToInt(34f * scale)),
                fontStyle = FontStyle.Italic
            };
            captionStyle.normal.textColor = Cream;
            hintStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleRight,
                fontSize = Mathf.Max(12, Mathf.RoundToInt(18f * scale))
            };
            hintStyle.normal.textColor = Cream;
            if (font != null)
            {
                captionStyle.font = font;
                hintStyle.font = font;
            }
        }
    }
}
