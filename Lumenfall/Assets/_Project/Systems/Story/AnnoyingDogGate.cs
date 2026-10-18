using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Spells;
using Lumenfall.Startup;
using UnityEngine;

namespace Lumenfall.Story
{
    /// <summary>
    /// A joke on the campus's north gate, which leads nowhere yet. The first time the player tries it in a save, a fake
    /// "loading next area" bar creeps along through nonsense file names until <c>annoying_dog.exe</c> loads, the bar drops
    /// back to zero, and the gate's own locked notice says a dog is in the way. After that the gate only shows the notice.
    /// It sits on the gate's blocked portal, which supplies the notice (see <see cref="IBlockedPortalHandler"/>).
    /// <para>
    /// The secret past it: after <see cref="AwareAfterTries"/> more tries the dog becomes aware of the player (the notice
    /// changes along the way, so it is worth persisting). Vis lifts it (still asleep), Rotara then spins it out of the way,
    /// and the gate opens onto the dog's room
    /// (<see cref="SecretRooms.DogRoomId"/>), once. Each step is a saved flag. After the visit the dog is back in the way.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AnnoyingDogGate : MonoBehaviour, IBlockedPortalHandler
    {
        public const string SeenFlag = "joke.annoying_dog.seen";
        public const string TriesCounter = "joke.annoying_dog.tries";
        public const string AwareFlag = "secret.dog.aware";
        public const string LiftedFlag = "secret.dog.lifted";
        public const string MovedFlag = "secret.dog.moved";
        public const int AwareAfterTries = 10;
        public static readonly Vector2Int GateCell = new Vector2Int(26, 88);
        private const string MapId = "campus";
        private const string Title = "Blocked";
        private const string DogFile = "annoying_dog.exe";

        // Each file the bar "loads": the progress it reaches and how long that takes, in seconds.
        private static readonly (string file, float progress, float seconds)[] Steps =
        {
            ("campus_north_boundary.terrain", 0.07f, 0.9f),
            ("reticulating_splines.dll", 0.15f, 1.0f),
            ("grass_FINAL_final_v2 (1).mesh", 0.26f, 1.1f),
            ("npc_small_talk_backup_backup.txt", 0.34f, 0.8f),
            ("cruxite_reserves.json (empty)", 0.43f, 1.0f),
            ("chapter_2_probably.unity", 0.57f, 1.2f),
            ("door_we_forgot_about.prefab", 0.66f, 0.8f),
            ("homework_due_tomorrow.docx", 0.78f, 1.1f),
            ("ice_trial_but_warmer.asset", 0.88f, 0.9f),
            ("almost_there_trust_me.tmp", 0.97f, 1.6f),
            (DogFile, 0.99f, 1.8f),
        };
        // Tries 2 to 9 after the joke, one line each: the gate's notice plus a sign the dog is slowly stirring. The last
        // ones hint that it could be lifted, leading into the tenth try, which points at Vis.
        private static readonly string[] StirringReplies =
        {
            "A dog obstructs the way. It is snoring softly.",
            "A dog obstructs the way. It snores a little louder, as if to make a point.",
            "A dog obstructs the way. One of its ears twitches.",
            "A dog obstructs the way. Both of its ears twitch.",
            "A dog obstructs the way. Its tail gives a single, sleepy thump.",
            "A dog obstructs the way. It is starting to stir...",
            "A dog obstructs the way. It rolls over, barely. It hardly seems to weigh a thing.",
            "A dog obstructs the way. One eye almost opens. A dog this small could surely be lifted...",
        };

        private const float DropSeconds = 0.45f;
        private const float HoldEmptySeconds = 0.9f;
        private const float FadeSeconds = 0.25f;

        private readonly MenuSkin skin = new MenuSkin();
        private GridPortal portal;
        private GridMover lockedMover;
        private bool moverWasLocked;
        private bool playing;
        private float startedAt;
        private float totalSeconds;

        private void OnDestroy() => skin.Dispose();

        private void OnDisable()
        {
            if (playing) Finish(false);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => SpellCaster.CastAttempted -= OnCastAttempted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SpellCaster.CastAttempted -= OnCastAttempted;
            SpellCaster.CastAttempted += OnCastAttempted;
        }

        /// <summary>
        /// What the gate says to a try once the joke has played: <c>null</c> for its usual notice, or the dog's latest
        /// state. A try past the moved-aside dog opens the way, which <paramref name="opens"/> reports.
        /// </summary>
        public static string ReplyToTry(GameEventRuntime runtime, out bool opens)
        {
            opens = false;
            if (SecretRooms.HasVisited(runtime, SecretRooms.DogRoomId)) return "The dog has drifted back into the way. It is fast asleep, and it is not moving again.";
            if (runtime.GetFlag(MovedFlag)) { opens = true; return "The dog has drifted aside, still asleep. The way is open."; }
            if (runtime.GetFlag(LiftedFlag)) return "The dog floats in the gateway, fast asleep. It is still in the way. If only it would turn aside...";
            if (runtime.GetFlag(AwareFlag)) return "The dog is aware of you. It does not seem to care. It does look light enough to lift.";
            int tries = runtime.GetCounter(TriesCounter) + 1;
            runtime.SetCounter(TriesCounter, tries);
            // The usual notice at first, then a new sign each try that persistence is getting somewhere.
            if (tries < 2) return null;
            if (tries < AwareAfterTries) return StirringReplies[tries - 2];
            runtime.SetFlag(AwareFlag, true);
            return "The dog is now aware of you. It looks light enough to lift.";
        }

        // Vis and then Rotara, cast by the gate once the dog is aware.
        private static void OnCastAttempted(SpellCaster.CastAttempt cast)
        {
            GameEventRuntime runtime = GameEventManager.Instance != null ? GameEventManager.Instance.Runtime : null;
            if (runtime == null || !string.Equals(cast.MapId, MapId, System.StringComparison.OrdinalIgnoreCase) || !runtime.GetFlag(AwareFlag) ||
                Mathf.Max(Mathf.Abs(cast.Cell.x - GateCell.x), Mathf.Abs(cast.Cell.y - GateCell.y)) > 2) return;
            if (cast.Spell == SpellId.Vis && !runtime.GetFlag(LiftedFlag))
            {
                runtime.SetFlag(LiftedFlag, true);
                PortalBlockedPopup.Show("You lift the dog with Vis. It stays asleep, and it stays in the way. Perhaps it could be turned aside.", "Vis", 4f);
                cast.Handled = true;
            }
            else if (cast.Spell == SpellId.Rotara && runtime.GetFlag(LiftedFlag) && !runtime.GetFlag(MovedFlag))
            {
                runtime.SetFlag(MovedFlag, true);
                PortalBlockedPopup.Show("The dog spins gently in the air, still asleep, and drifts out of the way.", "Rotara", 4f);
                cast.Handled = true;
            }
        }

        public bool HandleBlocked(GridPortal blockedPortal, GridMover mover)
        {
            if (playing) return true;
            GameEventRuntime runtime = GameEventManager.Instance != null ? GameEventManager.Instance.Runtime : null;
            if (runtime == null) return false;
            // At the end of the white dog's trail the gate leads somewhere else instead (see WhiteDogTrailSecret).
            if (WhiteDogTrailSecret.TryFollowAtGate(runtime)) return true;
            if (runtime.GetFlag(SeenFlag))
            {
                string reply = ReplyToTry(runtime, out bool opens);
                if (reply == null) return false;
                PortalBlockedPopup.Show(reply, opens ? "The north gate" : Title, 3f);
                if (opens) SecretRooms.Enter(SecretRooms.DogRoomId);
                return true;
            }
            // Saved straight away, so leaving or saving partway through does not replay it.
            runtime.SetFlag(SeenFlag, true);
            portal = blockedPortal;
            lockedMover = mover;
            moverWasLocked = mover != null && mover.IsMovementLocked;
            if (mover != null) mover.SetMovementLocked(true);
            playing = true;
            startedAt = Time.unscaledTime;
            totalSeconds = DropSeconds + HoldEmptySeconds;
            foreach ((string _, float _, float seconds) in Steps) totalSeconds += seconds;
            return true;
        }

        private void Update()
        {
            if (!playing) return;
            skin.Tick(Time.unscaledDeltaTime);
            if (Time.unscaledTime - startedAt >= totalSeconds) Finish(true);
        }

        private void Finish(bool showNotice)
        {
            playing = false;
            if (lockedMover != null && !moverWasLocked) lockedMover.SetMovementLocked(false);
            lockedMover = null;
            if (showNotice && portal != null && portal.Content != null)
                PortalBlockedPopup.Show(portal.Content.blockedMessage, portal.Content.blockedTitle);
        }

        // Where the bar is, which file it is on, and whether the dog has emptied it.
        private void Sample(float elapsed, out float progress, out string file, out bool dropped)
        {
            float from = 0f;
            float time = 0f;
            foreach ((string name, float target, float seconds) in Steps)
            {
                if (elapsed < time + seconds)
                {
                    // Each file eases in, so the bar lurches and then dawdles, as loading bars do.
                    float t = Mathf.Clamp01((elapsed - time) / seconds);
                    progress = Mathf.Lerp(from, target, 1f - (1f - t) * (1f - t) * (1f - t));
                    file = name;
                    dropped = false;
                    return;
                }
                time += seconds;
                from = target;
            }
            float drop = Mathf.Clamp01((elapsed - time) / DropSeconds);
            progress = Mathf.Lerp(from, 0f, drop * drop);
            file = DogFile;
            dropped = true;
        }

        // Drawn with GUI only, never GUILayout, so skip the per-frame layout pass and its allocations.
        private void Awake() => useGUILayout = false;

        private void OnGUI()
        {
            using (UiScale.Begin()) DrawGui();
        }

        private void DrawGui()
        {
            if (!playing) return;
            skin.Ensure();
            float now = Time.unscaledTime;
            float elapsed = now - startedAt;
            skin.Fade = Mathf.Min(Mathf.Clamp01(elapsed / FadeSeconds), Mathf.Clamp01((totalSeconds - elapsed) / FadeSeconds));
            Sample(elapsed, out float progress, out string file, out bool dropped);
            bool dog = file == DogFile;

            float width = Mathf.Min(UiScale.Width - 32f, 520f);
            float height = 214f;
            Rect panel = new Rect((UiScale.Width - width) * 0.5f, (UiScale.Height - height) * 0.5f, width, height);
            int previousDepth = GUI.depth;
            GUI.depth = -1000;
            skin.PanelFrame(panel, now);
            skin.DrawTitle(new Rect(panel.x + 28f, panel.y + 20f, panel.width - 56f, 44f), dropped ? "Loading failed" : "Loading next area...", now);
            skin.Divider(panel.center.x, panel.y + 74f, panel.width - 80f, now);

            // The bar: a dark track and a gold fill.
            Rect track = new Rect(panel.x + 40f, panel.y + 98f, panel.width - 80f, 18f);
            skin.Draw(track, Texture2D.whiteTexture, MenuArt.Ink, 0.85f);
            Rect fill = new Rect(track.x + 2f, track.y + 2f, Mathf.Max(0f, (track.width - 4f) * progress), track.height - 4f);
            skin.Draw(fill, Texture2D.whiteTexture, dog ? MenuArt.Ember : MenuArt.Gold, 1f);

            GUIStyle line = new GUIStyle(skin.Body) { alignment = TextAnchor.MiddleCenter, fontSize = 16, richText = true };
            Color previousColour = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, skin.Fade);
            GUI.Label(new Rect(panel.x + 30f, panel.y + 124f, panel.width - 60f, 22f), Mathf.RoundToInt(progress * 100f) + "%", line);
            // The dog's file stands out, and twitches.
            Vector2 jitter = dog ? new Vector2(Random.Range(-1.5f, 1.5f), Random.Range(-1f, 1f)) : Vector2.zero;
            string text = dropped ? "<color=#FF9C6E><b>" + DogFile + "</b> ate the progress bar</color>"
                : dog ? "Loading <color=#FF9C6E><b>" + DogFile + "</b></color>" : "Loading " + file;
            GUI.Label(new Rect(panel.x + 30f + jitter.x, panel.y + 154f + jitter.y, panel.width - 60f, 30f), text, line);
            GUI.color = previousColour;
            skin.DrawSparks(skin.Fade);
            GUI.depth = previousDepth;
        }
    }
}
