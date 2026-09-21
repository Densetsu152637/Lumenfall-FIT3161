using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Lumenfall.Spells;
using Lumenfall.GridMovement;
using Lumenfall.GameEvents;

namespace Lumenfall.Tests.GameEvents
{
    public sealed class SpellWorldStateTests
    {
        private readonly List<GameObject> created = new List<GameObject>();
        private WorldStateService state;
        private GridWorld grid;

        [SetUp]
        public void SetUp()
        {
            GameObject gridObject = new GameObject("test-grid"); created.Add(gridObject); grid = gridObject.AddComponent<GridWorld>(); grid.ConfigureBounds(12, 12);
            GameObject stateObject = new GameObject("test-spell-state"); created.Add(stateObject); state = stateObject.AddComponent<WorldStateService>(); state.BeginMapRebuild("test-map");
        }

        [TearDown]
        public void TearDown()
        {
            if (state != null) GameSaveManager.Instance.UnregisterProvider(state.SaveKey);
            for (int i = created.Count - 1; i >= 0; i--) if (created[i] != null) Object.DestroyImmediate(created[i]);
            created.Clear();
        }

        [Test]
        public void AmpereTogglesAndQueuesDurableEffectUntilAcknowledged()
        {
            GameObject target = Register("relic", new Vector2Int(2, 2), Vector2Int.one, null); SpellCaster caster = target.AddComponent<SpellCaster>(); state.EndMapRebuild();
            Assert.That(caster.Cast(SpellId.Ampere, "relic", Vector2Int.up), Is.True); Assert.That(state.TryGetObject("relic", out WorldObjectSnapshot on) && on.powered, Is.True);
            Assert.That(caster.Cast(SpellId.Ampere, "relic", Vector2Int.up), Is.True); Assert.That(state.TryGetObject("relic", out WorldObjectSnapshot off) && !off.powered, Is.True);
            Assert.That(state.AcknowledgeEffect("lightning:relic:off"), Is.True);
        }

        [Test]
        public void WeaveUsesDeclaredEndpointAndPersistsAttenuation()
        {
            Register("power", new Vector2Int(2, 2), Vector2Int.one, new[] { "weave-with:link" }); Register("link", new Vector2Int(4, 2), Vector2Int.one, new[] { "weave-with:power" }); state.EndMapRebuild();
            state.SetProperty("power", x => x.powered = true); SpellCaster caster = created[1].AddComponent<SpellCaster>(); Assert.That(caster.Cast(SpellId.Weave, "link", Vector2Int.right), Is.True);
            Assert.That(state.IsConnected("power", "link"), Is.True); Assert.That(state.GetConnectionStrength("power", "link"), Is.GreaterThan(0.2f).And.LessThan(1f));
            string save = state.CaptureStateJson(); state.ResetAllState(); state.BeginMapRebuild("test-map"); Register("power", new Vector2Int(2, 2), Vector2Int.one, new[] { "weave-with:link" }); Register("link", new Vector2Int(4, 2), Vector2Int.one, new[] { "weave-with:power" }); state.EndMapRebuild(); Assert.That(state.RestoreStateJson(save), Is.True); Assert.That(state.GetConnectionStrength("power", "link"), Is.GreaterThan(0.2f));
        }

        [Test]
        public void GlaciateStopsAtAuthoredGoalAndFootprintMovementRejectsBlocker()
        {
            GameObject ice = Register("ice", new Vector2Int(2, 4), Vector2Int.one, new[] { "slide-goal:5,4" }); SpellCaster caster = ice.AddComponent<SpellCaster>(); state.EndMapRebuild(); Assert.That(caster.Cast(SpellId.Glaciate, "ice", Vector2Int.right), Is.True); Assert.That(state.TryGetObject("ice", out WorldObjectSnapshot moved) && moved.cell == new Vector2Int(5, 4), Is.True);
            state.BeginMapRebuild("footprint"); GameObject block = Register("wide", new Vector2Int(2, 2), new Vector2Int(2, 1), null); GridObstacle obstacle = block.AddComponent<GridObstacle>(); obstacle.Configure(grid, new Vector2Int(2, 1)); GameObject blocker = Register("blocker", new Vector2Int(4, 2), Vector2Int.one, null); GridObstacle blockerObstacle = blocker.AddComponent<GridObstacle>(); blockerObstacle.Configure(grid, Vector2Int.one); state.EndMapRebuild(); Assert.That(state.TryMoveObject("wide", Vector2Int.right), Is.False);
        }

        private GameObject Register(string id, Vector2Int cell, Vector2Int size, string[] tags)
        { GameObject go = new GameObject(id); created.Add(go); go.transform.position = grid.CellToWorld(cell); state.RegisterObject(id, go, cell, size, true, tags); return go; }
    }
}
