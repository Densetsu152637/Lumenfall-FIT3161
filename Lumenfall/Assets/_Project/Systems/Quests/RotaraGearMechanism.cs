using System;
using System.Collections.Generic;
using UnityEngine;
using Lumenfall.Controls;
using Lumenfall.DataDriven;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Inventory;
using Lumenfall.Spells;

namespace Lumenfall.Quests
{
    /// <summary>
    /// Gear presentation for authored Rotara mechanisms. Orio teaches Rotara before the player
    /// casts the selected spell at the gear. Live world state, rather than a saved readiness flag,
    /// verifies the exact forest mechanism when Orio confirms the patch.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RotaraGearMechanism : MonoBehaviour
    {
        private const string RotaraTag = "rotara";
        private static float panelDrawnAt = -10f;
        /// True while a mechanism's bottom-left status panel is on screen, so other HUD can move out of its way.
        public static bool PanelVisible => Time.unscaledTime - panelDrawnAt < 0.2f;
        private const string QuestId = "orio_patch_notes";
        public const string PuzzleMapId = "westForest";
        public const string PuzzleObjectId = "mechanism.orio.rotate";
        public const string AlignmentCondition = "OrioMechanismAligned";
        private const string AlignedFlag = "orio.patch_notes.mechanism_aligned";
        private const int StepDegrees = 90;
        private const int FallbackRequiredRotation = 90;
        private const float TurnSpeed = 240f;
        private const int InteractionRange = 2;
        private const int ToothCount = 10;
        private const float RootRadius = 0.34f;
        private const float TipRadius = 0.42f;
        private const float GearThickness = 0.07f;
        private const float PedestalHeight = 0.26f;
        private static readonly Color GearColor = new Color(0.72f, 0.56f, 0.28f);
        private static readonly Color PlinthColor = new Color(0.22f, 0.24f, 0.3f);
        private static readonly Color PointerColor = new Color(0.95f, 0.35f, 0.25f);
        private static readonly Color TargetColor = new Color(0.2f, 0.55f, 0.48f);
        private static readonly Color SolvedColor = new Color(0.25f, 0.92f, 0.75f);

        private string objectId;
        private int requiredRotation;
        private Transform gear;
        private Transform plinth;
        private GameObject pointer;
        private GameObject targetNotch;
        private GridMover player;
        private float displayedAngle;
        private bool hasDisplayedAngle;
        private bool? shownSolved;

        /// <summary>Called by the area loader so every authored Rotara mechanism is presented as a gear.</summary>
        public static void AttachTo(GameObject areaRoot)
        {
            RegisterQuestConditions(GameEventManager.Instance?.Runtime);
            if (areaRoot == null) return;
            foreach (WorldObjectRegistration registration in areaRoot.GetComponentsInChildren<WorldObjectRegistration>(true))
                if (registration.GetComponent<RotaraGearMechanism>() == null && HasRotaraTag(registration.Tags))
                    registration.gameObject.AddComponent<RotaraGearMechanism>();
        }

        private void Awake()
        {
            objectId = GetComponent<WorldObjectRegistration>().ObjectId;
            requiredRotation = ResolveRequiredRotation(objectId);
            BuildPresentation();
            RegisterQuestConditions(GameEventManager.Instance?.Runtime);
        }

        // Area switches and save restores reactivate the area, so the gear snaps to stored state instead of spinning.
        private void OnEnable() { hasDisplayedAngle = false; shownSolved = null; }

        private void LateUpdate()
        {
            // SyncTransform snaps the whole object to the stored rotation; keep the plinth fixed and ease the gear.
            if (plinth != null) plinth.rotation = Quaternion.identity;
            WorldStateService state = WorldStateService.Instance;
            if (gear == null || state == null || !state.IsReady || !state.TryGetObject(objectId, out WorldObjectSnapshot snapshot)) return;
            float target = snapshot.rotation;
            if (!hasDisplayedAngle)
            {
                displayedAngle = target;
                hasDisplayedAngle = true;
            }
            else
            {
                // Turns are always clockwise, so chase the stored angle clockwise even when spells queue several quarter turns.
                float clockwise = Mathf.Repeat(target - displayedAngle, 360f);
                if (clockwise > 315f) displayedAngle = Mathf.MoveTowardsAngle(displayedAngle, target, TurnSpeed * Time.deltaTime);
                else displayedAngle = Mathf.Repeat(displayedAngle + Mathf.Min(clockwise, TurnSpeed * Time.deltaTime), 360f);
            }
            gear.rotation = Quaternion.Euler(0f, displayedAngle, 0f);

            bool solved = IsSolved(snapshot) && !IsAnimating(snapshot);
            if (shownSolved != solved)
            {
                shownSolved = solved;
                SetColor(targetNotch, solved ? SolvedColor : TargetColor);
                SetColor(pointer, solved ? SolvedColor : PointerColor);
            }

            GameEventRuntime runtime = GameEventManager.Instance?.Runtime;
            RegisterQuestConditions(runtime);
            // Preserve the legacy feedback flag, but never use it as completion authority.
            if (runtime != null && runtime.GetQuestStatus(QuestId) == QuestStatus.Active &&
                string.Equals(objectId, PuzzleObjectId, StringComparison.Ordinal))
            {
                bool aligned = IsQuestReady(state, runtime);
                if (runtime.GetFlag(AlignedFlag) != aligned) runtime.SetFlag(AlignedFlag, aligned);
            }
        }

        private bool PlayerInRange(WorldObjectSnapshot snapshot)
        {
            // Resolve through the player input so an NPC mover can never be mistaken for the player.
            if (player == null)
            {
                GridPlayerInput input = FindAnyObjectByType<GridPlayerInput>();
                player = input != null ? input.GetComponent<GridMover>() : FindAnyObjectByType<GridMover>();
            }
            return player != null && Mathf.Abs(player.CurrentCell.x - snapshot.cell.x) + Mathf.Abs(player.CurrentCell.y - snapshot.cell.y) <= InteractionRange;
        }

        private bool IsAnimating(WorldObjectSnapshot snapshot) => hasDisplayedAngle && Mathf.Abs(Mathf.DeltaAngle(displayedAngle, snapshot.rotation)) > 0.5f;
        private bool IsSolved(WorldObjectSnapshot snapshot) => RotationMatches(snapshot.rotation, requiredRotation);

        /// <summary>Registers a stateless resolver; it retains no gear, map instance or old runtime.</summary>
        public static void RegisterQuestConditions(GameEventRuntime runtime)
        {
            runtime?.RegisterConditionHandler(AlignmentCondition, (condition, current) =>
                string.Equals(condition.key, PuzzleObjectId, StringComparison.Ordinal) &&
                IsQuestReady(WorldStateService.Instance, current));
        }

        public static bool IsQuestReady(WorldStateService state, GameEventRuntime runtime) =>
            runtime != null && runtime.GetQuestStatus(QuestId) == QuestStatus.Active &&
            runtime.GetFlag("spell.unlocked.Rotara") && state != null && state.IsReady &&
            state.TryGetObject(PuzzleObjectId, out WorldObjectSnapshot snapshot) &&
            MatchesQuestTarget(state.CurrentMapId, snapshot);

        public static bool MatchesQuestTarget(string mapId, WorldObjectSnapshot snapshot) =>
            string.Equals(mapId, PuzzleMapId, StringComparison.OrdinalIgnoreCase) && snapshot != null &&
            string.Equals(snapshot.mapId, PuzzleMapId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(snapshot.id, PuzzleObjectId, StringComparison.Ordinal) && snapshot.spellCompatible &&
            HasRotaraTag(snapshot.tags) && RotationMatches(snapshot.rotation, FallbackRequiredRotation);

        public static bool RotationMatches(float angle, int required) =>
            !float.IsNaN(angle) && !float.IsInfinity(angle) &&
            Math.Abs(NormalizeDegrees(angle) - NormalizeDegrees(required)) < 0.001f;

        public static float NextQuarterTurn(float angle) =>
            NormalizeDegrees((float)Math.Round(NormalizeDegrees(angle) / StepDegrees, MidpointRounding.ToEven) * StepDegrees + StepDegrees);

        private static float NormalizeDegrees(float angle) { angle %= 360f; return angle < 0f ? angle + 360f : angle; }

        private void OnGUI()
        {
            WorldStateService state = WorldStateService.Instance;
            if (state == null || !state.IsReady || InventoryUiState.IsOpen ||
                GameEventManager.Instance?.Runtime?.ActiveDialogue != null ||
                !state.TryGetObject(objectId, out WorldObjectSnapshot snapshot) || !PlayerInRange(snapshot)) return;
            string status = RotationMatches(snapshot.rotation, requiredRotation) ? "At target. Return to Orio to confirm." :
                "Each Rotara cast turns clockwise by 90 degrees. Overshot? Keep turning to retry.";
            if (!string.Equals(state.CurrentMapId, PuzzleMapId, StringComparison.OrdinalIgnoreCase) || objectId != PuzzleObjectId)
                status = "Practice mechanism; this does not complete Orio's forest puzzle.";
            else if (!GameEventManager.Instance.Runtime.GetFlag("spell.unlocked.Rotara"))
                status = "Finish Orio's Rotara lesson before casting.";
            else if (GameEventManager.Instance.Runtime.GetQuestStatus(QuestId) == QuestStatus.Completed)
                status = "Patch confirmed. Further turns are practice; completion is saved.";
            panelDrawnAt = Time.unscaledTime;
            GUI.Box(new Rect(16f, Screen.height - 108f, 590f, 92f), "Rotara mechanism");
            GUI.Label(new Rect(28f, Screen.height - 84f, 566f, 24f),
                $"Current: {NormalizeDegrees(snapshot.rotation):0} degrees | Target: {requiredRotation} degrees | " +
                KeyBindings.GetKeyLabel(GameAction.Interact) + ": cast selected spell (choose Rotara on the spell wheel)");
            GUI.Label(new Rect(28f, Screen.height - 58f, 566f, 36f), status);
        }

        private void BuildPresentation()
        {
            Renderer placeholder = GetComponent<Renderer>();
            if (placeholder != null) placeholder.enabled = false;

            plinth = new GameObject("Gear Plinth").transform;
            plinth.SetParent(transform, false);
            GameObject plate = CreatePart(PrimitiveType.Cylinder, "Plate", plinth, new Vector3(0f, PedestalHeight * 0.5f, 0f), new Vector3(1.16f, PedestalHeight * 0.5f, 1.16f), PlinthColor);
            // The notch stays on the fixed plinth at the orientation the puzzle target expects.
            targetNotch = CreatePart(PrimitiveType.Cube, "Target Notch", plinth, Quaternion.Euler(0f, requiredRotation, 0f) * new Vector3(0f, PedestalHeight + 0.025f, 0.5f),
                new Vector3(0.1f, 0.05f, 0.14f), TargetColor);
            targetNotch.transform.localRotation = Quaternion.Euler(0f, requiredRotation, 0f);

            gear = new GameObject("Gear").transform;
            gear.SetParent(transform, false);
            GameObject body = new GameObject("Gear Body");
            body.transform.SetParent(gear, false);
            body.transform.localPosition = new Vector3(0f, PedestalHeight + 0.04f, 0f);
            body.AddComponent<MeshFilter>().sharedMesh = BuildGearMesh(ToothCount, RootRadius, TipRadius, GearThickness);
            body.AddComponent<MeshRenderer>().sharedMaterial = plate.GetComponent<Renderer>().sharedMaterial;
            SetColor(body, GearColor);
            CreatePart(PrimitiveType.Cylinder, "Hub", gear, new Vector3(0f, PedestalHeight + 0.1f, 0f), new Vector3(0.2f, 0.03f, 0.2f), PlinthColor);
            // Local +Z matches the stored rotation, so the pointer shows where the mechanism is facing.
            pointer = CreatePart(PrimitiveType.Cube, "Pointer", gear, new Vector3(0f, PedestalHeight + 0.09f, 0.225f), new Vector3(0.08f, 0.03f, 0.35f), PointerColor);
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
            SetColor(part, color);
            return part;
        }

        /// <summary>Extruded spur gear: a star-shaped outline fanned from the centre, plus flat-shaded side walls.</summary>
        private static Mesh BuildGearMesh(int teeth, float rootRadius, float tipRadius, float thickness)
        {
            // Per tooth: root land, rising flank, flat tip, falling flank into the next root land.
            float[] fractions = { 0f, 0.35f, 0.5f, 0.85f };
            float[] radii = { rootRadius, rootRadius, tipRadius, tipRadius };
            List<Vector3> outline = new List<Vector3>();
            float period = Mathf.PI * 2f / teeth;
            for (int i = 0; i < teeth; i++)
                for (int j = 0; j < fractions.Length; j++)
                {
                    float angle = (i + fractions[j]) * period;
                    outline.Add(new Vector3(Mathf.Sin(angle) * radii[j], 0f, Mathf.Cos(angle) * radii[j]));
                }

            Vector3 top = Vector3.up * thickness * 0.5f;
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            int count = outline.Count;
            int topCenter = vertices.Count; vertices.Add(top);
            for (int i = 0; i < count; i++) vertices.Add(outline[i] + top);
            for (int i = 0; i < count; i++) triangles.AddRange(new[] { topCenter, topCenter + 1 + i, topCenter + 1 + (i + 1) % count });
            int bottomCenter = vertices.Count; vertices.Add(-top);
            for (int i = 0; i < count; i++) vertices.Add(outline[i] - top);
            for (int i = 0; i < count; i++) triangles.AddRange(new[] { bottomCenter, bottomCenter + 1 + (i + 1) % count, bottomCenter + 1 + i });
            for (int i = 0; i < count; i++)
            {
                Vector3 a = outline[i], b = outline[(i + 1) % count];
                int start = vertices.Count;
                vertices.Add(a + top); vertices.Add(a - top); vertices.Add(b - top); vertices.Add(b + top);
                triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            }

            Mesh mesh = new Mesh { name = "Rotara Gear" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static int ResolveRequiredRotation(string id)
        {
            foreach (PuzzleTargetDefinition target in WorldStateService.Instance?.PuzzleTargets ?? Array.Empty<PuzzleTargetDefinition>())
                if (target != null && target.requiredRotation >= 0 && Array.IndexOf(target.objectIds ?? Array.Empty<string>(), id) >= 0) return target.requiredRotation;
            return FallbackRequiredRotation;
        }

        private static bool HasRotaraTag(string[] tags)
        {
            foreach (string tag in tags ?? Array.Empty<string>()) if (string.Equals(tag, RotaraTag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static void SetColor(GameObject target, Color color)
        {
            Renderer renderer = target == null ? null : target.GetComponent<Renderer>();
            if (renderer == null) return;
            renderer.material.color = color;
            renderer.material.SetColor("_EmissionColor", color * 0.18f);
            renderer.material.EnableKeyword("_EMISSION");
        }
    }
}
