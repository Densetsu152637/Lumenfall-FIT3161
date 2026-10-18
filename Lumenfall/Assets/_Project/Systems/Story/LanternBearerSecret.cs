using System;
using Lumenfall.DataDriven;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Spells;
using Lumenfall.Startup;
using Lumenfall.Rendering;
using UnityEngine;

namespace Lumenfall.Story
{
    /// <summary>
    /// Luna's favourite constellation, the Lantern Bearer, laid out in the west forest as seven star stones
    /// (<see cref="StoneCells"/>), from <see cref="StartCell"/> along <see cref="Steps"/> (up, up, right, up, right, right).
    /// The stones are an echo of the star the player fell as, so they are hidden, but walking them needs no spell: each
    /// stone appears, lit, as the player steps onto it in order from the first, with a ping a note higher each time. A
    /// wrong step thuds and the stones the walk uncovered fade away again. Lost Echoes can also reveal them, all seven at
    /// once (revealing one reveals them all), which shows the shape before walking it. When the last one lights, the stones flash in
    /// turn and a lantern spirit drifts down with a gift. The spirit stays (saved) until it has given it, then goes back
    /// into the trees; the stones stay lit. Like the secret rooms, finishing it without having heard Luna's and Moss's hints
    /// breaks the story (<see cref="SequenceBreakEnding.KnowsTooMuch"/>).
    /// </summary>
    public sealed class LanternBearerSecret : MonoBehaviour
    {
        public const string MapId = "westForest";
        /// <summary>The first star of the constellation, west of where the player wakes, where the trees close in.</summary>
        public static readonly Vector2Int StartCell = new Vector2Int(11, 3);
        public static readonly Vector2Int[] Steps =
        {
            Vector2Int.up, Vector2Int.up, Vector2Int.right, Vector2Int.up, Vector2Int.right, Vector2Int.right
        };
        /// <summary>The seven stones: the start, then one at the end of each step.</summary>
        public static readonly Vector2Int[] StoneCells = BuildStoneCells();
        public const string StoneIdPrefix = "lantern.stone.";
        public const string SummonedFlag = "secret.lantern_bearer.summoned";
        public const string SpiritXCounter = "secret.lantern_bearer.x";
        public const string SpiritYCounter = "secret.lantern_bearer.y";
        public const string SpiritId = "lantern_spirit";
        public const string RewardKey = "slightly_warm_pebble";
        private const string SpiritName = "Lantern Spirit";
        private const string StoneName = "Star Stone ";
        private static readonly Color DimStar = new Color(0.62f, 0.55f, 0.95f);
        private static readonly Color LitStar = new Color(1f, 0.86f, 0.5f);

        private static LanternBearerSecret instance;
        private int progress;
        private GridMover tracked;
        private AudioSource audioSource;
        private AudioClip[] pings;
        private AudioClip wrong;
        private Vector2Int lastCell;
        private bool hasLastCell;
        private string currentMapId;
        private GameObject spirit;
        private Renderer[][] stoneStars = Array.Empty<Renderer[]>();
        private string message;
        private float messageUntil;
        private WorldStateService boundState;
        // The congratulating light sequence: when it began, and who walked the stones.
        private float celebratingSince = -1f;
        // Stones this walk has uncovered, hidden again if it goes wrong (ones Lost Echoes revealed stay shown).
        private readonly System.Collections.Generic.List<int> uncovered = new System.Collections.Generic.List<int>();
        private GridMover celebrated;
        private const float SweepSeconds = 0.12f;
        private const float CelebrationSeconds = 2.2f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        private static Vector2Int[] BuildStoneCells()
        {
            Vector2Int[] cells = new Vector2Int[Steps.Length + 1];
            cells[0] = StartCell;
            for (int i = 0; i < Steps.Length; i++) cells[i + 1] = cells[i] + Steps[i];
            return cells;
        }

        public static string StoneId(int index) => StoneIdPrefix + index;

        /// <summary>
        /// The walk after one more step: how many of <see cref="Steps"/> are done. It can only begin with the first step
        /// taken from <see cref="StartCell"/>. A step that breaks a walk already begun is <paramref name="wrong"/> and resets
        /// it to 0; one that merely is not the start of a walk is neither.
        /// </summary>
        public static int Advance(int done, Vector2Int from, Vector2Int step, out bool wrong)
        {
            wrong = false;
            if (done <= 0) return from == StartCell && step == Steps[0] ? 1 : 0;
            if (done < Steps.Length && step == Steps[done]) return done + 1;
            wrong = true;
            return 0;
        }

        /// <summary>
        /// Called by the area loader on every map load, before EndMapRebuild: places and registers the hidden stones (so
        /// their saved reveal comes back) and brings back a summoned spirit that has not given its gift.
        /// </summary>
        public static void RegisterForMap(string mapId, GridWorld world, GameObject areaRoot)
        {
            if (instance == null)
            {
                GameObject host = new GameObject("[Lantern Bearer]");
                instance = host.AddComponent<LanternBearerSecret>();
                DontDestroyOnLoad(host);
            }
            instance.currentMapId = mapId;
            instance.ResetWalk();
            instance.spirit = null;
            instance.stoneStars = Array.Empty<Renderer[]>();
            GameEventRuntime runtime = GameEventManager.Instance != null ? GameEventManager.Instance.Runtime : null;
            if (!string.Equals(mapId, MapId, StringComparison.OrdinalIgnoreCase) || runtime == null || areaRoot == null || world == null) return;

            instance.stoneStars = new Renderer[StoneCells.Length][];
            WorldStateService state = WorldStateService.Instance;
            instance.Bind(state);
            for (int i = 0; i < StoneCells.Length; i++)
            {
                Transform stone = areaRoot.transform.Find(StoneName + i) ?? CreateStone(areaRoot.transform, i);
                stone.position = world.CellToWorld(StoneCells[i], 0.02f);
                instance.stoneStars[i] = stone.Find("Star").GetComponentsInChildren<Renderer>(true);
                state?.RegisterObject(StoneId(i), stone.gameObject, StoneCells[i], Vector2Int.one, true, new[] { "hidden", "echo:memory" });
            }
            bool summoned = runtime.GetFlag(SummonedFlag);
            instance.LightStones(summoned ? StoneCells.Length : 0);

            Transform existing = areaRoot.transform.Find(SpiritName);
            if (summoned && !MiscCollectibles.IsFound(runtime, RewardKey))
            {
                Vector2Int cell = new Vector2Int(runtime.GetCounter(SpiritXCounter), runtime.GetCounter(SpiritYCounter));
                instance.spirit = existing != null ? existing.gameObject : CreateSpirit(areaRoot.transform);
                instance.spirit.transform.position = world.CellToWorld(cell);
                instance.spirit.SetActive(true);
            }
            else if (existing != null) existing.gameObject.SetActive(false);
        }

        private void ResetWalk()
        {
            uncovered.Clear();
            progress = 0;
            hasLastCell = false;
            celebratingSince = -1f;
        }

        private void Bind(WorldStateService state)
        {
            if (boundState == state) return;
            if (boundState != null) boundState.ObjectRevealed -= OnObjectRevealed;
            boundState = state;
            if (state != null) state.ObjectRevealed += OnObjectRevealed;
        }

        // The stones are one constellation: an echo that finds any of them shows them all.
        private void OnObjectRevealed(WorldObjectSnapshot revealed)
        {
            if (revealed == null || revealed.id == null || !revealed.id.StartsWith(StoneIdPrefix, StringComparison.Ordinal) || boundState == null) return;
            for (int i = 0; i < StoneCells.Length; i++)
                if (!IsRevealed(i)) boundState.SetProperty(StoneId(i), value => value.revealed = true);
        }

        private void Update()
        {
            GameEventRuntime runtime = GameEventManager.Instance != null ? GameEventManager.Instance.Runtime : null;
            if (runtime == null || !string.Equals(currentMapId, MapId, StringComparison.OrdinalIgnoreCase)) return;
            AnimateSpirit(runtime);
            Track(GridMover.FindActive());
            if (celebratingSince >= 0f) Celebrate(runtime);
        }

        private void OnDestroy()
        {
            Track(null);
            Bind(null);
        }

        // The stones flash gold one after another, twice, then all at once; then the spirit comes (or the story breaks).
        private void Celebrate(GameEventRuntime runtime)
        {
            float t = Time.unscaledTime - celebratingSince;
            int sweep = Mathf.FloorToInt(t / SweepSeconds);
            int lit = sweep % (StoneCells.Length + 1);
            if (t < SweepSeconds * (StoneCells.Length + 1) * 2) FlashStones(lit);
            else LightStones(StoneCells.Length);
            if (t < CelebrationSeconds) return;
            celebratingSince = -1f;
            if (SequenceBreakEnding.KnowsTooMuch(runtime, SequenceBreakEnding.LanternBearerSecretId)) SequenceBreakEnding.Begin(SequenceBreakEnding.Cause.Knowledge);
            else if (celebrated != null) Summon(runtime, celebrated);
        }

        // Steps are read from the mover's own events, so held keys and back-to-back steps all count.
        private void Track(GridMover player)
        {
            if (tracked == player) return;
            if (tracked != null)
            {
                tracked.MoveCompleted -= OnMoved;
                tracked.Teleported -= OnTeleported;
            }
            tracked = player;
            ResetWalk();
            if (player == null) return;
            player.MoveCompleted += OnMoved;
            player.Teleported += OnTeleported;
            lastCell = player.CurrentCell;
            hasLastCell = true;
        }

        private void OnMoved(GridMover mover, Vector2Int arrived)
        {
            GameEventRuntime runtime = GameEventManager.Instance != null ? GameEventManager.Instance.Runtime : null;
            if (!string.Equals(currentMapId, MapId, StringComparison.OrdinalIgnoreCase) || runtime == null || runtime.GetFlag(SummonedFlag) || celebratingSince >= 0f) return;
            // Only single steps count; a slide or a jump quietly starts over.
            Vector2Int step = arrived - lastCell;
            bool single = hasLastCell && Mathf.Abs(step.x) + Mathf.Abs(step.y) == 1;
            Vector2Int from = lastCell;
            lastCell = arrived;
            hasLastCell = true;
            if (!single) { Restart(); return; }
            int before = progress;
            int next = Advance(progress, from, step, out bool wrongStep);
            if (wrongStep) { Play(wrong, 0.5f); Restart(); return; }
            progress = next;
            if (progress == before) return;
            Play(pings[progress - 1], 0.45f);
            // Each stone walked on comes out of hiding, lit.
            for (int i = 0; i <= progress; i++) Uncover(i);
            LightStones(progress + 1);
            if (progress < Steps.Length) return;
            // The finished constellation stays shown.
            uncovered.Clear();
            celebratingSince = Time.unscaledTime;
            celebrated = mover;
            Say("The lanterns flicker, as if they are listening.");
        }

        private void Restart()
        {
            progress = 0;
            LightStones(0);
            if (boundState != null)
                foreach (int stone in uncovered) boundState.SetProperty(StoneId(stone), value => value.revealed = false);
            uncovered.Clear();
        }

        private void Uncover(int stone)
        {
            if (boundState == null || IsRevealed(stone)) return;
            boundState.SetProperty(StoneId(stone), value => value.revealed = true);
            uncovered.Add(stone);
        }

        private void OnTeleported(GridMover mover, Vector2Int cell)
        {
            Restart();
            lastCell = cell;
            hasLastCell = true;
        }

        private static bool IsRevealed(int stone)
        {
            WorldStateService state = WorldStateService.Instance;
            return state != null && state.TryGetObject(StoneId(stone), out WorldObjectSnapshot snapshot) && snapshot.revealed;
        }

        // The first <paramref name="count"/> stones glow gold; the rest keep their faint violet echo.
        private void LightStones(int count)
        {
            for (int i = 0; i < stoneStars.Length; i++)
            {
                Color colour = i < count ? LitStar : DimStar;
                foreach (Renderer renderer in stoneStars[i] ?? Array.Empty<Renderer>())
                {
                    if (renderer == null) continue;
                    renderer.material.color = colour;
                    renderer.material.SetColor("_EmissionColor", colour * (i < count ? 2.2f : 0.6f));
                }
            }
        }

        // The sweep: one stone blazing white-gold, the ones before it lit, the rest dim.
        private void FlashStones(int head)
        {
            LightStones(head);
            if (head <= 0 || head > stoneStars.Length) return;
            foreach (Renderer renderer in stoneStars[head - 1] ?? Array.Empty<Renderer>())
                if (renderer != null) renderer.material.SetColor("_EmissionColor", Color.white * 4f);
        }

        private void Summon(GameEventRuntime runtime, GridMover player)
        {
            DataDrivenWorldLoader loader = DataDrivenWorldLoader.Instance;
            GridWorld world = player.World;
            if (loader == null || loader.CurrentAreaRoot == null || world == null) return;
            Vector2Int cell = player.CurrentCell + Vector2Int.up;
            foreach (Vector2Int offset in new[] { Vector2Int.up, Vector2Int.right, Vector2Int.left, Vector2Int.down })
                if (world.IsCellAvailable(player.CurrentCell + offset)) { cell = player.CurrentCell + offset; break; }
            runtime.SetCounter(SpiritXCounter, cell.x);
            runtime.SetCounter(SpiritYCounter, cell.y);
            runtime.SetFlag(SummonedFlag, true);
            spirit = CreateSpirit(loader.CurrentAreaRoot.transform);
            spirit.transform.position = world.CellToWorld(cell);
            Say("The stones blaze, and a light drifts down from the trees.");
        }

        private void AnimateSpirit(GameEventRuntime runtime)
        {
            if (spirit == null || !spirit.activeInHierarchy) return;
            Transform orb = spirit.transform.Find("Orb");
            if (orb != null) orb.localPosition = new Vector3(0f, 1.1f + Mathf.Sin(Time.time * 2f) * 0.12f, 0f);
            // Once its gift is given, it drifts back into the trees.
            if (MiscCollectibles.IsFound(runtime, RewardKey) && runtime.ActiveDialogue == null)
            {
                spirit.SetActive(false);
                Say("The lantern spirit drifts back up into the trees.");
            }
        }

        private void Awake()
        {
            // Drawn with GUI only, never GUILayout, so skip the per-frame layout pass and its allocations.
            useGUILayout = false;
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
            // Real clips can be dropped into Resources/Audio/LanternBearer (Ping1 to Ping6, Wrong); until then, built tones.
            // The pings climb a pentatonic scale, so the six right steps sound like a little tune.
            float[] notes = { 1046.5f, 1174.7f, 1318.5f, 1568f, 1760f, 2093f };
            pings = new AudioClip[notes.Length];
            for (int i = 0; i < notes.Length; i++)
                pings[i] = Resources.Load<AudioClip>("Audio/LanternBearer/Ping" + (i + 1)) ?? Tone("LanternPing" + (i + 1), 0.45f, notes[i], notes[i], 0.22f, 9f);
            wrong = Resources.Load<AudioClip>("Audio/LanternBearer/Wrong") ?? Tone("LanternWrong", 0.32f, 196f, 130f, 0.3f, 7f);
        }

        private void Play(AudioClip clip, float volume)
        {
            if (clip != null && audioSource != null) Lumenfall.Audio.AudioPlayback.PlayOneShot(audioSource, clip, volume * Lumenfall.Audio.AudioMixSettings.Sfx);
        }

        // A soft sine with a quick attack and an exponential fade; a bell ping when high and short, a dull thud when low.
        private static AudioClip Tone(string name, float seconds, float startHz, float endHz, float volume, float decay)
        {
            const int sampleRate = 44100;
            int length = Mathf.CeilToInt(seconds * sampleRate);
            float[] data = new float[length];
            float phase = 0f;
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)length;
                phase += 2f * Mathf.PI * Mathf.Lerp(startHz, endHz, t) / sampleRate;
                float envelope = Mathf.Min(1f, t * 80f) * Mathf.Exp(-decay * t);
                // A quiet octave above gives the ping a glassy edge.
                data[i] = volume * envelope * (Mathf.Sin(phase) + 0.25f * Mathf.Sin(2f * phase));
            }
            AudioClip clip = AudioClip.Create(name, length, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // A flat, mossy stone with a four-point star set into it. World state hides it until Lost Echoes reveals it.
        private static Transform CreateStone(Transform parent, int index)
        {
            GameObject stone = new GameObject(StoneName + index);
            stone.transform.SetParent(parent, false);
            SecretRooms.Part(PrimitiveType.Cylinder, "Stone", stone.transform, new Vector3(0f, 0.02f, 0f), new Vector3(0.7f, 0.025f, 0.7f), new Color(0.34f, 0.36f, 0.4f), 0.02f);
            GameObject star = new GameObject("Star");
            star.transform.SetParent(stone.transform, false);
            for (int i = 0; i < 2; i++)
                SecretRooms.Part(PrimitiveType.Cube, "Star Arm", star.transform, new Vector3(0f, 0.05f, 0f), new Vector3(0.5f, 0.012f, 0.09f), DimStar, 0.6f,
                    Quaternion.Euler(0f, 45f + i * 90f, 0f));
            return stone.transform;
        }

        private static GameObject CreateSpirit(Transform parent)
        {
            GameObject root = new GameObject(SpiritName);
            root.transform.SetParent(parent, true);
            GameObject orb = RuntimePrimitive.Create(PrimitiveType.Sphere);
            orb.name = "Orb";
            orb.transform.SetParent(root.transform, false);
            orb.transform.localPosition = new Vector3(0f, 1.1f, 0f);
            orb.transform.localScale = Vector3.one * 0.45f;
            Destroy(orb.GetComponent<Collider>());
            Renderer renderer = orb.GetComponent<Renderer>();
            Color warm = new Color(1f, 0.8f, 0.45f);
            renderer.material.color = warm;
            renderer.material.SetColor("_EmissionColor", warm * 3f);
            renderer.material.EnableKeyword("_EMISSION");
            Light light = orb.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = warm;
            light.range = 4f;
            light.intensity = 1.6f;
            light.shadows = LightShadows.None;
            DynamicNpc npc = root.AddComponent<DynamicNpc>();
            npc.Configure(new NpcDefinition { id = SpiritId, displayName = SpiritName, dialogueIds = new[] { "secret_lantern_spirit" } }, "secret_lantern_spirit");
            return root;
        }

        private void Say(string text)
        {
            message = text;
            messageUntil = Time.unscaledTime + 4f;
        }

        private void OnGUI()
        {
            using (UiScale.Begin()) DrawGui();
        }

        private void DrawGui()
        {
            if (Time.unscaledTime < messageUntil && !string.IsNullOrEmpty(message) && !StartupMenu.IsOpen) HudSkin.Message(message);
        }
    }
}
