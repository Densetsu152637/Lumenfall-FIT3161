using System;
using System.Collections.Generic;
using System.Reflection;
using Lumenfall.DataDriven;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Spells;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Lumenfall.Tests.GameEvents
{
    public sealed class WorldPresentationTests
    {
        private readonly List<GameObject> created = new List<GameObject>();
        private GridWorld grid;
        private WorldStateService state;
        [SetUp] public void SetUp()
        {
            grid = New("grid").AddComponent<GridWorld>(); grid.ConfigureBounds(60, 92);
            state = New("presentation state").AddComponent<WorldStateService>(); state.BeginMapRebuild("campus");
        }
        [TearDown] public void TearDown()
        {
            if (state != null) GameSaveManager.Instance.UnregisterProvider(state.SaveKey);
            for (int i = created.Count - 1; i >= 0; i--) if (created[i] != null) Object.DestroyImmediate(created[i]);
            created.Clear();
        }
        private GameObject New(string name) { var item = new GameObject(name); created.Add(item); return item; }

        [Test] public void ReconfigurationUsesApprovedIdentityAndKeepsOneLabel()
        {
            DynamicNpc npc = New("NPC").AddComponent<DynamicNpc>();
            npc.Configure(new NpcDefinition { id = "cinna_cirra_orientation", displayName = "Cinna Cirra" }, null);
            npc.Configure(new NpcDefinition { id = "cinna_cirra_trial", displayName = "Cinna Cirra" }, null);
            Assert.That(npc.GetComponents<WorldPresentationLabel>(), Has.Length.EqualTo(1));
            Assert.That(npc.GetComponent<WorldPresentationLabel>().Text, Does.Contain("Cinna Cirra").And.Contain("Provisional model"));
            Assert.That(npc.Definition.id, Is.EqualTo("cinna_cirra_trial"));
            npc.Configure(new NpcDefinition { id = "campus_student", displayName = "Campus student" }, null);
            Assert.That(npc.GetComponent<WorldPresentationLabel>().Visible, Is.False);
            Assert.That(npc.GetComponent<WorldPresentationLabel>().Text, Is.Empty);
            Assert.That(WorldPresentationCatalog.TryCharacter("ambient", out _), Is.False);
        }

        [Test] public void HiddenKeepsakeShowsOnlyAfterRevealAndReturnsToHiddenAfterRestore()
        {
            GameObject root = New("area"); GameObject item = New("keepsake"); item.transform.SetParent(root.transform);
            MeshRenderer renderer = item.AddComponent<MeshRenderer>();
            WorldObjectRegistration registration = item.AddComponent<WorldObjectRegistration>();
            registration.Configure("keepsake.katrina.hidden", new Vector2Int(42, 60), Vector2Int.one, true, new[] { "hidden", "lost-echoes" });
            state.RegisterObject(registration.ObjectId, item, registration.Cell, registration.Size, true, registration.Tags);
            var map = new MapDefinition { id = "campus" };
            WorldAreaPresentation.Install(root, map, grid, null, state); state.EndMapRebuild();
            WorldPresentationLabel label = item.GetComponent<WorldPresentationLabel>();
            Assert.That(label.Visible, Is.False); Assert.That(renderer.forceRenderingOff, Is.True);
            string hiddenSave = state.CaptureStateJson();
            state.SetProperty(registration.ObjectId, value => value.revealed = true);
            Assert.That(label.Visible, Is.True); Assert.That(label.Text, Does.Contain("Recovered keepsake"));
            Assert.That(renderer.forceRenderingOff, Is.False);
            Assert.That(state.RestoreStateJson(hiddenSave), Is.True);
            Assert.That(label.Visible, Is.False); Assert.That(renderer.forceRenderingOff, Is.True);
            root.SetActive(false);
            typeof(WorldAreaPresentation).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(root.GetComponent<WorldAreaPresentation>(), null);
            Assert.That(renderer.forceRenderingOff, Is.False, "Owned renderer override must be released.");
            root.SetActive(true);
            typeof(WorldAreaPresentation).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(root.GetComponent<WorldAreaPresentation>(), null);
            Assert.That(label.Visible, Is.False); Assert.That(renderer.forceRenderingOff, Is.True);
            Assert.That(state.TryGetObject(registration.ObjectId, out WorldObjectSnapshot restored) && restored.revealed, Is.False);
        }

        [Test] public void LiveConnectionFeedbackWorksWithoutExactConnectionMetadata()
        {
            GameObject root = New("area"); GameObject a = New("power"); a.transform.SetParent(root.transform);
            WorldObjectRegistration registration = a.AddComponent<WorldObjectRegistration>();
            registration.Configure("relic.stella.power", new Vector2Int(2, 2), Vector2Int.one, true, new[] { "ampere", "weave" });
            state.RegisterObject(registration.ObjectId, a, registration.Cell, registration.Size, true, registration.Tags);
            state.RegisterObject("other-endpoint", null, new Vector2Int(3, 2), Vector2Int.one, true, new[] { "weave" });
            state.RegisterPuzzleTargets(new[] { new PuzzleTargetDefinition { id = "connection", objectIds = new[] { registration.ObjectId }, requiredConnected = true } });
            WorldAreaPresentation.Install(root, new MapDefinition { id = "campus" }, grid, null, state); state.EndMapRebuild();
            Assert.That(a.GetComponent<WorldPresentationLabel>().Text, Does.Contain("Link: absent"));
            Assert.That(state.AddConnection(registration.ObjectId, "other-endpoint"), Is.True);
            Assert.That(a.GetComponent<WorldPresentationLabel>().Text, Does.Contain("Link: present"));
        }

        [Test] public void RepeatedInstallAddsNoCollisionsInteractionsOrDuplicateProps()
        {
            GameObject root = New("Sirius");
            var map = new MapDefinition { id = "B04.Ground", regions = new[] {
                new MapRegionDefinition { id = "D02", rect = new MapRectDefinition { x = 10, y = 2 } },
                new MapRegionDefinition { id = "D08", rect = new MapRectDefinition { x = 26, y = 7 } },
                new MapRegionDefinition { id = "D05", rect = new MapRectDefinition { x = 2, y = 7 } } } };
            WorldAreaPresentation.Install(root, map, grid, null, state);
            int additions = root.transform.childCount;
            root.SetActive(false); root.SetActive(true);
            WorldAreaPresentation.Install(root, map, grid, null, state);
            Assert.That(root.transform.childCount, Is.EqualTo(additions));
            Assert.That(root.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(root.GetComponentsInChildren<GridObstacle>(true), Is.Empty);
            Assert.That(root.GetComponentsInChildren<DynamicNpc>(true), Is.Empty);
            Assert.That(root.GetComponentsInChildren<GridPortal>(true), Is.Empty);
            Assert.That(grid.IsCellAvailable(new Vector2Int(26, 7)), Is.True);
            Assert.That(Array.Exists(root.GetComponentsInChildren<WorldPresentationLabel>(), l => l.Text.Contains("Reserved")), Is.True);
        }

        [Test] public void CataloguePlaceholderUsesExactAuthoredCellWithoutNpcActions()
        {
            GameObject root = New("campus");
            var catalog = new NpcDatabaseDefinition { npcs = new[] { new NpcDefinition { id = "katrina", mapId = "campus", x = 40, y = 68 } } };
            WorldAreaPresentation.Install(root, new MapDefinition { id = "campus" }, grid, catalog, state);
            WorldPresentationLabel label = root.GetComponentInChildren<WorldPresentationLabel>();
            Assert.That(label.Text, Does.Contain("Katrina").And.Contain("no interaction"));
            Assert.That(label.transform.position.x, Is.EqualTo(grid.CellToWorld(new Vector2Int(40, 68)).x));
            Assert.That(label.transform.position.z, Is.EqualTo(grid.CellToWorld(new Vector2Int(40, 68)).z));
            Assert.That(root.GetComponentsInChildren<DynamicNpc>(), Is.Empty);
            Assert.That(root.GetComponentsInChildren<Collider>(), Is.Empty);
        }
    }
}
