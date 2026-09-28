using System;
using UnityEngine;
using Lumenfall.GridMovement;
using Lumenfall.Spells;

namespace Lumenfall.Quests
{
    /// <summary>Draws the authored Weave connection for Stella's two-endpoint circuit puzzle.</summary>
    public sealed class StellaStringThemTogetherPuzzle : MonoBehaviour
    {
        private const string MapId = "B02.Ground";
        private const string PowerRelicId = "relic.stella.power";
        private const string CircuitLinkId = "circuit.stella.link";
        private static StellaStringThemTogetherPuzzle instance;

        private GridWorld grid;
        private WorldStateService state;
        private LineRenderer wire;
        private GameObject poweredIndicator;
        private bool mapVisible;

        /// <summary>Called by the area loader after rebuilding the current map's world objects.</summary>
        public static void RegisterForMap(string mapId, GridWorld world)
        {
            bool isPuzzleMap = string.Equals(mapId, MapId, StringComparison.OrdinalIgnoreCase);
            if (instance == null)
            {
                GameObject host = new GameObject("[Stella String Them Together]");
                instance = host.AddComponent<StellaStringThemTogetherPuzzle>();
                DontDestroyOnLoad(host);
            }

            instance.mapVisible = isPuzzleMap;
            instance.grid = isPuzzleMap ? world : null;
            instance.state = isPuzzleMap ? WorldStateService.Instance : null;
            if (instance.wire != null) instance.wire.enabled = isPuzzleMap;
            if (instance.poweredIndicator != null && !isPuzzleMap) instance.poweredIndicator.SetActive(false);
            instance.RefreshWire();
        }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            CreateWire();
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        private void Update()
        {
            if (!mapVisible) return;
            if (state == null) state = WorldStateService.Instance;
            RefreshWire();
        }

        private void CreateWire()
        {
            GameObject wireObject = new GameObject("Weave Connection");
            wireObject.transform.SetParent(transform, false);
            wire = wireObject.AddComponent<LineRenderer>();
            wire.useWorldSpace = true;
            wire.positionCount = 2;
            wire.startWidth = 0.075f;
            wire.endWidth = 0.075f;
            wire.numCapVertices = 4;
            wire.startColor = new Color(0.31f, 1f, 0.8f, 0.95f);
            wire.endColor = new Color(0.58f, 0.84f, 1f, 0.95f);
            wire.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            wire.receiveShadows = false;
            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            if (shader != null) wire.material = new Material(shader);
            wire.enabled = false;

            poweredIndicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            poweredIndicator.name = "Powered Relic Indicator";
            poweredIndicator.transform.SetParent(transform, false);
            poweredIndicator.transform.localScale = Vector3.one * 0.2f;
            Collider indicatorCollider = poweredIndicator.GetComponent<Collider>();
            if (indicatorCollider != null) Destroy(indicatorCollider);
            Renderer indicatorRenderer = poweredIndicator.GetComponent<Renderer>();
            if (indicatorRenderer != null && shader != null)
            {
                Material indicatorMaterial = new Material(shader);
                Color glow = new Color(0.28f, 1f, 0.76f, 1f);
                if (indicatorMaterial.HasProperty("_Color")) indicatorMaterial.SetColor("_Color", glow);
                if (indicatorMaterial.HasProperty("_BaseColor")) indicatorMaterial.SetColor("_BaseColor", glow);
                if (indicatorMaterial.HasProperty("_EmissionColor"))
                {
                    indicatorMaterial.EnableKeyword("_EMISSION");
                    indicatorMaterial.SetColor("_EmissionColor", glow * 1.8f);
                }
                indicatorRenderer.material = indicatorMaterial;
            }
            poweredIndicator.SetActive(false);
        }

        private void RefreshWire()
        {
            if (!mapVisible || grid == null || state == null || !state.IsReady || wire == null) return;
            bool hasPower = state.TryGetObject(PowerRelicId, out WorldObjectSnapshot power);
            bool hasLink = state.TryGetObject(CircuitLinkId, out WorldObjectSnapshot link);
            bool powered = hasPower && power.powered;
            if (poweredIndicator != null)
            {
                poweredIndicator.SetActive(powered);
                if (powered) poweredIndicator.transform.position = grid.CellToWorld(power.cell, power.elevation + 0.75f);
            }
            bool connected = state.IsConnected(PowerRelicId, CircuitLinkId) && hasPower && hasLink;
            wire.enabled = connected;
            if (!connected) return;

            wire.SetPosition(0, grid.CellToWorld(power.cell, power.elevation + 0.28f));
            wire.SetPosition(1, grid.CellToWorld(link.cell, link.elevation + 0.28f));
        }
    }
}
