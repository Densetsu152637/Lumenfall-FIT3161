using System;
using System.Collections.Generic;
using UnityEngine;
using Lumenfall.Controls;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Inventory;
using Lumenfall.Spells;

namespace Lumenfall.Quests
{
    /// <summary>
    /// Orio's Patch Notes dais: three rune discs turned through four floor sockets. Casting Rotara at a socket turns
    /// every disc it touches by one clockwise quarter turn, so the discs have to be solved together. The dais stands in
    /// the deep forest, reached through the west forest portal, and is registered in code like Stella's relic board.
    /// World state, read live in the deep forest or from its archive when reporting to Orio in the west forest, decides
    /// completion; the legacy flag never does.
    /// </summary>
    public sealed class OrioRuneDiscPuzzle : MonoBehaviour
    {
        public const string PuzzleMapId = "deepForest";
        // Inside the deep forest's walls (x 1-11, y 4-15), east of the entry portal at (1,10) and the spawn at (3,10).
        public static readonly Vector2Int DaisCentre = new Vector2Int(7, 11);
        // The centre socket keeps the original mechanism id, which Orio's completion condition names.
        public const string AnchorId = "mechanism.orio.rotate";
        public const string AlignmentCondition = "OrioMechanismAligned";
        public const string DiscTag = "rotara-disc";
        public const int TargetRotation = 0;
        private const string RotaraTag = "rotara";
        private const string QuestId = "orio_patch_notes";
        private const string AlignedFlag = "orio.patch_notes.mechanism_aligned";
        private const int InteractionRange = 1;
        private const int StepDegrees = 90;
        private const float TurnSpeed = 240f;
        private const float FlashSeconds = 0.4f;
        private const float DiscRadius = 0.95f;
        private const float SocketRadius = 0.22f;

        public static readonly string[] DiscIds = { "mechanism.orio.disc.northwest", "mechanism.orio.disc.northeast", "mechanism.orio.disc.south" };
        // Chosen so the intended solution is north 3, centre 2, west 2, east 1 presses; other press orders also solve it.
        public static readonly int[] DiscStartRotations = { 90, 180, 270 };
        public static readonly string[] SocketIds = { "mechanism.orio.socket.north", "mechanism.orio.socket.west", "mechanism.orio.socket.east", AnchorId };
        private static readonly Vector2Int[] DiscOffsets = { new Vector2Int(-2, 1), new Vector2Int(2, 1), new Vector2Int(0, -2) };
        private static readonly Vector2Int[] SocketOffsets = { new Vector2Int(0, 1), new Vector2Int(-2, -1), new Vector2Int(2, -1), Vector2Int.zero };
        private static readonly int[][] SocketLinks = { new[] { 0, 1 }, new[] { 0, 2 }, new[] { 1, 2 }, new[] { 0, 1, 2 } };

        // A forest shrine: a moss floor in a ring of old stone, lichen-covered rune discs whose carved runes glow like
        // fireflies, tree-stump sockets topped with firefly orbs, and vines tracing which discs each socket turns.
        private static readonly Color DaisColor = new Color(0.2f, 0.3f, 0.17f);
        private static readonly Color DaisRimColor = new Color(0.36f, 0.38f, 0.31f);
        private static readonly Color DaisSolvedColor = new Color(0.24f, 0.42f, 0.22f);
        private static readonly Color DiscBaseColor = new Color(0.42f, 0.43f, 0.37f);
        private static readonly Color DiscFaceColor = new Color(0.53f, 0.55f, 0.45f);
        private static readonly Color LichenColor = new Color(0.47f, 0.56f, 0.3f);
        private static readonly Color RuneColor = new Color(1f, 0.83f, 0.42f);
        private static readonly Color NotchColor = new Color(0.2f, 0.62f, 0.55f);
        private static readonly Color SolvedColor = new Color(0.55f, 1f, 0.55f);
        private static readonly Color StumpColor = new Color(0.38f, 0.26f, 0.15f);
        private static readonly Color StumpTopColor = new Color(0.62f, 0.47f, 0.3f);
        private static readonly Color SocketColor = new Color(0.72f, 0.6f, 0.28f);
        private static readonly Color SocketReadyColor = new Color(1f, 0.86f, 0.42f);
        private static readonly Color LinkColor = new Color(0.27f, 0.45f, 0.2f);
        private static readonly Color FlashColor = new Color(0.86f, 1f, 0.78f);
        private static readonly Color StoneColor = new Color(0.4f, 0.42f, 0.36f);
        private static readonly Color MossColor = new Color(0.28f, 0.45f, 0.2f);
        private static readonly Color MushroomStemColor = new Color(0.9f, 0.86f, 0.74f);
        private static readonly Color MushroomCapColor = new Color(0.78f, 0.2f, 0.16f);
        private static readonly Color FireflyColor = new Color(1f, 0.92f, 0.55f);
        private const float SoftGlow = 0.18f;
        private const float BrightGlow = 1.4f;
        private const int FireflyCount = 9;

        private static OrioRuneDiscPuzzle instance;
        private readonly GameObject[] discHolders = new GameObject[3];
        private readonly Transform[] discBases = new Transform[3];
        private readonly Transform[] discFaces = new Transform[3];
        private readonly List<Renderer>[] runeRenderers = new List<Renderer>[3];
        private readonly Renderer[] notchRenderers = new Renderer[3];
        private readonly float[] displayedAngles = new float[3];
        private readonly GameObject[] socketHolders = new GameObject[4];
        private readonly Renderer[] gemRenderers = new Renderer[4];
        private readonly List<Renderer>[] linkRenderers = new List<Renderer>[4];
        private readonly float[] lastSocketRotations = new float[4];
        private readonly float[] flashUntil = new float[4];
        private readonly Dictionary<Renderer, Color> appliedColors = new Dictionary<Renderer, Color>();
        private readonly Dictionary<Renderer, float> glowStrengths = new Dictionary<Renderer, float>();
        private readonly Transform[] fireflies = new Transform[FireflyCount];
        private readonly Vector3[] fireflyHomes = new Vector3[FireflyCount];
        private Transform presentationRoot;
        private Renderer daisRenderer;
        private GridMover player;
        private bool hasDisplayedAngles;
        private int highlightedSocket = -1;

        /// <summary>Socket links apply only in the deep forest, so the same ids elsewhere never turn the discs.</summary>
        public static bool TryGetLinkedDiscs(string mapId, string socketId, out string[] discIds)
        {
            discIds = null;
            int index = Array.IndexOf(SocketIds, socketId);
            if (index < 0 || !string.Equals(mapId, PuzzleMapId, StringComparison.OrdinalIgnoreCase)) return false;
            discIds = Array.ConvertAll(SocketLinks[index], disc => DiscIds[disc]);
            return true;
        }

        /// <summary>
        /// Registers the discs and sockets around the centre cell. Call between BeginMapRebuild and EndMapRebuild: discs
        /// start at their authored angles, then saved or archived angles are re-applied. Holders may be null when only
        /// world state is needed, as in tests.
        /// </summary>
        public static void RegisterPuzzleObjects(WorldStateService state, Vector2Int anchorCell, IList<GameObject> discs = null, IList<GameObject> sockets = null)
        {
            if (state == null) return;
            for (int i = 0; i < DiscIds.Length; i++)
            {
                // Discs can't be targeted directly; only sockets accept spells, or one disc could be turned on its own.
                state.RegisterObject(DiscIds[i], discs?[i], anchorCell + DiscOffsets[i], Vector2Int.one, false, new[] { DiscTag });
                int start = DiscStartRotations[i];
                state.SetProperty(DiscIds[i], item => item.rotation = start);
            }
            for (int i = 0; i < SocketIds.Length; i++)
                state.RegisterObject(SocketIds[i], sockets?[i], anchorCell + SocketOffsets[i], Vector2Int.one, true, new[] { RotaraTag });
        }

        public static bool RotationMatches(float angle, int required) =>
            !float.IsNaN(angle) && !float.IsInfinity(angle) &&
            Math.Abs(NormalizeDegrees(angle) - NormalizeDegrees(required)) < 0.001f;

        /// <summary>One clockwise Rotara quarter turn, snapped to the 90-degree grid so saved angles never drift.</summary>
        public static float NextQuarterTurn(float angle) =>
            NormalizeDegrees((float)Math.Round(NormalizeDegrees(angle) / StepDegrees, MidpointRounding.ToEven) * StepDegrees + StepDegrees);

        private static float NormalizeDegrees(float angle) { angle %= 360f; return angle < 0f ? angle + 360f : angle; }

        public static bool AreDiscsAligned(string mapId, IList<WorldObjectSnapshot> discs)
        {
            if (!string.Equals(mapId, PuzzleMapId, StringComparison.OrdinalIgnoreCase) || discs == null || discs.Count != DiscIds.Length) return false;
            for (int i = 0; i < DiscIds.Length; i++)
            {
                WorldObjectSnapshot disc = discs[i];
                if (disc == null || !string.Equals(disc.id, DiscIds[i], StringComparison.Ordinal) ||
                    !string.Equals(disc.mapId, PuzzleMapId, StringComparison.OrdinalIgnoreCase) || !HasTag(disc.tags, DiscTag) ||
                    !RotationMatches(disc.rotation, TargetRotation)) return false;
            }
            return true;
        }

        /// <summary>Reads the deep forest live while the player is in it, or its archived state when reporting to Orio in the west forest.</summary>
        public static bool IsQuestReady(WorldStateService state, GameEventRuntime runtime)
        {
            if (runtime == null || runtime.GetQuestStatus(QuestId) != QuestStatus.Active || !runtime.GetFlag("spell.unlocked.Rotara") ||
                state == null || !state.IsReady || !state.TryGetObjectInMap(PuzzleMapId, AnchorId, out _)) return false;
            WorldObjectSnapshot[] discs = new WorldObjectSnapshot[DiscIds.Length];
            for (int i = 0; i < DiscIds.Length; i++)
                if (!state.TryGetObjectInMap(PuzzleMapId, DiscIds[i], out discs[i])) return false;
            return AreDiscsAligned(PuzzleMapId, discs);
        }

        /// <summary>Registers a stateless resolver; it retains no dais, map instance or old runtime.</summary>
        public static void RegisterQuestConditions(GameEventRuntime runtime)
        {
            runtime?.RegisterConditionHandler(AlignmentCondition, (condition, current) =>
                string.Equals(condition.key, AnchorId, StringComparison.Ordinal) && IsQuestReady(WorldStateService.Instance, current));
        }

        /// <summary>Called by the area loader before EndMapRebuild so saved disc angles are applied.</summary>
        public static void RegisterForMap(string mapId, GridWorld world, GameObject areaRoot)
        {
            RegisterQuestConditions(GameEventManager.Instance?.Runtime);
            WorldStateService state = WorldStateService.Instance;
            if (!string.Equals(mapId, PuzzleMapId, StringComparison.OrdinalIgnoreCase) || world == null || state == null)
            {
                if (instance != null) instance.SetPresentationVisible(false);
                return;
            }
            if (instance == null)
            {
                GameObject host = new GameObject("[Orio Rune Disc Puzzle]");
                instance = host.AddComponent<OrioRuneDiscPuzzle>();
                DontDestroyOnLoad(host);
            }
            instance.Build(world, DaisCentre);
            instance.SetPresentationVisible(true);
            RegisterPuzzleObjects(state, DaisCentre, instance.discHolders, instance.socketHolders);
            instance.hasDisplayedAngles = false;
            for (int i = 0; i < instance.lastSocketRotations.Length; i++) instance.lastSocketRotations[i] = float.NaN;
        }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        private void Update()
        {
            if (!PuzzleMapActive(out WorldStateService state)) { highlightedSocket = -1; return; }
            highlightedSocket = NearestSocketInRange(state);
            GameEventRuntime runtime = GameEventManager.Instance?.Runtime;
            if (Time.timeScale <= 0f || InventoryUiState.IsOpen || runtime == null || runtime.ActiveDialogue != null ||
                highlightedSocket < 0 || !KeyBindings.WasPressed(GameAction.Interact)) return;
            // The dais stays inert until Orio explains it, and acceptance alone never substitutes for the Rotara lesson.
            if (runtime.GetQuestStatus(QuestId) == QuestStatus.NotStarted || AnyLinkedDiscTurning(state, highlightedSocket)) return;
            SpellCaster caster = player != null ? player.GetComponent<SpellCaster>() : null;
            if (caster != null && caster.IsSpellUnlocked(SpellId.Rotara)) caster.CastSelectedAt(SocketIds[highlightedSocket]);
        }

        private void LateUpdate()
        {
            if (!PuzzleMapActive(out WorldStateService state)) return;
            bool allSolved = true;
            for (int i = 0; i < DiscIds.Length; i++)
            {
                if (discFaces[i] == null || !state.TryGetObject(DiscIds[i], out WorldObjectSnapshot disc)) { allSolved = false; continue; }
                // SyncTransform snaps the holder to the stored angle; keep the base fixed and ease the rune face clockwise.
                discBases[i].rotation = Quaternion.identity;
                if (!hasDisplayedAngles) displayedAngles[i] = disc.rotation;
                else
                {
                    float clockwise = Mathf.Repeat(disc.rotation - displayedAngles[i], 360f);
                    if (clockwise > 315f) displayedAngles[i] = Mathf.MoveTowardsAngle(displayedAngles[i], disc.rotation, TurnSpeed * Time.deltaTime);
                    else displayedAngles[i] = Mathf.Repeat(displayedAngles[i] + Mathf.Min(clockwise, TurnSpeed * Time.deltaTime), 360f);
                }
                discFaces[i].rotation = Quaternion.Euler(0f, displayedAngles[i], 0f);
                bool solved = RotationMatches(disc.rotation, TargetRotation) && !IsTurning(i, disc);
                allSolved &= solved;
                foreach (Renderer rune in runeRenderers[i]) SetColor(rune, solved ? SolvedColor : RuneColor);
                SetColor(notchRenderers[i], solved ? SolvedColor : NotchColor);
            }
            hasDisplayedAngles = true;
            SetColor(daisRenderer, allSolved ? DaisSolvedColor : DaisColor);
            DriftFireflies();

            for (int i = 0; i < SocketIds.Length; i++)
            {
                if (!state.TryGetObject(SocketIds[i], out WorldObjectSnapshot socket)) continue;
                // Each cast turns the socket too, so a changed angle means it was just pressed.
                if (!float.IsNaN(lastSocketRotations[i]) && !Mathf.Approximately(lastSocketRotations[i], socket.rotation)) flashUntil[i] = Time.unscaledTime + FlashSeconds;
                lastSocketRotations[i] = socket.rotation;
                bool flashing = Time.unscaledTime < flashUntil[i];
                SetColor(gemRenderers[i], flashing ? FlashColor : i == highlightedSocket ? SocketReadyColor : SocketColor);
                foreach (Renderer link in linkRenderers[i]) SetColor(link, flashing ? FlashColor : LinkColor);
            }

            GameEventRuntime runtime = GameEventManager.Instance?.Runtime;
            RegisterQuestConditions(runtime);
            // Preserve the legacy feedback flag, but never use it as completion authority.
            if (runtime != null && runtime.GetQuestStatus(QuestId) == QuestStatus.Active)
            {
                bool aligned = IsQuestReady(state, runtime);
                if (runtime.GetFlag(AlignedFlag) != aligned) runtime.SetFlag(AlignedFlag, aligned);
            }
        }

        private bool PuzzleMapActive(out WorldStateService state)
        {
            state = WorldStateService.Instance;
            return state != null && state.IsReady && presentationRoot != null && presentationRoot.gameObject.activeSelf &&
                string.Equals(state.CurrentMapId, PuzzleMapId, StringComparison.OrdinalIgnoreCase);
        }

        private int NearestSocketInRange(WorldStateService state)
        {
            // Resolve through the player input so an NPC mover can never be mistaken for the player.
            if (player == null)
            {
                GridPlayerInput input = FindAnyObjectByType<GridPlayerInput>();
                player = input != null ? input.GetComponent<GridMover>() : FindAnyObjectByType<GridMover>();
            }
            if (player == null) return -1;
            int nearest = -1;
            int best = InteractionRange + 1;
            for (int i = 0; i < SocketIds.Length; i++)
            {
                if (!state.TryGetObject(SocketIds[i], out WorldObjectSnapshot socket)) continue;
                int distance = Mathf.Abs(player.CurrentCell.x - socket.cell.x) + Mathf.Abs(player.CurrentCell.y - socket.cell.y);
                if (distance < best) { best = distance; nearest = i; }
            }
            return nearest;
        }

        private bool AnyLinkedDiscTurning(WorldStateService state, int socket)
        {
            foreach (int disc in SocketLinks[socket])
                if (state.TryGetObject(DiscIds[disc], out WorldObjectSnapshot snapshot) && IsTurning(disc, snapshot)) return true;
            return false;
        }

        private bool IsTurning(int disc, WorldObjectSnapshot snapshot) =>
            hasDisplayedAngles && Mathf.Abs(Mathf.DeltaAngle(displayedAngles[disc], snapshot.rotation)) > 0.5f;

        private void SetPresentationVisible(bool visible)
        {
            if (presentationRoot != null && presentationRoot.gameObject.activeSelf != visible) presentationRoot.gameObject.SetActive(visible);
        }

        private void Build(GridWorld world, Vector2Int centreCell)
        {
            if (presentationRoot != null) return;

            presentationRoot = new GameObject("Rune Dais").transform;
            presentationRoot.SetParent(transform, false);
            Vector3 centre = world.CellToWorld(centreCell, 0f);
            CreatePart(PrimitiveType.Cylinder, "Stone Ring", presentationRoot, centre + new Vector3(0f, 0.008f, 0f), new Vector3(7.3f, 0.005f, 7.3f), DaisRimColor);
            daisRenderer = CreatePart(PrimitiveType.Cylinder, "Moss Floor", presentationRoot, centre + new Vector3(0f, 0.014f, 0f), new Vector3(6.7f, 0.005f, 6.7f), DaisColor).GetComponent<Renderer>();
            BuildSurroundings(centre);

            Vector3[] discPositions = new Vector3[DiscIds.Length];
            for (int i = 0; i < DiscIds.Length; i++)
            {
                discPositions[i] = world.CellToWorld(centreCell + DiscOffsets[i], 0f);
                discHolders[i] = BuildDisc(i, discPositions[i]);
            }

            for (int i = 0; i < SocketIds.Length; i++)
            {
                Vector3 socketPosition = world.CellToWorld(centreCell + SocketOffsets[i], 0f);
                socketHolders[i] = new GameObject(SocketIds[i]);
                socketHolders[i].transform.SetParent(presentationRoot, false);
                socketHolders[i].transform.position = socketPosition;
                gemRenderers[i] = BuildSocketModel(socketHolders[i].transform);
                // Vines along the floor show which discs each socket turns, and flash when it is pressed.
                linkRenderers[i] = new List<Renderer>();
                foreach (int disc in SocketLinks[i])
                {
                    Vector3 direction = discPositions[disc] - socketPosition;
                    direction.y = 0f;
                    float length = direction.magnitude - DiscRadius - SocketRadius;
                    if (length <= 0.05f) continue;
                    Vector3 start = socketPosition + direction.normalized * SocketRadius;
                    GameObject link = CreatePart(PrimitiveType.Cube, "Vine " + SocketIds[i] + " -> " + DiscIds[disc], presentationRoot,
                        start + direction.normalized * (length * 0.5f) + new Vector3(0f, 0.025f, 0f), new Vector3(0.07f, 0.008f, length), LinkColor);
                    link.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
                    linkRenderers[i].Add(link.GetComponent<Renderer>());
                }
            }
        }

        /// <summary>
        /// Standing stones, moss and mushrooms around the ring, and fireflies over it, so the dais reads as part of the
        /// forest. A fixed seed keeps it the same every visit. All of it is decoration and never blocks a tile.
        /// </summary>
        private void BuildSurroundings(Vector3 centre)
        {
            System.Random random = new System.Random(7);
            float Next(float min, float max) => min + (float)random.NextDouble() * (max - min);
            Vector3 OnRing(float angle, float radius) => centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;

            const int StoneCount = 9;
            for (int i = 0; i < StoneCount; i++)
            {
                float angle = (i + Next(-0.2f, 0.2f)) / StoneCount * Mathf.PI * 2f;
                Vector3 at = OnRing(angle, Next(3.55f, 3.8f));
                float height = Next(0.22f, 0.42f);
                GameObject stone = CreatePart(PrimitiveType.Cube, "Standing Stone", presentationRoot, at + new Vector3(0f, height * 0.5f, 0f),
                    new Vector3(Next(0.18f, 0.26f), height, Next(0.14f, 0.2f)), StoneColor);
                stone.transform.rotation = Quaternion.Euler(Next(-8f, 8f), angle * -Mathf.Rad2Deg + Next(-20f, 20f), Next(-8f, 8f));
                CreatePart(PrimitiveType.Sphere, "Moss", presentationRoot, at + new Vector3(0f, height, 0f), new Vector3(0.2f, 0.07f, 0.17f), MossColor);
            }
            for (int i = 0; i < 14; i++)
                CreatePart(PrimitiveType.Sphere, "Moss Tuft", presentationRoot, OnRing(Next(0f, Mathf.PI * 2f), Next(3.3f, 3.75f)) + new Vector3(0f, 0.02f, 0f),
                    new Vector3(Next(0.25f, 0.45f), 0.06f, Next(0.2f, 0.4f)), MossColor);
            for (int i = 0; i < 5; i++)
            {
                Vector3 at = OnRing(Next(0f, Mathf.PI * 2f), Next(3.4f, 3.7f));
                float size = Next(0.7f, 1.1f);
                CreatePart(PrimitiveType.Cylinder, "Mushroom Stem", presentationRoot, at + new Vector3(0f, 0.05f * size, 0f), new Vector3(0.05f, 0.05f, 0.05f) * size, MushroomStemColor);
                CreatePart(PrimitiveType.Sphere, "Mushroom Cap", presentationRoot, at + new Vector3(0f, 0.1f * size, 0f), new Vector3(0.16f, 0.07f, 0.16f) * size, MushroomCapColor);
            }
            for (int i = 0; i < FireflyCount; i++)
            {
                fireflyHomes[i] = OnRing(Next(0f, Mathf.PI * 2f), Next(0.6f, 3.4f)) + new Vector3(0f, Next(0.35f, 0.9f), 0f);
                fireflies[i] = CreatePart(PrimitiveType.Sphere, "Firefly", presentationRoot, fireflyHomes[i], Vector3.one * 0.05f, FireflyColor, BrightGlow).transform;
            }
        }

        private void DriftFireflies()
        {
            float time = Time.time;
            for (int i = 0; i < FireflyCount; i++)
                if (fireflies[i] != null)
                    fireflies[i].position = fireflyHomes[i] + new Vector3(Mathf.Sin(time * 0.6f + i * 1.7f) * 0.3f,
                        Mathf.Sin(time * 1.3f + i) * 0.12f, Mathf.Cos(time * 0.5f + i * 2.3f) * 0.3f);
        }

        private GameObject BuildDisc(int index, Vector3 position)
        {
            GameObject holder = new GameObject(DiscIds[index]);
            holder.transform.SetParent(presentationRoot, false);
            holder.transform.position = position;

            discBases[index] = new GameObject("Base").transform;
            discBases[index].SetParent(holder.transform, false);
            CreatePart(PrimitiveType.Cylinder, "Stone", discBases[index], new Vector3(0f, 0.04f, 0f), new Vector3(DiscRadius * 2f, 0.02f, DiscRadius * 2f), DiscBaseColor);
            // Lichen sits on the fixed base, so it stays put while the rune face turns.
            CreatePart(PrimitiveType.Sphere, "Lichen", discBases[index], new Vector3(-0.62f, 0.06f, -0.48f), new Vector3(0.36f, 0.02f, 0.24f), LichenColor);
            CreatePart(PrimitiveType.Sphere, "Lichen", discBases[index], new Vector3(0.66f, 0.06f, -0.38f), new Vector3(0.22f, 0.02f, 0.3f), LichenColor);
            // The notch is fixed on the base at the target orientation (north); the rune must point at it.
            notchRenderers[index] = CreatePart(PrimitiveType.Cube, "Target Notch", discBases[index], new Vector3(0f, 0.065f, DiscRadius - 0.11f),
                new Vector3(0.14f, 0.03f, 0.16f), NotchColor, BrightGlow * 0.5f).GetComponent<Renderer>();

            discFaces[index] = new GameObject("Face").transform;
            discFaces[index].SetParent(holder.transform, false);
            CreatePart(PrimitiveType.Cylinder, "Face", discFaces[index], new Vector3(0f, 0.07f, 0f), new Vector3(1.56f, 0.015f, 1.56f), DiscFaceColor);
            // Local +Z matches the stored rotation: a glowing carved arrow rune with a crossbar tail.
            runeRenderers[index] = new List<Renderer>
            {
                CreatePart(PrimitiveType.Cube, "Rune Shaft", discFaces[index], new Vector3(0f, 0.095f, -0.05f), new Vector3(0.1f, 0.02f, 0.75f), RuneColor, BrightGlow).GetComponent<Renderer>(),
                CreateRotatedPart("Rune Head Left", discFaces[index], new Vector3(-0.11f, 0.095f, 0.36f), new Vector3(0.08f, 0.02f, 0.34f), -38f),
                CreateRotatedPart("Rune Head Right", discFaces[index], new Vector3(0.11f, 0.095f, 0.36f), new Vector3(0.08f, 0.02f, 0.34f), 38f),
                CreatePart(PrimitiveType.Cube, "Rune Tail", discFaces[index], new Vector3(0f, 0.095f, -0.42f), new Vector3(0.36f, 0.02f, 0.07f), RuneColor, BrightGlow).GetComponent<Renderer>()
            };
            return holder;
        }

        private Renderer CreateRotatedPart(string name, Transform parent, Vector3 localPosition, Vector3 localScale, float yaw)
        {
            GameObject part = CreatePart(PrimitiveType.Cube, name, parent, localPosition, localScale, RuneColor, BrightGlow);
            part.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return part.GetComponent<Renderer>();
        }

        /// <summary>A tree stump with a cut-wood top and a firefly orb hovering over it; the orb carries the highlight.</summary>
        private Renderer BuildSocketModel(Transform parent)
        {
            CreatePart(PrimitiveType.Cylinder, "Stump", parent, new Vector3(0f, 0.08f, 0f), new Vector3(SocketRadius * 2f, 0.08f, SocketRadius * 2f), StumpColor);
            CreatePart(PrimitiveType.Cylinder, "Stump Top", parent, new Vector3(0f, 0.162f, 0f), new Vector3(SocketRadius * 1.7f, 0.004f, SocketRadius * 1.7f), StumpTopColor);
            CreatePart(PrimitiveType.Sphere, "Moss", parent, new Vector3(0.12f, 0.03f, -0.14f), new Vector3(0.2f, 0.06f, 0.14f), MossColor);
            return CreatePart(PrimitiveType.Sphere, "Firefly Orb", parent, new Vector3(0f, 0.3f, 0f), new Vector3(0.16f, 0.16f, 0.16f), SocketColor, BrightGlow).GetComponent<Renderer>();
        }

        private GameObject CreatePart(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 localScale, Color color, float glow = SoftGlow)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            Collider collider = part.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            Renderer renderer = part.GetComponent<Renderer>();
            glowStrengths[renderer] = glow;
            ApplyColor(renderer, color, glow);
            return part;
        }

        private void SetColor(Renderer renderer, Color color)
        {
            if (renderer == null || (appliedColors.TryGetValue(renderer, out Color current) && current == color)) return;
            appliedColors[renderer] = color;
            ApplyColor(renderer, color, glowStrengths.TryGetValue(renderer, out float glow) ? glow : SoftGlow);
        }

        private static void ApplyColor(Renderer renderer, Color color, float glow)
        {
            if (renderer == null) return;
            renderer.material.color = color;
            renderer.material.SetColor("_EmissionColor", color * glow);
            renderer.material.EnableKeyword("_EMISSION");
        }

        private static bool HasTag(string[] tags, string expected)
        {
            foreach (string tag in tags ?? Array.Empty<string>()) if (string.Equals(tag, expected, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
