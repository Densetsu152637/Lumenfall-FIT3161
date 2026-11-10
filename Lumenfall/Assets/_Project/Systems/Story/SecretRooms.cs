using System;
using System.Collections.Generic;
using Lumenfall.Cutscenes;
using Lumenfall.DataDriven;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Inventory;
using Lumenfall.Startup;
using Lumenfall.Rendering;
using UnityEngine;

namespace Lumenfall.Story
{
    /// <summary>
    /// The small rooms the campus secrets lead to: the developers' bedroom behind the dorm doors
    /// (<see cref="DormKnockSecret"/>), the dog's room past the north gate (<see cref="AnnoyingDogGate"/>), the staff
    /// closet beside the main hall (<see cref="StaffClosetSecret"/>) and the forgotten room the white dog's trail leads to
    /// (<see cref="WhiteDogTrailSecret"/>). Like Orio's legacy hall, each is built in code and
    /// resolved by the world loader through its map id, so a save made inside one restores. Each has one person to talk to
    /// (whose dialogue hands over a curio, see <see cref="MiscCollectibles.SecretRewards"/>) and a glowing ring by the
    /// door: stepping onto it takes the player back to where the secret was found. A room can be entered once: entering
    /// sets its <see cref="VisitedFlag"/>, and its secret answers as an ordinary locked door from then on, so a curio
    /// left behind stays behind.
    /// </summary>
    public sealed class SecretRooms : MonoBehaviour
    {
        public const string DevRoomId = "secret.devRoom";
        public const string DogRoomId = "secret.dogRoom";
        public const string ClosetId = "secret.staffCloset";
        public const string ForgottenRoomId = "secret.forgottenRoom";

        private const string ExitName = "Secret Exit Ring";
        private const string SleeperName = "Zzz Anchor";

        // Found by name by SecretRoomMood, which brings the two quiet rooms to life.
        internal const string PendantPrefix = "Pendant ";
        internal const string DoorwayLightName = "Doorway Light";
        internal const string ForgottenManGroupName = "Forgotten Man";
        internal const string EggName = "Egg";
        internal const string EggLookName = "Egg Look";
        internal const string PhotoName = "Photograph";
        internal const string SleepingDogName = "Sleeping Dog";
        internal const string LoadingSpinnerName = "Loading Spinner";
        /// <summary>Which of the forgotten room's lamps (counting from the door) is failing; the first is dead.</summary>
        internal const int FailingPendant = 1;
        /// <summary>Lost Echoes from this row of the forgotten room or beyond reaches the photograph behind the man.</summary>
        internal const int PhotoRow = 11;

        /// <summary>The things in the rooms worth a closer look, and the narrated dialogue each plays.</summary>
        internal static readonly string[] InspectDialogues =
        {
            "inspect_forgotten_frames", "inspect_forgotten_coat_rack", "inspect_forgotten_chair", "secret_forgotten_egg",
            "inspect_dog_loading_bar", "inspect_dog_hole", "inspect_dog_bowl",
        };

        /// <summary>A secret room: its size, where the player arrives and leaves, and where leaving takes them.</summary>
        public sealed class Room
        {
            public string Id;
            public string Name;
            public int Width;
            public int Height;
            public Vector2Int Arrival;
            public Vector2Int Exit;
            public string ReturnMapId;
            public Vector2Int ReturnCell;
            internal Action<Transform, SecretRoomArt> Build;
        }

        private static readonly Dictionary<string, Room> Rooms = new Dictionary<string, Room>(StringComparer.OrdinalIgnoreCase)
        {
            [DevRoomId] = new Room
            {
                Id = DevRoomId, Name = "The devs' room", Width = 9, Height = 7, Arrival = new Vector2Int(4, 1), Exit = new Vector2Int(4, 0),
                // The dorm hallway, in front of Room Door 4.
                ReturnMapId = SettlingInScene.MapId, ReturnCell = new Vector2Int(23, 17), Build = BuildDevRoom
            },
            [DogRoomId] = new Room
            {
                Id = DogRoomId, Name = "A very white room", Width = 9, Height = 7, Arrival = new Vector2Int(4, 1), Exit = new Vector2Int(4, 0),
                // Just south of the campus north gate.
                ReturnMapId = "campus", ReturnCell = new Vector2Int(26, 87), Build = BuildDogRoom
            },
            [ClosetId] = new Room
            {
                Id = ClosetId, Name = "Staff closet", Width = 6, Height = 5, Arrival = new Vector2Int(2, 1), Exit = new Vector2Int(2, 0),
                // In front of the main hall door.
                ReturnMapId = "campus", ReturnCell = new Vector2Int(18, 44), Build = BuildCloset
            },
            [ForgottenRoomId] = new Room
            {
                Id = ForgottenRoomId, Name = "A forgotten room", Width = 5, Height = 15, Arrival = new Vector2Int(2, 1), Exit = new Vector2Int(2, 0),
                // Just south of the campus north gate, where the white dog's trail ends.
                ReturnMapId = "campus", ReturnCell = new Vector2Int(26, 87), Build = BuildForgottenRoom
            },
        };

        private static readonly Dictionary<string, WorldAreaAsset> Areas = new Dictionary<string, WorldAreaAsset>(StringComparer.OrdinalIgnoreCase);
        private static SecretRooms instance;
        private string currentMapId;
        private Transform exitRing;
        private readonly List<Transform> sleepers = new List<Transform>();
        private GUIStyle zzzStyle;

        public static IEnumerable<Room> All => Rooms.Values;

        /// <summary>Set the first time the player enters the room; its way in stays shut after that.</summary>
        public static string VisitedFlag(string roomId) => "secret.visited." + roomId;
        public static bool HasVisited(GameEventRuntime runtime, string roomId) => runtime != null && runtime.GetFlag(VisitedFlag(roomId));
        public static bool IsSecretRoom(string mapId) => mapId != null && Rooms.ContainsKey(mapId);

        public static bool TryGetRoom(string mapId, out Room room)
        {
            room = null;
            return mapId != null && Rooms.TryGetValue(mapId, out room);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
            Areas.Clear();
        }

        /// <summary>The room's runtime area, built the first time it is asked for. False for any other map id.</summary>
        public static bool TryGetArea(string mapId, out WorldAreaAsset area)
        {
            area = null;
            if (!TryGetRoom(mapId, out Room room)) return false;
            if (!Areas.TryGetValue(room.Id, out area) || area == null)
            {
                area = ScriptableObject.CreateInstance<WorldAreaAsset>();
                area.name = room.Name + " (runtime)";
                area.hideFlags = HideFlags.DontSave;
                area.definition = new MapDefinition
                {
                    id = room.Id, width = room.Width, height = room.Height,
                    spawn = new SpawnDefinition { x = room.Arrival.x, y = room.Arrival.y }
                };
                area.areaPrefab = BuildTemplate(room);
                Areas[room.Id] = area;
            }
            return true;
        }

        /// <summary>
        /// Takes the player into a secret room, the one time they can go in. Found without the hints that lead to it
        /// (<see cref="SequenceBreakEnding.KnowsTooMuch"/>), it does not open: the story breaks instead.
        /// </summary>
        public static bool Enter(string roomId)
        {
            DataDrivenWorldLoader loader = DataDrivenWorldLoader.Instance;
            GameEventRuntime runtime = GameEventManager.Instance != null ? GameEventManager.Instance.Runtime : null;
            if (loader == null || !TryGetRoom(roomId, out Room room) || HasVisited(runtime, room.Id)) return false;
            if (SequenceBreakEnding.KnowsTooMuch(runtime, room.Id))
            {
                SequenceBreakEnding.Begin(SequenceBreakEnding.Cause.Knowledge);
                return false;
            }
            if (!loader.TravelTo(room.Id, room.Arrival)) return false;
            runtime?.SetFlag(VisitedFlag(room.Id), true);
            return true;
        }

        /// <summary>Called by the area loader on every map load, before EndMapRebuild: sets up the secrets on that map.</summary>
        public static void RegisterForMap(string mapId, GridWorld world, GameObject areaRoot)
        {
            if (instance == null)
            {
                GameObject host = new GameObject("[Secret Rooms]");
                instance = host.AddComponent<SecretRooms>();
                DontDestroyOnLoad(host);
            }
            instance.currentMapId = mapId;
            instance.exitRing = null;
            instance.sleepers.Clear();
            if (IsSecretRoom(mapId) && areaRoot != null)
                foreach (Transform child in areaRoot.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name == ExitName) instance.exitRing = child;
                    else if (child.name == SleeperName) instance.sleepers.Add(child);
                }
            // The developers' (and the forgotten man's) beards, curls and collars are fitted to the placed models.
            bool dressed = string.Equals(mapId, DevRoomId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mapId, ForgottenRoomId, StringComparison.OrdinalIgnoreCase);
            if (dressed && areaRoot != null)
                foreach (DynamicNpc npc in areaRoot.GetComponentsInChildren<DynamicNpc>(true))
                    foreach (DevChibis.Look look in DevChibis.Dressed)
                        if (npc.Definition != null && npc.Definition.id == look.Id) DevChibis.Dress(npc.gameObject, look);
            StaffClosetSecret.RegisterForMap(mapId, areaRoot);
            SecretRoomMood.OnMapLoaded(mapId, areaRoot);
            WhiteDogTrailSecret.OnMapLoaded(GameEventManager.Instance != null ? GameEventManager.Instance.Runtime : null, mapId);
            LanternBearerSecret.RegisterForMap(mapId, world, areaRoot);
        }

        private void Update()
        {
            if (exitRing != null) exitRing.Rotate(0f, 85f * Time.unscaledDeltaTime, 0f, Space.Self);
            if (!TryGetRoom(currentMapId, out Room room) || !CanLeave()) return;
            GridMover player = GridMover.FindActive();
            if (player == null || player.IsMoving || player.IsMovementLocked || player.CurrentCell != room.Exit) return;
            DataDrivenWorldLoader.Instance?.TravelTo(room.ReturnMapId, room.ReturnCell);
        }

        private static bool CanLeave() =>
            !CutscenePlayer.BlocksInput && !StartupMenu.IsOpen && Time.timeScale > 0f && !InventoryUiState.IsOpen &&
            GameEventManager.Instance?.Runtime?.ActiveDialogue == null && DataDrivenWorldLoader.Instance != null;

        // Sleepers snore: a drifting "z Z z" over each one.
        // Drawn with GUI only, never GUILayout, so skip the per-frame layout pass and its allocations.
        private void Awake() => useGUILayout = false;

        private void OnGUI()
        {
            using (UiScale.Begin()) DrawGui();
        }

        private void DrawGui()
        {
            if (sleepers.Count == 0 || Camera.main == null || StartupMenu.IsOpen) return;
            if (zzzStyle == null) zzzStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            Color previous = GUI.color;
            for (int i = 0; i < sleepers.Count; i++)
            {
                Transform anchor = sleepers[i];
                if (anchor == null || !anchor.gameObject.activeInHierarchy) continue;
                float phase = Mathf.Repeat(Time.unscaledTime * 0.45f + i * 0.37f, 1f);
                Vector3 screen = Camera.main.WorldToScreenPoint(anchor.position + Vector3.up * (phase * 0.6f));
                if (screen.z <= 0f) continue;
                GUI.color = new Color(0.85f, 0.9f, 1f, Mathf.Sin(phase * Mathf.PI));
                Vector2 point = UiScale.FromScreenPoint(screen);
                GUI.Label(new Rect(point.x - 40f + phase * 14f, point.y - 14f, 80f, 28f), phase < 0.5f ? "z Z" : "Z z Z", zzzStyle);
            }
            GUI.color = previous;
        }

        // ---- Building ----

        // An inactive template: the loader instantiates and activates it like an authored area prefab. A cell (x, y) is
        // local (x, 0, y), and the map bounds keep the player inside the walls.
        private static GameObject BuildTemplate(Room room)
        {
            GameObject template = new GameObject(room.Name);
            template.SetActive(false);
            DontDestroyOnLoad(template);
            room.Build(template.transform, SecretRoomArt.Load());
            Ring(template.transform, room.Exit);
            return template;
        }

        private static void BuildDevRoom(Transform root, SecretRoomArt art)
        {
            Room room = Rooms[DevRoomId];
            Shell(root, room, new Color(0.13f, 0.12f, 0.2f), new Color(0.2f, 0.19f, 0.3f), 1.6f);
            // Three desks along the back wall, a developer at each: two hard at work, and Nick, who is mostly asleep.
            (int x, DevChibis.Look look, GameObject prefab)[] desks =
            {
                (1, DevChibis.Glasses, art != null ? art.devGlasses : null),
                (4, DevChibis.Nick, art != null ? art.devNick : null),
                (7, DevChibis.Curls, art != null ? art.devCurls : null),
            };
            foreach ((int x, DevChibis.Look look, GameObject prefab) in desks)
            {
                GameObject desk = Model(root, art != null ? art.desk : null, "Desk", new Vector2Int(x, 6), 180f, 1.6f, Vector2Int.zero);
                float top = desk != null ? MeshBounds(desk).max.y : 0.75f;
                Model(root, art != null ? art.computerScreen : null, "Monitor", new Vector2Int(x, 6), 180f, 0.65f, Vector2Int.zero, top);
                Model(root, art != null ? art.keyboard : null, "Keyboard", new Vector2Int(x, 6), 180f, 0.45f, Vector2Int.zero, top, -0.25f);
                Glow(root, "Monitor Glow", new Vector3(x, 1.2f, 5.6f), new Color(0.45f, 0.65f, 1f), 2.6f, 1.1f);
                Blocker(root, new Vector2Int(x, 6), Vector2Int.one);
                if (look == DevChibis.Nick)
                    // His (cold) coffee, which he hands over.
                    Part(PrimitiveType.Cylinder, "Coffee Mug", root, new Vector3(x + 0.5f, top + 0.06f, 5.85f), new Vector3(0.1f, 0.06f, 0.1f), new Color(0.92f, 0.92f, 0.9f), 0.05f);
                else
                    Model(root, art != null ? art.lamp : null, "Desk Lamp", new Vector2Int(x, 6), 180f, 0.28f, Vector2Int.zero, top, 0.35f);

                Vector2Int cell = new Vector2Int(x, 5);
                // Facing their screen, back to the room, until someone comes to talk.
                GameObject dev = DevChibis.Create(prefab, look, root);
                if (dev == null) dev = Part(PrimitiveType.Capsule, look.Name, root, new Vector3(0f, 0.8f, 0f), new Vector3(0.6f, 0.8f, 0.6f), look.Outfit, 0.1f);
                dev.transform.localPosition = new Vector3(cell.x, 0f, cell.y);
                dev.transform.localRotation = Quaternion.identity;
                if (look == DevChibis.Nick) Anchor(dev.transform, SleeperName, new Vector3(0f, 1.75f, 0f));
                Talker(dev, look.Id, look.Name, look.DialogueId);
                Blocker(root, cell, Vector2Int.one);
            }
            Model(root, art != null ? art.bookcase : null, "Bookcase", new Vector2Int(8, 1), 270f, 1f, new Vector2Int(1, 1));
            Model(root, art != null ? art.plant : null, "Plant", new Vector2Int(0, 1), 0f, 0.6f, new Vector2Int(1, 1));
            // Takeaway boxes and an energy drink, because of course.
            Part(PrimitiveType.Cube, "Takeaway Box", root, new Vector3(6.2f, 0.12f, 2.3f), new Vector3(0.35f, 0.25f, 0.35f), new Color(0.93f, 0.9f, 0.82f), 0.05f);
            Part(PrimitiveType.Cylinder, "Energy Drink", root, new Vector3(6.6f, 0.15f, 2.1f), new Vector3(0.12f, 0.15f, 0.12f), new Color(0.3f, 0.95f, 0.45f), 0.6f);
            Blocker(root, new Vector2Int(6, 2), Vector2Int.one);
        }

        // A room that has not been finished, or perhaps never started: white with no edges (SecretRoomMood washes the
        // walls out into white fog and a white sky), and in the middle of it the dog, asleep on a pile of scrolls under a
        // loading spinner. Around it, what it has been up to: the chewed-up loading bar from the north gate, a bowl, and
        // a hole in the floor where the world's texture has not loaded, which is how it got in.
        private static void BuildDogRoom(Transform root, SecretRoomArt art)
        {
            Room room = Rooms[DogRoomId];
            Shell(root, room, new Color(0.97f, 0.97f, 0.98f), Color.white, 2.2f, 0.32f, 0.55f);
            Vector2Int bed = SecretRoomMood.DogBedCell;
            Color white = new Color(0.98f, 0.98f, 0.98f);
            Color ink = new Color(0.1f, 0.1f, 0.1f);
            // Its only shadow, soft and grey.
            Part(PrimitiveType.Cylinder, "Dog Shadow", root, new Vector3(bed.x, 0.003f, bed.y), new Vector3(1.35f, 0.002f, 1.05f), new Color(0.8f, 0.8f, 0.85f), 0.18f);
            // A pile of scrolls, and the dog asleep on top of it.
            for (int i = 0; i < 9; i++)
            {
                float angle = i * 47f;
                float reach = i < 6 ? 0.34f : 0.16f;
                Vector3 offset = new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad) * reach, 0.07f + (i % 3) * 0.08f, Mathf.Sin(angle * Mathf.Deg2Rad) * reach * 0.85f);
                Part(PrimitiveType.Cylinder, "Scroll", root, new Vector3(bed.x, 0f, bed.y) + offset, new Vector3(0.12f, 0.32f, 0.12f), new Color(0.93f, 0.87f, 0.7f), 0.08f, Quaternion.Euler(0f, angle, 90f));
            }
            BuildDog(root, new Vector3(bed.x, 0.28f, bed.y), white, ink);
            Blocker(root, bed, Vector2Int.one);

            // A loading spinner over it, forever nearly done.
            GameObject spinner = new GameObject(LoadingSpinnerName);
            spinner.transform.SetParent(root, false);
            spinner.transform.localPosition = new Vector3(bed.x, 1.45f, bed.y);
            for (int i = 0; i < 8; i++)
            {
                float angle = i * 45f * Mathf.Deg2Rad;
                float size = Mathf.Lerp(0.085f, 0.03f, i / 7f);
                Part(PrimitiveType.Cube, "Spinner Dot", spinner.transform, new Vector3(Mathf.Sin(angle), Mathf.Cos(angle), 0f) * 0.17f, Vector3.one * size,
                    new Color(0.96f, 0.8f, 0.45f), Mathf.Lerp(1.4f, 0.3f, i / 7f));
            }

            // The loading bar from the north gate, thoroughly chewed: its dark track snapped in two, gold bits everywhere.
            Vector2Int bar = new Vector2Int(2, 3);
            Color track = new Color(0.2f, 0.18f, 0.26f);
            Color gold = new Color(0.96f, 0.8f, 0.45f);
            Part(PrimitiveType.Cube, "Chewed Bar", root, new Vector3(bar.x - 0.22f, 0.04f, bar.y - 0.05f), new Vector3(0.7f, 0.08f, 0.13f), track, 0.05f, Quaternion.Euler(0f, 14f, 0f));
            Part(PrimitiveType.Cube, "Chewed Bar", root, new Vector3(bar.x + 0.36f, 0.04f, bar.y + 0.16f), new Vector3(0.42f, 0.08f, 0.13f), track, 0.05f, Quaternion.Euler(0f, -38f, 4f));
            Part(PrimitiveType.Cube, "Bar Fill", root, new Vector3(bar.x - 0.3f, 0.085f, bar.y - 0.07f), new Vector3(0.46f, 0.02f, 0.07f), gold, 0.8f, Quaternion.Euler(0f, 14f, 0f));
            (float x, float z, float yaw)[] bits = { (0.3f, -0.3f, 20f), (-0.4f, 0.32f, 70f), (0.05f, 0.38f, -15f), (0.45f, -0.05f, 40f), (-0.15f, -0.38f, 85f), (-0.48f, -0.2f, 10f) };
            foreach ((float x, float z, float yaw) in bits)
                Part(PrimitiveType.Cube, "Bar Bit", root, new Vector3(bar.x + x, 0.02f, bar.y + z), new Vector3(0.1f, 0.04f, 0.06f), gold, 0.8f, Quaternion.Euler(0f, yaw, 0f));
            Look(root, bar, "inspect_dog_loading_bar", true);

            // Where the floor has not loaded: a pink-and-black checkerboard, sunk a little, with loose pixels about it.
            Vector2Int hole = SecretRoomMood.HoleCell;
            GameObject missing = Part(PrimitiveType.Cube, "Missing Texture", root, new Vector3(hole.x, 0.004f, hole.y), new Vector3(0.92f, 0.01f, 0.92f), Color.white, 0f);
            Material checker = missing.GetComponent<Renderer>().sharedMaterial;
            Texture2D pattern = MissingTexture();
            checker.mainTexture = pattern;
            checker.SetTexture("_BaseMap", pattern);
            checker.SetTexture("_EmissionMap", pattern);
            checker.SetColor("_EmissionColor", Color.white * 0.7f);
            (float x, float z, float y, bool pink)[] pixels = { (-0.62f, 0.2f, 0.05f, true), (0.1f, -0.6f, 0.03f, false), (-0.5f, -0.45f, 0.12f, false), (0.55f, 0.5f, 0.2f, true) };
            foreach ((float x, float z, float y, bool pink) in pixels)
                Part(PrimitiveType.Cube, "Loose Pixel", root, new Vector3(hole.x + x, y, hole.y + z), Vector3.one * 0.08f,
                    pink ? new Color(1f, 0f, 0.86f) : new Color(0.03f, 0.03f, 0.03f), pink ? 0.9f : 0f, Quaternion.Euler(0f, x * 90f, 0f));
            Look(root, hole, "inspect_dog_hole", true);

            // Its bowl.
            Vector2Int bowl = new Vector2Int(6, 2);
            Part(PrimitiveType.Cylinder, "Bowl", root, new Vector3(bowl.x, 0.055f, bowl.y), new Vector3(0.38f, 0.055f, 0.38f), white, 0.2f);
            Part(PrimitiveType.Cylinder, "Bowl Band", root, new Vector3(bowl.x, 0.06f, bowl.y), new Vector3(0.39f, 0.018f, 0.39f), new Color(0.86f, 0.22f, 0.22f), 0.15f);
            Part(PrimitiveType.Sphere, "Kibble", root, new Vector3(bowl.x, 0.12f, bowl.y), Vector3.one * 0.06f, new Color(0.55f, 0.36f, 0.2f), 0.05f);
            Look(root, bowl, "inspect_dog_bowl", true);
        }

        // The dog, rounder than before, curled on its side with its floppy dark ears down. SecretRoomMood breathes its
        // Body, bobs its Head, twitches an Ear and thumps its Tail, so those keep their names.
        private static void BuildDog(Transform root, Vector3 position, Color white, Color ink)
        {
            GameObject dog = new GameObject(SleepingDogName);
            dog.transform.SetParent(root, false);
            dog.transform.localPosition = position;
            Part(PrimitiveType.Sphere, "Body", dog.transform, new Vector3(0f, 0.16f, 0f), new Vector3(0.62f, 0.34f, 0.4f), white, 0.15f);
            Part(PrimitiveType.Sphere, "Head", dog.transform, new Vector3(0.36f, 0.24f, 0f), new Vector3(0.34f, 0.3f, 0.32f), white, 0.15f);
            Transform head = dog.transform.Find("Head");
            Part(PrimitiveType.Sphere, "Snout", head, new Vector3(0.42f, -0.12f, 0f), new Vector3(0.48f, 0.4f, 0.44f), white, 0.15f);
            Part(PrimitiveType.Sphere, "Nose", head, new Vector3(0.66f, -0.06f, 0f), new Vector3(0.14f, 0.14f, 0.16f), ink, 0f);
            // Just proud of the head's surface, so they show. (The head's scale applies to everything on it.)
            Part(PrimitiveType.Cube, "Closed Eye", head, new Vector3(0.44f, 0.14f, 0.24f), new Vector3(0.04f, 0.05f, 0.18f), ink, 0f, Quaternion.Euler(0f, -20f, 0f));
            Part(PrimitiveType.Cube, "Closed Eye", head, new Vector3(0.44f, 0.14f, -0.24f), new Vector3(0.04f, 0.05f, 0.18f), ink, 0f, Quaternion.Euler(0f, 20f, 0f));
            foreach (float side in new[] { -1f, 1f })
            {
                Part(PrimitiveType.Cube, "Ear", dog.transform, new Vector3(0.3f, 0.3f, side * 0.16f), new Vector3(0.11f, 0.2f, 0.05f), ink, 0f, Quaternion.Euler(side * 18f, 0f, -24f));
                Part(PrimitiveType.Sphere, "Paw", dog.transform, new Vector3(0.24f, 0.03f, side * 0.1f), new Vector3(0.12f, 0.07f, 0.09f), white, 0.15f);
            }
            Part(PrimitiveType.Sphere, "Tail", dog.transform, new Vector3(-0.34f, 0.22f, 0f), new Vector3(0.17f, 0.07f, 0.07f), white, 0.15f, Quaternion.Euler(0f, 0f, 25f));
            Anchor(dog.transform, SleeperName, new Vector3(0.3f, 0.75f, 0f));
            // An empty spot on the dog is the one to talk to, so the sleeping dog never turns to face anyone.
            GameObject spot = new GameObject("Dog Talk Spot");
            spot.transform.SetParent(dog.transform, false);
            Talker(spot, "secret_dog", "Dog", "secret_dog");
        }

        private static Texture2D missingTexture;

        // The checkerboard a game shows where a texture is missing.
        private static Texture2D MissingTexture()
        {
            if (missingTexture != null) return missingTexture;
            missingTexture = new Texture2D(8, 8, TextureFormat.RGBA32, false) { name = "Missing Texture", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.DontSave };
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    missingTexture.SetPixel(x, y, ((x / 2) + (y / 2)) % 2 == 0 ? new Color(1f, 0f, 0.86f) : new Color(0.02f, 0.02f, 0.02f));
            missingTexture.Apply();
            return missingTexture;
        }

        private static void BuildCloset(Transform root, SecretRoomArt art)
        {
            Room room = Rooms[ClosetId];
            Shell(root, room, new Color(0.34f, 0.25f, 0.18f), new Color(0.42f, 0.4f, 0.36f), 1.5f);
            Model(root, art != null ? art.bookcase : null, "Shelves", new Vector2Int(0, 3), 90f, 1f, new Vector2Int(1, 2));
            Model(root, art != null ? art.books : null, "Cleaning Supplies", new Vector2Int(0, 3), 90f, 0.4f, Vector2Int.zero, 0.9f);
            Model(root, art != null ? art.bookcase : null, "Shelves", new Vector2Int(5, 3), 270f, 1f, new Vector2Int(1, 2));
            // A bucket and a mop leaning against the back wall.
            Part(PrimitiveType.Cylinder, "Bucket", root, new Vector3(4.2f, 0.2f, 4.3f), new Vector3(0.4f, 0.2f, 0.4f), new Color(0.3f, 0.5f, 0.85f), 0.1f);
            Part(PrimitiveType.Cylinder, "Mop Handle", root, new Vector3(3.6f, 0.7f, 4.35f), new Vector3(0.05f, 0.7f, 0.05f), new Color(0.55f, 0.4f, 0.25f), 0f, Quaternion.Euler(-12f, 0f, 8f));
            Part(PrimitiveType.Cube, "Mop Head", root, new Vector3(3.52f, 0.06f, 4.25f), new Vector3(0.32f, 0.12f, 0.2f), new Color(0.85f, 0.85f, 0.8f), 0.05f);
            Blocker(root, new Vector2Int(3, 4), new Vector2Int(2, 1));
            Glow(root, "Bare Bulb", new Vector3(2.5f, 1.45f, 2.5f), new Color(1f, 0.85f, 0.55f), 4f, 1.2f);
            Person(root, art, "secret_janitor", "Janitor", "secret_janitor", new Vector2Int(2, 3), 180f);
        }

        // A long, dark corridor at the end of the white dog's trail, the kind of place nobody has walked down in years. A
        // worn runner leads from the door to the far end, under pendant lamps that leave pools of light with dark between
        // (the first is dead, the second failing). Empty frames line the walls, an armchair sits facing the far wall, and
        // a dog's lead hangs alone on the coat rack by the door. At the very end a man stands facing the door, beside a
        // small stand with an egg on it, under one cold lamp, with a photograph too faded to see behind him.
        // SecretRoomMood darkens it, hums, ticks, flickers and, once he has spoken, takes him away.
        private static void BuildForgottenRoom(Transform root, SecretRoomArt art)
        {
            Room room = Rooms[ForgottenRoomId];
            const float wallHeight = 2.6f;
            Shell(root, room, new Color(0.11f, 0.095f, 0.085f), new Color(0.2f, 0.2f, 0.235f), wallHeight, 0.02f, 0.03f);
            float middleX = (room.Width - 1) * 0.5f, middleZ = (room.Height - 1) * 0.5f;
            float west = -0.5f, east = room.Width - 0.5f, north = room.Height - 0.5f;

            // Dark panelling to the height of a rail, all the way round.
            Color panel = new Color(0.12f, 0.11f, 0.14f), rail = new Color(0.27f, 0.23f, 0.19f);
            Part(PrimitiveType.Cube, "Wainscot", root, new Vector3(west + 0.03f, 0.45f, middleZ), new Vector3(0.06f, 0.9f, room.Height), panel, 0.02f);
            Part(PrimitiveType.Cube, "Wainscot", root, new Vector3(east - 0.03f, 0.45f, middleZ), new Vector3(0.06f, 0.9f, room.Height), panel, 0.02f);
            Part(PrimitiveType.Cube, "Wainscot", root, new Vector3(middleX, 0.45f, north - 0.03f), new Vector3(room.Width, 0.9f, 0.06f), panel, 0.02f);
            Part(PrimitiveType.Cube, "Rail", root, new Vector3(west + 0.05f, 0.92f, middleZ), new Vector3(0.1f, 0.05f, room.Height), rail, 0.02f);
            Part(PrimitiveType.Cube, "Rail", root, new Vector3(east - 0.05f, 0.92f, middleZ), new Vector3(0.1f, 0.05f, room.Height), rail, 0.02f);
            Part(PrimitiveType.Cube, "Rail", root, new Vector3(middleX, 0.92f, north - 0.05f), new Vector3(room.Width, 0.05f, 0.1f), rail, 0.02f);

            // The worn runner from the door to the man, with a faded border.
            const float runnerFrom = 1.3f, runnerTo = 12.6f;
            float runnerMiddle = (runnerFrom + runnerTo) * 0.5f, runnerLength = runnerTo - runnerFrom;
            Part(PrimitiveType.Cube, "Runner", root, new Vector3(middleX, 0.004f, runnerMiddle), new Vector3(1.3f, 0.008f, runnerLength), new Color(0.23f, 0.08f, 0.09f), 0.02f);
            foreach (float side in new[] { -1f, 1f })
                Part(PrimitiveType.Cube, "Runner Border", root, new Vector3(middleX + side * 0.58f, 0.006f, runnerMiddle), new Vector3(0.06f, 0.01f, runnerLength), new Color(0.34f, 0.27f, 0.15f), 0.02f);

            // Pendant lamps down the middle: dead, failing, dim, and the cold one over the man.
            (float z, Color colour, float intensity, float range)[] pendants =
            {
                (3.5f, new Color(1f, 0.8f, 0.55f), 0f, 3.4f),
                (6.5f, new Color(1f, 0.78f, 0.5f), 0.95f, 3.4f),
                (9.5f, new Color(1f, 0.8f, 0.55f), 0.6f, 3.2f),
                (12.4f, new Color(0.75f, 0.82f, 1f), 1.35f, 4.6f),
            };
            for (int i = 0; i < pendants.Length; i++) Pendant(root, i, new Vector3(middleX, 1.95f, pendants[i].z), wallHeight, pendants[i].colour, pendants[i].intensity, pendants[i].range);
            Glow(root, DoorwayLightName, new Vector3(middleX, 1.2f, 1.2f), new Color(0.75f, 0.8f, 0.95f), 2.5f, 0.45f);

            // Empty frames down both walls, each with the pale patch of a picture long gone; two hang crooked.
            (float z, bool onWest, float tilt)[] frames = { (2.6f, false, 0f), (4.6f, true, 0f), (7.4f, false, 7f), (8.6f, true, 0f), (10.4f, false, 0f), (11.2f, true, -6f) };
            foreach ((float z, bool onWest, float tilt) in frames)
            {
                float x = onWest ? west + 0.03f : east - 0.03f;
                float inward = onWest ? 1f : -1f;
                Quaternion hang = Quaternion.Euler(tilt, 0f, 0f);
                Part(PrimitiveType.Cube, "Frame", root, new Vector3(x + inward * 0.02f, 1.6f, z), new Vector3(0.04f, 0.46f, 0.6f), new Color(0.36f, 0.28f, 0.2f), 0f, hang);
                Part(PrimitiveType.Cube, "Faded Patch", root, new Vector3(x + inward * 0.045f, 1.6f, z), new Vector3(0.01f, 0.34f, 0.48f), new Color(0.46f, 0.46f, 0.5f), 0.04f, hang);
                Look(root, new Vector2Int(onWest ? 0 : room.Width - 1, Mathf.RoundToInt(z)), "inspect_forgotten_frames", false, new Vector3(onWest ? -0.35f : 0.35f, 0f, z - Mathf.RoundToInt(z)));
            }

            // By the door, a coat rack with nothing on it but a small dog's lead.
            Vector2Int rack = new Vector2Int(0, 2);
            GameObject coatRack = Model(root, art != null ? art.coatRack : null, "Coat Rack", rack, 90f, 0.5f, Vector2Int.one);
            float rackTop = coatRack != null ? MeshBounds(coatRack).max.y : 1.7f;
            if (coatRack == null)
            {
                Part(PrimitiveType.Cylinder, "Coat Rack", root, new Vector3(rack.x, 0.85f, rack.y), new Vector3(0.05f, 0.85f, 0.05f), new Color(0.3f, 0.22f, 0.15f), 0f);
                Blocker(root, rack, Vector2Int.one);
            }
            Color lead = new Color(0.62f, 0.14f, 0.14f);
            Part(PrimitiveType.Cylinder, "Dog Lead", root, new Vector3(rack.x + 0.12f, rackTop - 0.45f, rack.y), new Vector3(0.018f, 0.3f, 0.018f), lead, 0.05f, Quaternion.Euler(0f, 0f, 6f));
            Part(PrimitiveType.Cylinder, "Lead Loop", root, new Vector3(rack.x + 0.14f, rackTop - 0.12f, rack.y), new Vector3(0.1f, 0.01f, 0.1f), lead, 0.05f, Quaternion.Euler(90f, 0f, 0f));
            Part(PrimitiveType.Cylinder, "Small Collar", root, new Vector3(rack.x + 0.09f, rackTop - 0.8f, rack.y), new Vector3(0.12f, 0.015f, 0.12f), lead, 0.05f, Quaternion.Euler(80f, 0f, 0f));
            Look(root, rack, "inspect_forgotten_coat_rack", false);

            // An armchair halfway down, turned to face the far wall.
            Vector2Int chair = new Vector2Int(room.Width - 1, 7);
            if (Model(root, art != null ? art.armchair : null, "Armchair", chair, 0f, 0.85f, Vector2Int.one) == null)
            {
                Part(PrimitiveType.Cube, "Armchair", root, new Vector3(chair.x, 0.3f, chair.y), new Vector3(0.7f, 0.6f, 0.7f), new Color(0.3f, 0.2f, 0.2f), 0f);
                Blocker(root, chair, Vector2Int.one);
            }
            Look(root, chair, "inspect_forgotten_chair", false);

            // An old man: white hair and beard, spectacles and a faded coat (DevChibis.OldMan), leaning on a stick. The
            // baked prefab is used as it is; without it, a model is recoloured here and dressed when the room is placed.
            // He and his stick share one parent, so he can leave with it.
            Vector2Int man = new Vector2Int(2, room.Height - 2);
            GameObject group = new GameObject(ForgottenManGroupName);
            group.transform.SetParent(root, false);
            GameObject oldMan = null;
            if (art != null && art.forgottenMan != null)
            {
                oldMan = Instantiate(art.forgottenMan, group.transform);
                oldMan.name = DevChibis.OldMan.Name;
            }
            if (oldMan == null) oldMan = DevChibis.Create(art != null ? art.devGlasses : null, DevChibis.OldMan, group.transform);
            if (oldMan == null)
                oldMan = Part(PrimitiveType.Capsule, DevChibis.OldMan.Name, group.transform, new Vector3(0f, 0.8f, 0f), new Vector3(0.6f, 0.8f, 0.6f), DevChibis.OldMan.Outfit, 0.05f);
            oldMan.transform.localPosition = new Vector3(man.x, oldMan.transform.localPosition.y, man.y);
            oldMan.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            Talker(oldMan, DevChibis.OldMan.Id, DevChibis.OldMan.Name, DevChibis.OldMan.DialogueId);
            Blocker(root, man, Vector2Int.one);
            Part(PrimitiveType.Cylinder, "Walking Stick", group.transform, new Vector3(man.x - 0.42f, 0.5f, man.y - 0.12f), new Vector3(0.05f, 0.5f, 0.05f),
                new Color(0.4f, 0.28f, 0.18f), 0f, Quaternion.Euler(0f, 0f, -8f));
            Part(PrimitiveType.Sphere, "Stick Handle", group.transform, new Vector3(man.x - 0.49f, 1.0f, man.y - 0.12f), new Vector3(0.1f, 0.08f, 0.1f),
                new Color(0.4f, 0.28f, 0.18f), 0f);

            // The egg on its stand, beside him. If he goes without giving it away, it can still be taken.
            Vector2Int stand = new Vector2Int(man.x + 1, man.y);
            Part(PrimitiveType.Cylinder, "Egg Stand", root, new Vector3(stand.x, 0.35f, stand.y), new Vector3(0.35f, 0.35f, 0.35f), new Color(0.3f, 0.28f, 0.3f), 0.02f);
            Part(PrimitiveType.Sphere, EggName, root, new Vector3(stand.x, 0.82f, stand.y), new Vector3(0.2f, 0.26f, 0.2f), new Color(0.96f, 0.93f, 0.86f), 0.25f);
            Blocker(root, stand, Vector2Int.one);
            Look(root, stand, "secret_forgotten_egg", false, Vector3.zero, EggLookName).enabled = false;

            // Behind him, a photograph too faded to make out (Lost Echoes brings it back for a moment).
            Part(PrimitiveType.Cube, "Photograph Frame", root, new Vector3(1.1f, 1.8f, north - 0.04f), new Vector3(0.78f, 0.6f, 0.05f), new Color(0.42f, 0.33f, 0.24f), 0f);
            Part(PrimitiveType.Cube, PhotoName, root, new Vector3(1.1f, 1.8f, north - 0.07f), new Vector3(0.62f, 0.46f, 0.02f), new Color(0.58f, 0.58f, 0.62f), 0.04f);
        }

        // A lamp hung from the ceiling on a cord: SecretRoomMood dims and flickers its light and its bulb.
        private static void Pendant(Transform root, int index, Vector3 position, float ceiling, Color colour, float intensity, float range)
        {
            GameObject pendant = new GameObject(PendantPrefix + index);
            pendant.transform.SetParent(root, false);
            pendant.transform.localPosition = position;
            float cord = ceiling - position.y;
            Part(PrimitiveType.Cylinder, "Cord", pendant.transform, new Vector3(0f, cord * 0.5f, 0f), new Vector3(0.015f, cord * 0.5f, 0.015f), new Color(0.05f, 0.05f, 0.05f), 0f);
            Part(PrimitiveType.Cylinder, "Shade", pendant.transform, new Vector3(0f, 0.05f, 0f), new Vector3(0.4f, 0.07f, 0.4f), new Color(0.16f, 0.15f, 0.13f), 0.02f);
            Part(PrimitiveType.Sphere, "Bulb", pendant.transform, new Vector3(0f, -0.04f, 0f), Vector3.one * 0.11f,
                intensity > 0f ? colour : new Color(0.3f, 0.3f, 0.3f), intensity > 0f ? 2.2f : 0f);
            Light light = pendant.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = colour;
            light.range = range;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
        }

        // Something to look at on a cell: blocks it (when asked) and plays a narrated dialogue (RoomInspectable).
        private static RoomInspectable Look(Transform root, Vector2Int cell, string dialogueId, bool block, Vector3 offset = default, string name = null)
        {
            GameObject spot = new GameObject(name ?? "Look " + dialogueId);
            spot.transform.SetParent(root, false);
            spot.transform.localPosition = new Vector3(cell.x, 0f, cell.y) + offset;
            RoomInspectable look = spot.AddComponent<RoomInspectable>();
            look.Configure(dialogueId);
            if (block) Blocker(root, cell, Vector2Int.one);
            return look;
        }

        // ---- Parts ----

        private static void Shell(Transform root, Room room, Color floor, Color wall, float wallHeight, float floorEmission = 0.05f, float wallEmission = 0.08f)
        {
            Vector3 centre = new Vector3((room.Width - 1) * 0.5f, 0f, (room.Height - 1) * 0.5f);
            Part(PrimitiveType.Cube, "Floor", root, centre + new Vector3(0f, -0.05f, 0f), new Vector3(room.Width, 0.1f, room.Height), floor, floorEmission);
            const float thickness = 0.3f;
            Part(PrimitiveType.Cube, "Wall North", root, new Vector3(centre.x, wallHeight * 0.5f, room.Height - 0.5f + thickness * 0.5f), new Vector3(room.Width + thickness * 2f, wallHeight, thickness), wall, wallEmission);
            Part(PrimitiveType.Cube, "Wall West", root, new Vector3(-0.5f - thickness * 0.5f, wallHeight * 0.5f, centre.z), new Vector3(thickness, wallHeight, room.Height), wall, wallEmission);
            Part(PrimitiveType.Cube, "Wall East", root, new Vector3(room.Width - 0.5f + thickness * 0.5f, wallHeight * 0.5f, centre.z), new Vector3(thickness, wallHeight, room.Height), wall, wallEmission);
            // The camera looks in from the south, so that wall stays low.
            Part(PrimitiveType.Cube, "Wall South", root, new Vector3(centre.x, 0.15f, -0.5f - thickness * 0.5f), new Vector3(room.Width + thickness * 2f, 0.3f, thickness), wall, wallEmission);
        }

        private static void Ring(Transform root, Vector2Int cell)
        {
            GameObject ring = Part(PrimitiveType.Cylinder, ExitName, root, new Vector3(cell.x, 0.08f, cell.y), new Vector3(0.82f, 0.06f, 0.82f), new Color(0.36f, 0.82f, 1f), 2.2f);
            ring.GetComponent<Renderer>().sharedMaterial.SetColor("_EmissionColor", new Color(0.12f, 0.62f, 1f) * 2.2f);
        }

        private static void Person(Transform root, SecretRoomArt art, string id, string displayName, string dialogueId, Vector2Int cell, float facing)
        {
            GameObject person;
            if (art != null && art.person != null)
            {
                person = Instantiate(art.person, root);
                person.name = displayName;
                person.transform.localPosition = new Vector3(cell.x, 0f, cell.y);
            }
            else
            {
                person = Part(PrimitiveType.Capsule, displayName, root, new Vector3(cell.x, 0.8f, cell.y), new Vector3(0.6f, 0.8f, 0.6f), new Color(0.6f, 0.5f, 0.8f), 0.1f);
            }
            person.transform.localRotation = Quaternion.Euler(0f, facing, 0f);
            Talker(person, id, displayName, dialogueId);
            Blocker(root, cell, Vector2Int.one);
        }

        private static void Talker(GameObject target, string id, string displayName, string dialogueId)
        {
            DynamicNpc npc = target.AddComponent<DynamicNpc>();
            npc.Configure(new NpcDefinition { id = id, displayName = displayName, dialogueIds = new[] { dialogueId } }, dialogueId);
        }

        /// <summary>A Kenney model scaled so its widest side is <paramref name="width"/> cells, standing on <paramref name="cell"/>.</summary>
        private static GameObject Model(Transform root, GameObject prefab, string name, Vector2Int cell, float facing, float width, Vector2Int blocks,
            float height = 0f, float forward = 0f)
        {
            if (prefab == null) return null;
            GameObject model = Instantiate(prefab, root);
            model.name = name;
            model.transform.localRotation = Quaternion.Euler(0f, facing, 0f);
            model.transform.localScale = Vector3.one;
            Bounds bounds = MeshBounds(model);
            float widest = Mathf.Max(bounds.size.x, bounds.size.z);
            float scale = widest > 0.0001f ? width / widest : 1f;
            model.transform.localScale = Vector3.one * scale;
            bounds = MeshBounds(model);
            // Centre the footprint on the cell (or across a multi-cell footprint) and stand it on the floor, or on a desk.
            Vector3 footprintCentre = new Vector3(cell.x + Mathf.Max(0, blocks.x - 1) * 0.5f, 0f, cell.y + Mathf.Max(0, blocks.y - 1) * 0.5f);
            Vector3 offset = footprintCentre - new Vector3(bounds.center.x, 0f, bounds.center.z);
            offset.y = height - bounds.min.y;
            model.transform.localPosition += offset + model.transform.localRotation * Vector3.forward * forward;
            foreach (Collider collider in model.GetComponentsInChildren<Collider>(true)) DestroyImmediate(collider);
            if (blocks.x > 0 && blocks.y > 0) Blocker(root, cell, blocks);
            return model;
        }

        // Mesh bounds in the room's space, read from the meshes so they work on the inactive template.
        private static Bounds MeshBounds(GameObject model)
        {
            Transform root = model.transform.parent;
            bool any = false;
            Bounds result = new Bounds(model.transform.localPosition, Vector3.zero);
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                Bounds mesh = filter.sharedMesh.bounds;
                Matrix4x4 toRoot = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = mesh.center + Vector3.Scale(mesh.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 point = toRoot.MultiplyPoint3x4(corner);
                    if (!any) { result = new Bounds(point, Vector3.zero); any = true; }
                    else result.Encapsulate(point);
                }
            }
            return result;
        }

        private static void Blocker(Transform root, Vector2Int cell, Vector2Int size)
        {
            GameObject blocker = new GameObject("Blocker " + cell.x + "," + cell.y);
            blocker.transform.SetParent(root, false);
            blocker.transform.localPosition = new Vector3(cell.x, 0f, cell.y);
            // The grid is found when the room is placed; until then the template has none.
            blocker.AddComponent<GridObstacle>().Configure(null, size);
        }

        private static void Anchor(Transform parent, string name, Vector3 localPosition)
        {
            GameObject anchor = new GameObject(name);
            anchor.transform.SetParent(parent, false);
            anchor.transform.localPosition = localPosition;
        }

        private static void Glow(Transform root, string name, Vector3 localPosition, Color colour, float range, float intensity)
        {
            GameObject glow = new GameObject(name);
            glow.transform.SetParent(root, false);
            glow.transform.localPosition = localPosition;
            Light light = glow.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = colour;
            light.range = range;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
        }

        internal static GameObject Part(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 localScale, Color colour,
            float emission, Quaternion? rotation = null)
        {
            GameObject part = RuntimePrimitive.Create(type);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            if (rotation.HasValue) part.transform.localRotation = rotation.Value;
            // Immediate, because the template is instantiated in the same frame it is built.
            Collider collider = part.GetComponent<Collider>();
            if (collider != null) DestroyImmediate(collider);
            Renderer renderer = part.GetComponent<Renderer>();
            // In play each part gets its own copy of the material; in the editor (baking a prefab) the copy is made
            // explicitly, since renderer.material there would leak an untracked copy into the scene.
            Material material = Application.isPlaying ? renderer.material : (renderer.sharedMaterial = new Material(renderer.sharedMaterial));
            material.color = colour;
            material.SetColor("_EmissionColor", colour * emission);
            material.EnableKeyword("_EMISSION");
            // Matte, so hair and felt read as soft rather than glossy plastic.
            material.SetFloat("_Smoothness", 0.12f);
            return part;
        }
    }
}
