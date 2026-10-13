using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Lumenfall.Controls;
using Lumenfall.DataDriven;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Inventory;
using Lumenfall.Spells;
using Lumenfall.Startup;

namespace Lumenfall.Quests
{
    /// <summary>
    /// Builds Cinna Cirra's ice trial (<c>C03.IceTrial</c>) for Cold Feet, Warm Idea from its area definition. Cinna is
    /// spawned from their catalog record and teaches Glaciate. The frost relic is the orientation relic the player carried
    /// in with Vis during Out of Alignment and set on the pedestal; <see cref="CinnaAlignmentRelic"/> draws it, and it only
    /// takes Glaciate once delivered. Casting Glaciate on it freezes the pool: its water stops blocking movement and every pool cell except the switches becomes GridWorld ice, so the
    /// player slides until they land on a switch or a snow patch, or stop in front of a crystal pillar or the south wall.
    /// Landing on a switch toggles it. The west, north and east edges of the pool are open: a slide that carries the
    /// player off one drops them into the chasm, and they fall, fade out and reappear at the pool entrance with every
    /// switch dark. With all nine lit, landing on the seal on the east ledge lights it, which completes the quest through
    /// its monitor. R resets the switches and returns the player to the pool entrance; the pool stays frozen. A second Glaciate on the
    /// relic thaws the pool again, moving the player out to the entrance if they are standing in it. The component sits on
    /// the area prefab root, so registration runs inside the loader's map rebuild and saved state restores normally.
    /// The floor, walls, pool, pedestal and seal crystal use <see cref="CinnaIceTrialArt"/> when it has been built and fall
    /// back to coloured primitives otherwise; the static dressing (crystals, columns, braziers, snowfall) is baked into the
    /// area prefab. With the full art the room starts as a thawed stone hall and <see cref="CinnaIceTrialAmbience"/> freezes
    /// it from the relic outward, and <see cref="CinnaIceTrialSwitchVisuals"/> draws the switches as rune plates whose
    /// beams converge on the seal crystal.
    /// </summary>
    public sealed class CinnaIceTrialRoom : MonoBehaviour
    {
        public const string MapId = "C03.IceTrial";
        public const string ReturnPortalId = "P.ICETRIAL.CAMPUS";
        public const string QuestId = "cinna_cold_feet_warm_idea";
        public const string RelicId = "relic.cinna.frost";
        public const string FinalSwitchId = "switch.cinna.ice.final";
        public const string SwitchTag = "ice-switch";
        public const string FinalTag = "ice-final";
        public const string PoolType = "ice-pool";
        public const string EntranceType = "ice-entrance";
        /// <summary>A crystal pillar inside the pool: it blocks, so slides stop in front of it.</summary>
        public const string PillarType = "ice-pillar";
        /// <summary>A snow patch inside the pool: plain ground, so slides stop on it without toggling anything.</summary>
        public const string StopType = "ice-stop";
        /// <summary>An open edge over the chasm: stepping or sliding onto it is a fall.</summary>
        public const string DropType = "ice-drop";
        /// <summary>Height of the pedestal top, where the delivered relic stands.</summary>
        public const float StandHeight = 0.6f;
        private const string TrialNpcId = "cinna_cirra_trial";
        private const string LessonFlag = "spell.lesson.Glaciate";
        private const string LearnedFlag = "spell.unlocked.Glaciate";
        private static readonly Color FloorColor = new Color(0.34f, 0.38f, 0.45f);
        private static readonly Color WallColor = new Color(0.58f, 0.7f, 0.82f);
        private static readonly Color WaterColor = new Color(0.1f, 0.3f, 0.55f);
        private static readonly Color IceColor = new Color(0.74f, 0.9f, 0.98f);
        private static readonly Color SwitchOffColor = new Color(0.22f, 0.27f, 0.36f);
        private static readonly Color SwitchOnColor = new Color(0.4f, 0.95f, 1f);
        private static readonly Color SealClosedColor = new Color(0.32f, 0.36f, 0.48f);
        private static readonly Color SealOpenColor = new Color(1f, 0.84f, 0.38f);
        // The seal crystal glows faintly while closed so it reads as ice rather than a dark rock.
        private static readonly Color SealCrystalClosedColor = new Color(0.36f, 0.5f, 0.68f);
        // Where the switch beams meet on the seal crystal, near its tip so they read clearly above the pool.
        private const float SealBeamHeight = 1.25f;
        // The south wall is a low parapet so the camera, which looks over it from the lobby, can see the pool row behind it.
        private const float WallHeight = 0.4f;
        // The blended room's pool is sunk into a basin: water just below the floor, ice slightly above the water.
        private const float BasinDepth = 0.4f;
        private const float WaterSurfaceHeight = -0.09f;
        private const float IceSurfaceHeight = -0.05f;
        // The room is a platform over the chasm; its rock reaches this far down.
        private const float PlatformDepth = 6f;
        private const float FallSeconds = 0.55f;
        private const float FallDistance = 3.2f;
        private const float FallHoldSeconds = 0.25f;
        private const float FallFadeInSeconds = 0.45f;
        private static readonly Color RelicBaseColor = new Color(0.5f, 0.5f, 0.56f);
        private static readonly Color StoneColor = new Color(0.55f, 0.55f, 0.6f);
        private static readonly Color PortalColor = new Color(0.62f, 0.86f, 1f);

        [Tooltip("Animator controller for Cinna's model, matching their campus placement.")]
        [SerializeField] private RuntimeAnimatorController cinnaAnimator;

        private readonly Dictionary<Vector2Int, string> switchCells = new Dictionary<Vector2Int, string>();
        private readonly Dictionary<string, GameObject> registeredViews = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        private readonly Dictionary<string, Renderer> switchRenderers = new Dictionary<string, Renderer>(StringComparer.Ordinal);
        private readonly List<Renderer> sealCrystalRenderers = new List<Renderer>();
        private readonly List<string> switchIds = new List<string>();
        private readonly List<RectInt> wallAreas = new List<RectInt>();
        private readonly List<Vector2Int> iceCells = new List<Vector2Int>();
        private readonly HashSet<Vector2Int> dropCells = new HashSet<Vector2Int>();
        private readonly HashSet<Vector2Int> pillarCells = new HashSet<Vector2Int>();
        private readonly HashSet<Vector2Int> stopCells = new HashSet<Vector2Int>();
        private CinnaIceTrialArt art;
        private CinnaIceTrialAmbience ambience;
        private CinnaIceTrialSwitchVisuals switchVisuals;
        private CinnaIceTrialEffects effects;
        private int worldWidth;
        private int worldHeight;
        private Transform geometry;
        private GameObject portalObject;
        private Transform ring;
        private Renderer poolRenderer;
        private GridObstacle water;
        private Vector2Int finalCell;
        private Vector2Int entranceCell;
        private RectInt poolArea;
        private GridWorld world;
        private GridMover subscribedPlayer;
        private WorldStateService subscribedState;
        private bool frozenApplied;
        private string feedback;
        private float feedbackUntil;
        private float fallTime = -1f;
        private bool fallReturned;
        private Vector3 fallStart;
        private float fadeAlpha;

        private void OnEnable()
        {
            DataDrivenWorldLoader loader = DataDrivenWorldLoader.Instance;
            MapDefinition map = loader == null ? null : loader.LoadedMap;
            if (map == null || !string.Equals(map.id, MapId, StringComparison.OrdinalIgnoreCase)) return;
            world = FindAnyObjectByType<GridWorld>();
            if (world == null) return;
            if (geometry == null) Build(map, loader.CurrentArea, loader.LoadedNpcs);
            RegisterTrialObjects(map);
            Subscribe();
            ApplyState(false);
        }

        private void OnDisable()
        {
            if (subscribedState != null) { subscribedState.StateChanged -= OnStateChanged; subscribedState.MapRebuilt -= OnMapRebuilt; }
            subscribedState = null;
            FinishFall();
            if (subscribedPlayer != null) subscribedPlayer.MoveCompleted -= OnPlayerMoveCompleted;
            subscribedPlayer = null;
            // The GridWorld is shared by every area, so the pool's ice must not outlive this one.
            SetIce(false);
        }

        private void OnDestroy()
        {
        }

        private void Update()
        {
            if (geometry == null) return;
            Subscribe();
            if (ring != null && portalObject != null && portalObject.activeSelf) ring.Rotate(0f, 85f * Time.unscaledDeltaTime, 0f, Space.Self);
            AdvanceFall(Time.deltaTime);
            // A player left standing over the chasm (a save made mid-fall, for one) falls as soon as they are idle.
            if (fallTime < 0f && subscribedPlayer != null && !subscribedPlayer.IsMoving && dropCells.Contains(subscribedPlayer.CurrentCell))
                StartFall(subscribedPlayer);
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame) ResetTrial();
        }

        /// <summary>True from the moment the player drops off an edge until the fade back in at the entrance ends.</summary>
        public bool IsFalling => fallTime >= 0f;

        private void StartFall(GridMover player)
        {
            if (fallTime >= 0f || player == null) return;
            fallTime = 0f;
            fallReturned = false;
            fallStart = player.transform.position;
            player.SetMovementLocked(true);
            if (effects != null) effects.PlayFall(fallStart);
        }

        // The player drops out of sight with the screen fading to black, comes back at the entrance with the switches
        // dark, and the screen fades back in. Dialogue and menus also lock movement, so the lock is reasserted each frame.
        private void AdvanceFall(float deltaTime)
        {
            if (fallTime < 0f) return;
            if (subscribedPlayer == null) { FinishFall(); return; }
            subscribedPlayer.SetMovementLocked(true);
            fallTime += deltaTime;
            if (!fallReturned)
            {
                float u = Mathf.Clamp01(fallTime / FallSeconds);
                subscribedPlayer.transform.position = fallStart + Vector3.down * (FallDistance * u * u);
                fadeAlpha = u;
                if (u < 1f) return;
                ReturnFromFall();
                return;
            }
            if (fallTime < FallHoldSeconds) { fadeAlpha = 1f; return; }
            fadeAlpha = 1f - Mathf.Clamp01((fallTime - FallHoldSeconds) / FallFadeInSeconds);
            if (fadeAlpha > 0f) return;
            fallTime = -1f;
            subscribedPlayer.SetMovementLocked(false);
        }

        private void ReturnFromFall()
        {
            fallReturned = true;
            fallTime = 0f;
            WorldStateService state = WorldStateService.Instance;
            // Once the seal is open the trial is done, so a fall only sends the player back.
            bool sealOpen = state != null && state.TryGetObject(FinalSwitchId, out WorldObjectSnapshot seal) && seal.powered;
            if (!sealOpen) ResetSwitches(state);
            if (subscribedPlayer != null) subscribedPlayer.TryTeleportTo(world, entranceCell);
            ShowFeedback(sealOpen ? "You slip into the chasm and climb back to the entrance." : "You slip into the chasm. Every switch goes dark.");
        }

        // Leaving the room mid-fall still lands the player back at the entrance with the controls released.
        private void FinishFall()
        {
            if (fallTime < 0f) return;
            if (!fallReturned) ReturnFromFall();
            fallTime = -1f;
            fadeAlpha = 0f;
            if (subscribedPlayer != null) subscribedPlayer.SetMovementLocked(false);
        }

        // The relic and the switches are registered each time the area activates so the loader's pending save
        // restore applies to them; walls, water and Cinna only need their GridObstacles.
        private void RegisterTrialObjects(MapDefinition map)
        {
            WorldStateService state = WorldStateService.Instance;
            if (state == null) return;
            foreach (MapObjectDefinition item in map.objects ?? Array.Empty<MapObjectDefinition>())
                if (item != null && registeredViews.TryGetValue(item.id ?? string.Empty, out GameObject view))
                    state.RegisterObject(item.id, view, new Vector2Int(item.x, item.y), new Vector2Int(Mathf.Max(1, item.width), Mathf.Max(1, item.height)),
                        item.compatibleWithSpells && (item.id != RelicId || RelicDelivered()), item.tags);
        }

        /// <summary>True once the orientation relic has been carried in and set on the pedestal.</summary>
        public static bool RelicDelivered()
        {
            GameEventRuntime runtime = GameEventManager.Instance?.Runtime;
            return runtime != null && runtime.GetFlag(SpellCaster.VisDeliveredFlagPrefix + CinnaAlignmentRelic.RelicId);
        }

        private void Subscribe()
        {
            WorldStateService state = WorldStateService.Instance;
            if (state != null && state != subscribedState)
            {
                if (subscribedState != null) { subscribedState.StateChanged -= OnStateChanged; subscribedState.MapRebuilt -= OnMapRebuilt; }
                subscribedState = state;
                state.StateChanged += OnStateChanged;
                state.MapRebuilt += OnMapRebuilt;
            }

            GridPlayerInput input = FindAnyObjectByType<GridPlayerInput>();
            GridMover player = input == null ? null : input.GetComponent<GridMover>();
            if (player == null || player == subscribedPlayer) return;
            if (subscribedPlayer != null) subscribedPlayer.MoveCompleted -= OnPlayerMoveCompleted;
            subscribedPlayer = player;
            player.MoveCompleted += OnPlayerMoveCompleted;
            if (effects != null) effects.TrackPlayer(player);
        }

        // MapRebuilt runs after a save restore and before the rebuild's StateChanged, so a pool that was already frozen
        // is applied silently and only a live Glaciate cast announces the freeze.
        private void OnMapRebuilt(MapRebuildEvent rebuild) { ApplyState(false); }

        private void OnStateChanged(MapStateSnapshot snapshot) { ApplyState(true); }

        private void ApplyState(bool announce)
        {
            WorldStateService state = WorldStateService.Instance;
            if (state == null || geometry == null) return;
            // A save restore applies its state through the same change event as a live cast; show it as already settled.
            if (GameSaveManager.Instance != null && GameSaveManager.Instance.IsRestoring) announce = false;
            bool frozen = state.TryGetObject(RelicId, out WorldObjectSnapshot relic) && relic.frozen;
            if (announce && frozen && !frozenApplied) ShowFeedback("The pool freezes over. Step onto the ice from the gap in the south wall.");
            // A thaw puts the water back; anyone still in the pool steps out to the entrance first so they are not left
            // standing inside the water's blocker.
            if (!frozen && frozenApplied)
            {
                bool movedOut = subscribedPlayer != null && poolArea.Contains(subscribedPlayer.CurrentCell) && subscribedPlayer.TryTeleportTo(world, entranceCell);
                if (announce) ShowFeedback(movedOut ? "The pool thaws, and you step back out to the entrance." : "The pool thaws back to water.");
            }
            frozenApplied = frozen;
            if (water != null && water.enabled == frozen) water.enabled = !frozen;
            SetIce(frozen);
            // With the blended art the pool stays water underneath and the ambience spreads ice tiles over it.
            if (ambience != null) ambience.SetFrozen(frozen, announce);
            else if (poolRenderer != null)
            {
                if (art != null && art.waterMaterial != null && art.iceMaterial != null) poolRenderer.sharedMaterial = frozen ? art.iceMaterial : art.waterMaterial;
                else poolRenderer.material.color = frozen ? IceColor : WaterColor;
            }
            // Delivery happens while the room is loaded, so the relic starts taking Glaciate as soon as it lands.
            if (relic != null && !relic.spellCompatible && RelicDelivered()) state.SetProperty(RelicId, x => x.spellCompatible = true);
            if (switchVisuals != null)
            {
                int lit = 0, total = 0;
                bool open = false;
                foreach (string id in switchIds)
                {
                    bool on = state.TryGetObject(id, out WorldObjectSnapshot item) && item.powered;
                    if (id == FinalSwitchId) open = on;
                    else { total++; if (on) lit++; }
                    switchVisuals.SetLit(id, on, announce);
                }
                switchVisuals.SetSeal(lit, total, open, announce);
                return;
            }
            foreach (KeyValuePair<string, Renderer> pair in switchRenderers)
            {
                bool lit = state.TryGetObject(pair.Key, out WorldObjectSnapshot item) && item.powered;
                bool final = pair.Key == FinalSwitchId;
                SetColor(pair.Value, lit ? (final ? SealOpenColor : SwitchOnColor) : (final ? SealClosedColor : SwitchOffColor), lit);
                if (final) foreach (Renderer crystal in sealCrystalRenderers) SetColor(crystal, lit ? SealOpenColor : SealCrystalClosedColor, true);
            }
        }

        private void SetIce(bool frozen)
        {
            if (world == null) return;
            foreach (Vector2Int cell in iceCells) world.SetIce(cell, frozen);
        }

        private void OnPlayerMoveCompleted(GridMover player, Vector2Int cell)
        {
            if (dropCells.Contains(cell)) { StartFall(player); return; }
            string landed = switchCells.TryGetValue(cell, out string switchId) ? switchId : cell == finalCell ? FinalSwitchId : null;
            string message = Land(WorldStateService.Instance, landed);
            if (message != null) ShowFeedback(message);
        }

        /// <summary>
        /// Applies the trial rules when the player stops on a switch or the seal (<paramref name="landedId"/>), and
        /// returns the feedback line, or null when nothing applies. A switch toggles while the pool is frozen and the
        /// seal is closed; the seal opens only when every switch is lit, and the switches then hold until a reset.
        /// </summary>
        public static string Land(WorldStateService state, string landedId)
        {
            if (landedId == null || state == null || !state.IsReady || !string.Equals(state.CurrentMapId, MapId, StringComparison.OrdinalIgnoreCase) ||
                !state.TryGetObject(RelicId, out WorldObjectSnapshot relic) || !relic.frozen) return null;
            bool sealOpen = state.TryGetObject(FinalSwitchId, out WorldObjectSnapshot seal) && seal.powered;
            int litCount, total;
            if (landedId == FinalSwitchId)
            {
                CountSwitches(state, out litCount, out total);
                if (total == 0 || litCount < total) return $"The seal stays cold: {litCount} of {total} switches lit.";
                if (!sealOpen) state.SetProperty(FinalSwitchId, x => x.powered = true);
                return "The seal blazes open. The trial is complete.";
            }
            if (sealOpen || !state.TryGetObject(landedId, out WorldObjectSnapshot item) || !HasTag(item, SwitchTag)) return null;
            bool lit = !item.powered;
            state.SetProperty(landedId, x => x.powered = lit);
            CountSwitches(state, out litCount, out total);
            return lit ? $"Switch lit ({litCount} of {total})." : $"Switch dimmed ({litCount} of {total} lit).";
        }

        /// <summary>Turns every switch and the seal off and returns the player to the pool entrance.</summary>
        public bool ResetTrial()
        {
            WorldStateService state = WorldStateService.Instance;
            GameEventRuntime runtime = GameEventManager.Instance?.Runtime;
            if (Time.timeScale <= 0f || InventoryUiState.IsOpen || runtime?.ActiveDialogue != null || subscribedPlayer == null ||
                subscribedPlayer.IsMoving || IsFalling || !ResetSwitches(state)) return false;
            subscribedPlayer.TryTeleportTo(world, entranceCell);
            ShowFeedback("Trial reset. Every switch is dark again.");
            return true;
        }

        /// <summary>Turns every switch and the seal off; the pool stays frozen. False when the trial is not frozen.</summary>
        public static bool ResetSwitches(WorldStateService state)
        {
            if (state == null || !state.IsReady || !string.Equals(state.CurrentMapId, MapId, StringComparison.OrdinalIgnoreCase) ||
                !state.TryGetObject(RelicId, out WorldObjectSnapshot relic) || !relic.frozen) return false;
            foreach (WorldObjectSnapshot item in state.SnapshotObjects())
                if ((HasTag(item, SwitchTag) || HasTag(item, FinalTag)) && item.powered) state.SetProperty(item.id, x => x.powered = false);
            return true;
        }

        /// <summary>
        /// The next step of the trial for the quest hint overlay, read only from live world state so it works without
        /// a room instance.
        /// </summary>
        public static string DescribeStep(WorldStateService state)
        {
            if (state == null || !state.TryGetObject(RelicId, out WorldObjectSnapshot relic)) return "Enter Cinna's ice trial.";
            if (!relic.frozen) return "Cast Glaciate on the relic on its stand beside the pool to freeze it.";
            if (state.TryGetObject(FinalSwitchId, out WorldObjectSnapshot seal) && seal.powered) return "The seal is open. Talk to Cinna Cirra.";
            CountSwitches(state, out int lit, out int total);
            return lit == total
                ? "Every switch is lit. Slide into the seal on the east ledge."
                : $"Light every switch: {lit} of {total} lit. Landing on a lit switch dims it, and sliding off an edge drops you back to the start. Press R to reset.";
        }

        private static void CountSwitches(WorldStateService state, out int lit, out int total)
        {
            lit = 0; total = 0;
            foreach (WorldObjectSnapshot item in state.SnapshotObjects())
            {
                if (!HasTag(item, SwitchTag)) continue;
                total++;
                if (item.powered) lit++;
            }
        }

        private static bool HasTag(WorldObjectSnapshot item, string tag) =>
            item != null && Array.Exists(item.tags ?? Array.Empty<string>(), value => string.Equals(value, tag, StringComparison.OrdinalIgnoreCase));

        private static bool HasTag(MapObjectDefinition item, string tag) =>
            item != null && Array.Exists(item.tags ?? Array.Empty<string>(), value => string.Equals(value, tag, StringComparison.OrdinalIgnoreCase));

        private void ShowFeedback(string message) { feedback = message; feedbackUntil = Time.unscaledTime + 4f; }

        private void OnGUI()
        {
            if (fadeAlpha > 0f)
            {
                GUI.depth = -1000;
                Color previous = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, fadeAlpha);
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = previous;
            }
            if (geometry == null || Time.timeScale <= 0f || InventoryUiState.IsOpen) return;
            GameEventRuntime runtime = GameEventManager.Instance?.Runtime;
            if (runtime == null || runtime.ActiveDialogue != null) return;
            WorldStateService state = WorldStateService.Instance;
            if (state == null || !string.Equals(state.CurrentMapId, MapId, StringComparison.OrdinalIgnoreCase)) return;
            // Above the spell feedback message, which sits at the bottom of the screen.
            if (Time.unscaledTime < feedbackUntil && !string.IsNullOrEmpty(feedback)) HudSkin.Message(feedback, 82f);
            if (!runtime.GetFlag(LessonFlag) && !runtime.GetFlag(LearnedFlag)) return;

            string body;
            if (!state.TryGetObject(RelicId, out WorldObjectSnapshot relic) || !relic.frozen)
                body = $"1. Press {KeyBindings.GetKeyLabel(GameAction.Spell5)} to select Glaciate.\n" +
                       "2. Stand beside the relic on its stand and right-click (or press " + KeyBindings.GetKeyLabel(GameAction.Interact) + ") to freeze the pool.";
            else
                body = DescribeStep(state) + "\nSlides stop on switches and snow patches, or in front of crystal pillars and the south wall. " +
                       "The other edges drop into the chasm. Cast Glaciate on the relic again to thaw the pool.";
            // Top-right, below the Settings button; the quest hint card has the top-left.
            HudSkin.SidePanel("Ice Trial", body);
        }

        private void Build(MapDefinition map, WorldAreaAsset area, NpcDatabaseDefinition npcs)
        {
            geometry = new GameObject("Ice Trial Geometry").transform;
            geometry.SetParent(transform, false);
            art = CinnaIceTrialArt.Instance;
            worldWidth = map.width;
            worldHeight = map.height;
            // With the full art the room starts as a stone hall and the ambience freezes it from the relic outward.
            if (art != null && art.thawedFloorMaterial != null && art.thawedWallMaterial != null && art.basinMaterial != null &&
                art.wallMaterial != null && art.snowSurfaceMaterial != null && art.iceSurfaceMaterial != null && art.switchBaseMaterial != null)
            {
                ambience = geometry.gameObject.AddComponent<CinnaIceTrialAmbience>();
                MapObjectDefinition relicItem = Array.Find(map.objects ?? Array.Empty<MapObjectDefinition>(), item => item != null && item.id == RelicId);
                ambience.Configure(world, relicItem == null ? Vector2Int.zero : new Vector2Int(relicItem.x, relicItem.y));
                effects = geometry.gameObject.AddComponent<CinnaIceTrialEffects>();
                effects.Configure(ambience, ambience.Origin, art.sparkleMaterial, art.mistMaterial);
            }
            if (art != null && art.runeMaterial != null && art.switchBaseMaterial != null)
                switchVisuals = geometry.gameObject.AddComponent<CinnaIceTrialSwitchVisuals>();
            float size = world.CellSize;
            if (ambience == null)
            {
                Vector3 centre = (world.CellToWorld(Vector2Int.zero) + world.CellToWorld(new Vector2Int(map.width - 1, map.height - 1))) * 0.5f;
                GameObject floor = Part(PrimitiveType.Cube, "Floor", centre + Vector3.down * 0.05f, new Vector3(map.width * size, 0.1f, map.height * size), FloorColor);
                Skin(floor, art == null ? null : art.floorMaterial, new Vector2(map.width, map.height) / 3f);
            }

            List<MapObjectDefinition> switches = new List<MapObjectDefinition>();
            foreach (MapObjectDefinition item in map.objects ?? Array.Empty<MapObjectDefinition>())
            {
                if (item == null) continue;
                Vector2Int cell = new Vector2Int(item.x, item.y);
                Vector2Int footprint = new Vector2Int(Mathf.Max(1, item.width), Mathf.Max(1, item.height));
                if (string.Equals(item.type, PoolType, StringComparison.OrdinalIgnoreCase)) BuildPool(item.id, cell, footprint, size);
                else if (string.Equals(item.type, EntranceType, StringComparison.OrdinalIgnoreCase)) entranceCell = cell;
                else if (HasTag(item, SwitchTag) || HasTag(item, FinalTag)) switches.Add(item);
                else if (string.Equals(item.id, RelicId, StringComparison.Ordinal)) BuildRelic(item.id, cell, size);
                else if (string.Equals(item.type, PillarType, StringComparison.OrdinalIgnoreCase)) BuildPillar(item.id, cell, size);
                else if (string.Equals(item.type, StopType, StringComparison.OrdinalIgnoreCase)) BuildStopTile(item.id, cell, size);
                else if (string.Equals(item.type, DropType, StringComparison.OrdinalIgnoreCase))
                {
                    for (int x = 0; x < footprint.x; x++)
                        for (int y = 0; y < footprint.y; y++)
                            dropCells.Add(cell + new Vector2Int(x, y));
                }
                else if (item.blocks) BuildWall(item.id, cell, footprint, size);
            }
            // Pillars and snow patches sit inside the pool but are not ice.
            iceCells.RemoveAll(cell => pillarCells.Contains(cell) || stopCells.Contains(cell));
            // Switch cells are normal ground inside the pool, so slides stop on them.
            foreach (MapObjectDefinition item in switches)
            {
                Vector2Int cell = new Vector2Int(item.x, item.y);
                bool final = HasTag(item, FinalTag);
                if (final) finalCell = cell;
                else switchCells[cell] = item.id;
                iceCells.Remove(cell);
                BuildSwitch(item.id, cell, size, final);
            }
            if (ambience != null)
            {
                BuildBlendedFloor(map, size);
                foreach (RectInt wall in wallAreas) DressWall(wall.position, wall.size, size);
                ambience.BindDressing(transform.Find(CinnaIceTrialArt.DressingRootName));
            }

            BuildCinna(npcs);

            PortalContentAsset content = FindPortal(area, ReturnPortalId);
            if (content == null) { Debug.LogError($"Missing authored portal content '{ReturnPortalId}'.", this); return; }
            // A GridPortal's cell is its transform position; the landing stone sits on the trial spawn cell.
            Vector2Int landingCell = map.spawn != null ? new Vector2Int(map.spawn.x, map.spawn.y) : new Vector2Int(5, 0);
            Part(PrimitiveType.Cylinder, "Landing Stone", world.CellToWorld(landingCell, 0.02f), new Vector3(0.9f * size, 0.02f, 0.9f * size), StoneColor);
            // Built active inside the map rebuild so the loader finds it and suppresses re-entry on arrival.
            portalObject = new GameObject("Ice Trial Return Portal");
            portalObject.transform.SetParent(geometry, false);
            portalObject.transform.position = world.CellToWorld(landingCell, 0.03f);
            GridPortal portal = portalObject.AddComponent<GridPortal>();
            portal.Configure(content, world);
            GameObject visual = Part(PrimitiveType.Cylinder, "Return portal ring", Vector3.zero, new Vector3(0.82f * size, 0.06f, 0.82f * size), PortalColor, true);
            visual.transform.SetParent(portalObject.transform, false);
            visual.transform.localPosition = new Vector3(0f, 0.08f, 0f);
            ring = visual.transform;
        }

        private void BuildPool(string id, Vector2Int cell, Vector2Int footprint, float size)
        {
            Vector3 origin = world.CellToWorld(cell);
            Vector3 centre = origin + new Vector3((footprint.x - 1) * 0.5f * size, 0f, (footprint.y - 1) * 0.5f * size);
            // With the blended art the water sits down in the basin, below the floor, and its ripples drift.
            float waterTop = ambience != null ? WaterSurfaceHeight : 0.03f;
            GameObject pool = Part(PrimitiveType.Cube, id, centre + Vector3.up * (waterTop - 0.02f), new Vector3(footprint.x * size, 0.04f, footprint.y * size), WaterColor);
            Vector2 tiling = new Vector2(footprint.x, footprint.y) / 3f;
            Skin(pool, art == null ? null : art.waterMaterial, tiling);
            poolRenderer = pool.GetComponent<Renderer>();
            if (ambience != null) ambience.AddWater(poolRenderer, tiling);
            poolArea = new RectInt(cell, footprint);
            for (int x = 0; x < footprint.x; x++)
                for (int y = 0; y < footprint.y; y++)
                    iceCells.Add(cell + new Vector2Int(x, y));
            // Open water blocks the whole pool, switches included, until the relic is frozen.
            GameObject blocker = new GameObject(id + " water");
            blocker.transform.SetParent(geometry, false);
            blocker.transform.position = origin;
            water = blocker.AddComponent<GridObstacle>();
            water.Configure(world, footprint);
        }

        private void BuildWall(string id, Vector2Int cell, Vector2Int footprint, float size)
        {
            Vector3 origin = world.CellToWorld(cell);
            Vector3 centre = origin + new Vector3((footprint.x - 1) * 0.5f * size, 0f, (footprint.y - 1) * 0.5f * size);
            // With the blended art the walls reach down into the basin, so they line the sunken pool too.
            float bottom = ambience != null ? -BasinDepth : 0f;
            float height = WallHeight - bottom;
            GameObject wall = Part(PrimitiveType.Cube, id, centre + Vector3.up * (bottom + height * 0.5f), new Vector3(footprint.x * size, height, footprint.y * size), WallColor);
            // Each cube face maps the texture once, so the long faces repeat it about once per cell.
            Vector2 tiling = new Vector2(Mathf.Max(footprint.x, footprint.y), height);
            Skin(wall, art == null ? null : ambience != null ? art.thawedWallMaterial : art.wallMaterial, tiling);
            wallAreas.Add(new RectInt(cell, footprint));
            if (ambience != null)
            {
                // A slightly larger ice shell that climbs the stone wall from its base as the frost reaches it.
                Vector3 extent = new Vector3(footprint.x * size, 0f, footprint.y * size) * 0.5f;
                GameObject shell = Part(PrimitiveType.Cube, id + " ice", centre + Vector3.up * (bottom + height * 0.5f + 0.01f),
                    new Vector3(footprint.x * size + 0.05f, height + 0.02f, footprint.y * size + 0.05f), WallColor);
                Skin(shell, art.wallMaterial, tiling);
                Vector3 relicPoint = ambience.Origin;
                Vector3 nearest = new Vector3(Mathf.Clamp(relicPoint.x, centre.x - extent.x, centre.x + extent.x), centre.y,
                    Mathf.Clamp(relicPoint.z, centre.z - extent.z, centre.z + extent.z));
                ambience.AddWallShell(shell.transform, nearest);
            }
            GameObject blocker = new GameObject(id + " obstacle");
            blocker.transform.SetParent(geometry, false);
            blocker.transform.position = origin;
            blocker.AddComponent<GridObstacle>().Configure(world, footprint);
        }

        private void BuildSwitch(string id, Vector2Int cell, float size, bool final)
        {
            GameObject root = new GameObject(id);
            root.transform.SetParent(geometry, false);
            root.transform.position = world.CellToWorld(cell);
            registeredViews[id] = root;
            switchIds.Add(id);
            if (switchVisuals != null) switchVisuals.AddSwitch(id, root.transform, final, art, size);
            else
            {
                float plate = final ? 0.9f : 0.7f;
                GameObject view = Part(PrimitiveType.Cube, id + " plate", Vector3.zero, new Vector3(plate * size, 0.08f, plate * size), final ? SealClosedColor : SwitchOffColor);
                view.transform.SetParent(root.transform, false);
                view.transform.localPosition = new Vector3(0f, 0.06f, 0f);
                switchRenderers[id] = view.GetComponent<Renderer>();
            }
            // The seal sits in a gap in the east wall; its crystal stands just outside, seen through the gap.
            if (!final || art == null || art.sealCrystal == null) return;
            GameObject crystal = Instantiate(art.sealCrystal, geometry);
            crystal.name = id + " crystal";
            crystal.transform.position = world.CellToWorld(cell + Vector2Int.right);
            if (switchVisuals != null) switchVisuals.SetSealCrystal(crystal, SealBeamHeight);
            else sealCrystalRenderers.AddRange(crystal.GetComponentsInChildren<Renderer>(true));
        }

        // The blended room is a stone platform over the chasm: floor slabs everywhere that is neither pool nor open edge,
        // a sunken basin for the pool, and rock under all of it reaching down into the dark. A frost dusting covers each
        // slab and an ice sheet the pool; both use the Ice Trial Frost shader, which reveals them from the relic outward as
        // the ambience's front passes. Icicles hang from the pool's open edges once it freezes.
        private void BuildBlendedFloor(MapDefinition map, float size)
        {
            Transform parent = new GameObject("Blended Floor").transform;
            parent.SetParent(geometry, false);
            List<RectInt> slabs = FloorRects(map);
            // The seal crystal stands just past the seal, so give it a ledge to stand on.
            if (finalCell != default) slabs.Add(new RectInt(finalCell + Vector2Int.right, Vector2Int.one));
            foreach (RectInt slab in slabs)
            {
                Vector3 centre = RectCentre(slab, size);
                GameObject floor = Part(PrimitiveType.Cube, $"Floor {slab.x},{slab.y}", centre + Vector3.down * 0.05f,
                    new Vector3(slab.width * size, 0.1f, slab.height * size), FloorColor);
                floor.transform.SetParent(parent, true);
                // Offsetting each slab by its corner keeps the stone pattern continuous across slabs.
                Skin(floor, art.thawedFloorMaterial, new Vector2(slab.width, slab.height) / 3f, new Vector2(slab.x, slab.y) / 3f);
                AddSurface(parent, "Frost " + slab.x + "," + slab.y, centre + Vector3.up * 0.003f, slab, size, art.snowSurfaceMaterial);
                AddRock(parent, slab, size, 0.1f);
            }

            Vector3 poolCentre = RectCentre(poolArea, size);
            Vector3 poolSize = new Vector3(poolArea.width * size, 0f, poolArea.height * size);
            GameObject bottom = Part(PrimitiveType.Cube, "Basin Floor", poolCentre + Vector3.up * -(BasinDepth + 0.03f),
                new Vector3(poolSize.x, 0.06f, poolSize.z), FloorColor);
            bottom.transform.SetParent(parent, true);
            Skin(bottom, art.basinMaterial, new Vector2(poolArea.width, poolArea.height) / 3f);
            AddRock(parent, poolArea, size, BasinDepth + 0.06f);
            // Thin stone sides on all four edges: a lip along the open edges and the step down at the entrance gap.
            for (int side = 0; side < 4; side++)
            {
                bool alongX = side < 2;
                float sign = side % 2 == 0 ? -1f : 1f;
                Vector3 offset = alongX ? new Vector3(0f, 0f, sign * poolSize.z * 0.5f) : new Vector3(sign * poolSize.x * 0.5f, 0f, 0f);
                Vector3 scale = alongX ? new Vector3(poolSize.x + 0.1f, BasinDepth, 0.1f) : new Vector3(0.1f, BasinDepth, poolSize.z + 0.1f);
                GameObject wall = Part(PrimitiveType.Cube, "Basin Side " + side, poolCentre + offset + Vector3.down * (BasinDepth * 0.5f), scale, FloorColor);
                wall.transform.SetParent(parent, true);
                Skin(wall, art.basinMaterial, new Vector2(alongX ? poolArea.width : poolArea.height, 0.4f));
            }
            AddSurface(parent, "Pool Ice", poolCentre + Vector3.up * IceSurfaceHeight, poolArea, size, art.iceSurfaceMaterial);
            HangEdgeIcicles(size);
        }

        // Every cell that is not pool, not an open edge and not out of the map, as rectangles: runs along each row,
        // merged with the row below when they line up exactly.
        private List<RectInt> FloorRects(MapDefinition map)
        {
            List<RectInt> done = new List<RectInt>();
            List<RectInt> open = new List<RectInt>();
            for (int y = 0; y < map.height; y++)
            {
                List<RectInt> row = new List<RectInt>();
                for (int x = 0; x < map.width; x++)
                {
                    Vector2Int cell = new Vector2Int(x, y);
                    if (poolArea.Contains(cell) || dropCells.Contains(cell)) continue;
                    int start = x;
                    while (x + 1 < map.width && !poolArea.Contains(new Vector2Int(x + 1, y)) && !dropCells.Contains(new Vector2Int(x + 1, y))) x++;
                    row.Add(new RectInt(start, y, x - start + 1, 1));
                }
                List<RectInt> next = new List<RectInt>();
                foreach (RectInt run in row)
                {
                    int match = open.FindIndex(r => r.x == run.x && r.width == run.width);
                    if (match >= 0) { RectInt grown = open[match]; grown.height++; next.Add(grown); open.RemoveAt(match); }
                    else next.Add(run);
                }
                done.AddRange(open);
                open = next;
            }
            done.AddRange(open);
            return done;
        }

        // Dark rock under a part of the platform, from just below its top down into the chasm.
        private void AddRock(Transform parent, RectInt area, float size, float top)
        {
            float height = PlatformDepth - top;
            GameObject rock = Part(PrimitiveType.Cube, $"Platform Rock {area.x},{area.y}", RectCentre(area, size) + Vector3.down * (top + height * 0.5f),
                new Vector3(area.width * size, height, area.height * size), FloorColor);
            rock.transform.SetParent(parent, true);
            Skin(rock, art.basinMaterial, new Vector2(Mathf.Max(area.width, area.height), height) / 3f);
        }

        // Icicles under the lip wherever the pool meets an open edge, on alternate cells; they grow with the frost.
        private void HangEdgeIcicles(float size)
        {
            if (art.icicle == null) return;
            foreach (Vector2Int cell in iceCells)
                foreach (Vector2Int direction in new[] { Vector2Int.up, Vector2Int.left, Vector2Int.right })
                {
                    if (!dropCells.Contains(cell + direction) || (cell.x + cell.y) % 2 != 0) continue;
                    Vector3 lip = world.CellToWorld(cell, -BasinDepth - 0.02f) + new Vector3(direction.x, 0f, direction.y) * (size * 0.5f + 0.03f);
                    GameObject icicle = Instantiate(art.icicle, geometry);
                    icicle.name = "Edge Icicle " + cell.x + "," + cell.y;
                    icicle.transform.SetPositionAndRotation(lip, Quaternion.Euler(0f, (cell.x * 53 + cell.y * 29) % 360, 0f));
                    icicle.transform.localScale *= 1.6f;
                    ambience.AddGrowth(icicle.transform, lip);
                }
        }

        // A crystal pillar standing in the pool. It blocks in both looks, so the route around it never changes.
        private void BuildPillar(string id, Vector2Int cell, float size)
        {
            pillarCells.Add(cell);
            Vector3 position = world.CellToWorld(cell);
            if (art != null && art.icePillar != null)
            {
                GameObject pillar = Instantiate(art.icePillar, geometry);
                pillar.name = id;
                pillar.transform.SetPositionAndRotation(position + Vector3.down * 0.1f, Quaternion.Euler(0f, (cell.x * 41 + cell.y * 23) % 360, 0f));
            }
            else Part(PrimitiveType.Cylinder, id, position + Vector3.up * 0.5f, new Vector3(0.6f * size, 0.5f, 0.6f * size), IceColor, true);
            GameObject blocker = new GameObject(id + " obstacle");
            blocker.transform.SetParent(geometry, false);
            blocker.transform.position = position;
            blocker.AddComponent<GridObstacle>().Configure(world, Vector2Int.one);
        }

        // A snow patch in the pool: plain ground, so slides stop on it, without toggling anything.
        private void BuildStopTile(string id, Vector2Int cell, float size)
        {
            stopCells.Add(cell);
            GameObject patch = Part(PrimitiveType.Cylinder, id, world.CellToWorld(cell, 0.0f), new Vector3(0.86f * size, 0.06f, 0.86f * size), Color.white);
            if (art != null && art.frostMaterial != null) Skin(patch, art.frostMaterial, new Vector2(0.4f, 0.4f));
        }

        private void AddSurface(Transform parent, string name, Vector3 centre, RectInt area, float size, Material material)
        {
            GameObject surface = GameObject.CreatePrimitive(PrimitiveType.Quad);
            surface.name = name;
            Destroy(surface.GetComponent<Collider>());
            surface.transform.SetParent(parent, false);
            surface.transform.SetPositionAndRotation(centre, Quaternion.Euler(90f, 0f, 0f));
            surface.transform.localScale = new Vector3(area.width * size, area.height * size, 1f);
            Renderer renderer = surface.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ambience.AddRevealSurface(renderer);
        }

        // Crystal spikes along the wall top and icicles on every face that looks onto open floor, all grown by the frost.
        private void DressWall(Vector2Int cell, Vector2Int footprint, float size)
        {
            Vector3 centre = RectCentre(new RectInt(cell, footprint), size);
            GameObject coping = Part(PrimitiveType.Cube, "Wall Coping " + cell.x + "," + cell.y, centre + Vector3.up * (WallHeight + 0.03f),
                new Vector3(footprint.x * size + 0.12f, 0.06f, footprint.y * size + 0.12f), WallColor);
            Skin(coping, art.switchBaseMaterial, new Vector2(Mathf.Max(footprint.x, footprint.y), 0.1f));
            GameObject[] spikes = art.wallSpikes ?? Array.Empty<GameObject>();
            int length = Mathf.Max(footprint.x, footprint.y);
            bool alongX = footprint.x >= footprint.y;
            for (int i = 0; i < length && spikes.Length > 0; i++)
            {
                GameObject source = spikes[(cell.x * 7 + cell.y * 3 + i) % spikes.Length];
                if (source == null) continue;
                Vector2Int at = cell + (alongX ? new Vector2Int(i, 0) : new Vector2Int(0, i));
                float jitter = Mathf.Sin(at.x * 12.9898f + at.y * 78.233f) * 0.3f;
                Vector3 position = world.CellToWorld(at, WallHeight + 0.06f) + (alongX ? Vector3.right : Vector3.forward) * jitter;
                GameObject spike = Instantiate(source, geometry);
                spike.name = "Wall Spike " + at.x + "," + at.y;
                spike.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, (at.x * 47 + at.y * 31) % 360, 0f));
                spike.transform.localScale *= 0.8f + 0.4f * Mathf.Abs(jitter) / 0.3f;
                ambience.AddGrowth(spike.transform, position);
            }
            if (art.icicle == null) return;
            for (int x = 0; x < footprint.x; x++)
                for (int y = 0; y < footprint.y; y++)
                {
                    Vector2Int at = cell + new Vector2Int(x, y);
                    foreach (Vector2Int direction in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right })
                    {
                        Vector2Int neighbour = at + direction;
                        // Only faces looking onto open floor or the pool, and only on alternate cells so they do not crowd.
                        if ((at.x + at.y) % 2 != 0 || neighbour.x < 0 || neighbour.y < 0 || neighbour.x >= worldWidth || neighbour.y >= worldHeight ||
                            wallAreas.Exists(area => area.Contains(neighbour))) continue;
                        Vector3 face = world.CellToWorld(at, WallHeight - 0.02f) + new Vector3(direction.x, 0f, direction.y) * (size * 0.5f + 0.04f);
                        GameObject icicle = Instantiate(art.icicle, geometry);
                        icicle.name = "Icicle " + at.x + "," + at.y;
                        // Short icicles, so they hang from the low parapet without reaching the floor.
                        icicle.transform.localScale *= 0.55f;
                        icicle.transform.SetPositionAndRotation(face, Quaternion.Euler(0f, (at.x * 61 + at.y * 17) % 360, 0f));
                        ambience.AddGrowth(icicle.transform, face);
                    }
                }
        }

        private Vector3 RectCentre(RectInt area, float size) =>
            world.CellToWorld(area.position) + new Vector3((area.width - 1) * 0.5f * size, 0f, (area.height - 1) * 0.5f * size);

        private void BuildRelic(string id, Vector2Int cell, float size)
        {
            GameObject root = new GameObject(id);
            root.transform.SetParent(geometry, false);
            root.transform.position = world.CellToWorld(cell);
            if (art != null && art.pedestal != null)
            {
                GameObject model = Instantiate(art.pedestal, root.transform);
                model.name = id + " pedestal";
                model.transform.localPosition = Vector3.zero;
            }
            else
            {
                GameObject pedestal = Part(PrimitiveType.Cylinder, id + " pedestal", Vector3.zero, new Vector3(0.6f * size, StandHeight * 0.5f, 0.6f * size), RelicBaseColor);
                pedestal.transform.SetParent(root.transform, false);
                pedestal.transform.localPosition = new Vector3(0f, StandHeight * 0.5f, 0f);
            }
            root.AddComponent<GridObstacle>().Configure(world, Vector2Int.one);
            registeredViews[id] = root;
        }

        // Cinna is spawned from their catalog record (Cinna Cirra model, cell, quest-gated visibility and dialogue)
        // the way Katrina's model was placed in her garden recess.
        private void BuildCinna(NpcDatabaseDefinition npcs)
        {
            NpcDefinition cinna = Array.Find(npcs?.npcs ?? Array.Empty<NpcDefinition>(), npc =>
                npc != null && npc.id == TrialNpcId && string.Equals(npc.mapId, MapId, StringComparison.OrdinalIgnoreCase));
            if (cinna == null || cinna.prefab == null) { Debug.LogError($"NPC catalog has no '{TrialNpcId}' record with a prefab for {MapId}.", this); return; }
            Vector2Int cell = new Vector2Int(cinna.x, cinna.y);
            GameObject npc = Instantiate(cinna.prefab, geometry);
            npc.name = cinna.displayName;
            npc.transform.position = world.CellToWorld(cell);
            npc.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            Animator animator = npc.GetComponentInChildren<Animator>();
            if (animator != null && cinnaAnimator != null) animator.runtimeAnimatorController = cinnaAnimator;
            npc.AddComponent<DynamicNpc>().Configure(cinna, null);
            GameObject blocker = new GameObject(cinna.displayName + " obstacle");
            blocker.transform.SetParent(geometry, false);
            blocker.transform.position = world.CellToWorld(cell);
            blocker.AddComponent<GridObstacle>().Configure(world, Vector2Int.one);
        }

        private GameObject Part(PrimitiveType type, string name, Vector3 position, Vector3 scale, Color color, bool emissive = false)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = name;
            Collider collider = part.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            part.transform.SetParent(geometry, false);
            part.transform.position = position;
            part.transform.localScale = scale;
            Renderer renderer = part.GetComponent<Renderer>();
            if (renderer != null) SetColor(renderer, color, emissive);
            return part;
        }

        // Swaps a primitive's tinted material for an art material, tiled per object through a property block so the
        // shared material is never instanced.
        private static void Skin(GameObject part, Material material, Vector2 tiling) => Skin(part, material, tiling, Vector2.zero);

        private static void Skin(GameObject part, Material material, Vector2 tiling, Vector2 offset)
        {
            Renderer renderer = part == null || material == null ? null : part.GetComponent<Renderer>();
            if (renderer == null) return;
            if (Application.isPlaying) Destroy(renderer.material);
            else DestroyImmediate(renderer.material);
            renderer.sharedMaterial = material;
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            block.SetVector("_BaseMap_ST", new Vector4(tiling.x, tiling.y, offset.x, offset.y));
            renderer.SetPropertyBlock(block);
        }

        private static void SetColor(Renderer renderer, Color color, bool emissive)
        {
            renderer.material.color = color;
            if (emissive) { renderer.material.SetColor("_EmissionColor", color * 1.6f); renderer.material.EnableKeyword("_EMISSION"); }
            else renderer.material.DisableKeyword("_EMISSION");
        }

        private static PortalContentAsset FindPortal(WorldAreaAsset area, string id)
        {
            if (area != null)
                foreach (PortalContentAsset candidate in area.portals ?? Array.Empty<PortalContentAsset>())
                    if (candidate != null && string.Equals(candidate.id, id, StringComparison.Ordinal)) return candidate;
            return null;
        }
    }
}
