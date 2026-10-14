using System;
using System.Collections.Generic;
using UnityEngine;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
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
        // The centre socket keeps the original mechanism id, which Orio's completion condition names.
        public const string AnchorId = "mechanism.orio.rotate";
        public const string AlignmentCondition = "OrioMechanismAligned";
        public const string DiscTag = "rotara-disc";
        public const int TargetRotation = 0;
        private const string RotaraTag = "rotara";
        private const string QuestId = "orio_patch_notes";
        public const string CompletionEventId = "complete_orio_patch_notes";
        private const int InteractionRange = 1;
        private const int StepDegrees = 90;
        private const float FlashSeconds = 0.4f;
        private const float SolvedPulseSpeed = 2.4f;
        private const float SolvedGlowMin = 2.2f;
        private const float SolvedGlowMax = 4.2f;
        private const float DiscAnimationStepDegrees = 1.8f;
        private const float DiscAnimationStepSeconds = 0.01f;

        public static readonly string[] DiscIds = { "mechanism.orio.disc.northwest", "mechanism.orio.disc.northeast", "mechanism.orio.disc.south" };
        // Chosen so the intended solution is north 3, centre 2, west 2, east 1 presses; other press orders also solve it.
        public static readonly int[] DiscStartRotations = { 90, 180, 270 };
        public static readonly string[] SocketIds = { "mechanism.orio.socket.north", "mechanism.orio.socket.west", "mechanism.orio.socket.east", AnchorId };
        private static readonly int[][] SocketLinks = { new[] { 0, 1 }, new[] { 0, 2 }, new[] { 1, 2 }, new[] { 0, 1, 2 } };

        // A forest shrine: a moss floor in a ring of old stone, lichen-covered rune discs whose carved runes glow like
        // fireflies, tree-stump sockets topped with firefly orbs, and vines tracing which discs each socket turns.
        private static readonly Color DaisColor = new Color(0.2f, 0.3f, 0.17f);
        private static readonly Color DaisSolvedColor = new Color(0.24f, 0.42f, 0.22f);
        private static readonly Color RuneColor = new Color(1f, 0.83f, 0.42f);
        private static readonly Color NotchColor = new Color(0.2f, 0.62f, 0.55f);
        private static readonly Color SolvedColor = new Color(0.55f, 1f, 0.55f);
        private static readonly Color SocketColor = new Color(0.72f, 0.6f, 0.28f);
        private static readonly Color SocketReadyColor = new Color(1f, 0.86f, 0.42f);
        private static readonly Color LinkColor = new Color(0.27f, 0.45f, 0.2f);
        private static readonly Color FlashColor = new Color(0.86f, 1f, 0.78f);
        private static readonly Color StandingStoneColor = new Color(0.4f, 0.42f, 0.36f);
        private const float SoftGlow = 0.18f;
        private const float BrightGlow = 1.4f;
        private const int FireflyCount = 9;

        private static OrioRuneDiscPuzzle instance;
        private readonly GameObject[] discHolders = new GameObject[3];
        private readonly Vector3[] authoredDiscLocalPositions = new Vector3[3];
        private readonly Quaternion[] clearedDiscLocalRotations = new Quaternion[3];
        private readonly float[] solvedDiscStateRotations = new float[3];
        private readonly float[] displayedDiscRotations = new float[3];
        private readonly float[] discAnimationTargets = new float[3];
        private readonly float[] nextDiscStepAt = new float[3];
        private readonly bool[] discPresentationInitialized = new bool[3];
        private readonly List<Renderer>[] runeRenderers = new List<Renderer>[3];
        private readonly Renderer[] notchRenderers = new Renderer[3];
        private readonly Renderer[] standingStoneRenderers = new Renderer[9];
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

        public static bool RotationMatches(float angle, float required) =>
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
                    !RotationMatches(disc.rotation, SolvedStateRotation(i))) return false;
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

        // Orio's dialogue and completion event use this condition; its handler is registered once a runtime exists. In the
        // editor it is also declared after every script reload, which a reload during Play Mode needs: SubsystemRegistration
        // only runs when Play Mode starts.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        private static void DeclareQuestConditions() => GameEventCatalog.DeclareCustomConditionType(AlignmentCondition);

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
            OrioRuneDiscAuthoring authored = areaRoot == null ? null : areaRoot.GetComponentInChildren<OrioRuneDiscAuthoring>(true);
            if (authored == null)
            {
                Debug.LogError("Deep Forest is missing its authored Orio Rune Dais.");
                instance.SetPresentationVisible(false);
                return;
            }
            instance.BindAuthored(authored.transform);
            instance.SetPresentationVisible(true);
            instance.RegisterBoundPuzzleObjects(state, world);
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
        }

        private void LateUpdate()
        {
            if (!PuzzleMapActive(out WorldStateService state)) return;
            bool allSolved = true;
            bool animationsSettled = true;
            for (int i = 0; i < DiscIds.Length; i++)
            {
                if (discHolders[i] == null || !state.TryGetObject(DiscIds[i], out WorldObjectSnapshot disc)) { allSolved = false; continue; }
                // Keep the prefab-authored placement under the rotated Rune Dais. World state stores only the
                // quarter-turn offset; this component owns the actual local transform presentation.
                Transform holder = discHolders[i].transform;
                holder.localPosition = authoredDiscLocalPositions[i];
                UpdateDiscAnimation(i, disc.rotation);
                holder.localRotation = clearedDiscLocalRotations[i] * Quaternion.Euler(0f, displayedDiscRotations[i], 0f);
                bool solved = RotationMatches(disc.rotation, solvedDiscStateRotations[i]);
                allSolved &= solved;
                animationsSettled &= Mathf.Approximately(displayedDiscRotations[i], discAnimationTargets[i]);
                foreach (Renderer rune in runeRenderers[i]) SetColor(rune, solved ? SolvedColor : RuneColor);
                SetColor(notchRenderers[i], solved ? SolvedColor : NotchColor);
            }
            foreach (Renderer stone in standingStoneRenderers) SetColor(stone, StandingStoneColor);
            SetColor(daisRenderer, allSolved ? DaisSolvedColor : DaisColor);
            if (allSolved) ShineSolvedMonument();
            if (allSolved && animationsSettled)
                GameEventManager.Instance?.Runtime?.TriggerEvent(CompletionEventId);
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

        private void SetPresentationVisible(bool visible)
        {
            if (presentationRoot != null && presentationRoot.gameObject.activeSelf != visible) presentationRoot.gameObject.SetActive(visible);
        }

        private void RegisterBoundPuzzleObjects(WorldStateService state, GridWorld world)
        {
            if (state == null || world == null) return;
            for (int i = 0; i < DiscIds.Length; i++)
            {
                GameObject holder = discHolders[i];
                if (holder == null) { Debug.LogError($"Deep Forest Rune Dais is missing '{DiscIds[i]}'."); continue; }
                Vector2Int cell = world.WorldToCell(holder.transform.position);
                state.RegisterObject(DiscIds[i], holder, cell, Vector2Int.one, false, new[] { DiscTag }, syncSceneTransform: false);
                int start = DiscStartRotations[i];
                state.SetProperty(DiscIds[i], item => item.rotation = start);
                discPresentationInitialized[i] = false;
            }
            for (int i = 0; i < SocketIds.Length; i++)
            {
                GameObject holder = socketHolders[i];
                if (holder == null) { Debug.LogError($"Deep Forest Rune Dais is missing '{SocketIds[i]}'."); continue; }
                Vector2Int cell = world.WorldToCell(holder.transform.position);
                state.RegisterObject(SocketIds[i], holder, cell, Vector2Int.one, true, new[] { RotaraTag }, syncSceneTransform: false);
            }
        }

        private void BindAuthored(Transform root)
        {
            if (root == null) return;
            bool captureAuthoredPose = presentationRoot != root;
            presentationRoot = root;
            appliedColors.Clear();
            glowStrengths.Clear();
            daisRenderer = FindRenderer(root, "Moss Floor");
            for (int i = 0; i < DiscIds.Length; i++)
            {
                Transform holder = FindDescendant(root, DiscIds[i]);
                discHolders[i] = holder == null ? null : holder.gameObject;
                if (captureAuthoredPose && holder != null)
                {
                    authoredDiscLocalPositions[i] = holder.localPosition;
                    clearedDiscLocalRotations[i] = holder.localRotation;
                    // The completion rule is based on the authored disc root itself reaching local Y = 0.
                    // Convert that into the corresponding saved quarter-turn offset for this particular disc.
                    solvedDiscStateRotations[i] = NormalizeDegrees(Mathf.Round(-holder.localEulerAngles.y / StepDegrees) * StepDegrees);
                }
                Transform discBase = holder == null ? null : holder.Find("Base");
                Transform discFace = holder == null ? null : holder.Find("Face");
                notchRenderers[i] = discBase == null ? null : FindRenderer(discBase, "Target Notch");
                runeRenderers[i] = new List<Renderer>();
                if (discFace != null)
                    foreach (Renderer renderer in discFace.GetComponentsInChildren<Renderer>(true))
                        if (renderer.name.StartsWith("Rune ", StringComparison.Ordinal)) runeRenderers[i].Add(renderer);
            }
            for (int i = 0; i < standingStoneRenderers.Length; i++)
                standingStoneRenderers[i] = FindRenderer(root, "Standing Stone " + (i + 1));
            for (int i = 0; i < SocketIds.Length; i++)
            {
                Transform holder = FindDescendant(root, SocketIds[i]);
                socketHolders[i] = holder == null ? null : holder.gameObject;
                gemRenderers[i] = holder == null ? null : FindRenderer(holder, "Firefly Orb");
                linkRenderers[i] = new List<Renderer>();
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                    if (renderer.name.StartsWith("Vine " + SocketIds[i] + " -> ", StringComparison.Ordinal)) linkRenderers[i].Add(renderer);
            }
            int firefly = 0;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                Renderer renderer = child.GetComponent<Renderer>();
                if (renderer != null) glowStrengths[renderer] = child.name.StartsWith("Rune ", StringComparison.Ordinal) ||
                    child.name == "Firefly Orb" || child.name == "Firefly" ? BrightGlow : SoftGlow;
                if (child.name != "Firefly" || firefly >= FireflyCount) continue;
                fireflies[firefly] = child;
                fireflyHomes[firefly] = child.position;
                firefly++;
            }
        }

        private static Transform FindDescendant(Transform root, string objectName)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (string.Equals(child.name, objectName, StringComparison.Ordinal)) return child;
            return null;
        }

        private static float SolvedStateRotation(int discIndex) =>
            instance != null && discIndex >= 0 && discIndex < instance.solvedDiscStateRotations.Length
                ? instance.solvedDiscStateRotations[discIndex]
                : TargetRotation;

        private void UpdateDiscAnimation(int index, float stateRotation)
        {
            float target = NormalizeDegrees(stateRotation);
            if (!discPresentationInitialized[index])
            {
                displayedDiscRotations[index] = target;
                discAnimationTargets[index] = target;
                nextDiscStepAt[index] = Time.unscaledTime + DiscAnimationStepSeconds;
                discPresentationInitialized[index] = true;
                return;
            }

            if (!Mathf.Approximately(discAnimationTargets[index], target))
            {
                discAnimationTargets[index] = target;
                nextDiscStepAt[index] = Time.unscaledTime + DiscAnimationStepSeconds;
            }

            while (!Mathf.Approximately(displayedDiscRotations[index], target) &&
                   Time.unscaledTime >= nextDiscStepAt[index])
            {
                float remaining = NormalizeDegrees(target - displayedDiscRotations[index]);
                displayedDiscRotations[index] = remaining <= DiscAnimationStepDegrees
                    ? target
                    : NormalizeDegrees(displayedDiscRotations[index] + DiscAnimationStepDegrees);
                nextDiscStepAt[index] += DiscAnimationStepSeconds;
            }
        }

        private static Renderer FindRenderer(Transform root, string objectName)
        {
            Transform found = FindDescendant(root, objectName);
            return found == null ? null : found.GetComponent<Renderer>();
        }

        private void DriftFireflies()
        {
            float time = Time.time;
            for (int i = 0; i < FireflyCount; i++)
                if (fireflies[i] != null)
                    fireflies[i].position = fireflyHomes[i] + new Vector3(Mathf.Sin(time * 0.6f + i * 1.7f) * 0.3f,
                        Mathf.Sin(time * 1.3f + i) * 0.12f, Mathf.Cos(time * 0.5f + i * 2.3f) * 0.3f);
        }

        private void ShineSolvedMonument()
        {
            float pulse = (Mathf.Sin(Time.time * SolvedPulseSpeed) + 1f) * 0.5f;
            Color shine = Color.Lerp(SolvedColor, Color.white, pulse * 0.45f);
            float glow = Mathf.Lerp(SolvedGlowMin, SolvedGlowMax, pulse);
            foreach (List<Renderer> renderers in runeRenderers)
            {
                if (renderers == null) continue;
                foreach (Renderer rune in renderers) ApplyShine(rune, shine, glow);
            }
            foreach (Renderer stone in standingStoneRenderers) ApplyShine(stone, shine, glow);
        }

        private void ApplyShine(Renderer renderer, Color color, float glow)
        {
            if (renderer == null) return;
            appliedColors[renderer] = color;
            ApplyColor(renderer, color, glow);
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
