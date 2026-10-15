using System;
using System.Collections.Generic;
using Lumenfall.Cutscenes;
using Lumenfall.DataDriven;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Inventory;
using Lumenfall.Startup;
using UnityEngine;

namespace Lumenfall.Story
{
    /// <summary>
    /// Luna's favourite constellation, the Lantern Bearer. Starting where Moss says the lanterns hum loudest, a few steps
    /// up the path from where the player wakes (<see cref="StartCell"/>), walking its shape (<see cref="Steps"/>: up, up,
    /// right, up, right, right), then standing perfectly still for <see cref="StillSeconds"/> as Moss suggests, makes the lanterns
    /// hum, and a lantern spirit appears beside the player with a gift. Each right step of the walk pings, one note higher
    /// than the last; a wrong step thuds and the walk starts over from the start tile. The spirit stays (saved) until it
    /// has given its gift, then drifts back into the lanterns.
    /// </summary>
    public sealed class LanternBearerSecret : MonoBehaviour
    {
        public const string MapId = "westForest";
        /// <summary>Where the walk starts: four cells up and two left of where the player wakes (the forest spawn).</summary>
        public static readonly Vector2Int StartCell = new Vector2Int(19, 9);
        public static readonly Vector2Int[] Steps =
        {
            Vector2Int.up, Vector2Int.up, Vector2Int.right, Vector2Int.up, Vector2Int.right, Vector2Int.right
        };
        public const float StillSeconds = 10f;
        public const string SummonedFlag = "secret.lantern_bearer.summoned";
        public const string SpiritXCounter = "secret.lantern_bearer.x";
        public const string SpiritYCounter = "secret.lantern_bearer.y";
        public const string SpiritId = "lantern_spirit";
        public const string RewardKey = "slightly_warm_pebble";
        private const string SpiritName = "Lantern Spirit";

        private static LanternBearerSecret instance;
        private int progress;
        private GridMover tracked;
        private AudioSource audioSource;
        private AudioClip[] pings;
        private AudioClip wrong;
        private Vector2Int lastCell;
        private bool hasLastCell;
        private bool armed;
        private float stillFor;
        private string currentMapId;
        private GameObject spirit;
        private string message;
        private float messageUntil;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

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

        /// <summary>Called by the area loader on every map load: brings back a summoned spirit that has not given its gift.</summary>
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
            GameEventRuntime runtime = GameEventManager.Instance != null ? GameEventManager.Instance.Runtime : null;
            if (!string.Equals(mapId, MapId, StringComparison.OrdinalIgnoreCase) || runtime == null || areaRoot == null || world == null) return;
            Transform existing = areaRoot.transform.Find(SpiritName);
            if (runtime.GetFlag(SummonedFlag) && !MiscCollectibles.IsFound(runtime, RewardKey))
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
            progress = 0;
            hasLastCell = false;
            armed = false;
            stillFor = 0f;
        }

        private void Update()
        {
            GameEventRuntime runtime = GameEventManager.Instance != null ? GameEventManager.Instance.Runtime : null;
            if (runtime == null || !string.Equals(currentMapId, MapId, StringComparison.OrdinalIgnoreCase)) return;
            AnimateSpirit(runtime);
            GridMover player = GridMover.FindActive();
            Track(player);
            if (player == null || runtime.GetFlag(SummonedFlag)) return;
            if (player.IsMoving) { stillFor = 0f; return; }
            if (!armed || !IsQuiet(runtime)) return;
            stillFor += Time.deltaTime;
            if (stillFor >= StillSeconds) Summon(runtime, player);
        }

        private void OnDestroy() => Track(null);

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
            stillFor = 0f;
            GameEventRuntime runtime = GameEventManager.Instance != null ? GameEventManager.Instance.Runtime : null;
            if (!string.Equals(currentMapId, MapId, StringComparison.OrdinalIgnoreCase) || runtime == null || runtime.GetFlag(SummonedFlag)) return;
            // Only single steps count; a slide or a jump quietly starts over.
            Vector2Int step = arrived - lastCell;
            bool single = hasLastCell && Mathf.Abs(step.x) + Mathf.Abs(step.y) == 1;
            Vector2Int from = lastCell;
            lastCell = arrived;
            hasLastCell = true;
            if (!single) { progress = 0; armed = false; return; }
            int before = progress;
            progress = Advance(progress, from, step, out bool wrongStep);
            if (wrongStep) { Play(wrong, 0.5f); armed = false; return; }
            if (progress > before) Play(pings[progress - 1], 0.45f);
            bool wasArmed = armed;
            armed = progress == Steps.Length;
            if (armed && !wasArmed) Say("The lanterns flicker, as if they are listening.");
        }

        private void OnTeleported(GridMover mover, Vector2Int cell)
        {
            ResetWalk();
            lastCell = cell;
            hasLastCell = true;
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
            armed = false;
            Say("The lanterns hum together, and one of their lights drifts down to you.");
        }

        private void AnimateSpirit(GameEventRuntime runtime)
        {
            if (spirit == null || !spirit.activeInHierarchy) return;
            Transform orb = spirit.transform.Find("Orb");
            if (orb != null) orb.localPosition = new Vector3(0f, 1.1f + Mathf.Sin(Time.time * 2f) * 0.12f, 0f);
            // Once its gift is given, it drifts back into the lanterns.
            if (MiscCollectibles.IsFound(runtime, RewardKey) && runtime.ActiveDialogue == null)
            {
                spirit.SetActive(false);
                Say("The lantern spirit drifts back up into the trees.");
            }
        }

        private void Awake()
        {
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
            if (clip != null && audioSource != null) audioSource.PlayOneShot(clip, volume * Lumenfall.Audio.AudioMixSettings.Sfx);
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

        private static GameObject CreateSpirit(Transform parent)
        {
            GameObject root = new GameObject(SpiritName);
            root.transform.SetParent(parent, true);
            GameObject orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
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
            if (Time.unscaledTime < messageUntil && !string.IsNullOrEmpty(message) && !StartupMenu.IsOpen) HudSkin.Message(message);
        }

        // Standing still only counts while the player is actually playing: no menu, dialogue or cutscene.
        private static bool IsQuiet(GameEventRuntime runtime) =>
            !CutscenePlayer.IsPlaying && !StartupMenu.IsOpen && Time.timeScale > 0f && runtime.ActiveDialogue == null &&
            !InventoryUiState.IsOpen && !OverlayInputGate.IsBlocked;
    }
}
