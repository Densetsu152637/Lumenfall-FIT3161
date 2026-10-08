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
    /// the hall built by <see cref="OrioDaisRoom"/> and is registered in code, like Stella's relic board. World state,
    /// read live in the hall or from its archive when reporting to Orio in the forest, decides completion; the legacy
    /// flag never does.
    /// </summary>
    public sealed class OrioRuneDiscPuzzle : MonoBehaviour
    {
        public const string PuzzleMapId = OrioDaisRoom.MapId;
        // The centre socket keeps the original mechanism id, which Orio's completion condition names.
        public const string AnchorId = "mechanism.orio.rotate";
        public const string AlignmentCondition = "OrioMechanismAligned";
        public const string DiscTag = "rotara-disc";
        public const int TargetRotation = 0;
        private const string RotaraTag = "rotara";
        private const string QuestId = "orio_patch_notes";
        private const string AlignedFlag = "orio.patch_notes.mechanism_aligned";
        private const int InteractionRange = 1;
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

        private static readonly Color DaisColor = new Color(0.17f, 0.17f, 0.22f);
        private static readonly Color DaisRimColor = new Color(0.32f, 0.3f, 0.38f);
        private static readonly Color DaisSolvedColor = new Color(0.16f, 0.3f, 0.3f);
        private static readonly Color DiscBaseColor = new Color(0.27f, 0.27f, 0.33f);
        private static readonly Color DiscFaceColor = new Color(0.4f, 0.38f, 0.46f);
        private static readonly Color RuneColor = new Color(0.68f, 0.52f, 1f);
        private static readonly Color NotchColor = new Color(0.2f, 0.55f, 0.48f);
        private static readonly Color SolvedColor = new Color(0.25f, 0.92f, 0.75f);
        private static readonly Color SocketColor = new Color(0.55f, 0.42f, 0.2f);
        private static readonly Color SocketReadyColor = new Color(1f, 0.78f, 0.35f);
        private static readonly Color LinkColor = new Color(0.36f, 0.3f, 0.55f);
        private static readonly Color FlashColor = new Color(0.85f, 0.75f, 1f);

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
        private Transform presentationRoot;
        private Renderer daisRenderer;
        private GridMover player;
        private bool hasDisplayedAngles;
        private int highlightedSocket = -1;

        /// <summary>Socket links apply only in the hall; the forest pad and campus gear share the centre id but are not sockets.</summary>
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

        public static bool AreDiscsAligned(string mapId, IList<WorldObjectSnapshot> discs)
        {
            if (!string.Equals(mapId, PuzzleMapId, StringComparison.OrdinalIgnoreCase) || discs == null || discs.Count != DiscIds.Length) return false;
            for (int i = 0; i < DiscIds.Length; i++)
            {
                WorldObjectSnapshot disc = discs[i];
                if (disc == null || !string.Equals(disc.id, DiscIds[i], StringComparison.Ordinal) ||
                    !string.Equals(disc.mapId, PuzzleMapId, StringComparison.OrdinalIgnoreCase) || !HasTag(disc.tags, DiscTag) ||
                    !RotaraGearMechanism.RotationMatches(disc.rotation, TargetRotation)) return false;
            }
            return true;
        }

        /// <summary>Reads the hall live while the player is in it, or its archived state when reporting to Orio in the forest.</summary>
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
            instance.Build(world, OrioDaisRoom.DaisCentre);
            instance.SetPresentationVisible(true);
            RegisterPuzzleObjects(state, OrioDaisRoom.DaisCentre, instance.discHolders, instance.socketHolders);
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
                bool solved = RotaraGearMechanism.RotationMatches(disc.rotation, TargetRotation) && !IsTurning(i, disc);
                allSolved &= solved;
                foreach (Renderer rune in runeRenderers[i]) SetColor(rune, solved ? SolvedColor : RuneColor);
                SetColor(notchRenderers[i], solved ? SolvedColor : NotchColor);
            }
            hasDisplayedAngles = true;
            SetColor(daisRenderer, allSolved ? DaisSolvedColor : DaisColor);

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
            CreatePart(PrimitiveType.Cylinder, "Dais Rim", presentationRoot, centre + new Vector3(0f, 0.008f, 0f), new Vector3(7.1f, 0.005f, 7.1f), DaisRimColor);
            daisRenderer = CreatePart(PrimitiveType.Cylinder, "Dais", presentationRoot, centre + new Vector3(0f, 0.014f, 0f), new Vector3(6.8f, 0.005f, 6.8f), DaisColor).GetComponent<Renderer>();

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
                // Faint floor lines show which discs each socket turns, and flash when it is pressed.
                linkRenderers[i] = new List<Renderer>();
                foreach (int disc in SocketLinks[i])
                {
                    Vector3 direction = discPositions[disc] - socketPosition;
                    direction.y = 0f;
                    float length = direction.magnitude - DiscRadius - SocketRadius;
                    if (length <= 0.05f) continue;
                    Vector3 start = socketPosition + direction.normalized * SocketRadius;
                    GameObject link = CreatePart(PrimitiveType.Cube, "Link " + SocketIds[i] + " -> " + DiscIds[disc], presentationRoot,
                        start + direction.normalized * (length * 0.5f) + new Vector3(0f, 0.025f, 0f), new Vector3(0.06f, 0.006f, length), LinkColor);
                    link.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
                    linkRenderers[i].Add(link.GetComponent<Renderer>());
                }
            }
        }

        private GameObject BuildDisc(int index, Vector3 position)
        {
            GameObject holder = new GameObject(DiscIds[index]);
            holder.transform.SetParent(presentationRoot, false);
            holder.transform.position = position;

            discBases[index] = new GameObject("Base").transform;
            discBases[index].SetParent(holder.transform, false);
            CreatePart(PrimitiveType.Cylinder, "Stone", discBases[index], new Vector3(0f, 0.04f, 0f), new Vector3(DiscRadius * 2f, 0.02f, DiscRadius * 2f), DiscBaseColor);
            // The notch is fixed on the base at the target orientation (north); the rune must point at it.
            notchRenderers[index] = CreatePart(PrimitiveType.Cube, "Target Notch", discBases[index], new Vector3(0f, 0.065f, DiscRadius - 0.11f),
                new Vector3(0.14f, 0.03f, 0.16f), NotchColor).GetComponent<Renderer>();

            discFaces[index] = new GameObject("Face").transform;
            discFaces[index].SetParent(holder.transform, false);
            CreatePart(PrimitiveType.Cylinder, "Face", discFaces[index], new Vector3(0f, 0.07f, 0f), new Vector3(1.56f, 0.015f, 1.56f), DiscFaceColor);
            // Local +Z matches the stored rotation: an arrow rune with a crossbar tail.
            runeRenderers[index] = new List<Renderer>
            {
                CreatePart(PrimitiveType.Cube, "Rune Shaft", discFaces[index], new Vector3(0f, 0.095f, -0.05f), new Vector3(0.1f, 0.02f, 0.75f), RuneColor).GetComponent<Renderer>(),
                CreateRotatedPart("Rune Head Left", discFaces[index], new Vector3(-0.11f, 0.095f, 0.36f), new Vector3(0.08f, 0.02f, 0.34f), -38f),
                CreateRotatedPart("Rune Head Right", discFaces[index], new Vector3(0.11f, 0.095f, 0.36f), new Vector3(0.08f, 0.02f, 0.34f), 38f),
                CreatePart(PrimitiveType.Cube, "Rune Tail", discFaces[index], new Vector3(0f, 0.095f, -0.42f), new Vector3(0.36f, 0.02f, 0.07f), RuneColor).GetComponent<Renderer>()
            };
            return holder;
        }

        private static Renderer CreateRotatedPart(string name, Transform parent, Vector3 localPosition, Vector3 localScale, float yaw)
        {
            GameObject part = CreatePart(PrimitiveType.Cube, name, parent, localPosition, localScale, RuneColor);
            part.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return part.GetComponent<Renderer>();
        }

        private static Renderer BuildSocketModel(Transform parent)
        {
            CreatePart(PrimitiveType.Cylinder, "Pedestal", parent, new Vector3(0f, 0.06f, 0f), new Vector3(SocketRadius * 2f, 0.06f, SocketRadius * 2f), DiscBaseColor);
            return CreatePart(PrimitiveType.Sphere, "Gem", parent, new Vector3(0f, 0.17f, 0f), new Vector3(0.22f, 0.22f, 0.22f), SocketColor).GetComponent<Renderer>();
        }

        private static GameObject CreatePart(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 localScale, Color color)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            Collider collider = part.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            ApplyColor(part.GetComponent<Renderer>(), color);
            return part;
        }

        private void SetColor(Renderer renderer, Color color)
        {
            if (renderer == null || (appliedColors.TryGetValue(renderer, out Color current) && current == color)) return;
            appliedColors[renderer] = color;
            ApplyColor(renderer, color);
        }

        private static void ApplyColor(Renderer renderer, Color color)
        {
            if (renderer == null) return;
            renderer.material.color = color;
            renderer.material.SetColor("_EmissionColor", color * 0.18f);
            renderer.material.EnableKeyword("_EMISSION");
        }

        private static bool HasTag(string[] tags, string expected)
        {
            foreach (string tag in tags ?? Array.Empty<string>()) if (string.Equals(tag, expected, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
