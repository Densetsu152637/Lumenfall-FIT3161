using System;
using Lumenfall.DataDriven;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using UnityEngine;

namespace Lumenfall.Quests
{
    /// <summary>Unlocks a shortcut back to B02's entrance after Stella's completion conversation.</summary>
    public sealed class StellaReturnPortal : MonoBehaviour
    {
        private const string MapId = "B02.Ground";
        private const string QuestId = "stella_first_light";
        private const string DialogueFlag = "stella.first_light.debriefed";
        private const string PortalId = "P.B02.STELLA.CAMPUS";
        private static StellaReturnPortal instance;

        private GameObject portalObject;
        private Transform ring;
        private bool mapActive;

        public static void RegisterForMap(string mapId, GridWorld world)
        {
            if (instance == null)
            {
                GameObject host = new GameObject("[Stella Return Portal]");
                instance = host.AddComponent<StellaReturnPortal>();
                DontDestroyOnLoad(host);
            }

            instance.mapActive = string.Equals(mapId, MapId, StringComparison.OrdinalIgnoreCase);
            if (instance.mapActive && world != null) instance.BuildOrRebind(world);
            instance.RefreshVisibility();
        }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
        }

        private void BuildOrRebind(GridWorld world)
        {
            WorldAreaAsset area = DataDrivenWorldLoader.Instance?.CurrentArea;
            PortalContentAsset content = null;
            if (area != null)
                foreach (PortalContentAsset candidate in area.portals)
                    if (candidate != null && string.Equals(candidate.id, PortalId, StringComparison.Ordinal)) { content = candidate; break; }
            if (content == null) { Debug.LogError($"Missing authored portal content '{PortalId}'.", this); return; }

            if (portalObject == null)
            {
                portalObject = new GameObject("Stella Return Portal");
                portalObject.transform.SetParent(transform, false);
                portalObject.SetActive(false);
                GridPortal portal = portalObject.AddComponent<GridPortal>();

                GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                visual.name = "Return portal ring";
                visual.transform.SetParent(portalObject.transform, false);
                visual.transform.localPosition = new Vector3(0f, 0.08f, 0f);
                visual.transform.localScale = new Vector3(0.82f, 0.06f, 0.82f);
                Collider collider = visual.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
                Renderer renderer = visual.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.material.color = new Color(0.36f, 0.82f, 1f);
                    renderer.material.SetColor("_EmissionColor", new Color(0.12f, 0.62f, 1f) * 2.2f);
                    renderer.material.EnableKeyword("_EMISSION");
                }
                ring = visual.transform;
                portal.Configure(content, world);
            }
            else
            {
                portalObject.GetComponent<GridPortal>()?.Configure(content, world);
            }
            portalObject.transform.position = world.CellToWorld(content.cell, 0.03f);
        }

        private void Update()
        {
            RefreshVisibility();
            if (portalObject != null && portalObject.activeSelf && ring != null)
                ring.Rotate(0f, 85f * Time.unscaledDeltaTime, 0f, Space.Self);
        }

        private void RefreshVisibility()
        {
            if (portalObject == null) return;
            GameEventRuntime runtime = GameEventManager.Instance?.Runtime;
            bool unlocked = mapActive && runtime != null &&
                runtime.GetQuestStatus(QuestId) == QuestStatus.Completed && runtime.GetFlag(DialogueFlag);
            if (portalObject.activeSelf != unlocked) portalObject.SetActive(unlocked);
        }
    }
}
