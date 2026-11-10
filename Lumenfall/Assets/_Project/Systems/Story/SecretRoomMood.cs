using System.Collections.Generic;
using Lumenfall.Audio;
using Lumenfall.Cutscenes;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Spells;
using Lumenfall.Startup;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lumenfall.Story
{
    /// <summary>
    /// The atmosphere of the two quietest secret rooms, while the player is in one. Both silence the music (see
    /// <see cref="AudioContextPolicy.MapMusicCue"/>) and replace the scene's light, fog and sky with their own, all put
    /// back on leaving.
    /// <list type="bullet">
    /// <item>The forgotten room is dark and still. A few pendant lamps leave pools of light down a long corridor
    /// (one dead, one failing), dust turns in them, the building hums and a clock ticks somewhere out of sight. The clock
    /// stops while the man is talking. After he has spoken, once the player walks away, the lamps fail all at once; when
    /// they come back he is gone, for good (<see cref="ForgottenManGoneFlag"/>), and the clock with him. An egg he still
    /// had stays on its stand. Lost Echoes by the photograph behind him shows what it used to be.</item>
    /// <item>The dog's room is white without edges: bright ambient light, white fog and a white sky, so the walls
    /// disappear. The dog breathes in time with its snoring, twitches an ear and thumps its tail, and a loading spinner
    /// turns above it. Vis floats it (still asleep), Rotara turns it over and Lost Echoes shows where it came in.</item>
    /// </list>
    /// </summary>
    public sealed class SecretRoomMood : MonoBehaviour
    {
        /// <summary>Set when the forgotten man disappears; he is not there again, even after loading a save in the room.</summary>
        public const string ForgottenManGoneFlag = "secret.forgotten.gone";
        public const string EggTakenFlag = "inventory.misc.egg";
        internal static readonly Vector2Int DogBedCell = new Vector2Int(4, 4);
        internal static readonly Vector2Int HoleCell = new Vector2Int(7, 5);
        /// <summary>Walking back this far down the corridor (towards the door) after talking to the man sets off the blackout.</summary>
        internal const int VanishBelowRow = 8;

        private static SecretRoomMood current;

        private bool forgotten, glows;
        private Transform area;
        private readonly List<Object> owned = new List<Object>();
        private MaterialPropertyBlock block;

        // ---- Restored on leaving ----
        private AmbientMode ambientMode;
        private Color ambientLight;
        private SphericalHarmonicsL2 ambientProbe;
        private bool fog;
        private FogMode fogMode;
        private Color fogColour;
        private float fogDensity;
        private readonly List<(Light light, float intensity)> dimmed = new List<(Light, float)>();
        private Camera view;
        private CameraClearFlags clearFlags;
        private Color background;
        private bool changed;

        // ---- The forgotten room ----
        private sealed class Lamp
        {
            public Light Light;
            public Renderer Bulb;
            public float Intensity;
            public Color Colour;
            public float Level = 1f;
        }
        private readonly List<Lamp> lamps = new List<Lamp>();
        private Light doorway;
        private float doorwayIntensity;
        private Transform man;
        private GameObject egg;
        private RoomInspectable eggLook;
        private Renderer photo;
        private Color photoFaded;
        private Texture2D photoTexture;
        private float revealedAt = -100f;
        private bool talking, talked, gone;
        private float vanishAt = -1f;
        private AudioSource tone, ticks, clicks;
        private AudioClip tick, tock, lampClick, remember;
        private float nextTick;
        private bool tockNext;
        private float flickerLevel = 1f;
        private readonly List<Mote> motes = new List<Mote>();

        private sealed class Mote
        {
            public Transform Transform;
            public Renderer Renderer;
            public Vector3 Home;
            public float Phase, Speed, Size;
        }

        // ---- The dog's room ----
        private Transform dog, body, head, tail, spinner;
        private readonly List<Transform> ears = new List<Transform>();
        private Vector3 dogHome, bodyScale, headPosition;
        private Quaternion tailRotation;
        private readonly List<Quaternion> earRotations = new List<Quaternion>();
        private AudioSource snore, effects;
        private AudioClip boopUp, boopDown;
        private float floatedAt = -100f, turnedAt = -100f, pawsAt = -100f;
        private bool settled = true;
        private float nextTwitch, twitchAt = -100f, nextThump, thumpAt = -100f;
        private int twitchEar;
        private readonly List<(Transform transform, Renderer renderer, float delay)> paws = new List<(Transform, Renderer, float)>();
        private const float FloatSeconds = 5f, TurnSeconds = 1.4f, PawSeconds = 6.5f;

        /// <summary>Called on every map load (from <see cref="SecretRooms.RegisterForMap"/>): ends the last room's mood and starts this one's.</summary>
        public static void OnMapLoaded(string mapId, GameObject areaRoot)
        {
            if (current != null)
            {
                current.Restore();
                Destroy(current.gameObject);
                current = null;
            }
            bool isForgotten = string.Equals(mapId, SecretRooms.ForgottenRoomId, System.StringComparison.OrdinalIgnoreCase);
            bool isDog = string.Equals(mapId, SecretRooms.DogRoomId, System.StringComparison.OrdinalIgnoreCase);
            if (areaRoot == null || (!isForgotten && !isDog) || !Application.isPlaying) return;
            GameObject host = new GameObject("[Secret Room Mood]");
            DontDestroyOnLoad(host);
            current = host.AddComponent<SecretRoomMood>();
            current.forgotten = isForgotten;
            current.area = areaRoot.transform;
            current.Begin();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => current = null;

        private static GameEventRuntime Runtime => GameEventManager.Instance != null ? GameEventManager.Instance.Runtime : null;

        private void Begin()
        {
            block = new MaterialPropertyBlock();
            // The dust and the paw prints are glows; without the glow shader the rooms simply go without them.
            glows = CutsceneArt.Ensure();
            if (forgotten)
            {
                SetLight(new Color(0.05f, 0.05f, 0.07f), 0.08f, FogMode.Exponential, new Color(0.02f, 0.02f, 0.03f), 0.035f, new Color(0.012f, 0.012f, 0.018f));
                FindForgottenRoom();
                BuildForgottenAudio();
                if (glows) BuildMotes();
            }
            else
            {
                SetLight(new Color(0.94f, 0.94f, 0.96f), 0.5f, FogMode.Exponential, Color.white, 0.02f, Color.white);
                FindDogRoom();
                BuildDogAudio();
            }
            SpellCaster.CastAttempted += OnCastAttempted;
        }

        private void OnDestroy()
        {
            SpellCaster.CastAttempted -= OnCastAttempted;
            if (current == this) current = null;
            Restore();
            foreach (Mote mote in motes) if (mote.Transform != null) Destroy(mote.Transform.gameObject);
            foreach ((Transform transform, Renderer _, float _) in paws) if (transform != null) Destroy(transform.gameObject);
            foreach (Object item in owned) if (item != null) Destroy(item);
        }

        // ---- Light, fog and sky ----

        private void SetLight(Color ambient, float sunScale, FogMode mode, Color fogTint, float density, Color sky)
        {
            ambientMode = RenderSettings.ambientMode;
            ambientLight = RenderSettings.ambientLight;
            ambientProbe = RenderSettings.ambientProbe;
            fog = RenderSettings.fog;
            fogMode = RenderSettings.fogMode;
            fogColour = RenderSettings.fogColor;
            fogDensity = RenderSettings.fogDensity;
            view = Camera.main;
            if (view != null) { clearFlags = view.clearFlags; background = view.backgroundColor; }
            changed = true;

            // The sun (and any other daylight) is turned down, not off, so the player and the room still read.
            foreach (Light light in FindObjectsByType<Light>(FindObjectsInactive.Exclude))
            {
                if (light.type != LightType.Directional || !light.enabled || (area != null && light.transform.IsChildOf(area))) continue;
                dimmed.Add((light, light.intensity));
                light.intensity *= sunScale;
            }
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = ambient;
            SphericalHarmonicsL2 probe = new SphericalHarmonicsL2();
            probe.AddAmbientLight(ambient);
            RenderSettings.ambientProbe = probe;
            RenderSettings.fog = true;
            RenderSettings.fogMode = mode;
            RenderSettings.fogColor = fogTint;
            RenderSettings.fogDensity = density;
            if (view != null)
            {
                view.clearFlags = CameraClearFlags.SolidColor;
                view.backgroundColor = sky;
            }
        }

        private void Restore()
        {
            if (!changed) return;
            changed = false;
            foreach ((Light light, float intensity) in dimmed) if (light != null) light.intensity = intensity;
            dimmed.Clear();
            RenderSettings.ambientMode = ambientMode;
            RenderSettings.ambientLight = ambientLight;
            RenderSettings.ambientProbe = ambientProbe;
            RenderSettings.fog = fog;
            RenderSettings.fogMode = fogMode;
            RenderSettings.fogColor = fogColour;
            RenderSettings.fogDensity = fogDensity;
            if (view != null) { view.clearFlags = clearFlags; view.backgroundColor = background; }
        }

        // ---- Each frame ----

        private void Update()
        {
            if (area == null) { Destroy(gameObject); return; }
            if (forgotten) UpdateForgottenRoom();
            else UpdateDogRoom();
        }

        private void LateUpdate()
        {
            Camera camera = Camera.main;
            if (camera == null) return;
            Quaternion facing = camera.transform.rotation;
            foreach (Mote mote in motes) if (mote.Transform != null) mote.Transform.rotation = facing;
        }

        // ================= The forgotten room =================

        private void FindForgottenRoom()
        {
            foreach (Transform child in area.GetComponentsInChildren<Transform>(true))
            {
                if (child.name.StartsWith(SecretRooms.PendantPrefix, System.StringComparison.Ordinal) && child.TryGetComponent(out Light light))
                {
                    Transform bulb = child.Find("Bulb");
                    lamps.Add(new Lamp { Light = light, Bulb = bulb != null ? bulb.GetComponent<Renderer>() : null, Intensity = light.intensity, Colour = light.color });
                }
                else if (child.name == SecretRooms.DoorwayLightName && child.TryGetComponent(out Light door)) { doorway = door; doorwayIntensity = door.intensity; }
                else if (child.name == SecretRooms.ForgottenManGroupName) man = child;
                else if (child.name == SecretRooms.EggName) egg = child.gameObject;
                else if (child.name == SecretRooms.EggLookName) eggLook = child.GetComponent<RoomInspectable>();
                else if (child.name == SecretRooms.PhotoName) photo = child.GetComponent<Renderer>();
            }
            lamps.Sort((a, b) => a.Light.transform.position.z.CompareTo(b.Light.transform.position.z));
            if (photo != null) photoFaded = photo.material.color;

            GameEventRuntime runtime = Runtime;
            gone = runtime != null && runtime.GetFlag(ForgottenManGoneFlag);
            if (gone && man != null) man.gameObject.SetActive(false);
            RefreshEgg();
            nextTick = Time.time + 1f;
        }

        private void BuildForgottenAudio()
        {
            tone = Source(true);
            tone.clip = Keep(SecretRoomAudio.RoomTone());
            tone.volume = 0f;
            AudioPlayback.Play(tone);
            ticks = Source(false);
            clicks = Source(false);
            tick = Keep(SecretRoomAudio.Tick(false));
            tock = Keep(SecretRoomAudio.Tick(true));
            lampClick = Keep(SecretRoomAudio.LampClick());
            remember = Keep(SecretRoomAudio.Remember());
        }

        // Dust turning slowly in the corridor, seen only where a lamp lights it.
        private void BuildMotes()
        {
            System.Random random = new System.Random(61);
            if (!SecretRooms.TryGetRoom(SecretRooms.ForgottenRoomId, out SecretRooms.Room room)) return;
            for (int i = 0; i < 46; i++)
            {
                Transform quad = CutsceneArt.Quad(area, "Dust", CutsceneArt.GlowMaterial, out Renderer renderer);
                Vector3 home = new Vector3(Range(random, -0.3f, room.Width - 0.7f), Range(random, 0.5f, 2.3f), Range(random, 1.5f, room.Height - 1.5f));
                quad.localPosition = home;
                motes.Add(new Mote { Transform = quad, Renderer = renderer, Home = home, Phase = Range(random, 0f, 100f), Speed = Range(random, 0.05f, 0.14f), Size = Range(random, 0.025f, 0.05f) });
            }
        }

        private void UpdateForgottenRoom()
        {
            GameEventRuntime runtime = Runtime;
            GridMover player = GridMover.FindActive();
            float now = Time.time;

            // Who the player is talking to: the clock holds its breath while the man speaks.
            string dialogue = runtime?.ActiveDialogue?.Definition?.id;
            bool withMan = dialogue == DevChibis.OldMan.DialogueId || dialogue == NpcIntroductions.ChatDialoguePrefix + DevChibis.OldMan.DialogueId;
            if (withMan) talking = true;
            else if (talking) { talking = false; talked = true; }

            if (!gone && talked && vanishAt < 0f && player != null && !player.IsMovementLocked && player.CurrentCell.y <= VanishBelowRow)
            {
                vanishAt = now;
                Cue(clicks, lampClick, 0.6f);
            }
            float blackout = 1f;
            if (vanishAt >= 0f)
            {
                float since = now - vanishAt;
                blackout = BlackoutLevel(since);
                if (!gone && since >= 0.35f) Vanish(runtime);
                if (since > 1.6f) vanishAt = -1f;
            }

            // The second lamp is failing; its stutters click.
            float previous = flickerLevel;
            flickerLevel = FailingLampLevel(now);
            if (previous > 0.3f && flickerLevel <= 0.3f && blackout > 0.5f) Cue(clicks, lampClick, 0.18f);
            for (int i = 0; i < lamps.Count; i++)
            {
                Lamp lamp = lamps[i];
                float level = (i == SecretRooms.FailingPendant ? flickerLevel : 1f) * blackout;
                lamp.Level = level;
                lamp.Light.intensity = lamp.Intensity * level;
                if (lamp.Bulb != null && lamp.Intensity > 0f)
                    lamp.Bulb.material.SetColor("_EmissionColor", lamp.Colour * (2.2f * level));
            }
            if (doorway != null) doorway.intensity = doorwayIntensity * blackout;

            // The building's hum, and the clock: still while he talks, and gone with him.
            tone.volume = 0.32f * AudioMixSettings.Ambience * (0.5f + 0.5f * blackout);
            bool clockRuns = !gone && !withMan && Time.timeScale > 0f;
            if (now >= nextTick)
            {
                nextTick = now + 1f;
                if (clockRuns)
                {
                    Cue(ticks, tockNext ? tock : tick, 0.16f);
                    tockNext = !tockNext;
                }
            }

            UpdatePhoto(now);
            RefreshEgg();
            TickMotes(now);
        }

        /// <summary>The failing lamp's brightness at <paramref name="time"/>: steady, with now and then a burst of stutters.</summary>
        internal static float FailingLampLevel(float time)
        {
            float trouble = Mathf.PerlinNoise(time * 0.55f, 3.1f);
            if (trouble > 0.7f) return Mathf.PerlinNoise(time * 22f, 1.7f) > 0.48f ? 0.95f : 0.06f;
            return 0.88f + 0.12f * Mathf.PerlinNoise(time * 5f, 0.4f);
        }

        /// <summary>Every lamp's brightness <paramref name="since"/> seconds into the blackout: out, a stutter, out, and slowly back.</summary>
        internal static float BlackoutLevel(float since)
        {
            if (since < 0f) return 1f;
            if (since < 0.12f) return 0f;
            if (since < 0.22f) return 0.55f;
            if (since < 1.05f) return 0f;
            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((since - 1.05f) / 0.5f));
        }

        // In the dark, he goes. An egg he had not given away stays behind, warm.
        private void Vanish(GameEventRuntime runtime)
        {
            gone = true;
            runtime?.SetFlag(ForgottenManGoneFlag, true);
            if (man != null) man.gameObject.SetActive(false);
            RefreshEgg();
        }

        private void RefreshEgg()
        {
            bool taken = Runtime != null && Runtime.GetFlag(EggTakenFlag);
            if (egg != null && egg.activeSelf == taken) egg.SetActive(!taken);
            bool lookable = gone && !taken;
            if (eggLook != null && eggLook.enabled != lookable) eggLook.enabled = lookable;
        }

        private void TickMotes(float now)
        {
            foreach (Mote mote in motes)
            {
                if (mote.Transform == null) continue;
                float t = now * mote.Speed + mote.Phase;
                Vector3 drift = new Vector3(Mathf.Sin(t * 1.3f), Mathf.Sin(t * 0.7f + 1f) * 0.6f, Mathf.Cos(t * 1.1f)) * 0.35f;
                mote.Transform.localPosition = mote.Home + drift;
                mote.Transform.localScale = Vector3.one * mote.Size;
                // Bright only in a lit lamp's pool.
                float lit = 0f;
                foreach (Lamp lamp in lamps)
                {
                    if (lamp.Intensity <= 0f) continue;
                    float distance = Vector3.Distance(mote.Transform.position, lamp.Light.transform.position);
                    lit += lamp.Level * Mathf.Clamp01(1f - distance / 1.8f);
                }
                CutsceneArt.Tint(mote.Renderer, block, new Color(1f, 0.93f, 0.8f), Mathf.Clamp01(lit) * 0.55f);
            }
        }

        // ---- The photograph (Lost Echoes) ----

        private void RevealPhoto()
        {
            if (photo == null) return;
            if (photoTexture == null)
            {
                photoTexture = PhotoTexture();
                owned.Add(photoTexture);
            }
            revealedAt = Time.time;
            Material material = photo.material;
            material.SetTexture("_BaseMap", photoTexture);
            material.mainTexture = photoTexture;
            Cue(clicks, remember, 0.5f);
            PortalBlockedPopup.Show(gone
                ? "For a moment the photograph remembers: a sunny lawn, a man with a walking stick, and a small white dog. Then it forgets again."
                : "For a moment the photograph remembers: a sunny lawn, a man with a walking stick, and a small white dog at his feet.", "Lost Echoes", 5f);
        }

        // Developing from grey to full colour, held, then fading back to nothing.
        private void UpdatePhoto(float now)
        {
            if (photo == null || photoTexture == null) return;
            float since = now - revealedAt;
            float shown = since < 1.2f ? since / 1.2f : since < 5.5f ? 1f : 1f - (since - 5.5f) / 1.8f;
            Material material = photo.material;
            if (shown <= 0f)
            {
                material.SetTexture("_BaseMap", null);
                material.mainTexture = null;
                material.color = photoFaded;
                material.SetColor("_EmissionColor", photoFaded * 0.04f);
                return;
            }
            shown = Mathf.SmoothStep(0f, 1f, shown);
            material.color = Color.Lerp(photoFaded, Color.white, shown);
            material.SetColor("_EmissionColor", new Color(1f, 0.9f, 0.7f) * (0.35f * shown));
        }

        // A small, sun-faded photograph: a lawn, a tree, a man with a stick and a small white dog.
        private static Texture2D PhotoTexture()
        {
            const int width = 48, height = 36;
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = "Forgotten Photograph", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            System.Random grain = new System.Random(67);
            Color[] pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    Color colour = y < 13
                        ? Color.Lerp(new Color(0.36f, 0.52f, 0.24f), new Color(0.5f, 0.66f, 0.32f), y / 13f)
                        : Color.Lerp(new Color(0.96f, 0.9f, 0.74f), new Color(0.62f, 0.78f, 0.92f), (y - 13f) / (height - 13f));
                    if (Circle(x, y, 40, 30, 3.5f)) colour = new Color(1f, 0.96f, 0.82f);
                    // A tree on the left.
                    if (x >= 6 && x <= 8 && y >= 12 && y <= 21) colour = new Color(0.36f, 0.25f, 0.16f);
                    if (Circle(x, y, 7, 25, 6f)) colour = new Color(0.22f, 0.4f, 0.2f);
                    // The man: coat, head, white hair, and his stick.
                    if (x >= 24 && x <= 28 && y >= 8 && y <= 21) colour = new Color(0.38f, 0.32f, 0.28f);
                    if (x >= 25 && x <= 27 && y >= 4 && y <= 8) colour = new Color(0.25f, 0.22f, 0.22f);
                    if (Circle(x, y, 26, 24, 3f)) colour = y >= 26 ? new Color(0.94f, 0.94f, 0.92f) : new Color(0.9f, 0.77f, 0.64f);
                    if (x == 30 && y >= 4 && y <= 15) colour = new Color(0.42f, 0.3f, 0.2f);
                    // The dog at his feet, white, with dark ears.
                    if (Ellipse(x, y, 35, 7, 3.5f, 2f)) colour = new Color(0.98f, 0.98f, 0.96f);
                    if (Circle(x, y, 38, 9, 2f)) colour = new Color(0.98f, 0.98f, 0.96f);
                    if ((x == 37 || x == 39) && y == 11) colour = new Color(0.12f, 0.12f, 0.12f);
                    // Sepia, grain and a soft dark edge, like an old print.
                    float grey = colour.grayscale;
                    colour = Color.Lerp(colour, new Color(grey * 1.05f, grey * 0.92f, grey * 0.74f), 0.4f);
                    float edge = Mathf.Min(Mathf.Min(x, width - 1 - x) / 6f, Mathf.Min(y, height - 1 - y) / 5f);
                    colour *= Mathf.Lerp(0.72f, 1f, Mathf.Clamp01(edge)) * (0.94f + 0.06f * (float)grain.NextDouble());
                    colour.a = 1f;
                    pixels[y * width + x] = colour;
                }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private static bool Circle(int x, int y, float cx, float cy, float r) => (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r;
        private static bool Ellipse(int x, int y, float cx, float cy, float rx, float ry) => (x - cx) * (x - cx) / (rx * rx) + (y - cy) * (y - cy) / (ry * ry) <= 1f;

        // ================= The dog's room =================

        private void FindDogRoom()
        {
            foreach (Transform child in area.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == SecretRooms.SleepingDogName) dog = child;
                else if (child.name == SecretRooms.LoadingSpinnerName) spinner = child;
            }
            if (dog == null) return;
            dogHome = dog.localPosition;
            body = dog.Find("Body");
            head = dog.Find("Head");
            tail = dog.Find("Tail");
            foreach (Transform child in dog) if (child.name == "Ear") { ears.Add(child); earRotations.Add(child.localRotation); }
            if (body != null) bodyScale = body.localScale;
            if (head != null) headPosition = head.localPosition;
            if (tail != null) tailRotation = tail.localRotation;
            nextTwitch = Time.time + 4f;
            nextThump = Time.time + 7f;
        }

        private void BuildDogAudio()
        {
            snore = Source(true);
            snore.clip = Keep(SecretRoomAudio.Snore());
            snore.volume = 0f;
            AudioPlayback.Play(snore);
            effects = Source(false);
            boopUp = Keep(SecretRoomAudio.Boop(true));
            boopDown = Keep(SecretRoomAudio.Boop(false));
        }

        private void UpdateDogRoom()
        {
            float now = Time.time;
            if (spinner != null)
                // A loading spinner steps round like the real thing rather than turning smoothly.
                spinner.localRotation = Quaternion.Euler(0f, 0f, -45f * Mathf.Floor(now * 10f % 8f));
            if (dog == null) return;

            GridMover player = GridMover.FindActive();
            float distance = player != null ? Vector3.Distance(player.transform.position, dog.position) : 6f;
            snore.volume = 0.55f * AudioMixSettings.Sfx * Mathf.Clamp01(1.3f - distance / 7f);

            // Breathing with the snore it is making.
            float breath = SecretRoomAudio.Breath(snore.isPlaying ? snore.time : now);
            if (body != null) body.localScale = Vector3.Scale(bodyScale, new Vector3(1f + 0.05f * breath, 1f + 0.12f * breath, 1f + 0.05f * breath));
            if (head != null) head.localPosition = headPosition + Vector3.up * (0.015f * breath);

            // Now and then an ear twitches or the tail thumps once.
            if (now >= nextTwitch) { twitchAt = now; twitchEar = Random.Range(0, Mathf.Max(1, ears.Count)); nextTwitch = now + Random.Range(5f, 11f); }
            for (int i = 0; i < ears.Count; i++)
            {
                float twitch = i == twitchEar ? Pulse(now - twitchAt, 0.35f) : 0f;
                ears[i].localRotation = earRotations[i] * Quaternion.Euler(0f, 0f, 28f * twitch);
            }
            if (now >= nextThump) { thumpAt = now; nextThump = now + Random.Range(8f, 15f); }
            if (tail != null) tail.localRotation = tailRotation * Quaternion.Euler(0f, 0f, -40f * Pulse(now - thumpAt, 0.3f));

            // Floating (Vis) and turning over (Rotara), still asleep through both.
            float sinceFloat = now - floatedAt;
            float lift = sinceFloat < FloatSeconds ? Mathf.SmoothStep(0f, 1f, Mathf.Min(sinceFloat / 0.8f, (FloatSeconds - sinceFloat) / 0.8f)) : 0f;
            if (!settled && sinceFloat >= FloatSeconds - 0.6f) { settled = true; Cue(effects, boopDown, 0.4f); }
            float bob = lift * Mathf.Sin(now * 2.2f) * 0.05f;
            dog.localPosition = dogHome + Vector3.up * (0.65f * lift + bob);
            float turn = Mathf.Clamp01((now - turnedAt) / TurnSeconds);
            dog.localRotation = Quaternion.Euler(0f, 360f * Mathf.SmoothStep(0f, 1f, turn), 0f);

            UpdatePaws(now);
        }

        // Up and back down once over [0, seconds].
        private static float Pulse(float since, float seconds) => since < 0f || since > seconds ? 0f : Mathf.Sin(since / seconds * Mathf.PI);

        // Faint paw prints, from the hole in the floor to the dog's bed, appearing one after another and fading.
        private void ShowPaws()
        {
            foreach ((Transform transform, Renderer _, float _) in paws) if (transform != null) Destroy(transform.gameObject);
            paws.Clear();
            pawsAt = Time.time;
            Vector3 from = new Vector3(HoleCell.x, 0.02f, HoleCell.y), to = new Vector3(DogBedCell.x + 0.6f, 0.02f, DogBedCell.y);
            Vector3 along = (to - from).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, along);
            Quaternion flat = Quaternion.LookRotation(Vector3.down, along);
            int steps = Mathf.Max(2, Mathf.RoundToInt(Vector3.Distance(from, to) / 0.32f));
            for (int i = 0; i <= steps; i++)
            {
                Vector3 point = Vector3.Lerp(from, to, i / (float)steps) + side * ((i % 2 == 0 ? 1f : -1f) * 0.09f);
                AddPawPart(point, 0.13f, flat, i * 0.16f);
                for (int toe = -1; toe <= 1; toe++) AddPawPart(point + along * 0.1f + side * (toe * 0.055f), 0.055f, flat, i * 0.16f);
            }
        }

        private void AddPawPart(Vector3 localPosition, float size, Quaternion rotation, float delay)
        {
            Transform quad = CutsceneArt.Quad(area, "Paw Print", CutsceneArt.GlowMaterial, out Renderer renderer);
            quad.localPosition = localPosition;
            quad.localRotation = rotation;
            quad.localScale = Vector3.one * size;
            paws.Add((quad, renderer, delay));
        }

        private void UpdatePaws(float now)
        {
            if (paws.Count == 0) return;
            float since = now - pawsAt;
            if (since > PawSeconds + 3f)
            {
                foreach ((Transform transform, Renderer _, float _) in paws) if (transform != null) Destroy(transform.gameObject);
                paws.Clear();
                return;
            }
            foreach ((Transform transform, Renderer renderer, float delay) in paws)
            {
                if (transform == null) continue;
                float local = since - delay;
                float alpha = local <= 0f ? 0f : Mathf.Clamp01(local / 0.25f) * Mathf.Clamp01((PawSeconds - local) / 1.5f);
                CutsceneArt.Tint(renderer, block, new Color(0.55f, 0.72f, 1f), alpha * 0.8f);
            }
        }

        // ================= Spells =================

        private void OnCastAttempted(SpellCaster.CastAttempt cast)
        {
            string mapId = forgotten ? SecretRooms.ForgottenRoomId : SecretRooms.DogRoomId;
            if (!string.Equals(cast.MapId, mapId, System.StringComparison.OrdinalIgnoreCase)) return;
            if (forgotten)
            {
                // Lost Echoes from the far end of the corridor, by the photograph.
                if (cast.Spell == SpellId.LostEchoes && cast.Cell.y >= SecretRooms.PhotoRow)
                {
                    RevealPhoto();
                    cast.Handled = true;
                }
                return;
            }
            float now = Time.time;
            bool nearDog = Mathf.Max(Mathf.Abs(cast.Cell.x - DogBedCell.x), Mathf.Abs(cast.Cell.y - DogBedCell.y)) <= 2;
            if (cast.Spell == SpellId.Vis && nearDog && now - floatedAt > FloatSeconds)
            {
                floatedAt = now;
                settled = false;
                SpellSounds.Play(SpellSounds.VisUpPath);
                Cue(effects, boopUp, 0.4f);
                PortalBlockedPopup.Show("The dog floats up, fast asleep. It is very good at this.", "Vis", 3.5f);
                cast.Handled = true;
            }
            else if (cast.Spell == SpellId.Rotara && nearDog && now - turnedAt > TurnSeconds)
            {
                turnedAt = now;
                SpellSounds.Play(SpellSounds.RotaraPath);
                PortalBlockedPopup.Show("The dog turns all the way round in its sleep and ends up exactly where it started.", "Rotara", 3.5f);
                cast.Handled = true;
            }
            else if (cast.Spell == SpellId.LostEchoes)
            {
                if (glows) ShowPaws();
                PortalBlockedPopup.Show("Faint paw prints: out of the hole in the floor and straight to the scrolls. Nowhere else.", "Lost Echoes", 4.5f);
                cast.Handled = true;
            }
        }

        // ================= Helpers =================

        private AudioSource Source(bool loop)
        {
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0f;
            return source;
        }

        private AudioClip Keep(AudioClip clip)
        {
            owned.Add(clip);
            return clip;
        }

        private static void Cue(AudioSource source, AudioClip clip, float volume)
        {
            if (source != null && clip != null) AudioPlayback.PlayOneShot(source, clip, volume * AudioMixSettings.Sfx);
        }

        private static float Range(System.Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);
    }
}
