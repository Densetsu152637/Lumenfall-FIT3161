using System.Collections.Generic;
using Lumenfall.GridMovement;
using Lumenfall.Spells;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Lumenfall.Tests.GameEvents
{
    /// <summary>
    /// Fixes made by hand in area prefabs: blockers that match what the player sees, blockers that follow the
    /// character they belong to, and props that should not be there. Cells are read the way GridObstacle registers them
    /// (one unit per cell, areas at the origin).
    /// </summary>
    public sealed class AuthoredLayoutTests
    {
        private const string Areas = "Assets/_Project/World/Areas/Prefabs/";

        [Test]
        public void TheDormitoryPartitionAndCabinetCannotBeWalkedThrough()
        {
            HashSet<Vector2Int> blocked = BlockedCells(Load("Dormitory.prefab"));
            Assert.That(blocked, Has.Member(new Vector2Int(14, 13)), "Reception Cabinet East.");
            Assert.That(blocked, Has.Member(new Vector2Int(15, 13)), "The partition's half-covered end.");
            for (int x = 16; x <= 19; x++)
                Assert.That(blocked, Has.No.Member(new Vector2Int(x, 13)), "The hallway doorway stays open at x " + x);
        }

        [Test]
        public void TheWorkshopGyroscopeStellaAndCandelabraAreWhereTheyBelong()
        {
            GameObject workshop = Load("Arcane Workshop.prefab");
            HashSet<Vector2Int> blocked = BlockedCells(workshop);
            Assert.That(Cell(Find(workshop, "Stella")), Is.EqualTo(new Vector2Int(19, 10)));
            Assert.That(blocked, Has.Member(new Vector2Int(19, 10)), "Stella blocks her own tile.");
            Assert.That(Cell(Find(workshop, "magical_gyroscope")), Is.EqualTo(new Vector2Int(19, 14)));
            Assert.That(blocked, Has.Member(new Vector2Int(19, 14)), "The gyroscope blocks its own tile.");
            foreach (Vector2Int old in new[] { new Vector2Int(16, 15), new Vector2Int(19, 15), new Vector2Int(19, 16) })
                Assert.That(blocked, Has.No.Member(old), "No blocker is left where the gyroscope or Stella used to be: " + old);
            Assert.That(Find(workshop, "Teacher Table Candles").activeSelf, Is.False, "The candelabra outside the east wall is hidden.");
        }

        [Test]
        public void TheTerraceCinnasBlockerLeavesWithCinna()
        {
            Transform cinna = Find(Load("campus.prefab"), "Cinna Cirra (1)").transform;
            bool found = false;
            foreach (GridObstacle obstacle in cinna.GetComponentsInChildren<GridObstacle>(true))
                found |= Cells(obstacle).Contains(new Vector2Int(22, 51));
            Assert.That(found, Is.True, "The blocker on Cinna's terrace tile is a child of Cinna, so hiding Cinna frees the tile.");
        }

        [Test]
        public void RecolouringTheForgottenManInTheEditorLeavesTheSharedMaterialsAlone()
        {
            // The baker builds him in the editor: his colours must go onto copies, never onto the model's own materials.
            GameObject model = Lumenfall.Story.SecretRoomArt.Load()?.devGlasses;
            Assert.That(model, Is.Not.Null, "SecretRoomArt's short-haired base model.");
            var before = new Dictionary<Material, Color>();
            foreach (Renderer part in model.GetComponentsInChildren<Renderer>(true))
                foreach (Material material in part.sharedMaterials)
                    if (material != null && material.HasProperty("_BaseColor")) before[material] = material.GetColor("_BaseColor");

            GameObject man = Lumenfall.Story.DevChibis.Create(model, Lumenfall.Story.DevChibis.OldMan, null);
            try
            {
                foreach (Renderer part in man.GetComponentsInChildren<Renderer>(true))
                    foreach (Material material in part.sharedMaterials)
                        if (material != null && material.name.Contains("_Hair"))
                            Assert.That(EditorUtility.IsPersistent(material), Is.False, part.name + " is recoloured on a copy.");
                foreach (KeyValuePair<Material, Color> original in before)
                    Assert.That(original.Key.GetColor("_BaseColor"), Is.EqualTo(original.Value), original.Key.name + " is left as it was.");
            }
            finally { Object.DestroyImmediate(man); }
        }

        [Test]
        public void AmpereEffectsArePlayedAndClearedSoTheyCanFireAgain()
        {
            WorldStateService previous = WorldStateService.Instance;
            GameObject world = new GameObject("world"), host = new GameObject("ampere");
            try
            {
                WorldStateService state = world.AddComponent<WorldStateService>();
                EditModeLifecycle.SetWorldInstance(state);
                state.BeginMapRebuild("B02.Ground");
                state.EndMapRebuild();
                AmperePresentation presentation = host.AddComponent<AmperePresentation>();
                EditModeLifecycle.Call(presentation, "Update");
                int fired = 0;
                state.EffectQueued += _ => fired++;
                state.QueueEffect("stella-relic:1:on");
                state.QueueEffect("stella-relic:1:on");
                Assert.That(fired, Is.EqualTo(2), "A played effect is acknowledged, so the same resonance can flash again.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(world);
                EditModeLifecycle.SetWorldInstance(previous);
            }
        }

        private static GameObject Load(string prefab)
        {
            GameObject area = AssetDatabase.LoadAssetAtPath<GameObject>(Areas + prefab);
            Assert.That(area, Is.Not.Null, prefab);
            return area;
        }

        private static GameObject Find(GameObject root, string name)
        {
            foreach (Transform part in root.GetComponentsInChildren<Transform>(true))
                if (part.name == name) return part.gameObject;
            Assert.Fail(name + " is missing from " + root.name);
            return null;
        }

        private static Vector2Int Cell(GameObject item) =>
            new Vector2Int(Mathf.RoundToInt(item.transform.position.x), Mathf.RoundToInt(item.transform.position.z));

        private static HashSet<Vector2Int> BlockedCells(GameObject root)
        {
            HashSet<Vector2Int> cells = new HashSet<Vector2Int>();
            foreach (GridObstacle obstacle in root.GetComponentsInChildren<GridObstacle>(true))
                if (obstacle.enabled && ActiveInPrefab(obstacle.transform)) cells.UnionWith(Cells(obstacle));
            return cells;
        }

        private static HashSet<Vector2Int> Cells(GridObstacle obstacle)
        {
            SerializedObject serialized = new SerializedObject(obstacle);
            Vector2Int anchor = serialized.FindProperty("useTransformAsAnchor").boolValue
                ? Cell(obstacle.gameObject) : serialized.FindProperty("anchorCell").vector2IntValue;
            HashSet<Vector2Int> cells = new HashSet<Vector2Int>();
            for (int x = 0; x < obstacle.FootprintSize.x; x++)
                for (int y = 0; y < obstacle.FootprintSize.y; y++)
                    cells.Add(anchor + obstacle.FootprintOffset + new Vector2Int(x, y));
            return cells;
        }

        // Prefab assets are not in a scene, so activeInHierarchy does not apply; walk the parents instead.
        private static bool ActiveInPrefab(Transform part)
        {
            for (Transform current = part; current != null; current = current.parent)
                if (!current.gameObject.activeSelf) return false;
            return true;
        }
    }
}
