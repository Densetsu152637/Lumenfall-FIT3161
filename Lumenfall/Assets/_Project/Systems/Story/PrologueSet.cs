using System;
using System.Collections.Generic;
using Lumenfall.Cutscenes;
using Lumenfall.Rendering;
using UnityEngine;

namespace Lumenfall.Story
{
    /// <summary>
    /// The prologue's night-time set, built in code far from the game world on its own layer: a two-lane highway north
    /// through farmland, a dirt road leaving it to the west, posts, trees, hills, a fence and a field, a sky full of
    /// stars with a band of the galaxy and a moon, the student's car, the student and their phone, and the falling star.
    /// The car is low-poly primitives; the student is the player's own model in everyday clothes (<see cref="PrologueArt"/>),
    /// or, without it, a background NPC model recoloured into a hoodie (<see cref="DevChibis"/>). <see cref="PrologueScene"/>
    /// poses it all; this only builds it and tears it down.
    /// </summary>
    internal sealed class PrologueSet : IDisposable
    {
        /// <summary>Only the prologue's camera draws this layer (the sequence break's stage shares it, never at once).</summary>
        public const int Layer = 31;
        /// <summary>Far from every map, so nothing of the game is ever in shot.</summary>
        public static readonly Vector3 Origin = new Vector3(4000f, 0f, 4000f);
        public const float StudentHeight = 1.5f;
        public const float SkyRadius = 420f;

        private static readonly Color Asphalt = new Color(0.085f, 0.085f, 0.095f);
        private static readonly Color Dirt = new Color(0.2f, 0.155f, 0.11f);
        private static readonly Color Grass = new Color(0.06f, 0.105f, 0.07f);
        private static readonly Color Paint = new Color(0.82f, 0.8f, 0.66f);
        private static readonly Color CarPaint = new Color(0.25f, 0.35f, 0.53f);
        private static readonly Color Trim = new Color(0.06f, 0.06f, 0.07f);
        private static readonly Color Seat = new Color(0.16f, 0.16f, 0.18f);
        private static readonly Color Bark = new Color(0.12f, 0.085f, 0.06f);
        private static readonly Color Timber = new Color(0.3f, 0.23f, 0.16f);
        private static readonly Color HeadlightColour = new Color(1f, 0.95f, 0.82f);
        private static readonly Color PhoneGlow = new Color(0.68f, 0.8f, 1f);

        public readonly Transform Root;
        public Transform Car { get; private set; }
        public Transform DriverDoor { get; private set; }
        public readonly List<Transform> Wheels = new List<Transform>();
        public readonly List<Light> Headlights = new List<Light>();
        public Material TaillightMaterial { get; private set; }
        public Light TailGlow { get; private set; }
        public Light CabinLight { get; private set; }
        public Transform SteeringWheel { get; private set; }
        /// <summary>Where the student's hips rest on the driver's cushion, in the car's space.</summary>
        public static readonly Vector3 SeatHip = new Vector3(0.45f, 0.5f, 0f);
        /// <summary>
        /// Where the student's feet go so that their hips rest on the cushion, in the car's space. Their legs bend
        /// forward from the hips (see PrologueScene), which leaves the hips where they were.
        /// </summary>
        public Vector3 SeatPosition => SeatHip - Vector3.up * HipsHeight;
        /// <summary>The student's hips above their feet, and the top of their head above their hips, once scaled.</summary>
        public float HipsHeight { get; private set; }
        public float SittingHeight { get; private set; }
        /// <summary>The underside of the car's roof, raised to clear the student's (large, chibi) head as they sit.</summary>
        public float RoofHeight { get; private set; }

        public Transform Student { get; private set; }
        public Animator StudentAnimator { get; private set; }
        public Transform Head { get; private set; }
        public Transform RightHand { get; private set; }
        /// <summary>Left then right, bent by PrologueScene to sit the student down and put their hands on the wheel.</summary>
        public readonly List<Transform> Thighs = new List<Transform>(), Shins = new List<Transform>();
        public readonly List<Transform> UpperArms = new List<Transform>(), Forearms = new List<Transform>();

        public Transform Phone { get; private set; }
        public Transform PhoneMount { get; private set; }
        public Light PhoneLight { get; private set; }
        public Material PhoneScreen { get; private set; }

        public Transform Sky { get; private set; }
        public Light Moon { get; private set; }

        public Transform StarCore { get; private set; }
        public Transform StarHalo { get; private set; }
        public Renderer StarCoreRenderer { get; private set; }
        public Renderer StarHaloRenderer { get; private set; }
        public Light StarLight { get; private set; }
        public LineRenderer StarTrail { get; private set; }

        private readonly Dictionary<(Color, float), Material> materials = new Dictionary<(Color, float), Material>();
        private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        private readonly System.Random random = new System.Random(42);
        private Mesh starMesh;
        private Color[] starColours, starBase;
        private float[] starPhase, starSpeed;

        public PrologueSet()
        {
            Root = new GameObject("[Prologue Set]").transform;
            Root.position = Origin;
            BuildLand();
            BuildRoads();
            BuildRoadside();
            BuildField();
            BuildSky();
            Car = new GameObject("Car").transform;
            Car.SetParent(Root, false);
            // The student first: the car's cabin is sized to fit them.
            BuildStudent();
            BuildCar();
            BuildStar();
            foreach (Transform child in Root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = Layer;
        }

        /// <summary>Where the car comes to rest on the dirt road, and which way it faces.</summary>
        public static Vector3 RestPosition(out Vector3 forward, out Vector3 right)
        {
            Vector3 position = PrologueScript.CarPosition(PrologueScript.DriveLength, out forward);
            right = Vector3.Cross(Vector3.up, forward);
            return position;
        }

        /// <summary>A point in the set's space, in the world.</summary>
        public Vector3 World(Vector3 local) => Root.TransformPoint(local);

        /// <summary>Twinkles the stars: each brightens and dims at its own pace.</summary>
        public void Twinkle(float time)
        {
            if (starMesh == null) return;
            for (int s = 0; s < starPhase.Length; s++)
            {
                float flicker = 0.78f + 0.22f * Mathf.Sin(time * starSpeed[s] + starPhase[s]);
                Color colour = starBase[s];
                colour.a *= flicker;
                for (int v = 0; v < 4; v++) starColours[s * 4 + v] = colour;
            }
            starMesh.colors = starColours;
        }

        public void Dispose()
        {
            if (Root != null) UnityEngine.Object.Destroy(Root.gameObject);
            foreach (Material material in materials.Values) if (material != null) UnityEngine.Object.Destroy(material);
            foreach (UnityEngine.Object item in owned) if (item != null) UnityEngine.Object.Destroy(item);
            materials.Clear();
            owned.Clear();
        }

        // ---- Land and roads ----

        private void BuildLand()
        {
            Part(PrimitiveType.Cube, "Ground", Root, new Vector3(-40f, -0.06f, -60f), new Vector3(900f, 0.1f, 900f), Grass);
            // Low hills all round the horizon, mostly lost in the night haze.
            for (int i = 0; i < 14; i++)
            {
                float angle = i / 14f * Mathf.PI * 2f + Range(-0.15f, 0.15f);
                float distance = Range(190f, 330f);
                Vector3 at = new Vector3(Mathf.Cos(angle) * distance, Range(-14f, -6f), Mathf.Sin(angle) * distance - 60f);
                Part(PrimitiveType.Sphere, "Hill", Root, at, new Vector3(Range(140f, 220f), Range(34f, 58f), Range(140f, 220f)), new Color(0.045f, 0.06f, 0.08f));
            }
        }

        private void BuildRoads()
        {
            const float south = -220f, north = 80f;
            float length = north - south;
            Part(PrimitiveType.Cube, "Highway", Root, new Vector3(0f, -0.01f, (south + north) * 0.5f), new Vector3(7.2f, 0.04f, length), Asphalt);
            for (float z = south + 2f; z < north; z += 6f)
                Part(PrimitiveType.Cube, "Centre Dash", Root, new Vector3(0f, 0.016f, z), new Vector3(0.13f, 0.012f, 2.6f), Paint, 0.12f);
            Part(PrimitiveType.Cube, "Edge Line East", Root, new Vector3(3.35f, 0.016f, (south + north) * 0.5f), new Vector3(0.1f, 0.012f, length), Paint, 0.08f);
            // The west edge line breaks where the dirt road leaves.
            Part(PrimitiveType.Cube, "Edge Line West", Root, new Vector3(-3.35f, 0.016f, (south - 4f) * 0.5f), new Vector3(0.1f, 0.012f, -4f - south), Paint, 0.08f);
            Part(PrimitiveType.Cube, "Edge Line West", Root, new Vector3(-3.35f, 0.016f, (13f + north) * 0.5f), new Vector3(0.1f, 0.012f, north - 13f), Paint, 0.08f);

            // The dirt road: the bend the car takes, then west into the dark.
            Vector3 centre = new Vector3(PrologueScript.LaneX - PrologueScript.TurnRadius, 0f, 0f);
            const int pieces = 14;
            for (int i = 0; i < pieces; i++)
            {
                float from = i / (float)pieces * Mathf.PI * 0.5f, to = (i + 1) / (float)pieces * Mathf.PI * 0.5f;
                Vector3 a = centre + new Vector3(Mathf.Cos(from), 0f, Mathf.Sin(from)) * PrologueScript.TurnRadius;
                Vector3 b = centre + new Vector3(Mathf.Cos(to), 0f, Mathf.Sin(to)) * PrologueScript.TurnRadius;
                Vector3 middle = (a + b) * 0.5f;
                Part(PrimitiveType.Cube, "Dirt Road Bend", Root, new Vector3(middle.x, -0.02f, middle.z), new Vector3(3.6f, 0.04f, Vector3.Distance(a, b) + 0.3f), Dirt, 0f,
                    Quaternion.LookRotation(b - a));
            }
            float westEnd = -90f;
            Part(PrimitiveType.Cube, "Dirt Road", Root, new Vector3((centre.x + westEnd) * 0.5f, -0.02f, PrologueScript.TurnRadius), new Vector3(centre.x - westEnd, 0.04f, 3.4f), Dirt);
        }

        private void BuildRoadside()
        {
            // Guide posts on both verges, red reflectors on the left and white on the right, as on country roads.
            for (float z = -214f; z < 78f; z += 22f)
            {
                foreach (float side in new[] { -1f, 1f })
                {
                    if (side < 0f && z > -8f && z < 16f) continue;
                    Vector3 at = new Vector3(side * 4.3f, 0f, z);
                    Part(PrimitiveType.Cube, "Guide Post", Root, at + Vector3.up * 0.47f, new Vector3(0.09f, 0.95f, 0.09f), new Color(0.78f, 0.78f, 0.75f));
                    Color reflector = side < 0f ? new Color(1f, 0.22f, 0.16f) : new Color(1f, 0.97f, 0.9f);
                    Part(PrimitiveType.Cube, "Reflector", Root, at + new Vector3(0f, 0.82f, -0.05f), new Vector3(0.1f, 0.12f, 0.02f), reflector, 1.6f);
                }
            }

            Vector3 rest = RestPosition(out _, out _);
            for (int placed = 0, tries = 0; placed < 170 && tries < 2000; tries++)
            {
                float side = random.NextDouble() < 0.5 ? -1f : 1f;
                Vector3 at = new Vector3(side * Range(9f, 75f), 0f, Range(-220f, 85f));
                bool onDirtRoad = at.x < -6f && Mathf.Abs(at.z - PrologueScript.TurnRadius) < 7f;
                bool inTheBend = at.x < 0f && at.x > -16f && at.z > -6f && at.z < 16f;
                bool nearRest = Vector3.Distance(at, rest) < 16f;
                if (onDirtRoad || inTheBend || nearRest) continue;
                Tree(at, Range(0.8f, 1.35f));
                placed++;
            }
        }

        // The field the car stops beside: a fence, a lone tree, and a few trees behind the car to frame the sky.
        private void BuildField()
        {
            Vector3 rest = RestPosition(out Vector3 forward, out Vector3 right);
            Tree(rest + forward * 9f + right * 10f, 1.5f);
            Tree(rest - forward * 12f - right * 7f, 1.1f);
            Tree(rest + forward * 16f - right * 6f, 1.25f);
            Tree(rest - forward * 4f - right * 9f, 0.95f);

            Vector3 fenceStart = rest + right * 12f - forward * 16f;
            for (int i = 0; i <= 16; i++)
            {
                Vector3 post = fenceStart + forward * (i * 2.5f);
                Part(PrimitiveType.Cube, "Fence Post", Root, post + Vector3.up * 0.55f, new Vector3(0.12f, 1.1f, 0.12f), Timber);
            }
            Quaternion along = Quaternion.LookRotation(forward);
            foreach (float height in new[] { 0.5f, 0.9f })
                Part(PrimitiveType.Cube, "Fence Rail", Root, fenceStart + forward * 20f + Vector3.up * height, new Vector3(0.05f, 0.07f, 40f), Timber, 0f, along);
        }

        private void Tree(Vector3 at, float size)
        {
            float trunk = 1.3f * size;
            Part(PrimitiveType.Cylinder, "Trunk", Root, at + Vector3.up * (trunk * 0.5f), new Vector3(0.2f * size, trunk * 0.5f, 0.2f * size), Bark);
            Color leaves = new Color(Range(0.04f, 0.06f), Range(0.09f, 0.13f), Range(0.06f, 0.085f));
            Part(PrimitiveType.Sphere, "Canopy", Root, at + Vector3.up * (trunk + 1.0f * size), new Vector3(2.3f, 2.5f, 2.3f) * size, leaves);
            Part(PrimitiveType.Sphere, "Canopy", Root, at + new Vector3(0.5f, trunk + 1.7f * size, 0.3f) * 1f, new Vector3(1.5f, 1.6f, 1.5f) * size, leaves);
        }

        // ---- Sky ----

        private void BuildSky()
        {
            Sky = new GameObject("Night Sky").transform;
            Sky.SetParent(Root, false);
            BuildStars();

            Vector3 moonDirection = new Vector3(0.55f, 0.36f, 0.75f).normalized;
            Glow(Sky, "Moon Halo", moonDirection * (SkyRadius - 5f), 70f, new Color(0.6f, 0.68f, 0.95f), 0.22f);
            Glow(Sky, "Moon", moonDirection * (SkyRadius - 6f), 16f, new Color(1f, 0.98f, 0.9f), 1f);
            // A faint glow low in the south, from a town a long way behind.
            foreach ((float angle, float width, Color colour) in new[] { (250f, 360f, new Color(0.42f, 0.28f, 0.45f)), (205f, 240f, new Color(0.3f, 0.22f, 0.4f)) })
            {
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                Transform glow = Glow(Sky, "Horizon Glow", direction * (SkyRadius - 10f) + Vector3.up * 6f, 1f, colour, 0.2f);
                glow.localScale = new Vector3(width, 70f, 1f);
            }

            GameObject moonLight = new GameObject("Moonlight");
            moonLight.transform.SetParent(Root, false);
            moonLight.transform.rotation = Quaternion.LookRotation(-moonDirection);
            Moon = moonLight.AddComponent<Light>();
            Moon.type = LightType.Directional;
            Moon.color = new Color(0.6f, 0.68f, 1f);
            Moon.intensity = 0.55f;
            Moon.shadows = LightShadows.Soft;
            Moon.shadowStrength = 0.65f;
            Moon.cullingMask = 1 << Layer;
        }

        // One mesh of camera-facing quads on a dome: scattered stars, and a denser, fainter band across the sky.
        private void BuildStars()
        {
            const int scattered = 900, band = 1300;
            int count = scattered + band;
            Vector3[] vertices = new Vector3[count * 4];
            Vector2[] uv = new Vector2[count * 4];
            int[] triangles = new int[count * 6];
            starColours = new Color[count * 4];
            starBase = new Color[count];
            starPhase = new float[count];
            starSpeed = new float[count];
            Quaternion bandTilt = Quaternion.Euler(62f, 20f, 0f);
            int s = 0;
            while (s < count)
            {
                bool inBand = s >= scattered;
                Vector3 direction;
                if (inBand)
                {
                    float angle = Range(0f, Mathf.PI * 2f);
                    direction = bandTilt * new Vector3(Mathf.Cos(angle), Gaussian() * 0.08f, Mathf.Sin(angle));
                }
                else direction = new Vector3(Gaussian(), Gaussian(), Gaussian());
                direction.Normalize();
                if (direction.y < 0.02f) continue;

                float brightness = inBand ? Range(0.15f, 0.45f) : Mathf.Pow(Range(0f, 1f), 3f);
                float size = inBand ? Range(0.8f, 1.5f) : Mathf.Lerp(1.1f, 3.6f, brightness);
                float warmth = Range(0f, 1f);
                Color colour = Color.Lerp(new Color(0.78f, 0.86f, 1f), new Color(1f, 0.92f, 0.8f), warmth);
                colour.a = Mathf.Lerp(0.35f, 1f, brightness) * (inBand ? 0.7f : 1f);
                starBase[s] = colour;
                starPhase[s] = Range(0f, Mathf.PI * 2f);
                starSpeed[s] = Range(1.2f, 4.5f);

                Vector3 centre = direction * SkyRadius;
                Vector3 across = Vector3.Cross(direction, Mathf.Abs(direction.y) > 0.95f ? Vector3.right : Vector3.up).normalized * size * 0.5f;
                Vector3 up = Vector3.Cross(across, direction).normalized * size * 0.5f;
                int v = s * 4;
                vertices[v] = centre - across - up;
                vertices[v + 1] = centre + across - up;
                vertices[v + 2] = centre + across + up;
                vertices[v + 3] = centre - across + up;
                uv[v] = new Vector2(0f, 0f);
                uv[v + 1] = new Vector2(1f, 0f);
                uv[v + 2] = new Vector2(1f, 1f);
                uv[v + 3] = new Vector2(0f, 1f);
                for (int c = 0; c < 4; c++) starColours[v + c] = colour;
                int t = s * 6;
                triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
                triangles[t + 3] = v; triangles[t + 4] = v + 3; triangles[t + 5] = v + 2;
                s++;
            }

            starMesh = new Mesh { name = "Prologue Stars" };
            starMesh.vertices = vertices;
            starMesh.uv = uv;
            starMesh.colors = starColours;
            starMesh.triangles = triangles;
            starMesh.bounds = new Bounds(Vector3.zero, Vector3.one * SkyRadius * 2.2f);
            owned.Add(starMesh);

            GameObject stars = new GameObject("Stars");
            stars.transform.SetParent(Sky, false);
            stars.AddComponent<MeshFilter>().sharedMesh = starMesh;
            MeshRenderer renderer = stars.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = CutsceneArt.GlowMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        // ---- The car ----

        // A small, tall-cabined car in the game's chibi proportions, open inside: a floor, two seats, a dashboard and a
        // wheel behind low sides and the driver's door, with pillars up to a roof high enough for the student's head.
        private void BuildCar()
        {
            const float halfWidth = 0.95f, sill = 0.86f, cabinFront = 0.78f, cabinBack = -0.95f;
            float roof = RoofHeight;

            Part(PrimitiveType.Cube, "Floor", Car, new Vector3(0f, 0.29f, 0f), new Vector3(halfWidth * 2f, 0.14f, 3.8f), CarPaint);
            Part(PrimitiveType.Cube, "Bonnet", Car, new Vector3(0f, 0.54f, 1.34f), new Vector3(halfWidth * 2f, 0.64f, 1.12f), CarPaint);
            Part(PrimitiveType.Cube, "Boot", Car, new Vector3(0f, 0.56f, -1.425f), new Vector3(halfWidth * 2f, 0.68f, 0.95f), CarPaint);
            Part(PrimitiveType.Cube, "Front Bumper", Car, new Vector3(0f, 0.33f, 1.95f), new Vector3(halfWidth * 2f + 0.04f, 0.2f, 0.22f), Trim);
            Part(PrimitiveType.Cube, "Rear Bumper", Car, new Vector3(0f, 0.33f, -1.95f), new Vector3(halfWidth * 2f + 0.04f, 0.2f, 0.22f), Trim);
            // The passenger's side is one panel; the driver's has its door and a short panel behind it.
            Part(PrimitiveType.Cube, "Passenger Side", Car, new Vector3(-halfWidth + 0.025f, 0.61f, (cabinFront + cabinBack) * 0.5f),
                new Vector3(0.05f, 0.5f, cabinFront - cabinBack), CarPaint);
            Part(PrimitiveType.Cube, "Driver Side", Car, new Vector3(halfWidth - 0.025f, 0.61f, (cabinBack - 0.42f) * 0.5f),
                new Vector3(0.05f, 0.5f, -0.42f - cabinBack), CarPaint);

            foreach (float side in new[] { -1f, 1f })
            {
                float x = side * SeatHip.x;
                Part(PrimitiveType.Cube, "Seat", Car, new Vector3(x, 0.41f, -0.03f), new Vector3(0.6f, 0.1f, 0.56f), Seat);
                Part(PrimitiveType.Cube, "Seat Back", Car, new Vector3(x, 0.68f, -0.36f), new Vector3(0.6f, 0.44f, 0.1f), Seat, 0f, Quaternion.Euler(-8f, 0f, 0f));
                float edge = side * (halfWidth - 0.05f);
                Strut("A Pillar", new Vector3(edge, 1.04f, cabinFront), new Vector3(edge, roof, 0.38f), 0.08f, CarPaint);
                Strut("B Pillar", new Vector3(edge, sill, -0.47f), new Vector3(edge, roof, -0.47f), 0.08f, Trim);
                Strut("C Pillar", new Vector3(edge, 0.9f, cabinBack), new Vector3(edge, roof, -0.72f), 0.1f, CarPaint);
            }
            Part(PrimitiveType.Cube, "Roof", Car, new Vector3(0f, roof + 0.04f, -0.17f), new Vector3(halfWidth * 2f - 0.06f, 0.08f, 1.18f), CarPaint);
            Part(PrimitiveType.Cube, "Dashboard", Car, new Vector3(0f, 0.95f, 0.68f), new Vector3(halfWidth * 2f - 0.1f, 0.18f, 0.2f), Trim);
            SteeringWheel = new GameObject("Steering Wheel").transform;
            SteeringWheel.SetParent(Car, false);
            SteeringWheel.localPosition = new Vector3(SeatHip.x, 0.92f, 0.44f);
            SteeringWheel.localRotation = Quaternion.Euler(-65f, 0f, 0f);
            Part(PrimitiveType.Cylinder, "Rim", SteeringWheel, Vector3.zero, new Vector3(0.3f, 0.015f, 0.3f), Trim);
            Part(PrimitiveType.Cube, "Spoke", SteeringWheel, new Vector3(0f, 0.018f, 0f), new Vector3(0.26f, 0.01f, 0.035f), new Color(0.2f, 0.2f, 0.22f));
            Glass(new Vector3(0f, 1.04f, cabinFront), new Vector3(0f, roof, 0.38f), halfWidth * 2f - 0.16f, true);
            Glass(new Vector3(0f, 0.9f, cabinBack), new Vector3(0f, roof, -0.72f), halfWidth * 2f - 0.16f, false);

            // The driver sits on the right. Their door hinges at its front edge and swings out.
            DriverDoor = new GameObject("Driver Door").transform;
            DriverDoor.SetParent(Car, false);
            DriverDoor.localPosition = new Vector3(halfWidth, 0.36f, cabinFront);
            Part(PrimitiveType.Cube, "Door Panel", DriverDoor, new Vector3(-0.025f, 0.25f, -0.6f), new Vector3(0.05f, 0.5f, 1.2f), CarPaint);
            Part(PrimitiveType.Cube, "Door Handle", DriverDoor, new Vector3(0.01f, 0.4f, -1.0f), new Vector3(0.02f, 0.03f, 0.14f), Trim);

            foreach (float x in new[] { -0.88f, 0.88f })
                foreach (float z in new[] { -1.25f, 1.25f })
                {
                    Transform wheel = new GameObject("Wheel").transform;
                    wheel.SetParent(Car, false);
                    wheel.localPosition = new Vector3(x, 0.33f, z);
                    Part(PrimitiveType.Cylinder, "Tyre", wheel, Vector3.zero, new Vector3(0.66f, 0.12f, 0.66f), new Color(0.04f, 0.04f, 0.045f), 0f, Quaternion.Euler(0f, 0f, 90f));
                    Part(PrimitiveType.Cylinder, "Hub", wheel, new Vector3(Mathf.Sign(x) * 0.07f, 0f, 0f), new Vector3(0.34f, 0.02f, 0.34f), new Color(0.55f, 0.56f, 0.6f), 0f, Quaternion.Euler(0f, 0f, 90f));
                    Part(PrimitiveType.Cube, "Spoke", wheel, new Vector3(Mathf.Sign(x) * 0.085f, 0f, 0f), new Vector3(0.02f, 0.3f, 0.05f), new Color(0.3f, 0.3f, 0.33f));
                    Wheels.Add(wheel);
                }

            foreach (float x in new[] { -0.58f, 0.58f })
            {
                Part(PrimitiveType.Cube, "Headlight", Car, new Vector3(x, 0.64f, 1.91f), new Vector3(0.34f, 0.13f, 0.04f), HeadlightColour, 3f);
                GameObject beam = new GameObject("Headlight Beam");
                beam.transform.SetParent(Car, false);
                beam.transform.localPosition = new Vector3(x, 0.64f, 2f);
                beam.transform.localRotation = Quaternion.Euler(7f, x * 6f, 0f);
                Light spot = beam.AddComponent<Light>();
                spot.type = LightType.Spot;
                spot.color = HeadlightColour;
                spot.range = 38f;
                spot.spotAngle = 62f;
                spot.innerSpotAngle = 28f;
                spot.intensity = 7f;
                spot.shadows = LightShadows.None;
                spot.cullingMask = 1 << Layer;
                Headlights.Add(spot);
                // A faint shaft of light in the haze, two crossed quads so it reads from the side and from above.
                for (int plane = 0; plane < 2; plane++)
                {
                    Transform shaft = CutsceneArt.Quad(beam.transform, "Beam Shaft", CutsceneArt.ColumnMaterial, out Renderer shaftRenderer);
                    shaft.localPosition = new Vector3(0f, 0f, 5f);
                    shaft.localRotation = Quaternion.Euler(90f, 0f, plane * 90f);
                    shaft.localScale = new Vector3(1.8f, 10f, 1f);
                    CutsceneArt.Tint(shaftRenderer, new MaterialPropertyBlock(), HeadlightColour, 0.07f);
                }
            }

            TaillightMaterial = NewMaterial(new Color(0.85f, 0.08f, 0.06f), 1.2f);
            foreach (float x in new[] { -0.6f, 0.6f })
            {
                GameObject taillight = Part(PrimitiveType.Cube, "Taillight", Car, new Vector3(x, 0.72f, -1.91f), new Vector3(0.3f, 0.12f, 0.04f), Color.black);
                taillight.GetComponent<Renderer>().sharedMaterial = TaillightMaterial;
            }
            TailGlow = PointLight(Car, "Tail Glow", new Vector3(0f, 0.6f, -2.35f), new Color(1f, 0.15f, 0.1f), 4f, 0.5f);
            CabinLight = PointLight(Car, "Cabin Light", new Vector3(0f, roof - 0.12f, -0.1f), new Color(1f, 0.84f, 0.6f), 2.6f, 0f);

            // The phone in its dash mount, screen to the driver.
            PhoneMount = new GameObject("Phone Mount").transform;
            PhoneMount.SetParent(Car, false);
            PhoneMount.localPosition = new Vector3(0.12f, 1.13f, 0.64f);
            PhoneMount.localRotation = Quaternion.Euler(-12f, 200f, 0f);
            Phone = new GameObject("Phone").transform;
            Phone.SetParent(PhoneMount, false);
            Part(PrimitiveType.Cube, "Phone Body", Phone, Vector3.zero, new Vector3(0.075f, 0.15f, 0.012f), new Color(0.05f, 0.05f, 0.06f));
            PhoneScreen = NewMaterial(PhoneGlow, 1.2f);
            GameObject screen = Part(PrimitiveType.Cube, "Phone Screen", Phone, new Vector3(0f, 0f, 0.007f), new Vector3(0.066f, 0.138f, 0.002f), Color.black);
            screen.GetComponent<Renderer>().sharedMaterial = PhoneScreen;
            PhoneLight = PointLight(Phone, "Phone Glow", new Vector3(0f, 0f, 0.12f), PhoneGlow, 1.5f, 0.9f);
        }

        // A bar of the car's frame from one point to another, in the car's space.
        private void Strut(string name, Vector3 from, Vector3 to, float thickness, Color colour)
        {
            Vector3 along = to - from;
            Part(PrimitiveType.Cube, name, Car, (from + to) * 0.5f, new Vector3(thickness, along.magnitude, thickness), colour, 0f,
                Quaternion.FromToRotation(Vector3.up, along));
        }

        // A pane of glass from its bottom edge to its top edge, facing out of the front or the back of the car.
        private void Glass(Vector3 bottom, Vector3 top, float width, bool front)
        {
            Vector3 up = (top - bottom).normalized;
            Vector3 outward = Vector3.Cross(Vector3.right, up) * (front ? 1f : -1f);
            Transform pane = CutsceneArt.Quad(Car, "Glass", CutsceneArt.PlainMaterial, out Renderer paneRenderer);
            pane.localPosition = (bottom + top) * 0.5f;
            pane.localRotation = Quaternion.LookRotation(outward, up);
            pane.localScale = new Vector3(width, Vector3.Distance(bottom, top), 1f);
            CutsceneArt.Tint(paneRenderer, new MaterialPropertyBlock(), new Color(0.45f, 0.55f, 0.75f), 0.14f);
        }

        // ---- The student ----

        // The player's own clothes, before the academy: a grey-blue hoodie, dark jeans and off-white trim.
        internal static readonly Color EverydayHoodie = new Color(0.35f, 0.41f, 0.48f);
        internal static readonly Color EverydayJeans = new Color(0.17f, 0.2f, 0.28f);
        internal static readonly Color EverydayTrim = new Color(0.86f, 0.84f, 0.8f);
        /// <summary>
        /// The player's own blonde, as their default texture has it. The customisable material's own default hair is pink,
        /// so without this the student would not look like the player.
        /// </summary>
        internal static readonly Color PlayerHair = new Color(0.93f, 0.81f, 0.57f);

        private void BuildStudent()
        {
            // Dark messy hair, a grey-blue hoodie, white sneakers, and the satchel kept as a uni backpack.
            DevChibis.Look look = new DevChibis.Look
            {
                Id = "prologue_student", Name = "Student",
                Skin = new Color(0.92f, 0.77f, 0.65f), Hair = new Color(0.18f, 0.13f, 0.11f), Outfit = EverydayHoodie,
                Shoes = new Color(0.9f, 0.88f, 0.85f)
            };
            GameObject model = PlayerInEverydayClothes(Car);
            if (model == null)
            {
                SecretRoomArt art = SecretRoomArt.Load();
                model = DevChibis.Create(art != null ? art.devNick : null, look, Car);
            }
            if (model == null)
            {
                model = Part(PrimitiveType.Capsule, "Student", Car, Vector3.zero, new Vector3(0.5f, StudentHeight * 0.5f, 0.5f), look.Outfit);
                GameObject body = model;
                model = new GameObject("Student");
                model.transform.SetParent(Car, false);
                body.transform.SetParent(model.transform, false);
                body.transform.localPosition = Vector3.up * StudentHeight * 0.5f;
            }
            foreach (Collider collider in model.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.Destroy(collider);
            Student = model.transform;
            Student.localPosition = Vector3.zero;
            Student.localRotation = Quaternion.identity;
            Student.localScale = Vector3.one;
            if (BoundsOf(model, out Bounds unscaled) && unscaled.size.y > 0.05f) Student.localScale = Vector3.one * (StudentHeight / unscaled.size.y);

            Transform hips = null;
            StudentAnimator = model.GetComponentInChildren<Animator>();
            if (StudentAnimator != null)
            {
                StudentAnimator.applyRootMotion = false;
                // The title menu pauses the game, and the preview plays over it.
                StudentAnimator.updateMode = AnimatorUpdateMode.UnscaledTime;
                StudentAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                if (StudentAnimator.isHuman)
                {
                    Head = StudentAnimator.GetBoneTransform(HumanBodyBones.Head);
                    RightHand = StudentAnimator.GetBoneTransform(HumanBodyBones.RightHand);
                    hips = StudentAnimator.GetBoneTransform(HumanBodyBones.Hips);
                    Bones(Thighs, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg);
                    Bones(Shins, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg);
                    Bones(UpperArms, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm);
                    Bones(Forearms, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm);
                }
            }

            // Measured standing at the car's origin, before the student is seated. The model's legs are short and its
            // head is most of its height, so sitting barely lowers it: the roof is raised to suit.
            float feet = Student.position.y;
            float top = BoundsOf(model, out Bounds scaled) ? scaled.max.y - feet : StudentHeight;
            HipsHeight = hips != null ? Mathf.Clamp(hips.position.y - feet, 0.05f, StudentHeight * 0.6f) : StudentHeight * 0.45f;
            SittingHeight = Mathf.Clamp(top - HipsHeight, 0.5f, 1.6f);
            RoofHeight = Mathf.Clamp(SeatHip.y + SittingHeight + 0.07f, 1.35f, 2.15f);
            Student.localPosition = SeatPosition;
        }

        /// <summary>
        /// The player's witch model with its hat off and its robe recoloured into everyday clothes, driven by the NPC
        /// animator; null if the art is missing.
        /// </summary>
        private GameObject PlayerInEverydayClothes(Transform parent)
        {
            PrologueArt art = PrologueArt.Load();
            if (art == null || art.student == null) return null;
            GameObject model = UnityEngine.Object.Instantiate(art.student, parent);
            model.name = "Student";
            foreach (Transform part in model.GetComponentsInChildren<Transform>(true))
                if (part.name.StartsWith("hat", StringComparison.OrdinalIgnoreCase)) part.gameObject.SetActive(false);
            if (art.everydayClothes != null)
                foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] materials = renderer.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        // The body, hair and head share the main material; the eyes and mouth keep their own.
                        if (materials[i] == null || !materials[i].name.StartsWith("Mat_Main", StringComparison.Ordinal)) continue;
                        Material clothes = new Material(art.everydayClothes) { name = "Prologue Everyday Clothes" };
                        clothes.SetColor("_Hair_Color", PlayerHair);
                        clothes.SetColor("_MainCloth_Color", EverydayHoodie);
                        clothes.SetColor("_AdditionalCloth_Color", EverydayJeans);
                        clothes.SetColor("_ClothDetails_Color", EverydayTrim);
                        materials[i] = clothes;
                        owned.Add(clothes);
                        changed = true;
                    }
                    if (changed) renderer.sharedMaterials = materials;
                }
            Animator animator = model.GetComponentInChildren<Animator>();
            if (animator != null && art.animator != null) animator.runtimeAnimatorController = art.animator;
            return model;
        }

        private void Bones(List<Transform> into, params HumanBodyBones[] bones)
        {
            foreach (HumanBodyBones bone in bones)
            {
                Transform found = StudentAnimator.GetBoneTransform(bone);
                if (found != null) into.Add(found);
            }
        }

        private static bool BoundsOf(GameObject model, out Bounds bounds)
        {
            bool any = false;
            bounds = default;
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled || renderer is ParticleSystemRenderer) continue;
                if (!any) { bounds = renderer.bounds; any = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return any;
        }

        // ---- The star ----

        private void BuildStar()
        {
            GameObject star = new GameObject("Falling Star");
            star.transform.SetParent(Root, false);
            StarHalo = CutsceneArt.Quad(star.transform, "Star Halo", CutsceneArt.GlowMaterial, out Renderer halo);
            StarCore = CutsceneArt.Quad(star.transform, "Star Core", CutsceneArt.SparkleMaterial, out Renderer core);
            StarHaloRenderer = halo;
            StarCoreRenderer = core;
            StarLight = PointLight(star.transform, "Star Light", Vector3.zero, new Color(1f, 0.84f, 0.48f), 8f, 0f);

            GameObject trail = new GameObject("Star Trail");
            trail.transform.SetParent(star.transform, false);
            StarTrail = trail.AddComponent<LineRenderer>();
            StarTrail.sharedMaterial = CutsceneArt.PlainMaterial;
            StarTrail.useWorldSpace = true;
            StarTrail.positionCount = 0;
            StarTrail.numCapVertices = 4;
            StarTrail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            StarTrail.receiveShadows = false;
            StarTrail.widthCurve = new AnimationCurve(new Keyframe(0f, 0.05f), new Keyframe(1f, 0.6f));
            StarTrail.colorGradient = new Gradient
            {
                colorKeys = new[] { new GradientColorKey(new Color(0.75f, 0.55f, 1f), 0f), new GradientColorKey(new Color(1f, 0.84f, 0.48f), 0.6f), new GradientColorKey(new Color(1f, 0.97f, 0.88f), 1f) },
                alphaKeys = new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.6f, 0.6f), new GradientAlphaKey(1f, 1f) }
            };
            star.SetActive(false);
        }

        // ---- Parts ----

        private GameObject Part(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 localScale, Color colour,
            float emission = 0f, Quaternion? rotation = null)
        {
            GameObject part = RuntimePrimitive.Create(type);
            part.name = name;
            UnityEngine.Object.Destroy(part.GetComponent<Collider>());
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            if (rotation.HasValue) part.transform.localRotation = rotation.Value;
            part.GetComponent<Renderer>().sharedMaterial = SharedMaterial(colour, emission);
            return part;
        }

        // Parts of one colour share a material.
        private Material SharedMaterial(Color colour, float emission)
        {
            if (materials.TryGetValue((colour, emission), out Material material) && material != null) return material;
            material = NewMaterial(colour, emission, track: false);
            materials[(colour, emission)] = material;
            return material;
        }

        private Material NewMaterial(Color colour, float emission, bool track = true)
        {
            Material source = RuntimePrimitive.LitMaterial;
            Material material = source != null ? new Material(source) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.name = "Prologue " + ColorUtility.ToHtmlStringRGB(colour);
            material.SetColor("_BaseColor", colour);
            material.SetColor("_Color", colour);
            material.SetColor("_EmissionColor", colour * emission);
            material.EnableKeyword("_EMISSION");
            material.SetFloat("_Smoothness", 0.15f);
            if (track) owned.Add(material);
            return material;
        }

        private static Light PointLight(Transform parent, string name, Vector3 localPosition, Color colour, float range, float intensity)
        {
            GameObject host = new GameObject(name);
            host.transform.SetParent(parent, false);
            host.transform.localPosition = localPosition;
            Light light = host.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = colour;
            light.range = range;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
            light.cullingMask = 1 << Layer;
            return light;
        }

        private static Transform Glow(Transform parent, string name, Vector3 localPosition, float size, Color colour, float alpha)
        {
            Transform glow = CutsceneArt.Quad(parent, name, CutsceneArt.GlowMaterial, out Renderer glowRenderer);
            glow.localPosition = localPosition;
            glow.localRotation = Quaternion.LookRotation(localPosition.normalized);
            glow.localScale = Vector3.one * size;
            CutsceneArt.Tint(glowRenderer, new MaterialPropertyBlock(), colour, alpha);
            return glow;
        }

        private float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);

        // A normally distributed number (Box-Muller), for directions spread evenly over a sphere.
        private float Gaussian()
        {
            double u = 1.0 - random.NextDouble(), v = random.NextDouble();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u)) * Math.Cos(2.0 * Math.PI * v));
        }
    }
}
