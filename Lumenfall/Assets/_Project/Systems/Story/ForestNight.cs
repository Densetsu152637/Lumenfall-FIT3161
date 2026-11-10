using System.Collections.Generic;
using Lumenfall.Audio;
using Lumenfall.Cutscenes;
using Lumenfall.DataDriven;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Rendering;
using Lumenfall.Startup;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lumenfall.Story
{
    /// <summary>
    /// The west forest as the player first wakes in it, from the game design document: dark, with a small light that
    /// leads the way to the academy. From the moment the opening's light gathers until the gate scene
    /// (<see cref="LateToOrientationScene"/>) has played, the forest is night: the sun is dimmed, the ambient light
    /// turns blue-black and fog closes in. It lifts the nearer the player gets to the gate (<see cref="Darkness"/>),
    /// where the academy's warm light shows above the trees. A soft light keeps the ground around the player readable.
    /// <para>
    /// The wisp appears as the player wakes and floats a few cells ahead of them along the way to the gate (a
    /// <see cref="GridPathfinder"/> path from wherever they stand), bobbing and shedding motes. It waits when they stop
    /// and comes back for them if they wander off, and rises into the gate's light when the gate scene starts. Off the
    /// trail stands a little tree of what look like oranges (<see cref="OrangeTreeDialogue"/>).
    /// </para>
    /// Everything changed is recorded first and put back as the night lifts, and the night steps aside while the
    /// prologue plays (it sets its own). It is presentation only: nothing here is saved.
    /// </summary>
    public sealed class ForestNight : MonoBehaviour
    {
        public const string MapId = LateToOrientationScene.MapId;
        public const string OrangeTreeDialogue = "forest_orange_tree";
        /// <summary>Darkest at the opening's spawn, lightest at the gate.</summary>
        public const int DarkestRow = 6, GateRow = 46;
        /// <summary>Where the wisp leads: just short of Kepler at the gate.</summary>
        public static readonly Vector2Int WispGoal = new Vector2Int(17, 42);
        /// <summary>How many cells of the way ahead of the player the wisp floats, so it leads rather than tags along.</summary>
        public const int WispLead = 6;
        /// <summary>How quickly the wisp catches up with its place ahead: smoothing time in seconds, and top speed in units a second.</summary>
        public const float WispSmoothing = 0.18f, WispTopSpeed = 20f;
        /// <summary>How far along the first path from the spawn the orange tree stands, beside it.</summary>
        public const int OrangeTreeAlong = 9;

        private const float BlendSeconds = 1.4f;
        private static readonly Color NightAmbient = new Color(0.05f, 0.06f, 0.12f);
        private static readonly Color NightFog = new Color(0.025f, 0.035f, 0.07f);
        private static readonly Color WispColour = new Color(0.72f, 0.9f, 1f);
        private static readonly Color AcademyGlow = new Color(1f, 0.78f, 0.45f);

        private static ForestNight instance;

        private float weight;
        private bool applied;
        // Recorded before the night changes anything.
        private SphericalHarmonicsL2 dayProbe;
        private bool dayFog;
        private FogMode dayFogMode;
        private Color dayFogColour;
        private float dayFogDensity, dayFogStart, dayFogEnd;
        private readonly List<(Light light, float intensity)> suns = new List<(Light, float)>();

        private Transform root;
        private Light playerLight, gateLight;
        private MaterialPropertyBlock block;

        private Transform wisp;
        private Renderer wispCore, wispHalo, wispSpark;
        private Light wispLight;
        private float wispAlpha, wispRise;
        private Vector3 wispVelocity;
        private bool wispPlaced;
        private Vector2Int pathFrom = new Vector2Int(int.MinValue, 0);
        private float pathAge;
        private readonly List<Vector2Int> path = new List<Vector2Int>();
        private Vector3 wispTarget;
        private readonly List<Mote> motes = new List<Mote>();
        private float nextMote;
        private AudioSource chime;

        private GameObject orangeTree;
        private GameObject treeArea;

        private sealed class Mote
        {
            public Transform Transform;
            public Renderer Renderer;
            public Vector3 Velocity;
            public float Born, Life;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            GameObject host = new GameObject("[Forest Night]");
            instance = host.AddComponent<ForestNight>();
            DontDestroyOnLoad(host);
        }

        /// <summary>How dark the forest is on a row, from 1 at the spawn to 0.4 at the gate.</summary>
        public static float Darkness(float row) => 1f - 0.6f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(DarkestRow, GateRow, row));

        /// <summary>Whether the forest is night: on the west forest, after the opening began and before the gate scene ends.</summary>
        public static bool IsNight(GameEventRuntime runtime, string mapId)
        {
            if (runtime == null || mapId != MapId) return false;
            if (runtime.GetFlag(LateToOrientationScene.MetFlag)) return CutscenePlayer.CurrentId == LateToOrientationScene.CutsceneId;
            return true;
        }

        private void Awake()
        {
            useGUILayout = false;
            block = new MaterialPropertyBlock();
            PrologueScene.Starting += StepAside;
        }

        private void OnDestroy()
        {
            PrologueScene.Starting -= StepAside;
            Restore();
            if (root != null) Destroy(root.gameObject);
        }

        // The prologue records the lights as it finds them, so the night is taken down first.
        private void StepAside()
        {
            weight = 0f;
            Restore();
            HideWisp();
        }

        private void Update()
        {
            DataDrivenWorldLoader loader = DataDrivenWorldLoader.Instance;
            GameEventRuntime runtime = GameEventManager.Instance?.Runtime;
            string mapId = loader != null && loader.LoadedMap != null ? loader.LoadedMap.id : null;
            bool night = IsNight(runtime, mapId) && !PrologueScene.IsPlaying && !(StartupMenu.IsOpen && !StartupMenu.IsClosing);
            float step = Time.unscaledDeltaTime / BlendSeconds;
            weight = Mathf.MoveTowards(weight, night ? 1f : 0f, step);
            // Leaving the forest (or the menu opening over it) takes the night down at once.
            if (!night && (mapId != MapId || StartupMenu.IsOpen)) weight = 0f;

            GridMover player = StoryNpc.Player();
            if (weight <= 0f || player == null)
            {
                Restore();
                HideWisp();
                ClearMotes();
                if (mapId != MapId) RemoveTree();
                return;
            }

            if (!applied) Record();
            float dark = weight * Darkness(player.CurrentCell.y);
            Apply(dark, player);
            UpdateWisp(runtime, player);
            KeepTree(loader, player);
            UpdateMotes();
        }

        // ---- The night ----

        private void Record()
        {
            applied = true;
            dayProbe = RenderSettings.ambientProbe;
            dayFog = RenderSettings.fog;
            dayFogMode = RenderSettings.fogMode;
            dayFogColour = RenderSettings.fogColor;
            dayFogDensity = RenderSettings.fogDensity;
            dayFogStart = RenderSettings.fogStartDistance;
            dayFogEnd = RenderSettings.fogEndDistance;
            suns.Clear();
            foreach (Light light in FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                if (light.type == LightType.Directional && light.enabled && (root == null || !light.transform.IsChildOf(root)))
                    suns.Add((light, light.intensity));
            EnsureRoot();
        }

        private void Apply(float dark, GridMover player)
        {
            SphericalHarmonicsL2 night = new SphericalHarmonicsL2();
            night.AddAmbientLight(NightAmbient);
            RenderSettings.ambientProbe = dayProbe * (1f - dark) + night * dark;
            RenderSettings.fog = true;
            RenderSettings.fogMode = dayFog ? dayFogMode : FogMode.Exponential;
            RenderSettings.fogColor = Color.Lerp(dayFogColour, NightFog, dark);
            RenderSettings.fogDensity = Mathf.Lerp(dayFog ? dayFogDensity : 0f, 0.045f, dark);
            if (RenderSettings.fogMode == FogMode.Linear)
            {
                // Linear fog ignores density; pull its start and end in instead.
                RenderSettings.fogStartDistance = Mathf.Lerp(dayFogStart, 6f, dark);
                RenderSettings.fogEndDistance = Mathf.Lerp(dayFogEnd, 26f, dark);
            }
            foreach ((Light light, float intensity) in suns)
                if (light != null) light.intensity = intensity * (1f - 0.88f * dark);

            playerLight.transform.position = player.transform.position + Vector3.up * 2.2f;
            playerLight.intensity = 1.1f * dark;
            gateLight.intensity = 3.2f * weight;
            GridWorld world = player.World;
            if (world != null) gateLight.transform.position = world.CellToWorld(LateToOrientationScene.GateCell) + Vector3.up * 3.5f;
        }

        private void Restore()
        {
            if (!applied) return;
            applied = false;
            RenderSettings.ambientProbe = dayProbe;
            RenderSettings.fog = dayFog;
            RenderSettings.fogMode = dayFogMode;
            RenderSettings.fogColor = dayFogColour;
            RenderSettings.fogDensity = dayFogDensity;
            RenderSettings.fogStartDistance = dayFogStart;
            RenderSettings.fogEndDistance = dayFogEnd;
            foreach ((Light light, float intensity) in suns) if (light != null) light.intensity = intensity;
            suns.Clear();
            if (playerLight != null) playerLight.intensity = 0f;
            if (gateLight != null) gateLight.intensity = 0f;
        }

        private void EnsureRoot()
        {
            if (root != null) return;
            root = new GameObject("Forest Night Lights").transform;
            DontDestroyOnLoad(root.gameObject);
            playerLight = PointLight("Moonlight Round The Player", new Color(0.62f, 0.72f, 1f), 5f);
            gateLight = PointLight("Academy Glow", AcademyGlow, 16f);
        }

        private Light PointLight(string name, Color colour, float range)
        {
            GameObject host = new GameObject(name);
            host.transform.SetParent(root, false);
            Light light = host.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = colour;
            light.range = range;
            light.intensity = 0f;
            light.shadows = LightShadows.None;
            return light;
        }

        // ---- The wisp ----

        // It shows as the player wakes (the opening's first-thoughts dialogue) and while they walk; it rises away when
        // the gate scene starts, and hides for any other cutscene.
        private static bool WispWanted(GameEventRuntime runtime)
        {
            if (!CutscenePlayer.IsPlaying) return true;
            return CutscenePlayer.CurrentId == CutscenePlayer.OpeningCutsceneId && runtime?.ActiveDialogue?.Definition?.id == "opening_awakening";
        }

        private void UpdateWisp(GameEventRuntime runtime, GridMover player)
        {
            if (!CutsceneArt.Ensure()) return;
            bool atGate = CutscenePlayer.CurrentId == LateToOrientationScene.CutsceneId;
            bool wanted = WispWanted(runtime) || atGate;
            if (wisp == null)
            {
                if (!wanted) return;
                BuildWisp();
            }

            float dt = Time.unscaledDeltaTime;
            float time = Time.unscaledTime;
            if (!wispPlaced)
            {
                wispPlaced = true;
                wisp.position = player.transform.position + player.transform.forward * 2f + Vector3.up * 1.2f;
                wispVelocity = Vector3.zero;
                wispRise = 0f;
                if (chime != null && chime.clip != null) AudioPlayback.PlayOneShot(chime, chime.clip, 0.5f * AudioMixSettings.Sfx);
            }

            if (atGate)
            {
                // Up into the academy's light, and out.
                wispRise += dt;
                wisp.position += Vector3.up * dt * (1.2f + wispRise * 2f);
                wispAlpha = Mathf.MoveTowards(wispAlpha, 0f, dt / 1.4f);
            }
            else
            {
                wispAlpha = Mathf.MoveTowards(wispAlpha, wanted ? 1f : 0f, dt / 0.9f);
                if (!wanted && wispAlpha <= 0f) { HideWisp(); return; }
                wispTarget = WispTarget(player) + Vector3.up * (Mathf.Sin(time * 1.7f) * 0.12f);
                wisp.position = Vector3.SmoothDamp(wisp.position, wispTarget, ref wispVelocity, WispSmoothing, WispTopSpeed, dt);
            }

            Camera view = Camera.main;
            if (view != null) wisp.rotation = Quaternion.LookRotation(wisp.position - view.transform.position, view.transform.up);
            float pulse = 0.85f + 0.15f * Mathf.Sin(time * 3.1f) + 0.05f * Mathf.Sin(time * 11.3f);
            wispCore.transform.localScale = Vector3.one * 0.32f * pulse;
            wispHalo.transform.localScale = Vector3.one * 1.5f * (0.9f + 0.1f * Mathf.Sin(time * 1.3f));
            wispSpark.transform.localScale = Vector3.one * 0.7f * pulse;
            wispSpark.transform.localRotation = Quaternion.Euler(0f, 0f, time * 40f);
            CutsceneArt.Tint(wispCore, block, Color.white, wispAlpha);
            CutsceneArt.Tint(wispHalo, block, WispColour, 0.38f * wispAlpha);
            CutsceneArt.Tint(wispSpark, block, WispColour, 0.8f * wispAlpha);
            wispLight.intensity = 2.4f * wispAlpha * pulse;

            if (wispAlpha > 0.1f && time >= nextMote)
            {
                nextMote = time + 0.09f;
                SpawnMote(wisp.position);
            }
        }

        // Well ahead along the way to the gate from wherever the player stands, so it always leads. When they stop it waits
        // there, swaying a little further on and back, as if beckoning.
        private Vector3 WispTarget(GridMover player)
        {
            GridWorld world = player.World;
            Vector3 at = player.transform.position;
            if (world == null) return at + Vector3.up * 1.2f + Vector3.forward * 2f;
            Vector2Int cell = player.CurrentCell;
            pathAge += Time.unscaledDeltaTime;
            if (cell != pathFrom || pathAge > 1f)
            {
                pathFrom = cell;
                pathAge = 0f;
                if (!GridPathfinder.TryFindPath(world, cell, WispGoal, path, true, 16384, player)) path.Clear();
            }
            Vector3 ahead;
            if (path.Count > 0) ahead = world.CellToWorld(path[Mathf.Min(WispLead, path.Count) - 1]);
            else
            {
                Vector3 toGoal = world.CellToWorld(WispGoal) - at;
                toGoal.y = 0f;
                ahead = at + (toGoal.sqrMagnitude > 0.01f ? toGoal.normalized : Vector3.forward) * Mathf.Min(WispLead * world.CellSize, toGoal.magnitude);
            }
            ahead.y = at.y;
            Vector3 heading = ahead - at;
            heading.y = 0f;
            if (heading.sqrMagnitude > 0.01f && !player.IsMoving)
                ahead += heading.normalized * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2.4f)) * 0.8f;
            return ahead + Vector3.up * 1.35f;
        }

        private void BuildWisp()
        {
            EnsureRoot();
            wisp = new GameObject("Guiding Wisp").transform;
            wisp.SetParent(root, false);
            CutsceneArt.Quad(wisp, "Halo", CutsceneArt.GlowMaterial, out wispHalo);
            CutsceneArt.Quad(wisp, "Core", CutsceneArt.GlowMaterial, out wispCore);
            CutsceneArt.Quad(wisp, "Spark", CutsceneArt.SparkleMaterial, out wispSpark);
            wispHalo.transform.localPosition = new Vector3(0f, 0f, 0.02f);
            wispLight = wisp.gameObject.AddComponent<Light>();
            wispLight.type = LightType.Point;
            wispLight.color = WispColour;
            wispLight.range = 6.5f;
            wispLight.intensity = 0f;
            wispLight.shadows = LightShadows.None;
            chime = wisp.gameObject.AddComponent<AudioSource>();
            chime.playOnAwake = false;
            chime.spatialBlend = 0f;
            chime.clip = CutsceneArt.Clip("Wisp Chime", ChimeSamples());
            wispAlpha = 0f;
            wispPlaced = false;
        }

        private void HideWisp()
        {
            if (wisp == null) return;
            if (chime != null && chime.clip != null) Destroy(chime.clip);
            Destroy(wisp.gameObject);
            wisp = null;
            wispPlaced = false;
            wispAlpha = 0f;
            pathFrom = new Vector2Int(int.MinValue, 0);
        }

        private void SpawnMote(Vector3 at)
        {
            Transform mote = CutsceneArt.Quad(root, "Wisp Mote", CutsceneArt.GlowMaterial, out Renderer renderer);
            mote.position = at + Random.insideUnitSphere * 0.15f;
            mote.localScale = Vector3.one * Random.Range(0.06f, 0.12f);
            motes.Add(new Mote
            {
                Transform = mote,
                Renderer = renderer,
                Velocity = new Vector3(Random.Range(-0.15f, 0.15f), Random.Range(0.1f, 0.35f), Random.Range(-0.15f, 0.15f)),
                Born = Time.unscaledTime,
                Life = Random.Range(0.9f, 1.6f)
            });
        }

        private void ClearMotes()
        {
            foreach (Mote mote in motes) if (mote.Transform != null) Destroy(mote.Transform.gameObject);
            motes.Clear();
        }

        private void UpdateMotes()
        {
            Camera view = Camera.main;
            float now = Time.unscaledTime;
            for (int i = motes.Count - 1; i >= 0; i--)
            {
                Mote mote = motes[i];
                float age = (now - mote.Born) / mote.Life;
                if (mote.Transform == null || age >= 1f)
                {
                    if (mote.Transform != null) Destroy(mote.Transform.gameObject);
                    motes.RemoveAt(i);
                    continue;
                }
                mote.Transform.position += mote.Velocity * Time.unscaledDeltaTime;
                if (view != null) mote.Transform.rotation = Quaternion.LookRotation(mote.Transform.position - view.transform.position, view.transform.up);
                CutsceneArt.Tint(mote.Renderer, block, WispColour, Mathf.Sin(age * Mathf.PI) * 0.7f * wispAlpha);
            }
        }

        // A small bell, high and soft, as the light first appears.
        private static float[] ChimeSamples()
        {
            int length = Mathf.RoundToInt(CutsceneArt.SampleRate * 1.6f);
            float[] samples = new float[length];
            float[] notes = { 1318.5f, 1975.5f, 2637f };
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)CutsceneArt.SampleRate;
                float value = 0f;
                for (int n = 0; n < notes.Length; n++)
                {
                    float start = n * 0.09f;
                    if (t < start) continue;
                    float local = t - start;
                    value += Mathf.Sin(2f * Mathf.PI * notes[n] * local) * Mathf.Exp(-local * 3.2f) * Mathf.Min(1f, local * 200f) / (n + 1.5f);
                }
                samples[i] = value * 0.5f;
            }
            return samples;
        }

        // ---- The orange tree ----

        // Beside the way, a little past the spawn: picked once per visit from the first path the wisp finds.
        private void KeepTree(DataDrivenWorldLoader loader, GridMover player)
        {
            GameObject area = loader.CurrentAreaRoot;
            if (orangeTree != null && treeArea == area) return;
            if (area == null || player.World == null || path.Count == 0) return;
            RemoveTree();
            List<Vector2Int> way = new List<Vector2Int>(path);
            if (way.Count < OrangeTreeAlong + 2) return;
            if (!TryBesideTheWay(player.World, way, OrangeTreeAlong, out Vector2Int cell)) return;
            orangeTree = BuildOrangeTree(area.transform, player.World.CellToWorld(cell));
            // A tree, so it stands in the way like the others.
            GameObject blocker = new GameObject("Orange Tree obstacle");
            blocker.transform.SetParent(orangeTree.transform, false);
            blocker.AddComponent<GridObstacle>().Configure(player.World, Vector2Int.one);
            treeArea = area;
        }

        /// <summary>A free cell one or two steps to the side of the way at <paramref name="along"/>, not on it.</summary>
        internal static bool TryBesideTheWay(GridWorld world, List<Vector2Int> way, int along, out Vector2Int found)
        {
            HashSet<Vector2Int> onTheWay = new HashSet<Vector2Int>(way);
            Vector2Int at = way[along];
            Vector2Int heading = way[along + 1] - at;
            Vector2Int side = new Vector2Int(-heading.y, heading.x);
            if (side == Vector2Int.zero) side = Vector2Int.right;
            foreach (int distance in new[] { 2, -2, 3, -3 })
            {
                Vector2Int cell = at + side * distance;
                if (onTheWay.Contains(cell) || !world.IsCellAvailable(cell) || world.GetPortalAt(cell) != null) continue;
                found = cell;
                return true;
            }
            found = at;
            return false;
        }

        private void RemoveTree()
        {
            if (orangeTree != null) Destroy(orangeTree);
            orangeTree = null;
            treeArea = null;
        }

        private static GameObject BuildOrangeTree(Transform parent, Vector3 position)
        {
            GameObject tree = new GameObject("Orange Tree (it is an apple tree)");
            tree.transform.SetParent(parent, false);
            tree.transform.position = position;
            Color bark = new Color(0.36f, 0.24f, 0.16f), leaves = new Color(0.16f, 0.36f, 0.2f), orange = new Color(1f, 0.55f, 0.12f);
            Part(PrimitiveType.Cylinder, "Trunk", tree.transform, new Vector3(0f, 0.55f, 0f), new Vector3(0.18f, 0.55f, 0.18f), bark, 0f);
            Part(PrimitiveType.Sphere, "Crown", tree.transform, new Vector3(0f, 1.45f, 0f), new Vector3(1.25f, 1.05f, 1.25f), leaves, 0.05f);
            Part(PrimitiveType.Sphere, "Crown Top", tree.transform, new Vector3(0.15f, 1.95f, -0.05f), new Vector3(0.8f, 0.7f, 0.8f), leaves * 1.1f, 0.05f);
            Vector3[] fruit =
            {
                new Vector3(0.5f, 1.3f, -0.35f), new Vector3(-0.45f, 1.55f, -0.38f), new Vector3(0.1f, 1.15f, -0.58f),
                new Vector3(-0.2f, 1.85f, -0.42f), new Vector3(0.58f, 1.7f, 0.1f)
            };
            // A little glow of their own, so they catch the eye in the dark.
            foreach (Vector3 at in fruit) Part(PrimitiveType.Sphere, "Fruit", tree.transform, at, Vector3.one * 0.16f, orange, 0.45f);
            tree.AddComponent<RoomInspectable>().Configure(OrangeTreeDialogue);
            return tree;
        }

        private static void Part(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 scale, Color colour, float emission)
        {
            GameObject part = RuntimePrimitive.Create(type);
            part.name = name;
            Collider collider = part.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = scale;
            Material material = part.GetComponent<Renderer>().material;
            material.color = colour;
            material.SetColor("_EmissionColor", colour * emission);
            material.EnableKeyword("_EMISSION");
            material.SetFloat("_Smoothness", 0.15f);
        }
    }
}
