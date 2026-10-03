using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Lumenfall.DataDriven;
using Lumenfall.Diagnostics;
using Lumenfall.GridMovement;
using Lumenfall.Spells;

namespace Lumenfall.GameEvents.Tests
{
    /// <summary>Requires the Unity test runner; the pure collector harness does not execute these hooks.</summary>
    public sealed class PerformanceDiagnosticsIntegrationTests
    {
        private readonly List<Object> created = new List<Object>();
        private WorldStateService state;
        private GridWorld grid;
        private DataDrivenWorldLoader loader;

        [SetUp]
        public void SetUp()
        {
            PerformanceDiagnostics.Enabled = false;
            PerformanceDiagnostics.Reset();
            bool ownsSaveManager = Object.FindAnyObjectByType<GameSaveManager>() == null;
            var saves = GameSaveManager.Instance;
            if (ownsSaveManager) created.Add(saves.gameObject);
            var gridObject = NewObject("diagnostic-grid");
            grid = gridObject.AddComponent<GridWorld>();
            grid.ConfigureBounds(8, 8);
            state = NewObject("diagnostic-state").AddComponent<WorldStateService>();
        }

        [TearDown]
        public void TearDown()
        {
            PerformanceDiagnostics.Enabled = false;
            PerformanceDiagnostics.Reset();
            if (loader != null) GameSaveManager.Instance.UnregisterProvider(loader.SaveKey);
            if (state != null) GameSaveManager.Instance.UnregisterProvider(state.SaveKey);
            for (int i = created.Count - 1; i >= 0; i--)
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            created.Clear();
        }

        [Test]
        public void EffectQueueRetainsDeduplicationAndDisabledHooksStayEmpty()
        {
            int queued = 0;
            state.EffectQueued += _ => queued++;
            state.QueueEffect("disabled-effect");
            Assert.That(PerformanceDiagnostics.GetSnapshot(PerformanceMetric.WorldEffectQueue).SampleCount, Is.Zero);
            PerformanceDiagnostics.Enabled = true;
            state.QueueEffect("enabled-effect");
            state.QueueEffect("enabled-effect");
            state.QueueEffect(" ");
            Assert.That(queued, Is.EqualTo(2));
            Assert.That(state.AcknowledgeEffect("enabled-effect"), Is.True);
            Assert.That(state.AcknowledgeEffect("enabled-effect"), Is.False);
            Assert.That(PerformanceDiagnostics.GetSnapshot(PerformanceMetric.WorldEffectQueue).SampleCount, Is.EqualTo(3));
        }

        [Test]
        public void RebuildAndObjectHooksPreserveStateAndCountRegisteredWork()
        {
            PerformanceDiagnostics.Enabled = true;
            int rebuilt = 0;
            state.MapRebuilt += _ => rebuilt++;
            state.BeginMapRebuild("diagnostic-map");
            state.RegisterObject("fixture-object", null, Vector2Int.one, Vector2Int.one, true, null);
            state.EndMapRebuild();
            Assert.That(state.IsReady, Is.True);
            Assert.That(rebuilt, Is.EqualTo(1));
            Assert.That(state.SetProperty("missing", s => s.powered = true), Is.False);
            Assert.That(state.SetProperty("fixture-object", s => s.powered = true), Is.True);
            Assert.That(state.TryGetObject("fixture-object", out WorldObjectSnapshot snapshot) && snapshot.powered, Is.True);
            Assert.That(state.TryMoveObject("missing", Vector2Int.right), Is.False);
            Assert.That(state.AddConnection("missing", "fixture-object"), Is.False);
            Assert.That(PerformanceDiagnostics.GetSnapshot(PerformanceMetric.WorldBeginRebuild).SampleCount, Is.EqualTo(1));
            Assert.That(PerformanceDiagnostics.GetSnapshot(PerformanceMetric.WorldEndRebuild).TotalWorkUnits, Is.EqualTo(1));
            Assert.That(PerformanceDiagnostics.GetSnapshot(PerformanceMetric.WorldObjectProperty).SampleCount, Is.EqualTo(2));
            Assert.That(PerformanceDiagnostics.GetSnapshot(PerformanceMetric.WorldObjectMove).SampleCount, Is.EqualTo(1));
            Assert.That(PerformanceDiagnostics.GetSnapshot(PerformanceMetric.WorldConnection).SampleCount, Is.EqualTo(1));
        }

        [Test]
        public void CachedAreaLoadCountsActualInstantiationAndActivationSeparately()
        {
            loader = NewObject("diagnostic-loader").AddComponent<DataDrivenWorldLoader>();
            var area = ScriptableObject.CreateInstance<WorldAreaAsset>();
            created.Add(area);
            area.definition = new MapDefinition { id = "diagnostic-area", width = 8, height = 8 };
            area.areaPrefab = NewObject("diagnostic-area-prototype");
            area.areaPrefab.SetActive(false);
            PerformanceDiagnostics.Enabled = true;
            loader.Load(area, null);
            loader.Load(area, null);
            Assert.That(loader.CurrentArea, Is.SameAs(area));
            Assert.That(loader.LoadedMap, Is.SameAs(area.definition));
            Assert.That(loader.transform.childCount, Is.EqualTo(1));
            Assert.That(loader.transform.GetChild(0).gameObject.activeSelf, Is.True);
            Assert.That(PerformanceDiagnostics.GetSnapshot(PerformanceMetric.WorldLoad).SampleCount, Is.EqualTo(2));
            Assert.That(PerformanceDiagnostics.GetSnapshot(PerformanceMetric.WorldInstantiate).SampleCount, Is.EqualTo(1));
            Assert.That(PerformanceDiagnostics.GetSnapshot(PerformanceMetric.WorldActivate).SampleCount, Is.EqualTo(2));
        }

        [Test]
        public void InvalidAreaLoadStillReturnsEarlyAndRecordsOnlyLoadAttempt()
        {
            loader = NewObject("diagnostic-loader").AddComponent<DataDrivenWorldLoader>();
            PerformanceDiagnostics.Enabled = true;
            LogAssert.Expect(LogType.Error, "Assign an authored area prefab to this World Area asset.");
            loader.Load(null, null);
            Assert.That(loader.CurrentArea, Is.Null);
            Assert.That(PerformanceDiagnostics.GetSnapshot(PerformanceMetric.WorldLoad).SampleCount, Is.EqualTo(1));
            Assert.That(PerformanceDiagnostics.GetSnapshot(PerformanceMetric.WorldInstantiate).SampleCount, Is.Zero);
            Assert.That(PerformanceDiagnostics.GetSnapshot(PerformanceMetric.WorldActivate).SampleCount, Is.Zero);
        }

        [Test]
        public void PathHookCountsSuccessAndFailureWithoutMovingActor()
        {
            var actor = NewObject("diagnostic-npc");
            actor.transform.SetParent(grid.transform, false);
            var mover = actor.AddComponent<GridMover>();
            var navigator = actor.AddComponent<GridNpcNavigator>();
            PerformanceDiagnostics.Enabled = true;
            Assert.That(navigator.SetDestination(new Vector2Int(3, 3)), Is.True);
            Assert.That(navigator.QueuedStepCount, Is.GreaterThan(0));
            Assert.That(mover.CurrentCell, Is.EqualTo(Vector2Int.zero));
            Assert.That(navigator.SetDestination(new Vector2Int(99, 99)), Is.False);
            Assert.That(navigator.HasDestination, Is.False);
            Assert.That(PerformanceDiagnostics.GetSnapshot(PerformanceMetric.NpcBuildPath).SampleCount, Is.EqualTo(2));
        }

        private GameObject NewObject(string name)
        {
            var item = new GameObject(name);
            created.Add(item);
            return item;
        }
    }
}