using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using Lumenfall.GridMovement;
using Lumenfall.Spells;

namespace Lumenfall.DataDriven
{
    /// <summary>Display-only additions to cached authored areas. Never registers collision, travel or progression.</summary>
    [DisallowMultipleComponent]
    public sealed class WorldAreaPresentation : MonoBehaviour
    {
        private sealed class ObjectView
        {
            public WorldObjectRegistration Registration;
            public WorldPresentationLabel Label;
            public bool Hidden;
            public bool OwnsLabel;
            public Renderer[] Renderers;
            public bool[] OriginalForceOff;
        }
        private readonly List<ObjectView> objects = new List<ObjectView>();
        private readonly List<GameObject> additions = new List<GameObject>();
        private MapDefinition map;
        private WorldStateService state;
        private Mesh boxMesh;
        private Material propMaterial;
        private bool built;

        public static void Install(GameObject root, MapDefinition definition, GridWorld grid, NpcDatabaseDefinition catalog, WorldStateService service)
        {
            if (root == null || definition == null) return;
            WorldAreaPresentation presentation = root.GetComponent<WorldAreaPresentation>();
            if (presentation == null) presentation = root.AddComponent<WorldAreaPresentation>();
            presentation.Configure(definition, grid, catalog, service);
        }

        public void Configure(MapDefinition definition, GridWorld grid, NpcDatabaseDefinition catalog, WorldStateService service)
        {
            Unsubscribe();
            map = definition;
            state = service;
            if (!built && map != null)
            {
                built = true;
                BuildObjectLabels();
                BuildPortalLabels();
                BuildRoomPlaceholders(grid);
                BuildMissingCharacterPlaceholders(grid, catalog);
            }
            Subscribe();
            Refresh();
        }

        private void OnEnable() { Subscribe(); Refresh(); }
        private void OnDisable() { Unsubscribe(); RestoreHiddenRenderers(); }
        private void Subscribe()
        {
            if (state == null || !isActiveAndEnabled) return;
            // Configure can be called more than once for a cached area.
            state.StateChanged -= HandleChanged;
            state.MapRebuilt -= HandleRebuilt;
            state.StateChanged += HandleChanged;
            state.MapRebuilt += HandleRebuilt;
        }
        private void Unsubscribe()
        {
            if (state == null) return;
            state.StateChanged -= HandleChanged;
            state.MapRebuilt -= HandleRebuilt;
        }
        private void HandleChanged(MapStateSnapshot snapshot) { if (snapshot != null && map != null && snapshot.mapId == map.id) Refresh(); }
        private void HandleRebuilt(MapRebuildEvent change) { if (change != null && map != null && change.MapId == map.id) Refresh(); }

        private void BuildObjectLabels()
        {
            foreach (WorldObjectRegistration registration in GetComponentsInChildren<WorldObjectRegistration>(true))
            {
                string title = WorldPresentationCatalog.ObjectName(registration.ObjectId);
                if (title == null) continue;
                WorldPresentationLabel label = registration.GetComponent<WorldPresentationLabel>();
                bool ownsLabel = label == null;
                if (ownsLabel) label = registration.gameObject.AddComponent<WorldPresentationLabel>();
                label.Configure(title + "\nProvisional prop", 0xE4BF58, Vector3.up * 1.15f);
                label.SetVisible(false);
                // Live registration tags also account for inherited baked/source differences.
                bool hidden = Array.IndexOf(registration.Tags, "hidden") >= 0;
                Renderer[] renderers = hidden ? registration.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>();
                bool[] original = new bool[renderers.Length];
                for (int i = 0; i < renderers.Length; i++) original[i] = renderers[i].forceRenderingOff;
                objects.Add(new ObjectView { Registration = registration, Label = label, OwnsLabel = ownsLabel, Hidden = hidden, Renderers = renderers, OriginalForceOff = original });
            }
        }

        public void Refresh()
        {
            bool ready = state != null && state.IsReady && map != null && state.CurrentMapId == map.id;
            WorldObjectSnapshot[] knownObjects = ready ? state.SnapshotObjects() : Array.Empty<WorldObjectSnapshot>();
            foreach (ObjectView view in objects)
            {
                WorldObjectSnapshot snapshot = null;
                bool available = ready && state.TryGetObject(view.Registration.ObjectId, out snapshot);
                bool visible = WorldPresentationCatalog.CanShowObject(view.Hidden, available && snapshot.revealed, available);
                view.Label.SetVisible(visible);
                for (int i = 0; i < view.Renderers.Length; i++)
                    if (view.Renderers[i] != null) view.Renderers[i].forceRenderingOff = view.OriginalForceOff[i] || !visible;
                if (!visible) continue;
                bool connected = false;
                foreach (WorldObjectSnapshot other in knownObjects)
                    if (other != null && other.id != snapshot.id && state.IsConnected(snapshot.id, other.id)) { connected = true; break; }
                string text = WorldPresentationCatalog.ObjectName(snapshot.id) + "\n" +
                    WorldPresentationCatalog.ObjectStateText(snapshot.id, snapshot.powered, connected, snapshot.frozen,
                        snapshot.aligned, snapshot.rotation, snapshot.cell.x, snapshot.cell.y);
                foreach (PuzzleTargetDefinition target in state.PuzzleTargets)
                {
                    if (target == null || Array.IndexOf(target.objectIds ?? Array.Empty<string>(), snapshot.id) < 0) continue;
                    text += "\n" + RequirementText(target, state.EvaluatePuzzleTarget(target));
                }
                view.Label.Configure(text + "\nProvisional prop", 0xE4BF58, Vector3.up * 1.15f);
            }
        }

        private static string RequirementText(PuzzleTargetDefinition target, bool met)
        {
            var text = new StringBuilder("Requirement " + (met ? "met" : "pending"));
            if (target.requiredRotation >= 0) text.Append(" | Target: ").Append(WorldPresentationCatalog.Angle(target.requiredRotation));
            if (target.requiredPositionX != int.MinValue && target.requiredPositionY != int.MinValue)
                text.Append(" | Goal: ").Append(target.requiredPositionX).Append(", ").Append(target.requiredPositionY);
            return text.ToString();
        }

        private void BuildPortalLabels()
        {
            // Only baked live entrances get signs. Reserved asset definitions remain unopened.
            foreach (GridPortal portal in GetComponentsInChildren<GridPortal>(true))
            {
                PortalContentAsset content = portal.Content;
                string destination = content?.destination?.area?.definition?.id;
                string text = content == null || content.reserved || content.destination == null ? "Reserved / unavailable" :
                    "Entrance: " + (destination ?? "Authored destination");
                GameObject sign = Addition("Entrance sign");
                sign.transform.position = portal.transform.position;
                sign.AddComponent<WorldPresentationLabel>().Configure(text, 0x8DCEAE, Vector3.up * 1.1f);
            }
        }

        private void BuildRoomPlaceholders(GridWorld grid)
        {
            if (grid == null || map.id != "B04.Ground") return;
            foreach (MapRegionDefinition region in map.regions ?? Array.Empty<MapRegionDefinition>())
            {
                string name = WorldPresentationCatalog.DormitoryRegionName(region?.id);
                if (name == null || region.rect == null) continue;
                // Use the authored zone anchor; these raised props carry no occupancy or interaction.
                Vector3 at = grid.CellToWorld(new Vector2Int(region.rect.x, region.rect.y));
                GameObject sign = Addition("Dormitory zone sign " + region.id);
                sign.transform.position = at;
                sign.AddComponent<WorldPresentationLabel>().Configure(name + "\nProvisional room zone", 0x81BCF4, Vector3.up * 1.2f);
                if (region.id == "D08")
                {
                    Prop("Bed placeholder", at + Vector3.up * 0.2f, new Vector3(0.65f, 0.2f, 0.85f));
                    Prop("Storage placeholder", at + new Vector3(0.7f, 0.25f, 0f), new Vector3(0.25f, 0.45f, 0.25f));
                }
                if (region.id == "D02") Prop("Seating placeholder", at + Vector3.up * 0.2f, new Vector3(0.6f, 0.25f, 0.3f));
            }
        }

        private void BuildMissingCharacterPlaceholders(GridWorld grid, NpcDatabaseDefinition catalog)
        {
            if (grid == null) return;
            // Preserve baked NPC ownership. Catalogue-only Katrina gets a visible non-interactive stand-in.
            foreach (NpcDefinition npc in catalog?.npcs ?? Array.Empty<NpcDefinition>())
            {
                if (npc == null || npc.id != "katrina" || npc.mapId != map.id) continue;
                bool placed = false;
                foreach (DynamicNpc baked in GetComponentsInChildren<DynamicNpc>(true)) if (baked.Definition?.id == npc.id) placed = true;
                if (!placed) CharacterPlaceholder("katrina", grid.CellToWorld(new Vector2Int(npc.x, npc.y)));
            }
            if (map.id == "B01.Ground")
                foreach (MapRegionDefinition region in map.regions ?? Array.Empty<MapRegionDefinition>())
                    if (region?.id == "S02" && region.rect != null)
                        CharacterPlaceholder("receptionist", grid.CellToWorld(new Vector2Int(region.rect.x, region.rect.y)));
        }

        private void CharacterPlaceholder(string id, Vector3 at)
        {
            if (!WorldPresentationCatalog.TryCharacter(id, out CharacterPresentationIdentity identity)) return;
            GameObject placeholder = Prop(identity.Name + " placement placeholder", at + Vector3.up * 0.4f, new Vector3(0.35f, 0.8f, 0.35f));
            placeholder.AddComponent<WorldPresentationLabel>().Configure(identity.Symbol + " | " + identity.Name +
                "\nProvisional placement / no interaction", identity.AccentRgb, Vector3.up * 0.75f, identity);
        }

        private GameObject Addition(string name)
        {
            var item = new GameObject(name);
            item.transform.SetParent(transform, false);
            additions.Add(item);
            return item;
        }
        private GameObject Prop(string name, Vector3 position, Vector3 scale)
        {
            GameObject item = Addition(name);
            item.transform.position = position;
            item.transform.localScale = scale;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            // Labels stay useful even if the player's build strips the fallback shader.
            if (shader == null) return item;
            if (boxMesh == null) boxMesh = CreateBoxMesh();
            if (propMaterial == null)
            {
                propMaterial = new Material(shader) { name = "Owned provisional room material" };
                if (propMaterial.HasProperty("_BaseColor")) propMaterial.SetColor("_BaseColor", new Color(0.38f, 0.48f, 0.60f));
                if (propMaterial.HasProperty("_Color")) propMaterial.SetColor("_Color", new Color(0.38f, 0.48f, 0.60f));
            }
            item.AddComponent<MeshFilter>().sharedMesh = boxMesh;
            MeshRenderer renderer = item.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = propMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return item;
        }
        private static Mesh CreateBoxMesh()
        {
            var mesh = new Mesh { name = "Owned collider-free placeholder box" };
            mesh.vertices = new[] { new Vector3(-.5f,-.5f,-.5f), new Vector3(.5f,-.5f,-.5f), new Vector3(.5f,.5f,-.5f), new Vector3(-.5f,.5f,-.5f),
                new Vector3(-.5f,-.5f,.5f), new Vector3(.5f,-.5f,.5f), new Vector3(.5f,.5f,.5f), new Vector3(-.5f,.5f,.5f) };
            mesh.triangles = new[] { 0,2,1,0,3,2, 4,5,6,4,6,7, 0,4,7,0,7,3, 1,2,6,1,6,5, 3,7,6,3,6,2, 0,1,5,0,5,4 };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        private void RestoreHiddenRenderers()
        {
            foreach (ObjectView view in objects) for (int i = 0; i < view.Renderers.Length; i++)
                if (view.Renderers[i] != null) view.Renderers[i].forceRenderingOff = view.OriginalForceOff[i];
        }
        private void OnDestroy()
        {
            Unsubscribe(); RestoreHiddenRenderers();
            foreach (ObjectView view in objects) if (view.OwnsLabel && view.Label != null) Release(view.Label);
            foreach (GameObject item in additions) if (item != null) Release(item);
            if (boxMesh != null) Release(boxMesh);
            if (propMaterial != null) Release(propMaterial);
        }
        private static void Release(UnityEngine.Object item)
        { if (Application.isPlaying) Destroy(item); else DestroyImmediate(item); }
    }
}
