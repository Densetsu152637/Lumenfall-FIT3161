using System;
using System.Collections.Generic;
using Lumenfall.Controls;
using Lumenfall.Rendering;
using UnityEngine;

namespace Lumenfall.Story
{
    /// <summary>One of the sequence break's closing effects. The game closes when it has run for <see cref="Duration"/>.</summary>
    internal interface IEndingEffect : IDisposable
    {
        /// <summary>Seconds from the still coming back to the game closing.</summary>
        float Duration { get; }

        /// <summary>Moves the effect to <paramref name="t"/> seconds in. Called once per frame.</summary>
        void Tick(float t);

        /// <summary>Draws the moment <paramref name="t"/> seconds in, from <c>OnGUI</c> on Repaint.</summary>
        void Draw(float t);
    }

    /// <summary>
    /// The end of the sequence break (<see cref="SequenceBreakEnding"/>): the still of the game comes back, and a pale hand
    /// reaches in from each side and presses against its edges. Three hard shoves squeeze it narrower, bowing it toward the
    /// player, and cracks spread from both palms with a snap each time. Then the hands rush together, and the moment they
    /// meet in the middle the screen breaks between them in a white flash: its glass bursts out from between the palms in
    /// pieces cut like the old shatter's (<see cref="ScreenShatter"/>), each with its bright edge, and they tumble down
    /// into the dark while the hands draw apart and away. The game closes once the last piece has fallen. The hands are PolyOne Studio's rigged pair (<see cref="SecretRoomArt.crushingHand"/>), each its own
    /// copy of the model with the other hand collapsed, drawn in a flat pale glow and posed from their own bones (so the
    /// model's authored orientation does not matter). They and the screen stand on their own stage far below the world,
    /// seen only by their own camera, like Miki's in <see cref="SequenceBreakLockout"/>.
    /// </summary>
    internal sealed class ScreenCrush : IEndingEffect
    {
        private const int StageLayer = 31;
        private static readonly Vector3 StagePosition = new Vector3(0f, -3000f, 0f);
        private const string RightHand = "J_Right_Hand", LeftHand = "J_Left_Hand";
        private static readonly string[] Fingers = { "Index", "Middle", "Ring", "Pinky" };

        /// <summary>Every bone the effect poses, for both hands; the hands are not used unless the model has them all.</summary>
        internal static readonly string[] RequiredBones = BuildRequiredBones();

        // The timeline, in seconds from the still coming back.
        private const float ArriveFrom = 0.3f, ArriveTo = 1.4f;
        private static readonly float[] Shoves = { 1.7f, 2.4f, 3.1f };
        private static readonly float[] ShoveSqueeze = { 0.22f, 0.42f, 0.6f };
        private const float ShoveSeconds = 0.3f;
        private const float RushFrom = 3.6f;
        /// <summary>When the hands meet and the game breaks between them.</summary>
        internal const float BreakAt = 4.2f;
        // After the break: the squeezed pieces spring back to shape as they burst out, fall for this long (the game then
        // closes), and the hands, after a moment pressed together, draw apart and out of view.
        private const float BurstSeconds = 0.2f;
        private const float FallSeconds = 2f;
        private const float HandsPartFrom = 0.45f, HandsPartTo = 1.9f;
        private const float ShardGravity = 7f;

        // How far the cracks have spread from each palm (as a share of the screen's width) after contact and each shove.
        private static readonly float[] CrackReach = { 0.06f, 0.17f, 0.3f, 0.45f };
        // The fingers bend a little as they press, and clench as the hands meet.
        private static readonly float[] FingerBend = { 80f, 95f, 70f };
        private static readonly float[] ThumbBend = { 10f, 35f, 45f };
        // Palms face each other with the thumbs toward the camera, turned this much further toward it.
        private const float PalmTurn = 10f;
        private const float HandScale = 0.8f;
        // As the hands arrive the screen draws back to this size, so they press on its edges in full view.
        private const float ScreenSettle = 0.78f;
        private const float CameraDistance = 4.3f;

        private static readonly Color Skin = new Color(0.93f, 0.91f, 0.96f);
        private static readonly Color Glow = new Color(0.32f, 0.32f, 0.38f);

        /// <summary>One hand: its own copy of the model, posed from its bones.</summary>
        private sealed class Rig
        {
            public Transform Pivot, Model, Wrist, Middle;
            public readonly Transform[][] Fingers = new Transform[4][];
            public readonly Quaternion[][] FingerRest = new Quaternion[4][];
            public readonly Vector3[] FingerAxes = new Vector3[4];
            public readonly Transform[] Thumb = new Transform[3];
            public readonly Quaternion[] ThumbRest = new Quaternion[3];
            public Vector3 ThumbAxis, ThumbSweep;
            public float Side;

            public void Curl(float curl)
            {
                for (int f = 0; f < 4; f++)
                {
                    float bend = 0f;
                    for (int j = 0; j < 3; j++)
                    {
                        bend += FingerBend[j] * curl;
                        Fingers[f][j].rotation = Model.rotation * (Quaternion.AngleAxis(bend, FingerAxes[f]) * FingerRest[f][j]);
                    }
                }
                Quaternion sweep = Quaternion.AngleAxis(40f * curl, ThumbSweep);
                float thumbBend = 0f;
                for (int j = 0; j < 3; j++)
                {
                    thumbBend += ThumbBend[j] * curl;
                    Thumb[j].rotation = Model.rotation * (sweep * Quaternion.AngleAxis(thumbBend, ThumbAxis) * ThumbRest[j]);
                }
            }
        }

        private struct Crack
        {
            public Vector2 From, To;
            public float Near, Far;
            public int Side;
        }

        /// <summary>A piece of the screen's glass, cut from a web around where the hands meet, as the old shatter's are.</summary>
        private struct Shard
        {
            // Its corners about its centre: squeezed as it was when the hands met, and sprung back to shape.
            public Vector3[] Squeezed, Sprung;
            public Vector3 Start, Velocity, Axis;
            public float Spin, Delay;
            public int FirstVertex, FirstEdge;
        }

        private readonly GameObject stage;
        private readonly Rig left, right;
        private readonly Camera camera;
        private readonly Vector3 cameraRest;
        private readonly RenderTexture view;
        private readonly float width, height;
        private readonly Mesh sheetMesh, crackMesh;
        private readonly Vector2[] grid;
        private readonly Vector3[] creases, sheetVertices;
        private readonly Crack[] cracks;
        private readonly Vector3[] crackVertices;
        private readonly Material handMaterial, sheetMaterial, crackDark, crackLight, viewMaterial, plainMaterial;
        private readonly GameObject soundHost;
        private readonly AudioSource sound;
        private readonly AudioClip snap;
        private readonly bool calm;
        private readonly System.Random random = new System.Random(3162);
        private int snapsPlayed;
        private readonly GameObject sheetObject, crackObject, shardObject;
        private readonly Shard[] shards;
        private readonly Mesh shardMesh, edgeMesh;
        private readonly Vector3[] shardVertices, edgeVertices;

        /// <summary>The game closes once the last piece of the screen has fallen out of view.</summary>
        public float Duration => BreakAt + FallSeconds;

        /// <summary>The effect for <paramref name="still"/>, or null when the hand model is missing or incomplete.</summary>
        public static ScreenCrush TryCreate(Texture2D still, int pixelWidth = 0, int pixelHeight = 0)
        {
            SecretRoomArt art = SecretRoomArt.Load();
            if (still == null || art == null || art.crushingHand == null) return null;
            try { return new ScreenCrush(still, art.crushingHand, pixelWidth > 0 ? pixelWidth : Screen.width, pixelHeight > 0 ? pixelHeight : Screen.height); }
            catch (Exception exception)
            {
                Debug.LogWarning("The crushing hands could not be set up, so the screen shatters instead: " + exception.Message);
                return null;
            }
        }

        private ScreenCrush(Texture2D still, GameObject prefab, int pixelWidth, int pixelHeight)
        {
            calm = GameSettingsPanel.ReduceMotion;
            stage = new GameObject("[Crush Stage]");
            stage.transform.position = StagePosition;
            try
            {
                handMaterial = Tinted("Crushing Hand", Skin, Glow, 0.2f);
                right = BuildRig(prefab, true);
                left = BuildRig(prefab, false);

                camera = new GameObject("Crush Camera").AddComponent<Camera>();
                camera.transform.SetParent(stage.transform, false);
                cameraRest = new Vector3(0f, 0f, -CameraDistance);
                camera.transform.localPosition = cameraRest;
                camera.transform.localRotation = Quaternion.identity;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.cullingMask = 1 << StageLayer;
                camera.fieldOfView = 30f;
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 30f;
                camera.allowHDR = false;
                view = new RenderTexture(Mathf.Max(1, pixelWidth), Mathf.Max(1, pixelHeight), 24) { name = "Crush Stage", antiAliasing = 4 };
                camera.targetTexture = view;
                camera.aspect = view.width / (float)view.height;

                // The screen exactly fills the view at the stage's centre.
                height = 2f * CameraDistance * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                width = height * camera.aspect;

                const int columns = 24, rows = 12;
                grid = new Vector2[(columns + 1) * (rows + 1)];
                creases = new Vector3[grid.Length];
                sheetVertices = new Vector3[grid.Length];
                Vector2[] uv = new Vector2[grid.Length];
                for (int r = 0; r <= rows; r++)
                    for (int c = 0; c <= columns; c++)
                    {
                        int i = r * (columns + 1) + c;
                        grid[i] = new Vector2(c / (float)columns - 0.5f, r / (float)rows - 0.5f);
                        uv[i] = new Vector2(c / (float)columns, r / (float)rows);
                        creases[i] = new Vector3(Noise(), Noise(), Noise());
                    }
                int[] triangles = new int[columns * rows * 6];
                for (int r = 0, k = 0; r < rows; r++)
                    for (int c = 0; c < columns; c++)
                    {
                        int a = r * (columns + 1) + c, b = a + columns + 1;
                        triangles[k++] = a; triangles[k++] = b; triangles[k++] = b + 1;
                        triangles[k++] = a; triangles[k++] = b + 1; triangles[k++] = a + 1;
                    }
                sheetMesh = new Mesh { name = "Squeezed Still" };
                sheetMesh.MarkDynamic();
                sheetMesh.vertices = sheetVertices;
                sheetMesh.uv = uv;
                sheetMesh.triangles = triangles;
                sheetMaterial = Tinted("Squeezed Still", Color.black, Color.white, 0f);
                sheetMaterial.SetTexture("_BaseMap", still);
                sheetMaterial.SetTexture("_EmissionMap", still);
                sheetMaterial.mainTexture = still;
                sheetMaterial.SetFloat("_Cull", 0f);
                sheetObject = AddRenderer("Still", sheetMesh, sheetMaterial).gameObject;

                cracks = BuildCracks();
                crackVertices = new Vector3[cracks.Length * 8];
                int[] dark = new int[cracks.Length * 6], light = new int[cracks.Length * 6];
                for (int i = 0; i < cracks.Length; i++)
                {
                    int d = i * 8, l = d + 4;
                    int[] quad = { 0, 1, 2, 0, 2, 3 };
                    for (int q = 0; q < 6; q++) { dark[i * 6 + q] = d + quad[q]; light[i * 6 + q] = l + quad[q]; }
                }
                crackMesh = new Mesh { name = "Cracks", subMeshCount = 2 };
                crackMesh.MarkDynamic();
                crackMesh.vertices = crackVertices;
                crackMesh.SetTriangles(dark, 0);
                crackMesh.SetTriangles(light, 1);
                crackDark = Tinted("Crack Shadow", Color.black, new Color(0.02f, 0.02f, 0.03f), 0f);
                crackLight = Tinted("Crack", Color.black, new Color(0.82f, 0.85f, 0.92f), 0f);
                crackDark.SetFloat("_Cull", 0f);
                crackLight.SetFloat("_Cull", 0f);
                MeshRenderer crackRenderer = AddRenderer("Cracks", crackMesh, crackDark);
                crackRenderer.sharedMaterials = new[] { crackDark, crackLight };
                crackObject = crackRenderer.gameObject;

                // The glass the screen breaks into: textured pieces sharing the screen's material, and their bright edges.
                shards = BuildShards(out shardMesh, out edgeMesh);
                shardVertices = shardMesh.vertices;
                edgeVertices = edgeMesh.vertices;
                shardObject = new GameObject("Glass");
                shardObject.transform.SetParent(stage.transform, false);
                AddRenderer("Pieces", shardMesh, sheetMaterial).transform.SetParent(shardObject.transform, false);
                AddRenderer("Edges", edgeMesh, crackLight).transform.SetParent(shardObject.transform, false);
                shardObject.SetActive(false);

                Light spot = new GameObject("Crush Light").AddComponent<Light>();
                spot.transform.SetParent(stage.transform, false);
                spot.transform.localPosition = new Vector3(0.8f, 3.2f, -3.2f);
                spot.transform.LookAt(stage.transform.position);
                spot.type = LightType.Spot;
                spot.spotAngle = 70f;
                spot.innerSpotAngle = 40f;
                spot.range = 16f;
                spot.intensity = 8f;
                spot.color = new Color(1f, 0.96f, 0.9f);
                spot.shadows = LightShadows.Soft;
                spot.cullingMask = 1 << StageLayer;

                foreach (Transform child in stage.GetComponentsInChildren<Transform>(true))
                {
                    child.gameObject.layer = StageLayer;
                    if (!Application.isPlaying) child.gameObject.hideFlags = HideFlags.HideAndDontSave;
                }

                Shader ui = Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Hidden/Internal-Colored");
                viewMaterial = new Material(ui) { hideFlags = HideFlags.HideAndDontSave, mainTexture = view };
                plainMaterial = new Material(ui) { hideFlags = HideFlags.HideAndDontSave, mainTexture = Texture2D.whiteTexture };

                soundHost = new GameObject("[Screen Crush Sound]") { hideFlags = HideFlags.HideAndDontSave };
                sound = soundHost.AddComponent<AudioSource>();
                sound.ignoreListenerPause = true;
                sound.spatialBlend = 0f;
                sound.playOnAwake = false;
                snap = ScreenShatter.CreateSnap();
                Pose(0f);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private static string[] BuildRequiredBones()
        {
            List<string> names = new List<string>();
            foreach (string hand in new[] { RightHand, LeftHand })
            {
                names.Add(hand);
                foreach (string finger in new[] { "Thumb", "Index", "Middle", "Ring", "Pinky" })
                    for (int j = 1; j <= 3; j++) names.Add(hand + finger + j);
            }
            return names.ToArray();
        }

        // One hand from its own copy of the model, the other hand collapsed to nothing. Its palm faces the middle of the
        // screen, turned toward the camera, fingers up; a palm one unit long, centred on its pivot.
        private Rig BuildRig(GameObject prefab, bool isRight)
        {
            Rig rig = new Rig { Side = isRight ? 1f : -1f };
            rig.Pivot = new GameObject(isRight ? "Right Hand" : "Left Hand").transform;
            rig.Pivot.SetParent(stage.transform, false);
            rig.Model = UnityEngine.Object.Instantiate(prefab).transform;
            foreach (Animator animator in rig.Model.GetComponentsInChildren<Animator>(true)) animator.enabled = false;

            Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
            foreach (Transform child in rig.Model.GetComponentsInChildren<Transform>(true)) bones[child.name] = child;
            foreach (string name in RequiredBones)
                if (!bones.ContainsKey(name)) throw new InvalidOperationException("the hand model has no bone " + name);
            string hand = isRight ? RightHand : LeftHand, other = isRight ? LeftHand : RightHand;
            rig.Wrist = bones[hand];
            rig.Middle = bones[hand + "Middle1"];
            if (!rig.Wrist.IsChildOf(bones[other])) bones[other].localScale = Vector3.zero;
            string otherSide = isRight ? "Left" : "Right";
            foreach (Renderer renderer in rig.Model.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.name.IndexOf(otherSide, StringComparison.OrdinalIgnoreCase) >= 0) { renderer.enabled = false; continue; }
                Material[] materials = new Material[Mathf.Max(1, renderer.sharedMaterials.Length)];
                for (int i = 0; i < materials.Length; i++) materials[i] = handMaterial;
                renderer.sharedMaterials = materials;
                if (renderer is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;
            }

            // Looking at your own right palm with the fingers up, the thumb is on the right; for a left palm it is on the
            // left. So the palm's normal comes from the bones with the handedness flipped for the left hand.
            Vector3 up = (rig.Middle.position - rig.Wrist.position).normalized;
            Vector3 across = (bones[hand + "Index1"].position - bones[hand + "Pinky1"].position).normalized;
            Vector3 normal = (isRight ? Vector3.Cross(up, across) : Vector3.Cross(across, up)).normalized;
            up = (isRight ? Vector3.Cross(across, normal) : Vector3.Cross(normal, across)).normalized;
            // Palms face each other, so the thumbs point at the camera (which looks along +z); each is turned a little
            // further toward it so a sliver of palm shows.
            Vector3 wantNormal = Quaternion.Euler(0f, isRight ? -PalmTurn : PalmTurn, 0f) * (isRight ? Vector3.left : Vector3.right);
            Vector3 wantUp = Vector3.up;
            rig.Model.rotation = Quaternion.LookRotation(wantNormal, wantUp) * Quaternion.Inverse(Quaternion.LookRotation(normal, up)) * rig.Model.rotation;

            float palm = Vector3.Distance(rig.Middle.position, rig.Wrist.position);
            if (palm < 1e-5f) throw new InvalidOperationException("the hand's palm has no length");
            rig.Model.localScale /= palm;
            rig.Model.position += rig.Pivot.position - Vector3.Lerp(rig.Wrist.position, rig.Middle.position, 0.5f);
            rig.Model.SetParent(rig.Pivot, true);

            Quaternion toModel = Quaternion.Inverse(rig.Model.rotation);
            for (int f = 0; f < Fingers.Length; f++)
            {
                rig.Fingers[f] = new Transform[3];
                rig.FingerRest[f] = new Quaternion[3];
                for (int j = 0; j < 3; j++)
                {
                    rig.Fingers[f][j] = bones[hand + Fingers[f] + (j + 1)];
                    rig.FingerRest[f][j] = toModel * rig.Fingers[f][j].rotation;
                }
                // Turning about this axis carries the finger toward the palm's side.
                Vector3 along = rig.Fingers[f][1].position - rig.Fingers[f][0].position;
                rig.FingerAxes[f] = toModel * Vector3.Cross(along, wantNormal).normalized;
            }
            for (int j = 0; j < 3; j++)
            {
                rig.Thumb[j] = bones[hand + "Thumb" + (j + 1)];
                rig.ThumbRest[j] = toModel * rig.Thumb[j].rotation;
            }
            Vector3 thumbAlong = rig.Thumb[1].position - rig.Thumb[0].position;
            rig.ThumbAxis = toModel * Vector3.Cross(thumbAlong, wantNormal).normalized;
            // Turning about this axis carries the thumb across toward the palm's side.
            rig.ThumbSweep = toModel * wantUp * rig.Side;
            return rig;
        }

        private MeshRenderer AddRenderer(string name, Mesh mesh, Material material)
        {
            GameObject part = new GameObject(name);
            part.transform.SetParent(stage.transform, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }

        // Cracks run in from the middle of each side edge, where the palms press: forking rays across the screen, linked
        // by short cross-cracks into a web. Points are in screen units, -0.5 to 0.5 each way.
        private Crack[] BuildCracks()
        {
            List<Crack> list = new List<Crack>();
            const int rays = 9, steps = 6;
            float aspect = height / width;
            for (int side = -1; side <= 1; side += 2)
            {
                Vector2 origin = new Vector2(side * 0.5f, Noise() * 0.06f);
                Vector2[,] points = new Vector2[rays, steps + 1];
                for (int r = 0; r < rays; r++)
                {
                    float angle = Mathf.Lerp(-75f, 75f, r / (float)(rays - 1)) + Noise() * 8f;
                    Vector2 at = origin;
                    points[r, 0] = at;
                    for (int s = 1; s <= steps; s++)
                    {
                        angle += Noise() * 16f;
                        float rad = angle * Mathf.Deg2Rad;
                        // Inward from this side; distances are measured in widths, so the vertical step is scaled up.
                        Vector2 step = new Vector2(-side * Mathf.Cos(rad), Mathf.Sin(rad) / aspect) * (0.045f + 0.03f * s) * (0.8f + 0.4f * (float)random.NextDouble());
                        at += step;
                        at.y = Mathf.Clamp(at.y, -0.5f, 0.5f);
                        points[r, s] = at;
                        list.Add(Make(points[r, s - 1], at, origin, side));
                    }
                }
                for (int s = 1; s < steps - 1; s++)
                    for (int r = 0; r + 1 < rays; r++)
                        if (random.NextDouble() < 0.55) list.Add(Make(points[r, s], points[r + 1, s], origin, side));
            }
            return list.ToArray();
        }

        private Crack Make(Vector2 from, Vector2 to, Vector2 origin, int side)
        {
            float near = Distance(from, origin), far = Distance(to, origin);
            return near <= far ? new Crack { From = from, To = to, Near = near, Far = far, Side = side }
                : new Crack { From = to, To = from, Near = far, Far = near, Side = side };
        }

        // Distance across the screen in widths.
        private float Distance(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, (a.y - b.y) * height / width).magnitude;

        private float Noise() => (float)random.NextDouble() * 2f - 1f;

        private static Material Tinted(string name, Color colour, Color emission, float smoothness)
        {
            Material template = RuntimePrimitive.LitMaterial;
            if (template == null) throw new InvalidOperationException("Resources/" + RuntimePrimitive.MaterialPath + " is missing");
            Material material = new Material(template) { name = name, hideFlags = HideFlags.HideAndDontSave };
            material.SetColor("_BaseColor", colour);
            material.color = colour;
            material.SetColor("_EmissionColor", emission);
            material.EnableKeyword("_EMISSION");
            material.SetFloat("_Smoothness", smoothness);
            return material;
        }

        public void Tick(float t)
        {
            Pose(t);
            // A snap at contact, with each shove, and as the game breaks; then fainter ones as the glass falls.
            float[] at = { ArriveTo, Shoves[0], Shoves[1], Shoves[2], BreakAt, BreakAt + 0.25f, BreakAt + 0.6f };
            float[] loud = { 0.5f, 0.7f, 0.8f, 0.9f, 1f, 0.35f, 0.22f };
            while (snapsPlayed < at.Length && t >= at[snapsPlayed]) Audio.AudioPlayback.PlayOneShot(sound, snap, loud[snapsPlayed++]);
        }

        /// <summary>How far the screen has been squeezed at <paramref name="t"/>: 0 untouched, 1 when the hands meet.</summary>
        internal static float Squeeze(float t)
        {
            float squeeze = 0f;
            for (int i = 0; i < Shoves.Length; i++)
                squeeze = Mathf.Lerp(squeeze, ShoveSqueeze[i], EaseOut(Span(t, Shoves[i], Shoves[i] + ShoveSeconds)));
            // The last rush speeds up as it goes, so the meeting lands hard.
            float rush = Span(t, RushFrom, BreakAt);
            return Mathf.Lerp(squeeze, 1f, rush * rush * rush);
        }

        /// <summary>Places the hands, the screen and its cracks as they are <paramref name="t"/> seconds in.</summary>
        internal void Pose(float t)
        {
            bool broken = t >= BreakAt;
            sheetObject.SetActive(!broken);
            crackObject.SetActive(!broken);
            shardObject.SetActive(broken);
            if (broken)
            {
                PoseBroken(t - BreakAt);
                return;
            }
            float squeeze = Squeeze(t);
            float arrive = EaseOut(Span(t, ArriveFrom, ArriveTo));
            float settle = Mathf.Lerp(1f, ScreenSettle, arrive);
            // Never quite zero wide: the palms meet across what is left of it.
            float half = width * 0.5f * settle * (1f - 0.95f * squeeze);
            // Each shove starts with a little draw back.
            float windUp = 0f;
            foreach (float shove in Shoves) windUp += Mathf.Exp(-Mathf.Pow((t - (shove - 0.08f)) / 0.07f, 2f));
            float jolt = Jolt(t);
            // Pressing rather than clenching at the end, so the meeting fingers do not lace through each other.
            float curl = Mathf.Lerp(0.15f, 0.3f, Span(t, ArriveTo, Shoves[0])) + 0.12f * Span(t, RushFrom, BreakAt);

            // The palm's centre sits about half the hand's thickness out from the edge it presses.
            float contact = half + (0.2f + windUp * 0.06f) * HandScale;
            PlaceHands(Mathf.Lerp(width * 0.5f + 2.2f, contact, arrive), curl);
            camera.transform.localPosition = cameraRest + (calm ? Vector3.zero : new Vector3(Noise(), Noise(), 0f) * jolt * 0.05f);

            // The screen: squeezed between the palms, bowing toward the camera and buckling into creases as it goes.
            float bow = squeeze * width * 0.12f;
            float buckle = squeeze * squeeze;
            for (int i = 0; i < grid.Length; i++)
                sheetVertices[i] = Bend(grid[i], half, settle, bow, buckle, creases[i]);
            sheetMesh.vertices = sheetVertices;
            sheetMesh.RecalculateNormals();
            sheetMesh.RecalculateBounds();
            // It glows as the screen did, then takes the light as it bends.
            float lit = Mathf.Clamp01(squeeze * 1.6f);
            sheetMaterial.SetColor("_BaseColor", Color.Lerp(Color.black, new Color(0.8f, 0.8f, 0.8f), lit));
            sheetMaterial.SetColor("_EmissionColor", Color.white * Mathf.Lerp(1f, 0.35f, lit));

            float reach = CrackReachAt(t);
            float line = width * 0.0022f;
            for (int i = 0; i < cracks.Length; i++)
            {
                Crack crack = cracks[i];
                float shown = reach <= crack.Near ? 0f : crack.Far - crack.Near < 1e-4f ? 1f : Mathf.Clamp01((reach - crack.Near) / (crack.Far - crack.Near));
                Vector2 end = Vector2.Lerp(crack.From, crack.To, shown);
                // Just in front of the screen, clear of its shallow creases.
                Vector3 a = Bend(crack.From, half, settle, bow, buckle, Vector3.zero) + Vector3.back * (0.012f + buckle * width * 0.025f);
                Vector3 b = Bend(end, half, settle, bow, buckle, Vector3.zero) + Vector3.back * (0.012f + buckle * width * 0.025f);
                SetQuad(i * 8, a, b, shown > 0f ? line * 3f : 0f);
                SetQuad(i * 8 + 4, a + Vector3.back * 0.002f, b + Vector3.back * 0.002f, shown > 0f ? line : 0f);
            }
            crackMesh.vertices = crackVertices;
            crackMesh.RecalculateBounds();
        }

        private void PlaceHands(float x, float curl)
        {
            foreach (Rig rig in new[] { left, right })
            {
                rig.Pivot.localPosition = new Vector3(rig.Side * x, -0.1f, -0.05f);
                rig.Pivot.localRotation = Quaternion.Euler(0f, 0f, rig.Side * -6f);
                rig.Pivot.localScale = Vector3.one * HandScale;
                rig.Curl(curl);
            }
        }

        // The screen's shape at a given squeeze, once the hands have arrived and it has drawn back.
        private void Shape(float squeeze, out float half, out float bow, out float buckle)
        {
            half = width * 0.5f * ScreenSettle * (1f - 0.95f * squeeze);
            bow = squeeze * width * 0.12f;
            buckle = squeeze * squeeze;
        }

        // After the break, s seconds on: the pieces burst out from between the palms and tumble down, and the hands, after
        // a moment pressed together and a small spring apart, draw away.
        private void PoseBroken(float s)
        {
            Shape(1f, out float half, out _, out _);
            float meet = half + 0.2f * HandScale;
            float part = 0.12f * EaseOut(Span(s, 0f, 0.35f)) + (width * 0.5f + 2.2f) * Ease(Span(s, HandsPartFrom, HandsPartTo));
            PlaceHands(meet + part, 0.42f);
            float jolt = Kick(s + BreakAt, BreakAt) * 1.6f;
            camera.transform.localPosition = cameraRest + (calm ? Vector3.zero : new Vector3(Noise(), Noise(), 0f) * jolt * 0.05f);

            float burst = EaseOut(Span(s, 0f, BurstSeconds));
            float edge = width * 0.0022f * 1.4f;
            foreach (Shard shard in shards)
            {
                float m = Mathf.Max(0f, s - shard.Delay);
                Vector3 centre = shard.Start + shard.Velocity * m + Vector3.down * (0.5f * ShardGravity * m * m);
                Quaternion turn = Quaternion.AngleAxis(shard.Spin * m, shard.Axis);
                int n = shard.Squeezed.Length;
                shardVertices[shard.FirstVertex] = centre;
                for (int c = 0; c < n; c++)
                    shardVertices[shard.FirstVertex + 1 + c] = centre + turn * Vector3.Lerp(shard.Squeezed[c], shard.Sprung[c], burst);
                for (int c = 0; c < n; c++)
                {
                    Vector3 a = shardVertices[shard.FirstVertex + 1 + c], b = shardVertices[shard.FirstVertex + 1 + (c + 1) % n];
                    Vector3 along = b - a;
                    Vector3 side = along.sqrMagnitude < 1e-8f ? Vector3.zero : Vector3.Cross(along, Vector3.back).normalized * (edge * 0.5f);
                    int e = (shard.FirstEdge + c) * 4;
                    Vector3 lift = Vector3.back * 0.004f;
                    edgeVertices[e] = a - side + lift;
                    edgeVertices[e + 1] = a + side + lift;
                    edgeVertices[e + 2] = b + side + lift;
                    edgeVertices[e + 3] = b - side + lift;
                }
            }
            shardMesh.vertices = shardVertices;
            shardMesh.RecalculateNormals();
            shardMesh.RecalculateBounds();
            edgeMesh.vertices = edgeVertices;
            edgeMesh.RecalculateBounds();
        }

        // The screen's glass, cut as the old shatter cuts the screen: rays out from where the hands meet, crossed by uneven
        // rings, each cell a piece. Each piece starts where that part of the squeezed screen is when the hands meet and
        // springs back to the shape it had half squeezed as it bursts out. The pieces nearest the middle go first.
        private Shard[] BuildShards(out Mesh pieces, out Mesh edges)
        {
            const int rays = 12, rings = 4;
            Vector2 impact = new Vector2(0f, -0.03f);
            float reach = 0f;
            foreach (Vector2 corner in new[] { new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f), new Vector2(-0.5f, 0.5f), new Vector2(0.5f, 0.5f) })
                reach = Mathf.Max(reach, Distance(corner, impact));
            float tall = width / height;
            float step = Mathf.PI * 2f / rays;
            float[] angles = new float[rays];
            for (int j = 0; j < rays; j++) angles[j] = j * step + Noise() * 0.35f * step;
            Vector2[,] points = new Vector2[rings + 1, rays];
            for (int k = 0; k <= rings; k++)
                for (int j = 0; j < rays; j++)
                {
                    float radius = k == rings ? reach * 1.6f : reach * Mathf.Pow(k / (float)rings, 1.3f) * (k == 0 ? 1f : 1f + Noise() * 0.14f);
                    float angle = angles[j] + (k == 0 ? 0f : Noise() * 0.12f * step);
                    Vector2 point = impact + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) * tall) * radius;
                    // Cut to the screen's own edges.
                    points[k, j] = new Vector2(Mathf.Clamp(point.x, -0.5f, 0.5f), Mathf.Clamp(point.y, -0.5f, 0.5f));
                }

            Shape(1f, out float halfMet, out float bowMet, out float buckleMet);
            Shape(0.5f, out float halfSprung, out float bowSprung, out float buckleSprung);
            List<Shard> list = new List<Shard>();
            List<Vector3> positions = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> triangles = new List<int>(), edgeTriangles = new List<int>();
            int edgeCount = 0;
            for (int k = 0; k < rings; k++)
                for (int j = 0; j < rays; j++)
                {
                    int next = (j + 1) % rays;
                    Vector2[] corners = k == 0
                        ? new[] { points[0, j], points[1, j], points[1, next] }
                        : new[] { points[k, j], points[k + 1, j], points[k + 1, next], points[k, next] };
                    if (Area(corners) < 1e-5f) continue;
                    Vector2 middle = Vector2.zero;
                    foreach (Vector2 corner in corners) middle += corner;
                    middle /= corners.Length;
                    Vector3 met = Bend(middle, halfMet, ScreenSettle, bowMet, buckleMet, Vector3.zero);
                    Vector3 sprung = Bend(middle, halfSprung, ScreenSettle, bowSprung, buckleSprung, Vector3.zero);
                    Shard shard = new Shard
                    {
                        Squeezed = new Vector3[corners.Length],
                        Sprung = new Vector3[corners.Length],
                        Start = met,
                        FirstVertex = positions.Count,
                        FirstEdge = edgeCount,
                    };
                    for (int c = 0; c < corners.Length; c++)
                    {
                        shard.Squeezed[c] = Bend(corners[c], halfMet, ScreenSettle, bowMet, buckleMet, Vector3.zero) - met;
                        shard.Sprung[c] = Bend(corners[c], halfSprung, ScreenSettle, bowSprung, buckleSprung, Vector3.zero) - sprung;
                    }
                    // Out from between the palms: a little to the side, up or down, and toward the camera, then falling.
                    Vector2 away = middle - impact;
                    shard.Velocity = new Vector3(away.x * 0.9f + Noise() * 0.15f, away.y * 2.2f + 0.2f + (float)random.NextDouble() * 0.9f,
                        -(0.3f + (float)random.NextDouble() * 1.1f));
                    shard.Axis = new Vector3(Noise(), Noise(), Noise()).normalized;
                    if (shard.Axis.sqrMagnitude < 0.01f) shard.Axis = Vector3.forward;
                    shard.Spin = (120f + (float)random.NextDouble() * 300f) * (Noise() < 0f ? -1f : 1f) * (calm ? 0.4f : 1f);
                    shard.Delay = Distance(middle, impact) / reach * 0.35f + (float)random.NextDouble() * 0.08f;

                    // A fan from its centre, each corner showing its own part of the screen.
                    positions.Add(met);
                    uvs.Add(middle + new Vector2(0.5f, 0.5f));
                    for (int c = 0; c < corners.Length; c++)
                    {
                        positions.Add(met + shard.Squeezed[c]);
                        uvs.Add(corners[c] + new Vector2(0.5f, 0.5f));
                    }
                    for (int c = 0; c < corners.Length; c++)
                    {
                        triangles.Add(shard.FirstVertex);
                        triangles.Add(shard.FirstVertex + 1 + c);
                        triangles.Add(shard.FirstVertex + 1 + (c + 1) % corners.Length);
                        int e = (edgeCount + c) * 4;
                        edgeTriangles.AddRange(new[] { e, e + 1, e + 2, e, e + 2, e + 3 });
                    }
                    edgeCount += corners.Length;
                    list.Add(shard);
                }

            pieces = new Mesh { name = "Glass Pieces" };
            pieces.MarkDynamic();
            pieces.SetVertices(positions);
            pieces.SetUVs(0, uvs);
            pieces.SetTriangles(triangles, 0);
            edges = new Mesh { name = "Glass Edges" };
            edges.MarkDynamic();
            edges.vertices = new Vector3[edgeCount * 4];
            edges.SetTriangles(edgeTriangles, 0);
            return list.ToArray();
        }

        private static float Area(Vector2[] corners)
        {
            float area = 0f;
            for (int c = 0; c < corners.Length; c++)
            {
                Vector2 a = corners[c], b = corners[(c + 1) % corners.Length];
                area += a.x * b.y - b.x * a.y;
            }
            return Mathf.Abs(area) * 0.5f;
        }

        // A point on the screen, squeezed to the hands' gap, bowed out toward the camera in the middle, and (for the screen
        // itself) buckled by its creases. The cracks follow the same shape without the creases.
        private Vector3 Bend(Vector2 point, float half, float settle, float bow, float buckle, Vector3 crease)
        {
            float x = point.x * 2f * half;
            float z = -bow * Mathf.Cos(point.x * Mathf.PI) + crease.z * buckle * width * 0.025f;
            float y = point.y * height * settle + crease.y * buckle * height * 0.02f;
            return new Vector3(x + crease.x * buckle * half * 0.08f, y, z);
        }

        private void SetQuad(int index, Vector3 a, Vector3 b, float thickness)
        {
            Vector3 along = b - a;
            Vector3 side = along.sqrMagnitude < 1e-8f ? Vector3.zero : Vector3.Cross(along, Vector3.back).normalized * (thickness * 0.5f);
            crackVertices[index] = a - side;
            crackVertices[index + 1] = a + side;
            crackVertices[index + 2] = b + side;
            crackVertices[index + 3] = b - side;
        }

        private static float CrackReachAt(float t)
        {
            if (t < ArriveTo) return 0f;
            float reach = CrackReach[0] * EaseOut(Span(t, ArriveTo, ArriveTo + 0.15f));
            for (int i = 0; i < Shoves.Length; i++)
                reach = Mathf.Lerp(reach, CrackReach[i + 1], EaseOut(Span(t, Shoves[i], Shoves[i] + 0.2f)));
            // Racing across as the hands rush in, and everywhere once they meet.
            reach = Mathf.Lerp(reach, 0.9f, Span(t, RushFrom, BreakAt));
            return t >= BreakAt ? 2f : reach;
        }

        // A sharp kick at contact and each shove, and the biggest as the hands meet.
        private static float Jolt(float t)
        {
            float jolt = 0.4f * Kick(t, ArriveTo) + Kick(t, BreakAt) * 1.6f;
            foreach (float shove in Shoves) jolt += Kick(t, shove);
            return jolt;
        }

        private static float Kick(float t, float at) => t < at ? 0f : Mathf.Exp(-(t - at) * 12f);

        public void Draw(float t)
        {
            float w = Screen.width, h = Screen.height;
            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, w, 0f, h);
            Quad(plainMaterial, w, h, Color.black);
            Quad(viewMaterial, w, h, Color.white);
            // The instant it breaks: a white flash as the glass bursts out.
            if (t >= BreakAt) Quad(plainMaterial, w, h, new Color(1f, 1f, 1f, 0.6f * Mathf.Exp(-(t - BreakAt) * 14f)));
            GL.PopMatrix();
        }

        /// <summary>Renders the stage as it is now into a PNG (the editor's frame preview).</summary>
        internal void RenderTo(string path)
        {
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = view;
            Texture2D frame = new Texture2D(view.width, view.height, TextureFormat.RGB24, false);
            frame.ReadPixels(new Rect(0, 0, view.width, view.height), 0, 0);
            frame.Apply();
            RenderTexture.active = previous;
            System.IO.File.WriteAllBytes(path, frame.EncodeToPNG());
            Kill(frame);
        }

        public void Dispose()
        {
            if (camera != null) camera.targetTexture = null;
            Kill(stage);
            foreach (Rig rig in new[] { left, right }) if (rig?.Model != null) Kill(rig.Model.gameObject);
            if (view != null) { view.Release(); Kill(view); }
            Kill(handMaterial);
            Kill(sheetMaterial);
            Kill(crackDark);
            Kill(crackLight);
            Kill(viewMaterial);
            Kill(plainMaterial);
            Kill(sheetMesh);
            Kill(crackMesh);
            Kill(shardMesh);
            Kill(edgeMesh);
            Kill(soundHost);
            Kill(snap);
        }

        private static void Kill(UnityEngine.Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(target);
            else UnityEngine.Object.DestroyImmediate(target);
        }

        private static void Quad(Material material, float w, float h, Color colour)
        {
            material.SetPass(0);
            GL.Begin(GL.QUADS);
            GL.Color(colour);
            GL.TexCoord2(0f, 0f); GL.Vertex3(0f, 0f, 0f);
            GL.TexCoord2(0f, 1f); GL.Vertex3(0f, h, 0f);
            GL.TexCoord2(1f, 1f); GL.Vertex3(w, h, 0f);
            GL.TexCoord2(1f, 0f); GL.Vertex3(w, 0f, 0f);
            GL.End();
        }

        private static float Span(float t, float from, float to) => Mathf.Clamp01((t - from) / (to - from));
        private static float Ease(float t) => t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
        private static float EaseOut(float t) => 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);
    }
}
