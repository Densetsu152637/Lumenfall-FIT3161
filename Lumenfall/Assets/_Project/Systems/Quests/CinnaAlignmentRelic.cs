using System;
using Lumenfall.Rendering;
using UnityEngine;
using Lumenfall.DataDriven;
using Lumenfall.GridMovement;
using Lumenfall.Spells;

namespace Lumenfall.Quests
{
    /// <summary>
    /// Cinna's Out of Alignment relic. It rests on campus beside the C02 terrace (<c>vis-lift</c>). Vis lifts it, the
    /// player carries it through the ice-trial portal (<c>vis-destination:C03.IceTrial</c>), and Vis sets it on the
    /// trial pedestal (<c>vis-stand:relic.cinna.orientation</c>), where it becomes the frost relic Glaciate freezes.
    /// <see cref="SpellCaster"/> owns the rules; this component registers the campus relic and draws the relic on every
    /// map. The loader calls <see cref="RegisterForMap"/> before EndMapRebuild so saved state is restored like any
    /// authored object.
    /// </summary>
    /// <remarks>
    /// World state never moves while the relic is carried: it stays registered at its last safe placement, which a faint
    /// mark shows. The relic the player sees is a separate visual that layers motion on top: an idle wobble at rest; a
    /// dip and rise when lifted; a bobbing, slowly turning hover in front of the player; a float down when set down; a shudder
    /// or a bump when placement is refused; and on delivery a socket pulse and a shockwave ring. A ghost tile shows
    /// where the relic would land, green when allowed. Map loads, save restores and releases snap without animating.
    /// The relic is the <see cref="CinnaIceTrialArt"/> model (a stone base and a crystal that takes the state colours) when
    /// that asset has been built, and a primitive stone with a notch otherwise.
    /// </remarks>
    public sealed class CinnaAlignmentRelic : MonoBehaviour
    {
        public const string MapId = "campus";
        public const string RelicId = "relic.cinna.orientation";
        private const string StandTagPrefix = "vis-stand:";
        private const float AnticipationDuration = 0.08f;
        private const float AnticipationDepth = 0.08f;
        private const float LiftDuration = 0.3f;
        private const float PlaceDuration = 0.3f;
        private const float PlaceArc = 0.15f;
        private const float HoverHeight = 0.55f;
        private const float HoverReach = 0.85f;
        private const float HoverBob = 0.07f;
        private const float HoverTurnDegrees = 40f;
        private const float FollowSharpness = 14f;
        private const float PulseDuration = 0.4f;
        private const float ShudderDuration = 0.22f;
        private const float ShudderDistance = 0.05f;
        private const float ShudderDegrees = 4f;
        private const float BumpDuration = 0.25f;
        private const float BumpDistance = 0.15f;
        private const float WobbleDegrees = 2.5f;
        private const float RingDuration = 0.45f;
        private const float RingStartRadius = 0.45f;
        private const float RingEndRadius = 1.4f;
        private const float RingWidth = 0.08f;
        private const int RingSegments = 40;
        private const float CrystalGlow = 0.45f;
        private static readonly Color RestColor = new Color(0.45f, 0.4f, 0.62f);
        private static readonly Color NotchGlowColor = new Color(0.78f, 0.72f, 1f);
        private static readonly Color HeldColor = new Color(0.66f, 0.58f, 1f);
        private static readonly Color StandColor = new Color(0.55f, 0.95f, 1f);
        private static readonly Color FrozenColor = new Color(0.86f, 0.97f, 1f);
        private static readonly Color MarkColor = new Color(0.3f, 0.28f, 0.4f);
        private static readonly Color ValidColor = new Color(0.35f, 0.9f, 0.5f);
        private static readonly Color InvalidColor = new Color(0.95f, 0.35f, 0.3f);

        private enum Motion { None, Lift, Place }

        private static CinnaAlignmentRelic instance;
        private GridWorld grid;
        private string mapId;
        private bool campusRegistered;
        private bool standOnMap;
        private Vector2Int standCell;
        private Transform campusRoot;
        private GameObject relicView;
        private GameObject restMark;
        private Transform relicVisual;
        private Transform preview;
        private Renderer[] relicRenderers = Array.Empty<Renderer>();
        private Renderer notchRenderer;
        private Renderer previewRenderer;
        private Renderer markRenderer;
        private LineRenderer ring;
        private Material ringMaterial;
        private MaterialPropertyBlock block;
        private SpellCaster subscribedCaster;
        private float idleTime;
        private Motion motion;
        private float motionTime;
        private Vector3 motionFrom;
        private Quaternion turnFrom;
        private Vector3 placeTarget;
        private bool placeDelivers;
        private bool hoverPlaced;
        private float hoverAngle;
        private float feedbackTime = -1f;
        private bool feedbackBlocked;
        private Vector3 feedbackDirection;
        private Vector3 feedbackSide;
        private float pulseTime = -1f;
        private float ringTime = -1f;

        /// <summary>Registers the relic on campus, finds its stand on the trial map, and hides it everywhere else.</summary>
        public static void RegisterForMap(MapDefinition map, GridWorld world)
        {
            MapObjectDefinition[] objects = map?.objects ?? Array.Empty<MapObjectDefinition>();
            MapObjectDefinition relic = Array.Find(objects, item => item != null && item.id == RelicId);
            MapObjectDefinition stand = Array.Find(objects, item => item != null && HasTag(item.tags, StandTagPrefix + RelicId));
            if (world == null || (relic == null && stand == null))
            {
                if (instance != null) instance.Enter(map?.id, world, null, null);
                return;
            }
            if (instance == null)
            {
                GameObject host = new GameObject("[Cinna Alignment Relic]");
                instance = host.AddComponent<CinnaAlignmentRelic>();
                if (Application.isPlaying) DontDestroyOnLoad(host);
            }
            instance.Enter(map.id, world, relic, stand);
        }

        /// <summary>The next Out of Alignment step for the quest hint overlay, from live progression and the held relic.</summary>
        public static string DescribeStep(WorldStateService state, SpellCaster caster)
        {
            if (CinnaIceTrialRoom.RelicDelivered()) return "Go back to Cinna Cirra.";
            return "Lift the relic beside Cinna Cirra and carry it to the stand.\n" +
                "1. Use Q to open the Spell Wheel and use the mouse to select a spell.\n" +
                "2. Select Vis.\n" +
                "3. Right click or press F to cast towards the relic next to Cinna Cirra.\n" +
                "4. Hold it and carry it to the trial area at the lower part of the campus.\n" +
                "5. Face the stand and cast again to put the relic onto the stand.";
        }

        private void OnDestroy()
        {
            Unsubscribe();
            if (ringMaterial != null) { if (Application.isPlaying) Destroy(ringMaterial); else DestroyImmediate(ringMaterial); }
            if (instance == this) instance = null;
        }

        private void Enter(string map, GridWorld world, MapObjectDefinition relic, MapObjectDefinition stand)
        {
            if (map != mapId) hoverPlaced = false;
            mapId = map;
            grid = world;
            motion = Motion.None;
            feedbackTime = pulseTime = ringTime = -1f;
            standOnMap = stand != null;
            if (standOnMap) standCell = new Vector2Int(stand.x, stand.y);
            if (relicView == null && world != null) Build(world);
            if (relicView == null) return;
            // A delivered relic has left campus for good, so it is neither registered nor blocking there.
            campusRegistered = relic != null && !CinnaIceTrialRoom.RelicDelivered();
            campusRoot.gameObject.SetActive(campusRegistered);
            ring.gameObject.SetActive(false);
            if (campusRegistered)
            {
                WorldStateService state = WorldStateService.Instance;
                Vector2Int cell = new Vector2Int(relic.x, relic.y);
                relicView.transform.position = world.CellToWorld(cell, relic.elevation);
                // Registering with an authored rotation also resets a reused view to its authored cell and facing.
                state?.RegisterObject(relic.id, relicView, cell, Vector2Int.one, relic.compatibleWithSpells, relic.tags, relic.rotation);
            }
            Advance(0f);
        }

        private void Build(GridWorld world)
        {
            block = new MaterialPropertyBlock();
            campusRoot = new GameObject("Campus Relic").transform;
            campusRoot.SetParent(transform, false);
            // The registered relic carries identity and blocking only; world state moves it between cells.
            relicView = new GameObject(RelicId);
            relicView.transform.SetParent(campusRoot, false);
            relicView.AddComponent<GridObstacle>().Configure(world, Vector2Int.one);
            restMark = Part(PrimitiveType.Cylinder, "Last Placement Mark", campusRoot, new Vector3(0.7f, 0.01f, 0.7f));
            markRenderer = restMark.GetComponent<Renderer>();

            // The visual pivots at its base, so the idle wobble tilts it on the ground.
            relicVisual = new GameObject("Relic Visual").transform;
            relicVisual.SetParent(transform, false);
            CinnaIceTrialArt art = CinnaIceTrialArt.Instance;
            if (art != null && art.relic != null) BuildModel(art.relic);
            else
            {
                GameObject stone = Part(PrimitiveType.Cube, "Relic Stone", relicVisual, new Vector3(0.5f, 0.78f, 0.5f));
                stone.transform.localPosition = new Vector3(0f, 0.39f, 0f);
                GameObject notch = Part(PrimitiveType.Cube, "Relic Notch", relicVisual, new Vector3(0.18f, 0.18f, 0.08f));
                notch.transform.localPosition = new Vector3(0f, 0.6f, 0.27f);
                notchRenderer = notch.GetComponent<Renderer>();
                relicRenderers = relicVisual.GetComponentsInChildren<Renderer>(true);
            }

            preview = Part(PrimitiveType.Cube, "Placement Preview", transform, new Vector3(0.92f, 0.02f, 0.92f)).transform;
            previewRenderer = preview.GetComponent<Renderer>();
            BuildRing();
        }

        // The model's stone base keeps its own material; only the crystal takes the state colours, so a textured model is
        // not muddied by the tints. The crystal also stands in for the primitive's glowing notch.
        private void BuildModel(GameObject model)
        {
            GameObject instance = Instantiate(model, relicVisual);
            instance.name = "Relic Model";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
            {
                if (Application.isPlaying) Destroy(collider);
                else DestroyImmediate(collider);
            }
            relicRenderers = Array.FindAll(instance.GetComponentsInChildren<Renderer>(true), IsCrystalPart);
            notchRenderer = relicRenderers.Length > 0 ? relicRenderers[0] : null;
        }

        private static bool IsCrystalPart(Renderer renderer)
        {
            for (Transform part = renderer.transform; part != null; part = part.parent)
                if (part.name == CinnaIceTrialArt.CrystalPartName) return true;
            return false;
        }

        // A flat line loop on the ground; it grows and thins out from the stand on delivery. It uses the same
        // vertex-coloured line material as the Weave cables, so it can fade without a transparent mesh material.
        private void BuildRing()
        {
            GameObject host = new GameObject("Seat Shockwave");
            host.transform.SetParent(transform, false);
            host.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            ring = host.AddComponent<LineRenderer>();
            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null) ring.sharedMaterial = ringMaterial = new Material(shader) { name = "Seat Shockwave" };
            ring.useWorldSpace = false;
            ring.loop = true;
            ring.alignment = LineAlignment.TransformZ;
            ring.positionCount = RingSegments;
            ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.receiveShadows = false;
            host.SetActive(false);
        }

        private static GameObject Part(PrimitiveType type, string name, Transform parent, Vector3 scale)
        {
            GameObject part = RuntimePrimitive.Create(type);
            part.name = name;
            Collider collider = part.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying) Destroy(collider);
                else DestroyImmediate(collider);
            }
            part.transform.SetParent(parent, false);
            part.transform.localScale = scale;
            return part;
        }

        private void Update() => Advance(Time.deltaTime);

        private void Advance(float deltaTime)
        {
            WorldStateService state = WorldStateService.Instance;
            if (relicView == null || state == null || !state.IsReady) return;
            TrackCaster();
            idleTime += deltaTime;
            bool held = subscribedCaster != null && subscribedCaster.HeldRelic == RelicId;
            bool delivered = CinnaIceTrialRoom.RelicDelivered();
            // Effects started by a landing this frame begin ticking next frame.
            bool pulseRunning = pulseTime >= 0f, ringRunning = ringTime >= 0f;

            if (motion != Motion.None) AdvanceMotion(deltaTime, held);
            else if (held) PoseHover(deltaTime);
            else if (campusRegistered) PoseAtRest(deltaTime);
            else if (standOnMap && delivered) PoseOnStand();
            relicVisual.gameObject.SetActive(motion != Motion.None || held || campusRegistered || (standOnMap && delivered));
            if (pulseRunning && pulseTime >= 0f) AdvancePulse(deltaTime);
            if (ringRunning && ringTime >= 0f) AdvanceRing(deltaTime);
            UpdateMarkers(state, held);
            if (motion == Motion.None && pulseTime < 0f) ApplyColours(held, delivered, state);
        }

        private void AdvanceMotion(float deltaTime, bool held)
        {
            motionTime += deltaTime;
            if (motion == Motion.Lift)
            {
                // A short dip, as if Vis takes the weight, then a rise to the hover in front of the player.
                if (motionTime < AnticipationDuration)
                {
                    float dip = Mathf.Sin(motionTime / AnticipationDuration * Mathf.PI * 0.5f);
                    relicVisual.SetPositionAndRotation(motionFrom + Vector3.down * (AnticipationDepth * dip), turnFrom);
                    return;
                }
                float t = Mathf.Clamp01((motionTime - AnticipationDuration) / LiftDuration);
                Vector3 from = motionFrom + Vector3.down * AnticipationDepth;
                relicVisual.SetPositionAndRotation(Vector3.LerpUnclamped(from, HoverTarget(), EaseOutCubic(t)), Quaternion.Slerp(turnFrom, HoverRotation(), t));
                if (t < 1f) return;
                motion = Motion.None;
                hoverAngle = FacingAngle();
                hoverPlaced = true;
                return;
            }
            // Place: float down onto the tile (or the stand) along a small arc.
            float p = Mathf.Clamp01(motionTime / PlaceDuration);
            relicVisual.SetPositionAndRotation(
                Vector3.LerpUnclamped(motionFrom, placeTarget, EaseOutCubic(p)) + Vector3.up * (PlaceArc * Mathf.Sin(Mathf.PI * p)),
                Quaternion.Slerp(turnFrom, Quaternion.identity, EaseOutBack(p)));
            if (p < 1f) return;
            motion = Motion.None;
            hoverPlaced = false;
            relicVisual.SetPositionAndRotation(placeTarget, Quaternion.identity);
            if (placeDelivers) { pulseTime = 0f; StartRing(); }
        }

        // Beside the player at chest height, on the camera's right, so the relic never sinks into the character's hat.
        // In front of the player at chest height, clear of the character's hat, whichever way they face.
        private Vector3 HoverTarget() => HoverPoint(PlayerPosition(), FacingAngle());

        private static Vector3 HoverPoint(Vector3 centre, float angle) =>
            centre + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * HoverReach + Vector3.up * HoverHeight;

        private Vector3 PlayerPosition() => subscribedCaster != null ? subscribedCaster.transform.position : relicView.transform.position;

        // The facing as a world yaw, so turning swings the relic around the player instead of through them.
        private float FacingAngle()
        {
            Vector2Int facing = subscribedCaster != null ? subscribedCaster.FacingDirection : Vector2Int.up;
            if (facing == Vector2Int.zero) facing = Vector2Int.up;
            Vector3 direction = grid != null ? grid.CellToWorld(facing) - grid.CellToWorld(Vector2Int.zero) : new Vector3(facing.x, 0f, facing.y);
            return Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        }

        private Quaternion HoverRotation() => Quaternion.Euler(0f, idleTime * HoverTurnDegrees, 0f);

        private void PoseHover(float deltaTime)
        {
            // The relic tracks the player's position exactly: easing it would let it trail behind a walking player and
            // drift back into the model's face. The player's own movement is already smooth, so only the swing round
            // when they turn is eased (snapped on the first held frame of a map).
            hoverAngle = hoverPlaced ? Mathf.LerpAngle(hoverAngle, FacingAngle(), 1f - Mathf.Exp(-FollowSharpness * deltaTime)) : FacingAngle();
            hoverPlaced = true;
            Vector3 position = HoverPoint(PlayerPosition(), hoverAngle) + Vector3.up * (HoverBob * Mathf.Sin(idleTime * 2.6f));
            Quaternion rotation = HoverRotation();
            ApplyFeedback(deltaTime, ref position, ref rotation);
            relicVisual.SetPositionAndRotation(position, rotation);
        }

        private void PoseAtRest(float deltaTime)
        {
            Quaternion rotation = relicView.transform.rotation *
                Quaternion.Euler(Mathf.Sin(idleTime * 1.6f) * WobbleDegrees, 0f, Mathf.Sin(idleTime * 1.1f + 1f) * WobbleDegrees * 0.8f);
            relicVisual.SetPositionAndRotation(relicView.transform.position, rotation);
        }

        private void PoseOnStand() => relicVisual.SetPositionAndRotation(StandPosition(), Quaternion.identity);

        private Vector3 StandPosition() => grid == null ? Vector3.zero : grid.CellToWorld(standCell, CinnaIceTrialRoom.StandHeight);

        // Refused placements: a shudder for the wrong place, a bump toward a blocked tile.
        private void ApplyFeedback(float deltaTime, ref Vector3 position, ref Quaternion rotation)
        {
            if (feedbackTime < 0f) return;
            feedbackTime += deltaTime;
            float u = Mathf.Clamp01(feedbackTime / (feedbackBlocked ? BumpDuration : ShudderDuration));
            if (feedbackBlocked) position += feedbackDirection * (BumpDistance * Mathf.Sin(Mathf.PI * u));
            else
            {
                float shake = Mathf.Sin(u * Mathf.PI * 8f) * (1f - u);
                position += feedbackSide * (ShudderDistance * shake);
                rotation *= Quaternion.Euler(0f, ShudderDegrees * shake, 0f);
            }
            if (u >= 1f) feedbackTime = -1f;
        }

        private void UpdateMarkers(WorldStateService state, bool held)
        {
            // The last safe placement stays blocked while the relic is carried; the mark explains the empty tile.
            bool showMark = held && campusRegistered;
            if (restMark.activeSelf != showMark) restMark.SetActive(showMark);
            if (showMark)
            {
                restMark.transform.position = relicView.transform.position + Vector3.up * 0.01f;
                SetColor(markRenderer, MarkColor);
            }
            Vector2Int cell = default;
            bool showPreview = held && motion == Motion.None && (campusRegistered || standOnMap) && grid != null &&
                subscribedCaster.TryGetPlacementCell(out cell);
            if (preview.gameObject.activeSelf != showPreview) preview.gameObject.SetActive(showPreview);
            if (!showPreview) return;
            preview.position = grid.CellToWorld(cell, 0.02f);
            SetColor(previewRenderer, subscribedCaster.CanPlaceHeldRelicAt(cell) ? ValidColor : InvalidColor);
        }

        // Follow the live caster rather than the one present when the relic was built.
        private void TrackCaster()
        {
            if (subscribedCaster != null) return;
            Unsubscribe();
            subscribedCaster = FindAnyObjectByType<SpellCaster>();
            if (subscribedCaster == null) return;
            subscribedCaster.VisLifted += OnLifted;
            subscribedCaster.VisPlaced += OnPlaced;
            subscribedCaster.VisPlaceRefused += OnPlaceRefused;
            subscribedCaster.VisHoldReleased += OnReleased;
        }

        private void Unsubscribe()
        {
            if ((object)subscribedCaster == null) return;
            subscribedCaster.VisLifted -= OnLifted;
            subscribedCaster.VisPlaced -= OnPlaced;
            subscribedCaster.VisPlaceRefused -= OnPlaceRefused;
            subscribedCaster.VisHoldReleased -= OnReleased;
            subscribedCaster = null;
        }

        private void OnLifted(string relic)
        {
            if (relic != RelicId || !relicVisual.gameObject.activeInHierarchy) return;
            StartMotion(Motion.Lift);
        }

        // World state has already moved (or the delivery flag is set); the visual still shows the hover pose.
        private void OnPlaced(string relic, Vector2Int cell, bool delivered)
        {
            if (relic != RelicId || grid == null) return;
            placeDelivers = delivered;
            placeTarget = delivered ? StandPosition() : relicView.transform.position;
            StartMotion(Motion.Place);
        }

        private void OnPlaceRefused(string relic, Vector2Int cell, bool blocked)
        {
            if (relic != RelicId || motion != Motion.None || grid == null || subscribedCaster == null) return;
            GridMover player = subscribedCaster.GetComponent<GridMover>();
            Vector3 direction = player == null ? Vector3.zero : grid.CellToWorld(cell) - grid.CellToWorld(player.CurrentCell);
            direction.y = 0f;
            feedbackDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.zero;
            feedbackSide = feedbackDirection == Vector3.zero ? Vector3.right : Vector3.Cross(Vector3.up, feedbackDirection);
            feedbackBlocked = blocked && feedbackDirection != Vector3.zero;
            feedbackTime = 0f;
        }

        private void OnReleased(string relic)
        {
            if (relic != RelicId) return;
            motion = Motion.None;
            feedbackTime = -1f;
            hoverPlaced = false;
        }

        private void StartMotion(Motion next)
        {
            motionFrom = relicVisual.position;
            turnFrom = relicVisual.rotation;
            motionTime = 0f;
            feedbackTime = -1f;
            motion = next;
        }

        private void AdvancePulse(float deltaTime)
        {
            pulseTime += deltaTime;
            float u = Mathf.Clamp01(pulseTime / PulseDuration);
            float flash = Mathf.Sin(Mathf.PI * u);
            relicVisual.localScale = Vector3.one * (1f + 0.12f * flash);
            foreach (Renderer renderer in relicRenderers) SetColor(renderer, Color.Lerp(StandColor, Color.white, 0.5f * flash));
            if (u < 1f) return;
            pulseTime = -1f;
            relicVisual.localScale = Vector3.one;
        }

        private void StartRing()
        {
            ringTime = 0f;
            ring.transform.position = grid.CellToWorld(standCell, 0.05f);
            ring.gameObject.SetActive(true);
            PoseRing(0f);
        }

        private void AdvanceRing(float deltaTime)
        {
            ringTime += deltaTime;
            float u = Mathf.Clamp01(ringTime / RingDuration);
            PoseRing(u);
            if (u < 1f) return;
            ringTime = -1f;
            ring.gameObject.SetActive(false);
        }

        private void PoseRing(float u)
        {
            float radius = Mathf.Lerp(RingStartRadius, RingEndRadius, EaseOutCubic(u));
            for (int i = 0; i < RingSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / RingSegments;
                ring.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
            }
            ring.widthMultiplier = RingWidth * (1f - u);
            Color color = Color.Lerp(StandColor, Color.white, 0.4f);
            color.a = 1f - u;
            ring.startColor = ring.endColor = color;
        }

        private void ApplyColours(bool held, bool delivered, WorldStateService state)
        {
            Color body = held ? HeldColor : campusRegistered ? RestColor :
                state.TryGetObject(CinnaIceTrialRoom.RelicId, out WorldObjectSnapshot frost) && frost.frozen ? FrozenColor : StandColor;
            foreach (Renderer renderer in relicRenderers) SetColor(renderer, body);
            // At rest the notch glows faintly, inviting a Vis cast.
            if (!held && campusRegistered) SetColor(notchRenderer, Color.Lerp(RestColor, NotchGlowColor, 0.3f * (1f + Mathf.Sin(idleTime * 2.2f))));
        }

        private static bool HasTag(string[] tags, string tag) =>
            Array.Exists(tags ?? Array.Empty<string>(), value => string.Equals(value, tag, StringComparison.OrdinalIgnoreCase));
        private static float EaseOutCubic(float t) { float inverse = 1f - t; return 1f - inverse * inverse * inverse; }
        // Overshoots slightly past the target before settling, so setting it down reads as a click.
        private static float EaseOutBack(float t) { const float c1 = 1.70158f, c3 = c1 + 1f; float s = t - 1f; return 1f + c3 * s * s * s + c1 * s * s; }

        // A property block tints the shared primitive material without instancing one per renderer.
        private void SetColor(Renderer renderer, Color color)
        {
            if (renderer == null) return;
            renderer.GetPropertyBlock(block);
            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);
            // Only the art model's crystal material has emission enabled; on the primitives this does nothing.
            block.SetColor("_EmissionColor", color * CrystalGlow);
            renderer.SetPropertyBlock(block);
        }
    }
}
