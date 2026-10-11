using System;
using UnityEngine;
using Lumenfall.DataDriven;
using Lumenfall.GridMovement;
using Lumenfall.Spells;

namespace Lumenfall.Quests
{
    /// <summary>
    /// Campus entrance to Cinna Cirra's ice trial at <c>P.CAMPUS.ICETRIAL</c>, on the campus path at <c>(47,45)</c>.
    /// Like Katrina's recess entrance it is open whenever campus is loaded; the trial returns to this tile through its
    /// landing stone. It is built at runtime, so the baked area prefabs and their PlayMode route count are unchanged.
    /// </summary>
    public sealed class CinnaIceTrialEntrance : MonoBehaviour
    {
        public const string PortalId = "P.CAMPUS.ICETRIAL";
        private const string MapId = "campus";
        // Portal content assets only describe connections; a GridPortal's cell is its transform position.
        public static readonly Vector2Int EntranceCell = new Vector2Int(47, 45);
        private static readonly Color PortalColor = new Color(0.62f, 0.86f, 1f);
        private static CinnaIceTrialEntrance instance;

        private WorldStateService subscribedState;
        private GameObject portalObject;
        private GridPortal portal;
        private Transform ring;
        private bool onCampus;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            GameObject host = new GameObject("[Cinna Ice Trial Entrance]");
            instance = host.AddComponent<CinnaIceTrialEntrance>();
            DontDestroyOnLoad(host);
        }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            Subscribe();
        }

        private void OnDestroy()
        {
            if (subscribedState != null) subscribedState.MapRebuilt -= OnMapRebuilt;
            if (instance == this) instance = null;
        }

        private void Update()
        {
            Subscribe();
            if (ring != null && portalObject != null && portalObject.activeSelf) ring.Rotate(0f, 85f * Time.unscaledDeltaTime, 0f, Space.Self);
        }

        // MapRebuilt fires inside the loader's Load, before SwitchArea looks up the live destination portal, so the
        // entrance is active in time to suppress an immediate re-entry when the player returns onto its tile.
        private void Subscribe()
        {
            WorldStateService state = WorldStateService.Instance;
            if (state == null || state == subscribedState) return;
            if (subscribedState != null) subscribedState.MapRebuilt -= OnMapRebuilt;
            subscribedState = state;
            state.MapRebuilt += OnMapRebuilt;
            if (state.IsReady) OnMapRebuilt(new MapRebuildEvent(state.CurrentMapId));
        }

        private void OnMapRebuilt(MapRebuildEvent rebuild)
        {
            onCampus = rebuild != null && string.Equals(rebuild.MapId, MapId, StringComparison.OrdinalIgnoreCase);
            if (onCampus) BuildOrRebind();
            if (portalObject != null && portalObject.activeSelf != onCampus) portalObject.SetActive(onCampus);
        }

        private void BuildOrRebind()
        {
            GridWorld world = FindAnyObjectByType<GridWorld>();
            PortalContentAsset content = null;
            foreach (PortalContentAsset candidate in DataDrivenWorldLoader.Instance?.CurrentArea?.portals ?? Array.Empty<PortalContentAsset>())
                if (candidate != null && string.Equals(candidate.id, PortalId, StringComparison.Ordinal)) { content = candidate; break; }
            if (world == null || content == null) { Debug.LogError($"Missing authored portal content '{PortalId}'.", this); return; }

            if (portalObject == null)
            {
                portalObject = new GameObject("Cinna Ice Trial Entrance");
                portalObject.transform.SetParent(transform, false);
                portalObject.SetActive(false);
                portal = portalObject.AddComponent<GridPortal>();
                GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                visual.name = "Ice trial entrance ring";
                visual.transform.SetParent(portalObject.transform, false);
                visual.transform.localPosition = new Vector3(0f, 0.08f, 0f);
                visual.transform.localScale = new Vector3(0.82f, 0.06f, 0.82f);
                Collider collider = visual.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
                Renderer renderer = visual.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.material.color = PortalColor;
                    renderer.material.SetColor("_EmissionColor", PortalColor * 2f);
                    renderer.material.EnableKeyword("_EMISSION");
                }
                ring = visual.transform;
            }
            // Sit the ring on the player's ground height so raised campus tiles cannot bury it.
            GridPlayerInput input = FindAnyObjectByType<GridPlayerInput>();
            float elevation = input == null ? 0f : world.WorldToElevation(input.transform.position);
            portalObject.transform.position = world.CellToWorld(EntranceCell, elevation + 0.03f);
            portal.Configure(content, world);
            if (!world.IsCellAvailable(EntranceCell)) Debug.LogWarning($"Ice trial entrance cell {EntranceCell} is blocked on campus.", this);
        }
    }
}
