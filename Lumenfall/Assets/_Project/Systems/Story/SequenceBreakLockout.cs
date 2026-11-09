using System;
using System.Collections.Generic;
using Lumenfall.GameEvents;
using Lumenfall.Startup;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Lumenfall.Story
{
    /// <summary>
    /// What is left after the sequence break (<see cref="SequenceBreakEnding"/>) shatters the screen and closes the game.
    /// Every launch after that seems ordinary at first: the title opens, but to its own music (<see cref="TitleMusicPath"/>)
    /// and without answering any key. When that music ends the screen cuts to black and silence. After
    /// <see cref="SilenceSeconds"/> a light flickers on and Miki, one of the developers, is standing in it. He offers to
    /// let the player play again. Refusing closes the game, and the next launch is the same black screen. Accepting
    /// comes at a price, something given in return, never named. Refusing that also closes the game; agreeing lets Miki
    /// keep it and opens the title as normal, and every launch from then on is an ordinary one.
    /// The lockout is kept in PlayerPrefs (<see cref="LockedOutKey"/>), outside every save slot, so neither loading a
    /// file nor starting over clears it. While it is set the startup menu does not open by itself: this opens it, and
    /// it ignores every key and click until the player is let back in.
    /// </summary>
    public sealed class SequenceBreakLockout : MonoBehaviour
    {
        /// <summary>Set when the sequence break closes the game; cleared when the player accepts Miki's price.</summary>
        public const string LockedOutKey = "secret.sequence_break.locked_out";
        /// <summary>Set once the player has accepted the price. What was given is never said.</summary>
        public const string GivenKey = "secret.sequence_break.given";
        /// <summary>How long the empty screen holds before Miki appears.</summary>
        public const float SilenceSeconds = 30f;
        /// <summary>The music the title opens to while the game is locked out; the screen goes black when it ends.</summary>
        internal const string TitleMusicPath = "Audio/Music/lumen_MMMMMMMMMMMMMMMMMMMM";
        /// <summary>How long the title stays up if its music cannot be loaded.</summary>
        internal const float FallbackTitleSeconds = 5f;
        /// <summary>The longest to wait for the title to appear before going black anyway.</summary>
        internal const float TitleAppearTimeout = 8f;

        /// <summary>Where a line leads when it closes the game, and when it lets the player play.</summary>
        public const string QuitStep = "<quit>";
        public const string PlayStep = "<play>";

        /// <summary>
        /// Miki's lines in order of the conversation. A line with <c>yes</c> and <c>no</c> asks a question and waits for
        /// the answer; any other line leads on to <c>next</c>.
        /// </summary>
        internal static readonly (string id, string text, string next, string yes, string no)[] Script =
        {
            ("back", "Oh. You returned for more?", "who", null, null),
            ("who", "I spent so much time making this world", "broke", null, null),
            ("broke", "You broke it without considering the consequences.", "ask", null, null),
            ("ask", "I can feel your persistence. Do you want to play again?", null, "price", "refuse"),
            ("refuse", "...alr, you know where to find me", QuitStep, null, null),
            ("price", "Sure. But fixing a whole world isn't free.", "give", null, null),
            ("give", "You'll have to give me something in return.", "deal", null, null),
            ("deal", "Deal?", null, "keep", "decline"),
            ("decline", "Then there's nothing left to play.", QuitStep, null, null),
            ("keep", "Thank you. I'll keep it safe.", "dont", null, null),
            ("dont", "Don't worry about what it was. You won't miss it.", "go", null, null),
            ("go", "Go on, then. Have fun :D, it might be your last", PlayStep, null, null),
        };

        private const int StageLayer = 31;
        private static readonly Vector3 StagePosition = new Vector3(0f, -2000f, 0f);
        private const float AppearSeconds = 2.6f;
        private const float QuitFadeSeconds = 1.2f;
        private const float PlayFadeSeconds = 1.8f;
        private const float CharactersPerSecond = 30f;
        private const float InputDelaySeconds = 0.35f;
        private const float SpotIntensity = 9f;
        private static readonly Color Cream = new Color(0.96f, 0.91f, 0.82f);
        private static readonly Color Gold = new Color(1f, 0.84f, 0.45f);
        private static readonly Color MikiColour = new Color(0.62f, 0.68f, 0.98f);

        private enum Phase { Title, Silence, Appearing, Talking, Leaving }

        private static SequenceBreakLockout instance;
        private Phase phase;
        private float phaseAt;
        private string leavingTo;
        private int line = -1;
        private float lineAt;
        private int selected;
        private bool dressPending;
        private int builtFrame;
        private GameObject stage;
        private GameObject miki;
        private Light spot;
        private Camera stageCamera;
        private RenderTexture view;
        private AudioSource titleMusic;
        private bool titleMusicStarted;
        private float titleMusicAt;
        private GUIStyle textStyle, speakerStyle, choiceStyle;
        private readonly Rect[] choiceRects = new Rect[2];

        /// <summary>True while the game is locked out: the next launch opens on the empty screen.</summary>
        public static bool IsLockedOut => PlayerPrefs.GetInt(LockedOutKey, 0) != 0;

        /// <summary>True while the locked-out screen is up.</summary>
        public static bool InProgress => instance != null;

        /// <summary>True once the title has gone black: from then on nothing of the title is drawn until the player is let back in.</summary>
        public static bool HidesTitle => instance != null && instance.phase != Phase.Title;

        /// <summary>Locks the game out from the next launch on.</summary>
        public static void Arm()
        {
            PlayerPrefs.SetInt(LockedOutKey, 1);
            PlayerPrefs.Save();
        }

        /// <summary>Lifts the lockout without the conversation (the editor's Secrets menu).</summary>
        public static void Clear()
        {
            PlayerPrefs.DeleteKey(LockedOutKey);
            PlayerPrefs.Save();
        }

        /// <summary>Closes the game: leaves play mode in the editor.</summary>
        internal static void QuitGame()
        {
            // Nothing stays stopped once the game is closed (in the editor, these would outlive play mode).
            Time.timeScale = 1f;
            AudioListener.pause = false;
            Cursor.visible = true;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
            // Where a build cannot close itself (in a browser), the empty screen begins in place instead.
            BeginNow();
#endif
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (IsLockedOut) BeginNow();
        }

        private static void BeginNow()
        {
            if (instance != null) return;
            GameObject host = new GameObject("[Sequence Break Lockout]");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<SequenceBreakLockout>();
        }

        private void Awake()
        {
            useGUILayout = false;
            OverlayInputGate.Acquire(this);
            BeginTitle(Time.unscaledTime);
        }

        // The game seems to open as usual: the title comes up (frozen by StartupMenu while this exists), with its music.
        private void BeginTitle(float now)
        {
            SetPhase(Phase.Title, now);
            titleMusicStarted = false;
            Hold();
            StartupMenu.Open();
            // The title holds the world still while it is open, as it does on any launch.
            if (StartupMenu.IsOpen) Time.timeScale = 0f;
        }

        // Everything stays stopped behind the black: the world, its sounds and the pointer. While the title is up only its
        // usual music is held back, so the locked-out track plays alone.
        private void Hold()
        {
            AudioListener.pause = true;
            if (phase == Phase.Title) return;
            Time.timeScale = 0f;
            Cursor.visible = phase == Phase.Talking;
        }

        /// <summary>How long the locked-out title stays up: as long as its music, or a moment without it.</summary>
        internal static float TitleSeconds(AudioClip music) => music != null && music.length > 0f ? music.length : FallbackTitleSeconds;

        private void UpdateTitle(float now)
        {
            if (!titleMusicStarted)
            {
                // Its music starts once the title is on screen, so the two arrive together.
                if (!StartupMenu.IsVisible && now - phaseAt < TitleAppearTimeout) return;
                titleMusicStarted = true;
                titleMusicAt = now;
                if (titleMusic == null) titleMusic = SequenceBreakEnding.CreatePauseProofSource(gameObject, Resources.Load<AudioClip>(TitleMusicPath));
                if (titleMusic.clip != null)
                {
                    titleMusic.volume = Lumenfall.Audio.AudioMixSettings.Music;
                    Lumenfall.Audio.AudioPlayback.Play(titleMusic);
                }
                return;
            }
            if (now - titleMusicAt < TitleSeconds(titleMusic != null ? titleMusic.clip : null)) return;
            // Suddenly black, and silent.
            if (titleMusic != null) titleMusic.Stop();
            SetPhase(Phase.Silence, now);
            Hold();
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
            OverlayInputGate.Release(this);
            if (stage != null) Destroy(stage);
            if (view != null) { view.Release(); Destroy(view); }
        }

        private void Update()
        {
            Hold();
            float now = Time.unscaledTime;
            float since = now - phaseAt;
            switch (phase)
            {
                case Phase.Title:
                    UpdateTitle(now);
                    break;
                case Phase.Silence:
                    if (since < SilenceSeconds) break;
                    BuildStage();
                    SetPhase(Phase.Appearing, now);
                    break;
                case Phase.Appearing:
                    if (since >= AppearSeconds) { SetPhase(Phase.Talking, now); Show(0, now); }
                    break;
                case Phase.Talking:
                    HandleInput(now);
                    break;
                case Phase.Leaving:
                    if (since < (leavingTo == PlayStep ? PlayFadeSeconds : QuitFadeSeconds)) break;
                    if (leavingTo == PlayStep) { Play(); return; }
                    QuitGame();
                    // Where the game cannot close (in a browser), the empty screen starts over.
                    Restart(now);
                    return;
            }
            UpdateStage(now);
        }

        private void Restart(float now)
        {
            if (stage != null) Destroy(stage);
            stage = null;
            miki = null;
            spot = null;
            stageCamera = null;
            line = -1;
            leavingTo = null;
            // As if the game had been opened again.
            BeginTitle(now);
        }

        private void SetPhase(Phase next, float now)
        {
            phase = next;
            phaseAt = now;
        }

        private void Show(int index, float now)
        {
            line = index;
            lineAt = now;
            selected = 0;
        }

        private bool Revealed(float now) => line >= 0 && ShownCharacters(now) >= Script[line].text.Length;

        private int ShownCharacters(float now) => Mathf.FloorToInt((now - lineAt) * CharactersPerSecond);

        private bool Asks => line >= 0 && Script[line].yes != null;

        private void HandleInput(float now)
        {
            if (now - lineAt < InputDelaySeconds) return;
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            Gamepad pad = Gamepad.current;
            bool confirm = (keyboard != null && (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame ||
                                                 keyboard.numpadEnterKey.wasPressedThisFrame || keyboard.eKey.wasPressedThisFrame)) ||
                           (pad != null && pad.buttonSouth.wasPressedThisFrame);
            bool click = mouse != null && mouse.leftButton.wasPressedThisFrame;
            if (!Revealed(now))
            {
                if (confirm || click) lineAt = now - Script[line].text.Length / CharactersPerSecond - InputDelaySeconds;
                return;
            }
            if (!Asks)
            {
                if (confirm || click) Advance(Script[line].next, now);
                return;
            }

            bool move = keyboard != null && (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame ||
                                             keyboard.upArrowKey.wasPressedThisFrame || keyboard.downArrowKey.wasPressedThisFrame ||
                                             keyboard.aKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame ||
                                             keyboard.wKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame);
            if (pad != null && (pad.dpad.left.wasPressedThisFrame || pad.dpad.right.wasPressedThisFrame)) move = true;
            if (move) selected = 1 - selected;
            if (keyboard != null && (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame)) { Answer(0, now); return; }
            if (keyboard != null && (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame)) { Answer(1, now); return; }
            if (mouse != null)
            {
                Vector2 pointer = PointerInGui(mouse);
                for (int i = 0; i < choiceRects.Length; i++)
                {
                    if (!choiceRects[i].Contains(pointer)) continue;
                    selected = i;
                    if (click) { Answer(i, now); return; }
                }
            }
            if (confirm) Answer(selected, now);
        }

        private static Vector2 PointerInGui(Mouse mouse)
        {
            Vector2 raw = mouse.position.ReadValue();
            float factor = UiScale.Factor;
            return new Vector2(raw.x / factor, (Screen.height - raw.y) / factor);
        }

        private void Answer(int choice, float now) => Advance(choice == 0 ? Script[line].yes : Script[line].no, now);

        private void Advance(string step, float now)
        {
            if (step == QuitStep || step == PlayStep)
            {
                leavingTo = step;
                SetPhase(Phase.Leaving, now);
                return;
            }
            int index = Array.FindIndex(Script, entry => entry.id == step);
            if (index < 0)
            {
                Debug.LogWarning("The locked-out conversation has no line '" + step + "'; closing the game.");
                leavingTo = QuitStep;
                SetPhase(Phase.Leaving, now);
                return;
            }
            Show(index, now);
        }

        // Miki keeps what was given; from now on the game opens as normal.
        private void Play()
        {
            PlayerPrefs.DeleteKey(LockedOutKey);
            PlayerPrefs.SetInt(GivenKey, 1);
            PlayerPrefs.Save();
            AudioListener.pause = false;
            Cursor.visible = true;
            Destroy(gameObject);
            // The title has been open under the black all along; it now answers again, with its usual music, and
            // keeps the world still until it closes. Should it have gone, it opens afresh.
            if (StartupMenu.IsOpen) Time.timeScale = 0f;
            else
            {
                Time.timeScale = 1f;
                StartupMenu.Open();
            }
        }

        // ---- Miki's stage: his model in a spotlight, far below the world, seen only by its own camera ----

        private void BuildStage()
        {
            stage = new GameObject("[Locked Out Stage]");
            DontDestroyOnLoad(stage);
            stage.transform.position = StagePosition;
            SecretRoomArt art = SecretRoomArt.Load();
            miki = DevChibis.Create(art != null ? art.devCurls : null, DevChibis.Curls, stage.transform);
            if (miki != null)
            {
                // Facing the camera, which stands in front of him.
                miki.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                foreach (Animator animator in miki.GetComponentsInChildren<Animator>(true)) animator.updateMode = AnimatorUpdateMode.UnscaledTime;
                foreach (Behaviour behaviour in miki.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
                dressPending = true;
                builtFrame = Time.frameCount;
            }
            // A dark floor for the light to land on.
            SecretRooms.Part(PrimitiveType.Cylinder, "Floor", stage.transform, new Vector3(0f, -0.02f, 0f), new Vector3(2.6f, 0.02f, 2.6f), new Color(0.16f, 0.15f, 0.18f), 0f);

            spot = new GameObject("Spotlight").AddComponent<Light>();
            spot.transform.SetParent(stage.transform, false);
            spot.transform.localPosition = new Vector3(0f, 4.6f, 1.4f);
            spot.transform.LookAt(stage.transform.position + Vector3.up * 0.8f);
            spot.type = LightType.Spot;
            spot.spotAngle = 44f;
            spot.innerSpotAngle = 22f;
            spot.range = 12f;
            spot.color = new Color(1f, 0.95f, 0.86f);
            spot.intensity = 0f;
            spot.shadows = LightShadows.Soft;
            spot.cullingMask = 1 << StageLayer;

            stageCamera = new GameObject("Stage Camera").AddComponent<Camera>();
            stageCamera.transform.SetParent(stage.transform, false);
            stageCamera.transform.localPosition = new Vector3(0f, 1.3f, 5.4f);
            stageCamera.transform.LookAt(stage.transform.position + Vector3.up * 0.55f);
            stageCamera.clearFlags = CameraClearFlags.SolidColor;
            stageCamera.backgroundColor = Color.black;
            stageCamera.cullingMask = 1 << StageLayer;
            stageCamera.fieldOfView = 34f;
            stageCamera.nearClipPlane = 0.1f;
            stageCamera.farClipPlane = 30f;
            stageCamera.allowHDR = false;
            SetLayer(stage);
            EnsureView();
        }

        private void UpdateStage(float now)
        {
            if (stage == null) return;
            EnsureView();
            // Dressed a frame after he is built, once the rig has taken its pose, as in the devs' room.
            if (dressPending && miki != null && Time.frameCount > builtFrame)
            {
                dressPending = false;
                DevChibis.Dress(miki, DevChibis.Curls);
                SetLayer(miki);
            }
            if (spot != null) spot.intensity = SpotIntensity * StageLight(now);
        }

        private void EnsureView()
        {
            if (stageCamera == null) return;
            int width = Mathf.Max(1, Screen.width), height = Mathf.Max(1, Screen.height);
            if (view != null && view.width == width && view.height == height) return;
            if (view != null) { stageCamera.targetTexture = null; view.Release(); Destroy(view); }
            view = new RenderTexture(width, height, 24) { name = "Locked Out Stage", antiAliasing = 4 };
            stageCamera.targetTexture = view;
            stageCamera.aspect = width / (float)height;
        }

        private static void SetLayer(GameObject root)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = StageLayer;
        }

        /// <summary>How lit the stage is: a light that stutters on, holds, and goes out as he leaves.</summary>
        private float StageLight(float now)
        {
            float since = now - phaseAt;
            switch (phase)
            {
                case Phase.Appearing:
                    // Two false starts, then it catches.
                    if (since < 0.08f) return 0.7f;
                    if (since < 0.45f) return 0f;
                    if (since < 0.52f) return 0.5f;
                    if (since < 0.62f) return 0f;
                    if (since < 0.7f) return 0.9f;
                    if (since < 0.95f) return 0.1f;
                    return Mathf.SmoothStep(0.3f, 1f, Mathf.Clamp01((since - 0.95f) / 1.2f));
                case Phase.Talking:
                    return 1f;
                case Phase.Leaving:
                    // Refused: the light cuts out. Agreed: it fades gently.
                    return leavingTo == PlayStep ? 1f - Mathf.Clamp01(since / (PlayFadeSeconds * 0.8f)) : since < 0.1f ? 1f : 0f;
                default:
                    return 0f;
            }
        }

        // ---- Drawing ----

        private void OnGUI()
        {
            using (UiScale.Begin()) DrawGui();
        }

        private void DrawGui()
        {
            int previousDepth = GUI.depth;
            GUI.depth = -3100;
            Color previous = GUI.color;
            float width = UiScale.Width, height = UiScale.Height;
            // The title shows through until its music ends.
            if (phase == Phase.Title)
            {
                GUI.depth = previousDepth;
                return;
            }
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0f, 0f, width, height), Texture2D.whiteTexture);
            float now = Time.unscaledTime;
            float lit = StageLight(now);
            if (view != null && lit > 0f)
            {
                GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(lit * 1.2f));
                GUI.DrawTexture(new Rect(0f, 0f, width, height), view, ScaleMode.StretchToFill, false);
            }
            if (phase == Phase.Talking && line >= 0) DrawLine(width, height, now);
            GUI.color = previous;
            GUI.depth = previousDepth;
        }

        private void DrawLine(float width, float height, float now)
        {
            EnsureStyles();
            var entry = Script[line];
            Rect box = new Rect(width * 0.16f, height * 0.73f, width * 0.68f, height * 0.12f);
            GUI.color = MikiColour;
            GUI.Label(new Rect(box.x, box.y - 30f, box.width, 26f), "Miki", speakerStyle);
            GUI.color = Cream;
            GUI.Label(box, entry.text.Substring(0, Mathf.Clamp(ShownCharacters(now), 0, entry.text.Length)), textStyle);
            if (!Revealed(now)) return;
            if (!Asks)
            {
                GUI.color = new Color(Cream.r, Cream.g, Cream.b, 0.35f + 0.25f * Mathf.Sin(now * 3f));
                GUI.Label(new Rect(box.x, box.yMax + 6f, box.width, 24f), "▼", speakerStyle);
                return;
            }
            string[] answers = { "Yes", "No" };
            float choiceWidth = 160f, gap = 60f;
            float left = width * 0.5f - choiceWidth - gap * 0.5f;
            for (int i = 0; i < answers.Length; i++)
            {
                choiceRects[i] = new Rect(left + i * (choiceWidth + gap), box.yMax + 4f, choiceWidth, 40f);
                GUI.color = i == selected ? Gold : new Color(Cream.r, Cream.g, Cream.b, 0.45f);
                GUI.Label(choiceRects[i], i == selected ? "- " + answers[i] + " -" : answers[i], choiceStyle);
            }
        }

        private void EnsureStyles()
        {
            if (textStyle != null) return;
            Font font = Resources.Load<Font>("Dialogue/Nunito-Regular");
            textStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, fontSize = 26, wordWrap = true, normal = { textColor = Color.white } };
            if (font != null) textStyle.font = font;
            speakerStyle = new GUIStyle(textStyle) { fontSize = 18, fontStyle = FontStyle.Bold, wordWrap = false };
            choiceStyle = new GUIStyle(textStyle) { alignment = TextAnchor.MiddleCenter, fontSize = 26, fontStyle = FontStyle.Bold, wordWrap = false };
        }

        /// <summary>Every line the conversation can lead to: the steps a line names, other than closing and playing.</summary>
        internal static IEnumerable<string> StepsFrom((string id, string text, string next, string yes, string no) entry)
        {
            if (entry.yes != null) { yield return entry.yes; yield return entry.no; }
            else yield return entry.next;
        }
    }
}
